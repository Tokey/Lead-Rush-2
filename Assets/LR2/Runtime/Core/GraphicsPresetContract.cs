using UnityEngine;

namespace LR2.Core
{
    public enum LR2GraphicsPreset { Low, Medium, High, Ultra }

    /// <summary>Stable named preset contract; UI and benchmarks do not depend on project quality indices.</summary>
    public static class GraphicsPresetContract
    {
        public static string Label(LR2GraphicsPreset preset) => preset.ToString().ToUpperInvariant();

        public static int ResolveQualityIndex(LR2GraphicsPreset preset)
        {
            int desired = (int)preset;
            if (QualitySettings.names == null || QualitySettings.names.Length == 0) return -1;
            return Mathf.Clamp(desired, 0, QualitySettings.names.Length - 1);
        }

        public static bool SupportsRayTracing => SystemInfo.supportsRayTracing;
    }
}
