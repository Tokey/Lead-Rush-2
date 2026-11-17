using System.Collections.Generic;
using UnityEngine;

namespace LR2.AI
{
    public class TacticalPoint : MonoBehaviour
    {
        private static readonly List<TacticalPoint> Registry = new List<TacticalPoint>();
        public static IReadOnlyList<TacticalPoint> All => Registry;

        public TacticalPointType type = TacticalPointType.Patrol;
        [Range(0f, 1f)] public float weight = 0.5f;

        [System.NonSerialized] public float lastVisitTime = -999f;

        private void OnEnable() { if (!Registry.Contains(this)) Registry.Add(this); }
        private void OnDisable() => Registry.Remove(this);

        private void OnDrawGizmos()
        {
            Gizmos.color = type == TacticalPointType.Overlook ? new Color(1f, 0.6f, 0.1f) :
                type == TacticalPointType.Choke ? new Color(1f, 0.2f, 0.2f) :
                type == TacticalPointType.Sniper ? new Color(0.8f, 0.2f, 1f) : new Color(0.3f, 0.9f, 1f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.2f, 0.35f + weight * 0.3f);
            Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 1.6f);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Registry.Clear();
    }
}
