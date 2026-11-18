using System.Collections.Generic;
using LR2.AI;
using LR2.Characters;
using LR2.Combat;
using LR2.Core;
using LR2.Game;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LR2.UI
{
    /// <summary>
    /// Live in-match tactical &amp; banter chat room: bots trash-talk opponents and each other on kills with human typing
    /// latency, teammate bots suggest tactical TDM plans, CTF zone calls alert pushes, and players can press T to chat
    /// or send quick squad commands ("flank", "hold", "push", "flag").
    /// </summary>
    public class TacticalChatSystem : MonoBehaviour
    {
        private struct ChatEntry
        {
            public string sender;
            public Color color;
            public string body;
            public float time;
        }

        private struct QueuedMsg
        {
            public float postAt;
            public string sender;
            public Color color;
            public string body;
        }

        private readonly List<ChatEntry> _history = new List<ChatEntry>(8);
        private readonly List<QueuedMsg> _pending = new List<QueuedMsg>(8);
        private readonly List<TMP_Text> _lines = new List<TMP_Text>(5);

        private CanvasGroup _group;
        private CanvasGroup _inputGroup;
        private TMP_Text _inputText;
        private TMP_Text _hintText;
        private SoldierController _playerSoldier;
        private Combatant _playerCombatant;

        private bool _typing;
        private string _draft = "";
        private float _lastActivityAt = -20f;
        private float _nextAutoBanterAt;
        private float _nextSquadPlanAt;
        private string _lastZoneName;
        private FlagZoneController.ZoneState _lastZoneState = FlagZoneController.ZoneState.Idle;

        private const int MaxVisibleLines = 5;

        private static readonly string[] KillPlayerRoasts =
        {
            "sit down {0}, wide peek paid the tax",
            "thanks for standing still {0} lol",
            "check your corners next time {0}",
            "clean scoreboard diff rn {0}",
            "caught you reloading in the open {0}",
            "you dropped your aim on that peek {0}"
        };

        private static readonly string[] HeadshotRoasts =
        {
            "helmet check {0} — spotless",
            "straight through the optic {0} ouch",
            "one tap special for {0}"
        };

        private static readonly string[] BotVsBotTrashTalk =
        {
            "{1} were you shooting at birds or me??",
            "back to spawn {1}, my lane now",
            "somebody tell {1} ADS helps lol",
            "easy pick on {1}, onto the next",
            "{1} really swung that wide with 20hp"
        };

        private static readonly string[] BotKilledByPlayerReactions =
        {
            "alright {0}, sharp flick ngl",
            "ok {0} is locked in today",
            "wait how did {0} win that trade?!",
            "nice shot {0}, running it back now",
            "fair play {0}, checking that off-angle next"
        };

        private static readonly string[] TdmSquadPlans =
        {
            "Holding mid sightline — push opposite flank on my contact!",
            "Enemies stacking tight; let's pinch from two angles.",
            "I'm anchoring rear cover while you pressure forward.",
            "Got eyes on movement near center — stacking up with you!",
            "Rotating to crossfire; flush them out toward my lane."
        };

        public static TacticalChatSystem Mount(Transform hudParent)
        {
            var go = new GameObject("TacticalChatRoom", typeof(RectTransform));
            go.transform.SetParent(hudParent, false);
            var chat = go.AddComponent<TacticalChatSystem>();
            chat.BuildUI((RectTransform)go.transform);
            return chat;
        }

        private void OnEnable()
        {
            GameEvents.Killed += OnKilled;
            GameEvents.MatchStateChanged += OnMatchStateChanged;
            if (Keyboard.current != null) Keyboard.current.onTextInput += OnTextInputChar;
        }

        private void OnDisable()
        {
            GameEvents.Killed -= OnKilled;
            GameEvents.MatchStateChanged -= OnMatchStateChanged;
            if (Keyboard.current != null) Keyboard.current.onTextInput -= OnTextInputChar;
            if (_typing && _playerSoldier != null) _playerSoldier.ControlsEnabled = true;
        }

        private void OnTextInputChar(char ch)
        {
            if (!_typing) return;
            if (ch == '\b')
            {
                if (_draft.Length > 0) _draft = _draft.Substring(0, _draft.Length - 1);
            }
            else if (ch >= 32 && ch != 127 && _draft.Length < 64)
            {
                _draft += ch;
            }
            if (_inputText != null) _inputText.text = "> " + _draft + "_";
        }

        private void BuildUI(RectTransform rt)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.30f);
            rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = new Vector2(28f, 0f);
            rt.sizeDelta = new Vector2(470f, 165f);

            _group = rt.gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.interactable = false;
            _group.blocksRaycasts = false;

            var bg = rt.gameObject.AddComponent<Image>();
            bg.color = new Color(0.025f, 0.032f, 0.044f, 0.62f);

            for (int i = 0; i < MaxVisibleLines; i++)
            {
                var lGo = new GameObject($"Line{i}", typeof(RectTransform));
                lGo.transform.SetParent(rt, false);
                var lrt = (RectTransform)lGo.transform;
                lrt.anchorMin = lrt.anchorMax = new Vector2(0f, 1f);
                lrt.pivot = new Vector2(0f, 1f);
                lrt.anchoredPosition = new Vector2(10f, -8f - i * 24f);
                lrt.sizeDelta = new Vector2(450f, 23f);
                var txt = lGo.AddComponent<TextMeshProUGUI>();
                txt.fontSize = 14.5f;
                txt.alignment = TextAlignmentOptions.MidlineLeft;
                txt.textWrappingMode = TextWrappingModes.NoWrap;
                txt.overflowMode = TextOverflowModes.Ellipsis;
                txt.richText = true;
                txt.text = "";
                _lines.Add(txt);
            }

            var inGo = new GameObject("InputBar", typeof(RectTransform));
            inGo.transform.SetParent(rt, false);
            var irt = (RectTransform)inGo.transform;
            irt.anchorMin = irt.anchorMax = new Vector2(0f, 0f);
            irt.pivot = new Vector2(0f, 0f);
            irt.anchoredPosition = new Vector2(8f, 6f);
            irt.sizeDelta = new Vector2(454f, 26f);
            _inputGroup = inGo.AddComponent<CanvasGroup>();
            _inputGroup.alpha = 0f;

            var inBg = inGo.AddComponent<Image>();
            inBg.color = new Color(0.07f, 0.09f, 0.12f, 0.92f);

            var itGo = new GameObject("Text", typeof(RectTransform));
            itGo.transform.SetParent(inGo.transform, false);
            var itRt = (RectTransform)itGo.transform;
            itRt.anchorMin = Vector2.zero;
            itRt.anchorMax = Vector2.one;
            itRt.offsetMin = new Vector2(8f, 0f);
            itRt.offsetMax = new Vector2(-8f, 0f);
            _inputText = itGo.AddComponent<TextMeshProUGUI>();
            _inputText.fontSize = 14.5f;
            _inputText.alignment = TextAlignmentOptions.MidlineLeft;
            _inputText.color = Color.white;

            var hGo = new GameObject("Hint", typeof(RectTransform));
            hGo.transform.SetParent(rt, false);
            var hRt = (RectTransform)hGo.transform;
            hRt.anchorMin = hRt.anchorMax = new Vector2(0f, 0f);
            hRt.pivot = new Vector2(0f, 0f);
            hRt.anchoredPosition = new Vector2(10f, 7f);
            hRt.sizeDelta = new Vector2(450f, 20f);
            _hintText = hGo.AddComponent<TextMeshProUGUI>();
            _hintText.fontSize = 12f;
            _hintText.color = new Color(0.7f, 0.76f, 0.84f, 0.72f);
            _hintText.text = "[T] TEAM/ALL CHAT  ·  COMMANDS: FLANK / HOLD / PUSH";
        }

        private void OnMatchStateChanged(MatchStateEvent e)
        {
            if (e.current == MatchState.Playing)
            {
                _nextSquadPlanAt = Time.unscaledTime + 5.5f;
                _nextAutoBanterAt = Time.unscaledTime + 11f;
                Enqueue(0.9f, "COMMS", new Color(0.95f, 0.74f, 0.28f), "Live tactical comms online. Press [T] to chat or issue squad calls.");
            }
        }

        private void OnKilled(KillEvent e)
        {
            if (e.killer == null || e.victim == null || e.killer == e.victim) return;
            string kName = e.killer.DisplayName;
            string vName = e.victim.DisplayName;

            if (!e.killer.IsLocalPlayer && e.victim.IsLocalPlayer)
            {
                if (Random.value > 0.78f) return;
                string pool = e.headshot && Random.value < 0.65f
                    ? HeadshotRoasts[Random.Range(0, HeadshotRoasts.Length)]
                    : KillPlayerRoasts[Random.Range(0, KillPlayerRoasts.Length)];
                Enqueue(Random.Range(1.0f, 2.1f), kName, TeamUtil.EnemyColor, string.Format(pool, vName));
            }
            else if (!e.killer.IsLocalPlayer && !e.victim.IsLocalPlayer)
            {
                if (Random.value > 0.55f) return;
                string line = BotVsBotTrashTalk[Random.Range(0, BotVsBotTrashTalk.Length)];
                Enqueue(Random.Range(1.1f, 2.2f), kName, ColorOf(e.killer), string.Format(line, kName, vName));
            }
            else if (e.killer.IsLocalPlayer && !e.victim.IsLocalPlayer)
            {
                if (Random.value > 0.45f) return;
                string line = BotKilledByPlayerReactions[Random.Range(0, BotKilledByPlayerReactions.Length)];
                Enqueue(Random.Range(1.2f, 2.4f), vName, ColorOf(e.victim), string.Format(line, kName));
            }
        }

        private void Update()
        {
            if (_playerCombatant == null)
            {
                _playerCombatant = Combatant.FindLocalPlayer();
                _playerSoldier = _playerCombatant != null ? _playerCombatant.GetComponent<SoldierController>() : null;
            }

            float now = Time.unscaledTime;
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                if (now >= _pending[i].postAt)
                {
                    var m = _pending[i];
                    _pending.RemoveAt(i);
                    Post(m.sender, m.color, m.body);
                }
            }

            PollSquadAndFlagCallouts(now);
            HandlePlayerChatInput(now);

            float idleAge = now - _lastActivityAt;
            float targetAlpha = _typing ? 1f : idleAge < 7.5f ? 0.96f : 0.22f;
            if (_history.Count == 0 && !_typing) targetAlpha = 0f;
            if (_group != null) _group.alpha = Mathf.MoveTowards(_group.alpha, targetAlpha, Time.unscaledDeltaTime * 5f);
            if (_inputGroup != null) _inputGroup.alpha = _typing ? 1f : 0f;
            if (_hintText != null) _hintText.enabled = !_typing && _history.Count > 0;
        }

        private void PollSquadAndFlagCallouts(float now)
        {
            var mm = MatchManager.Instance;
            if (mm == null || mm.State != MatchState.Playing) return;

            var flag = FlagZoneController.Instance;
            if (flag != null)
            {
                if (_lastZoneName != flag.ActiveSectorName)
                {
                    _lastZoneName = flag.ActiveSectorName;
                    Enqueue(0.8f, "FLAG HQ", new Color(0.28f, 0.85f, 1f),
                        $"New strategic flag at {flag.ActiveSectorName}! Crash inner ring for 2.25x capture!");
                }
                else if (_lastZoneState != flag.State && flag.State == FlagZoneController.ZoneState.Contested && Random.value < 0.7f)
                {
                    Enqueue(0.6f, "SECTOR", new Color(1f, 0.72f, 0.28f),
                        $"{flag.ActiveSectorName} contested — push closer to overpower perimeter campers!");
                }
                _lastZoneState = flag.State;
            }

            if (mm.IsTeamMode && now >= _nextSquadPlanAt)
            {
                _nextSquadPlanAt = now + Random.Range(22f, 34f);
                Combatant ally = FindFriendlyBot();
                if (ally != null)
                {
                    string plan = TdmSquadPlans[Random.Range(0, TdmSquadPlans.Length)];
                    Enqueue(0.2f, ally.DisplayName + " [TEAM]", TeamUtil.FriendlyColor, plan);
                }
            }
            else if (!mm.IsTeamMode && now >= _nextAutoBanterAt)
            {
                _nextAutoBanterAt = now + Random.Range(26f, 40f);
                Combatant enemy = FindAnyBot();
                if (enemy != null)
                {
                    string taunt = Random.value < 0.5f
                        ? "who's hiding behind those containers lol"
                        : "peek mid already someone, I'm bored";
                    Enqueue(0.2f, enemy.DisplayName, TeamUtil.EnemyColor, taunt);
                }
            }
        }

        private void HandlePlayerChatInput(float now)
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (!_typing)
            {
                if (kb.tKey.wasPressedThisFrame && (_playerSoldier == null || !_playerSoldier.IsDead))
                {
                    var mm = MatchManager.Instance;
                    if (mm != null && mm.State == MatchState.Ended) return;
                    _typing = true;
                    _draft = "";
                    _lastActivityAt = now;
                    if (_playerSoldier != null) _playerSoldier.ControlsEnabled = false;
                    if (_inputText != null) _inputText.text = "> _";
                }
                return;
            }

            _lastActivityAt = now;
            if (kb.escapeKey.wasPressedThisFrame)
            {
                _typing = false;
                _draft = "";
                if (_playerSoldier != null) _playerSoldier.ControlsEnabled = true;
                return;
            }

            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
            {
                string msg = _draft.Trim();
                _typing = false;
                _draft = "";
                if (_playerSoldier != null) _playerSoldier.ControlsEnabled = true;
                if (!string.IsNullOrEmpty(msg)) SubmitPlayerMessage(msg);
                return;
            }
        }

        private void SubmitPlayerMessage(string msg)
        {
            string pName = _playerCombatant != null ? _playerCombatant.DisplayName : LR2Settings.PlayerName;
            Post(pName, TeamUtil.FriendlyColor, msg);

            string lower = msg.ToLowerInvariant();
            Combatant ally = FindFriendlyBot();
            if (ally != null && _playerCombatant != null)
            {
                var bot = ally.GetComponent<BotController>();
                if (lower.Contains("flank"))
                {
                    if (bot != null) bot.IssueDirective(new BotDirective { order = BotOrder.Flank, position = _playerCombatant.Position + _playerCombatant.transform.right * 14f, duration = 12f });
                    Enqueue(0.9f, ally.DisplayName + " [TEAM]", TeamUtil.FriendlyColor, "Copy! Swinging wide flank right now!");
                    return;
                }
                if (lower.Contains("hold"))
                {
                    if (bot != null) bot.IssueDirective(new BotDirective { order = BotOrder.HoldPosition, position = ally.Position, duration = 12f });
                    Enqueue(0.9f, ally.DisplayName + " [TEAM]", TeamUtil.FriendlyColor, "Holding current angle, call when you push!");
                    return;
                }
                if (lower.Contains("push") || lower.Contains("flag"))
                {
                    Vector3 targetPos = FlagZoneController.Instance != null ? FlagZoneController.Instance.ActiveFlagPosition : _playerCombatant.Position + _playerCombatant.transform.forward * 16f;
                    if (bot != null) bot.IssueDirective(new BotDirective { order = BotOrder.MoveTo, position = targetPos, duration = 12f });
                    Enqueue(0.9f, ally.DisplayName + " [TEAM]", TeamUtil.FriendlyColor, "Rogering up — crashing the objective with you!");
                    return;
                }
            }

            Combatant responder = FindAnyBot();
            if (responder != null)
            {
                string reply = lower.Contains("gg") ? "gg, tight rounds!" :
                               lower.Contains("nice") ? "appreciate it, watch this next angle" :
                               "talk all you want, peek mid then 👀";
                Enqueue(Random.Range(1.0f, 1.9f), responder.DisplayName, ColorOf(responder), reply);
            }
        }

        private Combatant FindFriendlyBot()
        {
            if (_playerCombatant == null) return null;
            foreach (var c in Combatant.All)
                if (c != null && c != _playerCombatant && c.IsAlive && !TeamUtil.AreHostile(_playerCombatant, c) && c.GetComponent<BotController>() != null)
                    return c;
            return null;
        }

        private Combatant FindAnyBot()
        {
            foreach (var c in Combatant.All)
                if (c != null && !c.IsLocalPlayer && c.GetComponent<BotController>() != null)
                    return c;
            return null;
        }

        private Color ColorOf(Combatant c)
        {
            if (c == null) return Color.white;
            if (c.IsLocalPlayer || (_playerCombatant != null && !TeamUtil.AreHostile(_playerCombatant, c)))
                return TeamUtil.FriendlyColor;
            return TeamUtil.EnemyColor;
        }

        private void Enqueue(float delay, string sender, Color color, string body)
        {
            if (_pending.Count > 10) _pending.RemoveAt(0);
            _pending.Add(new QueuedMsg { postAt = Time.unscaledTime + delay, sender = sender, color = color, body = body });
        }

        private void Post(string sender, Color color, string body)
        {
            _history.Add(new ChatEntry { sender = sender, color = color, body = body, time = Time.unscaledTime });
            while (_history.Count > MaxVisibleLines) _history.RemoveAt(0);
            _lastActivityAt = Time.unscaledTime;

            for (int i = 0; i < _lines.Count; i++)
            {
                if (i < _history.Count)
                {
                    var e = _history[i];
                    string hex = ColorUtility.ToHtmlStringRGB(e.color);
                    _lines[i].text = $"<color=#{hex}><b>{e.sender}</b></color>: {e.body}";
                }
                else
                {
                    _lines[i].text = "";
                }
            }
        }
    }
}
