using UnityEngine;

namespace LR2.Audio
{
    /// <summary>
    /// Global sound slots (hit markers, kill confirms, UI, whizzes, low health). Loaded from
    /// Resources/LR2/LR2CommonSounds so any system can use it without scene wiring.
    /// </summary>
    [CreateAssetMenu(menuName = "LR2/Audio/Common Sounds", fileName = "LR2CommonSounds")]
    public class LR2CommonSounds : ScriptableObject
    {
        private static LR2CommonSounds _instance;
        private static bool _searched;

        public static LR2CommonSounds Instance
        {
            get
            {
                if (_instance == null && !_searched)
                {
                    _searched = true;
                    _instance = Resources.Load<LR2CommonSounds>("LR2/LR2CommonSounds");
                }
                return _instance;
            }
        }

        [Header("Hit feedback (2D, local player only)")]
        public SoundSet hitmarker = new SoundSet(AudioCategory.UI, 0.9f, 10f, 0f);
        public SoundSet hitmarkerHeadshot = new SoundSet(AudioCategory.UI, 1f, 10f, 0f);
        public SoundSet killConfirm = new SoundSet(AudioCategory.UI, 1f, 10f, 0f);
        public SoundSet hitmarkerArmor = new SoundSet(AudioCategory.UI, 0.8f, 10f, 0f);

        [Header("Incoming fire (local player)")]
        public SoundSet bulletWhizz = new SoundSet(AudioCategory.Weapons, 1f, 16f, 1f);
        public SoundSet bulletCrack = new SoundSet(AudioCategory.Weapons, 1f, 18f, 1f);
        public SoundSet playerHurt = new SoundSet(AudioCategory.Voice, 0.9f, 10f, 0f);
        public SoundSet lowHealthLoop = new SoundSet(AudioCategory.Sfx, 0.6f, 10f, 0f);
        public SoundSet playerDeath = new SoundSet(AudioCategory.Voice, 1f, 10f, 0f);

        [Header("Characters (3D)")]
        public SoundSet bodyFall = new SoundSet(AudioCategory.Sfx, 0.9f, 25f, 1f);
        public SoundSet painGrunt = new SoundSet(AudioCategory.Voice, 0.8f, 25f, 1f);
        public SoundSet deathGrunt = new SoundSet(AudioCategory.Voice, 0.9f, 30f, 1f);
        public SoundSet jump = new SoundSet(AudioCategory.Footsteps, 0.6f, 20f, 1f);
        public SoundSet land = new SoundSet(AudioCategory.Footsteps, 0.8f, 25f, 1f);
        public SoundSet slide = new SoundSet(AudioCategory.Footsteps, 0.8f, 25f, 1f);
        public SoundSet mantle = new SoundSet(AudioCategory.Footsteps, 0.8f, 20f, 1f);
        public SoundSet adsIn = new SoundSet(AudioCategory.Sfx, 0.55f, 8f, 1f);
        public SoundSet adsOut = new SoundSet(AudioCategory.Sfx, 0.45f, 8f, 1f);
        public SoundSet weaponSwap = new SoundSet(AudioCategory.Sfx, 0.7f, 12f, 1f);
        public SoundSet ammoPickup = new SoundSet(AudioCategory.Sfx, 0.9f, 12f, 1f);

        [Header("UI")]
        public SoundSet uiHover = new SoundSet(AudioCategory.UI, 0.10f, 10f, 0f);
        public SoundSet uiClick = new SoundSet(AudioCategory.UI, 0.20f, 10f, 0f);
        public SoundSet uiBack = new SoundSet(AudioCategory.UI, 0.18f, 10f, 0f);
        public SoundSet medal = new SoundSet(AudioCategory.UI, 0.9f, 10f, 0f);
        public SoundSet matchStart = new SoundSet(AudioCategory.UI, 1f, 10f, 0f);
        public SoundSet victory = new SoundSet(AudioCategory.Music, 1f, 10f, 0f);
        public SoundSet defeat = new SoundSet(AudioCategory.Music, 1f, 10f, 0f);
        public SoundSet countdownTick = new SoundSet(AudioCategory.UI, 0.8f, 10f, 0f);

        [Header("World")]
        public SoundSet orbExplode = new SoundSet(AudioCategory.Sfx, 1f, 50f, 1f);
        public SoundSet orbAlert = new SoundSet(AudioCategory.Sfx, 0.8f, 30f, 1f);

        [Header("Ambience loops")]
        public AudioClip[] ambienceLoops;
        [Range(0f, 1f)] public float ambienceVolume = 0.35f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
            _searched = false;
        }

        private void OnEnable()
        {
            // Guard serialized asset defaults against harsh/loud menu click bursts and ensure wide bullet flyby reach
            if (uiHover != null) uiHover.volume = Mathf.Min(uiHover.volume, 0.14f);
            if (uiClick != null) uiClick.volume = Mathf.Min(uiClick.volume, 0.24f);
            if (uiBack != null) uiBack.volume = Mathf.Min(uiBack.volume, 0.22f);
            if (bulletWhizz != null) { bulletWhizz.minDistance = Mathf.Max(bulletWhizz.minDistance, 2.5f); bulletWhizz.maxDistance = Mathf.Max(bulletWhizz.maxDistance, 16f); }
            if (bulletCrack != null) { bulletCrack.minDistance = Mathf.Max(bulletCrack.minDistance, 2.5f); bulletCrack.maxDistance = Mathf.Max(bulletCrack.maxDistance, 18f); }
        }
    }
}
