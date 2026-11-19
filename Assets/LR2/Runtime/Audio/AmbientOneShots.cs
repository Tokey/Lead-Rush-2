using UnityEngine;

namespace LR2.Audio
{
    /// <summary>
    /// Random spatial one-shots around the listener (crows, wind gusts, distant thunder), indoor water drips,
    /// and a gritty war-torn battlefield horizon layer (far-off muffled artillery barrages and rusted industrial
    /// wind groans carved below 950 Hz so footsteps remain crisp; B36).
    /// </summary>
    public class AmbientOneShots : MonoBehaviour
    {
        public AudioClip[] outdoorClips;
        public Vector2 outdoorInterval = new Vector2(5f, 14f);
        [Range(0f, 1f)] public float outdoorVolume = 0.55f;
        public AudioClip[] dripClips;
        public Vector3[] dripPoints;
        public Vector2 dripInterval = new Vector2(0.8f, 3f);
        [Range(0f, 1f)] public float dripVolume = 0.5f;

        [Header("B36 War-Torn Horizon Ambience")]
        public bool warzoneHorizonAmbience = true;
        public Vector2 warzoneInterval = new Vector2(9f, 19f);
        [Range(0f, 1f)] public float warzoneVolume = 0.36f;

        private float _nextOutdoor, _nextDrip, _nextWarzone;
        private static AudioClip _artilleryClip;
        private static AudioClip _metalWindClip;

        public static bool HasWarzoneHorizonClips => _artilleryClip != null && _metalWindClip != null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _artilleryClip = null;
            _metalWindClip = null;
        }

        private void Start()
        {
            _nextOutdoor = Time.time + Random.Range(2f, 6f);
            _nextDrip = Time.time + 1f;
            _nextWarzone = Time.time + Random.Range(3.5f, 8.5f);
            if (warzoneHorizonAmbience) EnsureWarzoneClips();
        }

        private void Update()
        {
            Vector3 l = LR2Audio.ListenerPosition;
            if (outdoorClips != null && outdoorClips.Length > 0 && Time.time >= _nextOutdoor)
            {
                _nextOutdoor = Time.time + Random.Range(outdoorInterval.x, outdoorInterval.y);
                Vector2 d = Random.insideUnitCircle.normalized * Random.Range(18f, 45f);
                var clip = outdoorClips[Random.Range(0, outdoorClips.Length)];
                LR2Audio.PlayClip(clip, l + new Vector3(d.x, Random.Range(4f, 18f), d.y), outdoorVolume, Random.Range(0.92f, 1.06f), 8f, 140f, 0.85f, AudioCategory.Ambience, 205);
            }
            if (dripClips != null && dripClips.Length > 0 && dripPoints != null && dripPoints.Length > 0 && Time.time >= _nextDrip)
            {
                _nextDrip = Time.time + Random.Range(dripInterval.x, dripInterval.y);
                var p = dripPoints[Random.Range(0, dripPoints.Length)] + new Vector3(Random.Range(-2f, 2f), 0f, Random.Range(-2f, 2f));
                if ((p - l).sqrMagnitude < 18f * 18f)
                    LR2Audio.PlayClip(dripClips[Random.Range(0, dripClips.Length)], p, dripVolume, Random.Range(0.9f, 1.1f), 1f, 14f, 1f, AudioCategory.Ambience, 225);
            }
            if (warzoneHorizonAmbience && Time.time >= _nextWarzone)
            {
                EnsureWarzoneClips();
                _nextWarzone = Time.time + Random.Range(warzoneInterval.x, warzoneInterval.y);
                Vector2 dir = Random.insideUnitCircle;
                if (dir.sqrMagnitude < 1e-4f) dir = Vector2.right;
                dir.Normalize();
                float dist = Random.Range(42f, 90f);
                bool artillery = Random.value < 0.65f;
                var clip = artillery ? _artilleryClip : _metalWindClip;
                if (clip != null)
                {
                    Vector3 pos = l + new Vector3(dir.x * dist, artillery ? Random.Range(6f, 18f) : Random.Range(3f, 10f), dir.y * dist);
                    LR2Audio.PlayClip(clip, pos, warzoneVolume, Random.Range(0.90f, 1.08f), 18f, 180f, 0.82f, AudioCategory.Ambience, 215, ignoreOcclusion: artillery);
                }
            }
        }

        public static void EnsureWarzoneClips()
        {
            if (_artilleryClip == null) _artilleryClip = BuildDistantArtilleryClip();
            if (_metalWindClip == null) _metalWindClip = BuildWarzoneWindGroanClip();
        }

        private static AudioClip BuildDistantArtilleryClip()
        {
            const int sr = 22050;
            const float dur = 2.6f;
            int n = Mathf.RoundToInt(sr * dur);
            float[] data = new float[n];
            var rng = new System.Random(1945);
            float[] booms = { 0.08f, 0.62f, 1.24f };
            float[] amps = { 0.95f, 0.62f, 0.48f };
            float lp1 = 0f, lp2 = 0f;
            float alpha1 = Mathf.Clamp01(2f * Mathf.PI * 240f / sr);
            float alpha2 = Mathf.Clamp01(2f * Mathf.PI * 190f / sr);

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                float raw = 0f;
                for (int b = 0; b < booms.Length; b++)
                {
                    float dt = t - booms[b];
                    if (dt < 0f || dt > 1.35f) continue;
                    float env = Mathf.Clamp01(dt / 0.035f) * Mathf.Exp(-dt * 2.4f);
                    float f = Mathf.Lerp(68f, 29f, Mathf.Clamp01(dt * 1.4f));
                    raw += (Mathf.Sin(2f * Mathf.PI * f * dt) * 0.58f + white * 0.52f) * env * amps[b];
                }
                lp1 += alpha1 * (raw - lp1);
                lp2 += alpha2 * (lp1 - lp2);
                float fadeOut = Mathf.Clamp01((dur - t) / 0.25f);
                data[i] = Mathf.Clamp(lp2 * 0.85f * fadeOut, -0.78f, 0.78f);
            }
            var clip = AudioClip.Create("LR2_WarzoneArtillery", n, 1, sr, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip BuildWarzoneWindGroanClip()
        {
            const int sr = 22050;
            const float dur = 3.2f;
            int n = Mathf.RoundToInt(sr * dur);
            float[] data = new float[n];
            var rng = new System.Random(1984);
            float lp = 0f, hp = 0f, prev = 0f;
            float lpAlpha = Mathf.Clamp01(2f * Mathf.PI * 760f / sr);
            const float hpAlpha = 0.96f;

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / dur));
                env = env * env;
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                float metallic = Mathf.Sin(2f * Mathf.PI * (186f + 14f * Mathf.Sin(t * 1.1f)) * t) * 0.16f
                               + Mathf.Sin(2f * Mathf.PI * (281f - 9f * Mathf.Sin(t * 0.8f)) * t) * 0.11f;
                float raw = (white * 0.42f + metallic) * env;
                hp = hpAlpha * (hp + raw - prev);
                prev = raw;
                lp += lpAlpha * (hp - lp);
                data[i] = Mathf.Clamp(lp * 0.52f, -0.55f, 0.55f);
            }
            var clip = AudioClip.Create("LR2_WarzoneMetalWind", n, 1, sr, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
