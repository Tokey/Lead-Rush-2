using UnityEngine;

namespace LR2.World
{
    /// <summary>
    /// Map-authored realism knobs shared by builders and runtime weather. Values are intentionally bounded
    /// so atmosphere and wetness cannot silently destroy readability.
    /// </summary>
    [CreateAssetMenu(menuName = "LR2/World/Environment Realism Profile", fileName = "EnvironmentRealismProfile")]
    public sealed class EnvironmentRealismProfile : ScriptableObject
    {
        [Range(0f, 1f)] public float materialVariation = 0.35f;
        [Range(0f, 1f)] public float grime = 0.3f;
        [Range(0f, 1f)] public float edgeWear = 0.25f;
        [Range(0f, 1f)] public float clutterDensity = 0.35f;
        [Range(0.5f, 2f)] public float scaleCueDensity = 1f;
        [Range(0f, 1f)] public float wetnessResponse = 0.7f;
        [Range(0f, 1f)] public float vegetationDensity = 0.25f;
        [Range(0f, 1f)] public float practicalLightDensity = 0.4f;

        public float EffectiveWetness(float weatherWetness)
        {
            return Mathf.Clamp01(weatherWetness * wetnessResponse);
        }

        public float DetailWeight(float distanceMeters)
        {
            return Mathf.Clamp01((1f - distanceMeters / 45f) * materialVariation);
        }
    }
}
