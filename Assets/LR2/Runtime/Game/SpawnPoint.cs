using System.Collections.Generic;
using LR2.Combat;
using LR2.Core;
using UnityEngine;
using UnityEngine.AI;

namespace LR2.Game
{
    public class SpawnPoint : MonoBehaviour
    {
        private static readonly List<SpawnPoint> Registry = new List<SpawnPoint>();
        public static IReadOnlyList<SpawnPoint> All => Registry;

        public TeamId team = TeamId.None;
        public bool allowFreeForAll = true;
        [Tooltip("Can seeker drones spawn here?")]
        public bool allowDrones = true;

        [System.NonSerialized] public float lastUsedTime = -999f;

        private void OnEnable() { if (!Registry.Contains(this)) Registry.Add(this); }
        private void OnDisable() => Registry.Remove(this);

        private void OnDrawGizmos()
        {
            Gizmos.color = team == TeamId.Alpha ? TeamUtil.FriendlyColor : team == TeamId.Bravo ? TeamUtil.EnemyColor : TeamUtil.NeutralColor;
            Vector3 p = transform.position;
            Gizmos.DrawWireCube(p + Vector3.up * 0.9f, new Vector3(0.6f, 1.8f, 0.6f));
            Gizmos.DrawLine(p + Vector3.up * 1.5f, p + Vector3.up * 1.5f + transform.forward * 0.8f);
        }

        // ------------------------------------------------------------------ D66: never spawn inside geometry or another soldier
        private const float SoldierRadius = 0.38f, SoldierHeight = 1.8f; // LR2_Soldier CharacterController
        private static readonly Collider[] OverlapBuffer = new Collider[16];
        private static readonly HashSet<string> Reported = new HashSet<string>();

        /// <summary>
        /// D66 (user 11-18: "sometimes player get inside objects"): a spawn point can end up inside a prop (map edits, props
        /// placed later, cover that moved) or another soldier can be standing on it. Tests the soldier capsule at the point
        /// and, when it is blocked, returns the nearest clear NavMesh point on the same floor, on rings up to 4 m away.
        /// </summary>
        public static Vector3 ClearPosition(Vector3 desired, Combatant ignore = null, string label = null)
        {
            if (IsClear(desired, ignore)) return desired;
            for (float r = 0.7f; r <= 4.05f; r += 0.65f)
            {
                for (int i = 0; i < 12; i++)
                {
                    Vector3 c = desired + Quaternion.Euler(0f, i * 30f + r * 17f, 0f) * Vector3.forward * r;
                    if (NavMesh.SamplePosition(c, out var hit, 1.2f, NavMesh.AllAreas)) c = hit.position;
                    if (Mathf.Abs(c.y - desired.y) > 1.2f) continue; // not another floor
                    if (!IsClear(c, ignore)) continue;
                    if (label != null && Reported.Add(label))
                        Debug.Log($"[LR2] Spawn '{label}' at {desired} was blocked; used a clear point {Vector3.Distance(desired, c):0.0} m away.");
                    return c;
                }
            }
            if (label != null && Reported.Add(label)) Debug.LogWarning($"[LR2] Spawn '{label}' at {desired} is blocked and no clear point was found within 4 m.");
            return desired;
        }

        public static bool IsClear(Vector3 feet, Combatant ignore = null)
        {
            Vector3 p0 = feet + Vector3.up * (SoldierRadius + 0.06f);
            Vector3 p1 = feet + Vector3.up * (SoldierHeight - SoldierRadius);
            int mask = LR2Layers.EnvironmentMask | (1 << LR2Layers.PlayerClip) | (1 << LR2Layers.Character);
            int n = Physics.OverlapCapsuleNonAlloc(p0, p1, SoldierRadius * 0.92f, OverlapBuffer, mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var col = OverlapBuffer[i];
                if (col == null) continue;
                if (ignore != null && col.transform.IsChildOf(ignore.transform)) continue;
                return false;
            }
            return true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Registry.Clear();
            Reported.Clear();
        }
    }
}
