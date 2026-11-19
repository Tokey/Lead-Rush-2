using System;
using System.Collections;
using System.Collections.Generic;
using LR2.Combat;
using LR2.Core;
using LR2.Game;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LR2.UI
{
    /// <summary>
    /// Command &amp; Conquer style skirmish lobby behind the main menu's PLAY (user 11-19: "just have play, then in that have all
    /// the settings ... 3 way, 4 way, so i can select how many bots and which team they can be in ... a AI vs AI mode ...
    /// make it scrllable ... maybe max 20"). Left: the roster (you or a spectator seat, then up to 20 bots, each with its own
    /// difficulty and team). Right: mode, map, limits, weapon, OPTIONS and RULES (the full per-mode rules sub-menu). Built in
    /// code like the tactical chat, so the builder needs no prefab changes.
    /// </summary>
    public class SkirmishLobby : MonoBehaviour
    {
        public CanvasGroup Group { get; private set; }
        public Action OnBack, OnOptions, OnRules;
        public Action<GameModeType> OnStart;
        /// <summary>Mode changed: the menu background cuts to a matching shot.</summary>
        public Action<GameModeType> OnModeChanged;

        private UITheme _theme;
        private RectTransform _content;
        private ScrollRect _scroll;
        private TMP_Text _countLabel, _summary;
        private Image _preview;
        private Button _startButton;
        private readonly List<Action> _refresh = new List<Action>();
        private ModeRuleConfig _rules;
        private Func<GameModeType, MatchSettings> _templateFor;
        private Coroutine _intro;
        private bool _buildingRows; // row widgets are rebuilt, so they register no refresher

        private Color Accent => _theme != null ? _theme.accent : new Color(1f, 0.71f, 0.28f);
        private Color Text => _theme != null ? _theme.text : new Color(1f, 1f, 1f, 0.92f);
        private Color Dim => _theme != null ? _theme.textDim : new Color(1f, 1f, 1f, 0.55f);
        private static readonly Color Panel = new Color(0.035f, 0.045f, 0.055f, 0.80f);
        private static readonly Color RowColor = new Color(1f, 1f, 1f, 0.045f);
        private static readonly Color[] DiffColors =
        {
            new Color(0.62f, 0.85f, 0.55f), new Color(0.80f, 0.86f, 0.92f), new Color(1f, 0.70f, 0.32f),
            new Color(1f, 0.38f, 0.32f), new Color(0.72f, 0.55f, 1f)
        };

        public static SkirmishLobby Create(Transform parent, Func<GameModeType, MatchSettings> templateFor)
        {
            var go = new GameObject("SkirmishLobby", typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(parent, false);
            var lobby = go.AddComponent<SkirmishLobby>();
            lobby._templateFor = templateFor;
            lobby.Build();
            return lobby;
        }

        // ------------------------------------------------------------------ build
        private void Build()
        {
            _theme = Resources.Load<UITheme>("LR2/UITheme");
            SkirmishSetup.Load();
            LoadRules();
            var root = (RectTransform)transform;
            Stretch(root);
            Group = GetComponent<CanvasGroup>();

            var shade = Img(root, "Shade", new Color(0.015f, 0.02f, 0.025f, 0.84f));
            Anchor(shade.rectTransform, 0f, 0f, 0.70f, 1f);
            var edge = Img(root, "ShadeEdge", new Color(0.015f, 0.02f, 0.025f, 0.45f));
            Anchor(edge.rectTransform, 0.70f, 0f, 0.74f, 1f);

            var title = Label(root, "SKIRMISH", 54, Text, true);
            Anchor(title.rectTransform, 0.035f, 0.885f, 0.5f, 0.955f);
            var sub = Label(root, "ANY MATCH YOU WANT  ·  UP TO 20 BOTS  ·  2, 3 OR 4 TEAMS  ·  AI VS AI TEST BENCH", 17, Dim);
            Anchor(sub.rectTransform, 0.036f, 0.855f, 0.69f, 0.89f);

            // presets
            var presets = Rect(root, "Presets");
            Anchor(presets.rectTransform(), 0.035f, 0.80f, 0.69f, 0.845f);
            var ph = presets.AddComponent<HorizontalLayoutGroup>();
            ph.spacing = 8f; ph.childForceExpandWidth = true; ph.childControlWidth = true; ph.childControlHeight = true;
            foreach (string p in SkirmishSetup.Presets)
            {
                string preset = p;
                Btn(presets.transform, p, 16, () => { SkirmishSetup.ApplyPreset(preset); LoadRules(); Rebuild(); OnModeChanged?.Invoke(SkirmishSetup.Mode); },
                    p == "AI VS AI" ? new Color(0.45f, 0.30f, 0.85f, 0.55f) : new Color(1f, 1f, 1f, 0.07f));
            }

            BuildRoster(root);
            BuildMatchColumn(root);

            var back = Btn(root, "BACK", 20, () => OnBack?.Invoke(), new Color(1f, 1f, 1f, 0.07f));
            Anchor((RectTransform)back.transform, 0.035f, 0.045f, 0.13f, 0.10f);
            _startButton = Btn(root, "START", 28, Start_, new Color(Accent.r, Accent.g, Accent.b, 0.85f));
            Anchor((RectTransform)_startButton.transform, 0.50f, 0.04f, 0.69f, 0.11f);
            _startButton.GetComponentInChildren<TMP_Text>().color = new Color(0.05f, 0.05f, 0.05f);
            _summary = Label(root, "", 17, Dim);
            _summary.alignment = TextAlignmentOptions.MidlineRight;
            Anchor(_summary.rectTransform, 0.15f, 0.045f, 0.49f, 0.10f);

            Rebuild();
        }

        private void BuildRoster(RectTransform root)
        {
            var panel = Img(root, "Roster", Panel);
            Anchor(panel.rectTransform, 0.035f, 0.13f, 0.455f, 0.785f);
            var pr = panel.rectTransform;

            _countLabel = Label(pr, "", 20, Text, true);
            Anchor(_countLabel.rectTransform, 0.03f, 0.915f, 0.40f, 0.985f);
            var all = Label(pr, "ALL BOTS", 15, Dim);
            Anchor(all.rectTransform, 0.42f, 0.915f, 0.56f, 0.985f);
            Stepper(pr, 0.56f, 0.915f, 0.97f, 0.985f, () => AllDifficultyLabel(), d =>
            {
                int cur = SkirmishSetup.Bots.Count > 0 ? SkirmishSetup.Bots[0].difficulty : 1;
                int next = (cur + d + SkirmishSetup.DifficultyNames.Length) % SkirmishSetup.DifficultyNames.Length;
                foreach (var b in SkirmishSetup.Bots) b.difficulty = next;
                Rebuild();
            });

            foreach (var (text, x) in new[] { ("SLOT", 0.045f), ("DIFFICULTY", 0.37f), ("TEAM", 0.67f) })
            {
                var head = Label(pr, text, 13, Dim);
                Anchor(head.rectTransform, x, 0.87f, x + 0.3f, 0.91f);
            }

            var view = Rect(pr, "Viewport");
            var vr = view.rectTransform();
            Anchor(vr, 0.015f, 0.10f, 0.975f, 0.87f);
            view.AddComponent<RectMask2D>();
            var content = Rect(vr, "Content");
            _content = content.rectTransform();
            _content.anchorMin = new Vector2(0f, 1f); _content.anchorMax = new Vector2(1f, 1f); _content.pivot = new Vector2(0.5f, 1f);
            _content.offsetMin = _content.offsetMax = Vector2.zero;
            var vl = content.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 4f; vl.childControlHeight = true; vl.childControlWidth = true; vl.childForceExpandHeight = false;
            vl.padding = new RectOffset(4, 10, 4, 4);
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _scroll = panel.gameObject.AddComponent<ScrollRect>();
            _scroll.viewport = vr; _scroll.content = _content; _scroll.horizontal = false;
            _scroll.movementType = ScrollRect.MovementType.Clamped; _scroll.scrollSensitivity = 36f;

            var sbGo = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            sbGo.transform.SetParent(pr, false);
            sbGo.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.05f);
            Anchor((RectTransform)sbGo.transform, 0.982f, 0.10f, 0.992f, 0.87f);
            var area = Rect(sbGo.transform, "Sliding Area");
            Stretch(area.rectTransform());
            var handle = Img(area.rectTransform(), "Handle", new Color(Accent.r, Accent.g, Accent.b, 0.7f));
            Stretch(handle.rectTransform);
            var sb = sbGo.GetComponent<Scrollbar>();
            sb.handleRect = handle.rectTransform; sb.targetGraphic = handle; sb.direction = Scrollbar.Direction.BottomToTop;
            _scroll.verticalScrollbar = sb;
            _scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            var add = Btn(pr, "+  ADD BOT", 18, AddBot, new Color(1f, 1f, 1f, 0.08f));
            Anchor((RectTransform)add.transform, 0.03f, 0.02f, 0.40f, 0.085f);
            var balance = Btn(pr, "BALANCE TEAMS", 16, BalanceTeams, new Color(1f, 1f, 1f, 0.06f));
            Anchor((RectTransform)balance.transform, 0.42f, 0.02f, 0.68f, 0.085f);
            var clear = Btn(pr, "CLEAR", 16, () => { SkirmishSetup.Bots.Clear(); Rebuild(); }, new Color(1f, 1f, 1f, 0.06f));
            Anchor((RectTransform)clear.transform, 0.70f, 0.02f, 0.97f, 0.085f);
        }

        private void BuildMatchColumn(RectTransform root)
        {
            var panel = Img(root, "Match", Panel);
            Anchor(panel.rectTransform, 0.47f, 0.13f, 0.69f, 0.785f);
            var pr = panel.rectTransform;
            var t = Label(pr, "MATCH", 20, Text, true);
            Anchor(t.rectTransform, 0.06f, 0.915f, 0.9f, 0.985f);

            float y = 0.905f;
            void Row(string label, Func<string> value, Action<int> step)
            {
                y -= 0.085f;
                var l = Label(pr, label, 14, Dim);
                Anchor(l.rectTransform, 0.06f, y + 0.045f, 0.94f, y + 0.085f);
                Stepper(pr, 0.05f, y, 0.95f, y + 0.05f, value, step);
            }
            Row("MODE", () => SkirmishSetup.ModeNames[ModeIndex()], d =>
            {
                int i = (ModeIndex() + d + SkirmishSetup.Modes.Length) % SkirmishSetup.Modes.Length;
                SkirmishSetup.Mode = SkirmishSetup.Modes[i];
                LoadRules(); Rebuild(); OnModeChanged?.Invoke(SkirmishSetup.Mode);
            });
            Row("MAP", () => LR2Session.MapDisplayNames[LR2Session.SelectedMapIndex].ToUpperInvariant(), d =>
            {
                int n = LR2Session.MapDisplayNames.Length;
                LR2Session.SelectedMapIndex = (LR2Session.SelectedMapIndex + d + n) % n;
                Rebuild();
            });
            y -= 0.20f;
            _preview = Img(pr, "MapPreview", Color.white);
            _preview.preserveAspect = false;
            Anchor(_preview.rectTransform, 0.06f, y + 0.005f, 0.94f, y + 0.195f);
            Row("SCORE LIMIT", () => $"{_rules.scoreLimit}", d => { _rules.scoreLimit += d * (_rules.scoreLimit < 10 ? 1 : 5); SaveRules(); });
            Row("TIME LIMIT", () => _rules.timeLimitSeconds <= 0f ? "NONE" : $"{Mathf.RoundToInt(_rules.timeLimitSeconds / 60f)} MIN",
                d => { _rules.timeLimitSeconds = Mathf.Max(60f, _rules.timeLimitSeconds + d * 60f); SaveRules(); });
            Row("RESPAWN", () => $"{_rules.respawnDelay:0.0} S", d => { _rules.respawnDelay = Mathf.Max(0.5f, _rules.respawnDelay + d * 0.5f); SaveRules(); });
            Row("SEEKER DRONES", () => _rules.spawnOrbs ? "ON" : "OFF", d => { _rules.spawnOrbs = !_rules.spawnOrbs; SaveRules(); });
            Row("YOUR WEAPON", () => LR2Session.PrimaryWeaponNames[LR2Session.SelectedPrimaryWeapon], d =>
            {
                int n = LR2Session.PrimaryWeaponNames.Length;
                LR2Session.SelectedPrimaryWeapon = (LR2Session.SelectedPrimaryWeapon + d + n) % n;
                Rebuild();
            });

            var opt = Btn(pr, "OPTIONS", 16, () => OnOptions?.Invoke(), new Color(1f, 1f, 1f, 0.07f));
            Anchor((RectTransform)opt.transform, 0.05f, 0.02f, 0.49f, 0.085f);
            var rules = Btn(pr, "ALL RULES", 16, () => OnRules?.Invoke(), new Color(1f, 1f, 1f, 0.07f));
            Anchor((RectTransform)rules.transform, 0.51f, 0.02f, 0.95f, 0.085f);
        }

        // ------------------------------------------------------------------ roster
        private void Rebuild()
        {
            for (int i = _content.childCount - 1; i >= 0; i--)
            {
                var old = _content.GetChild(i).gameObject;
                old.SetActive(false); // out of the layout this frame; destroyed at the end of it
                Destroy(old);
            }
            _buildingRows = true;
            PlayerRow();
            for (int i = 0; i < SkirmishSetup.Bots.Count; i++) BotRow(i);
            _buildingRows = false;
            foreach (var r in _refresh) r();
            int n = SkirmishSetup.Bots.Count;
            _countLabel.text = $"PLAYERS  <color=#{Hex(Accent)}>{n + (SkirmishSetup.PlayerSpectates ? 0 : 1)}</color><size=70%> / {SkirmishSetup.MaxBots + 1}</size>";
            var sprite = Resources.Load<Sprite>(LR2Session.PreviewResourcePath(LR2Session.SelectedMapIndex));
            _preview.sprite = sprite;
            _preview.color = sprite != null ? Color.white : new Color(1f, 1f, 1f, 0.05f);
            bool ok = n >= (SkirmishSetup.PlayerSpectates ? 2 : 1);
            _startButton.interactable = ok;
            _summary.text = ok ? Summary() : SkirmishSetup.PlayerSpectates ? "AI VS AI NEEDS AT LEAST 2 BOTS" : "ADD AT LEAST ONE BOT";
        }

        private string Summary()
        {
            int bots = SkirmishSetup.Bots.Count;
            string who = SkirmishSetup.PlayerSpectates ? "AI VS AI" : "YOU";
            if (!SkirmishSetup.IsTeamGame) return $"{who}  ·  FREE FOR ALL  ·  {bots} BOTS";
            int teams = SkirmishSetup.TeamCount;
            string kind = teams >= 4 ? "4-WAY" : teams == 3 ? "3-WAY" : teams == 2 ? "2 TEAMS" : "1 TEAM";
            return $"{who}  ·  {kind} {SkirmishSetup.ModeNames[ModeIndex()]}  ·  {bots} BOTS";
        }

        private RectTransform Row(Color strip)
        {
            var row = Img(_content, "Row", RowColor);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 46f;
            row.gameObject.AddComponent<CanvasGroup>();
            var s = Img(row.rectTransform, "Strip", strip);
            Anchor(s.rectTransform, 0f, 0f, 0f, 1f);
            s.rectTransform.sizeDelta = new Vector2(6f, 0f);
            s.rectTransform.anchoredPosition = new Vector2(3f, 0f);
            return row.rectTransform;
        }

        private void PlayerRow()
        {
            bool spec = SkirmishSetup.PlayerSpectates;
            var r = Row(spec ? new Color(0.72f, 0.55f, 1f) : TeamUtil.TeamColor(SkirmishSetup.PlayerTeam));
            var name = Label(r, spec ? "SPECTATOR" : $"YOU  <color=#{Hex(Dim)}><size=75%>{LR2Settings.PlayerName}</size></color>", 18, Text, true);
            Anchor(name.rectTransform, 0.03f, 0f, 0.34f, 1f);
            Stepper(r, 0.35f, 0.12f, 0.66f, 0.88f, () => spec ? "AI VS AI" : "PLAYING", d =>
            {
                SkirmishSetup.PlayerSpectates = !SkirmishSetup.PlayerSpectates;
                if (!SkirmishSetup.PlayerSpectates && SkirmishSetup.PlayerTeam == TeamId.None && SkirmishSetup.IsTeamGame) SkirmishSetup.PlayerTeam = TeamId.Alpha;
                Rebuild();
            }, spec ? new Color(0.72f, 0.55f, 1f) : Text);
            if (!spec) TeamStepper(r, () => SkirmishSetup.PlayerTeam, t => SkirmishSetup.PlayerTeam = t);
        }

        private void BotRow(int index)
        {
            var bot = SkirmishSetup.Bots[index];
            bool teams = TeamsMatter;
            var r = Row(teams ? TeamUtil.TeamColor(bot.team) : new Color(0.6f, 0.6f, 0.6f));
            var name = Label(r, $"<color=#{Hex(Dim)}>{index + 1:00}</color>  BOT", 18, Text);
            Anchor(name.rectTransform, 0.03f, 0f, 0.34f, 1f);
            int d0 = Mathf.Clamp(bot.difficulty, 0, DiffColors.Length - 1);
            Stepper(r, 0.35f, 0.12f, 0.66f, 0.88f, () => SkirmishSetup.DifficultyNames[Mathf.Clamp(bot.difficulty, 0, 4)], d =>
            {
                bot.difficulty = (bot.difficulty + d + SkirmishSetup.DifficultyNames.Length) % SkirmishSetup.DifficultyNames.Length;
                Rebuild();
            }, DiffColors[d0]);
            TeamStepper(r, () => bot.team, t => bot.team = t);
            var x = Btn(r, "X", 16, () => { SkirmishSetup.Bots.Remove(bot); Rebuild(); }, new Color(1f, 0.3f, 0.27f, 0.18f));
            Anchor((RectTransform)x.transform, 0.935f, 0.14f, 0.995f, 0.86f);
        }

        private bool TeamsMatter => SkirmishSetup.Mode != GameModeType.FreeForAll && SkirmishSetup.Mode != GameModeType.Duel;

        private void TeamStepper(RectTransform r, Func<TeamId> get, Action<TeamId> set)
        {
            if (!TeamsMatter)
            {
                var ffa = Label(r, "FREE FOR ALL", 15, Dim);
                ffa.alignment = TextAlignmentOptions.Center;
                Anchor(ffa.rectTransform, 0.67f, 0f, 0.93f, 1f);
                return;
            }
            Stepper(r, 0.67f, 0.12f, 0.93f, 0.88f, () => TeamUtil.ShortName(get()), d => { set(SkirmishSetup.NextTeam(get(), d)); Rebuild(); }, TeamUtil.TeamColor(get()));
        }

        private void AddBot()
        {
            if (SkirmishSetup.Bots.Count >= SkirmishSetup.MaxBots) return;
            var b = new SkirmishBot { difficulty = SkirmishSetup.Bots.Count > 0 ? SkirmishSetup.Bots[SkirmishSetup.Bots.Count - 1].difficulty : 1 };
            b.team = TeamsMatter ? SmallestTeam() : TeamId.None;
            SkirmishSetup.Bots.Add(b);
            Rebuild();
            StartCoroutine(ScrollToBottom());
        }

        private IEnumerator ScrollToBottom()
        {
            yield return null;
            if (_scroll != null) _scroll.verticalNormalizedPosition = 0f;
        }

        /// <summary>The used team with the fewest soldiers (the player counts), or Bravo when only one team is used.</summary>
        private TeamId SmallestTeam()
        {
            var counts = new Dictionary<TeamId, int>();
            if (!SkirmishSetup.PlayerSpectates && SkirmishSetup.PlayerTeam != TeamId.None) counts[SkirmishSetup.PlayerTeam] = 1;
            foreach (var b in SkirmishSetup.Bots)
                if (b.team != TeamId.None) { counts.TryGetValue(b.team, out int n); counts[b.team] = n + 1; }
            if (counts.Count < 2) return counts.ContainsKey(TeamId.Alpha) ? TeamId.Bravo : TeamId.Alpha;
            TeamId best = TeamId.Alpha; int min = int.MaxValue;
            foreach (var kv in counts) if (kv.Value < min) { min = kv.Value; best = kv.Key; }
            return best;
        }

        /// <summary>Deal the bots round-robin over the teams already in use (player counted), so sizes differ by at most one.</summary>
        private void BalanceTeams()
        {
            if (!TeamsMatter) return;
            var used = new List<TeamId>();
            if (!SkirmishSetup.PlayerSpectates && SkirmishSetup.PlayerTeam != TeamId.None) used.Add(SkirmishSetup.PlayerTeam);
            foreach (var b in SkirmishSetup.Bots) if (b.team != TeamId.None && !used.Contains(b.team)) used.Add(b.team);
            if (used.Count < 2) { used.Clear(); used.Add(TeamId.Alpha); used.Add(TeamId.Bravo); }
            var size = new Dictionary<TeamId, int>();
            foreach (var t in used) size[t] = 0;
            if (!SkirmishSetup.PlayerSpectates && size.ContainsKey(SkirmishSetup.PlayerTeam)) size[SkirmishSetup.PlayerTeam] = 1;
            foreach (var b in SkirmishSetup.Bots)
            {
                TeamId pick = used[0];
                foreach (var t in used) if (size[t] < size[pick]) pick = t;
                b.team = pick; size[pick]++;
            }
            Rebuild();
        }

        private string AllDifficultyLabel()
        {
            if (SkirmishSetup.Bots.Count == 0) return "-";
            int d = SkirmishSetup.Bots[0].difficulty;
            foreach (var b in SkirmishSetup.Bots) if (b.difficulty != d) return "MIXED";
            return SkirmishSetup.DifficultyNames[Mathf.Clamp(d, 0, 4)];
        }

        private int ModeIndex() => Mathf.Max(0, Array.IndexOf(SkirmishSetup.Modes, SkirmishSetup.Mode));

        // ------------------------------------------------------------------ rules
        private void LoadRules()
        {
            var template = _templateFor != null ? _templateFor(SkirmishSetup.Mode) : null;
            _rules = template != null ? LR2Session.LoadModeRules(template) : default;
        }

        /// <summary>Saved per mode, so LR2Session.PrepareConfiguredMatch (which re-applies the saved rules) keeps them.</summary>
        private void SaveRules()
        {
            _rules.ClampFor(SkirmishSetup.Mode);
            LR2Session.SaveModeRules(SkirmishSetup.Mode, _rules);
            foreach (var r in _refresh) r();
        }

        /// <summary>The rules sub-menu may have changed them.</summary>
        public void Reload()
        {
            LoadRules();
            Rebuild();
        }

        private void Start_()
        {
            if (!_startButton.interactable) return;
            SaveRules();
            SkirmishSetup.Save();
            SkirmishSetup.Active = true;
            OnStart?.Invoke(SkirmishSetup.Mode);
        }

        // ------------------------------------------------------------------ intro animation
        /// <summary>Rows slide in one after another (unscaled time: the menu background runs in slow motion).</summary>
        public void PlayIntro()
        {
            if (_intro != null) StopCoroutine(_intro);
            _intro = StartCoroutine(Intro());
        }

        private IEnumerator Intro()
        {
            var rows = new List<(RectTransform rt, CanvasGroup cg)>();
            for (int i = 0; i < _content.childCount; i++)
            {
                var rt = (RectTransform)_content.GetChild(i);
                var cg = rt.GetComponent<CanvasGroup>();
                if (cg != null && rt.gameObject.activeSelf) { cg.alpha = 0f; rows.Add((rt, cg)); }
            }
            float t0 = Time.unscaledTime;
            while (true)
            {
                bool done = true;
                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i].cg == null) continue;
                    float k = Mathf.Clamp01((Time.unscaledTime - t0 - i * 0.03f) / 0.28f);
                    float e = 1f - (1f - k) * (1f - k) * (1f - k);
                    rows[i].cg.alpha = e;
                    if (k < 1f) done = false;
                }
                if (done) break;
                yield return null;
            }
            _intro = null;
        }

        // ------------------------------------------------------------------ widgets
        private void Stepper(RectTransform parent, float x0, float y0, float x1, float y1, Func<string> value, Action<int> step, Color? valueColor = null)
        {
            var box = Img(parent, "Stepper", new Color(0f, 0f, 0f, 0.28f));
            Anchor(box.rectTransform, x0, y0, x1, y1);
            var prev = Btn(box.rectTransform, "<", 18, () => step(-1), new Color(1f, 1f, 1f, 0.05f));
            Anchor((RectTransform)prev.transform, 0f, 0f, 0.16f, 1f);
            var next = Btn(box.rectTransform, ">", 18, () => step(1), new Color(1f, 1f, 1f, 0.05f));
            Anchor((RectTransform)next.transform, 0.84f, 0f, 1f, 1f);
            var v = Label(box.rectTransform, value(), 16, valueColor ?? Text, true);
            v.alignment = TextAlignmentOptions.Center;
            Anchor(v.rectTransform, 0.16f, 0f, 0.84f, 1f);
            if (!_buildingRows) _refresh.Add(() => { if (v != null) v.text = value(); });
        }

        private Button Btn(Transform parent, string text, float size, Action onClick, Color bg)
        {
            var img = Img(parent, "Btn_" + text, bg);
            var b = img.gameObject.AddComponent<Button>();
            var cb = b.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.8f, 1.7f, 1.5f, 2.6f); // tint multiplies the faint background: hover lifts it
            cb.pressedColor = new Color(1.2f, 1.2f, 1.2f, 3.2f);
            cb.selectedColor = Color.white;
            cb.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            cb.colorMultiplier = 1f;
            cb.fadeDuration = 0.08f;
            b.colors = cb;
            b.onClick.AddListener(() => onClick());
            var l = Label(img.rectTransform, text, size, Text, true);
            l.alignment = TextAlignmentOptions.Center;
            Stretch(l.rectTransform);
            return b;
        }

        private TMP_Text Label(Transform parent, string text, float size, Color color, bool heading = false)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            if (_theme != null && (heading ? _theme.headingFont : _theme.bodyFont) != null) t.font = heading ? _theme.headingFont : _theme.bodyFont;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.richText = true;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.characterSpacing = heading ? 2f : 1f;
            return t;
        }

        private static Image Img(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            return img;
        }

        private static GameObject Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private static void Anchor(RectTransform rt, float x0, float y0, float x1, float y1)
        {
            rt.anchorMin = new Vector2(x0, y0); rt.anchorMax = new Vector2(x1, y1); rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);
    }

    internal static class LobbyRectExt
    {
        public static RectTransform rectTransform(this GameObject go) => (RectTransform)go.transform;
    }
}
