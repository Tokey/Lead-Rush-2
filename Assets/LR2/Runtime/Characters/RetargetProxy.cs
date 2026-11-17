using LR2.Combat;
using UnityEngine;

namespace LR2.Characters
{
    /// <summary>
    /// Visual body proxy. The soldier is animated by Kinemation's humanoid FPS rig (the "driver": correct grip, ADS,
    /// reloads, equips for every demo weapon), and this copies that pose onto the SWAT model every frame through
    /// Unity's humanoid muscle space. Two-bone IK then locks the SWAT hands exactly onto the driver's hands, so the
    /// fingers sit on the pistol grip / handguard / magazine in first and third person. Stops on death so the SWAT
    /// ragdoll takes over.
    /// </summary>
    [DefaultExecutionOrder(60)] // after FPSAnimator.LateUpdate (Kinemation post-process + camera), before bodycam (1100)
    public class RetargetProxy : MonoBehaviour
    {
        [SerializeField] private Animator driver;
        [SerializeField] private Animator visual;
        [Tooltip("Lock the visual hands onto the driver hands (grip accuracy).")]
        [SerializeField] private bool handIK = true;
        [SerializeField, Range(0f, 1f)] private float handIKWeight = 1f;

        private HumanPoseHandler _driverHandler, _visualHandler;
        private HumanPose _pose;
        private Health _health;
        private Transform _dL, _dR;
        private Transform _lUpper, _lLower, _lHand, _rUpper, _rLower, _rHand;
        private Quaternion _lOffset = Quaternion.identity, _rOffset = Quaternion.identity;
        private bool _offsetsReady;
        private int _frames;

        public Animator Driver => driver;
        public Animator Visual => visual;

        public void Configure(Animator driverAnimator, Animator visualAnimator)
        {
            driver = driverAnimator;
            visual = visualAnimator;
        }

        private void Start()
        {
            _health = GetComponentInParent<Health>();
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
            _lUpper = visual.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            _lLower = visual.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            _lHand = visual.GetBoneTransform(HumanBodyBones.LeftHand);
            _rUpper = visual.GetBoneTransform(HumanBodyBones.RightUpperArm);
            _rLower = visual.GetBoneTransform(HumanBodyBones.RightLowerArm);
            _rHand = visual.GetBoneTransform(HumanBodyBones.RightHand);
        }

        private void OnDestroy()
        {
            _driverHandler?.Dispose();
            _visualHandler?.Dispose();
        }

        private void LateUpdate()
        {
            if (_driverHandler == null) return;
            if (_health != null && !_health.IsAlive) return; // ragdoll owns the visual skeleton now

            _driverHandler.GetHumanPose(ref _pose);
            // GetHumanPose expresses the body relative to the DRIVER root's parent (world: the soldier root has no parent),
            // SetHumanPose expects it relative to the VISUAL root's parent (the soldier). Convert world -> soldier space.
            var space = visual.transform.parent;
            if (space != null && driver.transform.parent != space)
            {
                Quaternion inv = Quaternion.Inverse(space.rotation);
                _pose.bodyPosition = inv * (_pose.bodyPosition - space.position);
                _pose.bodyRotation = inv * _pose.bodyRotation;
            }
            _visualHandler.SetHumanPose(ref _pose);

            if (!handIK || handIKWeight <= 0f || _dL == null || _dR == null) return;
            if (!_offsetsReady)
            {
                // humanoid retarget aligns the hands in muscle space: capture the constant bone-axis difference once
                _lOffset = Quaternion.Inverse(_dL.rotation) * _lHand.rotation;
                _rOffset = Quaternion.Inverse(_dR.rotation) * _rHand.rotation;
                _offsetsReady = true;
            }
            Solve(_rUpper, _rLower, _rHand, _dR.position, _dR.rotation * _rOffset, handIKWeight);
            Solve(_lUpper, _lLower, _lHand, _dL.position, _dL.rotation * _lOffset, handIKWeight);
            if (++_frames == 90)
            {
                var dh = driver.GetBoneTransform(HumanBodyBones.Hips);
                var vh = visual.GetBoneTransform(HumanBodyBones.Hips);
                Debug.Log($"[LR2] RetargetProxy {name}: hipsDelta={(dh != null && vh != null ? Vector3.Distance(dh.position, vh.position) : -1f):0.000} " +
                          $"handR err={Vector3.Distance(_rHand.position, _dR.position):0.000} handL err={Vector3.Distance(_lHand.position, _dL.position):0.000} " +
                          $"driverHandR={_dR.position} visualHips={(vh != null ? vh.position : Vector3.zero)} root={transform.position}");
            }
        }

        /// <summary>Analytic two-bone IK (same maths as Animation Rigging's TwoBoneIK, keeps the current elbow plane).</summary>
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
