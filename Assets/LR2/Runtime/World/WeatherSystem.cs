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
        public float EffectiveRain { get; private set; }
        public float Wetness { get; private set; }
        public int WetSurfaceCount => _wetMats.Count + _wetTerrainLayers.Count;

        private struct WetMatRecord
        {
            public Material mat;
            public bool isClothOrGear;
            public bool hasBaseColor; public Color origColor;
            public bool hasSmoothness; public float origSmoothness;
            public bool hasRemapMin; public float origRemapMin;
            public bool hasRemapMax; public float origRemapMax;
            public bool hasCoatMask; public float origCoatMask;
        }

        private struct WetTerrainLayerRecord
        {
            public TerrainLayer layer;
            public float origSmoothness;
            public Color origDiffuseMax;
        }

        private struct VisorDrop
        {
            public float x, y, speed, size, alpha, aspect;
        }

        private readonly System.Collections.Generic.List<WetMatRecord> _wetMats = new System.Collections.Generic.List<WetMatRecord>(64);
        private readonly System.Collections.Generic.List<WetTerrainLayerRecord> _wetTerrainLayers = new System.Collections.Generic.List<WetTerrainLayerRecord>(16);
        private bool _wetTargetsCollected;
        private float _appliedWetness = -1f;
        private float _thunderStart = -999f;
        private float _thunderRainUntil = -999f;
        private float _thunderRainBoost;

        private ParticleSystem _splashBurstPS;
        private ParticleSystem _puddleRipplePS;
        private Texture2D _rainStreakTex;
        private Texture2D _rippleRingTex;
        private Texture2D _dropletTex;
        private Material _runtimeRainMat;
        private Material _runtimeSplashMat;
        private Material _runtimeRippleMat;
        private AudioClip _procThunderClip;
        private ParticleSystem.EmitParams _splashEmit;
        private ParticleSystem.EmitParams _rippleEmit;
        private float _splashAcc;
        private readonly VisorDrop[] _visorDrops = new VisorDrop[18];
        private bool _visorInited;

        private int _from, _to;
        private float _blend = 1f, _nextChange, _nextStrike = float.MaxValue;
        private float _baseSun, _baseMfp, _baseExposure, _baseHdriExposure, _baseHdriMult;
        private Fog _fog;
        private Exposure _exposureOverride;
        private HDRISky _hdriSky;
        private PhysicallyBasedSky _physSky;
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
                profile.TryGet(out _hdriSky);
                profile.TryGet(out _physSky);
                if (_fog != null) _baseMfp = _fog.meanFreePath.value;
                if (_exposureOverride != null) _baseExposure = _exposureOverride.compensation.value;
                if (_hdriSky != null)
                {
                    _baseHdriExposure = _hdriSky.exposure.value > 5f ? _hdriSky.exposure.value : 10.2f;
                    _baseHdriMult = _hdriSky.multiplier.value;
                }
            }
            if (lightning != null) lightning.intensity = 0f;
            _from = _to = Mathf.Clamp(randomStart ? Random.Range(0, presets.Length) : startPreset, 0, Mathf.Max(0, presets.Length - 1));
            _nextChange = Time.time + Random.Range(holdTime.x, holdTime.y);
            if (maxRainRate < 1400f) maxRainRate = 1850f;
            if (lightningLux < 2500f) lightningLux = 18000f;
            CreateRainAudio();
            EnsureRealisticRainRig();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            RestoreWetSurfaces();
            if (_rainStreakTex != null) Destroy(_rainStreakTex);
            if (_rippleRingTex != null) Destroy(_rippleRingTex);
            if (_dropletTex != null) Destroy(_dropletTex);
            if (_runtimeRainMat != null) Destroy(_runtimeRainMat);
            if (_runtimeSplashMat != null) Destroy(_runtimeSplashMat);
            if (_runtimeRippleMat != null) Destroy(_runtimeRippleMat);
            if (_procThunderClip != null) Destroy(_procThunderClip);
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
            float thunderK = 0f;
            if (Time.time < _thunderRainUntil)
            {
                float fadeIn = Mathf.Clamp01((Time.time - _thunderStart) / 1.8f);
                float fadeOut = Mathf.Clamp01((_thunderRainUntil - Time.time) / 5f);
                thunderK = _thunderRainBoost * fadeIn * fadeOut;
            }
            float effectiveRain = Mathf.Clamp01(Mathf.Max(rainK, thunderK));
            EffectiveRain = effectiveRain;
            if (effectiveRain > 0.1f) Wind = Mathf.Max(Wind, 0.25f + effectiveRain * 0.70f);

            // Darken sun, thicken storm fog & brood HDRi sky when rain builds; flash illuminates storm clouds on lightning
            float stormDark = Mathf.Clamp01(Mathf.Max(effectiveRain * 0.85f, thunderK * 0.85f));
            if (sun != null)
            {
                float stormSun = Mathf.Max(0.58f, sunK * Mathf.Lerp(1f, 0.72f, stormDark)) + _flash * 1.35f;
                sun.intensity = _baseSun * stormSun;
            }
            if (_fog != null && stormDark > 0.05f)
            {
                float stormMfp = Mathf.Min(_fog.meanFreePath.value, Mathf.Lerp(_baseMfp, Mathf.Max(_baseMfp * 0.75f, 85f), stormDark));
                _fog.meanFreePath.value = stormMfp;
                _fog.albedo.value = Color.Lerp(tint, new Color(0.38f, 0.44f, 0.52f), stormDark * 0.55f);
            }
            if (_hdriSky != null)
            {
                _hdriSky.exposure.Override(Mathf.Max(9.2f, _baseHdriExposure - 0.85f * stormDark) + _flash * 2.2f);
                _hdriSky.multiplier.Override(_baseHdriMult * (Mathf.Lerp(1f, 0.72f, stormDark) + _flash * 1.45f));
            }
            else if (_physSky != null)
            {
                _physSky.aerosolDensity.Override(Mathf.Lerp(0.22f, 0.75f, stormDark));
                _physSky.exposure.Override(-2.1f * stormDark + _flash * 2.5f);
            }

            UpdateRain(effectiveRain);
            UpdateGroundSplashesAndVisor(effectiveRain);
            UpdateWorldWetness(effectiveRain);
            UpdateLightning(strikes);
        }

        private void UpdateRain(float amount)
        {
            if (rain != null)
            {
                var cam = Camera.main;
                if (cam != null) rain.transform.position = cam.transform.position + Vector3.up * 22f + cam.transform.forward * 6f;
                var em = rain.emission;
                em.rateOverTime = amount * maxRainRate;
                var vel = rain.velocityOverLifetime;
                float wx = Wind * 3.4f;
                vel.x = new ParticleSystem.MinMaxCurve(wx * 0.85f, wx * 1.15f);
                vel.y = new ParticleSystem.MinMaxCurve(-22f, -18f);
                vel.z = new ParticleSystem.MinMaxCurve(-0.35f, 0.35f);
                if (amount > 0.01f && !rain.isPlaying) rain.Play();
                else if (amount <= 0.01f && rain.isPlaying) rain.Stop();
            }
            if (_rainSource != null)
            {
                bool indoors = LR2Audio.IsIndoors(LR2Audio.ListenerPosition);
                float mix = LR2Audio.CategoryVolume(AudioCategory.Ambience) / 0.68f;
                _rainSource.volume = Mathf.Lerp(_rainSource.volume, amount * rainVolume * mix * (indoors ? 0.45f : 1f), Time.deltaTime * 2f);
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
                TriggerThunderRain(0.95f, 38f);
                StartCoroutine(Strike());
            }
            if (lightning != null) lightning.intensity = _flash * lightningLux;
        }

        /// <summary>
        /// Called when thunder sounds (either storm lightning or external match-start thunder).
        /// Forces realistic storm rain + wind + gradual world wetness even if the current weather preset was dry.
        /// </summary>
        public void TriggerThunderRain(float intensity = 0.92f, float durationSec = 36f, bool withFlash = false)
        {
            if (Time.time >= _thunderRainUntil) _thunderStart = Time.time;
            _thunderRainUntil = Mathf.Max(_thunderRainUntil, Time.time + Mathf.Max(4f, durationSec));
            _thunderRainBoost = Mathf.Max(_thunderRainBoost, Mathf.Clamp01(intensity));
            if (rain != null && !rain.isPlaying) rain.Play();
            if (withFlash) _flash = Mathf.Max(_flash, 0.75f);
        }

        /// <summary>
        /// Immediately advances or tests storm wetness for deterministic verification or storm warmup.
        /// </summary>
        public void SetWetnessForWarmup(float targetWetness)
        {
            CollectWetSurfaceTargets();
            Wetness = Mathf.Clamp01(targetWetness);
            _appliedWetness = Wetness;
            ApplyWetnessToWorld(Wetness);
        }

        private void EnsureRealisticRainRig()
        {
            if (lightning == null)
            {
                var lGo = new GameObject("LightningFlash_Runtime");
                lGo.transform.SetParent(transform, false);
                lGo.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
                lightning = lGo.AddComponent<Light>();
                lightning.type = LightType.Directional;
                lightning.color = new Color(0.84f, 0.91f, 1.0f);
                lightning.intensity = 0f;
                lightning.shadows = LightShadows.None;
            }

            if (rain == null)
            {
                var rGo = new GameObject("Rain_Runtime");
                rGo.transform.SetParent(transform, false);
                rain = rGo.AddComponent<ParticleSystem>();
            }

            _rainStreakTex = BuildRainStreakTexture();
            _rippleRingTex = BuildRippleRingTexture();
            _dropletTex = BuildDropletTexture();

            var pShader = Shader.Find("LR2/ParticleUnlit") ?? Shader.Find("Sprites/Default");
            _runtimeRainMat = new Material(pShader);
            _runtimeRainMat.SetTexture("_MainTex", _rainStreakTex);
            _runtimeRainMat.SetColor("_Color", new Color(0.80f, 0.88f, 0.96f, 0.36f));
            _runtimeRainMat.SetFloat("_Intensity", 0.95f);
            _runtimeRainMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _runtimeRainMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _runtimeRainMat.renderQueue = 3000;

            // Fine high-altitude optical rain streaks + strict 3D roof collision (keeps interiors dry)
            var main = rain.main;
            main.loop = true;
            main.maxParticles = 5200;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.35f, 1.85f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(19f, 24f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.015f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.76f, 0.86f, 0.96f, 0.26f), new Color(0.90f, 0.95f, 1f, 0.48f));
            main.gravityModifier = 0.0f;

            var shape = rain.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(34f, 3.5f, 34f);

            var vel = rain.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(1.4f, 3.0f);
            vel.y = new ParticleSystem.MinMaxCurve(-22f, -18f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);

            var col = rain.collision;
            col.enabled = true;
            col.type = ParticleSystemCollisionType.World;
            col.mode = ParticleSystemCollisionMode.Collision3D;
            col.collidesWith = LR2.Core.LR2Layers.EnvironmentMask;
            col.dampen = 1f;
            col.bounce = 0f;
            col.lifetimeLoss = 1f;
            col.radiusScale = 0.04f;

            var psr = rain.GetComponent<ParticleSystemRenderer>();
            psr.renderMode = ParticleSystemRenderMode.Stretch;
            psr.velocityScale = 0.022f;
            psr.lengthScale = 0.14f;
            psr.cameraVelocityScale = 0.02f;
            psr.sharedMaterial = _runtimeRainMat;

            // Ground impact crown droplet bursts
            var sGo = new GameObject("RainSplashBursts_Runtime");
            sGo.transform.SetParent(transform, false);
            _splashBurstPS = sGo.AddComponent<ParticleSystem>();
            var sm = _splashBurstPS.main;
            sm.loop = true;
            sm.playOnAwake = false;
            sm.maxParticles = 900;
            sm.simulationSpace = ParticleSystemSimulationSpace.World;
            sm.startLifetime = new ParticleSystem.MinMaxCurve(0.14f, 0.24f);
            sm.startSpeed = new ParticleSystem.MinMaxCurve(1.1f, 2.4f);
            sm.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.055f);
            sm.gravityModifier = 2.4f;
            var sem = _splashBurstPS.emission; sem.enabled = false;
            var scol = _splashBurstPS.colorOverLifetime;
            scol.enabled = true;
            var sg = new Gradient();
            sg.SetKeys(
                new[] { new GradientColorKey(new Color(0.86f, 0.93f, 1f), 0f), new GradientColorKey(new Color(0.80f, 0.90f, 1f), 1f) },
                new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0f, 1f) });
            scol.color = sg;
            _runtimeSplashMat = new Material(_runtimeRainMat);
            _runtimeSplashMat.SetTexture("_MainTex", _dropletTex);
            _runtimeSplashMat.SetColor("_Color", new Color(0.86f, 0.94f, 1f, 0.85f));
            var spsr = sGo.GetComponent<ParticleSystemRenderer>();
            spsr.renderMode = ParticleSystemRenderMode.Billboard;
            spsr.sharedMaterial = _runtimeSplashMat;

            // Expanding concentric puddle ripples across horizontal surfaces
            var rpGo = new GameObject("RainPuddleRipples_Runtime");
            rpGo.transform.SetParent(transform, false);
            _puddleRipplePS = rpGo.AddComponent<ParticleSystem>();
            var rm = _puddleRipplePS.main;
            rm.loop = true;
            rm.playOnAwake = false;
            rm.maxParticles = 600;
            rm.simulationSpace = ParticleSystemSimulationSpace.World;
            rm.startLifetime = new ParticleSystem.MinMaxCurve(0.38f, 0.58f);
            rm.startSpeed = 0f;
            rm.startSize = new ParticleSystem.MinMaxCurve(0.32f, 0.56f);
            var rem = _puddleRipplePS.emission; rem.enabled = false;
            var rsz = _puddleRipplePS.sizeOverLifetime;
            rsz.enabled = true;
            rsz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.16f), new Keyframe(1f, 1.0f)));
            var rcol = _puddleRipplePS.colorOverLifetime;
            rcol.enabled = true;
            var rg = new Gradient();
            rg.SetKeys(
                new[] { new GradientColorKey(new Color(0.84f, 0.92f, 1f), 0f), new GradientColorKey(new Color(0.84f, 0.92f, 1f), 1f) },
                new[] { new GradientAlphaKey(0.65f, 0f), new GradientAlphaKey(0.32f, 0.55f), new GradientAlphaKey(0f, 1f) });
            rcol.color = rg;
            _runtimeRippleMat = new Material(_runtimeRainMat);
            _runtimeRippleMat.SetTexture("_MainTex", _rippleRingTex);
            _runtimeRippleMat.SetColor("_Color", new Color(0.82f, 0.92f, 1f, 0.72f));
            var rpsr = rpGo.GetComponent<ParticleSystemRenderer>();
            rpsr.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            rpsr.sharedMaterial = _runtimeRippleMat;

            // Play a procedural thunder fallback clip if map omitted thunderClips
            if (thunderClips == null || thunderClips.Length == 0)
            {
                _procThunderClip = BuildProceduralThunderClip();
                thunderClips = new[] { _procThunderClip };
            }
        }

        private void UpdateGroundSplashesAndVisor(float rainAmount)
        {
            if (rain != null)
            {
                var vel = rain.velocityOverLifetime;
                vel.x = new ParticleSystem.MinMaxCurve(Wind * 3.8f - 0.8f, Wind * 3.8f + 1.1f);
            }

            if (rainAmount <= 0.04f || _splashBurstPS == null || _puddleRipplePS == null) return;
            var cam = Camera.main;
            if (cam == null) return;

            // Emit real ground crown splashes & concentric water ripples strictly on outdoor surfaces
            float targetPerSec = Mathf.Lerp(12f, 160f, rainAmount);
            _splashAcc += targetPerSec * Time.deltaTime;
            int burstCount = Mathf.Min(12, Mathf.FloorToInt(_splashAcc));
            _splashAcc -= burstCount;

            Vector3 originCenter = cam.transform.position + cam.transform.forward * 7f;
            int mask = LR2.Core.LR2Layers.EnvironmentMask;
            for (int i = 0; i < burstCount; i++)
            {
                float angle = (float)(_noise.NextDouble() * Mathf.PI * 2.0);
                float dist = Mathf.Sqrt((float)_noise.NextDouble()) * 16f;
                Vector3 sampleTop = new Vector3(
                    originCenter.x + Mathf.Cos(angle) * dist,
                    cam.transform.position.y + 18f,
                    originCenter.z + Mathf.Sin(angle) * dist);
                if (!Physics.Raycast(sampleTop, Vector3.down, out var hit, 42f, mask, QueryTriggerInteraction.Ignore)) continue;
                if (Vector3.Dot(hit.normal, Vector3.up) < 0.65f) continue;

                // Small 2-3 droplet upward splash crown
                int drops = _noise.Next(2, 4);
                for (int d = 0; d < drops; d++)
                {
                    _splashEmit.position = hit.point + hit.normal * 0.025f;
                    _splashEmit.velocity = new Vector3(
                        ((float)_noise.NextDouble() - 0.5f) * 1.4f,
                        1.15f + (float)_noise.NextDouble() * 1.25f,
                        ((float)_noise.NextDouble() - 0.5f) * 1.4f);
                    _splashBurstPS.Emit(_splashEmit, 1);
                }

                // Expanding horizontal water ripple once surfaces start getting wet (or heavy downpour)
                if (Wetness > 0.12f || rainAmount > 0.5f)
                {
                    _rippleEmit.position = hit.point + hit.normal * 0.02f;
                    _rippleEmit.velocity = Vector3.zero;
                    _puddleRipplePS.Emit(_rippleEmit, 1);
                }
            }
        }

        private void UpdateWorldWetness(float rainAmount)
        {
            if (!_wetTargetsCollected && Time.frameCount > 3) CollectWetSurfaceTargets();

            if (rainAmount > 0.08f)
            {
                float targetWet = Mathf.Clamp01(0.35f + rainAmount * 0.70f);
                // Reaches full glistening wet sheen over ~16-22s of steady rain
                float rate = 0.042f + 0.040f * rainAmount;
                Wetness = Mathf.MoveTowards(Wetness, targetWet, rate * Time.deltaTime);
            }
            else if (Wetness > 0f)
            {
                Wetness = Mathf.MoveTowards(Wetness, 0f, 0.018f * Time.deltaTime);
            }

            if (Mathf.Abs(Wetness - _appliedWetness) >= 0.008f)
            {
                _appliedWetness = Wetness;
                ApplyWetnessToWorld(Wetness);
            }
        }

        private void CollectWetSurfaceTargets()
        {
            _wetTargetsCollected = true;
            _wetMats.Clear();
            _wetTerrainLayers.Clear();
            var seen = new System.Collections.Generic.HashSet<Material>();

            foreach (var r in FindObjectsByType<Renderer>())
            {
                if (r == null || r is ParticleSystemRenderer) continue;
                bool isCharacterOrWeapon = r is SkinnedMeshRenderer ||
                    r.GetComponentInParent<LR2.Characters.SoldierController>() != null ||
                    r.GetComponentInParent<LR2.Weapons.LR2Item>() != null;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || !seen.Add(m) || m.shader == null) continue;
                    string sName = m.shader.name;
                    if (!sName.Contains("Lit")) continue;
                    if (m == _runtimeRainMat || m == _runtimeSplashMat || m == _runtimeRippleMat) continue;

                    string mn = m.name ?? "";
                    bool fabricHint = isCharacterOrWeapon ||
                        mn.IndexOf("Glove", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                        mn.IndexOf("Cloth", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                        mn.IndexOf("Swat", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                        mn.IndexOf("Uniform", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                        mn.IndexOf("Mask", System.StringComparison.OrdinalIgnoreCase) >= 0;

                    var rec = new WetMatRecord { mat = m, isClothOrGear = fabricHint };
                    if (m.HasProperty("_BaseColor")) { rec.hasBaseColor = true; rec.origColor = m.GetColor("_BaseColor"); }
                    if (m.HasProperty("_Smoothness")) { rec.hasSmoothness = true; rec.origSmoothness = m.GetFloat("_Smoothness"); }
                    if (m.HasProperty("_SmoothnessRemapMin")) { rec.hasRemapMin = true; rec.origRemapMin = m.GetFloat("_SmoothnessRemapMin"); }
                    if (m.HasProperty("_SmoothnessRemapMax")) { rec.hasRemapMax = true; rec.origRemapMax = m.GetFloat("_SmoothnessRemapMax"); }
                    if (m.HasProperty("_CoatMask")) { rec.hasCoatMask = true; rec.origCoatMask = m.GetFloat("_CoatMask"); }
                    if (rec.hasSmoothness || rec.hasRemapMax || rec.hasBaseColor) _wetMats.Add(rec);
                }
            }

            var seenLayers = new System.Collections.Generic.HashSet<TerrainLayer>();
            foreach (var ter in Terrain.activeTerrains)
            {
                if (ter == null || ter.terrainData == null) continue;
                var layers = ter.terrainData.terrainLayers;
                if (layers == null) continue;
                for (int i = 0; i < layers.Length; i++)
                {
                    var l = layers[i];
                    if (l == null || !seenLayers.Add(l)) continue;
                    _wetTerrainLayers.Add(new WetTerrainLayerRecord
                    {
                        layer = l,
                        origSmoothness = l.smoothness,
                        origDiffuseMax = l.diffuseRemapMax
                    });
                }
            }
        }

        private void ApplyWetnessToWorld(float w)
        {
            for (int i = 0; i < _wetMats.Count; i++)
            {
                var r = _wetMats[i];
                if (r.mat == null) continue;
                if (r.isClothOrGear)
                {
                    // Damp tactical cloth/gloves slightly darker without turning into glossy latex plastic
                    float clothShade = Mathf.Lerp(1f, 0.86f, w);
                    if (r.hasBaseColor)
                        r.mat.SetColor("_BaseColor", new Color(r.origColor.r * clothShade, r.origColor.g * clothShade, r.origColor.b * clothShade, r.origColor.a));
                    if (r.hasSmoothness) r.mat.SetFloat("_Smoothness", Mathf.Lerp(r.origSmoothness, Mathf.Min(Mathf.Max(r.origSmoothness, 0.28f), 0.34f), w));
                    continue;
                }

                float shade = Mathf.Lerp(1f, 0.72f, w);
                if (r.hasBaseColor)
                {
                    Color wetCol = new Color(r.origColor.r * shade, r.origColor.g * shade, r.origColor.b * shade, r.origColor.a);
                    r.mat.SetColor("_BaseColor", wetCol);
                }
                if (r.hasSmoothness) r.mat.SetFloat("_Smoothness", Mathf.Lerp(r.origSmoothness, Mathf.Max(r.origSmoothness, 0.88f), w));
                if (r.hasRemapMin) r.mat.SetFloat("_SmoothnessRemapMin", Mathf.Lerp(r.origRemapMin, Mathf.Max(r.origRemapMin, 0.42f), w));
                if (r.hasRemapMax) r.mat.SetFloat("_SmoothnessRemapMax", Mathf.Lerp(r.origRemapMax, Mathf.Max(r.origRemapMax, 0.94f), w));
                if (r.hasCoatMask) r.mat.SetFloat("_CoatMask", Mathf.Lerp(r.origCoatMask, Mathf.Max(r.origCoatMask, 0.36f), w));
            }

            for (int i = 0; i < _wetTerrainLayers.Count; i++)
            {
                var tr = _wetTerrainLayers[i];
                if (tr.layer == null) continue;
                tr.layer.smoothness = Mathf.Lerp(tr.origSmoothness, 0.85f, w);
                Color dm = tr.origDiffuseMax;
                tr.layer.diffuseRemapMax = new Color(dm.r * Mathf.Lerp(1f, 0.76f, w), dm.g * Mathf.Lerp(1f, 0.76f, w), dm.b * Mathf.Lerp(1f, 0.78f, w), dm.a);
            }
        }

        private void RestoreWetSurfaces()
        {
            for (int i = 0; i < _wetMats.Count; i++)
            {
                var r = _wetMats[i];
                if (r.mat == null) continue;
                if (r.hasBaseColor) r.mat.SetColor("_BaseColor", r.origColor);
                if (r.hasSmoothness) r.mat.SetFloat("_Smoothness", r.origSmoothness);
                if (r.hasRemapMin) r.mat.SetFloat("_SmoothnessRemapMin", r.origRemapMin);
                if (r.hasRemapMax) r.mat.SetFloat("_SmoothnessRemapMax", r.origRemapMax);
                if (r.hasCoatMask) r.mat.SetFloat("_CoatMask", r.origCoatMask);
            }
            for (int i = 0; i < _wetTerrainLayers.Count; i++)
            {
                var tr = _wetTerrainLayers[i];
                if (tr.layer == null) continue;
                tr.layer.smoothness = tr.origSmoothness;
                tr.layer.diffuseRemapMax = tr.origDiffuseMax;
            }
            _wetMats.Clear();
            _wetTerrainLayers.Clear();
        }

        private void OnGUI()
        {
            if (Application.isBatchMode || EffectiveRain < 0.25f || _dropletTex == null) return;
            var cam = Camera.main;
            if (cam == null) return;
            // Subtle peripheral visor droplets only when outdoors under open rain
            if (Physics.Raycast(cam.transform.position + Vector3.up * 0.25f, Vector3.up, 28f, LR2.Core.LR2Layers.EnvironmentMask, QueryTriggerInteraction.Ignore))
                return;

            if (!_visorInited)
            {
                _visorInited = true;
                for (int i = 0; i < _visorDrops.Length; i++)
                {
                    bool leftEdge = (i % 2) == 0;
                    _visorDrops[i] = new VisorDrop
                    {
                        x = leftEdge ? Random.Range(0.02f, 0.22f) : Random.Range(0.78f, 0.96f),
                        y = Random.value,
                        speed = Random.Range(0.06f, 0.16f),
                        size = Random.Range(14f, 32f),
                        alpha = Random.Range(0.07f, 0.15f),
                        aspect = Random.Range(1.4f, 2.3f)
                    };
                }
            }

            Color prev = GUI.color;
            float dt = Time.deltaTime;
            for (int i = 0; i < _visorDrops.Length; i++)
            {
                ref var d = ref _visorDrops[i];
                d.y += d.speed * (0.6f + EffectiveRain) * dt;
                if (d.y > 1.05f) { d.y = -0.05f; d.x = ((i % 2) == 0) ? Random.Range(0.02f, 0.22f) : Random.Range(0.78f, 0.96f); }
                GUI.color = new Color(0.84f, 0.91f, 1f, d.alpha * Mathf.Clamp01((EffectiveRain - 0.2f) * 1.4f));
                GUI.DrawTexture(new Rect(d.x * Screen.width, d.y * Screen.height, d.size, d.size * d.aspect), _dropletTex);
            }
            GUI.color = prev;
        }

        private static Texture2D BuildRainStreakTexture()
        {
            const int w = 32, h = 128;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                float v = y / (float)(h - 1);
                float vEnv = Mathf.Sin(v * Mathf.PI);
                vEnv = Mathf.Pow(vEnv, 0.55f);
                // Brighter leading drop tip toward lower half
                float headGlow = Mathf.Exp(-Mathf.Pow((v - 0.72f) / 0.22f, 2f));
                for (int x = 0; x < w; x++)
                {
                    float u = (x - (w - 1) * 0.5f) / (w * 0.5f);
                    float uEnv = Mathf.Exp(-u * u * 5.2f);
                    float a = Mathf.Clamp01(uEnv * vEnv * (0.45f + 0.55f * headGlow));
                    byte b = (byte)Mathf.RoundToInt(a * 255f);
                    px[y * w + x] = new Color32(226, 240, 255, b);
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        private static Texture2D BuildRippleRingTexture()
        {
            const int s = 64;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dx = (x - (s - 1) * 0.5f) / (s * 0.5f);
                    float dy = (y - (s - 1) * 0.5f) / (s * 0.5f);
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float outer = Mathf.Exp(-Mathf.Pow((r - 0.78f) / 0.11f, 2f));
                    float inner = Mathf.Exp(-Mathf.Pow((r - 0.48f) / 0.09f, 2f)) * 0.5f;
                    float edgeFade = 1f - Mathf.SmoothStep(0.85f, 1.0f, r);
                    byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01((outer + inner) * edgeFade) * 255f);
                    px[y * s + x] = new Color32(225, 240, 255, a);
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        private static Texture2D BuildDropletTexture()
        {
            const int s = 32;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dx = (x - (s - 1) * 0.5f) / (s * 0.5f);
                    float dy = (y - (s - 1) * 0.5f) / (s * 0.5f);
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float spec = Mathf.Exp(-((dx + 0.25f) * (dx + 0.25f) + (dy - 0.25f) * (dy - 0.25f)) * 12f);
                    float a = Mathf.Clamp01(1f - Mathf.SmoothStep(0.35f, 0.96f, r));
                    byte lum = (byte)Mathf.RoundToInt(Mathf.Lerp(210f, 255f, spec));
                    px[y * s + x] = new Color32(lum, (byte)Mathf.Min(255, lum + 8), 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        private static AudioClip BuildProceduralThunderClip()
        {
            const int sr = 44100;
            const float dur = 4.2f;
            int n = Mathf.RoundToInt(sr * dur);
            var data = new float[n];
            var rng = new System.Random(9001);
            float lp = 0f, lpSub = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                float crack = Mathf.Exp(-t * 9f) * (0.65f + 0.35f * Mathf.Sin(t * 210f));
                float rumbleEnv = Mathf.Clamp01(t / 0.14f) * Mathf.Exp(-Mathf.Max(0f, t - 0.18f) * 0.85f) * (1f + 0.28f * Mathf.Sin(t * 14f));
                float w = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += 0.032f * (w - lp);
                lpSub += 0.006f * (w - lpSub);
                data[i] = Mathf.Clamp((crack * lp * 1.4f + rumbleEnv * (lpSub * 4.5f + lp * 1.3f)) * 0.72f, -0.95f, 0.95f);
            }
            var clip = AudioClip.Create("ProceduralThunder", n, 1, sr, false);
            clip.SetData(data, 0);
            return clip;
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
