using LR2.Combat;
using UnityEngine;

namespace LR2.FX
{
    /// <summary>Central surface-to-decal policy used by impact and QA code.</summary>
    public static class SurfaceDecalPolicy
    {
        public static bool ShouldProject(SurfaceType surface)
        {
            return surface != SurfaceType.Water && surface != SurfaceType.Fabric;
        }

        public static string Variant(SurfaceType surface)
        {
            switch (surface)
            {
                case SurfaceType.Metal: return "Metal";
                case SurfaceType.Concrete: return "Concrete";
                case SurfaceType.Wood: return "Wood";
                case SurfaceType.Dirt: return "Dirt";
                case SurfaceType.Glass: return "Glass";
                case SurfaceType.Plastic: return "Plastic";
                case SurfaceType.Fabric: return "Fabric";
                case SurfaceType.Water: return "Water";
                default: return "Default";
            }
        }

        public static float Depth(SurfaceType surface) => surface == SurfaceType.Glass ? 0.04f : 0.12f;
    }
}
