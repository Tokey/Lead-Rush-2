using System.Collections.Generic;
using LR2.Audio;
using LR2.Characters;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LR2.Game
{
    /// <summary>
    /// Mid-map strategic resupply station: walk-over or press [F] nearby to refill reserve ammo and armour plates,
    /// tracked on minimap (B39 ammo, B74 armour). One shared cooldown per station, so holding it is worth a fight.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class AmmoCrate : MonoBehaviour
    {
        public static readonly List<AmmoCrate> All = new List<AmmoCrate>(8);

        [SerializeField] private int rounds = 120;
        [Tooltip("Armour restored per use (soldiers carry 50 max).")]
        [SerializeField] private float armor = 50f;
        [SerializeField] private float cooldown = 20f;
        [SerializeField] private Light indicator;
        [SerializeField] private Renderer indicatorRenderer;

        private float _readyAt;
        public bool IsReady => Time.time >= _readyAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => All.Clear();

        private void OnEnable()
        {
            if (!All.Contains(this)) All.Add(this);
        }

        private void OnDisable() => All.Remove(this);

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

            if (ready && Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame)
            {
                var lp = Combat.Combatant.FindLocalPlayer();
                var soldier = lp != null ? lp.GetComponent<SoldierController>() : null;
                if (soldier != null && !soldier.IsDead && Vector3.Distance(soldier.transform.position, transform.position) <= 3.0f)
                    TryResupply(soldier);
            }
        }

        private void OnTriggerStay(Collider other)
        {
            if (Time.time < _readyAt) return;
            var soldier = other.GetComponentInParent<SoldierController>();
            if (soldier == null || soldier.IsDead) return;
            TryResupply(soldier);
        }

        private void TryResupply(SoldierController soldier)
        {
            int ammo = soldier.AddReserveAmmo(rounds);
            float plates = soldier.Health != null ? soldier.Health.AddArmor(armor) : 0f;
            if (ammo <= 0 && plates <= 0f) return;
            _readyAt = Time.time + cooldown;
            var s = LR2CommonSounds.Instance;
            if (s != null)
            {
                LR2Audio.PlayAt(s.ammoPickup, transform.position);
                if (plates > 0f && s.hitmarkerArmor.IsValid) LR2Audio.PlayAt(s.hitmarkerArmor, transform.position, 0.7f);
            }

            var local = Combat.Combatant.FindLocalPlayer();
            if (local != null && local.gameObject == soldier.gameObject)
            {
                string msg = ammo > 0 && plates > 0f ? $"+{ammo} AMMO   +{Mathf.CeilToInt(plates)} ARMOR"
                           : plates > 0f ? $"+{Mathf.CeilToInt(plates)} ARMOR" : $"+{ammo} AMMO";
                UI.HUDController.Notify(msg);
            }
        }
    }
}
