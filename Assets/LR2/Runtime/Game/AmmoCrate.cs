using LR2.Audio;
using LR2.Characters;
using UnityEngine;

namespace LR2.Game
{
    /// <summary>Walk-over resupply. Refills reserve ammo, then goes dark for a cooldown.</summary>
    [RequireComponent(typeof(Collider))]
    public class AmmoCrate : MonoBehaviour
    {
        [SerializeField] private int rounds = 120;
        [SerializeField] private float cooldown = 20f;
        [SerializeField] private Light indicator;
        [SerializeField] private Renderer indicatorRenderer;

        private float _readyAt;

        private void Reset()
        {
            var c = GetComponent<Collider>();
            c.isTrigger = true;
        }

        private void Update()
        {
            bool ready = Time.time >= _readyAt;
            if (indicator != null) indicator.enabled = ready;
            if (indicatorRenderer != null) indicatorRenderer.enabled = ready;
        }

        private void OnTriggerStay(Collider other)
        {
            if (Time.time < _readyAt) return;
            var soldier = other.GetComponentInParent<SoldierController>();
            if (soldier == null || soldier.IsDead) return;
            if (soldier.AddReserveAmmo(rounds) <= 0) return;
            _readyAt = Time.time + cooldown;
            var s = LR2CommonSounds.Instance;
            if (s != null) LR2Audio.PlayAt(s.ammoPickup, transform.position);
        }
    }
}
