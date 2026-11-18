using LR2.Characters;
using LR2.Combat;
using LR2.Core;
using UnityEngine;

namespace LR2.World
{
    /// <summary>
    /// Trigger-volume fire hazard. Applies damage through the normal Health path and creates a
    /// self-contained flame, smoke, flickering-light, and optional crackle-loop presentation.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class FireHazard : MonoBehaviour
    {
        [Header("Damage")]
        [SerializeField, Min(0f)] private float damagePerSecond = 12f;
        [SerializeField, Min(0.05f)] private float damageInterval = 0.25f;
        [SerializeField, Min(0f)] private float damageRadius = 1.8f;

        [Header("Presentation")]
        [SerializeField, Min(0f)] private float lightIntensity = 2.8f;
        [SerializeField, Min(0f)] private float lightRange = 5f;
        [SerializeField, Range(0f, 1f)] private float flickerAmount = 0.18f;
        [SerializeField, Range(0f, 1f)] private float smokeAmount = 0.7f;
        [SerializeField] private AudioClip crackleLoop;

        private readonly Collider[] _overlaps = new Collider[16];
        private float _nextDamageAt;
        private float _lightBaseIntensity;
        private float _seed;
        private Light _flameLight;
        private ParticleSystem _flameParticles;
        private ParticleSystem _smokeParticles;
        private AudioSource _crackle;

        private void Reset()
        {
            var collider = GetComponent<Collider>();
            collider.isTrigger = true;
            if (collider is SphereCollider sphere)
                sphere.radius = damageRadius;
        }

        private void Awake()
        {
            var trigger = GetComponent<Collider>();
            trigger.isTrigger = true;
            _seed = Random.value * 100f;
            BuildPresentation();
        }

        private void OnEnable()
        {
            _nextDamageAt = Time.time;
            if (_flameParticles != null) _flameParticles.Play();
            if (_smokeParticles != null) _smokeParticles.Play();
            if (_crackle != null) _crackle.Play();
        }

        private void OnDisable()
        {
            if (_flameParticles != null) _flameParticles.Stop();
            if (_smokeParticles != null) _smokeParticles.Stop();
            if (_crackle != null) _crackle.Stop();
        }

        private void Update()
        {
            if (_flameLight != null)
            {
                float noise = Mathf.PerlinNoise(Time.time * 8f, _seed);
                float second = Mathf.PerlinNoise(Time.time * 13f + 17f, _seed);
                _flameLight.intensity = _lightBaseIntensity *
                    Mathf.Clamp01(1f - flickerAmount + flickerAmount * (noise * 0.65f + second * 0.35f));
            }

            if (Time.time < _nextDamageAt) return;
            _nextDamageAt = Time.time + damageInterval;
            ApplyDamageTick();
        }

        private void ApplyDamageTick()
        {
            int count = Physics.OverlapSphereNonAlloc(
                transform.position, damageRadius, _overlaps, LR2Layers.CharacterMask, QueryTriggerInteraction.Ignore);

            float amount = damagePerSecond * damageInterval;
            for (int i = 0; i < count; i++)
            {
                if (_overlaps[i] == null) continue;
                var soldier = _overlaps[i].GetComponentInParent<SoldierController>();
                if (soldier == null || soldier.IsDead || soldier.Health == null) continue;

                var combatant = soldier.Combatant;
                Vector3 point = combatant != null ? combatant.ChestPosition : soldier.transform.position + Vector3.up * 1.2f;
                var info = new DamageInfo
                {
                    amount = amount,
                    type = DamageType.Environment,
                    zone = HitZone.LowerTorso,
                    attacker = null,
                    victim = combatant,
                    source = gameObject,
                    weaponName = "Fire",
                    point = point,
                    normal = (point - transform.position).normalized,
                    direction = Vector3.up,
                    force = 0f
                };
                soldier.Health.ApplyDamage(ref info);
            }
        }

        private void BuildPresentation()
        {
            var lightGo = new GameObject("Fire Light");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.localPosition = Vector3.up * Mathf.Max(0.35f, damageRadius * 0.45f);
            _flameLight = lightGo.AddComponent<Light>();
            _flameLight.type = LightType.Point;
            _flameLight.color = new Color(1f, 0.42f, 0.12f);
            _flameLight.range = lightRange;
            _lightBaseIntensity = lightIntensity;
            _flameLight.intensity = _lightBaseIntensity;
            _flameLight.shadows = LightShadows.None;

            _flameParticles = BuildParticles("Fire Flame", new Color(1f, 0.32f, 0.03f, 0.9f),
                new Color(1f, 0.85f, 0.18f, 0.1f), 0.16f, 0.55f, 46f);
            _smokeParticles = BuildParticles("Fire Smoke", new Color(0.12f, 0.12f, 0.12f, 0.48f),
                new Color(0.04f, 0.04f, 0.04f, 0f), 0.5f, 1.8f, 18f * smokeAmount);

            if (crackleLoop != null)
            {
                var audioGo = new GameObject("Fire Crackle");
                audioGo.transform.SetParent(transform, false);
                _crackle = audioGo.AddComponent<AudioSource>();
                _crackle.clip = crackleLoop;
                _crackle.loop = true;
                _crackle.spatialBlend = 1f;
                _crackle.minDistance = 2f;
                _crackle.maxDistance = Mathf.Max(12f, lightRange * 2f);
                _crackle.volume = 0.45f;
            }
        }

        private ParticleSystem BuildParticles(string name, Color startColor, Color endColor,
            float minSize, float maxSize, float emissionRate)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.up * 0.05f;
            var particles = go.AddComponent<ParticleSystem>();
            var main = particles.main;
            main.startLifetime = name.Contains("Smoke") ? 1.8f : 0.5f;
            main.startSpeed = name.Contains("Smoke") ? 0.8f : 1.1f;
            main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
            main.startColor = new ParticleSystem.MinMaxGradient(startColor, endColor);
            main.gravityModifier = name.Contains("Smoke") ? -0.02f : 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 256;

            var emission = particles.emission;
            emission.rateOverTime = emissionRate;

            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = Mathf.Max(0.15f, damageRadius * 0.38f);

            var color = particles.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(startColor, endColor);
            return particles;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.35f, 0.05f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, damageRadius);
        }
    }
}
