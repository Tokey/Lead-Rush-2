using UnityEngine;

namespace LR2.Core
{
    public static class LR2Math
    {
        /// <summary>Frame-rate independent exponential smoothing factor.</summary>
        public static float Damp(float sharpness, float dt) => 1f - Mathf.Exp(-sharpness * dt);

        public static float WrapAngle(float degrees)
        {
            degrees %= 360f;
            if (degrees > 180f) degrees -= 360f;
            if (degrees < -180f) degrees += 360f;
            return degrees;
        }

        /// <summary>Yaw (deg, world) and pitch (deg, positive = looking DOWN, Kinemation convention) for a direction.</summary>
        public static Vector2 DirectionToYawPitch(Vector3 dir)
        {
            if (dir.sqrMagnitude < 1e-8f) return Vector2.zero;
            dir.Normalize();
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg;
            return new Vector2(yaw, pitch);
        }

        public static Vector3 YawPitchToDirection(float yaw, float pitch)
        {
            return Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;
        }

        /// <summary>Random direction inside a cone of the given half-angle (degrees), gaussian-ish distribution.</summary>
        public static Vector3 RandomInCone(Vector3 forward, float halfAngleDeg)
        {
            if (halfAngleDeg <= 0.0001f) return forward;
            // Average of two uniforms gives a softer centre-weighted spread (more "CoD", less shotgun).
            float r = (Random.value + Random.value) * 0.5f * halfAngleDeg;
            float a = Random.value * Mathf.PI * 2f;
            var rot = Quaternion.AngleAxis(Mathf.Cos(a) * r, Vector3.up) * Quaternion.AngleAxis(Mathf.Sin(a) * r, Vector3.right);
            var basis = Quaternion.LookRotation(forward);
            return basis * (rot * Vector3.forward);
        }

        public static Vector3 ClosestPointOnSegment(Vector3 a, Vector3 b, Vector3 p)
        {
            var ab = b - a;
            float t = Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f);
            return a + ab * Mathf.Clamp01(t);
        }
    }
}
