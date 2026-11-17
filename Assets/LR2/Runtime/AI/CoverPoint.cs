using System.Collections.Generic;
using UnityEngine;

namespace LR2.AI
{
    public class CoverPoint : MonoBehaviour
    {
        private static readonly List<CoverPoint> Registry = new List<CoverPoint>();
        public static IReadOnlyList<CoverPoint> All => Registry;

        [Tooltip("True if the cover is only waist-high (bot crouches behind it and stands to shoot).")]
        public bool crouchCover = true;

        [System.NonSerialized] public Object occupant;

        private void OnEnable() { if (!Registry.Contains(this)) Registry.Add(this); }
        private void OnDisable() => Registry.Remove(this);

        private void OnDrawGizmos()
        {
            Gizmos.color = crouchCover ? new Color(0.2f, 1f, 0.4f) : new Color(0.1f, 0.6f, 0.2f);
            Vector3 p = transform.position + Vector3.up * 0.1f;
            Gizmos.DrawCube(p, new Vector3(0.3f, 0.05f, 0.3f));
            Gizmos.DrawLine(p, p + transform.forward * 0.6f);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Registry.Clear();
    }
}
