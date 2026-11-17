using System.Collections.Generic;
using UnityEngine;

namespace LR2.Combat
{
    public enum SurfaceType
    {
        Default,
        Concrete,
        Metal,
        Wood,
        Dirt,
        Glass,
        Water,
        Flesh,
        Plastic,
        Fabric
    }

    /// <summary>Explicit surface override; put it on a collider or any parent.</summary>

    /// <summary>
    /// Resolves the acoustic/visual surface of a collider: explicit <see cref="SurfaceIdentifier"/> first, then
    /// hitboxes (flesh), then keywords in physic-material / render-material / object names. Results are cached.
    /// </summary>
    public static class SurfaceDetector
    {
        private static readonly Dictionary<Collider, SurfaceType> Cache = new Dictionary<Collider, SurfaceType>(512);

        private static readonly (string key, SurfaceType type)[] Keywords =
        {
            ("glass", SurfaceType.Glass), ("window", SurfaceType.Glass),
            ("water", SurfaceType.Water), ("ocean", SurfaceType.Water), ("sea", SurfaceType.Water),
            ("metal", SurfaceType.Metal), ("steel", SurfaceType.Metal), ("iron", SurfaceType.Metal),
            ("container", SurfaceType.Metal), ("pipe", SurfaceType.Metal), ("rail", SurfaceType.Metal),
            ("barrel", SurfaceType.Metal), ("tank", SurfaceType.Metal), ("crane", SurfaceType.Metal),
            ("vent", SurfaceType.Metal), ("grate", SurfaceType.Metal), ("catwalk", SurfaceType.Metal),
            ("fence", SurfaceType.Metal), ("car", SurfaceType.Metal), ("truck", SurfaceType.Metal),
            ("corrugated", SurfaceType.Metal), ("shutter", SurfaceType.Metal), ("ladder", SurfaceType.Metal),
            ("wood", SurfaceType.Wood), ("plank", SurfaceType.Wood), ("crate", SurfaceType.Wood),
            ("pallet", SurfaceType.Wood), ("door", SurfaceType.Wood), ("box", SurfaceType.Wood),
            ("dirt", SurfaceType.Dirt), ("mud", SurfaceType.Dirt), ("sand", SurfaceType.Dirt),
            ("grass", SurfaceType.Dirt), ("soil", SurfaceType.Dirt), ("terrain", SurfaceType.Dirt), ("gravel", SurfaceType.Dirt),
            ("plastic", SurfaceType.Plastic), ("rubber", SurfaceType.Plastic), ("tire", SurfaceType.Plastic),
            ("cloth", SurfaceType.Fabric), ("fabric", SurfaceType.Fabric), ("tarp", SurfaceType.Fabric), ("sandbag", SurfaceType.Fabric),
            ("concrete", SurfaceType.Concrete), ("cement", SurfaceType.Concrete), ("brick", SurfaceType.Concrete),
            ("stone", SurfaceType.Concrete), ("asphalt", SurfaceType.Concrete), ("rock", SurfaceType.Concrete),
        };

        public static SurfaceType Detect(Collider collider)
        {
            if (collider == null) return SurfaceType.Default;
            if (Cache.TryGetValue(collider, out var cached)) return cached;

            SurfaceType result = Resolve(collider);
            Cache[collider] = result;
            return result;
        }

        private static SurfaceType Resolve(Collider collider)
        {
            var id = collider.GetComponentInParent<SurfaceIdentifier>();
            if (id != null) return id.surface;
            if (collider.GetComponent<Hitbox>() != null) return SurfaceType.Flesh;

            if (collider.sharedMaterial != null && TryKeywords(collider.sharedMaterial.name, out var pm)) return pm;

            var renderer = collider.GetComponent<Renderer>();
            if (renderer == null) renderer = collider.GetComponentInParent<Renderer>();
            if (renderer != null && renderer.sharedMaterial != null && TryKeywords(renderer.sharedMaterial.name, out var rm)) return rm;

            Transform t = collider.transform;
            for (int i = 0; i < 4 && t != null; i++, t = t.parent)
                if (TryKeywords(t.name, out var nm)) return nm;

            return SurfaceType.Concrete;
        }

        private static bool TryKeywords(string name, out SurfaceType type)
        {
            if (!string.IsNullOrEmpty(name))
            {
                string lower = name.ToLowerInvariant();
                foreach (var (key, t) in Keywords)
                {
                    if (lower.Contains(key))
                    {
                        type = t;
                        return true;
                    }
                }
            }
            type = SurfaceType.Default;
            return false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Cache.Clear();
    }
}
