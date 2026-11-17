using UnityEngine;

namespace LR2.Audio
{
    /// <summary>Scene-level tuning for LR2's dynamic audio (occlusion, air absorption, ducking) + ambience loops.</summary>
    public class LR2AudioEnvironment : MonoBehaviour
    {
        [Header("Occlusion (sound behind walls)")]
        [Range(0f, 1f)] public float occlusionVolume = 0.45f;
        [Tooltip("Low-pass cutoff (Hz) when fully occluded.")]
        public float occlusionCutoff = 1100f;
        public float partialOcclusionCutoff = 3200f;

        [Header("Air absorption")]
        [Tooltip("Distance (m) at which high frequencies are mostly absorbed.")]
        public float absorptionDistance = 160f;
        public float absorptionMinCutoff = 3800f;

        [Header("Ducking (HDR audio)")]
        [Tooltip("How fast ducked sounds recover (per second).")]
        public float duckRecovery = 1.6f;

        [Header("Ambience")]
        public AudioClip[] ambienceLoops;
        [Range(0f, 1f)] public float ambienceVolume = 0.35f;

        private AudioSource[] _ambience;

        private void Awake()
        {
            LR2Audio.OcclusionVolume = occlusionVolume;
            LR2Audio.OcclusionCutoff = occlusionCutoff;
            LR2Audio.PartialOcclusionCutoff = partialOcclusionCutoff;
            LR2Audio.AbsorptionDistance = absorptionDistance;
            LR2Audio.AbsorptionMinCutoff = absorptionMinCutoff;
            LR2Audio.DuckRecovery = duckRecovery;
        }

        private void Start()
        {
            var loops = ambienceLoops;
            if ((loops == null || loops.Length == 0) && LR2CommonSounds.Instance != null) loops = LR2CommonSounds.Instance.ambienceLoops;
            if (loops == null) return;
            _ambience = new AudioSource[loops.Length];
            for (int i = 0; i < loops.Length; i++)
            {
                if (loops[i] == null) continue;
                var src = gameObject.AddComponent<AudioSource>();
                src.clip = loops[i];
                src.loop = true;
                src.spatialBlend = 0f;
                src.priority = 230;
                src.reverbZoneMix = 0f;
                src.bypassReverbZones = true;
                src.playOnAwake = false;
                src.time = Random.Range(0f, Mathf.Max(0f, loops[i].length - 1f));
                src.Play();
                _ambience[i] = src;
            }
        }

        private void Update()
        {
            if (_ambience == null) return;
            float v = ambienceVolume * LR2Audio.CategoryVolume(AudioCategory.Ambience) / Mathf.Max(1, _ambience.Length) * 1.4f;
            foreach (var a in _ambience)
                if (a != null) a.volume = v;
        }
    }
}
