using LR2.Audio;
using UnityEngine;

namespace LR2.Characters
{
    /// <summary>
    /// Soldier + weapon foley sounds (Modern Warfare SFX). Field names match the folders under Assets/LR2/Audio/Foley,
    /// filled by the content builder. Lives in Resources/LR2/FoleyLibrary.
    /// </summary>
    [CreateAssetMenu(menuName = "LR2/Audio/Foley Library", fileName = "FoleyLibrary")]
    public class FoleyLibrary : ScriptableObject
    {
        private static FoleyLibrary _instance;
        public static FoleyLibrary Instance => _instance != null ? _instance : _instance = Resources.Load<FoleyLibrary>("LR2/FoleyLibrary");

        [Header("Gear on the body (every step)")]
        public SoundSet gearWalk = new SoundSet(AudioCategory.Sfx, 0.45f, 22f, 1f);
        public SoundSet gearSprint = new SoundSet(AudioCategory.Sfx, 0.6f, 28f, 1f);
        public SoundSet clothWalk = new SoundSet(AudioCategory.Sfx, 0.4f, 16f, 1f);
        public SoundSet weaponRattle = new SoundSet(AudioCategory.Sfx, 0.4f, 20f, 1f);
        public SoundSet accent = new SoundSet(AudioCategory.Sfx, 0.4f, 18f, 1f);

        [Header("Stance / traversal")]
        public SoundSet stanceDown = new SoundSet(AudioCategory.Sfx, 0.6f, 16f, 1f);
        public SoundSet stanceUp = new SoundSet(AudioCategory.Sfx, 0.6f, 16f, 1f);
        public SoundSet mantle = new SoundSet(AudioCategory.Sfx, 0.8f, 22f, 1f);
        public SoundSet weaponLand = new SoundSet(AudioCategory.Sfx, 0.7f, 24f, 1f);
        public SoundSet landConcrete = new SoundSet(AudioCategory.Sfx, 0.8f, 30f, 1f);
        public SoundSet landDirt = new SoundSet(AudioCategory.Sfx, 0.8f, 26f, 1f);

        [Header("Breathing / voice")]
        public SoundSet breathSprintIn = new SoundSet(AudioCategory.Sfx, 0.5f, 10f, 1f);
        public SoundSet breathSprintOut = new SoundSet(AudioCategory.Sfx, 0.55f, 10f, 1f);
        public SoundSet breathRecover = new SoundSet(AudioCategory.Sfx, 0.5f, 9f, 1f);
        public SoundSet breathJump = new SoundSet(AudioCategory.Sfx, 0.7f, 14f, 1f);
        public SoundSet breathLandHard = new SoundSet(AudioCategory.Sfx, 0.8f, 16f, 1f);
        public SoundSet breathLandSoft = new SoundSet(AudioCategory.Sfx, 0.5f, 12f, 1f);
        public SoundSet breathMantle = new SoundSet(AudioCategory.Sfx, 0.75f, 14f, 1f);
        public SoundSet pain = new SoundSet(AudioCategory.Sfx, 0.9f, 22f, 1f);

        [Header("Weapon mechanics")]
        public SoundSet triggerReset = new SoundSet(AudioCategory.Weapons, 0.45f, 6f, 0.3f);
        public SoundSet selector = new SoundSet(AudioCategory.Weapons, 0.7f, 8f, 1f);
        public SoundSet magDrop = new SoundSet(AudioCategory.Sfx, 0.8f, 20f, 1f);
    }
}
