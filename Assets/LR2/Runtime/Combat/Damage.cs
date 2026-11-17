using UnityEngine;

namespace LR2.Combat
{
    public enum HitZone
    {
        Generic,
        Head,
        Neck,
        UpperTorso,
        LowerTorso,
        Arm,
        Leg
    }

    public enum DamageType
    {
        Bullet,
        Melee,
        Explosion,
        Contact,
        Fall,
        Environment,
        Suicide
    }

    /// <summary>Everything known about one instance of damage. Passed by ref so receivers can annotate it.</summary>
    public struct DamageInfo
    {
        public float amount;           // final damage to apply (after range falloff + zone multiplier)
        public DamageType type;
        public HitZone zone;
        public Combatant attacker;     // may be null (environment)
        public Combatant victim;       // filled in by Health
        public GameObject source;      // weapon / orb / projectile
        public string weaponName;
        public Vector3 point;
        public Vector3 normal;
        public Vector3 direction;      // travel direction of the bullet / force
        public float distance;
        public float force;            // impulse applied to ragdolls (N·s)
        public bool penetrated;        // passed through cover (wallbang)

        public bool IsHeadshot => zone == HitZone.Head;
    }

    public interface IDamageable
    {
        bool CanBeDamaged { get; }

        /// <summary>Applies damage and returns the amount actually removed from health.</summary>
        float ApplyDamage(ref DamageInfo info);
    }
}
