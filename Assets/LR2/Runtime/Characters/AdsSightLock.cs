using KINEMATION.FPSAnimationFramework.Runtime.Core;
using LR2.Weapons;
using UnityEngine;

namespace LR2.Characters
{
    /// <summary>
    /// B91 (user 11-19 00:13: "when ADSing, the reticle should never move fully away, it can sway a little bit, but through the
    /// hole most of the time the iron sight in front needs to be seen ... like COD or BF6"). Bullets fly along the view
    /// (<see cref="SoldierController.AimDirection"/>), so any gun motion relative to the camera puts the sights somewhere the
    /// bullets do not go. Kinemation sway, recoil and camera shake all move the gun or the camera alone.
    ///
    /// Near full ADS this keeps the weapon's aim point (the eye point Kinemation's ADS layer puts at the camera) within
    /// <see cref="maxAngle"/> of the view direction and <see cref="maxOffset"/> of the view axis, by rotating the gun about the
    /// aim point and sliding it, only by the excess. Small sway stays; the front post never leaves the rear aperture.
    /// Runs after WeaponAntiClip (200, which resets the gun's local pose every frame, so nothing accumulates) and before
    /// RetargetProxy (250), so the hands follow the corrected gun. First-person (local player) only.
    /// </summary>
    [DefaultExecutionOrder(230)]
    public class AdsSightLock : MonoBehaviour
    {
        [Tooltip("Largest angle (deg) between the sight line and the view at full ADS.")]
        [SerializeField] private float maxAngle = 0.35f;
        [Tooltip("Largest distance (m) between the aim point and the view axis at full ADS.")]
        [SerializeField] private float maxOffset = 0.0035f;
        [Tooltip("ADS blend where the lock starts to apply (it reaches full strength at 1).")]
        [SerializeField, Range(0f, 1f)] private float startBlend = 0.6f;

        private SoldierController _soldier;
        private LR2Firearm _cachedGun;
        private FPSAnimatorEntity _cachedEntity;

        /// <summary>Last correction applied (deg, m), for QA.</summary>
        public Vector2 LastCorrection { get; private set; }

        private void Awake() => _soldier = GetComponent<SoldierController>();

        private void LateUpdate()
        {
            LastCorrection = Vector2.zero;
            if (_soldier == null || !_soldier.IsLocalPlayer || _soldier.IsDead) return;
            float blend = _soldier.AimBlend;
            if (blend <= startBlend) return;
            var gun = _soldier.ActiveFirearm;
            var cam = _soldier.ViewCamera;
            if (gun == null || cam == null || !gun.gameObject.activeInHierarchy || gun.IsReloading) return;
            if (gun != _cachedGun)
            {
                _cachedGun = gun;
                _cachedEntity = gun.GetComponent<FPSAnimatorEntity>();
            }
            Transform ap = _cachedEntity != null ? _cachedEntity.defaultAimPoint : null;
            if (ap == null) return;

            float w = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(startBlend, 1f, blend));
            Transform g = gun.transform;
            Transform view = cam.transform;

            // 1) direction: rotate the gun about the aim point by the part of the error beyond the allowed cone
            Quaternion toView = Quaternion.FromToRotation(ap.forward, view.forward);
            toView.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            float allowedAngle = Mathf.Lerp(25f, maxAngle, w);
            float excessAngle = 0f;
            if (Mathf.Abs(angle) > allowedAngle && axis.sqrMagnitude > 1e-6f)
            {
                excessAngle = angle - Mathf.Sign(angle) * allowedAngle;
                Quaternion corr = Quaternion.AngleAxis(excessAngle, axis);
                Vector3 pivot = ap.position;
                g.SetPositionAndRotation(pivot + corr * (g.position - pivot), corr * g.rotation);
            }

            // 2) position: slide the gun so the aim point stays near the view axis (camera-space x/y)
            Vector3 local = view.InverseTransformPoint(ap.position);
            Vector2 lateral = new Vector2(local.x, local.y);
            float allowedOffset = Mathf.Lerp(0.05f, maxOffset, w);
            float excessOffset = 0f;
            if (lateral.magnitude > allowedOffset)
            {
                Vector2 move = lateral.normalized * (lateral.magnitude - allowedOffset);
                excessOffset = move.magnitude;
                g.position -= view.TransformVector(new Vector3(move.x, move.y, 0f));
            }
            LastCorrection = new Vector2(Mathf.Abs(excessAngle), excessOffset);
        }
    }
}
