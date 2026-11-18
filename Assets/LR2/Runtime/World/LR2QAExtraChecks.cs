using System;
using System.Reflection;
using LR2.Characters;
using LR2.Core;
using LR2.Weapons;
using UnityEngine;

namespace LR2.World
{
    /// <summary>Focused runtime assertions for regressions found after the original QA bot was written.</summary>
    public static class LR2QAExtraChecks
    {
        private const float MaxGunAngle = 50f;
        private const float MaxFootSlideSpeed = 0.5f;
        private const float MaxHandError = 0.03f;

        public static string CheckAbsoluteReloadPose(SoldierController soldier)
        {
            if (soldier == null || soldier.ActiveFirearm == null || soldier.ViewTransform == null)
                return "FAIL Reload.AbsolutePose: missing soldier, firearm, or view transform";

            var gun = soldier.ActiveFirearm.transform;
            var view = soldier.ViewTransform;
            float forwardAngle = Vector3.Angle(gun.forward, view.forward);
            Vector3 gunUp = Vector3.ProjectOnPlane(gun.up, view.forward);
            Vector3 viewUp = Vector3.ProjectOnPlane(view.up, view.forward);
            float roll = gunUp.sqrMagnitude > 1e-6f && viewUp.sqrMagnitude > 1e-6f
                ? Vector3.SignedAngle(viewUp, gunUp, view.forward)
                : 0f;
            bool pass = forwardAngle <= MaxGunAngle && Mathf.Abs(roll) <= MaxGunAngle;
            return $"{(pass ? "PASS" : "FAIL")} Reload.AbsolutePose: forward={forwardAngle:0.0}deg roll={roll:0.0}deg max={MaxGunAngle:0.0}";
        }

        public static string CheckTacticalReload(LR2Firearm gun, int ammoAfter, float maxChargeTravel)
        {
            if (gun == null)
                return "FAIL Reload.TacticalInvariant: missing firearm";

            bool chargeStable = maxChargeTravel < 0.02f;
            bool ammoValid = ammoAfter == gun.MagazineSize + 1;
            return $"{(chargeStable && ammoValid ? "PASS" : "FAIL")} Reload.TacticalInvariant: charge={maxChargeTravel:0.000}m ammo={ammoAfter}/{gun.MagazineSize + 1}";
        }

        public static string CheckFootSlide(Vector3 previousFootPosition, Vector3 currentFootPosition, float deltaTime)
        {
            if (deltaTime <= 0f)
                return "FAIL Movement.FootSlide: non-positive sample interval";

            Vector3 delta = currentFootPosition - previousFootPosition;
            delta.y = 0f;
            float speed = delta.magnitude / deltaTime;
            return $"{(speed < MaxFootSlideSpeed ? "PASS" : "FAIL")} Movement.FootSlide: speed={speed:0.000}m/s max={MaxFootSlideSpeed:0.000}";
        }

        public static string CheckHandsOnGun(RetargetProxy proxy)
        {
            if (proxy == null)
                return "FAIL Hands.OnGun: missing retarget proxy";

            float left = proxy.HandErrorLeft;
            float right = proxy.HandErrorRight;
            bool pass = left >= 0f && right >= 0f && left < MaxHandError && right < MaxHandError;
            return $"{(pass ? "PASS" : "FAIL")} Hands.OnGun: left={left:0.000}m right={right:0.000}m max={MaxHandError:0.000}m";
        }

        public static string CheckCameraShake(float traumaBeforeFire, float traumaAfterFire, float traumaAfterRecovery, float recoverySeconds)
        {
            bool rises = traumaAfterFire > traumaBeforeFire;
            bool recovers = traumaAfterRecovery <= 0.001f && recoverySeconds <= 1.5f;
            return $"{(rises && recovers ? "PASS" : "FAIL")} CameraShake.Response: before={traumaBeforeFire:0.000} after={traumaAfterFire:0.000} recovered={traumaAfterRecovery:0.000} recovery={recoverySeconds:0.00}s";
        }

        public static string CheckHorizontalFov()
        {
            float standard = LR2Settings.VerticalFov(90f, 16f / 9f);
            float ultrawide = LR2Settings.VerticalFov(90f, 21f / 9f);
            bool pass = Mathf.Abs(standard - ultrawide) <= 0.001f;
            return $"{(pass ? "PASS" : "FAIL")} Camera.HorizontalFov: 16:9={standard:0.000} 21:9={ultrawide:0.000}";
        }

        public static bool TryGetCameraShakeTrauma(out float trauma)
        {
            trauma = 0f;
            var instance = CameraShake.Instance;
            if (instance == null) return false;

            FieldInfo field = typeof(CameraShake).GetField("_trauma", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null || field.FieldType != typeof(float)) return false;
            trauma = (float)field.GetValue(instance);
            return true;
        }
    }
}
