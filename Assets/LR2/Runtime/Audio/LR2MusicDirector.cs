using LR2.Combat;
using LR2.Core;
using LR2.Game;
using LR2.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LR2.Audio
{
    /// <summary>
    /// Adaptive, progressively layered music system:
    /// - Menu: calm atmospheric tactical briefing score (<c>Resources/LR2/Music/Music_Menu</c>).
    /// - Gameplay: 3 sample-locked synchronized stems (<c>Music_Calm</c>, <c>Music_Tension</c>, <c>Music_Combat</c>)
    ///   that surge organically during firefights, close encounters, hits, and low-health moments, then ebb back to
    ///   calm stealth during quiet traversal — while preserving crisp spectral &amp; gain headroom so footsteps remain audible.
    /// </summary>
    public class LR2MusicDirector : MonoBehaviour
    {
        public static LR2MusicDirector Instance { get; private set; }

        [Header("Headroom caps (multiplied by LR2Settings.MusicVolume)")]
        [SerializeField, Range(0f, 1f)] private float menuCeiling = 0.22f;
        [SerializeField, Range(0f, 1f)] private float calmCeiling = 0.10f;
        [SerializeField, Range(0f, 1f)] private float tensionCeiling = 0.17f;
        [SerializeField, Range(0f, 1f)] private float combatCeiling = 0.28f;

        private AudioSource _menuSrc;
        private AudioSource _calmSrc;
        private AudioSource _tensionSrc;
        private AudioSource _combatSrc;
        private AudioSource _drumrollSrc;
        private AudioLowPassFilter _calmLpf, _tensionLpf, _combatLpf;

        private AudioClip _menuClip, _calmClip, _tensionClip, _combatClip, _drumrollClip;
        private bool _isMenuScene;
        private float _intensity;
        private float _targetIntensity;
        private float _combatHoldUntil;
        private float _nextDrumrollAt;
        private float _nextProximityScan;
        private float _proximityFloor;
        private Combatant _player;

        /// <summary>Current smoothed combat intensity 0..1.</summary>
        public float Intensity => _intensity;
        /// <summary>Target raw intensity 0..1 driven by live events.</summary>
        public float TargetIntensity => _targetIntensity;
        public bool IsMenuMode => _isMenuScene;
        public string StateLabel => _isMenuScene ? "Menu" : _intensity >= 0.65f ? "Combat" : _intensity >= 0.28f ? "Tension" : "Calm";
        public float CalmVolume => _calmSrc != null ? _calmSrc.volume : 0f;
        public float TensionVolume => _tensionSrc != null ? _tensionSrc.volume : 0f;
        public float CombatVolume => _combatSrc != null ? _combatSrc.volume : 0f;
        public float LastDrumrollTime { get; private set; } = -10f;
        public float MenuVolume => _menuSrc != null ? _menuSrc.volume : 0f;
        public float EffectiveTotalGain => _isMenuScene ? MenuVolume : (CalmVolume + TensionVolume * 0.75f + CombatVolume * 0.85f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            EnsureInstance();
        }

        public static LR2MusicDirector EnsureInstance()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("[LR2 MusicDirector]");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<LR2MusicDirector>();
            return Instance;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            LoadClips();
            BuildSources();

            SceneManager.sceneLoaded += OnSceneLoaded;
            GameEvents.ShotFired += OnShotFired;
            GameEvents.Hit += OnHit;
            GameEvents.Killed += OnKilled;
            GameEvents.Noise += OnNoise;

            ConfigureForScene(SceneManager.GetActiveScene().name);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            GameEvents.ShotFired -= OnShotFired;
            GameEvents.Hit -= OnHit;
            GameEvents.Killed -= OnKilled;
            GameEvents.Noise -= OnNoise;
        }

        private void LoadClips()
        {
            _menuClip = Resources.Load<AudioClip>("LR2/Music/Music_Menu");
            _calmClip = Resources.Load<AudioClip>("LR2/Music/Music_Calm");
            _tensionClip = Resources.Load<AudioClip>("LR2/Music/Music_Tension");
            _combatClip = Resources.Load<AudioClip>("LR2/Music/Music_Combat");
        }

        private AudioSource CreateMusicSource(string srcName, out AudioLowPassFilter lpf)
        {
            var child = new GameObject(srcName);
            child.transform.SetParent(transform, false);
            var s = child.AddComponent<AudioSource>();
            s.loop = true;
            s.spatialBlend = 0f;
            s.playOnAwake = false;
            s.priority = 242;
            s.ignoreListenerPause = true; // handle pause ducking smoothly rather than hard-cutting music
            s.volume = 0f;
            lpf = child.AddComponent<AudioLowPassFilter>();
            lpf.cutoffFrequency = 8500f;
            lpf.lowpassResonanceQ = 0.8f;
            return s;
        }

        private void BuildSources()
        {
            _menuSrc = CreateMusicSource("Menu", out _);
            _menuSrc.clip = _menuClip;

            _calmSrc = CreateMusicSource("Stem_Calm", out _calmLpf);
            _tensionSrc = CreateMusicSource("Stem_Tension", out _tensionLpf);
            _combatSrc = CreateMusicSource("Stem_Combat", out _combatLpf);

            _calmSrc.clip = _calmClip;
            _tensionSrc.clip = _tensionClip;
            _combatSrc.clip = _combatClip;

            _drumrollSrc = CreateMusicSource("Music_FirefightDrumroll", out var drumLpf);
            _drumrollSrc.loop = false;
            if (drumLpf != null) { drumLpf.enabled = true; drumLpf.cutoffFrequency = 4600f; }
            _drumrollClip = BuildFirefightDrumrollClip();
            _drumrollSrc.clip = _drumrollClip;
        }

        private static AudioClip BuildFirefightDrumrollClip()
        {
            const int sr = 44100;
            const float dur = 0.85f;
            int n = Mathf.RoundToInt(sr * dur);
            float[] data = new float[n];
            var rng = new System.Random(1987);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                float step = Mathf.Lerp(0.068f, 0.042f, t / dur);
                int stroke = Mathf.FloorToInt(t / step);
                float dt = t - stroke * step;
                if (dt > 0.07f) continue;
                float env = Mathf.Pow(Mathf.Clamp01(t / (dur * 0.88f)), 1.25f) * Mathf.Clamp01((dur - t) / 0.08f);
                float freq = (stroke >= 14) ? Mathf.Lerp(135f, 75f, dt * 18f) : Mathf.Lerp(215f, 145f, dt * 24f);
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                float raw = (Mathf.Sin(2f * Mathf.PI * freq * dt) * 0.62f + noise * Mathf.Exp(-dt * 34f) * 0.38f) * Mathf.Exp(-dt * 28f) * env;
                lp += 0.35f * (raw - lp);
                data[i] = Mathf.Clamp(lp * 0.55f, -0.75f, 0.75f);
            }
            var clip = AudioClip.Create("LR2_FirefightDrumroll", n, 1, sr, false);
            clip.SetData(data, 0);
            return clip;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ConfigureForScene(scene.name);
        }

        private void ConfigureForScene(string sceneName)
        {
            _player = null;
            _intensity = 0.05f;
            _targetIntensity = 0.08f;
            _combatHoldUntil = 0f;
            _proximityFloor = 0f;

            _isMenuScene = string.IsNullOrEmpty(sceneName) || sceneName.Contains("MainMenu") || Combatant.FindLocalPlayer() == null && FindAnyObjectByType<MatchManager>() == null;

            if (_isMenuScene)
            {
                StopStem(_calmSrc);
                StopStem(_tensionSrc);
                StopStem(_combatSrc);
                if (_menuSrc != null && _menuClip != null)
                {
                    if (!_menuSrc.isPlaying) _menuSrc.Play();
                }
            }
            else
            {
                StopStem(_menuSrc);
                StartSynchronizedGameplayStems();
            }
        }

        private void StartSynchronizedGameplayStems()
        {
            if (_calmSrc == null || _tensionSrc == null || _combatSrc == null) return;
            StopStem(_calmSrc);
            StopStem(_tensionSrc);
            StopStem(_combatSrc);

            double dspStart = AudioSettings.dspTime + 0.06;
            if (_calmClip != null) _calmSrc.PlayScheduled(dspStart);
            if (_tensionClip != null) _tensionSrc.PlayScheduled(dspStart);
            if (_combatClip != null) _combatSrc.PlayScheduled(dspStart);
        }

        private static void StopStem(AudioSource s)
        {
            if (s == null) return;
            if (s.isPlaying) s.Stop();
            s.volume = 0f;
        }

        /// <summary>Resets dynamic intensity smoothly back to quiet stealth floor (used on spawn / QA resets).</summary>
        public void ResetToCalm()
        {
            _intensity = 0.05f;
            _targetIntensity = 0.05f;
            _combatHoldUntil = 0f;
            _proximityFloor = 0f;
        }

        /// <summary>Allows runtime gameplay or QA tests to nudge combat intensity cleanly.</summary>
        public void NotifyCombatEvent(float impulse, float holdSeconds)
        {
            if (_isMenuScene) return;
            float prevTarget = _targetIntensity;
            _targetIntensity = Mathf.Clamp01(Mathf.Max(_targetIntensity + impulse * 0.65f, Mathf.Min(1f, _intensity + impulse)));
            _combatHoldUntil = Mathf.Max(_combatHoldUntil, Time.unscaledTime + Mathf.Clamp(holdSeconds, 1.5f, 4.2f));
            if (_drumrollSrc != null && _drumrollClip != null && Time.unscaledTime >= _nextDrumrollAt &&
                ((_targetIntensity >= 0.48f && prevTarget < 0.48f) || impulse >= 0.30f))
            {
                _nextDrumrollAt = Time.unscaledTime + 4.5f;
                LastDrumrollTime = Time.unscaledTime;
                _drumrollSrc.volume = combatCeiling * LR2Settings.MusicVolume * 0.72f;
                _drumrollSrc.PlayOneShot(_drumrollClip);
            }
        }

        private void OnShotFired(ShotEvent e)
        {
            if (_isMenuScene || e.shooter == null) return;
            BindPlayer();
            if (_player == null) return;
            if (e.shooter == _player)
            {
                NotifyCombatEvent(0.32f, 3.0f);
                return;
            }
            float d = Vector3.Distance(_player.Position, e.origin);
            if (d < 30f) NotifyCombatEvent(Mathf.Lerp(0.36f, 0.14f, d / 30f), 2.8f);
        }

        private void OnHit(HitEvent e)
        {
            if (_isMenuScene) return;
            BindPlayer();
            if (_player == null) return;
            if (e.victim == _player)
                NotifyCombatEvent(e.damage >= 28f ? 0.58f : 0.40f, 3.8f);
            else if (e.attacker == _player && e.victim != _player)
                NotifyCombatEvent(e.killed ? 0.62f : 0.36f, 3.4f);
        }

        private void OnKilled(KillEvent e)
        {
            if (_isMenuScene) return;
            BindPlayer();
            if (_player == null) return;
            if (e.killer == _player) NotifyCombatEvent(0.50f, 3.2f);
            else if (e.victim == _player) _targetIntensity = Mathf.Min(_targetIntensity, 0.25f);
        }

        private void OnNoise(NoiseEvent e)
        {
            if (_isMenuScene || e.type != NoiseType.Explosion) return;
            BindPlayer();
            if (_player != null && Vector3.Distance(_player.Position, e.position) < 25f)
                NotifyCombatEvent(0.42f, 3.2f);
        }

        private void BindPlayer()
        {
            if (_player == null) _player = Combatant.FindLocalPlayer();
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f) return;

            float userMusic = LR2Settings.MusicVolume;

            if (_isMenuScene)
            {
                if (_menuSrc != null)
                {
                    float targetMenu = menuCeiling * userMusic;
                    _menuSrc.volume = Mathf.MoveTowards(_menuSrc.volume, targetMenu, dt * 0.25f);
                }
                return;
            }

            BindPlayer();
            if (Time.unscaledTime >= _nextProximityScan)
            {
                _nextProximityScan = Time.unscaledTime + 0.45f;
                UpdateProximityFloor();
            }

            float now = Time.unscaledTime;
            var mm = MatchManager.Instance;
            bool playingMatch = mm == null || mm.State == MatchState.Playing;
            bool ended = mm != null && mm.State == MatchState.Ended;

            // Organic decay after firefight quiet window
            float baseFloor = playingMatch ? Mathf.Max(0.06f, _proximityFloor) : 0.04f;
            if (ended) baseFloor = 0.02f;

            if (now > _combatHoldUntil)
                _targetIntensity = Mathf.MoveTowards(_targetIntensity, baseFloor, dt * 0.24f);
            else
                _targetIntensity = Mathf.Max(_targetIntensity, baseFloor);

            // Fast surge into firefights (no delayed lag) and smooth gradual fade back into calm stealth
            float rate = _targetIntensity > _intensity ? 2.40f : 0.22f;
            _intensity = Mathf.MoveTowards(_intensity, _targetIntensity, dt * rate);

            // Never duck/dim music on player gunfire — keep combat score pulsing live while capping treble under footsteps
            float stateAtten = 1f;
            if (AudioListener.pause) stateAtten *= 0.35f;
            if (KillcamDirector.IsPlaying) stateAtten *= 0.42f;
            if (ended) stateAtten *= 0.40f;
            if (_player != null && !_player.IsAlive) stateAtten *= 0.45f;
            float master = userMusic * stateAtten;

            float gCalm = calmCeiling * Mathf.Lerp(0.85f, 0.38f, _intensity) * master;
            float gTension = tensionCeiling * Mathf.SmoothStep(0.14f, 0.72f, _intensity) * master;
            float gCombat = combatCeiling * Mathf.SmoothStep(0.42f, 0.96f, _intensity) * master;

            if (_calmSrc != null) _calmSrc.volume = gCalm;
            if (_tensionSrc != null) _tensionSrc.volume = gTension;
            if (_combatSrc != null) _combatSrc.volume = gCombat;

            // Open filter progressively during fights while keeping upper transient crunch bands clear for footsteps
            float cutoffCalm = Mathf.Lerp(3200f, 5200f, _intensity);
            float cutoffTension = Mathf.Lerp(4200f, 7000f, _intensity);
            float cutoffCombat = Mathf.Lerp(5000f, 8000f, _intensity);
            if (_calmLpf != null) _calmLpf.cutoffFrequency = cutoffCalm;
            if (_tensionLpf != null) _tensionLpf.cutoffFrequency = cutoffTension;
            if (_combatLpf != null) _combatLpf.cutoffFrequency = cutoffCombat;
        }

        private void UpdateProximityFloor()
        {
            if (_player == null || !_player.IsAlive)
            {
                _proximityFloor = 0f;
                return;
            }
            float minDist = 999f;
            var all = Combatant.All;
            Vector3 pPos = _player.Position;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c == null || c == _player || !c.IsAlive || !TeamUtil.AreHostile(_player, c)) continue;
                float d = Vector3.Distance(pPos, c.Position);
                if (d < minDist) minDist = d;
            }

            float prox = minDist < 22f ? Mathf.Lerp(0.45f, 0.10f, Mathf.Clamp01((minDist - 5f) / 17f)) : 0f;
            float lowHp = (_player.Health != null && _player.Health.Normalized < 0.4f) ? 0.32f : 0f;
            _proximityFloor = Mathf.Max(prox, lowHp);
        }
    }
}
