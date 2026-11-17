using System.Collections.Generic;
using LR2.Combat;
using UnityEngine;

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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Registry.Clear();
    }
}
