using LR2.Characters;
using UnityEngine;

namespace LR2.World
{
    /// <summary>
    /// Scripted input for the main-menu soldiers (B110). Movement is a steady walk between two marks with a pause at each
    /// (user 11-19: strafing "looks a bit janky"): no proportional creeping near the turn, input eased in and out, so the
    /// locomotion blend never flickers between idle and walk. Patrols walk facing their way with the rifle low and look
    /// at their target while they stand; fighters strafe facing the target and shoot short bursts only once they have
    /// stopped, like a counter-strafe. The soldiers are invulnerable and aim just wide.
    /// </summary>
    public class MenuActor : MonoBehaviour, ISoldierInputSource
    {
        public SoldierController sc;
        public Vector3 home;
        public float idleYaw;
        public float strafeAmp;
        public float strafeFreq = 0.45f; // kept for callers; pace and pauses now set the rhythm
        public float phase;
        public Transform aimAt;
        public Vector3 aimOffset;
        public bool raise, fight;

        private int _side = 1;
        private float _pauseUntil, _burstAt = -1f, _burstEnd;
        private Vector2 _move;
        private float _travelYaw;
        private bool _seeded;

        private float Rand(float a, float b) => Random.Range(a, b);

        public void GetInput(SoldierController s, ref SoldierInput input)
        {
            if (!_seeded) { _seeded = true; _side = phase > 2f ? -1 : 1; _pauseUntil = Time.time + phase * 0.35f; _travelYaw = idleYaw; }
            input.absoluteLook = true;
            input.sprint = false;
            input.fire = false;
            Vector3 pos = s.transform.position;
            Vector3 eye = s.EyeTransform != null ? s.EyeTransform.position : pos + Vector3.up * 1.6f;

            // the strafe line: across the line to the target (fighters), or along the facing (patrols, idle)
            Vector3 toTarget = aimAt != null ? aimAt.position - home : Quaternion.Euler(0f, idleYaw, 0f) * Vector3.forward;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.01f) toTarget = Vector3.forward;
            Vector3 axis = Vector3.Cross(Vector3.up, toTarget.normalized);

            Vector2 want = Vector2.zero;
            bool moving = false;
            if (strafeAmp > 0.05f && Time.time >= _pauseUntil)
            {
                Vector3 goal = home + axis * (strafeAmp * _side);
                Vector3 to = goal - pos;
                to.y = 0f;
                if (to.magnitude < 0.15f)
                {
                    _side = -_side;
                    _pauseUntil = Time.time + (fight ? Rand(0.55f, 1.25f) : Rand(1.0f, 2.4f));
                    if (fight) { _burstAt = Time.time + Rand(0.12f, 0.3f); _burstEnd = _burstAt + Rand(0.16f, 0.4f); }
                }
                else
                {
                    moving = true;
                    _travelYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                    Vector3 local = Quaternion.Euler(0f, -s.Yaw, 0f) * to.normalized;
                    float pace = fight ? 0.85f : 1f;
                    want = new Vector2(local.x, local.z) * pace;
                }
            }
            // ease in and out (about a quarter second), never a step
            _move = Vector2.MoveTowards(_move, want, Time.deltaTime * 4.5f);
            input.move = _move;

            bool patrolWalk = !fight && moving;
            if (aimAt != null && (raise || fight) && !patrolWalk)
            {
                Vector3 d = aimAt.position + Vector3.up * 1.3f + aimOffset - eye;
                input.aimYaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                input.aimPitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
                input.aim = fight || !moving; // patrols raise the rifle only while they stand and watch
            }
            else
            {
                input.aimYaw = patrolWalk ? _travelYaw : idleYaw;
                input.aimPitch = 3f;
                input.aim = false;
            }

            if (!fight) return;
            bool stopped = _move.sqrMagnitude < 0.02f;
            input.fire = stopped && _burstAt > 0f && Time.time >= _burstAt && Time.time < _burstEnd;
            var gun = s.ActiveFirearm;
            if (gun != null && gun.AmmoInMagazine <= 0) input.reload = true;
        }
    }
}
