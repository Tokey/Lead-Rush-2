using System;
using System.Collections;
using LR2.Audio;
using LR2.Characters;
using LR2.Combat;
using LR2.Core;
using LR2.FX;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LR2.UI
{
    /// <summary>
    /// Full-screen tactical loading & intro curtain mounted before frame 0 so shaders, uniform textures, mipmaps,
    /// weapon prefabs, humanoid retarget rigs, and HDRP exposure settle BEFORE anything is revealed on screen.
    /// Automatically stays inert during headless / automated screenshot runners so QA captures are unaffected.
    /// </summary>
    [DefaultExecutionOrder(-900)]
    public class LR2LoadingIntro : MonoBehaviour
    {
        public static LR2LoadingIntro Instance { get; private set; }
        public static bool IsCoveringScreen => Instance != null && Instance._covering;

        private Canvas _canvas;
        private CanvasGroup _group;
        private TMP_Text _titleText;
        private TMP_Text _statusText;
        private TMP_Text _tipText;
        private Image _progressFill;
        private RectTransform _scanLine;
        private bool _covering;
        private bool _initialBoot = true;
        private bool _shadersWarmed;
        private string _pendingTitle = "LEADRUSH II";
        private string _pendingSub = "INITIALIZING TACTICAL SYSTEMS";
        private Coroutine _activeRoutine;

        private static readonly string[] Tips =
        {
            "SUPPRESSIVE FIRE AND CLOSE COMBAT RAMP ADAPTIVE INTENSITY — LISTEN FOR ENEMY FOOTSTEPS.",
            "BULLETS PASSING NEARBY TRIGGER SUPERSONIC WHIZ AND CRACK CUES TO LOCATE SHOOTERS.",
            "AIM DOWN SIGHTS TO HALVE RECOIL CLIMB AND TIGHTEN GROUPING AT MEDIUM RANGE.",
            "RELOADING ON THE MOVE KEEPS FULL MOBILITY — WATCH YOUR MAGAZINE WELL.",
        };

        private static bool IsAutomatedRunner()
        {
            if (Application.isBatchMode) return true;
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "-lr2QaBot" || a == "-lr2PoseCheck" || a == "-lr2Robot" || a == "-lr2CapturePreviews" || a == "-lr2Verify")
                    return true;
            }
            return false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void BootBeforeFirstFrame()
        {
            if (IsAutomatedRunner() || Instance != null) return;
            var go = new GameObject("[LR2 LoadingIntro]");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<LR2LoadingIntro>();
        }

        /// <summary>Called by MainMenuController before activating a target map scene so the curtain stays opaque smoothly.</summary>
        public static void BeginSceneTransition(string mapDisplayName)
        {
            if (Instance == null || IsAutomatedRunner()) return;
            Instance._pendingTitle = "DEPLOYING // " + (string.IsNullOrWhiteSpace(mapDisplayName) ? "COMBAT ZONE" : mapDisplayName.ToUpperInvariant());
            Instance._pendingSub = "SYNCHRONIZING TEXTURES & GEAR...";
            Instance.ShowImmediate(0.15f);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            BuildOverlay();
            SceneManager.sceneLoaded += OnSceneLoaded;

            string activeName = SceneManager.GetActiveScene().name;
            SetupPromptForScene(activeName);
            ShowImmediate(0f);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (IsAutomatedRunner())
            {
                if (_group != null) _group.alpha = 0f;
                _covering = false;
                return;
            }
            SetupPromptForScene(scene.name);
            if (_activeRoutine != null) StopCoroutine(_activeRoutine);
            _activeRoutine = StartCoroutine(PreloadAndRevealRoutine());
        }

        private void SetupPromptForScene(string sceneName)
        {
            bool menu = string.IsNullOrEmpty(sceneName) || sceneName.Contains("MainMenu");
            if (_initialBoot)
            {
                _pendingTitle = "LEADRUSH II";
                _pendingSub = "WARMING SHADERS & TACTICAL ASSETS...";
            }
            else if (menu)
            {
                _pendingTitle = "TACTICAL HQ";
                _pendingSub = "PREPARING COMMAND INTERFACE...";
            }
            else
            {
                string clean = sceneName.Replace("LR2_", "").ToUpperInvariant();
                _pendingTitle = "SECTOR // " + clean;
                _pendingSub = "PRELOADING MODELS, UNIFORMS & TEXTURES...";
            }
            if (_tipText != null) _tipText.text = Tips[UnityEngine.Random.Range(0, Tips.Length)];
        }

        private void ShowImmediate(float progress)
        {
            _covering = true;
            if (_group != null)
            {
                _group.alpha = 1f;
                _group.blocksRaycasts = true;
            }
            if (_titleText != null) _titleText.text = _pendingTitle;
            if (_statusText != null) _statusText.text = _pendingSub;
            if (_progressFill != null) _progressFill.fillAmount = Mathf.Clamp01(progress);
        }

        private IEnumerator PreloadAndRevealRoutine()
        {
            ShowImmediate(0.18f);
            float tStart = Time.unscaledTime;
            float minHold = _initialBoot ? 1.15f : 0.65f;
            string sceneName = SceneManager.GetActiveScene().name;
            bool isGameplay = !string.IsNullOrEmpty(sceneName) && !sceneName.Contains("MainMenu");

            // 1. Pre-warm shared audio libraries & UI sound sets
            if (_statusText != null) _statusText.text = "LOADING AUDIO BANKS & MUSIC STEMS...";
            _ = LR2CommonSounds.Instance;
            _ = SurfaceLibrary.Instance;
            _ = FoleyLibrary.Instance;
            SetProgress(0.32f);
            yield return null;

            // 2. Preload character/weapon textures cleanly without Shader.WarmupAllShaders() (unsupported on HDRP D3D12)
            // or global mip-0 VRAM spikes; active scene shaders compile organically while the loading curtain renders real frames.
            if (!_shadersWarmed)
            {
                if (_statusText != null) _statusText.text = "INITIALIZING PIPELINE STATES & GEAR...";
                _shadersWarmed = true;
            }
            SetProgress(0.52f);
            yield return null;

            if (_statusText != null) _statusText.text = "LOADING SOLDIER UNIFORMS, WEAPONS & RIGS...";
            if (isGameplay)
            {
                float waitSpawn = Time.unscaledTime + 3.0f;
                while (Combatant.FindLocalPlayer() == null && Time.unscaledTime < waitSpawn)
                    yield return null;
            }

            SoldierVisuals.ApplyAllNow();
            PreloadActiveCharacterAndWeaponTextures();
            foreach (var proxy in FindObjectsByType<RetargetProxy>())
                if (proxy != null) proxy.SyncPoseNow();

            SetProgress(0.78f);

            // Render hidden warmup frames so textures upload to VRAM and HDRP Progressive Exposure converges cleanly
            if (_statusText != null) _statusText.text = "FINALIZING LIGHTING & OPTICS...";
            for (int f = 0; f < 10; f++)
            {
                SoldierVisuals.ApplyAllNow();
                float p = Mathf.Lerp(0.78f, 0.96f, f / 9f);
                SetProgress(p);
                yield return null;
            }

            while (Time.unscaledTime - tStart < minHold)
            {
                float p = Mathf.Clamp01(0.78f + 0.22f * ((Time.unscaledTime - tStart) / minHold));
                SetProgress(p);
                yield return null;
            }
            SetProgress(1f);
            if (_statusText != null) _statusText.text = "DEPLOYMENT READY";
            yield return new WaitForSecondsRealtime(0.12f);

            _initialBoot = false;
            float fade = 0f;
            while (fade < 0.34f)
            {
                fade += Time.unscaledDeltaTime;
                if (_group != null) _group.alpha = 1f - Mathf.Clamp01(fade / 0.34f);
                yield return null;
            }

            if (_group != null)
            {
                _group.alpha = 0f;
                _group.blocksRaycasts = false;
            }
            _covering = false;
            _activeRoutine = null;
        }

        private void Update()
        {
            if (!_covering || _scanLine == null) return;
            float phase = Mathf.PingPong(Time.unscaledTime * 0.6f, 1f);
            _scanLine.anchorMin = new Vector2(0f, phase);
            _scanLine.anchorMax = new Vector2(1f, phase);
        }

        private void SetProgress(float v)
        {
            if (_progressFill != null) _progressFill.fillAmount = Mathf.Clamp01(v);
        }

        private static void PreloadActiveCharacterAndWeaponTextures()
        {
            foreach (var visuals in FindObjectsByType<SoldierVisuals>())
            {
                if (visuals == null) continue;
                foreach (var rend in visuals.GetComponentsInChildren<Renderer>(true))
                    PromoteRendererTexturesMip0(rend);
            }
            foreach (var firearm in FindObjectsByType<LR2.Weapons.LR2Firearm>())
            {
                if (firearm == null) continue;
                foreach (var rend in firearm.GetComponentsInChildren<Renderer>(true))
                    PromoteRendererTexturesMip0(rend);
            }
        }

        private static void PromoteRendererTexturesMip0(Renderer rend)
        {
            if (rend == null) return;
            var mats = rend.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var mat = mats[i];
                if (mat == null) continue;
                foreach (int propId in mat.GetTexturePropertyNameIDs())
                {
                    if (mat.GetTexture(propId) is Texture2D t2d)
                        t2d.requestedMipmapLevel = 0;
                }
            }
        }

        private void BuildOverlay()
        {
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 9999;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            gameObject.AddComponent<GraphicRaycaster>();
            _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 1f;
            _group.blocksRaycasts = true;
            _group.interactable = false;

            var bg = new GameObject("Background").AddComponent<Image>();
            bg.transform.SetParent(transform, false);
            bg.color = new Color(0.032f, 0.041f, 0.052f, 1f);
            Stretch(bg.rectTransform);

            // Subtle tactical vignette border accents
            var topLine = new GameObject("AccentTop").AddComponent<Image>();
            topLine.transform.SetParent(transform, false);
            topLine.color = new Color(0.89f, 0.62f, 0.18f, 0.9f);
            var tlRt = topLine.rectTransform;
            tlRt.anchorMin = new Vector2(0.28f, 0.58f);
            tlRt.anchorMax = new Vector2(0.72f, 0.58f);
            tlRt.sizeDelta = new Vector2(0f, 3f);

            var scanGo = new GameObject("ScanRule").AddComponent<Image>();
            scanGo.transform.SetParent(transform, false);
            scanGo.color = new Color(0.89f, 0.62f, 0.18f, 0.07f);
            _scanLine = scanGo.rectTransform;
            _scanLine.sizeDelta = new Vector2(0f, 2f);

            _titleText = MakeText("Title", new Vector2(0.5f, 0.635f), new Vector2(1200f, 96f), 58f,
                FontStyles.Bold, new Color(0.95f, 0.96f, 0.97f, 1f));
            _statusText = MakeText("Status", new Vector2(0.5f, 0.53f), new Vector2(1000f, 40f), 22f,
                FontStyles.Normal, new Color(0.89f, 0.64f, 0.22f, 0.95f));
            _tipText = MakeText("Tip", new Vector2(0.5f, 0.14f), new Vector2(1300f, 44f), 17f,
                FontStyles.Italic, new Color(0.62f, 0.68f, 0.74f, 0.85f));

            var barBg = new GameObject("BarBg").AddComponent<Image>();
            barBg.transform.SetParent(transform, false);
            barBg.color = new Color(0.14f, 0.17f, 0.21f, 0.95f);
            var bgRt = barBg.rectTransform;
            bgRt.anchorMin = new Vector2(0.32f, 0.46f);
            bgRt.anchorMax = new Vector2(0.68f, 0.46f);
            bgRt.sizeDelta = new Vector2(0f, 8f);

            var barFill = new GameObject("BarFill").AddComponent<Image>();
            barFill.transform.SetParent(barBg.transform, false);
            barFill.color = new Color(0.92f, 0.63f, 0.17f, 1f);
            barFill.type = Image.Type.Filled;
            barFill.fillMethod = Image.FillMethod.Horizontal;
            barFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            barFill.fillAmount = 0f;
            Stretch(barFill.rectTransform);
            _progressFill = barFill;
        }

        private TMP_Text MakeText(string name, Vector2 anchor, Vector2 size, float fontSize, FontStyles style, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var txt = go.AddComponent<TextMeshProUGUI>();
            txt.alignment = TextAlignmentOptions.Center;
            txt.fontSize = fontSize;
            txt.fontStyle = style;
            txt.color = color;
            txt.characterSpacing = 4f;
            var rt = txt.rectTransform;
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.sizeDelta = size;
            return txt;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
