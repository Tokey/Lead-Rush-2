using LR2.Combat;
using UnityEngine;

namespace LR2.Characters
{
    /// <summary>
    /// Visual body proxy. Kinemation's humanoid FPS rig (the hidden "driver") does every animation (grip, ADS, reloads,
    /// equips for every demo weapon) and carries the first-person camera on its head. Every frame this copies the
    /// driver's pose onto the SWAT model through Unity's humanoid muscle space, then:
    /// <list type="number">
    /// <item>Third person (bots, remote players): slides the SWAT hips horizontally onto the driver's hips, so the body,
    /// its hitboxes and the gun all line up.</item>
    /// <item>First person (local player): pins the SWAT head onto the driver's head, so the camera sits at the soldier's
    /// own eyes, nudged back a little so the collar never reaches the lens. Leg IK then re-plants the feet.</item>
    /// <item>Two-bone IK locks the SWAT hands onto the driver's hands (fingers on grip / handguard / magazine).</item>
    /// </list>
    /// Stops on death so the SWAT ragdoll takes over.
    /// </summary>
    [DefaultExecutionOrder(250)] // after WeaponAntiClip (200) and FPSAnimator.LateUpdate, before bodycam (1100)
    public class RetargetProxy : MonoBehaviour
    {
        [SerializeField] private Animator driver;
        [SerializeField] private Animator visual;

        [Header("Hands")]
        [Tooltip("Lock the visual hands onto the driver hands (grip accuracy).")]
        [SerializeField] private bool handIK = true;
        [SerializeField, Range(0f, 1f)] private float handIKWeight = 1f;

        [Header("Body alignment")]
        [Tooltip("Third person: slide the visible body so its hips sit exactly over the driver's hips (body, hitboxes and gun line up).")]
        [SerializeField] private bool alignHips = true;
        [Tooltip("First person (local player): move the visible body so its EYES sit at the camera (the driver's eye socket).")]
        [SerializeField] private bool firstPersonHeadLock = true;
        [Tooltip("Visible model's eyes: height above its head joint, along the head's up axis (m).")]
        [SerializeField, Range(0f, 0.2f)] private float eyeHeight = 0.085f;
        [Tooltip("Visible model's eyes: distance in front of its head joint (m).")]
        [SerializeField, Range(0f, 0.2f)] private float eyeForward = 0.095f;
        [Tooltip("First person: shift the visible body this far back along the view (m), so the collar and hood stay behind the lens.")]
        [SerializeField, Range(0f, 0.2f)] private float firstPersonBodyBack = 0.02f;
        [Tooltip("First person: re-plant the feet with leg IK after the head lock moved the body.")]
        [SerializeField] private bool replantFeet = true;
        [Tooltip("Log alignment numbers once, 1.5 s after spawn.")]
        [SerializeField] private bool logAlignment = true;

        private HumanPoseHandler _driverHandler, _visualHandler;
        private HumanPose _pose;
        private Health _health;
        private SoldierController _soldier;
        private Transform _dL, _dR, _dHead, _dHips;
        private Transform _vHips, _vHead, _vHeadTop;
        private Vector3 _eyeUpLocal = Vector3.up, _eyeFwdLocal = Vector3.forward;
        private bool _eyeAxesReady;
        private Transform _lUpper, _lLower, _lHand, _rUpper, _rLower, _rHand;
        private Transform _lThigh, _lShin, _lFoot, _rThigh, _rShin, _rFoot;
        private Quaternion _lOffset = Quaternion.identity, _rOffset = Quaternion.identity;
        private ReloadMotionBridge _bridge;
        private bool _offsetsReady;
        private int _frames;
        private static readonly int ProneWeightHash = Animator.StringToHash("ProneWeight");

        public Animator Driver => driver;
        public Animator Visual => visual;

        /// <summary>Distance between driver and visual hips right after the muscle-space copy (before alignment).</summary>
        public float RawHipsDelta { get; private set; }
        /// <summary>Driver head minus visual head right after the muscle-space copy (before alignment), world space.</summary>
        public Vector3 RawHeadDelta { get; private set; }
        /// <summary>World-space shift applied to the visual skeleton this frame.</summary>
        public Vector3 AppliedShift { get; private set; }
        /// <summary>Residual hand-IK errors (m) after solving.</summary>
        public float HandErrorLeft { get; private set; }
        public float HandErrorRight { get; private set; }
        /// <summary>True while the first-person eye lock is active.</summary>
        public bool FirstPersonActive { get; private set; }

        /// <summary>Estimated eye point of the visible model (world), from its head joint, eyeHeight and eyeForward.</summary>
        public Vector3 VisualEyePosition
        {
            get
            {
                if (_vHead == null) return transform.position;
                EnsureEyeAxes();
                return _vHead.position + _vHead.rotation * (_eyeUpLocal * eyeHeight + _eyeFwdLocal * eyeForward);
            }
        }

        /// <summary>
        /// Head-local "up" (head joint to head top) and "forward" (character facing, perpendicular to up), captured once
        /// from the retargeted pose so the eye point follows every head tilt afterwards.
        /// </summary>
        private void EnsureEyeAxes()
        {
            if (_eyeAxesReady || _vHead == null) return;
            Vector3 up = _vHeadTop != null ? (_vHeadTop.position - _vHead.position) : transform.up;
            if (up.sqrMagnitude < 1e-6f) up = transform.up;
            up.Normalize();
            Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, up);
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.ProjectOnPlane(transform.forward + transform.up * 0.1f, up);
            fwd.Normalize();
            Quaternion inv = Quaternion.Inverse(_vHead.rotation);
            _eyeUpLocal = inv * up;
            _eyeFwdLocal = inv * fwd;
            _eyeAxesReady = true;
        }

        public void Configure(Animator driverAnimator, Animator visualAnimator)
        {
            driver = driverAnimator;
            visual = visualAnimator;
        }

        private void Start()
        {
            _health = GetComponentInParent<Health>();
            _soldier = GetComponentInParent<SoldierController>();
            if (driver == null || visual == null || driver.avatar == null || visual.avatar == null || !driver.avatar.isHuman || !visual.avatar.isHuman)
            {
                Debug.LogWarning("[LR2] RetargetProxy needs two humanoid animators", this);
                enabled = false;
                return;
            }
            _driverHandler = new HumanPoseHandler(driver.avatar, driver.transform);
            _visualHandler = new HumanPoseHandler(visual.avatar, visual.transform);
            _dL = driver.GetBoneTransform(HumanBodyBones.LeftHand);
            _dR = driver.GetBoneTransform(HumanBodyBones.RightHand);
            _dHead = driver.GetBoneTransform(HumanBodyBones.Head);
            _dHips = driver.GetBoneTransform(HumanBodyBones.Hips);
            _vHips = visual.GetBoneTransform(HumanBodyBones.Hips);
            _vHead = visual.GetBoneTransform(HumanBodyBones.Head);
            _vHeadTop = _vHead != null && _vHead.childCount > 0 ? FindHeadTop(_vHead) : null;
            _lUpper = visual.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            _lLower = visual.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            _lHand = visual.GetBoneTransform(HumanBodyBones.LeftHand);
            _rUpper = visual.GetBoneTransform(HumanBodyBones.RightUpperArm);
            _rLower = visual.GetBoneTransform(HumanBodyBones.RightLowerArm);
            _rHand = visual.GetBoneTransform(HumanBodyBones.RightHand);
            _lThigh = visual.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            _lShin = visual.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            _lFoot = visual.GetBoneTransform(HumanBodyBones.LeftFoot);
            _rThigh = visual.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            _rShin = visual.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            _rFoot = visual.GetBoneTransform(HumanBodyBones.RightFoot);
            _bridge = new ReloadMotionBridge(driver.transform, _soldier != null ? _soldier.Playables : null);
        }

        /// <summary>The head's end bone (Mixamo "HeadTop_End"): the child furthest from the head joint, skipping eyes/jaw.</summary>
        private static Transform FindHeadTop(Transform head)
        {
            Transform best = null;
            float bestD = 0f;
            foreach (Transform c in head)
            {
                string n = c.name.ToLowerInvariant();
                if (n.Contains("eye") || n.Contains("jaw")) continue;
                float d = (c.position - head.position).sqrMagnitude;
                if (n.Contains("top")) d += 100f;
                if (d > bestD) { bestD = d; best = c; }
            }
            return best;
        }

        private void OnDestroy()
        {
            _driverHandler?.Dispose();
            _visualHandler?.Dispose();
        }

        private bool IsFirstPerson =>
            _soldier != null && _soldier.IsLocalPlayer && _soldier.ViewCamera != null && _soldier.ViewCamera.isActiveAndEnabled;

        private void LateUpdate()
        {
            if (_driverHandler == null) return;
            if (_health != null && !_health.IsAlive) return; // ragdoll owns the visual skeleton now

            // The visual root must stay at identity under the soldier: the pose below is expressed in that space.
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;

            _driverHandler.GetHumanPose(ref _pose);
            ToVisualSpace(ref _pose);
            _visualHandler.SetHumanPose(ref _pose);

            if (!_offsetsReady && _dL != null && _dR != null && _lHand != null && _rHand != null)
            {
                // muscle space aligns the hands anatomically: capture the constant bone-axis difference once
                _lOffset = Quaternion.Inverse(_dL.rotation) * _lHand.rotation;
                _rOffset = Quaternion.Inverse(_dR.rotation) * _rHand.rotation;
                _offsetsReady = true;
            }

            AlignBody();

            if (handIK && handIKWeight > 0f && _dL != null && _dR != null)
            {
                Vector3 lPos = _dL.position, rPos = _dR.position;
                Quaternion lRot = _dL.rotation, rRot = _dR.rotation;
                Transform itemTf = _soldier != null && _soldier.ActiveItem != null ? _soldier.ActiveItem.transform : null;
                _bridge?.Apply(_soldier != null ? _soldier.ActiveFirearm : null, itemTf, _dL, _dR,
                               ref lPos, ref lRot, ref rPos, ref rRot);
                ApplyProneWeaponPose(itemTf, ref lPos, ref lRot, ref rPos, ref rRot);
                Solve(_rUpper, _rLower, _rHand, rPos, rRot * _rOffset, handIKWeight);
                Solve(_lUpper, _lLower, _lHand, lPos, lRot * _lOffset, handIKWeight);
                HandErrorRight = _rHand != null ? Vector3.Distance(_rHand.position, rPos) : -1f;
                HandErrorLeft = _lHand != null ? Vector3.Distance(_lHand.position, lPos) : -1f;
            }

            if (logAlignment && ++_frames == 90)
                Debug.Log($"[LR2] RetargetProxy {name}: rawHipsDelta={RawHipsDelta:0.000} rawHeadDelta={RawHeadDelta} ({RawHeadDelta.magnitude:0.000}) " +
                          $"shift={AppliedShift} firstPerson={FirstPersonActive} handErr L={HandErrorLeft:0.000} R={HandErrorRight:0.000} " +
                          $"humanScale driver={driver.humanScale:0.0000} visual={visual.humanScale:0.0000}");
        }

        /// <summary>
        /// HumanPose.bodyPosition is NORMALISED (divided by Animator.humanScale) and relative to the handler root's parent.
        /// Re-express the driver's body in the soldier's space, still normalised by the driver's scale: SetHumanPose then
        /// multiplies by the visual's own scale, so a differently sized body keeps its feet on the ground.
        /// (Treating it as plain world units put the body off by ~1.2% of the soldier's distance from the map origin.)
        /// </summary>
        private void ToVisualSpace(ref HumanPose pose)
        {
            float ds = driver.humanScale > 1e-4f ? driver.humanScale : 1f;
            Vector3 bodyWorld = pose.bodyPosition * ds;
            Quaternion rotWorld = pose.bodyRotation;
            var dParent = driver.transform.parent;
            if (dParent != null)
            {
                bodyWorld = dParent.TransformPoint(bodyWorld);
                rotWorld = dParent.rotation * rotWorld;
            }
            var vParent = visual.transform.parent;
            if (vParent != null)
            {
                bodyWorld = vParent.InverseTransformPoint(bodyWorld);
                rotWorld = Quaternion.Inverse(vParent.rotation) * rotWorld;
            }
            pose.bodyPosition = bodyWorld / ds;
            pose.bodyRotation = rotWorld;
        }

        /// <summary>Kinemation's prone pose arches the rifle high and across the camera; rotate/tuck the gun+hands rigidly together.</summary>
        private void ApplyProneWeaponPose(Transform item, ref Vector3 lPos, ref Quaternion lRot, ref Vector3 rPos, ref Quaternion rRot)
        {
            float w = driver != null ? Mathf.Clamp01(driver.GetFloat(ProneWeightHash)) : (_soldier != null && _soldier.IsProning ? 1f : 0f);
            if (w <= 0.001f || _soldier == null || item == null) return;

            Vector3 aimFwd = _soldier.ViewCamera != null && _soldier.ViewCamera.isActiveAndEnabled
                ? _soldier.ViewCamera.transform.forward
                : Quaternion.AngleAxis(_soldier.Pitch, _soldier.transform.right) * _soldier.transform.forward;
            Vector3 aimRight = Vector3.Cross(Vector3.up, aimFwd);
            aimRight = aimRight.sqrMagnitude > 1e-4f ? aimRight.normalized : _soldier.transform.right;
            Vector3 aimUp = Vector3.Cross(aimFwd, aimRight).normalized;

            Quaternion rotDelta = Quaternion.AngleAxis(30f * w, aimRight);
            Vector3 pivot = item.position;
            Vector3 shift = (aimRight * -0.11f + aimUp * -0.14f + aimFwd * 0.11f) * w;
            Vector3 Xform(Vector3 p) => pivot + rotDelta * (p - pivot) + shift;

            item.SetPositionAndRotation(pivot + shift, rotDelta * item.rotation);
            lPos = Xform(lPos);
            rPos = Xform(rPos);
            lRot = rotDelta * lRot;
            rRot = rotDelta * rRot;
        }

        private void AlignBody()
        {
            if (_vHips == null) return;
            RawHipsDelta = _dHips != null ? Vector3.Distance(_dHips.position, _vHips.position) : -1f;
            RawHeadDelta = _dHead != null && _vHead != null ? _dHead.position - _vHead.position : Vector3.zero;

            FirstPersonActive = firstPersonHeadLock && IsFirstPerson && _dHead != null && _vHead != null;
            Vector3 shift;
            if (FirstPersonActive)
            {
                // eyes onto the lens (the driver's camera is its eye socket); the head joints of the two rigs sit at
                // different heights in the skull, so matching joints left the lens at the SWAT's chin
                var cam = _soldier.ViewCamera.transform;
                shift = cam.position - VisualEyePosition;
                if (firstPersonBodyBack > 0f)
                {
                    Vector3 fwd = cam.forward;
                    fwd.y = 0f;
                    if (fwd.sqrMagnitude > 1e-4f) shift -= fwd.normalized * firstPersonBodyBack;
                }
            }
            else if (alignHips && _dHips != null)
            {
                shift = _dHips.position - _vHips.position;
                shift.y = 0f; // keep the visual's own (scaled) hip height: feet stay on the ground
            }
            else shift = Vector3.zero;

            AppliedShift = shift;
            if (shift.sqrMagnitude < 1e-8f) return;

            bool plant = FirstPersonActive && replantFeet && (_soldier == null || !_soldier.IsProning) && _lFoot != null && _rFoot != null;
            Vector3 lf = Vector3.zero, rf = Vector3.zero;
            Quaternion lfr = Quaternion.identity, rfr = Quaternion.identity;
            if (plant)
            {
                lf = _lFoot.position; lfr = _lFoot.rotation;
                rf = _rFoot.position; rfr = _rFoot.rotation;
            }
            _vHips.position += shift;
            if (plant)
            {
                // feet follow the body sideways but keep the height the retargeted pose gave them (planted)
                Vector3 horiz = new Vector3(shift.x, 0f, shift.z);
                Solve(_lThigh, _lShin, _lFoot, lf + horiz, lfr, 1f);
                Solve(_rThigh, _rShin, _rFoot, rf + horiz, rfr, 1f);
            }
        }

        /// <summary>Analytic two-bone IK (same maths as Animation Rigging's TwoBoneIK, keeps the current bend plane).</summary>
        private static void Solve(Transform a, Transform b, Transform c, Vector3 target, Quaternion targetRot, float weight)
        {
            if (a == null || b == null || c == null) return;
            Vector3 aPos = a.position, bPos = b.position, cPos = c.position;
            Vector3 tPos = Vector3.Lerp(cPos, target, weight);
            Vector3 ab = bPos - aPos, bc = cPos - bPos, ac = cPos - aPos, at = tPos - aPos;
            float abLen = ab.magnitude, bcLen = bc.magnitude, acLen = ac.magnitude, atLen = at.magnitude;
            if (abLen < 1e-4f || bcLen < 1e-4f) return;

            float oldAngle = TriangleAngle(acLen, abLen, bcLen);
            float newAngle = TriangleAngle(Mathf.Min(atLen, abLen + bcLen - 1e-4f), abLen, bcLen);
            Vector3 axis = Vector3.Cross(ab, bc);
            if (axis.sqrMagnitude < 1e-8f) axis = Vector3.Cross(ab, Vector3.up);
            if (axis.sqrMagnitude < 1e-8f) axis = Vector3.right;
            axis.Normalize();
            float half = 0.5f * (oldAngle - newAngle);
            float s = Mathf.Sin(half);
            b.rotation = new Quaternion(axis.x * s, axis.y * s, axis.z * s, Mathf.Cos(half)) * b.rotation;

            cPos = c.position;
            ac = cPos - aPos;
            if (ac.sqrMagnitude > 1e-8f && at.sqrMagnitude > 1e-8f)
                a.rotation = Quaternion.FromToRotation(ac, at) * a.rotation;
            c.rotation = Quaternion.Slerp(c.rotation, targetRot, weight);
        }

        private static float TriangleAngle(float opposite, float side1, float side2)
        {
            float cos = Mathf.Clamp((side1 * side1 + side2 * side2 - opposite * opposite) / (2f * side1 * side2), -1f, 1f);
            return Mathf.Acos(cos);
        }
    }
}
