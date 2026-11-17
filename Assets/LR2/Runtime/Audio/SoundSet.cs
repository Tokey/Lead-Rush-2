using System;
using UnityEngine;

namespace LR2.Audio
{
    public enum AudioCategory
    {
        Sfx,
        Weapons,
        Footsteps,
        UI,
        Music,
        Ambience,
        Voice
    }

    /// <summary>A pool of variations for one sound slot (e.g. "AK-12 shot, player"), with randomisation settings.</summary>
    [Serializable]
    public class SoundSet
    {
        public AudioClip[] clips = Array.Empty<AudioClip>();
        [Range(0f, 2f)] public float volume = 1f;
        public Vector2 pitch = new Vector2(0.97f, 1.03f);
        [Range(0f, 1f)] public float spatialBlend = 1f;
        [Min(0.01f)] public float minDistance = 1.5f;
        [Min(0.1f)] public float maxDistance = 60f;
        public AudioCategory category = AudioCategory.Sfx;
        [Range(0, 256)] public int priority = 128;

        [NonSerialized] private int _lastIndex = -1;

        public bool IsValid => clips != null && clips.Length > 0;

        public SoundSet() { }

        public SoundSet(AudioCategory category, float volume = 1f, float maxDistance = 60f, float spatialBlend = 1f)
        {
            this.category = category;
            this.volume = volume;
            this.maxDistance = maxDistance;
            this.spatialBlend = spatialBlend;
        }

        /// <summary>Picks a random clip, never the same one twice in a row.</summary>
        public AudioClip Pick()
        {
            if (!IsValid) return null;
            if (clips.Length == 1) return clips[0];
            int i = UnityEngine.Random.Range(0, clips.Length);
            if (i == _lastIndex) i = (i + 1 + UnityEngine.Random.Range(0, clips.Length - 1)) % clips.Length;
            _lastIndex = i;
            return clips[i];
        }

        public float PickPitch() => UnityEngine.Random.Range(Mathf.Min(pitch.x, pitch.y), Mathf.Max(pitch.x, pitch.y));
    }
}
