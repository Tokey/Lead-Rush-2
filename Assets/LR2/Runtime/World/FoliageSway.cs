using System.Collections.Generic;
using UnityEngine;

namespace LR2.World
{
    /// <summary>
    /// Wind for the plants: every small plant / ivy / bush under the given roots sways around its base, driven by
    /// <see cref="WeatherSystem.Wind"/> (gusts, storms). Plants must not be static-batched (the map builder handles it).
    /// </summary>
    public class FoliageSway : MonoBehaviour
    {
        public Transform[] roots;
        [Tooltip("Max sway angle in degrees at full wind.")]
        public float maxAngle = 4.5f;
        public float frequency = 1.3f;
        [Tooltip("Plants taller than this (m) sway less (stiff bushes / saplings).")]
        public float stiffHeight = 2.5f;

        private struct Plant
        {
            public Transform t;
            public Quaternion baseRot;
            public float phase, amp;
        }

        private readonly List<Plant> _plants = new List<Plant>();

        private void Start()
        {
            if (roots == null) return;
            foreach (var root in roots)
            {
                if (root == null) continue;
                foreach (Transform child in root)
                {
                    var r = child.GetComponentInChildren<Renderer>();
                    if (r == null || child.GetComponentInChildren<Collider>() != null || child.name.StartsWith("SM_Debris")) continue;
                    float h = r.bounds.size.y;
                    _plants.Add(new Plant
                    {
                        t = child,
                        baseRot = child.localRotation,
                        phase = child.position.x * 0.37f + child.position.z * 0.21f,
                        amp = h > stiffHeight ? 0.35f : 1f
                    });
                }
            }
        }

        private void Update()
        {
            float wind = WeatherSystem.Instance != null ? WeatherSystem.Wind : 0.3f;
            float time = Time.time * frequency * (0.7f + wind * 0.8f);
            float a = maxAngle * wind;
            for (int i = 0; i < _plants.Count; i++)
            {
                var p = _plants[i];
                if (p.t == null) continue;
                float s = Mathf.Sin(time + p.phase) + 0.35f * Mathf.Sin(time * 2.3f + p.phase * 1.7f);
                float c = Mathf.Cos(time * 0.8f + p.phase * 0.6f);
                p.t.localRotation = p.baseRot * Quaternion.Euler(s * a * p.amp, 0f, (c * 0.6f + 0.4f) * a * p.amp);
            }
        }
    }
}
