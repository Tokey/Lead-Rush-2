using LR2.Audio;
using UnityEngine;
using UnityEngine.EventSystems;

namespace LR2.UI
{
    /// <summary>Subtle hover/click feedback (soft damped ticks instead of harsh metal clicks) + scale &amp; accent bar.</summary>
    public class UIButtonFX : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public RectTransform accentBar;
        public float hoverScale = 1.03f;
        private bool _hover;

        private static AudioClip _softHover, _softClick;
        private static float _lastHoverTime = -10f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _softHover = null;
            _softClick = null;
            _lastHoverTime = -10f;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _hover = true;
            if (Time.unscaledTime - _lastHoverTime < 0.065f) return;
            _lastHoverTime = Time.unscaledTime;
            EnsureClips();
            LR2Audio.PlayClip(_softHover, Vector3.zero, 0.065f, Random.Range(0.97f, 1.04f), 1f, 500f, 0f, AudioCategory.UI, 210);
        }

        public void OnPointerExit(PointerEventData eventData) => _hover = false;

        public void OnPointerClick(PointerEventData eventData)
        {
            EnsureClips();
            LR2Audio.PlayClip(_softClick, Vector3.zero, 0.16f, Random.Range(0.96f, 1.03f), 1f, 500f, 0f, AudioCategory.UI, 200);
        }

        private void Update()
        {
            float target = _hover ? hoverScale : 1f;
            transform.localScale = Vector3.one * Mathf.Lerp(transform.localScale.x, target, Time.unscaledDeltaTime * 14f);
            if (accentBar != null)
                accentBar.localScale = new Vector3(Mathf.Lerp(accentBar.localScale.x, _hover ? 1f : 0f, Time.unscaledDeltaTime * 14f), 1f, 1f);
        }

        private static void EnsureClips()
        {
            if (_softHover != null && _softClick != null) return;
            const int sr = 44100;
            _softHover = SynthSoft("LR2_UI_SoftHover", 0.022f, sr, t =>
            {
                float attack = Mathf.Clamp01(t / 0.002f);
                float env = Mathf.Exp(-t * 220f) * attack;
                return Mathf.Sin(2f * Mathf.PI * 540f * t) * env * 0.45f;
            });
            _softClick = SynthSoft("LR2_UI_SoftClick", 0.032f, sr, t =>
            {
                float attack = Mathf.Clamp01(t / 0.0015f);
                float env = Mathf.Exp(-t * 165f) * attack;
                float f = Mathf.Lerp(420f, 180f, Mathf.Clamp01(t * 70f));
                return (Mathf.Sin(2f * Mathf.PI * f * t) * 0.72f + Mathf.Sin(2f * Mathf.PI * 820f * t) * Mathf.Exp(-t * 280f) * 0.25f) * env;
            });
        }

        private static AudioClip SynthSoft(string name, float seconds, int sr, System.Func<float, float> fn)
        {
            int n = Mathf.RoundToInt(seconds * sr);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                float fadeOut = Mathf.Clamp01((n - i) / (sr * 0.005f));
                data[i] = Mathf.Clamp(fn(t) * fadeOut, -0.95f, 0.95f);
            }
            var clip = AudioClip.Create(name, n, 1, sr, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
