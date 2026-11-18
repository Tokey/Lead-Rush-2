using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace LR2.Core
{
    /// <summary>
    /// Applies user Ray Tracing (DXR) + revamped lighting preferences onto HDRP scene volumes cleanly at runtime
    /// and on scene load, safely guarding against batchmode / unsupported GPUs.
    /// </summary>
    public class LR2RayTracingController : MonoBehaviour
    {
        public static LR2RayTracingController Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Instance != null) return;
            var go = new GameObject("[LR2 RayTracingController]");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<LR2RayTracingController>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            LR2Settings.Changed += ApplyAll;
            SceneManager.sceneLoaded += OnSceneLoaded;
            ApplyAll();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            LR2Settings.Changed -= ApplyAll;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ApplyAll();

        public static bool CanUseRayTracing()
        {
            if (Application.isBatchMode) return false;
            if (!SystemInfo.supportsRayTracing) return false;
            var hdrp = GraphicsSettings.currentRenderPipeline as HDRenderPipelineAsset;
            return hdrp != null && hdrp.currentPlatformRenderPipelineSettings.supportRayTracing;
        }

        public static void ApplyAll()
        {
            bool enableDxr = LR2Settings.RayTracing && CanUseRayTracing();
            var volumes = FindObjectsByType<Volume>();
            for (int i = 0; i < volumes.Length; i++)
            {
                var v = volumes[i];
                if (v == null || !v.isGlobal || v.priority > 1f) continue;
                var profile = v.profile;
                if (profile == null) continue;

                if (profile.TryGet<ScreenSpaceReflection>(out var ssr))
                {
                    ssr.enabled.Override(true);
                    ssr.tracing.Override(enableDxr ? RayCastingMode.RayTracing : RayCastingMode.RayMarching);
                }
                if (profile.TryGet<GlobalIllumination>(out var gi))
                {
                    gi.enable.Override(enableDxr);
                    gi.tracing.Override(enableDxr ? RayCastingMode.RayTracing : RayCastingMode.RayMarching);
                    gi.mode.Override(RayTracingMode.Performance);
                }
                if (profile.TryGet<ScreenSpaceAmbientOcclusion>(out var ao))
                {
                    ao.rayTracing.Override(enableDxr);
                }
            }
        }
    }
}
