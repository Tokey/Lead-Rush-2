using System.Collections;
using System.Collections.Generic;
using LR2.Core;
using LR2.Game;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LR2.UI
{
    /// <summary>Main menu: pick a map (with preview thumbnail), customize per-mode rules, adjust settings, and deploy.</summary>
    public class MainMenuController : MonoBehaviour
    {
        [Header("Modes")]
        public MatchSettings duelSettings;
        public MatchSettings ffaSettings;
        public MatchSettings tdmSettings;

        [Header("Buttons")]
        public Button duelButton;
        public Button ffaButton;
        public Button tdmButton;
        public Button modeRulesButton;
        public Button settingsButton;
        public Button quitButton;
        public Button settingsBackButton;

        [Header("Panels")]
        public CanvasGroup mainPanel;
        public CanvasGroup modeRulesPanel;
        public CanvasGroup settingsPanel;
        public CanvasGroup loadingPanel;

        [Header("Map Preview Card")]
        public Image mapPreviewImage;
        public TMP_Text mapPreviewTitle;
        public TMP_Text mapPreviewDescription;
        public TMP_Text mapPreviewIndexText;
        public TMP_Text mapPreviewStatusText;
        public Button mapPrevButton;
        public Button mapNextButton;

        [Header("Mode Rules Customizer")]
        public Button modeRulesDuelTab;
        public Button modeRulesFfaTab;
        public Button modeRulesTdmTab;
        public Button modeRulesBackButton;
        public Button modeRulesResetButton;
        public Button modeRulesPlayButton;
        public TMP_Text modeRulesTitleText;
        public Slider rulesScoreLimitSlider;
        public TMP_Text rulesScoreLimitValue;
        public Slider rulesTimeLimitSlider;
        public TMP_Text rulesTimeLimitValue;
        public Slider rulesRespawnSlider;
        public TMP_Text rulesRespawnValue;
        public Slider rulesCountdownSlider;
        public TMP_Text rulesCountdownValue;
        public Button rulesBotDiffPrevButton;
        public Button rulesBotDiffNextButton;
        public TMP_Text rulesBotDiffValueText;
        public Slider rulesEnemyBotsSlider;
        public TMP_Text rulesEnemyBotsValue;
        public Slider rulesFriendlyBotsSlider;
        public TMP_Text rulesFriendlyBotsValue;
        public Toggle rulesSeekerDronesToggle;
        public Slider rulesMaxOrbsSlider;
        public TMP_Text rulesMaxOrbsValue;
        public Slider rulesOrbIntervalSlider;
        public TMP_Text rulesOrbIntervalValue;

        [Header("Loading Anim")]
        public RectTransform loadingSpinner;
        public Image loadingBarFill;
        public TMP_Text loadingStatusText;

        [Header("Bot Difficulty Roster")]
        public Button difficultyPrevButton;
        public Button difficultyNextButton;
        public TMP_Text difficultyValueText;
        public TMP_Text versionText;
        public string mapScene = LR2Session.MapScene;

        private static readonly string[] DifficultyLabels = { "RECRUIT", "REGULAR", "HARDENED", "VETERAN" };

        private bool _isLoading;
        private bool _loadingRules;
        private GameModeType _editingMode = GameModeType.Duel;
        private ModeRuleConfig _editingRules;

        private readonly List<Texture2D> _generatedTexs = new List<Texture2D>();
        private readonly List<Sprite> _generatedSprites = new List<Sprite>();
        private Sprite[] _cachedMapSprites;

        private string _duelBaseLabel;
        private string _ffaBaseLabel;
        private string _tdmBaseLabel;

        private void Start()
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            var sp = settingsPanel != null ? settingsPanel.GetComponentInChildren<SettingsPanel>(true) : null;
            if (sp != null) sp.Load();
            LR2Settings.Apply();

            Show(mainPanel, true);
            Show(modeRulesPanel, false);
            Show(settingsPanel, false);
            Show(loadingPanel, false);
            if (versionText != null) versionText.text = $"LEADRUSH 2  v{Application.version}";

            CacheBaseModeLabels();

            if (duelButton != null) duelButton.onClick.AddListener(() => Play(duelSettings));
            if (ffaButton != null) ffaButton.onClick.AddListener(() => Play(ffaSettings));
            if (tdmButton != null) tdmButton.onClick.AddListener(() => Play(tdmSettings));
            if (modeRulesButton != null) modeRulesButton.onClick.AddListener(() => OpenModeRules(_editingMode));
            if (settingsButton != null) settingsButton.onClick.AddListener(() => { Show(mainPanel, false); Show(settingsPanel, true); });
            if (settingsBackButton != null) settingsBackButton.onClick.AddListener(ReturnFromSettings);
            if (quitButton != null) quitButton.onClick.AddListener(Quit);

            if (mapPrevButton != null) mapPrevButton.onClick.AddListener(() => StepMap(-1));
            if (mapNextButton != null) mapNextButton.onClick.AddListener(() => StepMap(1));

            if (difficultyPrevButton != null) difficultyPrevButton.onClick.AddListener(() => StepDifficulty(-1));
            if (difficultyNextButton != null) difficultyNextButton.onClick.AddListener(() => StepDifficulty(1));

            BindModeRulesUI();
            SelectMap(LR2Session.SelectedMapIndex);
            RefreshDifficultyLabel();
            RefreshModeButtons();
        }

        private void OnDestroy()
        {
            foreach (var s in _generatedSprites)
                if (s != null) Destroy(s);
            foreach (var t in _generatedTexs)
                if (t != null) Destroy(t);
            _generatedSprites.Clear();
            _generatedTexs.Clear();
        }

        private void Update()
        {
            if (_isLoading && loadingSpinner != null)
            {
                loadingSpinner.Rotate(0f, 0f, -280f * Time.unscaledDeltaTime);
                return;
            }

            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                if (settingsPanel != null && settingsPanel.alpha > 0.5f) ReturnFromSettings();
                else if (modeRulesPanel != null && modeRulesPanel.alpha > 0.5f) ReturnFromModeRules();
            }
        }

        private void ReturnFromSettings()
        {
            LR2Settings.Save();
            Show(settingsPanel, false);
            Show(mainPanel, true);
            RefreshDifficultyLabel();
            RefreshPreviewStatus();
        }

        private void StepDifficulty(int delta)
        {
            int count = DifficultyLabels.Length;
            int cur = Mathf.Clamp(LR2Settings.BotDifficulty, 0, count - 1);
            LR2Settings.BotDifficulty = (cur + delta % count + count) % count;
            RefreshDifficultyLabel();
            RefreshPreviewStatus();
        }

        private void RefreshDifficultyLabel()
        {
            if (difficultyValueText != null)
                difficultyValueText.text = DifficultyLabels[Mathf.Clamp(LR2Settings.BotDifficulty, 0, DifficultyLabels.Length - 1)];
        }

        // ------------------------------------------------------------------ map selection & preview
        private void StepMap(int delta)
        {
            int count = LR2Session.MapScenes.Length;
            int next = (LR2Session.SelectedMapIndex + delta % count + count) % count;
            SelectMap(next);
        }

        public void SelectMap(int index)
        {
            LR2Session.SelectedMapIndex = index;
            int idx = LR2Session.SelectedMapIndex;
            mapScene = LR2Session.CurrentMapScene;

            if (mapPreviewTitle != null && idx < LR2Session.MapDisplayNames.Length)
                mapPreviewTitle.text = LR2Session.MapDisplayNames[idx];
            if (mapPreviewDescription != null && idx < LR2Session.MapDescriptions.Length)
                mapPreviewDescription.text = LR2Session.MapDescriptions[idx];
            if (mapPreviewIndexText != null)
                mapPreviewIndexText.text = $"SECTOR {idx + 1:00} / {LR2Session.MapScenes.Length:00}";
            if (mapPreviewImage != null)
                mapPreviewImage.sprite = GetMapPreviewSprite(idx);

            RefreshPreviewStatus();
        }

        private Sprite GetMapPreviewSprite(int idx)
        {
            _cachedMapSprites ??= new Sprite[LR2Session.MapScenes.Length];
            if (_cachedMapSprites[idx] != null) return _cachedMapSprites[idx];

            Sprite loaded = Resources.Load<Sprite>(LR2Session.PreviewResourcePath(idx));
            if (loaded != null)
            {
                _cachedMapSprites[idx] = loaded;
                return loaded;
            }

            // Procedural tactical preview fallback if Play/Menu runs before screenshots are generated
            Sprite fallback = BuildProceduralMapSprite(idx);
            _cachedMapSprites[idx] = fallback;
            return fallback;
        }

        private Sprite BuildProceduralMapSprite(int idx)
        {
            const int w = 320, h = 180;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;

            Color[] accents =
            {
                new Color(0.28f, 0.56f, 0.68f, 1f), // Saltworks harbour
                new Color(0.75f, 0.36f, 0.24f, 1f), // Deadsector ruined city
                new Color(0.92f, 0.58f, 0.22f, 1f), // Foundry pipe refinery
                new Color(0.45f, 0.72f, 0.48f, 1f), // Test Range arena
            };
            Color col = accents[idx % accents.Length];
            Color bg = new Color(0.05f, 0.06f, 0.08f, 1f);

            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                float ny = (float)y / h;
                for (int x = 0; x < w; x++)
                {
                    float nx = (float)x / w;
                    bool grid = (x % 32 == 0) || (y % 30 == 0);
                    float vignette = 1f - Mathf.Clamp01(Vector2.Distance(new Vector2(nx, ny), new Vector2(0.5f, 0.5f)) * 1.15f);
                    float horizon = ny < 0.42f ? 0.32f + ny * 0.6f : 0.14f;
                    Color c = Color.Lerp(bg, col * horizon, vignette * 0.85f);
                    if (grid) c = Color.Lerp(c, col, 0.22f);

                    // Distinct architectural blocks per map silhouette
                    if (ny > 0.28f && ny < 0.58f)
                    {
                        float blk = Mathf.Sin((nx * 14f + idx * 1.7f) * Mathf.PI);
                        if (blk > 0.62f && ny < 0.45f + (idx % 3) * 0.05f) c = Color.Lerp(c, col * 1.1f, 0.45f);
                    }
                    pixels[y * w + x] = c;
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            var sp = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
            _generatedTexs.Add(tex);
            _generatedSprites.Add(sp);
            return sp;
        }

        // ------------------------------------------------------------------ per-mode rule customizer
        private MatchSettings SettingsFor(GameModeType mode) => mode switch
        {
            GameModeType.FreeForAll => ffaSettings,
            GameModeType.TeamDeathmatch => tdmSettings,
            _ => duelSettings,
        };

        private void CacheBaseModeLabels()
        {
            _duelBaseLabel = ReadButtonLabel(duelButton, "DUEL  -  1V1 VS BOT");
            _ffaBaseLabel = ReadButtonLabel(ffaButton, "FREE FOR ALL");
            _tdmBaseLabel = ReadButtonLabel(tdmButton, "TEAM DEATHMATCH");
        }

        private static string ReadButtonLabel(Button b, string fallback)
        {
            if (b == null) return fallback;
            var t = b.GetComponentInChildren<TMP_Text>(true);
            return t != null && !string.IsNullOrEmpty(t.text) ? t.text : fallback;
        }

        private static void SetButtonLabel(Button b, string text)
        {
            if (b == null) return;
            var t = b.GetComponentInChildren<TMP_Text>(true);
            if (t != null) t.text = text;
        }

        private void RefreshModeButtons()
        {
            SetButtonLabel(duelButton, _duelBaseLabel + (LR2Session.HasCustomModeRules(GameModeType.Duel) ? "  [CUSTOM]" : ""));
            SetButtonLabel(ffaButton, _ffaBaseLabel + (LR2Session.HasCustomModeRules(GameModeType.FreeForAll) ? "  [CUSTOM]" : ""));
            SetButtonLabel(tdmButton, _tdmBaseLabel + (LR2Session.HasCustomModeRules(GameModeType.TeamDeathmatch) ? "  [CUSTOM]" : ""));
            RefreshPreviewStatus();
        }

        private void RefreshPreviewStatus()
        {
            if (mapPreviewStatusText == null) return;
            var template = SettingsFor(_editingMode) ?? duelSettings;
            var cfg = LR2Session.LoadModeRules(template);
            int mins = Mathf.RoundToInt(cfg.timeLimitSeconds / 60f);
            string modeLabel = _editingMode switch
            {
                GameModeType.FreeForAll => "FFA",
                GameModeType.TeamDeathmatch => "TDM",
                _ => "DUEL",
            };
            string bots = _editingMode == GameModeType.TeamDeathmatch
                ? $"{cfg.friendlyBots + 1}V{cfg.enemyBots}"
                : _editingMode == GameModeType.Duel ? "1V1" : $"1V{cfg.enemyBots}";
            string drones = cfg.spawnOrbs ? $"DRONES ON ({cfg.maxOrbs})" : "DRONES OFF";
            mapPreviewStatusText.text = $"{modeLabel} RULES  ·  {bots}  ·  FIRST TO {cfg.scoreLimit}  ·  {mins} MIN  ·  {cfg.respawnDelay:0.0}S RESPAWN  ·  {drones}";
        }

        private void OpenModeRules(GameModeType mode)
        {
            _editingMode = mode;
            Show(mainPanel, false);
            Show(settingsPanel, false);
            Show(modeRulesPanel, true);
            LoadModeIntoEditor(mode);
        }

        private void ReturnFromModeRules()
        {
            Show(modeRulesPanel, false);
            Show(mainPanel, true);
            RefreshModeButtons();
        }

        private void BindModeRulesUI()
        {
            if (modeRulesDuelTab != null) modeRulesDuelTab.onClick.AddListener(() => { _editingMode = GameModeType.Duel; LoadModeIntoEditor(_editingMode); });
            if (modeRulesFfaTab != null) modeRulesFfaTab.onClick.AddListener(() => { _editingMode = GameModeType.FreeForAll; LoadModeIntoEditor(_editingMode); });
            if (modeRulesTdmTab != null) modeRulesTdmTab.onClick.AddListener(() => { _editingMode = GameModeType.TeamDeathmatch; LoadModeIntoEditor(_editingMode); });
            if (modeRulesBackButton != null) modeRulesBackButton.onClick.AddListener(ReturnFromModeRules);
            if (modeRulesResetButton != null) modeRulesResetButton.onClick.AddListener(() =>
            {
                LR2Session.ResetModeRules(_editingMode);
                LoadModeIntoEditor(_editingMode);
                RefreshModeButtons();
            });
            if (modeRulesPlayButton != null) modeRulesPlayButton.onClick.AddListener(() =>
            {
                Show(modeRulesPanel, false);
                Play(SettingsFor(_editingMode) ?? duelSettings);
            });

            void StepRulesDifficulty(int delta)
            {
                if (_loadingRules) return;
                int cur = Mathf.Clamp(_editingRules.botDifficulty + 1, 0, 4);
                _editingRules.botDifficulty = (cur + delta % 5 + 5) % 5 - 1;
                SaveCurrentRules();
                RefreshRulesLabels();
            }
            if (rulesBotDiffPrevButton != null) rulesBotDiffPrevButton.onClick.AddListener(() => StepRulesDifficulty(-1));
            if (rulesBotDiffNextButton != null) rulesBotDiffNextButton.onClick.AddListener(() => StepRulesDifficulty(1));

            HookSlider(rulesScoreLimitSlider, v => _editingRules.scoreLimit = Mathf.RoundToInt(v));
            HookSlider(rulesTimeLimitSlider, v => _editingRules.timeLimitSeconds = Mathf.RoundToInt(v) * 60f);
            HookSlider(rulesRespawnSlider, v => _editingRules.respawnDelay = Mathf.RoundToInt(v) * 0.5f);
            HookSlider(rulesCountdownSlider, v => _editingRules.countdownSeconds = Mathf.RoundToInt(v));
            HookSlider(rulesEnemyBotsSlider, v => _editingRules.enemyBots = Mathf.RoundToInt(v));
            HookSlider(rulesFriendlyBotsSlider, v => _editingRules.friendlyBots = Mathf.RoundToInt(v));
            HookSlider(rulesMaxOrbsSlider, v => _editingRules.maxOrbs = Mathf.RoundToInt(v));
            HookSlider(rulesOrbIntervalSlider, v => _editingRules.orbSpawnInterval = Mathf.RoundToInt(v) * 5f);

            if (rulesSeekerDronesToggle != null)
            {
                rulesSeekerDronesToggle.onValueChanged.AddListener(on =>
                {
                    if (_loadingRules) return;
                    _editingRules.spawnOrbs = on;
                    SaveCurrentRules();
                    RefreshRulesLabels();
                });
            }
        }

        private void HookSlider(Slider s, System.Action<float> setter)
        {
            if (s == null) return;
            s.onValueChanged.AddListener(v =>
            {
                if (_loadingRules) return;
                setter(v);
                SaveCurrentRules();
                RefreshRulesLabels();
            });
        }

        private void LoadModeIntoEditor(GameModeType mode)
        {
            _loadingRules = true;
            _editingRules = LR2Session.LoadModeRules(SettingsFor(mode));

            HighlightTab(modeRulesDuelTab, mode == GameModeType.Duel);
            HighlightTab(modeRulesFfaTab, mode == GameModeType.FreeForAll);
            HighlightTab(modeRulesTdmTab, mode == GameModeType.TeamDeathmatch);

            if (modeRulesTitleText != null)
            {
                string label = mode switch
                {
                    GameModeType.FreeForAll => "FREE FOR ALL  ·  SOLO ARENA RULES",
                    GameModeType.TeamDeathmatch => "TEAM DEATHMATCH  ·  SQUAD RULES",
                    _ => "DUEL  ·  1V1 COMPETITIVE RULES",
                };
                modeRulesTitleText.text = label;
            }

            SetRuleSlider(rulesScoreLimitSlider, 1f, 100f, _editingRules.scoreLimit, true);
            SetRuleSlider(rulesTimeLimitSlider, 1f, 30f, Mathf.RoundToInt(_editingRules.timeLimitSeconds / 60f), true);
            SetRuleSlider(rulesRespawnSlider, 0f, 30f, Mathf.RoundToInt(_editingRules.respawnDelay * 2f), true);
            SetRuleSlider(rulesCountdownSlider, 0f, 10f, Mathf.RoundToInt(_editingRules.countdownSeconds), true);
            SetRuleSlider(rulesEnemyBotsSlider, 1f, 8f, _editingRules.enemyBots, true);
            SetRuleSlider(rulesFriendlyBotsSlider, 0f, 5f, _editingRules.friendlyBots, true);
            SetRuleSlider(rulesMaxOrbsSlider, 1f, 6f, _editingRules.maxOrbs, true);
            SetRuleSlider(rulesOrbIntervalSlider, 2f, 18f, Mathf.RoundToInt(_editingRules.orbSpawnInterval / 5f), true);

            if (rulesSeekerDronesToggle != null) rulesSeekerDronesToggle.SetIsOnWithoutNotify(_editingRules.spawnOrbs);

            RefreshRulesLabels();
            _loadingRules = false;
        }

        private void SaveCurrentRules()
        {
            LR2Session.SaveModeRules(_editingMode, _editingRules);
            RefreshModeButtons();
        }

        private void RefreshRulesLabels()
        {
            if (rulesScoreLimitValue != null) rulesScoreLimitValue.text = $"{_editingRules.scoreLimit} KILLS";
            if (rulesTimeLimitValue != null) rulesTimeLimitValue.text = $"{Mathf.RoundToInt(_editingRules.timeLimitSeconds / 60f)} MIN";
            if (rulesRespawnValue != null) rulesRespawnValue.text = $"{_editingRules.respawnDelay:0.0} S";
            if (rulesCountdownValue != null) rulesCountdownValue.text = $"{Mathf.RoundToInt(_editingRules.countdownSeconds)} S";

            bool canEditEnemies = _editingMode != GameModeType.Duel;
            if (rulesEnemyBotsSlider != null) rulesEnemyBotsSlider.interactable = canEditEnemies;
            if (rulesEnemyBotsValue != null) rulesEnemyBotsValue.text = canEditEnemies ? _editingRules.enemyBots.ToString() : "1 (1v1)";

            bool canEditFriendly = _editingMode == GameModeType.TeamDeathmatch;
            if (rulesFriendlyBotsSlider != null) rulesFriendlyBotsSlider.interactable = canEditFriendly;
            if (rulesFriendlyBotsValue != null) rulesFriendlyBotsValue.text = canEditFriendly ? _editingRules.friendlyBots.ToString() : "N/A";

            bool dronesOn = _editingRules.spawnOrbs;
            if (rulesMaxOrbsSlider != null) rulesMaxOrbsSlider.interactable = dronesOn;
            if (rulesOrbIntervalSlider != null) rulesOrbIntervalSlider.interactable = dronesOn;
            if (rulesMaxOrbsValue != null) rulesMaxOrbsValue.text = dronesOn ? _editingRules.maxOrbs.ToString() : "OFF";
            if (rulesOrbIntervalValue != null) rulesOrbIntervalValue.text = dronesOn ? $"{Mathf.RoundToInt(_editingRules.orbSpawnInterval)} S" : "OFF";
            if (rulesBotDiffValueText != null)
            {
                int d = _editingRules.botDifficulty;
                rulesBotDiffValueText.text = d < 0 ? "GLOBAL SETTING" : DifficultyLabels[Mathf.Clamp(d, 0, DifficultyLabels.Length - 1)];
            }
        }

        private static void SetRuleSlider(Slider s, float min, float max, float val, bool whole)
        {
            if (s == null) return;
            s.minValue = min;
            s.maxValue = max;
            s.wholeNumbers = whole;
            s.SetValueWithoutNotify(val);
        }

        private static void HighlightTab(Button tab, bool active)
        {
            if (tab == null) return;
            var cb = tab.colors;
            cb.normalColor = active ? new Color(1f, 0.9f, 0.7f, 1f) : Color.white;
            cb.selectedColor = cb.normalColor;
            tab.colors = cb;
            var img = tab.GetComponent<Image>();
            if (img != null) img.color = active ? new Color(0.28f, 0.22f, 0.12f, 0.96f) : new Color(0.07f, 0.08f, 0.09f, 0.82f);
            var lbl = tab.GetComponentInChildren<TMP_Text>(true);
            if (lbl != null) lbl.color = active ? new Color(1f, 0.78f, 0.35f, 1f) : Color.white;
        }

        // ------------------------------------------------------------------ launch
        public void Play(MatchSettings settings)
        {
            if (_isLoading) return;
            string targetScene = LR2Session.CurrentMapScene;
            if (!Application.CanStreamedLevelBeLoaded(targetScene)) targetScene = mapScene;
            if (!Application.CanStreamedLevelBeLoaded(targetScene)) targetScene = "LR2_Saltworks";
            if (!Application.CanStreamedLevelBeLoaded(targetScene))
            {
                Debug.LogError($"[LR2] Scene '{targetScene}' is not in Build Settings. Run LR2 > Build Everything.");
                return;
            }
            LR2Session.PrepareConfiguredMatch(settings);
            LR2Settings.Save();
            Show(mainPanel, false);
            Show(modeRulesPanel, false);
            Show(loadingPanel, true);
            StartCoroutine(LoadMapRoutine(targetScene));
        }

        private IEnumerator LoadMapRoutine(string sceneName)
        {
            _isLoading = true;
            if (loadingBarFill != null) loadingBarFill.fillAmount = 0f;
            string niceName = LR2Session.MapDisplayNames[LR2Session.SelectedMapIndex];
            var op = SceneManager.LoadSceneAsync(sceneName);
            if (op == null)
            {
                _isLoading = false;
                yield break;
            }
            op.allowSceneActivation = false;
            float start = Time.unscaledTime;
            while (!op.isDone)
            {
                float elapsed = Time.unscaledTime - start;
                float realPct = Mathf.Clamp01(op.progress / 0.9f);
                float smoothPct = Mathf.Min(realPct, Mathf.Clamp01(elapsed / 0.65f));
                if (loadingBarFill != null) loadingBarFill.fillAmount = smoothPct;
                if (loadingStatusText != null) loadingStatusText.text = $"DEPLOYING TO {niceName}... {Mathf.RoundToInt(smoothPct * 100f)}%";
                if (op.progress >= 0.9f && elapsed >= 0.65f)
                {
                    if (loadingBarFill != null) loadingBarFill.fillAmount = 1f;
                    if (loadingStatusText != null) loadingStatusText.text = $"DEPLOYING TO {niceName}... 100%";
                    op.allowSceneActivation = true;
                }
                yield return null;
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
    }
}
