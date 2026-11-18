using System.Collections.Generic;
using LR2.Core;
using UnityEngine;

namespace LR2.Audio
{
    /// <summary>
    /// Pooled one-shot audio with "dynamic depth": occlusion (sounds behind walls are muffled + quieter), air
    /// absorption (high frequencies fade with distance), speed-of-sound delay, indoor detection and HDR-style ducking
    /// (your own gunfire momentarily pushes ambience/footsteps down). Tuned per scene by LR2AudioEnvironment.
    /// </summary>
    public static class LR2Audio
    {
        private const int MaxVoices = 72;
        private const float SpeedOfSound = 343f;

        // Environment tuning (overridden by LR2AudioEnvironment in the scene)
        public static float OcclusionVolume = 0.45f;
        public static float OcclusionCutoff = 1100f;
        public static float PartialOcclusionCutoff = 3200f;
        public static float AbsorptionDistance = 160f;
        public static float AbsorptionMinCutoff = 3800f;
        public static float DuckRecovery = 1.6f;

        private static readonly List<AudioSource> Voices = new List<AudioSource>(MaxVoices);
        private static readonly Dictionary<AudioSource, AudioLowPassFilter> Filters = new Dictionary<AudioSource, AudioLowPassFilter>(MaxVoices);
        private static readonly Dictionary<AudioSource, float> StartTimes = new Dictionary<AudioSource, float>(MaxVoices);
        private static readonly List<float> ActiveTailEndTimes = new List<float>(8);
        private static WeaponSoundTailRouter _tailRouter;
        private static Transform _root;
        private static AnimationCurve _rolloff;
        private static AudioListener _listener;
        private static float _nextListenerSearch;

        /// <summary>0..1 duck amount currently applied to ambience/footsteps/sfx.</summary>
        public static float DuckFactor { get; private set; }
        public static int ActiveTailCount => ActiveTailEndTimes.Count;

        public static float CategoryVolume(AudioCategory category)
        {
            switch (category)
            {
                case AudioCategory.UI: return LR2Settings.UiVolume;
                case AudioCategory.Music: return LR2Settings.MusicVolume;
                case AudioCategory.Weapons: return LR2Settings.SfxVolume;
                case AudioCategory.Voice: return LR2Settings.SfxVolume * (1f - DuckFactor * 0.2f);
                case AudioCategory.Ambience: return LR2Settings.SfxVolume * 0.72f * (1f - DuckFactor * 0.85f);
                case AudioCategory.Footsteps: return LR2Settings.SfxVolume * 0.16f * (1f - DuckFactor * 0.25f);
                case AudioCategory.Sfx: return LR2Settings.SfxVolume * (1f - DuckFactor * 0.45f);
                default: return LR2Settings.SfxVolume;
            }
        }

        /// <summary>Momentarily ducks non-weapon sounds (call on loud local events such as the player's gunshot).</summary>
        public static void Duck(float amount)
        {
            DuckFactor = Mathf.Clamp01(Mathf.Max(DuckFactor, amount));
            EnsureRoot();
        }

        internal static void TickDuck(float dt)
        {
            DuckFactor = Mathf.MoveTowards(DuckFactor, 0f, DuckRecovery * dt);
            if (_tailRouter != null && ActiveTailEndTimes.Count > 0)
            {
                float now = Time.unscaledTime;
                for (int i = ActiveTailEndTimes.Count - 1; i >= 0; i--)
                {
                    if (now >= ActiveTailEndTimes[i])
                    {
                        ActiveTailEndTimes.RemoveAt(i);
                        _tailRouter.NotifyTailEnded();
                    }
                }
            }
        }

        public static AudioSource PlayAt(SoundSet set, Vector3 position, float volumeScale = 1f, float delay = 0f,
            bool ignoreOcclusion = false, float pitchMultiplier = 1f)
        {
            if (set == null || !set.IsValid) return null;
            return PlayClip(set.Pick(), position, set.volume * volumeScale, set.PickPitch() * pitchMultiplier, set.minDistance, set.maxDistance,
                set.spatialBlend, set.category, set.priority, delay, null, ignoreOcclusion);
        }

        public static AudioSource Play2D(SoundSet set, float volumeScale = 1f, float delay = 0f)
        {
            if (set == null || !set.IsValid) return null;
            return PlayClip(set.Pick(), Vector3.zero, set.volume * volumeScale, set.PickPitch(), 1f, 500f, 0f, set.category,
                set.priority, delay, null);
        }

        public static AudioSource PlayAttached(SoundSet set, Transform parent, float volumeScale = 1f)
        {
            if (set == null || !set.IsValid || parent == null) return null;
            return PlayClip(set.Pick(), parent.position, set.volume * volumeScale, set.PickPitch(), set.minDistance, set.maxDistance,
                set.spatialBlend, set.category, set.priority, 0f, parent);
        }

        /// <summary>Plays a 3D sound delayed by its travel time to the listener (distant gunfire arrives late).</summary>
        public static AudioSource PlayAtWithTravelTime(SoundSet set, Vector3 position, Vector3 listener, float volumeScale = 1f)
        {
            float delay = Vector3.Distance(position, listener) / SpeedOfSound;
            return PlayAt(set, position, volumeScale, delay);
        }

        /// <summary>Plays an environmental weapon tail gated and balanced through <see cref="WeaponSoundTailRouter"/>.</summary>
        public static AudioSource PlayWeaponTail(AudioClip clip, Vector3 position, float volume, float minDistance, float maxDistance, bool indoors)
        {
            return PlayClipCore(clip, position, volume, UnityEngine.Random.Range(0.96f, 1.04f), minDistance, maxDistance, 1f,
                AudioCategory.Weapons, 150, 0f, null, false, forceTailRoute: true, tailIndoorsOverride: indoors);
        }

        public static AudioSource PlayClip(AudioClip clip, Vector3 position, float volume, float pitch, float minDistance,
            float maxDistance, float spatialBlend, AudioCategory category, int priority = 128, float delay = 0f, Transform parent = null,
            bool ignoreOcclusion = false)
        {
            return PlayClipCore(clip, position, volume, pitch, minDistance, maxDistance, spatialBlend, category, priority,
                delay, parent, ignoreOcclusion, forceTailRoute: false, tailIndoorsOverride: null);
        }

        private static AudioSource PlayClipCore(AudioClip clip, Vector3 position, float volume, float pitch, float minDistance,
            float maxDistance, float spatialBlend, AudioCategory category, int priority, float delay, Transform parent,
            bool ignoreOcclusion, bool forceTailRoute, bool? tailIndoorsOverride)
        {
            if (clip == null) return null;
            EnsureRoot();
            bool isTail = forceTailRoute || (category == AudioCategory.Weapons &&
                clip.name.IndexOf("tail", System.StringComparison.OrdinalIgnoreCase) >= 0);
            float vol = volume;
            bool tailReserved = false;
            float tailEndTime = 0f;
            if (isTail && _tailRouter != null)
            {
                if (!_tailRouter.CanPlay(Time.unscaledTime + delay)) return null;
                tailReserved = true;
                bool indoors = tailIndoorsOverride ??
                    (clip.name.IndexOf("interior", System.StringComparison.OrdinalIgnoreCase) >= 0 || IsIndoors(parent != null ? parent.position : position));
                vol *= _tailRouter.Gain(indoors);
                tailEndTime = Time.unscaledTime + delay + Mathf.Clamp(clip.length, 0.15f, 2.5f);
            }

            var src = GetVoice(priority);
            if (src == null)
            {
                // B65 / D39: Release reserved tail slot immediately when voice allocation fails under pool saturation.
                if (tailReserved && _tailRouter != null) _tailRouter.CancelReservation();
                return null;
            }
            if (tailReserved) ActiveTailEndTimes.Add(tailEndTime);

            var t = src.transform;
            t.SetParent(parent != null ? parent : _root, false);
            if (parent != null) t.localPosition = Vector3.zero;
            else t.position = position;

            float cutoff = 22000f;
            if (!ignoreOcclusion && spatialBlend > 0.5f && category != AudioCategory.UI && category != AudioCategory.Music)
                ApplyDepth(parent != null ? parent.position : position, ref vol, ref cutoff);
            if (category == AudioCategory.Footsteps)
            {
                // Soften harsh upper-mid / treble concrete heel transients while preserving step timing
                cutoff = Mathf.Min(cutoff, 4800f);
                priority = Mathf.Max(priority, 175);
            }
            else if (category == AudioCategory.Voice)
            {
                // Restrained, mask-muffled vocal presence for suppressed agony grunts
                cutoff = Mathf.Min(cutoff, 3200f);
            }

            src.clip = clip;
            src.volume = Mathf.Clamp01(vol * CategoryVolume(category));
            src.pitch = pitch;
            src.spatialBlend = spatialBlend;
            src.minDistance = minDistance;
            src.maxDistance = Mathf.Max(minDistance + 0.1f, maxDistance);
            src.priority = priority;
            src.rolloffMode = AudioRolloffMode.Custom;
            src.SetCustomCurve(AudioSourceCurveType.CustomRolloff, Rolloff);
            src.dopplerLevel = 0f;
            src.spread = spatialBlend > 0.5f ? 25f : 0f;
            src.reverbZoneMix = spatialBlend > 0.5f ? 1f : 0f;
            src.ignoreListenerPause = category == AudioCategory.UI;
            src.loop = false;

            if (Filters.TryGetValue(src, out var lpf) && lpf != null)
            {
                lpf.enabled = cutoff < 21500f;
                lpf.cutoffFrequency = cutoff;
            }

            if (delay > 0.001f) src.PlayDelayed(delay);
            else src.Play();
            StartTimes[src] = Time.unscaledTime + delay;
            return src;
        }

        /// <summary>Occlusion + air absorption for a world-space emitter.</summary>
        private static void ApplyDepth(Vector3 emitter, ref float volume, ref float cutoff)
        {
            Vector3 listener = ListenerPosition;
            float dist = Vector3.Distance(listener, emitter);
            cutoff = Mathf.Lerp(22000f, AbsorptionMinCutoff, Mathf.Clamp01(dist / Mathf.Max(1f, AbsorptionDistance)));
            if (dist < 0.75f) return;

            int mask = LR2Layers.EnvironmentMask;
            bool direct = Physics.Linecast(listener, emitter, mask, QueryTriggerInteraction.Ignore);
            if (!direct) return;
            bool high = Physics.Linecast(listener + Vector3.up * 0.6f, emitter + Vector3.up * 1.2f, mask, QueryTriggerInteraction.Ignore);
            if (high)
            {
                volume *= OcclusionVolume;
                cutoff = Mathf.Min(cutoff, OcclusionCutoff);
            }
            else
            {
                volume *= Mathf.Lerp(OcclusionVolume, 1f, 0.55f);
                cutoff = Mathf.Min(cutoff, PartialOcclusionCutoff);
            }
        }

        /// <summary>True if there is a roof above the position (used to pick interior gun tails / reverb).</summary>
        public static bool IsIndoors(Vector3 position)
        {
            Vector3 p = position + Vector3.up * 0.5f;
            int mask = LR2Layers.EnvironmentMask;
            return Physics.Raycast(p, Vector3.up, 25f, mask, QueryTriggerInteraction.Ignore)
                   && Physics.Raycast(p, (Vector3.up + Vector3.forward * 0.6f).normalized, 25f, mask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>World position of the active AudioListener (the local player's camera in game).</summary>
        public static Vector3 ListenerPosition
        {
            get
            {
                if ((_listener == null || !_listener.isActiveAndEnabled) && Time.unscaledTime >= _nextListenerSearch)
                {
                    _nextListenerSearch = Time.unscaledTime + 0.5f;
                    _listener = Object.FindAnyObjectByType<AudioListener>();
                }
                return _listener != null ? _listener.transform.position : Vector3.zero;
            }
        }

        private static AnimationCurve Rolloff
        {
            get
            {
                if (_rolloff == null)
                {
                    _rolloff = new AnimationCurve(
                        new Keyframe(0f, 1f, 0f, -3.5f),
                        new Keyframe(0.08f, 0.62f, -2.6f, -2.6f),
                        new Keyframe(0.25f, 0.3f, -1.0f, -1.0f),
                        new Keyframe(0.6f, 0.08f, -0.3f, -0.3f),
                        new Keyframe(1f, 0f, -0.1f, 0f));
                }
                return _rolloff;
            }
        }

        private static void EnsureRoot()
        {
            if (_root != null) return;
            var go = new GameObject("[LR2 Audio]");
            go.AddComponent<LR2AudioDriver>();
            _tailRouter = go.AddComponent<WeaponSoundTailRouter>();
            _root = go.transform;
            Voices.Clear();
            Filters.Clear();
            StartTimes.Clear();
            ActiveTailEndTimes.Clear();
        }

        private static AudioSource GetVoice(int priority)
        {
            EnsureRoot();
            for (int i = Voices.Count - 1; i >= 0; i--)
            {
                var v = Voices[i];
                if (v == null)
                {
                    Voices.RemoveAt(i);
                    continue;
                }
                if (!v.isPlaying && Time.unscaledTime >= StartTime(v)) return v;
            }

            if (Voices.Count < MaxVoices)
            {
                var go = new GameObject("Voice");
                go.transform.SetParent(_root, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                var lpf = go.AddComponent<AudioLowPassFilter>();
                lpf.cutoffFrequency = 22000f;
                lpf.lowpassResonanceQ = 1f;
                lpf.enabled = false;
                Voices.Add(src);
                Filters[src] = lpf;
                return src;
            }

            // Steal the lowest-priority (highest priority integer), oldest active voice eligible for preemption
            AudioSource victim = null;
            int victimPriority = priority - 1;
            float victimTime = float.MaxValue;
            foreach (var v in Voices)
            {
                if (v == null || v.priority < priority) continue;
                float st = StartTime(v);
                if (v.priority > victimPriority || (v.priority == victimPriority && st < victimTime))
                {
                    victimPriority = v.priority;
                    victimTime = st;
                    victim = v;
                }
            }
            if (victim != null) victim.Stop();
            return victim;
        }

        /// <summary>Verifies tail reservation rollback under simulated voice-allocation failure (B65 / D39).</summary>
        public static bool VerifyTailReservationRollback(out int activeAfterReserve, out int activeAfterRollback)
        {
            var tempGo = new GameObject("[TailRollbackCheck]");
            var router = tempGo.AddComponent<WeaponSoundTailRouter>();
            int before = router.ActiveTailCount;
            bool reserved = router.CanPlay(Time.unscaledTime + 10f);
            activeAfterReserve = router.ActiveTailCount;
            if (reserved) router.CancelReservation();
            activeAfterRollback = router.ActiveTailCount;
            Object.DestroyImmediate(tempGo);
            return reserved && activeAfterReserve == before + 1 && activeAfterRollback == before;
        }

        private static float StartTime(AudioSource v) => StartTimes.TryGetValue(v, out float t) ? t : 0f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Voices.Clear();
            Filters.Clear();
            StartTimes.Clear();
            ActiveTailEndTimes.Clear();
            _tailRouter = null;
            _root = null;
            _listener = null;
            DuckFactor = 0f;
        }
    }
}
