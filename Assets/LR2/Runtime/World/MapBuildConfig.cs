using UnityEngine;

namespace LR2.World
{
    /// <summary>
    /// Every tunable of the Saltworks map build (lighting, atmosphere, dressing). Edit in the Inspector, then press
    /// "Rebuild Map" on the MapRoot in the scene (or LR2 > Build Map) to regenerate.
    /// </summary>
    [CreateAssetMenu(menuName = "LR2/World/Map Build Config", fileName = "SaltworksConfig")]
    public class MapBuildConfig : ScriptableObject
    {
        [Tooltip("Internal: bumps when a new look preset is applied to an existing asset.")]
        public int presetVersion = 0;

        [Header("Layout")]
        public int seed = 1717;
        [Range(0f, 1f)] public float propDensity = 0.85f;
        public bool backgroundDressing = true;
        public bool decals = true;

        [Header("Reclaimed by nature")]
        public bool forest = true;
        [Range(0, 400)] public int forestTrees = 190;
        [Range(0, 60)] public int yardTrees = 16;
        [Range(0, 20)] public int fallenTrees = 7;
        [Range(0, 4000)] public int undergrowth = 1600;
        [Tooltip("How much sky/ambient light is removed inside buildings (0 = none, 1 = pitch black except direct light).")]
        [Range(0f, 1f)] public float indoorDarkness = 0.94f;
        [Tooltip("Street/warehouse lamps. Off: nobody has paid the power bill for a hundred years.")]
        public bool practicalLights = false;

        [Header("Sun (physically based)")]
        [Range(2f, 89f)] public float sunElevation = 21f;
        [Range(0f, 360f)] public float sunAzimuth = 238f;
        [Tooltip("Direct sunlight in lux (clear golden hour ~ 20,000-60,000).")]
        public float sunLux = 52000f;
        [Range(1500f, 12000f)] public float sunTemperature = 4700f;
        [Range(0f, 5f)] public float sunAngularDiameter = 0.53f;

        [Header("Atmosphere (ScansFactory-style dense volumetrics)")]
        public float fogMeanFreePath = 120f;
        public float fogBaseHeight = 2f;
        public float fogMaxHeight = 45f;
        public Color fogAlbedo = new Color(0.66f, 0.72f, 0.78f);
        [Range(-1f, 1f)] public float fogAnisotropy = 0.72f;
        public float fogDepthExtent = 90f;

        [Header("Exposure / grade")]
        public float exposureCompensation = -0.9f;
        [Tooltip("Darkest/brightest EV the eye adapts to. A high minimum keeps interiors dark.")]
        public float exposureLimitMin = 8.2f;
        public float exposureLimitMax = 11.5f;
        [Range(-100f, 100f)] public float contrast = 28f;
        [Range(-100f, 100f)] public float saturation = -6f;
        [Range(-100f, 100f)] public float temperature = 6f;
        [Range(0f, 1f)] public float vignette = 0.28f;
        [Range(0f, 1f)] public float bloom = 0.35f;

        [Header("Bodycam lens")]
        [Range(-1f, 1f)] public float lensDistortion = -0.18f;
        [Range(0f, 1f)] public float chromaticAberration = 0.12f;
        [Range(0f, 1f)] public float filmGrain = 0.22f;
        [Range(0f, 2f)] public float motionBlur = 0.65f;
    }
}
