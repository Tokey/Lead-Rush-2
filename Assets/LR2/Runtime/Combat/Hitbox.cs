using LR2.Core;
using UnityEngine;

namespace LR2.Combat
{
    /// <summary>
    /// Damage collider on a bone. Bullets raycast against the Hitbox layer; the zone decides the weapon
    /// multiplier (head / torso / limbs). Never collides physically.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class Hitbox : MonoBehaviour, IDamageable
    {
        [SerializeField] private HitZone zone = HitZone.UpperTorso;
        [SerializeField] private Health owner;

        public HitZone Zone => zone;
        public Health Owner => owner;
        public Combatant Combatant => owner != null ? owner.Owner : null;
        public bool CanBeDamaged => owner != null && owner.CanBeDamaged;

        public void Configure(Health healthOwner, HitZone hitZone)
        {
            owner = healthOwner;
            zone = hitZone;
            gameObject.layer = LR2Layers.Hitbox;
        }

        public float ApplyDamage(ref DamageInfo info)
        {
            info.zone = zone;
            return owner != null ? owner.ApplyDamage(ref info) : 0f;
        }

        private void Awake()
        {
            if (owner == null) owner = GetComponentInParent<Health>();
        }

        private void Reset()
        {
            owner = GetComponentInParent<Health>();
            gameObject.layer = LR2Layers.Hitbox;
        }
    }
}
