using UnityEngine;

namespace LR2.FX
{
    /// <summary>Muzzle flash: particle burst + a very short light pulse that lights up the surroundings.</summary>
    public class MuzzleFlash : MonoBehaviour
    {
        [SerializeField] private ParticleSystem[] particles;
        [SerializeField] private Light flashLight;
        [SerializeField, Min(0.005f)] private float lightDuration = 0.045f;
        [SerializeField] private Vector2 intensityRange = new Vector2(0.75f, 1.15f);
        [SerializeField] private bool randomRoll = true;

        private float _baseIntensity;
        private float _lightOffAt;

        private void Awake()
        {
            if (particles == null || particles.Length == 0) particles = GetComponentsInChildren<ParticleSystem>(true);
            if (flashLight == null) flashLight = GetComponentInChildren<Light>(true);
            if (flashLight != null)
            {
                _baseIntensity = flashLight.intensity;
                flashLight.enabled = false;
            }
        }

        public void Play()
        {
            if (randomRoll) transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            if (particles != null)
                foreach (var p in particles)
                    if (p != null) p.Play(true);

            if (flashLight != null)
            {
                flashLight.intensity = _baseIntensity * Random.Range(intensityRange.x, intensityRange.y);
                flashLight.enabled = true;
                _lightOffAt = Time.time + lightDuration;
            }
        }

        private void Update()
        {
            if (flashLight != null && flashLight.enabled && Time.time >= _lightOffAt) flashLight.enabled = false;
        }

        private void OnDisable()
        {
            if (flashLight != null) flashLight.enabled = false;
        }
    }
}
