using UnityEngine;

namespace LR2.UI
{
    /// <summary>Top-down capture of the map + its world XZ bounds. Placed by the map builder; read by the HUD minimap.</summary>
    public class MinimapData : MonoBehaviour
    {
        public static MinimapData Current { get; private set; }

        public Texture2D mapTexture;
        public Vector2 worldMin = new Vector2(-30f, -26f);
        public Vector2 worldMax = new Vector2(30f, 26f);

        public Vector2 WorldSize => worldMax - worldMin;

        public Vector2 WorldToUV(Vector3 world) =>
            new Vector2(Mathf.InverseLerp(worldMin.x, worldMax.x, world.x), Mathf.InverseLerp(worldMin.y, worldMax.y, world.z));

        private void OnEnable() => Current = this;
        private void OnDisable() { if (Current == this) Current = null; }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Vector3 c = new Vector3((worldMin.x + worldMax.x) * 0.5f, transform.position.y, (worldMin.y + worldMax.y) * 0.5f);
            Gizmos.DrawWireCube(c, new Vector3(WorldSize.x, 0.1f, WorldSize.y));
        }
    }
}
