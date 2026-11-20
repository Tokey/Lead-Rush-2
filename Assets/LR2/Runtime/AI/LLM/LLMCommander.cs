using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using LR2.Combat;
using LR2.Core;
using LR2.Game;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace LR2.AI.LLM
{
    /// <summary>
    /// B127: which teams get an LLM commander, which Groq models, how often. Command line wins over the saved setting:
    /// <c>-lr2LlmTeams Alpha,Bravo</c>, <c>-lr2LlmModel name</c>, <c>-lr2LlmInterval seconds</c>.
    /// </summary>
    public static class LLMCommanderConfig
    {
        public const string PrefTeams = "LR2.LLM.Teams";
        public const string PrefModel = "LR2.LLM.Model";
        /// <summary>Tried in order; a model Groq rejects (unknown or retired) falls through to the next.</summary>
        public static readonly string[] DefaultModels = { "llama-3.3-70b-versatile", "openai/gpt-oss-20b", "llama-3.1-8b-instant" };

        private static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return null;
        }

        public static List<TeamId> Teams()
        {
            string raw = Arg("-lr2LlmTeams") ?? PlayerPrefs.GetString(PrefTeams, "");
            var list = new List<TeamId>();
            foreach (var part in raw.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                if (Enum.TryParse(part.Trim(), true, out TeamId t) && t != TeamId.None && t != TeamId.Hostile && !list.Contains(t)) list.Add(t);
            return list;
        }

        public static string[] Models()
        {
            string m = Arg("-lr2LlmModel") ?? PlayerPrefs.GetString(PrefModel, "");
            if (string.IsNullOrWhiteSpace(m)) return DefaultModels;
            var list = new List<string> { m.Trim() };
            foreach (var d in DefaultModels) if (d != m.Trim()) list.Add(d);
            return list.ToArray();
        }

        public static float Interval()
        {
            return float.TryParse(Arg("-lr2LlmInterval"), NumberStyles.Float, CultureInfo.InvariantCulture, out float s) ? Mathf.Clamp(s, 4f, 60f) : 12f;
        }
    }

    /// <summary>B127: creates the configured commanders once a match is playing. Lives only in match scenes.</summary>
    public class LLMCommanderHost : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            TryCreate();
        }

        private static void OnSceneLoaded(Scene s, LoadSceneMode m) => TryCreate();

        private static void TryCreate()
        {
            if (LLMCommanderConfig.Teams().Count == 0) return;
            if (FindAnyObjectByType<LLMCommanderHost>() != null) return;
            new GameObject("LR2 LLM Commanders").AddComponent<LLMCommanderHost>();
        }

        private IEnumerator Start()
        {
            float until = Time.realtimeSinceStartup + 20f;
            while (MatchManager.Instance == null && Time.realtimeSinceStartup < until) yield return null;
            if (MatchManager.Instance == null) { Destroy(gameObject); yield break; }   // menu or a non-match scene
            while (MatchManager.Instance != null && MatchManager.Instance.State != MatchState.Playing) yield return null;
            if (MatchManager.Instance == null) { Destroy(gameObject); yield break; }
            yield return new WaitForSeconds(1f);
            foreach (var team in LLMCommanderConfig.Teams())
            {
                bool any = false;
                foreach (var c in Combatant.All) if (c != null && c.Team == team && c.GetComponent<BotController>() != null) { any = true; break; }
                if (!any) { Debug.Log($"[LR2] LLM commander: no bots on {team}, skipped"); continue; }
                gameObject.AddComponent<LLMCommander>().Init(team);
            }
        }
    }

    /// <summary>
    /// B127: an LLM squad commander for one team. Every <see cref="Interval"/> s it sends Groq a compact JSON picture of what
    /// its bots know (no information they could not have: only enemies a bot has seen, with how long ago), and turns the reply into
    /// <see cref="BotDirective"/>s through the same <see cref="BotController.IssueDirective"/> path the chat orders use. The bots
    /// still aim, shoot, take cover and reload themselves; orders last 1.6 intervals, so a slow or failed reply only means the
    /// bots fall back to their own brains. Every exchange is logged (never the key) for the comparison with non-LLM bots.
    /// </summary>
    public class LLMCommander : MonoBehaviour
    {
        public static readonly List<LLMCommander> All = new List<LLMCommander>();

        public TeamId Team { get; private set; }
        public float Interval { get; private set; } = 10f;
        public string Model => _models != null && _modelIndex < _models.Length ? _models[_modelIndex] : "-";
        public int Decisions { get; private set; }
        public int Failures { get; private set; }
        public int OrdersIssued { get; private set; }
        public int OrdersRejected { get; private set; }
        public float TotalLatency { get; private set; }
        public int PromptTokens { get; private set; }
        public int CompletionTokens { get; private set; }
        public string LastPlan { get; private set; } = "";
        public string LastError { get; private set; } = "";

        private string[] _models;
        private int _modelIndex;
        private readonly Dictionary<Combatant, string> _ids = new Dictionary<Combatant, string>();
        private readonly Dictionary<string, Combatant> _byId = new Dictionary<string, Combatant>();
        private int _allyN, _enemyN;
        private Vector2 _boundsMin, _boundsMax;
        private string _logPath;
        private bool _toldNoKey;
        /// <summary>What the team knew at the last decision: enemy -> last known position. Orders never use more than this.</summary>
        private readonly Dictionary<Combatant, Vector3> _known = new Dictionary<Combatant, Vector3>();

        public void Init(TeamId team)
        {
            Team = team;
            Interval = LLMCommanderConfig.Interval();
            _models = LLMCommanderConfig.Models();
            ComputeBounds();
            string dir = Application.isEditor ? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "LR2_LLM"))
                                              : Path.Combine(Application.persistentDataPath, "LLM");
            try { Directory.CreateDirectory(dir); } catch (Exception) { }
            _logPath = Path.Combine(dir, $"llm_{team}_{DateTime.Now:yyyyMMdd_HHmmss}.jsonl");
            All.Add(this);
            Debug.Log($"[LR2] LLM commander for {team}: models {string.Join(", ", _models)}, every {Interval:0} s, key from {(GroqClient.HasKey ? GroqClient.KeySource : "nowhere")}");
            StartCoroutine(Loop());
        }

        private void OnDestroy() => All.Remove(this);

        private IEnumerator Loop()
        {
            yield return new WaitForSeconds(UnityEngine.Random.Range(0.2f, 1.5f)); // two commanders do not fire together
            while (MatchManager.Instance != null && MatchManager.Instance.State == MatchState.Playing)
            {
                if (!GroqClient.HasKey)
                {
                    LastError = "no Groq API key";
                    if (!_toldNoKey)
                    {
                        _toldNoKey = true;
                        Debug.LogWarning($"[LR2] LLM commander {Team}: no Groq API key. Set GROQ_API_KEY or put the key in {GroqClient.DefaultKeyPath}");
                        Say($"No LLM key: {TeamUtil.DisplayName(Team)} bots fight on their own.");
                    }
                    yield return new WaitForSeconds(5f);
                    GroqClient.ReloadKey();
                    continue;
                }

                string state = BuildState();
                GroqClient.Result result = null;
                float sent = Time.time;
                yield return GroqClient.Chat(Model, SystemPrompt(), state, 700, 0.4f, true, r => result = r);
                Decisions++;
                if (result != null && result.ok)
                {
                    TotalLatency += result.seconds;
                    PromptTokens += result.promptTokens;
                    CompletionTokens += result.completionTokens;
                    var (issued, rejected, plan, note) = Apply(result.content);
                    OrdersIssued += issued;
                    OrdersRejected += rejected;
                    if (!string.IsNullOrWhiteSpace(plan)) { LastPlan = plan; Say(plan); }
                    LastError = note ?? "";
                    Log(sent, state, result, issued, rejected, note);
                }
                else
                {
                    Failures++;
                    LastError = result != null ? result.error : "no result";
                    Log(sent, state, result, 0, 0, LastError);
                    if (result != null && result.modelRejected && _modelIndex < _models.Length - 1)
                    {
                        Debug.LogWarning($"[LR2] LLM commander {Team}: model {Model} rejected ({result.error}); trying {_models[_modelIndex + 1]}");
                        _modelIndex++;
                        continue;
                    }
                }
                float wait = Interval - (Time.time - sent);
                if (wait > 0f) yield return new WaitForSeconds(wait);
            }
        }

        // ------------------------------------------------------------------ the prompt
        private string SystemPrompt()
        {
            return
                $"You command team {TeamUtil.DisplayName(Team)} in LeadRush 2, a fast close-quarters shooter. Every {Interval:0} s you get the battlefield " +
                "as JSON and give your bots orders for the next stretch. Your bots aim, shoot, take cover and reload by themselves; you decide where " +
                "they go and whom they fight. Orders: \"hold\" (stay and watch), \"move\" (go to x,z), \"attack\" (hunt enemy id), \"flank\" " +
                "(reach x,z from the side, then engage), \"retreat\" (fall back, to x,z if given), \"regroup\" (join ally id), \"free\" (bot decides). " +
                "Coordinates are metres on the map's x,z plane inside the bounds given. Enemies listed are only the ones your bots have seen; " +
                "seenAgo is seconds since. Good commanders keep bots in pairs, attack where they outnumber the enemy, flank fights already going, " +
                "pull back badly hurt bots, and in capture the flag hold and contest the zone. Reply with one JSON object only: " +
                "{\"plan\":\"one short sentence\",\"orders\":[{\"bot\":\"A1\",\"order\":\"flank\",\"x\":12,\"z\":-30,\"target\":\"E2\"}]}. " +
                "At most one order per bot; leave out bots that should keep doing what they do.";
        }

        private string BuildState()
        {
            var mm = MatchManager.Instance;
            var sb = new StringBuilder(1024);
            var inv = CultureInfo.InvariantCulture;
            int us = mm != null ? mm.GetTeamScore(Team) : 0, them = 0;
            if (mm != null)
                foreach (TeamId t in Enum.GetValues(typeof(TeamId)))
                    if (t != Team && t != TeamId.None && t != TeamId.Hostile) them = Mathf.Max(them, mm.GetTeamScore(t));
            sb.Append("{\"map\":\"").Append(SceneManager.GetActiveScene().name.Replace("LR2_", "")).Append('"');
            sb.Append(",\"mode\":\"").Append(mm != null && mm.Settings != null ? mm.Settings.mode.ToString() : "?").Append('"');
            sb.Append(",\"score\":{\"us\":").Append(us).Append(",\"bestEnemy\":").Append(them)
              .Append(",\"limit\":").Append(mm != null ? mm.ScoreLimit : 0).Append('}');
            sb.Append(",\"timeLeft\":").Append(Mathf.RoundToInt(mm != null ? mm.TimeRemaining : 0f));
            sb.Append(",\"bounds\":{\"minX\":").Append(Mathf.RoundToInt(_boundsMin.x)).Append(",\"maxX\":").Append(Mathf.RoundToInt(_boundsMax.x))
              .Append(",\"minZ\":").Append(Mathf.RoundToInt(_boundsMin.y)).Append(",\"maxZ\":").Append(Mathf.RoundToInt(_boundsMax.y)).Append('}');
            var flag = FlagZoneController.Instance;
            if (flag != null && mm != null && mm.Settings != null && mm.Settings.IsFlagMode)
            {
                Vector3 f = flag.ActiveFlagPosition;
                sb.Append(",\"flagZone\":{\"x\":").Append(Mathf.RoundToInt(f.x)).Append(",\"z\":").Append(Mathf.RoundToInt(f.z)).Append('}');
            }
            if (!string.IsNullOrEmpty(LastPlan)) sb.Append(",\"yourLastPlan\":\"").Append(Clean(LastPlan)).Append('"');

            // squad and what it knows
            var known = new Dictionary<Combatant, (Vector3 pos, float ago, string by)>();
            sb.Append(",\"squad\":[");
            bool first = true;
            int dead = 0;
            foreach (var c in Combatant.All)
            {
                if (c == null || c.Team != Team) continue;
                if (!c.IsAlive) { dead++; continue; }
                var bot = c.GetComponent<BotController>();
                string id = IdOf(c, true);
                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"id\":\"").Append(id).Append('"');
                if (bot == null) sb.Append(",\"player\":true");
                sb.Append(",\"x\":").Append(Mathf.RoundToInt(c.Position.x)).Append(",\"z\":").Append(Mathf.RoundToInt(c.Position.z));
                if (c.Health != null)
                    sb.Append(",\"hp\":").Append(Mathf.RoundToInt(c.Health.Current)).Append(",\"armor\":").Append(Mathf.RoundToInt(c.Health.Armor));
                if (bot != null)
                {
                    var bb = bot.Blackboard;
                    sb.Append(",\"ammo\":").Append(Mathf.RoundToInt(bb.ammo01 * 100f)).Append(",\"doing\":\"").Append(bot.CurrentGoalName).Append('"');
                    if (bb.hasDirective && Time.time < bb.directiveUntil) sb.Append(",\"order\":\"").Append(bb.directive.order).Append('"');
                    var tgt = bb.target;
                    if (tgt != null && tgt.IsAlive && TeamUtil.AreHostile(c, tgt))
                    {
                        float ago = bb.targetVisible ? 0f : bb.TimeSinceSeen;
                        if (ago < 20f)
                        {
                            Vector3 p = bb.targetVisible ? tgt.Position : bb.targetLastKnown;
                            if (!known.TryGetValue(tgt, out var k) || ago < k.ago) known[tgt] = (p, ago, id);
                            if (bb.targetVisible) sb.Append(",\"sees\":\"").Append(IdOf(tgt, false)).Append("\",\"dist\":").Append(Mathf.RoundToInt(bb.targetDistance));
                        }
                    }
                }
                sb.Append('}');
            }
            _known.Clear();
            foreach (var kv in known) _known[kv.Key] = kv.Value.pos;
            sb.Append("],\"deadAllies\":").Append(dead);
            sb.Append(",\"enemies\":[");
            first = true;
            foreach (var kv in known)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"id\":\"").Append(IdOf(kv.Key, false)).Append("\",\"x\":").Append(Mathf.RoundToInt(kv.Value.pos.x))
                  .Append(",\"z\":").Append(Mathf.RoundToInt(kv.Value.pos.z)).Append(",\"seenAgo\":")
                  .Append(kv.Value.ago.ToString("0.0", inv)).Append(",\"by\":\"").Append(kv.Value.by).Append("\"}");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        // ------------------------------------------------------------------ the reply
#pragma warning disable 0649 // filled by JsonUtility
        [Serializable] private class Reply { public string plan; public OrderDto[] orders; }
        [Serializable] private class OrderDto { public string bot; public string order; public string target; public float x; public float z; }
#pragma warning restore 0649

        private (int issued, int rejected, string plan, string note) Apply(string content)
        {
            int a = content.IndexOf('{'), b = content.LastIndexOf('}');
            if (a < 0 || b <= a) return (0, 0, null, "reply had no JSON object");
            Reply reply;
            try { reply = JsonUtility.FromJson<Reply>(content.Substring(a, b - a + 1)); }
            catch (Exception e) { return (0, 0, null, "unreadable JSON: " + e.Message); }
            if (reply == null) return (0, 0, null, "empty JSON");
            int issued = 0, rejected = 0;
            var reasons = new List<string>();
            if (reply.orders != null)
                foreach (var o in reply.orders)
                {
                    string why = Issue(o);
                    if (why == null) issued++;
                    else { rejected++; if (reasons.Count < 4) reasons.Add($"{o?.bot}:{why}"); }
                }
            return (issued, rejected, reply.plan, reasons.Count > 0 ? string.Join("; ", reasons) : null);
        }

        /// <summary>Turns one order into a directive. Returns null when issued, or why it was rejected.</summary>
        private string Issue(OrderDto o)
        {
            if (o == null || string.IsNullOrEmpty(o.bot) || string.IsNullOrEmpty(o.order)) return "incomplete";
            if (!_byId.TryGetValue(o.bot.Trim(), out var c) || c == null || !c.IsAlive || c.Team != Team) return "unknown bot";
            var bot = c.GetComponent<BotController>();
            if (bot == null) return "not a bot";
            bool hasXZ = !(Mathf.Approximately(o.x, 0f) && Mathf.Approximately(o.z, 0f));
            Combatant target = null;
            if (!string.IsNullOrEmpty(o.target)) _byId.TryGetValue(o.target.Trim(), out target);
            float dur = Interval * 1.6f;
            string reason = "LLM: " + (o.order ?? "");
            switch (o.order.Trim().ToLowerInvariant())
            {
                case "hold":
                {
                    Vector3 p = c.Position;
                    if (hasXZ && Sample(new Vector3(o.x, c.Position.y, o.z), out var h) && Vector3.Distance(h, c.Position) < 15f) p = h;
                    bot.IssueDirective(new BotDirective { order = BotOrder.HoldPosition, position = p, duration = dur, rationale = reason });
                    return null;
                }
                case "move": case "push": case "advance":
                {
                    if (!hasXZ) return "move without x,z";
                    if (!Sample(new Vector3(o.x, c.Position.y, o.z), out var p)) return "x,z off the map";
                    bot.IssueDirective(new BotDirective { order = BotOrder.MoveTo, position = p, duration = dur, rationale = reason });
                    return null;
                }
                case "attack": case "hunt": case "engage":
                {
                    // fair play: head for where the team last saw that enemy, not its live position (BotOrder.Attack tracks the
                    // target's real position, which no player would know); the bot's own eyes take over once it is in view
                    if (target != null && target.IsAlive && TeamUtil.AreHostile(c, target) && _known.TryGetValue(target, out var seenAt)
                        && Sample(seenAt, out var at))
                    {
                        bot.IssueDirective(new BotDirective { order = BotOrder.MoveTo, position = at, duration = dur, rationale = reason + " " + o.target });
                        return null;
                    }
                    if (hasXZ && Sample(new Vector3(o.x, c.Position.y, o.z), out var p))
                    {
                        bot.IssueDirective(new BotDirective { order = BotOrder.MoveTo, position = p, duration = dur, rationale = reason });
                        return null;
                    }
                    return "attack without a known enemy";
                }
                case "flank":
                {
                    Vector3 goal;
                    if (hasXZ && Sample(new Vector3(o.x, c.Position.y, o.z), out var p)) goal = p;
                    else if (target != null && target.IsAlive && _known.TryGetValue(target, out var seen))
                    {
                        Vector3 to = seen - c.Position; to.y = 0f;
                        Vector3 side = Vector3.Cross(Vector3.up, to.sqrMagnitude > 0.01f ? to.normalized : c.transform.forward);
                        if (!Sample(seen + side * 11f - to.normalized * 4f, out goal) && !Sample(seen - side * 11f - to.normalized * 4f, out goal))
                            return "no flank route";
                    }
                    else return "flank without x,z or target";
                    bot.IssueDirective(new BotDirective { order = BotOrder.Flank, position = goal, duration = dur, rationale = reason });
                    return null;
                }
                case "retreat": case "fall back": case "fallback":
                {
                    Vector3 p = Vector3.zero;
                    if (hasXZ && Sample(new Vector3(o.x, c.Position.y, o.z), out var h)) p = h;
                    bot.IssueDirective(new BotDirective { order = BotOrder.Retreat, position = p, duration = dur, rationale = reason });
                    return null;
                }
                case "regroup": case "follow":
                {
                    if (target == null || !target.IsAlive || target.Team != Team) return "regroup without an ally";
                    Vector3 off = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f) * Vector3.forward * 2.2f;
                    if (!Sample(target.Position + off, out var p)) p = target.Position;
                    bot.IssueDirective(new BotDirective { order = BotOrder.MoveTo, position = p, duration = Mathf.Min(dur, 8f), rationale = reason });
                    return null;
                }
                case "free": case "none":
                    bot.IssueDirective(new BotDirective { order = BotOrder.FreeRoam, duration = 0.1f, rationale = reason });
                    return null;
                default:
                    return "unknown order " + o.order;
            }
        }

        // ------------------------------------------------------------------ helpers
        private string IdOf(Combatant c, bool ally)
        {
            if (_ids.TryGetValue(c, out var id)) return id;
            id = ally ? (c.GetComponent<BotController>() == null ? "P" + (++_allyN) : "A" + (++_allyN)) : "E" + (++_enemyN);
            _ids[c] = id;
            _byId[id] = c;
            return id;
        }

        private static bool Sample(Vector3 p, out Vector3 onMesh)
        {
            if (NavMesh.SamplePosition(p, out var hit, 8f, NavMesh.AllAreas)) { onMesh = hit.position; return true; }
            onMesh = p;
            return false;
        }

        private void ComputeBounds()
        {
            var tri = NavMesh.CalculateTriangulation();
            if (tri.vertices == null || tri.vertices.Length == 0) { _boundsMin = new Vector2(-50f, -50f); _boundsMax = new Vector2(50f, 50f); return; }
            Vector2 mn = new Vector2(float.MaxValue, float.MaxValue), mx = new Vector2(float.MinValue, float.MinValue);
            foreach (var v in tri.vertices) { mn = Vector2.Min(mn, new Vector2(v.x, v.z)); mx = Vector2.Max(mx, new Vector2(v.x, v.z)); }
            _boundsMin = mn; _boundsMax = mx;
        }

        private void Say(string text)
        {
            var chat = LR2.UI.TacticalChatSystem.Instance;
            if (chat != null) chat.PostCommander($"{TeamUtil.DisplayName(Team).ToUpperInvariant()} CMD", TeamUtil.TeamColor(Team), Clean(text));
        }

        private static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace('"', '\'').Replace('\\', '/').Replace('\n', ' ').Replace('\r', ' ');
            return s.Length > 140 ? s.Substring(0, 140) : s;
        }

        private void Log(float sentAt, string state, GroqClient.Result r, int issued, int rejected, string note)
        {
            if (string.IsNullOrEmpty(_logPath)) return;
            try
            {
                var inv = CultureInfo.InvariantCulture;
                string reply = r != null && r.content != null ? Clean(r.content.Length > 1500 ? r.content.Substring(0, 1500) : r.content) : "";
                string line = "{\"t\":" + sentAt.ToString("0.0", inv) + ",\"team\":\"" + Team + "\",\"model\":\"" + Model +
                              "\",\"status\":" + (r != null ? r.status : 0) + ",\"ok\":" + (r != null && r.ok ? "true" : "false") +
                              ",\"seconds\":" + (r != null ? r.seconds.ToString("0.00", inv) : "0") +
                              ",\"promptTokens\":" + (r != null ? r.promptTokens : 0) + ",\"completionTokens\":" + (r != null ? r.completionTokens : 0) +
                              ",\"remainingRequests\":\"" + (r?.remainingRequests ?? "") + "\",\"remainingTokens\":\"" + (r?.remainingTokens ?? "") +
                              "\",\"issued\":" + issued + ",\"rejected\":" + rejected + ",\"note\":\"" + Clean(note) +
                              "\",\"reply\":\"" + reply + "\",\"state\":" + state + "}";
                File.AppendAllText(_logPath, line + "\n");
            }
            catch (Exception) { }
        }
    }
}
