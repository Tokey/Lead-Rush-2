using System;
using LR2.Audio;
using LR2.Combat;
using UnityEngine;

namespace LR2.FX
{
    [Serializable]
    public class SurfaceFx
    {
        public SurfaceType surface = SurfaceType.Concrete;
        [Tooltip("Particle prefab spawned at bullet impacts.")]
        public GameObject impactPrefab;
        [Tooltip("HDRP/Decal material for bullet holes.")]
        public Material decalMaterial;
        public Vector2 decalSize = new Vector2(0.09f, 0.14f);
        public SoundSet impactSound = new SoundSet(AudioCategory.Sfx, 0.8f, 35f, 1f);
        public SoundSet footstepSound = new SoundSet(AudioCategory.Footsteps, 0.6f, 25f, 1f);
    }

    /// <summary>Per-surface impact, decal and footstep data. Lives at Resources/LR2/SurfaceLibrary.</summary>
    [CreateAssetMenu(menuName = "LR2/FX/Surface Library", fileName = "SurfaceLibrary")]
    public class SurfaceLibrary : ScriptableObject
    {
        private static SurfaceLibrary _instance;
        private static bool _searched;

        public static SurfaceLibrary Instance
        {
            get
            {
                if (_instance == null && !_searched)
                {
                    _searched = true;
                    _instance = Resources.Load<SurfaceLibrary>("LR2/SurfaceLibrary");
                }
                return _instance;
            }
        }

        public SurfaceFx[] surfaces = Array.Empty<SurfaceFx>();
        public SurfaceFx fallback = new SurfaceFx();

        [Header("Flesh")]
        public GameObject bloodPrefab;
        public GameObject bloodHeadshotPrefab;
        public Material bloodDecal;
        public SoundSet fleshImpact = new SoundSet(AudioCategory.Sfx, 0.9f, 25f, 1f);
        public SoundSet headshotImpact = new SoundSet(AudioCategory.Sfx, 1f, 30f, 1f);

        [Header("Shared")]
        public GameObject tracerPrefab;
        [Range(16, 256)] public int maxDecals = 96;
        public float decalLifetime = 25f;
        [Range(0f, 1f)] public float localFootstepVolume = 0.55f;

        public SurfaceFx Get(SurfaceType type)
        {
            if (surfaces != null)
                foreach (var s in surfaces)
                    if (s != null && s.surface == type) return s;
            return fallback;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
            _searched = false;
        }
    }
}
