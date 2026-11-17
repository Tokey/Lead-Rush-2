using UnityEngine;

namespace LR2.Core
{
    [DisallowMultipleComponent]
    public sealed class PooledObject : MonoBehaviour
    {
        internal GameObject prefab;
        internal float despawnAt = -1f;
        private ParticleSystem[] _particles;
        private TrailRenderer[] _trails;

        internal void OnSpawned(float lifetime)
        {
            despawnAt = lifetime > 0f ? Time.time + lifetime : -1f;
            _particles ??= GetComponentsInChildren<ParticleSystem>(true);
            _trails ??= GetComponentsInChildren<TrailRenderer>(true);
            foreach (var t in _trails) t.Clear();
            foreach (var p in _particles)
            {
                p.Clear(true);
                p.Play(true);
            }
        }

        private void Update()
        {
            if (despawnAt > 0f && Time.time >= despawnAt) LR2Pool.Despawn(gameObject);
        }
    }
}
