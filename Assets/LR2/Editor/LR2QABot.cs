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
    public static class LR2QABot
    {
        public static void RunBatch()
        {
            AssetDatabase.DisallowAutoRefresh(); // R12: never recompile mid-PlayMode run
            string scenePath = LR2MapBuilder.ScenePath;
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-lr2Scene" && File.Exists(args[i + 1])) scenePath = args[i + 1];

            EditorSceneManager.playModeStartScene = null; // QA bot targets specific scenes directly
            EditorSceneManager.OpenScene(scenePath);
            EditorApplication.isPlaying = true;
        }
    }
}
