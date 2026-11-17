using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LR2.EditorTools
{
    /// <summary>One-click (or batch-mode) build of all LeadRush 2 content. Menu: LR2 > Build Everything.</summary>
    public static class LR2BuildPipeline
    {
        private const string AutoBuildVersion = "v108"; // Claude: Kinemation driver + SWAT visual proxy, gear
        private const string StampPath = "Library/LR2_AutoBuildStamp.txt";
        private const string ReportPath = "Library/LR2_AutoBuildReport.txt";

        [InitializeOnLoadMethod]
        private static void ScheduleAutoRebuildOnScriptReload()
        {
            if (Application.isBatchMode) return;
            if (File.Exists(StampPath) && File.ReadAllText(StampPath).Trim() == AutoBuildVersion) return;
            EditorApplication.delayCall += RunAutoRebuildIfReady;
        }

        private static void RunAutoRebuildIfReady()
        {
            if (Application.isBatchMode) return;
            if (File.Exists(StampPath) && File.ReadAllText(StampPath).Trim() == AutoBuildVersion) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += RunAutoRebuildIfReady;
                return;
            }

            File.WriteAllText(StampPath, AutoBuildVersion);
            bool ok = true;
            ok &= Step("FX", LR2ContentBuilder.BuildFx);
            ok &= Step("Audio", LR2ContentBuilder.BuildAudio);
            ok &= Step("Weapons", LR2ContentBuilder.BuildWeapons);
            ok &= Step("Soldiers", LR2ContentBuilder.BuildSoldiers);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string gripInfo = File.Exists("Library/LR2_GripCalibration.txt") ? File.ReadAllText("Library/LR2_GripCalibration.txt") : "n/a";
            File.WriteAllText(ReportPath,
                $"version={AutoBuildVersion}\nutc={DateTime.UtcNow:O}\nok={ok}\ngrip={gripInfo}\n");
            Debug.Log($"[LR2] Auto-rebuild {AutoBuildVersion}: {(ok ? "SUCCESS" : "FAILED")} ({gripInfo.Trim()})");
        }

        [MenuItem("LR2/Build Everything", priority = 0)]
        public static void BuildEverything() => Run(true);

        [MenuItem("LR2/Build All Maps Only", priority = 20)]
        public static void BuildMapOnly()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Step("Map: Saltworks", LR2MapBuilder.Build);
            Step("Map: Deadsector", LR2MapBuilder.BuildDeadsector);
            Step("Map: Foundry", LR2MapBuilder.BuildFoundry);
            Step("Map: Test Range", LR2MapBuilder.BuildTestRange);
        }

        [MenuItem("LR2/Build Menu Scene Only", priority = 21)]
        public static void BuildMenuOnly() => Step("Menu scene", LR2MapBuilder.BuildMenuScene);

        [MenuItem("LR2/Open Saltworks", priority = 40)]
        public static void OpenMap() => EditorSceneManager.OpenScene(LR2MapBuilder.ScenePath);

        [MenuItem("LR2/Open Deadsector", priority = 41)]
        public static void OpenDeadsector() => EditorSceneManager.OpenScene(LR2MapBuilder.DeadsectorScenePath);

        [MenuItem("LR2/Open Foundry", priority = 42)]
        public static void OpenFoundry() => EditorSceneManager.OpenScene(LR2MapBuilder.FoundryScenePath);

        [MenuItem("LR2/Open Test Range", priority = 43)]
        public static void OpenTestRange() => EditorSceneManager.OpenScene(LR2MapBuilder.TestRangeScenePath);

        [MenuItem("LR2/Open Main Menu", priority = 45)]
        public static void OpenMenu() => EditorSceneManager.OpenScene(LR2MapBuilder.MenuScenePath);

        /// <summary>Batch entry: Unity -batchmode -executeMethod LR2.EditorTools.LR2BuildPipeline.BuildAllBatch -quit</summary>
        public static void BuildAllBatch()
        {
            try
            {
                bool ok = Run(false);
                if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LR2] BUILD FAILED with exception: {ex}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
        }

        /// <summary>
        /// Batch entry (no -quit): rebuild FX, audio, weapons and soldiers (no maps), then start the play-mode smoke test in
        /// the same process. Honours -lr2Scene, -lr2Duration, -lr2OutDir and the -lr2Robot / -lr2PoseCheck harness flags.
        /// The smoke test exits Unity when it finishes.
        /// </summary>
        public static void BuildCharactersThenPlayBatch()
        {
            bool ok = true;
            ok &= Step("FX", LR2ContentBuilder.BuildFx);
            ok &= Step("Audio", LR2ContentBuilder.BuildAudio);
            ok &= Step("Weapons", LR2ContentBuilder.BuildWeapons);
            ok &= Step("Soldiers", LR2ContentBuilder.BuildSoldiers);
            AssetDatabase.SaveAssets();
            EditorUtility.ClearProgressBar();
            Debug.Log($"[LR2] Characters rebuilt: {(ok ? "SUCCESS" : "FAILED")}");
            if (!ok && Application.isBatchMode)
            {
                EditorApplication.Exit(1);
                return;
            }
            LR2SmokeTest.RunBatch();
        }

        private static bool Run(bool interactive)
        {
            if (interactive && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
            bool ok = true;
            ok &= Step("Convert Kinemation demo materials", LR2ContentBuilder.ConvertDemoMaterials);
            ok &= Step("Data presets (difficulty, modes, fonts)", LR2ContentBuilder.BuildData);
            ok &= Step("FX", LR2ContentBuilder.BuildFx);
            ok &= Step("Audio", LR2ContentBuilder.BuildAudio);
            ok &= Step("Weapons", LR2ContentBuilder.BuildWeapons);
            ok &= Step("Soldiers", LR2ContentBuilder.BuildSoldiers);
            ok &= Step("Gameplay prefabs", LR2ContentBuilder.BuildGameplayPrefabs);
            ok &= Step("UI", LR2UIBuilder.Build);
            ok &= Step("Map: Saltworks", LR2MapBuilder.Build);
            ok &= Step("Map: Deadsector", LR2MapBuilder.BuildDeadsector);
            ok &= Step("Map: Foundry", LR2MapBuilder.BuildFoundry);
            ok &= Step("Map: Test Range", LR2MapBuilder.BuildTestRange);
            ok &= Step("Main menu scene", LR2MapBuilder.BuildMenuScene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(ok ? "[LR2] BUILD EVERYTHING: SUCCESS" : "[LR2] BUILD EVERYTHING: FINISHED WITH ERRORS (see log)");
            if (ok) EditorSceneManager.OpenScene(LR2MapBuilder.MenuScenePath);
            return ok;
        }

        private static bool Step(string name, Action action)
        {
            try
            {
                EditorUtility.DisplayProgressBar("LeadRush 2", name, 0.5f);
                action();
                Debug.Log($"[LR2] Step OK: {name}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[LR2] Step FAILED: {name}\n{e}");
                return false;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
    }
}
