using LR2.Characters;
using UnityEngine;

namespace LR2.World
{
    /// <summary>
    /// Scripted input for the main-menu soldiers (B110): stand or strafe around a home spot, raise the rifle at a target,
    /// and in a fight shoot short bursts just past it (the soldiers are invulnerable; misses spark off the set).
    /// </summary>
    public class MenuActor : MonoBehaviour, ISoldierInputSource
    {
        public SoldierController sc;
        public Vector3 home;
        public float idleYaw;
        public float strafeAmp;
        public float strafeFreq = 0.45f;
        public float phase;
        public Transform aimAt;
        public Vector3 aimOffset;
        public bool raise, fight;
        private float _nextBurst, _burstEnd;

        public void GetInput(SoldierController s, ref SoldierInput input)
        {
            input.absoluteLook = true;
            Vector3 eye = s.EyeTransform != null ? s.EyeTransform.position : s.transform.position + Vector3.up * 1.6f;
            if (aimAt != null && (raise || fight))
            {
                Vector3 d = aimAt.position + Vector3.up * 1.3f + aimOffset - eye;
                input.aimYaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                input.aimPitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
                input.aim = true;
            }
            else
            {
                input.aimYaw = idleYaw;
                input.aimPitch = 3f;
                input.aim = false;
            }

            float t = Time.time * strafeFreq + phase;
            Vector3 right = Quaternion.Euler(0f, input.aimYaw, 0f) * Vector3.right;
            Vector3 want = home + right * (Mathf.Sin(t) * strafeAmp);
            Vector3 to = want - s.transform.position;
            to.y = 0f;
            Vector3 local = Quaternion.Euler(0f, -s.Yaw, 0f) * to;
            input.move = to.sqrMagnitude < 0.004f ? Vector2.zero : Vector2.ClampMagnitude(new Vector2(local.x, local.z) * 1.6f, fight ? 0.75f : 0.55f);
            input.sprint = false;

            input.fire = false;
            if (!fight) return;
            if (Time.time >= _nextBurst)
            {
                _burstEnd = Time.time + Random.Range(0.14f, 0.38f);
                _nextBurst = _burstEnd + Random.Range(0.8f, 2.0f);
            }
            input.fire = Time.time < _burstEnd;
            var gun = s.ActiveFirearm;
            if (gun != null && gun.AmmoInMagazine <= 0) input.reload = true;
        }
    }
}
