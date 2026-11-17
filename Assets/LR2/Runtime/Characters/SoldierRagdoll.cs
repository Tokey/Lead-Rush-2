using System;
using System.Collections.Generic;
using LR2.Combat;
using LR2.Core;
using UnityEngine;

namespace LR2.Characters
{
    [Serializable]
    public class RagdollBone
    {
        public Rigidbody body;
        public Collider collider;
    }

    /// <summary>
    /// The bone hitboxes double as ragdoll bodies: kinematic + Hitbox layer while alive (raycast only), dynamic +
    /// Ragdoll layer when dead. The killing bullet's impulse is applied at the hit point.
    /// </summary>
    public class SoldierRagdoll : MonoBehaviour
    {
        [SerializeField] private List<RagdollBone> bones = new List<RagdollBone>();
        [SerializeField] private float impulseScale = 1.35f;
        [SerializeField] private float maxImpulse = 135f;
        [SerializeField, Range(0f, 1f)] private float inheritVelocity = 0.85f;

        public bool IsActive { get; private set; }
        public IReadOnlyList<RagdollBone> Bones => bones;

        public void SetBones(List<RagdollBone> newBones) => bones = newBones ?? new List<RagdollBone>();

        private void Awake() => SetKinematic(true);

        public void Activate(DamageInfo info)
        {
            var animator = GetComponent<Animator>();
            if (animator != null) animator.enabled = false;
            var combatant = GetComponent<Combatant>();
            Vector3 velocity = combatant != null ? combatant.Velocity * inheritVelocity : Vector3.zero;

            Physics.SyncTransforms();
            SetKinematic(false);
            Rigidbody closest = null;
            float best = float.MaxValue;
            foreach (var b in bones)
            {
                if (b == null || b.body == null) continue;
                b.body.linearVelocity = velocity;
                b.body.angularVelocity = Vector3.zero;
                float d = (b.body.worldCenterOfMass - info.point).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    closest = b.body;
                }
            }

            if (closest != null && info.direction.sqrMagnitude > 0.001f)
            {
                float impulse = Mathf.Min(maxImpulse, Mathf.Max(info.force, 25f) * impulseScale);
                closest.AddForceAtPosition(info.direction.normalized * impulse, info.point, ForceMode.Impulse);
            }
            IsActive = true;
        }

        public void Deactivate()
        {
            SetKinematic(true);
            IsActive = false;
        }

        private void SetKinematic(bool kinematic)
        {
            foreach (var b in bones)
            {
                if (b == null || b.body == null) continue;
                if (!kinematic)
                {
                    b.body.isKinematic = false;
                    b.body.interpolation = RigidbodyInterpolation.Interpolate;
                    b.body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                }
                else
                {
                    b.body.interpolation = RigidbodyInterpolation.None;
                    b.body.collisionDetectionMode = CollisionDetectionMode.Discrete;
                    if (!b.body.isKinematic)
                    {
                        b.body.linearVelocity = Vector3.zero;
                        b.body.angularVelocity = Vector3.zero;
                    }
                    b.body.isKinematic = true;
                }
                if (b.collider != null) b.collider.gameObject.layer = kinematic ? LR2Layers.Hitbox : LR2Layers.Ragdoll;
            }
        }
    }
}
