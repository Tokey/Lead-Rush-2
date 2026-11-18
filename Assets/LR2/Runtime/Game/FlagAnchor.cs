using System.Collections.Generic;
using UnityEngine;

namespace LR2.Game
{
    /// <summary>
    /// Strategic Capture The Flag plaza/objective point baked on maps by LR2MapBuilder.
    /// Registered statically so <see cref="FlagZoneController"/> can rotate objectives across maps seamlessly.
    /// </summary>
    public class FlagAnchor : MonoBehaviour
    {
        public string sectorName = "CENTRAL HUB";
        public float captureRadius = 11.5f;
        public float coreRadius = 2.6f;

        private static readonly List<FlagAnchor> Registry = new List<FlagAnchor>();
        public static IReadOnlyList<FlagAnchor> All => Registry;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Registry.Clear();

        private void OnEnable()
        {
            if (!Registry.Contains(this)) Registry.Add(this);
        }

        private void OnDisable() => Registry.Remove(this);
    }
}
