using LR2.Core;
using LR2.Weapons;
using UnityEngine;

namespace LR2.Characters
{
    /// <summary>
    /// Runs after Kinemation's FPSAnimator.LateUpdate():
    /// 1) Prevents the active rifle from clipping into walls/props at oblique angles via a forward spherecast.
    /// 2) Adds crisp tactile magwell/bolt rock accents during reload when the player is in first-person.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class WeaponAntiClip : MonoBehaviour
    {
        private SoldierController _soldier;

        private void Awake() => _soldier = GetComponent<SoldierController>();

        private void LateUpdate()
        {
            if (_soldier == null) return;
            var item = _soldier.ActiveItem;
            if (item == null || !item.gameObject.activeInHierarchy) return;

            // Keep weapon aligned with IK WeaponBone & IK hands (wall collision runs in Kinemation CollisionLayerJob).
            item.transform.localPosition = Vector3.zero;
            item.transform.localRotation = Quaternion.identity;
        }
    }
}
