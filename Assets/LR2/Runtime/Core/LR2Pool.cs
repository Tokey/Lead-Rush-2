using System.Collections.Generic;
using UnityEngine;

namespace LR2.Core
{
    /// <summary>Marks an instance created by <see cref="LR2Pool"/>; handles timed auto-despawn.</summary>

    /// <summary>
    /// Lightweight prefab pool for VFX, decals, casings, tracers and audio voices. Instances live under a
    /// scene-local root so they are cleaned up with the scene.
    /// </summary>
    public static class LR2Pool
    {
        private static readonly Dictionary<GameObject, Stack<GameObject>> Pools = new Dictionary<GameObject, Stack<GameObject>>();
        private static Transform _root;

        private static Transform Root
        {
            get
            {
                if (_root == null)
                {
                    _root = new GameObject("[LR2 Pool]").transform;
                    Pools.Clear();
                }
                return _root;
            }
        }

        public static GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, float lifetime = -1f, Transform parent = null)
        {
            if (prefab == null) return null;
            var root = Root;
            GameObject go = null;
            if (Pools.TryGetValue(prefab, out var stack))
            {
                while (stack.Count > 0 && go == null) go = stack.Pop();
            }

            if (go == null)
            {
                go = Object.Instantiate(prefab, position, rotation, parent != null ? parent : root);
                var po = go.GetComponent<PooledObject>();
                if (po == null) po = go.AddComponent<PooledObject>();
                po.prefab = prefab;
            }
            else
            {
                go.transform.SetParent(parent != null ? parent : root, false);
                go.transform.SetPositionAndRotation(position, rotation);
                go.SetActive(true);
            }

            go.GetComponent<PooledObject>().OnSpawned(lifetime);
            return go;
        }

        public static T Spawn<T>(T prefab, Vector3 position, Quaternion rotation, float lifetime = -1f, Transform parent = null) where T : Component
        {
            if (prefab == null) return null;
            var go = Spawn(prefab.gameObject, position, rotation, lifetime, parent);
            return go != null ? go.GetComponent<T>() : null;
        }

        public static void Despawn(GameObject instance)
        {
            if (instance == null) return;
            var po = instance.GetComponent<PooledObject>();
            if (po == null || po.prefab == null)
            {
                Object.Destroy(instance);
                return;
            }

            po.despawnAt = -1f;
            instance.SetActive(false);
            instance.transform.SetParent(Root, false);
            if (!Pools.TryGetValue(po.prefab, out var stack))
            {
                stack = new Stack<GameObject>();
                Pools[po.prefab] = stack;
            }
            stack.Push(instance);
        }

        public static void Prewarm(GameObject prefab, int count)
        {
            if (prefab == null) return;
            var temp = new List<GameObject>(count);
            for (int i = 0; i < count; i++) temp.Add(Spawn(prefab, Vector3.zero, Quaternion.identity));
            foreach (var go in temp) Despawn(go);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Pools.Clear();
            _root = null;
        }
    }
}
