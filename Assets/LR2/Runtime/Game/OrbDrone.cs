using LR2.Audio;
using LR2.Combat;
using LR2.Core;
using UnityEngine;

namespace LR2.Game
{
    /// <summary>
    /// The legacy LeadRush "orb" enemy, re-imagined as a seeker drone: hovers toward the nearest soldier, telegraphs
    /// when close (light pulse + alert), and detonates on contact. Dies (pops) when shot.
    /// </summary>
    [RequireComponent(typeof(Combatant), typeof(Health))]
    public class OrbDrone : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float speed = 4.6f;
        [SerializeField] private float acceleration = 6f;
        [SerializeField] private float hoverHeight = 1.4f;
        [SerializeField] private float bobAmplitude = 0.12f;
        [SerializeField] private float avoidDistance = 2.2f;

        [Header("Attack")]
        [SerializeField] private float alertDistance = 8f;
        [SerializeField] private float detonateDistance = 1.3f;
        [SerializeField] private float blastRadius = 3.5f;
        [SerializeField] private float blastDamage = 75f;

        [Header("Visuals")]
        [SerializeField] private Transform spinner;
        [SerializeField] private Light glow;
        [SerializeField] private GameObject explosionPrefab;
        [SerializeField] private float spinSpeed = 220f;

        private Combatant _self;
        private Health _health;
        private Vector3 _velocity;
        private bool _alerted;
        private bool _dead;
        private float _baseGlow;
        private float _seed;

        public GameObject ExplosionPrefab { get => explosionPrefab; set => explosionPrefab = value; }

        private void Awake()
        {
            _self = GetComponent<Combatant>();
            _health = GetComponent<Health>();
            _self.Team = TeamId.Hostile;
            _self.IsBot = true;
            _self.SetAimPoints(transform, transform, transform);
            if (string.IsNullOrEmpty(_self.DisplayName) || _self.DisplayName == "Soldier") _self.DisplayName = "SEEKER DRONE";
            _health.Died += OnDied;
            if (glow != null) _baseGlow = glow.intensity;
            _seed = Random.value * 10f;
        }

        private void OnDestroy()
        {
            if (_health != null) _health.Died -= OnDied;
        }

        private void Update()
        {
            if (_dead) return;
            Combatant target = FindTarget(out float dist);
            Vector3 pos = transform.position;
            Vector3 desired = Vector3.zero;

            if (target != null)
            {
                Vector3 aim = target.ChestPosition;
                Vector3 to = aim - pos;
                desired = to.normalized * speed;
                if (dist < alertDistance && !_alerted)
                {
                    _alerted = true;
                    var s = LR2CommonSounds.Instance;
                    if (s != null) LR2Audio.PlayAt(s.orbAlert, pos);
                }
                if (dist < detonateDistance)
                {
                    Detonate();
                    return;
                }
            }

            // Keep hover height above ground and steer around obstacles.
            if (Physics.Raycast(pos, Vector3.down, out var ground, 6f, LR2Layers.EnvironmentMask, QueryTriggerInteraction.Ignore))
            {
                float targetY = ground.point.y + hoverHeight + Mathf.Sin(Time.time * 2.3f + _seed) * bobAmplitude;
                desired.y += (targetY - pos.y) * 3f;
            }
            if (_velocity.sqrMagnitude > 0.01f &&
                Physics.SphereCast(pos, 0.35f, _velocity.normalized, out var hit, avoidDistance, LR2Layers.EnvironmentMask, QueryTriggerInteraction.Ignore))
            {
                Vector3 slide = Vector3.ProjectOnPlane(desired, hit.normal);
                desired = slide + hit.normal * speed * 0.6f + Vector3.up * speed * 0.3f;
            }

            _velocity = Vector3.MoveTowards(_velocity, desired, acceleration * Time.deltaTime);
            transform.position = pos + _velocity * Time.deltaTime;
            if (_self != null) _self.Velocity = _velocity;

            if (spinner != null) spinner.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.Self);
            if (glow != null)
            {
                float pulse = _alerted ? 0.5f + 0.5f * Mathf.Sin(Time.time * 18f) : 0.85f + 0.15f * Mathf.Sin(Time.time * 3f);
                glow.intensity = _baseGlow * pulse * (_alerted ? 1.8f : 1f);
            }
        }

        private Combatant FindTarget(out float bestDist)
        {
            Combatant best = null;
            bestDist = float.MaxValue;
            foreach (var c in Combatant.All)
            {
                if (c == null || c == _self || !c.IsAlive || c.Team == TeamId.Hostile) continue;
                float d = Vector3.Distance(transform.position, c.ChestPosition);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = c;
                }
            }
            return best;
        }

        private void Detonate()
        {
            if (_dead) return;
            Vector3 pos = transform.position;
            foreach (var c in Combatant.All.ToArrayCopy())
            {
                if (c == null || c == _self || !c.IsAlive) continue;
                float d = Vector3.Distance(pos, c.ChestPosition);
                if (d > blastRadius) continue;
                if (Physics.Linecast(pos, c.ChestPosition, LR2Layers.EnvironmentMask, QueryTriggerInteraction.Ignore)) continue;
                var health = c.Health;
                if (health == null) continue;
                var info = new DamageInfo
                {
                    amount = blastDamage * (1f - d / blastRadius),
                    type = DamageType.Explosion,
                    attacker = _self,
                    source = gameObject,
                    weaponName = "Drone",
                    point = c.ChestPosition,
                    direction = (c.ChestPosition - pos).normalized,
                    distance = d,
                    force = 120f
                };
                health.ApplyDamage(ref info);
            }
            GameEvents.RaiseNoise(_self, pos, 45f, NoiseType.Explosion);
            _health.Kill(DamageType.Explosion);
        }

        private void OnDied(DamageInfo info)
        {
            if (_dead) return;
            _dead = true;
            if (explosionPrefab != null) LR2Pool.Spawn(explosionPrefab, transform.position, Quaternion.identity, 4f);
            var s = LR2CommonSounds.Instance;
            if (s != null) LR2Audio.PlayAt(s.orbExplode, transform.position);
            foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;
            foreach (var col in GetComponentsInChildren<Collider>()) col.enabled = false;
            if (glow != null) glow.enabled = false;
            Destroy(gameObject, 0.2f);
        }
    }

    internal static class CombatantListExtensions
    {
        public static Combatant[] ToArrayCopy(this System.Collections.Generic.IReadOnlyList<Combatant> list)
        {
            var arr = new Combatant[list.Count];
            for (int i = 0; i < list.Count; i++) arr[i] = list[i];
            return arr;
        }
    }
}
