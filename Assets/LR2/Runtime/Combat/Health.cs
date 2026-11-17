using System;
using LR2.Core;
using UnityEngine;

namespace LR2.Combat
{
    /// <summary>
    /// Call-of-Duty style health: 100 HP, no armour, fast full regeneration after a short delay without damage,
    /// brief spawn protection that ends as soon as the owner fires.
    /// </summary>
    [DisallowMultipleComponent]
    public class Health : MonoBehaviour, IDamageable
    {
        [Header("Health")]
        [SerializeField, Min(1f)] private float maxHealth = 100f;

        [Header("Regeneration")]
        [SerializeField] private bool regenerate = true;
        [Tooltip("Seconds without taking damage before health starts regenerating.")]
        [SerializeField, Min(0f)] private float regenDelay = 4f;
        [Tooltip("Health regenerated per second once regeneration starts.")]
        [SerializeField, Min(0f)] private float regenPerSecond = 55f;

        [Header("Protection")]
        [SerializeField, Min(0f)] private float spawnProtection = 1.5f;
        [SerializeField] private bool allowFriendlyFire;
        [Tooltip("If true the attacker can hurt itself (explosions).")]
        [SerializeField] private bool allowSelfDamage = true;

        public event Action<DamageInfo> Damaged;
        public event Action<DamageInfo> Died;
        public event Action Revived;

        public float Current { get; private set; }
        public float Max => maxHealth;
        public float Normalized => maxHealth > 0f ? Current / maxHealth : 0f;
        public bool IsAlive { get; private set; } = true;
        public bool IsInvulnerable => Time.time < _invulnerableUntil;
        public float LastDamageTime { get; private set; } = -999f;
        public DamageInfo LastDamage { get; private set; }
        public Combatant Owner { get; private set; }
        public bool CanBeDamaged => IsAlive && !IsInvulnerable;

        private float _invulnerableUntil;

        public float MaxHealth
        {
            get => maxHealth;
            set { maxHealth = Mathf.Max(1f, value); Current = Mathf.Min(Current, maxHealth); }
        }

        public bool Regenerate { get => regenerate; set => regenerate = value; }

        private void Awake()
        {
            Owner = GetComponent<Combatant>();
            Current = maxHealth;
        }

        private void Update()
        {
            if (!IsAlive || !regenerate || Current >= maxHealth) return;
            if (Time.time - LastDamageTime < regenDelay) return;
            Current = Mathf.Min(maxHealth, Current + regenPerSecond * Time.deltaTime);
        }

        public float ApplyDamage(ref DamageInfo info)
        {
            if (!IsAlive || info.amount <= 0f) return 0f;
            bool environmental = info.type == DamageType.Suicide || info.type == DamageType.Environment || info.type == DamageType.Fall;
            if (IsInvulnerable && !environmental) return 0f;

            if (info.attacker != null && Owner != null)
            {
                if (info.attacker == Owner)
                {
                    if (!allowSelfDamage) return 0f;
                }
                else if (!allowFriendlyFire && !TeamUtil.AreHostile(info.attacker, Owner))
                {
                    return 0f;
                }
            }

            info.victim = Owner;
            float applied = Mathf.Min(Current, info.amount);
            Current -= info.amount;
            LastDamageTime = Time.time;
            LastDamage = info;

            bool killed = Current <= 0.001f;
            if (info.attacker != null && info.attacker != Owner)
                info.attacker.stats.damageDealt += applied;

            Damaged?.Invoke(info);
            GameEvents.RaiseHit(new HitEvent
            {
                attacker = info.attacker,
                victim = Owner,
                damage = applied,
                zone = info.zone,
                type = info.type,
                killed = killed,
                point = info.point,
                direction = info.direction
            });

            if (killed) Die(info);
            return applied;
        }

        public void Die(DamageInfo info)
        {
            if (!IsAlive) return;
            Current = 0f;
            IsAlive = false;
            info.victim = Owner;
            if (Owner != null) Owner.LastDeathTime = Time.time;

            Died?.Invoke(info);
            GameEvents.RaiseKilled(new KillEvent
            {
                killer = info.attacker,
                victim = Owner,
                weaponName = info.weaponName,
                type = info.type,
                headshot = info.IsHeadshot,
                wallbang = info.penetrated,
                distance = info.distance,
                victimPosition = transform.position,
                direction = info.direction
            });
        }

        /// <summary>Kills the owner without an attacker (falling out of the map, admin, etc.).</summary>
        public void Kill(DamageType type = DamageType.Suicide)
        {
            Die(new DamageInfo { amount = Current, type = type, point = transform.position, direction = Vector3.down, weaponName = type.ToString() });
        }

        public void Heal(float amount)
        {
            if (!IsAlive) return;
            Current = Mathf.Min(maxHealth, Current + Mathf.Max(0f, amount));
        }

        public void Revive()
        {
            Current = maxHealth;
            IsAlive = true;
            LastDamageTime = -999f;
            _invulnerableUntil = Time.time + spawnProtection;
            Revived?.Invoke();
        }

        public void SetInvulnerable(float seconds) => _invulnerableUntil = Time.time + Mathf.Max(0f, seconds);

        /// <summary>Firing ends spawn protection immediately (prevents spawn-protected trading).</summary>
        public void CancelSpawnProtection() => _invulnerableUntil = 0f;
    }
}
