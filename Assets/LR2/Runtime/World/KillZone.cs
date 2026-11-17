using LR2.Combat;
using UnityEngine;

namespace LR2.World
{
    /// <summary>Kills anything that falls below <see cref="killHeight"/> (out of the map).</summary>
    public class KillZone : MonoBehaviour
    {
        [SerializeField] private float killHeight = -15f;
        [SerializeField] private float checkInterval = 0.5f;
        private float _next;

        private void Update()
        {
            if (Time.time < _next) return;
            _next = Time.time + checkInterval;
            var all = Combatant.All;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                var c = all[i];
                if (c != null && c.IsAlive && c.Position.y < killHeight && c.Health != null) c.Health.Kill(DamageType.Environment);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0f, 0f, 0.25f);
            Gizmos.DrawCube(new Vector3(transform.position.x, killHeight, transform.position.z), new Vector3(200f, 0.05f, 200f));
        }
    }
}
