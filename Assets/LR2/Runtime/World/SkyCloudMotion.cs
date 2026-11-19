using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace LR2.World
{
    /// <summary>
    /// B120 (user 11-19: "make the skybox clouds move like real life a bit ... specially during storms"). The maps use an HDRI
    /// sky, so the clouds are part of the image: HDRP's procedural HDRI distortion scrolls the upper hemisphere like drifting
    /// cloud. Calm weather drifts at ~10 km/h with slow gusts and a wind that wanders +-10 deg over minutes; a storm drives
    /// it up to ~70 km/h with stronger gusts and the wind veering +-30 deg. Works on the global Volume's runtime profile copy,
    /// so the shared profile asset never changes.
    /// </summary>
    public class SkyCloudMotion : MonoBehaviour
    {
        private HDRISky _sky;
        private float _speed = -1f, _storm;
        private float _seed;

        public static void Ensure()
        {
            if (FindAnyObjectByType<SkyCloudMotion>() == null) new GameObject("[LR2 SkyMotion]").AddComponent<SkyCloudMotion>();
        }

        private void Start()
        {
            _seed = Random.value * 100f;
            foreach (var v in FindObjectsByType<Volume>(FindObjectsSortMode.None))
            {
                if (v == null || v.sharedProfile == null || !v.sharedProfile.Has<HDRISky>()) continue;
                if (v.profile.TryGet(out HDRISky sky)) { _sky = sky; if (v.isGlobal) break; }
            }
            if (_sky == null) { enabled = false; return; }
            _sky.distortionMode.overrideState = true;
            _sky.distortionMode.value = HDRISky.DistortionMode.Procedural;
            _sky.upperHemisphereOnly.overrideState = true;
            _sky.upperHemisphereOnly.value = true;
            _sky.scrollOrientation.overrideState = true;
            _sky.scrollSpeed.overrideState = true;
        }

        private static WindParameter.WindParamaterValue Custom(float v) =>
            new WindParameter.WindParamaterValue { mode = WindParameter.WindOverrideMode.Custom, customValue = v, additiveValue = 0f, multiplyValue = 1f };

        private void Update()
        {
            if (_sky == null) return;
            var w = WeatherSystem.Instance;
            float rain = w != null ? Mathf.Clamp01(w.EffectiveRain) : 0f;
            bool thunder = w != null && w.CurrentName.IndexOf("Thunder", System.StringComparison.OrdinalIgnoreCase) >= 0;
            float stormTarget = Mathf.Clamp01(rain * (thunder ? 1f : 0.6f) + (thunder ? 0.3f : 0f));
            _storm = Mathf.MoveTowards(_storm, stormTarget, Time.deltaTime * 0.05f); // the front builds over ~20 s

            float t = Time.time + _seed;
            float gust = 1f + (Mathf.PerlinNoise(t * 0.06f, 3.1f) - 0.5f) * (0.4f + 0.6f * _storm)
                            + (Mathf.PerlinNoise(t * 0.23f, 5.7f) - 0.5f) * 0.25f * _storm; // short squalls in a storm
            float target = Mathf.Lerp(10f, 70f, _storm) * Mathf.Max(0.3f, gust);
            _speed = _speed < 0f ? target : Mathf.Lerp(_speed, target, 1f - Mathf.Exp(-Time.deltaTime * 0.5f));
            float dir = 235f + (Mathf.PerlinNoise(t * 0.008f, 7.7f) - 0.5f) * (20f + 40f * _storm);

            _sky.scrollSpeed.value = Custom(_speed);
            _sky.scrollOrientation.value = Custom(Mathf.Repeat(dir, 360f));
        }
    }
}
