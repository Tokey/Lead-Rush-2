using System.Collections.Generic;
using System.Text;
using LR2.Combat;
using LR2.Core;
using LR2.Game;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LR2.UI
{
    /// <summary>Pause menu (Esc), settings, scoreboard (hold Tab) and end-of-match screen.</summary>
    public class GameMenuController : MonoBehaviour
    {
        [Header("Panels")]
        public CanvasGroup pausePanel;
        public CanvasGroup settingsPanel;
        public CanvasGroup scoreboardPanel;
        public CanvasGroup endPanel;

        [Header("Pause buttons")]
        public Button resumeButton;
        public Button settingsButton;
        public Button restartButton;
        public Button menuButton;
        public Button quitButton;
        public Button settingsBackButton;

        [Header("Scoreboard")]
        public TMP_Text scoreboardTitle;
        public TMP_Text scoreboardBody;

        [Header("End screen")]
        public TMP_Text endTitle;
        public TMP_Text endSubtitle;
        public TMP_Text endScoreboard;
        public Button endRestartButton;
        public Button endMenuButton;

        public bool IsPaused { get; private set; }
        private float _endShownAt = -1f;

        private void Awake()
        {
            Show(pausePanel, false);
            Show(settingsPanel, false);
            Show(scoreboardPanel, false);
            Show(endPanel, false);
            Bind(resumeButton, Resume);
            Bind(settingsButton, () => { Show(pausePanel, false); Show(settingsPanel, true); });
            Bind(settingsBackButton, () => { Show(settingsPanel, false); Show(pausePanel, IsPaused); });
            Bind(restartButton, () => { Resume(); MatchManager.Instance?.RestartMatch(); });
            Bind(menuButton, () => { Resume(); MatchManager.Instance?.QuitToMenu(); });
            Bind(quitButton, Quit);
            Bind(endRestartButton, () => MatchManager.Instance?.RestartMatch());
            Bind(endMenuButton, () => MatchManager.Instance?.QuitToMenu());
        }

        private static void Bind(Button b, UnityEngine.Events.UnityAction a)
        {
            if (b != null) b.onClick.AddListener(a);
        }

        private void Update()
        {
            var kb = Keyboard.current;
            var mm = MatchManager.Instance;
            bool ended = mm != null && mm.State == MatchState.Ended;

            if (kb != null && kb.escapeKey.wasPressedThisFrame && !ended)
            {
                if (settingsPanel != null && settingsPanel.alpha > 0.5f)
                {
                    Show(settingsPanel, false);
                    Show(pausePanel, true);
                }
                else if (IsPaused) Resume();
                else Pause();
            }

            bool tab = kb != null && kb.tabKey.isPressed && !IsPaused && !ended;
            Show(scoreboardPanel, tab);
            if (tab) FillScoreboard(scoreboardBody);

            if (ended)
            {
                if (_endShownAt < 0f) _endShownAt = Time.unscaledTime;
                if (Time.unscaledTime - _endShownAt > 1.6f && endPanel != null && endPanel.alpha < 1f)
                {
                    Show(endPanel, true);
                    FillEnd(mm);
                }
            }
        }

        public void Pause()
        {
            IsPaused = true;
            Time.timeScale = 0f;
            AudioListener.pause = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Show(pausePanel, true);
        }

        public void Resume()
        {
            IsPaused = false;
            Time.timeScale = 1f;
            AudioListener.pause = false;
            Show(pausePanel, false);
            Show(settingsPanel, false);
            var mm = MatchManager.Instance;
            if (mm == null || mm.State != MatchState.Ended)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private static void Show(CanvasGroup g, bool show)
        {
            if (g == null) return;
            g.alpha = show ? 1f : 0f;
            g.interactable = show;
            g.blocksRaycasts = show;
        }

        private void FillEnd(MatchManager mm)
        {
            if (mm == null) return;
            if (endTitle != null)
            {
                endTitle.text = mm.IsDraw ? "DRAW" : mm.LocalPlayerWon ? "VICTORY" : "DEFEAT";
                endTitle.color = mm.IsDraw ? Color.white : mm.LocalPlayerWon ? new Color(1f, 0.75f, 0.3f) : new Color(1f, 0.32f, 0.28f);
            }
            if (endSubtitle != null)
            {
                string mvp = "";
                Combatant best = null;
                foreach (var p in mm.Participants)
                    if (p != null && (best == null || p.stats.score > best.stats.score)) best = p;
                if (best != null) mvp = $"TOP SCORE  {best.DisplayName}  -  {best.stats.kills} KILLS  /  {best.stats.score} POINTS";
                endSubtitle.text = mvp;
            }
            FillScoreboard(endScoreboard);
        }

        private void FillScoreboard(TMP_Text target)
        {
            if (target == null) return;
            var mm = MatchManager.Instance;
            var list = new List<Combatant>();
            if (mm != null) list.AddRange(mm.Participants);
            else foreach (var c in Combatant.All) if (c != null && c.Team != TeamId.Hostile) list.Add(c);
            list.RemoveAll(c => c == null);
            list.Sort((a, b) => b.stats.score != a.stats.score ? b.stats.score.CompareTo(a.stats.score) : b.stats.kills.CompareTo(a.stats.kills));

            var local = Combatant.FindLocalPlayer();
            var sb = new StringBuilder();
            sb.Append("<color=#9AA0A6><size=80%>PLAYER<pos=55%>K<pos=65%>D<pos=75%>A<pos=87%>SCORE</size></color>\n");
            foreach (var c in list)
            {
                Color col = c == local ? new Color(1f, 0.75f, 0.3f) : (local != null && !TeamUtil.AreHostile(local, c)) ? TeamUtil.FriendlyColor : TeamUtil.EnemyColor;
                sb.Append($"<color=#{ColorUtility.ToHtmlStringRGB(col)}>{c.DisplayName}</color>")
                  .Append($"<pos=55%>{c.stats.kills}<pos=65%>{c.stats.deaths}<pos=75%>{c.stats.assists}<pos=87%>{c.stats.score}\n");
            }
            target.text = sb.ToString();
            if (scoreboardTitle != null && mm != null && mm.Settings != null) scoreboardTitle.text = mm.Settings.displayName.ToUpperInvariant();
        }
    }
}
