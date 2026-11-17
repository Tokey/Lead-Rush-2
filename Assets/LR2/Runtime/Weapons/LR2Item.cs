// Based on KINEMATION FPS Animation Framework demo FPSItem (2025); field names kept identical so demo prefab data
// carries over when the LR2 builder swaps components.
using KINEMATION.FPSAnimationFramework.Runtime.Layers.IkMotionLayer;
using LR2.Characters;
using UnityEngine;

namespace LR2.Weapons
{
    public abstract class LR2Item : MonoBehaviour
    {
        [SerializeField] protected RuntimeAnimatorController overrideController;
        [SerializeField] protected IkMotionLayerSettings equipMotion;
        [SerializeField] protected IkMotionLayerSettings unEquipMotion;

        [Header("LR2 Item")]
        [SerializeField] protected string displayNameOverride;

        public SoldierController Owner { get; protected set; }
        public virtual string DisplayName => string.IsNullOrEmpty(displayNameOverride) ? name.Replace("(Clone)", "").Trim() : displayNameOverride;

        /// <summary>True while the item blocks other actions (reloading, equipping...).</summary>
        public virtual bool IsBusy => false;

        public virtual void OnEquip(SoldierController owner) { Owner = owner; }
        public virtual void OnUnEquip() { }
        public virtual bool OnAimPressed() { return false; }
        public virtual bool OnAimReleased() { return false; }
        public virtual bool OnFirePressed() { return false; }
        public virtual bool OnFireReleased() { return false; }
        public virtual bool OnReload() { return false; }
        public virtual void OnCycleScope() { }
        public virtual void OnChangeFireMode() { }
        public virtual void Tick(float deltaTime) { }
        public virtual void CancelActions() { }
        public virtual void RefillAmmo() { }
        public virtual int AddReserveAmmo(int rounds) { return 0; }
        public virtual void UpdateFieldOfView(bool aiming) { }
    }
}
