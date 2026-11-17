using System.IO;
using LR2.Characters;
using LR2.Combat;
using LR2.Core;
using LR2.FX;
using LR2.Game;
using LR2.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace LR2.EditorTools
{
    /// <summary>
    /// Captures 16:9 hero preview thumbnails for every map in LR2Session.MapScenes into
    /// Assets/LR2/Resources/LR2/MapPreviews/ so the Main Menu can show live map previews.
    /// </summary>
    public static class LR2MapPreviewCapture
    {
        private const int PreviewWidth = 960;
        private const int PreviewHeight = 540;
        private const string OutputFolder = "Assets/LR2/Resources/LR2/MapPreviews";
        private const string StampVersion = "v1";
        private const string StampPath = "Library/LR2_GeminiUiStamp.txt";
        private const string ReportPath = "Library/LR2_GeminiBuildReport.txt";
        private static double _readyAt;

        [InitializeOnLoadMethod]
        private static void ScheduleUiAndPreviewCapture()
        {
            if (Application.isBatchMode) return;
            if (File.Exists(StampPath) && File.ReadAllText(StampPath).Trim() == StampVersion) return;
            // Delay slightly so Claude's LR2BuildPipeline auto-rebuild finishes first on domain reload
            _readyAt = EditorApplication.timeSinceStartup + 2.5;
            EditorApplication.update -= WaitThenCapture;
            EditorApplication.update += WaitThenCapture;
        }

        private static void WaitThenCapture()
        {
            if (Application.isBatchMode || EditorApplication.timeSinceStartup < _readyAt) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (File.Exists(LR2AgentBridge.CommandPath) || File.Exists(LR2AgentBridge.RobotFlag)) return;
            if (SessionState.GetBool("LR2GeminiVerify.Active", false)) return;
            if (SceneManager.GetActiveScene().isDirty) return; // Never discard user's unsaved scene edits

            EditorApplication.update -= WaitThenCapture;
            if (File.Exists(StampPath) && File.ReadAllText(StampPath).Trim() == StampVersion) return;
            File.WriteAllText(StampPath, StampVersion);

            bool uiOk = false;
            int count = 0;
            try
            {
                LR2UIBuilder.Build();
                uiOk = true;
                count = CaptureAll(restorePreviousScene: true);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }
            File.WriteAllText(ReportPath, $"version={StampVersion}\nutc={System.DateTime.UtcNow:O}\nui={uiOk}\npreviews={count}/{LR2Session.MapScenes.Length}\n");
            Debug.Log($"[LR2] Gemini {StampVersion} UI + Map Previews: ui={uiOk}, previews={count}/{LR2Session.MapScenes.Length}");
        }

        [MenuItem("LR2/UI/Capture Map Previews", priority = 220)]
        public static void CaptureAllInteractive()
        {
            if (Application.isPlaying)
            {
                Debug.LogWarning("[LR2] Exit Play mode before capturing map previews.");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            CaptureAll(restorePreviousScene: true);
        }

        public static void CaptureAllBatch() => CaptureAll(restorePreviousScene: false);

        public static int CaptureAll(bool restorePreviousScene)
        {
            EnsureOutputDirectory();
            string prevScenePath = SceneManager.GetActiveScene().path;
            int captured = 0;

            try
            {
                for (int i = 0; i < LR2Session.MapScenes.Length; i++)
                {
                    string sceneName = LR2Session.MapScenes[i];
                    string scenePath = $"Assets/LR2/Scenes/{sceneName}.unity";
                    if (!File.Exists(scenePath))
                    {
                        Debug.LogWarning($"[LR2MapPreviewCapture] Missing scene at {scenePath}");
                        continue;
                    }

                    EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                    string outPath = $"{OutputFolder}/Preview_{sceneName}.png";
                    if (CaptureCurrentScene(sceneName, outPath))
                    {
                        captured++;
                        Debug.Log($"[LR2MapPreviewCapture] Captured {outPath}");
                    }
                }
            }
            finally
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                ConfigureSpriteImporters();
                if (restorePreviousScene && !string.IsNullOrEmpty(prevScenePath) && File.Exists(prevScenePath))
                    EditorSceneManager.OpenScene(prevScenePath, OpenSceneMode.Single);
            }

            Debug.Log($"[LR2MapPreviewCapture] Completed {captured}/{LR2Session.MapScenes.Length} map preview captures.");
            return captured;
        }

        private static void EnsureOutputDirectory()
        {
            Directory.CreateDirectory(OutputFolder);
        }

        private static bool CaptureCurrentScene(string sceneName, string assetPath)
        {
            Bounds arena = ComputeArenaBounds(out Vector3[] spawnPositions, out float floorY);
            Vector3 lookTarget = new Vector3(arena.center.x, floorY + 3.0f, arena.center.z);
            float baseRadius = Mathf.Clamp(Mathf.Max(arena.extents.x, arena.extents.z) * 0.68f, 24f, 44f);

            Light sun = FindSunLight();
            Vector3 sunDir = sun != null ? -sun.transform.forward : new Vector3(0.5f, 0.7f, 0.4f).normalized;
            Vector3 flatSun = new Vector3(sunDir.x, 0f, sunDir.z);
            float sunCamAz = flatSun.sqrMagnitude > 0.01f
                ? Mathf.Atan2(-flatSun.x, -flatSun.z) * Mathf.Rad2Deg
                : GetPreferredAzimuth(sceneName);

            Vector3 bestCamPos = lookTarget + Quaternion.Euler(0f, sunCamAz, 0f) * new Vector3(0f, 6.8f, -Mathf.Clamp(baseRadius * 1.15f, 28f, 44f));
            float bestScore = float.MinValue;

            // Gentle ~7-13 deg downward pitch from outer arena perimeter so sky, skyline, and cover all frame together
            float[] heights = { 5.8f, 8.2f };
            float[] radii = { Mathf.Clamp(baseRadius * 1.05f, 26f, 42f), Mathf.Clamp(baseRadius * 1.32f, 34f, 52f) };
            float[] azOffsets = { 0f, 22f, -22f, 45f, -45f, 75f, -75f, 110f, -110f, 145f, -145f, 180f };

            foreach (float radius in radii)
            {
                foreach (float height in heights)
                {
                    foreach (float off in azOffsets)
                    {
                        float az = sunCamAz + off;
                        Vector3 horizDir = Quaternion.Euler(0f, az, 0f) * Vector3.back;
                        Vector3 candidate = new Vector3(lookTarget.x, floorY, lookTarget.z) + horizDir * radius + Vector3.up * height;

                        if (Physics.CheckSphere(candidate, 1.8f, ~0, QueryTriggerInteraction.Ignore)) continue;
                        Vector3 viewDir = (lookTarget - candidate).normalized;
                        if (Physics.SphereCast(candidate, 0.85f, viewDir, out _, 10.5f, ~0, QueryTriggerInteraction.Ignore))
                            continue;

                        int visibleSpawns = 0;
                        foreach (var sp in spawnPositions)
                        {
                            Vector3 spEye = sp + Vector3.up * 1.6f;
                            if (!Physics.Linecast(candidate, spEye, ~0, QueryTriggerInteraction.Ignore))
                                visibleSpawns++;
                        }

                        float sunAlign = Vector3.Dot(-viewDir, sunDir);
                        float heightPref = 1f - Mathf.Abs(height - 7.0f) / 5f;
                        float score = visibleSpawns * 9f + sunAlign * 14f + heightPref * 4f;

                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestCamPos = candidate;
                        }
                    }
                }
            }

            if (bestScore <= float.MinValue * 0.5f)
            {
                for (int k = 0; k < 16; k++)
                {
                    float az = sunCamAz + k * 22.5f;
                    Vector3 horizDir = Quaternion.Euler(0f, az, 0f) * Vector3.back;
                    Vector3 candidate = new Vector3(lookTarget.x, floorY, lookTarget.z) + horizDir * Mathf.Clamp(baseRadius * 0.9f, 22f, 36f) + Vector3.up * 7.8f;
                    if (!Physics.CheckSphere(candidate, 1.6f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        bestCamPos = candidate;
                        break;
                    }
                }
            }

            var camGo = new GameObject("[LR2_PreviewCaptureCamera]");
            camGo.hideFlags = HideFlags.HideAndDontSave;
            camGo.transform.position = bestCamPos;
            camGo.transform.LookAt(lookTarget, Vector3.up);

            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 52f;
            cam.nearClipPlane = 0.25f;
            cam.farClipPlane = 650f;
            cam.clearFlags = CameraClearFlags.Skybox;

            var hd = camGo.AddComponent<HDAdditionalCameraData>();
            hd.clearColorMode = HDAdditionalCameraData.ClearColorMode.Sky;
            hd.volumeLayerMask = ~0;
            hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;

            var rt = new RenderTexture(PreviewWidth, PreviewHeight, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.antiAliasing = 1;
            rt.Create();
            cam.targetTexture = rt;

            Volume fallbackVol = null;
            Texture2D tex = new Texture2D(PreviewWidth, PreviewHeight, TextureFormat.RGB24, false, false);

            try
            {
                // Render warmup frames so HDRP histogram exposure & volumetric clouds settle
                for (int w = 0; w < 4; w++) cam.Render();

                RenderTexture prev = RenderTexture.active;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, PreviewWidth, PreviewHeight), 0, 0, false);
                tex.Apply(false, false);

                float avgLum = SampleAverageLuminance(tex);
                if (avgLum < 0.045f || avgLum > 0.92f)
                {
                    // Fallback fixed exposure only when scene adaptation did not settle in EditMode
                    var volGo = new GameObject("[LR2_PreviewExposure]");
                    volGo.hideFlags = HideFlags.HideAndDontSave;
                    fallbackVol = volGo.AddComponent<Volume>();
                    fallbackVol.isGlobal = true;
                    fallbackVol.priority = 5000f;
                    var profile = ScriptableObject.CreateInstance<VolumeProfile>();
                    profile.hideFlags = HideFlags.HideAndDontSave;
                    var exp = profile.Add<Exposure>(true);
                    exp.mode.Override(ExposureMode.Fixed);
                    exp.fixedExposure.Override(avgLum < 0.045f ? 10.2f : 12.6f);
                    fallbackVol.sharedProfile = profile;

                    cam.Render();
                    cam.Render();
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, PreviewWidth, PreviewHeight), 0, 0, false);
                    tex.Apply(false, false);
                }

                RenderTexture.active = prev;
                File.WriteAllBytes(assetPath, tex.EncodeToPNG());
                return true;
            }
            finally
            {
                cam.targetTexture = null;
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(tex);
                Object.DestroyImmediate(camGo);
                if (fallbackVol != null)
                {
                    if (fallbackVol.sharedProfile != null) Object.DestroyImmediate(fallbackVol.sharedProfile);
                    Object.DestroyImmediate(fallbackVol.gameObject);
                }
            }
        }

        private static Bounds ComputeArenaBounds(out Vector3[] spawnPositions, out float floorY)
        {
            var spawns = Object.FindObjectsByType<SpawnPoint>();
            if (spawns != null && spawns.Length > 0)
            {
                var posList = new System.Collections.Generic.List<Vector3>(spawns.Length);
                Bounds b = new Bounds(spawns[0].transform.position, Vector3.zero);
                double sumY = 0.0;
                foreach (var sp in spawns)
                {
                    if (sp == null) continue;
                    b.Encapsulate(sp.transform.position);
                    posList.Add(sp.transform.position);
                    sumY += sp.transform.position.y;
                }
                spawnPositions = posList.ToArray();
                floorY = posList.Count > 0 ? (float)(sumY / posList.Count) : 0f;
                if (b.size.sqrMagnitude > 4f)
                {
                    b.Expand(new Vector3(12f, 6f, 12f));
                    return b;
                }
            }
            else
            {
                spawnPositions = System.Array.Empty<Vector3>();
                floorY = 0f;
            }

            var renderers = Object.FindObjectsByType<MeshRenderer>();
            Bounds rb = new Bounds(Vector3.zero, Vector3.one * 30f);
            bool has = false;
            foreach (var r in renderers)
            {
                if (r == null || !r.enabled) continue;
                Vector3 ext = r.bounds.extents;
                if (ext.x > 140f || ext.z > 140f) continue; // ignore giant ground planes/skirts
                if (!has) { rb = r.bounds; has = true; }
                else rb.Encapsulate(r.bounds);
            }
            if (spawnPositions.Length == 0 && has) floorY = rb.min.y;
            return rb;
        }

        private static Light FindSunLight()
        {
            var lights = Object.FindObjectsByType<Light>();
            Light best = null;
            float maxIntensity = -1f;
            foreach (var l in lights)
            {
                if (l == null || !l.enabled || l.type != LightType.Directional) continue;
                if (l.intensity > maxIntensity)
                {
                    maxIntensity = l.intensity;
                    best = l;
                }
            }
            return best;
        }

        private static float GetPreferredAzimuth(string sceneName)
        {
            switch (sceneName)
            {
                case "LR2_Saltworks": return 35f;
                case "LR2_Deadsector": return -42f;
                case "LR2_Foundry": return 128f;
                case "LR2_TestRange": return 25f;
                default: return 35f;
            }
        }

        private static float SampleAverageLuminance(Texture2D tex)
        {
            Color32[] pixels = tex.GetPixels32();
            if (pixels == null || pixels.Length == 0) return 0.5f;
            int step = Mathf.Max(1, pixels.Length / 512);
            double sum = 0.0;
            int count = 0;
            for (int i = 0; i < pixels.Length; i += step)
            {
                Color c = pixels[i];
                sum += 0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b;
                count++;
            }
            return count > 0 ? (float)(sum / count) : 0.5f;
        }

        private static void ConfigureSpriteImporters()
        {
            foreach (string sceneName in LR2Session.MapScenes)
            {
                string path = $"{OutputFolder}/Preview_{sceneName}.png";
                if (!File.Exists(path)) continue;
                var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti == null) continue;
                bool changed = ti.textureType != TextureImporterType.Sprite
                    || ti.spriteImportMode != SpriteImportMode.Single
                    || ti.mipmapEnabled
                    || ti.sRGBTexture == false
                    || ti.wrapMode != TextureWrapMode.Clamp;
                if (!changed) continue;

                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.sRGBTexture = true;
                ti.mipmapEnabled = false;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.filterMode = FilterMode.Bilinear;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.maxTextureSize = 1024;
                ti.SaveAndReimport();
            }
        }
    }

    /// <summary>
    /// End-to-end visible PlayMode verifier for Gemini deliverables: Main Menu + map thumbnails,
    /// Mode Rules persistence into MatchManager, two-column Settings, and GunBloodFX + HUD reload bar in Saltworks.
    /// </summary>
    [InitializeOnLoad]
    public static class LR2GeminiVerifyRunner
    {
        private const string KeyActive = "LR2GeminiVerify.Active";
        private const string KeyStage = "LR2GeminiVerify.Stage";
        private const string KeyAt = "LR2GeminiVerify.At";
        private const string KeyErrors = "LR2GeminiVerify.Errors";
        private const string KeyLog = "LR2GeminiVerify.Log";
        private const string KeyDeadline = "LR2GeminiVerify.Deadline";
        private const string VerifyDir = "Logs/LR2_GeminiVerify";

        static LR2GeminiVerifyRunner()
        {
            if (!SessionState.GetBool(KeyActive, false)) return;
            EditorApplication.update += Tick;
            Application.logMessageReceived += OnLog;
        }

        [MenuItem("LR2/UI/Run E2E Verification (Visible Play)", priority = 222)]
        public static void RunInteractive()
        {
            if (EditorApplication.isPlaying) return;
            StartVerify();
        }

        public static void RunVisibleAutoExit() => StartVerify();

        private static void StartVerify()
        {
            Directory.CreateDirectory(VerifyDir);
            foreach (var f in Directory.GetFiles(VerifyDir, "*.png")) File.Delete(f);

            SessionState.SetBool(KeyActive, true);
            SessionState.SetInt(KeyStage, 0);
            SessionState.SetFloat(KeyAt, 0f);
            SessionState.SetInt(KeyErrors, 0);
            SessionState.SetString(KeyLog, "");
            SessionState.SetFloat(KeyDeadline, (float)EditorApplication.timeSinceStartup + 28f);

            LR2UIBuilder.Build();
            EditorSceneManager.OpenScene("Assets/LR2/Scenes/LR2_MainMenu.unity", OpenSceneMode.Single);
            EditorApplication.ExecuteMenuItem("Window/General/Game");

            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
            EditorApplication.isPlaying = true;
        }

        private static void OnLog(string msg, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            SessionState.SetInt(KeyErrors, SessionState.GetInt(KeyErrors, 0) + 1);
            AppendNote($"{type}: {msg}");
        }

        private static void Tick()
        {
            if (!SessionState.GetBool(KeyActive, false) || !EditorApplication.isPlaying) return;

            int stage = SessionState.GetInt(KeyStage, 0);
            float nextAt = SessionState.GetFloat(KeyAt, 0f);
            float now = (float)EditorApplication.timeSinceStartup;
            float deadline = SessionState.GetFloat(KeyDeadline, 0f);
            if (deadline > 0f && now > deadline)
            {
                AppendNote($"Verify timed out at stage {stage}");
                FinishVerify();
                return;
            }
            if (nextAt <= 0f)
            {
                SessionState.SetFloat(KeyAt, now + 0.8f);
                return;
            }
            if (now < nextAt) return;

            switch (stage)
            {
                case 0:
                {
                    var mm = Object.FindAnyObjectByType<MainMenuController>();
                    if (mm == null)
                    {
                        SessionState.SetFloat(KeyAt, now + 0.5f);
                        return;
                    }
                    mm.SelectMap(0); // Saltworks
                    bool hasSprite = mm.mapPreviewImage != null && mm.mapPreviewImage.sprite != null;
                    AppendNote($"MainMenu previewImage.sprite={(hasSprite ? mm.mapPreviewImage.sprite.name : "NULL")}");
                    ScreenCapture.CaptureScreenshot(Path.Combine(VerifyDir, "01_menu_main.png"));
                    SessionState.SetInt(KeyStage, 1);
                    SessionState.SetFloat(KeyAt, now + 0.7f);
                    break;
                }
                case 1:
                {
                    var mm = Object.FindAnyObjectByType<MainMenuController>();
                    if (mm != null && mm.modeRulesButton != null)
                    {
                        mm.modeRulesButton.onClick.Invoke();
                        if (mm.modeRulesTdmTab != null) mm.modeRulesTdmTab.onClick.Invoke();
                        if (mm.rulesScoreLimitSlider != null) mm.rulesScoreLimitSlider.value = 50f;
                        if (mm.rulesCountdownSlider != null) mm.rulesCountdownSlider.value = 1f;
                        AppendNote($"ModeRules TDM custom={LR2Session.HasCustomModeRules(GameModeType.TeamDeathmatch)} score={(mm.rulesScoreLimitSlider != null ? mm.rulesScoreLimitSlider.value : -1)} cd={(mm.rulesCountdownSlider != null ? mm.rulesCountdownSlider.value : -1)}");
                    }
                    ScreenCapture.CaptureScreenshot(Path.Combine(VerifyDir, "02_menu_mode_rules.png"));
                    SessionState.SetInt(KeyStage, 2);
                    SessionState.SetFloat(KeyAt, now + 0.7f);
                    break;
                }
                case 2:
                {
                    var mm = Object.FindAnyObjectByType<MainMenuController>();
                    if (mm != null)
                    {
                        if (mm.modeRulesBackButton != null) mm.modeRulesBackButton.onClick.Invoke();
                        if (mm.settingsButton != null) mm.settingsButton.onClick.Invoke();
                        var sp = Object.FindAnyObjectByType<SettingsPanel>();
                        AppendNote($"SettingsPanel uiVolume={(sp != null && sp.uiVolume != null ? "OK" : "MISSING")}");
                    }
                    ScreenCapture.CaptureScreenshot(Path.Combine(VerifyDir, "03_menu_settings.png"));
                    SessionState.SetInt(KeyStage, 3);
                    SessionState.SetFloat(KeyAt, now + 0.7f);
                    break;
                }
                case 3:
                {
                    var mm = Object.FindAnyObjectByType<MainMenuController>();
                    if (mm != null)
                    {
                        if (mm.settingsBackButton != null) mm.settingsBackButton.onClick.Invoke();
                        mm.Play(mm.tdmSettings);
                        AppendNote("Clicked Play(tdmSettings) -> deploying to LR2_Saltworks");
                    }
                    SessionState.SetInt(KeyStage, 4);
                    SessionState.SetFloat(KeyAt, now + 2.2f);
                    break;
                }
                case 4:
                {
                    var match = MatchManager.Instance;
                    var player = Combatant.FindLocalPlayer();
                    if (match == null || player == null || match.State != MatchState.Playing)
                    {
                        SessionState.SetFloat(KeyAt, now + 0.35f);
                        return;
                    }
                    AppendNote($"Saltworks Match State={match.State} ScoreLimit={match.ScoreLimit} (expected 50), GunBloodFX={(GunBloodFX.Instance != null ? "ACTIVE" : "NULL")}");

                    var soldier = player.GetComponent<SoldierController>();
                    if (soldier != null && soldier.ActiveFirearm != null)
                    {
                        soldier.ActiveFirearm.OnFirePressed();
                        AppendNote($"Firing {soldier.ActiveFirearm.DisplayName} to consume round...");
                    }

                    SessionState.SetInt(KeyStage, 5);
                    SessionState.SetFloat(KeyAt, now + 0.25f);
                    break;
                }
                case 5:
                {
                    var match = MatchManager.Instance;
                    var player = Combatant.FindLocalPlayer();
                    var soldier = player != null ? player.GetComponent<SoldierController>() : null;
                    if (soldier != null && soldier.ActiveFirearm != null)
                    {
                        soldier.ActiveFirearm.OnFireReleased();
                        soldier.RequestReload();
                        AppendNote($"Reloading {soldier.ActiveFirearm.DisplayName}: IsReloading={soldier.ActiveFirearm.IsReloading}");
                    }

                    if (player != null && player.Health != null)
                    {
                        player.Health.CancelSpawnProtection();
                        var info = new DamageInfo
                        {
                            amount = 38f,
                            type = DamageType.Bullet,
                            attacker = null,
                            point = player.ChestPosition,
                            normal = Vector3.right,
                            direction = -Vector3.right,
                            force = 2f,
                        };
                        float dmg = player.Health.ApplyDamage(ref info);
                        AppendNote($"Applied damage={dmg:0} -> HP={player.Health.Current:0}, GunDrops={(GunBloodFX.Instance != null ? GunBloodFX.Instance.ActiveGunDropletCount : -1)}");
                    }

                    var hud = Object.FindAnyObjectByType<HUDController>();
                    if (hud != null)
                    {
                        string dmgSprite = hud.damageIndicatorTemplate != null && hud.damageIndicatorTemplate.sprite != null ? hud.damageIndicatorTemplate.sprite.name : "NULL";
                        AppendNote($"HUD DamageArc.sprite={dmgSprite}");
                    }

                    SessionState.SetInt(KeyStage, 6);
                    SessionState.SetFloat(KeyAt, now + 0.4f);
                    break;
                }
                case 6:
                {
                    ScreenCapture.CaptureScreenshot(Path.Combine(VerifyDir, "04_gameplay_blood_hud.png"));
                    CaptureCamFallback(Path.Combine(VerifyDir, "05_gameplay_cam.png"));
                    SessionState.SetInt(KeyStage, 7);
                    SessionState.SetFloat(KeyAt, now + 0.6f);
                    break;
                }
                default:
                {
                    FinishVerify();
                    break;
                }
            }
        }

        private static void AppendNote(string line)
        {
            string cur = SessionState.GetString(KeyLog, "");
            SessionState.SetString(KeyLog, cur + $"[{System.DateTime.Now:HH:mm:ss}] {line}\n");
            Debug.Log("[LR2 GeminiVerify] " + line);
        }

        private static void CaptureCamFallback(string path)
        {
            var player = Combatant.FindLocalPlayer();
            var soldier = player != null ? player.GetComponent<SoldierController>() : null;
            var cam = soldier != null && soldier.ViewCamera != null && soldier.ViewCamera.isActiveAndEnabled ? soldier.ViewCamera : Camera.main;
            if (cam == null) return;
            var rt = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var hd = cam.GetComponent<HDAdditionalCameraData>();
            var prevAa = hd != null ? hd.antialiasing : HDAdditionalCameraData.AntialiasingMode.None;
            try
            {
                if (hd != null) hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
                var req = new RenderPipeline.StandardRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(cam, req))
                {
                    RenderPipeline.SubmitRenderRequest(cam, req);
                    RenderPipeline.SubmitRenderRequest(cam, req);
                }
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.Destroy(tex);
            }
            finally
            {
                if (hd != null) hd.antialiasing = prevAa;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        private static void FinishVerify()
        {
            LR2Session.ResetModeRules(GameModeType.TeamDeathmatch);
            int errs = SessionState.GetInt(KeyErrors, 0);
            string fullLog = SessionState.GetString(KeyLog, "") + $"Errors={errs}\n";
            File.WriteAllText(Path.Combine(VerifyDir, "report.txt"), fullLog);

            SessionState.SetBool(KeyActive, false);
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;

            bool autoExit = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-lr2AutoExit") >= 0;
            if (autoExit)
            {
                EditorApplication.playModeStateChanged += state =>
                {
                    if (state == PlayModeStateChange.EnteredEditMode)
                        EditorApplication.Exit(errs == 0 ? 0 : 2);
                };
            }
            EditorApplication.isPlaying = false;
        }
    }
}
