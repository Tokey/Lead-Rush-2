using System.Collections.Generic;
using KINEMATION.FPSAnimationFramework.Runtime.Playables;
using LR2.Weapons;
using UnityEngine;

namespace LR2.Characters
{
    /// <summary>
    /// Puts Kinemation's weapon reload onto the VISIBLE gun and hands.
    ///
    /// The demo reload clips are generic clips. On the humanoid driver the arm curves are ignored, the upper-body mask
    /// blocks <c>Skeleton/WeaponBone</c>, and nothing maps the animated helper bones (<c>Skeleton/ik_hand_gun</c>,
    /// <c>Skeleton/IK_Hand_Left/Right</c>) onto the procedural IK targets, so the driver's gun never tilts and its left hand
    /// never leaves the handguard. The helper bones DO animate, so this replays them:
    /// <list type="bullet">
    /// <item>Gun: the clip gun's motion is measured in its OWN rest frame and applied in the visible gun's local frame,
    /// on top of the current procedural pose. Looking up/down, walking bob and sway therefore never rotate the reload
    /// around the wrong axes (the old skeleton-space version broke while moving or pitching).</item>
    /// <item>Hands: both follow the clip's hand targets relative to the moving gun, calibrated at reload start so the
    /// first frame matches the procedural grip exactly. The left hand blends in with the clip's MaskAttachHand curve,
    /// the right hand (charging handle on empty reloads) with the overall reload weight.</item>
    /// <item>Safety: the gun delta is clamped (angle and offset), so bad data can never swing the rifle into the face.</item>
    /// </list>
    /// </summary>
    public class ReloadMotionBridge
    {
        [System.Serializable]
        public struct Rest
        {
            public bool valid;
            public Vector3 gunPos, handLPos, handRPos;
            public Quaternion gunRot, handLRot, handRRot;
        }

        /// <summary>Largest gun rotation (degrees) the reload may add on top of the procedural pose.</summary>
        public static float MaxGunAngle = 38f;
        /// <summary>Largest gun translation (m) the reload may add on top of the procedural pose.</summary>
        public static float MaxGunOffset = 0.18f;
        /// <summary>Per-axis limits of the reload gun motion, degrees (x = pitch, muzzle up/down; y = yaw; z = roll/cant). The cant
        /// carries a believable mag change; pitch is kept small so the muzzle stays roughly forward (B35: the front view read as a
        /// vertical rifle with the old -26..22 pitch range).</summary>
        public static Vector2 PitchRange = new Vector2(-12f, 12f), YawRange = new Vector2(-18f, 18f), RollRange = new Vector2(-34f, 34f);
        /// <summary>
        /// B35: Kinemation already moves the driver's weapon bone with the clip, weighted by the clip's WeaponBoneWeight curve, and the
        /// visible gun inherits that. 1 = the bridge only adds the share Kinemation has not applied (1 - WeaponBoneWeight); 0 = old
        /// behaviour (full clip motion on top, which stood the rifle up mid-reload).
        /// </summary>
        public static float WeaponBoneOverlap = 0f; // Must stay 0: Kinemation does not move the weapon bone on this rig (A/B pose checks 11-18 00:24 AK-12 and 04:14 MK18: base turn 2-4 deg; overlap 1 froze the gun, 0 deg reload motion).

        private static readonly Dictionary<AnimationClip, Rest> RestCache = new Dictionary<AnimationClip, Rest>();

        private readonly Transform _skeleton, _gun, _handL, _handR;
        private readonly IPlayablesController _playables;
        private float _gunWeight;
        private bool _calibrated;
        private Quaternion _clipToVisible = Quaternion.identity;   // O: visibleLocal = clipLocal * O at reload start
        private Quaternion _gunRotS0 = Quaternion.identity;        // base gun rotation in skeleton space at reload start (QA)
        private Quaternion _rotOffL = Quaternion.identity, _rotOffR = Quaternion.identity;
        private Vector3 _posOffL, _posOffR;
        private AnimationClip _clip;
        private LR2Firearm _lastFirearm;
        private Transform _lastItem;

        public bool Valid => _skeleton != null && _gun != null && _handL != null && _handR != null;
        public float GunWeight => _gunWeight;

        // B16/D40: once the reload stops (tactical end at mag-in, sprint interrupt) the helper bones blend back to
        // locomotion, so the bridge replays the last clip sample while it eases out instead of re-reading them.
        private Quaternion _heldRotV = Quaternion.identity, _heldLRelRot = Quaternion.identity, _heldRRelRot = Quaternion.identity;
        private Vector3 _heldPosV, _heldLRel, _heldRRel;
        private float _heldRelease, _heldWbw;
        private bool _hasHeld;
        /// <summary>Seconds the reload motion takes to settle back to ready after the reload stops (smoothstep ease).</summary>
        public static float FadeOutSeconds = 0.35f;
        public float HandWeight { get; private set; }
        /// <summary>Rotation (degrees) the reload added to the visible gun this frame (QA).</summary>
        public float LastGunAngle { get; private set; }
        /// <summary>Translation (m) the reload added to the visible gun this frame (QA).</summary>
        public float LastGunOffset { get; private set; }
        /// <summary>Clip WeaponBoneWeight curve this frame (how much Kinemation itself moves the weapon bone).</summary>
        public float LastWeaponBoneWeight { get; private set; }
        /// <summary>How far (degrees) the BASE gun pose turned since reload start, before the bridge adds anything (QA).</summary>
        public float LastBaseAngle { get; private set; }

        public ReloadMotionBridge(Transform driverRoot, IPlayablesController playables)
        {
            _skeleton = driverRoot.Find("Skeleton");
            if (_skeleton == null) return;
            _gun = _skeleton.Find("ik_hand_gun");
            _handL = _skeleton.Find("IK_Hand_Left");
            _handR = _skeleton.Find("IK_Hand_Right");
            _playables = playables;
        }

        /// <summary>The clip's first frame for the helper bones (baked by the builder on the firearm when available).</summary>
        private static Rest GetRest(LR2Firearm gun, AnimationClip clip)
        {
            if (clip == null) return default;
            if (gun != null && gun.ReloadRestPose.valid)
            {
                RestCache[clip] = gun.ReloadRestPose;
                return gun.ReloadRestPose;
            }
            if (RestCache.TryGetValue(clip, out var r)) return r;
            var root = new GameObject("ReloadRestProbe") { hideFlags = HideFlags.HideAndDontSave };
            root.AddComponent<Animator>();
            var skel = new GameObject("Skeleton").transform;
            skel.SetParent(root.transform, false);
            Transform Make(string n) { var t = new GameObject(n).transform; t.SetParent(skel, false); return t; }
            var gunTf = Make("ik_hand_gun");
            var hl = Make("IK_Hand_Left");
            var hr = Make("IK_Hand_Right");
            try
            {
                clip.SampleAnimation(root, 0f);
                // an unsampled hierarchy stays at identity: treat that as a failure instead of swinging the gun 90 degrees
                bool sampled = gunTf.localRotation != Quaternion.identity || gunTf.localPosition != Vector3.zero;
                r = new Rest
                {
                    valid = sampled,
                    gunPos = gunTf.localPosition, gunRot = gunTf.localRotation,
                    handLPos = hl.localPosition, handLRot = hl.localRotation,
                    handRPos = hr.localPosition, handRRot = hr.localRotation,
                };
            }
            catch (System.Exception) { r = default; }
            Object.Destroy(root);
            RestCache[clip] = r;
            return r;
        }

        private static Quaternion ClampRotation(Quaternion q, float maxDeg)
        {
            q.ToAngleAxis(out float angle, out Vector3 axis);
            if (float.IsNaN(axis.x) || axis.sqrMagnitude < 1e-8f) return Quaternion.identity;
            if (angle > 180f) angle -= 360f;
            return Quaternion.AngleAxis(Mathf.Clamp(angle, -maxDeg, maxDeg), axis);
        }

        /// <summary>
        /// Call every LateUpdate after the weapon is placed. Moves <paramref name="item"/> by the clip's gun motion and
        /// outputs world-space hand targets in the driver-hand convention. Returns false when no reload motion applies.
        /// </summary>
        public bool Apply(LR2Firearm gun, Transform item, Transform driverHandL, Transform driverHandR,
                          ref Vector3 leftPos, ref Quaternion leftRot, ref Vector3 rightPos, ref Quaternion rightRot)
        {
            if (gun != _lastFirearm || item != _lastItem)
            {
                _lastFirearm = gun;
                _lastItem = item;
                _clip = null;
                _calibrated = false;
                _gunWeight = 0f;
                HandWeight = 0f;
                _clipToVisible = Quaternion.identity;
                _gunRotS0 = Quaternion.identity;
                _rotOffL = _rotOffR = Quaternion.identity;
                _posOffL = _posOffR = Vector3.zero;
                LastGunAngle = LastGunOffset = LastBaseAngle = LastWeaponBoneWeight = 0f;
                _hasHeld = false;
            }
            bool reloading = gun != null && gun.IsReloading && gun.ReloadClip != null && gun.ReloadClip.clip != null;
            _gunWeight = Mathf.MoveTowards(_gunWeight, reloading ? 1f : 0f, Time.deltaTime / (reloading ? 0.18f : Mathf.Max(0.05f, FadeOutSeconds)));
            if (!reloading && !_hasHeld) _gunWeight = 0f; // nothing sampled yet: nothing to ease out
            LastGunAngle = 0f;
            LastGunOffset = 0f;
            if (!Valid || item == null || _gunWeight <= 0.001f)
            {
                _calibrated = false;
                _hasHeld = false;
                HandWeight = 0f;
                return false;
            }
            var clip = reloading ? gun.ReloadClip.clip : _clip;
            if (clip == null) return false;
            if (clip != _clip) { _clip = clip; _calibrated = false; }
            var rest = GetRest(gun, clip);
            if (!rest.valid) return false;

            Vector3 gPos = item.position;
            Quaternion gRot = item.rotation;
            Quaternion invRest = Quaternion.Inverse(rest.gunRot);

            if (!_calibrated)
            {
                // O maps the clip gun's local frame onto the visible gun's local frame (both "at rest" right now).
                Quaternion gRotS = Quaternion.Inverse(_skeleton.rotation) * gRot;
                _clipToVisible = invRest * gRotS;
                _gunRotS0 = gRotS;
                Quaternion invO0 = Quaternion.Inverse(_clipToVisible);
                // hand convention offsets: where the clip's rest hands land on the visible gun vs where the real hands are
                Vector3 hl0 = gPos + gRot * (invO0 * (invRest * (rest.handLPos - rest.gunPos)));
                Quaternion hl0r = gRot * (invO0 * (invRest * rest.handLRot));
                Vector3 hr0 = gPos + gRot * (invO0 * (invRest * (rest.handRPos - rest.gunPos)));
                Quaternion hr0r = gRot * (invO0 * (invRest * rest.handRRot));
                if (driverHandL != null)
                {
                    _rotOffL = Quaternion.Inverse(hl0r) * driverHandL.rotation;
                    _posOffL = Quaternion.Inverse(hl0r) * (driverHandL.position - hl0);
                }
                if (driverHandR != null)
                {
                    _rotOffR = Quaternion.Inverse(hr0r) * driverHandR.rotation;
                    _posOffR = Quaternion.Inverse(hr0r) * (driverHandR.position - hr0);
                }
                _calibrated = true;
            }
            Quaternion O = _clipToVisible, invO = Quaternion.Inverse(O);
            LastBaseAngle = Quaternion.Angle(_gunRotS0, Quaternion.Inverse(_skeleton.rotation) * gRot);
            float wbw = 0f;
            Quaternion dRotV, lRelRot, rRelRot;
            Vector3 dPosV, lRel, rRel;
            float release = 0f;
            if (reloading)
            {
                if (_playables != null)
                {
                    try { wbw = Mathf.Clamp01(_playables.GetCurveValue("WeaponBoneWeight")); } catch (System.Exception) { wbw = 0f; }
                    try { release = Mathf.Clamp01(_playables.GetCurveValue("MaskAttachHand")); } catch (System.Exception) { release = 0f; }
                }

                // Clip gun delta in its own rest frame, re-expressed in the visible gun's frame.
                Quaternion cRot = _gun.localRotation;
                Vector3 cPos = _gun.localPosition;
                Quaternion rawRot = ClampRotation(invO * (invRest * cRot) * O, MaxGunAngle);
                Vector3 euler = rawRot.eulerAngles;
                float rx = euler.x > 180f ? euler.x - 360f : euler.x;
                float ry = euler.y > 180f ? euler.y - 360f : euler.y;
                float rz = euler.z > 180f ? euler.z - 360f : euler.z;
                // Keep barrel tactical (prevent vertical 90 deg pointing into helmet in 3P or shoulder clipping 1P camera)
                dRotV = Quaternion.Euler(Mathf.Clamp(rx, PitchRange.x, PitchRange.y), Mathf.Clamp(ry, YawRange.x, YawRange.y), Mathf.Clamp(rz, RollRange.x, RollRange.y));
                dPosV = Vector3.ClampMagnitude(invO * (invRest * (cPos - rest.gunPos)), MaxGunOffset);
                dPosV = new Vector3(Mathf.Clamp(dPosV.x, -0.09f, 0.06f), Mathf.Clamp(dPosV.y, -0.05f, 0.09f), Mathf.Clamp(dPosV.z, -0.015f, 0.14f));

                // clip hand targets relative to the clip gun (re-expressed in the visible gun's frame)
                Quaternion invC = Quaternion.Inverse(cRot);
                lRel = invO * (invC * (_handL.localPosition - cPos));
                lRelRot = invO * (invC * _handL.localRotation);
                rRel = invO * (invC * (_handR.localPosition - cPos));
                rRelRot = invO * (invC * _handR.localRotation);

                _heldRotV = dRotV; _heldPosV = dPosV;
                _heldLRel = lRel; _heldLRelRot = lRelRot; _heldRRel = rRel; _heldRRelRot = rRelRot;
                _heldRelease = release; _heldWbw = wbw;
                _hasHeld = true;
            }
            else
            {
                dRotV = _heldRotV; dPosV = _heldPosV;
                lRel = _heldLRel; lRelRot = _heldLRelRot; rRel = _heldRRel; rRelRot = _heldRRelRot;
                release = _heldRelease; wbw = _heldWbw;
            }
            LastWeaponBoneWeight = wbw;
            float eased = _gunWeight * _gunWeight * (3f - 2f * _gunWeight); // smoothstep: no velocity step at either end
            float motion = eased * (1f - wbw * Mathf.Clamp01(WeaponBoneOverlap));

            Quaternion dRotW = Quaternion.Slerp(Quaternion.identity, dRotV, motion);
            Quaternion newRot = gRot * dRotW;
            Vector3 newPos = gPos + gRot * (dPosV * motion);
            LastGunAngle = Quaternion.Angle(gRot, newRot);
            LastGunOffset = Vector3.Distance(gPos, newPos);

            // rigid carry for anything still holding the gun
            Quaternion carryRot = newRot * Quaternion.Inverse(gRot);
            Vector3 Carry(Vector3 p) => newPos + carryRot * (p - gPos);

            // clip hand targets -> onto the moved visible gun (+ calibration offsets)
            Quaternion lFreeRot = newRot * lRelRot;
            Quaternion rFreeRot = newRot * rRelRot;
            Vector3 lFreePos = newPos + newRot * lRel + lFreeRot * _posOffL;
            Vector3 rFreePos = newPos + newRot * rRel + rFreeRot * _posOffR;
            lFreeRot *= _rotOffL;
            rFreeRot *= _rotOffR;

            HandWeight = release * eased;

            // Right hand: follows the clip relative to the gun (stays on the grip, works the charging handle on empty reloads).
            Vector3 rCarry = Carry(rightPos);
            Quaternion rCarryRot = carryRot * rightRot;
            rightPos = Vector3.Lerp(rCarry, rFreePos, eased);
            rightRot = Quaternion.Slerp(rCarryRot, rFreeRot, eased);
            // Left hand: rides on the gun while attached, follows the clip while released (mag out, pouch, mag in).
            Vector3 lCarry = Carry(leftPos);
            Quaternion lCarryRot = carryRot * leftRot;
            leftPos = Vector3.Lerp(lCarry, lFreePos, HandWeight);
            leftRot = Quaternion.Slerp(lCarryRot, lFreeRot, HandWeight);

            item.SetPositionAndRotation(newPos, newRot);
            return true;
        }
    }
}
