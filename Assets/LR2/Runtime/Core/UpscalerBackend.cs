using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace LR2.Core
{
    public enum LR2UpscalerKind
    {
        Native = 0,
        STP = 1,
        TAAU = 2,
        FSR1 = 3,
        DLSS = 4,
        FSR2 = 5
    }

    /// <summary>
    /// HDRP 17.5 upscaler capability probe &amp; dynamic resolution controller (B48 / P3).
    /// Exposes built-in HDRP upscalers (Native 100%, STP, TAAU, FSR 1.0 EdgeAdaptiveScalingUpres)
    /// plus capability-gated DLSS and FSR 2 passes, wiring per-camera <see cref="HDAdditionalCameraData"/>
    /// and <see cref="DynamicResolutionHandler"/> scaling cleanly at runtime.
    /// </summary>
    public static class UpscalerBackend
    {
        public const float DefaultQualityScalePercent = 77f;
        private const float MinAdaptiveScalePercent = 68f;
        private const float MaxAdaptiveScalePercent = 85f;
        private const float TargetGpuBudgetMs = 13.5f;

        private static bool _scalerRegistered;
        private static float _requestedPercent = 100f;
        private static float _adaptivePercent = DefaultQualityScalePercent;
        private static readonly FrameTiming[] TimingScratch = new FrameTiming[1];
        private static float _nextAdaptiveUpdate;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _scalerRegistered = false;
            _requestedPercent = 100f;
            _adaptivePercent = DefaultQualityScalePercent;
            _nextAdaptiveUpdate = 0f;
        }

        public static bool SupportsHardwareDrs =>
            SystemInfo.supportsDynamicResolution
            && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D11
            && SystemInfo.graphicsDeviceType != GraphicsDeviceType.OpenGLES3
            && SystemInfo.graphicsDeviceType != GraphicsDeviceType.OpenGLCore
            && SystemInfo.graphicsDeviceType != GraphicsDeviceType.WebGPU;

        public static bool IsDlssAvailable => HDDynamicResolutionPlatformCapabilities.DLSSDetected;
        public static bool IsFsr2Available => HDDynamicResolutionPlatformCapabilities.FSR2Detected;

        public static bool AssetSupportsDrs
        {
            get
            {
                if (!(GraphicsSettings.currentRenderPipeline is HDRenderPipelineAsset hdrp)) return false;
                var drs = hdrp.currentPlatformRenderPipelineSettings.dynamicResolutionSettings;
                return drs.enabled && ((drs.dynResType == DynamicResolutionType.Hardware && SupportsHardwareDrs) || drs.forceResolution);
            }
        }

        public static bool AssetSupportsStp => AssetSupportsDrs;
        public static bool SupportsStp => AssetSupportsDrs;

        public static bool IsModeSupported(LR2UpscalerKind kind)
        {
            switch (kind)
            {
                case LR2UpscalerKind.Native: return true;
                case LR2UpscalerKind.STP:
                case LR2UpscalerKind.TAAU:
                case LR2UpscalerKind.FSR1:
                    return AssetSupportsDrs;
                case LR2UpscalerKind.DLSS:
                    return AssetSupportsDrs && IsDlssAvailable;
                case LR2UpscalerKind.FSR2:
                    return AssetSupportsDrs && IsFsr2Available;
                default:
                    return false;
            }
        }

        public static LR2UpscalerKind RequestedKind =>
            (LR2UpscalerKind)Mathf.Clamp(LR2Settings.UpscalerMode, 0, (int)LR2UpscalerKind.FSR2);

        public static LR2UpscalerKind ActiveKind
        {
            get
            {
                var req = RequestedKind;
                return IsModeSupported(req) ? req : LR2UpscalerKind.Native;
            }
        }

        public static float RequestedScalePercent =>
            ActiveKind != LR2UpscalerKind.Native ? _requestedPercent : 100f;

        public static float CurrentScalePercent =>
            ActiveKind != LR2UpscalerKind.Native && ScalableBufferManager.widthScaleFactor > 0.05f
                ? Mathf.Clamp(ScalableBufferManager.widthScaleFactor * 100f, 25f, 100f)
                : RequestedScalePercent;

        public static string ActiveUpscalerLabel()
        {
            var req = RequestedKind;
            var active = ActiveKind;
            if (active != LR2UpscalerKind.Native)
            {
                string tag = active switch
                {
                    LR2UpscalerKind.STP => "HDRP STP",
                    LR2UpscalerKind.TAAU => "HDRP TAAU",
                    LR2UpscalerKind.FSR1 => "HDRP FSR 1.0",
                    LR2UpscalerKind.DLSS => "NVIDIA DLSS",
                    LR2UpscalerKind.FSR2 => "AMD FSR 2",
                    _ => "HDRP"
                };
                return $"{tag} ({CurrentScalePercent:0}%->100%)";
            }
            if (req != LR2UpscalerKind.Native)
                return $"NATIVE ({req.ToString().ToUpperInvariant()} UNAVAILABLE)";
            return "NATIVE (100%)";
        }

        public static string GetCapabilitiesNote()
        {
            string dxrState = GraphicsPresetContract.SupportsRayTracing
                ? (LR2RayTracingController.CanUseRayTracing() && LR2Settings.RayTracing ? "DXR ON" : "DXR READY")
                : "DXR N/A";
            var req = RequestedKind;
            if (req == LR2UpscalerKind.DLSS && !IsDlssAvailable)
                return $"{dxrState} · DLSS UNAVAILABLE (NO NGX/RTX PLUGIN) · RENDERING NATIVE 100%";
            if (req == LR2UpscalerKind.FSR2 && !IsFsr2Available)
                return $"{dxrState} · FSR 2 UNAVAILABLE (NO AMD PLUGIN) · RENDERING NATIVE 100%";
            string builtin = AssetSupportsDrs ? "STP/TAAU/FSR1 READY" : "DRS INACTIVE";
            string dlss = IsDlssAvailable ? "DLSS READY" : "DLSS NO PLUGIN";
            string fsr2 = IsFsr2Available ? "FSR2 READY" : "FSR2 NO PLUGIN";
            return $"{dxrState} · {builtin} · {dlss} · {fsr2}";
        }

        public static void EnsureScalerRegistered()
        {
            if (_scalerRegistered) return;
            _scalerRegistered = true;
            DynamicResolutionHandler.SetDynamicResScaler(ResolveDynamicResolutionPercentage, DynamicResScalePolicyType.ReturnsPercentage);
        }

        private static void SyncPipelineAdvancedPriority(LR2UpscalerKind active)
        {
            if (!(GraphicsSettings.currentRenderPipeline is HDRenderPipelineAsset hdrp)) return;
            var list = hdrp.currentPlatformRenderPipelineSettings.dynamicResolutionSettings.advancedUpscalerNames;
            if (list == null) return;
            list.Clear();
            // HDRP's advancedUpscalerNames picks the first matching entry (HDRenderPipeline.cs:1420), overriding
            // DynamicResUpscaleFilter.TAAU / EdgeAdaptiveScalingUpres when "STP" is listed.
            if (active == LR2UpscalerKind.DLSS) list.Add(nameof(AdvancedUpscalers.DLSS));
            else if (active == LR2UpscalerKind.FSR2) list.Add(nameof(AdvancedUpscalers.FSR2));
            else if (active == LR2UpscalerKind.STP) list.Add(nameof(AdvancedUpscalers.STP));
        }

        public static void ApplyToCamera(Camera cam, int antiAliasingMode, int upscalerMode,
            float taaSharpen = 0.3f, float taaHistorySharpening = 0.15f, float taaAntiFlicker = 0.7f, float taaBaseBlend = 0.9f)
        {
            if (cam == null) return;
            EnsureScalerRegistered();

            var hd = cam.GetComponent<HDAdditionalCameraData>();
            if (hd == null) return;

            var req = (LR2UpscalerKind)Mathf.Clamp(upscalerMode, 0, (int)LR2UpscalerKind.FSR2);
            var active = IsModeSupported(req) ? req : LR2UpscalerKind.Native;
            bool scaling = active != LR2UpscalerKind.Native;

            SyncPipelineAdvancedPriority(active);
            hd.allowDynamicResolution = scaling;
            hd.allowDeepLearningSuperSampling = active == LR2UpscalerKind.DLSS;
            hd.allowFidelityFX2SuperResolution = active == LR2UpscalerKind.FSR2;
            if (active == LR2UpscalerKind.FSR1)
            {
                hd.fsrOverrideSharpness = true;
                hd.fsrSharpness = 0.85f;
                DynamicResolutionHandler.SetUpscaleFilter(cam, DynamicResUpscaleFilter.EdgeAdaptiveScalingUpres);
            }
            else if (active == LR2UpscalerKind.TAAU || active == LR2UpscalerKind.STP)
            {
                DynamicResolutionHandler.SetUpscaleFilter(cam, DynamicResUpscaleFilter.TAAU);
            }
            else
            {
                DynamicResolutionHandler.SetUpscaleFilter(cam, DynamicResUpscaleFilter.CatmullRom);
            }

            _requestedPercent = scaling ? _adaptivePercent : 100f;

            // HDRP 17.5 requires TemporalAntialiasing on HDCamera for STP / TAAU upscaling passes.
            if (active == LR2UpscalerKind.STP || active == LR2UpscalerKind.TAAU)
            {
                hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
                hd.TAAQuality = HDAdditionalCameraData.TAAQualityLevel.High;
                hd.taaSharpenMode = HDAdditionalCameraData.TAASharpenMode.ContrastAdaptiveSharpening;
                hd.taaSharpenStrength = taaSharpen;
                hd.taaHistorySharpening = taaHistorySharpening;
                hd.taaAntiFlicker = taaAntiFlicker;
                hd.taaBaseBlendFactor = taaBaseBlend;
                return;
            }

            switch (Mathf.Clamp(antiAliasingMode, 0, 3))
            {
                case 0:
                    hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
                    break;
                case 1:
                    hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.FastApproximateAntialiasing;
                    break;
                case 2:
                    hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                    hd.SMAAQuality = HDAdditionalCameraData.SMAAQualityLevel.High;
                    break;
                default:
                    hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
                    hd.TAAQuality = HDAdditionalCameraData.TAAQualityLevel.High;
                    hd.taaSharpenStrength = taaSharpen;
                    hd.taaHistorySharpening = taaHistorySharpening;
                    hd.taaAntiFlicker = taaAntiFlicker;
                    hd.taaBaseBlendFactor = taaBaseBlend;
                    break;
            }
        }

        private static float ResolveDynamicResolutionPercentage()
        {
            if (ActiveKind == LR2UpscalerKind.Native)
            {
                _requestedPercent = 100f;
                return 100f;
            }

            float now = Time.unscaledTime;
            if (now >= _nextAdaptiveUpdate && FrameTimingManager.IsFeatureEnabled())
            {
                _nextAdaptiveUpdate = now + 0.5f;
                FrameTimingManager.CaptureFrameTimings();
                uint count = FrameTimingManager.GetLatestTimings(1, TimingScratch);
                if (count > 0 && TimingScratch[0].gpuFrameTime > 0.1)
                {
                    double gpuMs = TimingScratch[0].gpuFrameTime;
                    if (gpuMs > TargetGpuBudgetMs)
                        _adaptivePercent = Mathf.Max(MinAdaptiveScalePercent, _adaptivePercent - 2f);
                    else if (gpuMs < TargetGpuBudgetMs * 0.72f)
                        _adaptivePercent = Mathf.Min(MaxAdaptiveScalePercent, _adaptivePercent + 1f);
                }
            }

            _requestedPercent = _adaptivePercent;
            return _requestedPercent;
        }
    }
}
