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
    /// <c>Skeleton/IK_Hand_Left/Right</c>) onto the procedural IK targets. So the driver's gun never tilts and its left
    /// hand never leaves the handguard. The helper bones DO animate, though: this replays their motion on the visible rifle
    /// (gun pose relative to the clip's rest pose) and returns hand targets relative to that moving gun. The left hand is
    /// weighted by the clip's <c>MaskAttachHand</c> curve (1 = hand off the gun for the mag swap).
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

        private static readonly Dictionary<AnimationClip, Rest> RestCache = new Dictionary<AnimationClip, Rest>();

        private readonly Transform _skeleton, _gun, _handL, _handR;
        private readonly IPlayablesController _playables;
        private float _gunWeight;
        private bool _calibrated;
        private Quaternion _rotOffL = Quaternion.identity, _rotOffR = Quaternion.identity;
        private Vector3 _posOffL, _posOffR;
        private AnimationClip _clip;

        public bool Valid => _skeleton != null && _gun != null && _handL != null && _handR != null;
        public float GunWeight => _gunWeight;
        public float HandWeight { get; private set; }

        public ReloadMotionBridge(Transform driverRoot, IPlayablesController playables)
        {
            _skeleton = driverRoot.Find("Skeleton");
            if (_skeleton == null) return;
            _gun = _skeleton.Find("ik_hand_gun");
            _handL = _skeleton.Find("IK_Hand_Left");
            _handR = _skeleton.Find("IK_Hand_Right");
            _playables = playables;
        }

        /// <summary>The clip's first frame for the helper bones (uses baked clip curves on the firearm when available).</summary>
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
                r = new Rest
                {
                    valid = true,
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

        /// <summary>
        /// Call every LateUpdate after the weapon is placed. Moves <paramref name="item"/> by the clip's gun motion and
        /// outputs world-space hand targets in the driver-hand convention: (position, rotation) for left and right.
        /// Returns false when no reload motion applies (targets untouched).
        /// </summary>
        public bool Apply(LR2Firearm gun, Transform item, Transform driverHandL, Transform driverHandR,
                          ref Vector3 leftPos, ref Quaternion leftRot, ref Vector3 rightPos, ref Quaternion rightRot)
        {
            bool reloading = gun != null && gun.IsReloading && gun.ReloadClip != null && gun.ReloadClip.clip != null;
            _gunWeight = Mathf.MoveTowards(_gunWeight, reloading ? 1f : 0f, Time.deltaTime / 0.18f);
            if (!Valid || item == null || _gunWeight <= 0.001f)
            {
                _calibrated = false;
                HandWeight = 0f;
                return false;
            }
            var clip = reloading ? gun.ReloadClip.clip : _clip;
            if (clip == null) return false;
            if (clip != _clip) { _clip = clip; _calibrated = false; }
            var rest = GetRest(gun, clip);
            if (!rest.valid) return false;

            // Rigid motion of the clip's gun relative to its rest pose, in skeleton space.
            Quaternion skelRot = _skeleton.rotation, invSkel = Quaternion.Inverse(skelRot);
            Quaternion motionRot = _gun.localRotation * Quaternion.Inverse(rest.gunRot);
            Vector3 MoveS(Vector3 pS) => motionRot * (pS - rest.gunPos) + _gun.localPosition;

            // Visible gun: skeleton space -> moved -> world (weighted).
            Vector3 gS = _skeleton.InverseTransformPoint(item.position);
            Quaternion gRotS = invSkel * item.rotation;
            Vector3 gMovedS = MoveS(gS);
            Quaternion gMovedRotS = motionRot * gRotS;
            Vector3 gMovedW = _skeleton.TransformPoint(Vector3.Lerp(gS, gMovedS, _gunWeight));
            Quaternion gMovedRotW = skelRot * Quaternion.Slerp(gRotS, gMovedRotS, _gunWeight);
            // rigid delta the visible gun went through this frame (applied to hands that stay on it)
            Quaternion deltaRot = gMovedRotW * Quaternion.Inverse(item.rotation);
            Vector3 itemPos = item.position;
            Vector3 Carry(Vector3 p) => gMovedW + deltaRot * (p - itemPos);

            // Clip hand targets: follow the clip directly, offset by where the visible gun sits relative to the clip's gun
            Vector3 offsetS = gMovedS - _gun.localPosition;
            Vector3 lClipW = _skeleton.TransformPoint(_handL.localPosition + offsetS * _gunWeight);
            Vector3 rClipW = _skeleton.TransformPoint(_handR.localPosition + offsetS * _gunWeight);
            Quaternion lClipRotW = skelRot * _handL.localRotation;
            Quaternion rClipRotW = skelRot * _handR.localRotation;

            // Calibrate the helper-bone convention against the real (attached) hands while the reload is just starting.
            if (!_calibrated && driverHandL != null && driverHandR != null)
            {
                Vector3 l0 = _skeleton.TransformPoint(rest.handLPos + (gS - rest.gunPos));
                Vector3 r0 = _skeleton.TransformPoint(rest.handRPos + (gS - rest.gunPos));
                Quaternion l0r = skelRot * rest.handLRot, r0r = skelRot * rest.handRRot;
                _rotOffL = Quaternion.Inverse(l0r) * driverHandL.rotation;
                _rotOffR = Quaternion.Inverse(r0r) * driverHandR.rotation;
                _posOffL = Quaternion.Inverse(l0r) * (driverHandL.position - l0);
                _posOffR = Quaternion.Inverse(r0r) * (driverHandR.position - r0);
                _calibrated = true;
            }

            float release = 0f;
            if (_playables != null)
            {
                try { release = Mathf.Clamp01(_playables.GetCurveValue("MaskAttachHand")); } catch (System.Exception) { release = 0f; }
            }
            HandWeight = release * _gunWeight;

            // Right hand: rides on the gun (it never lets go of the grip during a reload).
            rightPos = Carry(rightPos);
            rightRot = deltaRot * rightRot;
            // Left hand: rides on the gun while attached, follows the clip while released.
            Vector3 lAttachedPos = Carry(leftPos);
            Quaternion lAttachedRot = deltaRot * leftRot;
            Quaternion lFreeRot = lClipRotW * _rotOffL;
            Vector3 lFreePos = lClipW + lClipRotW * _posOffL;
            leftPos = Vector3.Lerp(lAttachedPos, lFreePos, HandWeight);
            leftRot = Quaternion.Slerp(lAttachedRot, lFreeRot, HandWeight);

            item.SetPositionAndRotation(gMovedW, gMovedRotW);
            return true;
        }
    }
}
