using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LR2.EditorTools
{
    /// <summary>
    /// Batch entry point for <see cref="LR2.World.LR2QABotRunner"/>: opens the requested <c>-lr2Scene</c>
    /// (default <c>LR2_Saltworks.unity</c>) and enters PlayMode for automated visual, audio &amp; mechanics QA.
    /// </summary>
    [InitializeOnLoad]
    public static class LR2QABot
    {
        private const string KeyActive = "LR2QABot.Active";
        private const string KeyDone = "LR2QABot.Done";
        private const string KeyExitCode = "LR2QABot.ExitCode";

        static LR2QABot()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        public static void RunBatch()
        {
            AssetDatabase.DisallowAutoRefresh(); // R12: never recompile mid-PlayMode run
            string scenePath = LR2MapBuilder.ScenePath;
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-lr2Scene" && File.Exists(args[i + 1])) scenePath = args[i + 1];

            SessionState.SetBool(KeyActive, true);
            SessionState.SetBool(KeyDone, false);
            SessionState.SetInt(KeyExitCode, 0);
            EditorSceneManager.playModeStartScene = null; // QA bot targets specific scenes directly
            EditorSceneManager.OpenScene(scenePath);
            EditorApplication.isPlaying = true;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode) return;
            if (!SessionState.GetBool(KeyActive, false) || !SessionState.GetBool(KeyDone, false)) return;

            int exitCode = SessionState.GetInt(KeyExitCode, 0);
            SessionState.SetBool(KeyActive, false);
            SessionState.SetBool(KeyDone, false);

            if (File.Exists(LR2MapBuilder.MenuScenePath))
            {
                string setupPath = Path.Combine(Directory.GetCurrentDirectory(), "Library", "LastSceneManagerSetup.txt");
                File.WriteAllText(setupPath,
                    "sceneSetups:\n- path: " + LR2MapBuilder.MenuScenePath.Replace('\\', '/') + "\n  isLoaded: 1\n  isActive: 1\n  isSubScene: 0\n");
            }
            EditorApplication.delayCall += () => EditorApplication.Exit(exitCode);
        }
    }
}
