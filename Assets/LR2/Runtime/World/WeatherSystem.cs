using System;
using LR2.Audio;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Random = UnityEngine.Random;

namespace LR2.World
{
    public enum WeatherKind { DuskClear, Overcast, Drizzle, Thunderstorm, Fog }

    [Serializable]
    public class WeatherPreset
    {
        public string name = "Weather";
        public WeatherKind kind;
        [Tooltip("Sun intensity multiplier (1 = the map's configured sun).")]
        [Range(0f, 1.5f)] public float sun = 1f;
        [Tooltip("Fog density multiplier (2 = twice as thick).")]
        [Range(0.3f, 6f)] public float fog = 1f;
        [Range(0f, 1f)] public float rain;
        [Range(0f, 1f)] public float wind = 0.3f;
        [Tooltip("Lightning strikes per minute.")]
        [Range(0f, 12f)] public float lightning;
        [Tooltip("Exposure compensation offset (negative = darker).")]
        [Range(-2f, 1f)] public float exposure;
        public Color fogTint = new Color(0.6f, 0.68f, 0.66f);
    }

    /// <summary>
    /// Fully dynamic weather: cycles through presets (dusk, overcast, drizzle, thunderstorm, fog) with smooth
    /// transitions. Drives the sun, volumetric fog density/tint, exposure, rain particles (+ procedural rain hiss),
    /// wind (WindZone + plant sway), lightning flashes with distance-delayed thunder. Everything is editable here.
    /// </summary>
    public class WeatherSystem : MonoBehaviour
    {
        public static float Wind { get; private set; } = 0.3f;
        public static WeatherSystem Instance { get; private set; }

        [Header("Cycle")]
        public bool dynamicCycle = true;
        public bool randomStart = false;
        public int startPreset = 0;
        [Tooltip("How long a weather state holds before changing (seconds, random in range).")]
        public Vector2 holdTime = new Vector2(70f, 150f);
        [Tooltip("Seconds to blend from one weather into the next.")]
        public float transitionTime = 30f;
        public WeatherPreset[] presets =
        {
            new WeatherPreset { name = "Dusk", kind = WeatherKind.DuskClear, sun = 1f, fog = 1f, wind = 0.25f },
            new WeatherPreset { name = "Overcast", kind = WeatherKind.Overcast, sun = 0.35f, fog = 1.6f, wind = 0.45f, exposure = -0.2f, fogTint = new Color(0.55f, 0.6f, 0.62f) },
            new WeatherPreset { name = "Drizzle", kind = WeatherKind.Drizzle, sun = 0.2f, fog = 2.2f, rain = 0.45f, wind = 0.55f, exposure = -0.35f, fogTint = new Color(0.5f, 0.56f, 0.6f) },
            new WeatherPreset { name = "Thunderstorm", kind = WeatherKind.Thunderstorm, sun = 0.08f, fog = 2.8f, rain = 1f, wind = 1f, lightning = 1.5f, exposure = -0.6f, fogTint = new Color(0.42f, 0.47f, 0.52f) },
            new WeatherPreset { name = "Fog", kind = WeatherKind.Fog, sun = 0.45f, fog = 4.5f, wind = 0.1f, exposure = -0.1f, fogTint = new Color(0.66f, 0.7f, 0.7f) },
        };

        [Header("Scene references (wired by the map builder)")]
        public Light sun;
        public Light lightning;
        public Volume globalVolume;
        public WindZone windZone;
        public ParticleSystem rain;
        public AudioClip[] thunderClips;
        [Range(0f, 1f)] public float rainVolume = 0.5f;
        public float maxRainRate = 5000f;
        public float lightningLux = 9000f;

        public string CurrentName => presets.Length > 0 ? presets[_to].name : "-";

        private int _from, _to;
        private float _blend = 1f, _nextChange, _nextStrike = float.MaxValue;
        private float _baseSun, _baseMfp, _baseExposure;
        private Fog _fog;
        private Exposure _exposureOverride;
        private AudioSource _rainSource;
        private float _flash;
        private readonly System.Random _noise = new System.Random();

        private void Awake()
        {
            Instance = this;
            if (sun != null) _baseSun = sun.intensity;
            if (globalVolume != null)
            {
                var profile = globalVolume.profile; // runtime copy, assets stay untouched
                profile.TryGet(out _fog);
                profile.TryGet(out _exposureOverride);
                if (_fog != null) _baseMfp = _fog.meanFreePath.value;
                if (_exposureOverride != null) _baseExposure = _exposureOverride.compensation.value;
            }
            if (lightning != null) lightning.intensity = 0f;
            _from = _to = Mathf.Clamp(randomStart ? Random.Range(0, presets.Length) : startPreset, 0, Mathf.Max(0, presets.Length - 1));
            _nextChange = Time.time + Random.Range(holdTime.x, holdTime.y);
            CreateRainAudio();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Force a weather state (blends over transitionTime).</summary>
        public void SetWeather(int index)
        {
            if (presets.Length == 0) return;
            _from = _to;
            _to = Mathf.Clamp(index, 0, presets.Length - 1);
            _blend = 0f;
            _nextChange = Time.time + transitionTime + Random.Range(holdTime.x, holdTime.y);
        }

        private void Update()
        {
            if (presets.Length == 0) return;
            if (dynamicCycle && Time.time >= _nextChange && _blend >= 1f)
            {
                int next = _to;
                if (presets.Length > 1) while (next == _to) next = Random.Range(0, presets.Length);
                SetWeather(next);
            }
            _blend = Mathf.MoveTowards(_blend, 1f, Time.deltaTime / Mathf.Max(0.1f, transitionTime));
            float t = Mathf.SmoothStep(0f, 1f, _blend);
            var a = presets[_from];
            var b = presets[_to];

            float sunK = Mathf.Lerp(a.sun, b.sun, t);
            float fogK = Mathf.Lerp(a.fog, b.fog, t);
            float rainK = Mathf.Lerp(a.rain, b.rain, t);
            float windK = Mathf.Lerp(a.wind, b.wind, t);
            float strikes = Mathf.Lerp(a.lightning, b.lightning, t);
            float expo = Mathf.Lerp(a.exposure, b.exposure, t);
            Color tint = Color.Lerp(a.fogTint, b.fogTint, t);

            // gusts on top of the base wind
            float gust = Mathf.PerlinNoise(Time.time * 0.13f, 3.7f);
            Wind = Mathf.Clamp01(windK * (0.65f + gust * 0.7f));

            if (sun != null) sun.intensity = _baseSun * sunK;
            if (_fog != null)
            {
                _fog.meanFreePath.value = _baseMfp / Mathf.Max(0.05f, fogK);
                _fog.albedo.value = tint;
            }
            if (_exposureOverride != null) _exposureOverride.compensation.value = _baseExposure + expo;
            if (windZone != null)
            {
                windZone.windMain = Wind * 2.5f;
                windZone.windTurbulence = Wind * 1.5f;
            }
            UpdateRain(rainK);
            UpdateLightning(strikes);
        }

        private void UpdateRain(float amount)
        {
            if (rain != null)
            {
                var cam = Camera.main;
                if (cam != null) rain.transform.position = cam.transform.position + Vector3.up * 9f + cam.transform.forward * 4f;
                var em = rain.emission;
                em.rateOverTime = amount * maxRainRate;
                var vel = rain.velocityOverLifetime;
                float wx = Wind * 4f;
                vel.x = new ParticleSystem.MinMaxCurve(wx * 0.85f, wx * 1.15f);
                vel.y = new ParticleSystem.MinMaxCurve(-13f, -11f);
                vel.z = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
                if (amount > 0.01f && !rain.isPlaying) rain.Play();
                else if (amount <= 0.01f && rain.isPlaying) rain.Stop();
            }
            if (_rainSource != null)
            {
                bool indoors = LR2Audio.IsIndoors(LR2Audio.ListenerPosition);
                _rainSource.volume = Mathf.Lerp(_rainSource.volume, amount * rainVolume * (indoors ? 0.45f : 1f), Time.deltaTime * 2f);
                var lp = _rainSource.GetComponent<AudioLowPassFilter>();
                if (lp != null) lp.cutoffFrequency = Mathf.Lerp(lp.cutoffFrequency, indoors ? 1400f : 7000f, Time.deltaTime * 3f);
            }
        }

        private void UpdateLightning(float perMinute)
        {
            if (perMinute <= 0.01f) _nextStrike = float.MaxValue;
            else if (_nextStrike == float.MaxValue) _nextStrike = Time.time + Random.Range(4f, 60f / perMinute);
            if (Time.time >= _nextStrike)
            {
                _nextStrike = Time.time + Random.Range(0.4f, 2f) * 60f / Mathf.Max(0.1f, perMinute);
                StartCoroutine(Strike());
            }
            if (lightning != null) lightning.intensity = _flash * lightningLux;
        }

        private System.Collections.IEnumerator Strike()
        {
            if (lightning != null)
                lightning.transform.rotation = Quaternion.Euler(Random.Range(35f, 70f), Random.Range(0f, 360f), 0f);
            int flickers = Random.Range(2, 5);
            for (int i = 0; i < flickers; i++)
            {
                _flash = Random.Range(0.5f, 1f);
                yield return new WaitForSeconds(Random.Range(0.04f, 0.1f));
                _flash = 0f;
                yield return new WaitForSeconds(Random.Range(0.03f, 0.18f));
            }
            if (thunderClips != null && thunderClips.Length > 0)
            {
                float distance = Random.Range(400f, 2500f);
                Vector3 l = LR2Audio.ListenerPosition;
                Vector2 d = Random.insideUnitCircle.normalized * 60f;
                LR2Audio.PlayClip(thunderClips[Random.Range(0, thunderClips.Length)], l + new Vector3(d.x, 30f, d.y),
                    Mathf.Lerp(1f, 0.55f, distance / 2500f), Random.Range(0.85f, 1.05f), 30f, 400f, 0.6f, AudioCategory.Sfx, 30, distance / 343f);
            }
        }

        // ---- procedural rain hiss (filtered noise with random droplet crackle)
        private void CreateRainAudio()
        {
            const int rate = 44100;
            var clip = AudioClip.Create("ProceduralRain", rate * 4, 2, rate, false);
            var data = new float[rate * 4 * 2];
            float b0 = 0f, b1 = 0f;
            for (int i = 0; i < data.Length; i += 2)
            {
                float white = (float)(_noise.NextDouble() * 2.0 - 1.0);
                b0 = 0.985f * b0 + white * 0.15f;           // pink-ish body
                b1 = 0.6f * b1 + white * 0.4f;              // hiss
                float drop = _noise.NextDouble() < 0.0009 ? (float)(_noise.NextDouble() * 0.8) : 0f;
                float s = (b0 * 0.5f + b1 * 0.35f) * 0.6f + drop;
                data[i] = s;
                data[i + 1] = s * 0.9f + (float)(_noise.NextDouble() - 0.5) * 0.05f;
            }
            clip.SetData(data, 0);
            var go = new GameObject("RainAudio");
            go.transform.SetParent(transform, false);
            _rainSource = go.AddComponent<AudioSource>();
            _rainSource.clip = clip;
            _rainSource.loop = true;
            _rainSource.spatialBlend = 0f;
            _rainSource.volume = 0f;
            _rainSource.Play();
            go.AddComponent<AudioLowPassFilter>().cutoffFrequency = 7000f;
        }
    }
}
