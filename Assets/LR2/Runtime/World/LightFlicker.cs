using UnityEngine;

namespace LR2.World
{
    /// <summary>Subtle flicker for practical lights (sodium lamps, faulty fluorescents).</summary>
    [RequireComponent(typeof(Light))]
    public class LightFlicker : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] private float amount = 0.15f;
        [SerializeField] private float speed = 7f;
        [Tooltip("Chance per second of a short dropout (broken lamp).")]
        [SerializeField, Range(0f, 1f)] private float dropoutChance = 0.03f;
        private Light _light;
        private float _base, _seed, _dropoutUntil;

        public void Configure(float amt, float spd, float dropout)
        {
            amount = Mathf.Clamp01(amt);
            speed = Mathf.Max(0.1f, spd);
            dropoutChance = Mathf.Clamp01(dropout);
        }

        private void Awake()
        {
            _light = GetComponent<Light>();
            _base = _light.intensity;
            _seed = Random.value * 100f;
        }

        private void Update()
        {
            if (Time.time > _dropoutUntil && Random.value < dropoutChance * Time.deltaTime) _dropoutUntil = Time.time + Random.Range(0.05f, 0.2f);
            float n = Mathf.PerlinNoise(Time.time * speed, _seed);
            float f = Time.time < _dropoutUntil ? 0.15f : 1f - amount + n * amount;
            _light.intensity = _base * f;
        }
    }
}
