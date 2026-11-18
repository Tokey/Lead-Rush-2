using LR2.Combat;
using LR2.Core;
using LR2.FX;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace LR2.UI
{
    /// <summary>
    /// Screen-space damage feedback through HDRP post: red vignette + chromatic punch on hits, desaturation at low
    /// health, near-miss suppression flicker. Builds its own high-priority runtime Volume.
    /// </summary>
    public class CombatPostFX : MonoBehaviour
    {
        [SerializeField] private float hitVignette = 0.42f;
        [SerializeField] private float hitAberration = 0.6f;
        [SerializeField] private float lowHealthDesaturation = -55f;
        [SerializeField] private float suppressionVignette = 0.18f;

        private Volume _volume;
        private Vignette _vignette;
        private ChromaticAberration _aberration;
        private ColorAdjustments _color;
        private MotionBlur _motionBlur;
        private FilmGrain _filmGrain;
        private Combatant _player;
        private float _hit, _suppression;

        private void Awake()
        {
            var go = new GameObject("[LR2 Combat PostFX]");
            go.transform.SetParent(transform, false);
            _volume = go.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 100f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "CombatPostFX (runtime)";
            _vignette = profile.Add<Vignette>(false);
            _vignette.color.Override(new Color(0.45f, 0f, 0f));
            _vignette.intensity.Override(0f);
            _vignette.smoothness.Override(0.85f);
            _vignette.active = false;
            _aberration = profile.Add<ChromaticAberration>(false);
            _aberration.intensity.Override(0f);
            _aberration.active = false;
            _color = profile.Add<ColorAdjustments>(false);
            _color.saturation.Override(0f);
            _color.contrast.Override(0f);
            _color.active = false;
            _motionBlur = profile.Add<MotionBlur>(false);
            _motionBlur.intensity.Override(0.25f);
            _motionBlur.active = false;
            _filmGrain = profile.Add<FilmGrain>(false);
            _filmGrain.intensity.Override(0.18f);
            _filmGrain.active = false;
            _volume.sharedProfile = profile;
            GameEvents.Hit += OnHit;
            GameEvents.NearMiss += OnNearMiss;
            GunBloodFX.EnsureInstance();
        }

        private void OnDestroy()
        {
            GameEvents.Hit -= OnHit;
            GameEvents.NearMiss -= OnNearMiss;
            if (_volume != null && _volume.sharedProfile != null) Destroy(_volume.sharedProfile);
        }

        private void OnHit(HitEvent e)
        {
            if (_player != null && e.victim == _player && e.damage > 0f) _hit = Mathf.Min(1f, _hit + e.damage / 35f);
        }

        private void OnNearMiss(NearMissEvent e)
        {
            if (_player != null && e.listener == _player) _suppression = Mathf.Min(1f, _suppression + 0.35f);
        }

        private void Update()
        {
            if (_player == null) _player = Combatant.FindLocalPlayer();
            float dt = Time.unscaledDeltaTime;
            _hit = Mathf.MoveTowards(_hit, 0f, dt * 2.2f);
            _suppression = Mathf.MoveTowards(_suppression, 0f, dt * 1.2f);
            float health = _player != null && _player.Health != null ? _player.Health.Normalized : 1f;
            bool alive = _player == null || _player.IsAlive;
            float low = alive ? Mathf.Clamp01((0.5f - health) / 0.5f) : 1f;
            if (LR2.World.KillcamDirector.IsPlaying)
            {
                // B14.1: the replay is the killer's healthy view; the dead victim's red/grey low-health grade must not leak in
                low = 0f;
                _hit = 0f;
                _suppression = 0f;
            }

            _vignette.active = false;
            _aberration.active = false;
            _color.active = false;

            float vigPulse = low > 0.25f && alive ? (0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * 5.2f)) : 1f;
            float vig = Mathf.Clamp01(_hit * hitVignette + low * 0.32f * vigPulse + _suppression * suppressionVignette);
            float ab = Mathf.Clamp01(_hit * hitAberration + _suppression * 0.25f + low * 0.08f);
            float sat = Mathf.Lerp(0f, lowHealthDesaturation, low);
            float contrast = Mathf.Lerp(0f, 12f, Mathf.Clamp01(_hit + low * 0.5f));

            _vignette.intensity.value = vig;
            _vignette.active = vig > 0.001f;
            _aberration.intensity.value = ab;
            _aberration.active = ab > 0.001f;
            _color.saturation.value = sat;
            _color.contrast.value = contrast;
            _color.active = Mathf.Abs(sat) > 0.01f || contrast > 0.05f;

            if (_motionBlur != null)
            {
                _motionBlur.active = LR2Settings.MotionBlur;
                _motionBlur.intensity.value = LR2Settings.MotionBlur ? 0.25f : 0f;
            }
            if (_filmGrain != null)
            {
                _filmGrain.active = LR2Settings.FilmGrain;
                _filmGrain.intensity.value = LR2Settings.FilmGrain ? 0.18f : 0f;
            }
        }
    }
}
