using System.Collections.Generic;
using System.IO;
using System.Text;
using LR2.AI;
using LR2.Characters;
using LR2.Combat;
using LR2.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LR2.EditorTools
{
    /// <summary>
    /// Headless play-mode smoke test: opens Saltworks, plays ~25 s (bot vs idle player), then writes a report
    /// (spawns, camera, shots, kills, bot goals, runtime errors) to Logs/LR2_SmokeTest.txt.
    /// Batch: -executeMethod LR2.EditorTools.LR2SmokeTest.RunBatch
    /// </summary>
    [InitializeOnLoad]
    public static class LR2SmokeTest
    {
        private const string KeyActive = "LR2Smoke.Active", KeyStart = "LR2Smoke.Start", KeyDone = "LR2Smoke.Done", KeyErrors = "LR2Smoke.Errors";
        private const float Duration = 50f;
        private static readonly List<string> Messages = new List<string>();

        private const string KeyDuration = "LR2Smoke.Duration", KeyScene = "LR2Smoke.Scene";

        /// <summary>Started by LR2AgentBridge inside the user's open editor (robot flag file is already written).</summary>
        public static void RunFromBridge(string scenePath, float seconds)
        {
            SessionState.SetFloat(KeyDuration, seconds);
            SessionState.SetString(KeyScene, scenePath);
            Begin();
        }

        private static float GetDuration()
        {
            float bridge = SessionState.GetFloat(KeyDuration, 0f);
            if (bridge > 2f) return bridge;
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-lr2Duration" && float.TryParse(args[i + 1], out float d) && d > 2f) return d;
            return Duration;
        }

        static LR2SmokeTest()
        {
            if (!SessionState.GetBool(KeyActive, false)) return;
            EditorApplication.update += Tick;
            Application.logMessageReceived += OnLog;
            EditorApplication.playModeStateChanged += OnPlayMode;
        }

        [MenuItem("LR2/Run Smoke Test (25s play)", priority = 60)]
        public static void RunInteractive() => Begin();

        public static void RunBatch() => Begin();

        private static void Begin()
        {
            string scenePath = LR2MapBuilder.ScenePath;
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-lr2Scene" && File.Exists(args[i + 1])) scenePath = args[i + 1];
            string bridgeScene = SessionState.GetString(KeyScene, "");
            if (!string.IsNullOrEmpty(bridgeScene) && File.Exists(bridgeScene)) scenePath = bridgeScene;

            SessionState.SetBool(KeyActive, true);
            SessionState.SetBool(KeyDone, false);
            SessionState.SetInt(KeyErrors, 0);
            SessionState.SetFloat(KeyStart, -1f);
            EditorSceneManager.OpenScene(scenePath);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorApplication.isPlaying = true;
        }

        private static void OnLog(string msg, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            SessionState.SetInt(KeyErrors, SessionState.GetInt(KeyErrors, 0) + 1);
            if (Messages.Count < 40)
            {
                string first = string.IsNullOrEmpty(stack) ? "" : stack.Split('\n')[0];
                Messages.Add($"{type}: {msg}\n    at {first}");
            }
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying || SessionState.GetBool(KeyDone, false)) return;
            float start = SessionState.GetFloat(KeyStart, -1f);
            if (start < 0f)
            {
                SessionState.SetFloat(KeyStart, (float)EditorApplication.timeSinceStartup);
                return;
            }
            if (EditorApplication.timeSinceStartup - start < GetDuration()) return;
            WriteReport();
            SessionState.SetBool(KeyDone, true);
            EditorApplication.isPlaying = false;
        }

        private static void OnPlayMode(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode) return;
            bool done = SessionState.GetBool(KeyDone, false);
            bool active = SessionState.GetBool(KeyActive, false);
            if (!done && !active) return;

            int errors = SessionState.GetInt(KeyErrors, 0);
            SessionState.SetBool(KeyActive, false);
            // bridge run finished: clear overrides + robot flag so normal Play is not hijacked
            SessionState.EraseFloat(KeyDuration);
            SessionState.EraseString(KeyScene);
            if (File.Exists(LR2AgentBridge.RobotFlag)) File.Delete(LR2AgentBridge.RobotFlag);
            File.AppendAllText(LR2AgentBridge.ResultPath, $"[{System.DateTime.Now:HH:mm:ss}] play finished, errors={SessionState.GetInt(KeyErrors, 0)}\n");
            SessionState.SetBool(KeyDone, false);
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            if (!done) return;
            // Restore MainMenu as the active scene so opening Unity later lands on the menu.
            if (Application.isBatchMode)
            {
                if (File.Exists(LR2MapBuilder.MenuScenePath))
                {
                    string setupPath = Path.Combine(Directory.GetCurrentDirectory(), "Library", "LastSceneManagerSetup.txt");
                    File.WriteAllText(setupPath,
                        "sceneSetups:\n- path: " + LR2MapBuilder.MenuScenePath.Replace('\\', '/') + "\n  isLoaded: 1\n  isActive: 1\n  isSubScene: 0\n");
                }
                EditorApplication.delayCall += () => EditorApplication.Exit(errors == 0 ? 0 : 2);
            }
            else if (File.Exists(LR2MapBuilder.MenuScenePath))
            {
                EditorSceneManager.OpenScene(LR2MapBuilder.MenuScenePath, OpenSceneMode.Single);
            }
        }

        private static void WriteReport()
        {
            var sb = new StringBuilder();
            var mm = MatchManager.Instance;
            sb.AppendLine("=== LR2 SMOKE TEST ===");
            sb.AppendLine($"MatchManager: {(mm != null ? mm.State.ToString() : "MISSING")}  mode={(mm != null && mm.Settings != null ? mm.Settings.displayName : "-")}  timeLeft={(mm != null ? mm.TimeRemaining : 0f):0}");
            var cam = Camera.main;
            sb.AppendLine($"Camera.main: {(cam != null ? cam.name + " active=" + cam.isActiveAndEnabled + " pos=" + cam.transform.position : "NONE")}");
            foreach (var c in Combatant.All)
            {
                if (c == null) continue;
                var s = c.GetComponent<SoldierController>();
                var bot = c.GetComponent<BotController>();
                string gun = s != null && s.ActiveFirearm != null ? $"{s.ActiveFirearm.DisplayName} {s.ActiveFirearm.AmmoInMagazine}/{s.ActiveFirearm.ReserveAmmo}" : "-";
                sb.AppendLine($"- {c.DisplayName} team={c.Team} local={c.IsLocalPlayer} alive={c.IsAlive} hp={(c.Health != null ? c.Health.Current : 0):0} pos={c.Position} " +
                              $"K/D={c.stats.kills}/{c.stats.deaths} shots={c.stats.shotsFired} hits={c.stats.shotsHit} gun={gun} " +
                              $"{(bot != null && bot.enabled ? "goal=" + bot.CurrentGoalName + " target=" + (bot.CurrentTarget != null ? bot.CurrentTarget.DisplayName : "none") : "")}");
            }
            int errors = SessionState.GetInt(KeyErrors, 0);
            sb.AppendLine($"Runtime errors/exceptions: {errors}");
            foreach (var m in Messages) sb.AppendLine(m);
            string path = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "LR2_SmokeTest.txt");
            File.WriteAllText(path, sb.ToString());
            Debug.Log(sb.ToString());
        }
    }
}
