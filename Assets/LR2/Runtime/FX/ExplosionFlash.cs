using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace LR2.FX
{
    /// <summary>B112: the short HDRP light pulse of an explosion (the Particle Pack's own lights are built-in pipeline).</summary>
    public class ExplosionFlash : MonoBehaviour
    {
        private Light _light;
        private float _start, _duration, _lumens;

        public static void Spawn(Vector3 position, Color color, float lumens, float range, float duration)
        {
            var go = new GameObject("ExplosionFlash");
            go.transform.position = position;
            var f = go.AddComponent<ExplosionFlash>();
            f._light = go.AddComponent<Light>();
            f._light.type = LightType.Point;
            f._light.color = color;
            f._light.range = range;
            f._light.shadows = LightShadows.None;
            f._light.lightUnit = LightUnit.Lumen;
            f._light.intensity = lumens;
            if (!go.TryGetComponent<HDAdditionalLightData>(out _)) go.AddComponent<HDAdditionalLightData>();
            f._start = Time.time;
            f._duration = Mathf.Max(0.05f, duration);
            f._lumens = lumens;
            Destroy(go, duration + 0.05f);
        }

        private void Update()
        {
            float k = 1f - Mathf.Clamp01((Time.time - _start) / _duration);
            if (_light != null) _light.intensity = _lumens * k * k;
        }
    }
}
