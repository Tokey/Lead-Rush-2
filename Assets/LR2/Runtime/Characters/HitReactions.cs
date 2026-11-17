using LR2.Combat;
using UnityEngine;

namespace LR2.Characters
{
    /// <summary>
    /// Procedural flinch for third-person soldiers: a damped spring rotates spine/head away from the hit, applied
    /// after the Kinemation pose so it layers on top of any animation (the animator overwrites it next frame).
    /// </summary>
    [DefaultExecutionOrder(500)]
    public class HitReactions : MonoBehaviour
    {
        [SerializeField] private Transform spine;
        [SerializeField] private Transform head;
        [SerializeField, Range(0f, 45f)] private float maxAngle = 28f;
        [SerializeField] private float degreesPer25Damage = 16f;
        [SerializeField] private float stiffness = 175f;
        [SerializeField] private float damping = 11f;
        [SerializeField] private bool disableForLocalPlayer = true;

        private Health _health;
        private Combatant _combatant;
        private Vector3 _angle;      // axis * degrees
        private Vector3 _velocity;
        private float _headBias;

        public void Configure(Transform spineBone, Transform headBone)
        {
            spine = spineBone;
            head = headBone;
        }

        private void Awake()
        {
            _health = GetComponent<Health>();
            _combatant = GetComponent<Combatant>();
            var animator = GetComponent<Animator>();
            if (animator != null && animator.isHuman)
            {
                if (spine == null) spine = animator.GetBoneTransform(HumanBodyBones.Chest);
                if (spine == null) spine = animator.GetBoneTransform(HumanBodyBones.Spine);
                if (head == null) head = animator.GetBoneTransform(HumanBodyBones.Head);
            }
        }

        private void OnEnable()
        {
            if (_health != null) _health.Damaged += OnDamaged;
        }

        private void OnDisable()
        {
            if (_health != null) _health.Damaged -= OnDamaged;
        }

        private void OnDamaged(DamageInfo info)
        {
            if (disableForLocalPlayer && _combatant != null && _combatant.IsLocalPlayer) return;
            Vector3 dir = info.direction.sqrMagnitude > 0.001f ? info.direction.normalized : -transform.forward;
            Vector3 axis = Vector3.Cross(Vector3.up, dir);
            if (axis.sqrMagnitude < 0.001f) axis = transform.right;
            float deg = Mathf.Min(maxAngle, degreesPer25Damage * info.amount / 25f);
            _velocity += axis.normalized * deg * stiffness * 0.12f;
            _headBias = info.zone == HitZone.Head ? 1.6f : 0.6f;
        }

        private void LateUpdate()
        {
            if (_health != null && !_health.IsAlive) return;
            float dt = Time.deltaTime;
            Vector3 accel = -_angle * stiffness - _velocity * damping;
            _velocity += accel * dt;
            _angle += _velocity * dt;
            if (_angle.magnitude > maxAngle) _angle = _angle.normalized * maxAngle;
            float mag = _angle.magnitude;
            if (mag < 0.01f) return;
            Quaternion q = Quaternion.AngleAxis(mag, _angle / mag);
            if (spine != null) spine.rotation = q * spine.rotation;
            if (head != null) head.rotation = Quaternion.AngleAxis(mag * _headBias, _angle / mag) * head.rotation;
        }
    }
}
