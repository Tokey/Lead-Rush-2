using LR2.Audio;
using LR2.Combat;
using LR2.Core;
using UnityEngine;
using UnityEngine.UI;

namespace LR2.UI
{
    /// <summary>
    /// Battlefield-style hit feedback for the local player, built entirely at runtime (no UI rebuild needed):
    /// - a thick, outlined X hitmarker that punches in on every hit: white = body, orange = headshot, red = kill;
    /// - a kill marker (red skull, gold rim for headshot kills) that pops at the crosshair, then shrinks and fades;
    /// - layered procedural sounds: crisp body "tk", metallic headshot "tink", kill = sub thump + two-tone chime.
    /// HUDController adds this automatically and hands its hitmarker/hit sounds over to it.
    /// </summary>
    public class HitFeedbackHUD : MonoBehaviour
    {
        [Header("Hitmarker")]
        public float armLength = 15f;
        public float armThickness = 4f;
        public float armGap = 9f;
        public float holdTime = 0.12f;
        public float fadeTime = 0.2f;
        public Color bodyColor = new Color(1f, 1f, 1f, 1f);
        public Color headColor = new Color(1f, 0.6f, 0.12f, 1f);
        public Color killColor = new Color(1f, 0.16f, 0.12f, 1f);

        [Header("Kill marker")]
        public float skullSize = 46f;
        public float skullHold = 0.55f;
        public float skullFade = 0.3f;

        [Header("Audio")]
        [Range(0f, 1.5f)] public float volume = 1f;

        private RectTransform _root, _xRoot, _skullRT;
        private readonly Image[] _arms = new Image[4];
        private Image _skull, _skullRim;
        private float _hitTime = -10f, _killTime = -10f;
        private Color _color;
        private float _scale = 1f, _thick = 1f;
        private int _streak; // rapid consecutive hits grow the marker a little
        private Combatant _player;

        private static AudioClip _bodyTick, _headTink, _killThump, _killChime, _headKillDing;
        private static Sprite _skullSprite, _skullRimSprite;

        public static HitFeedbackHUD Create(Transform parent)
        {
            var go = new GameObject("HitFeedback", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var hf = go.AddComponent<HitFeedbackHUD>();
            return hf;
        }

        private void Awake()
        {
            _root = (RectTransform)transform;
            _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0.5f);
            _root.pivot = new Vector2(0.5f, 0.5f);
            _root.anchoredPosition = Vector2.zero;
            _root.sizeDelta = new Vector2(200f, 200f);

            _xRoot = NewRect("X", _root, Vector2.zero);
            for (int i = 0; i < 4; i++)
            {
                var arm = NewRect("Arm" + i, _xRoot, Vector2.zero);
                var img = arm.gameObject.AddComponent<Image>();
                img.raycastTarget = false;
                var o = arm.gameObject.AddComponent<Outline>();
                o.effectColor = new Color(0f, 0f, 0f, 0.65f);
                o.effectDistance = new Vector2(1.4f, -1.4f);
                _arms[i] = img;
            }

            if (_skullSprite == null) BuildSkullSprites();
            _skullRT = NewRect("KillMarker", _root, new Vector2(0f, 0f));
            _skullRT.sizeDelta = new Vector2(skullSize, skullSize);
            _skullRim = NewRect("Rim", _skullRT, Vector2.zero).gameObject.AddComponent<Image>();
            _skullRim.rectTransform.sizeDelta = new Vector2(skullSize, skullSize) * 1.18f;
            _skullRim.sprite = _skullRimSprite;
            _skullRim.raycastTarget = false;
            _skull = NewRect("Skull", _skullRT, Vector2.zero).gameObject.AddComponent<Image>();
            _skull.rectTransform.sizeDelta = new Vector2(skullSize, skullSize);
            _skull.sprite = _skullSprite;
            _skull.raycastTarget = false;
            var so = _skull.gameObject.AddComponent<Outline>();
            so.effectColor = new Color(0f, 0f, 0f, 0.7f);
            so.effectDistance = new Vector2(1.5f, -1.5f);

            SetAlpha(0f, 0f);
        }

        private void OnEnable() => GameEvents.Hit += OnHit;
        private void OnDisable() => GameEvents.Hit -= OnHit;

        private static RectTransform NewRect(string name, Transform parent, Vector2 pos)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            return rt;
        }

        private void OnHit(HitEvent e)
        {
            if (_player == null) _player = Combatant.FindLocalPlayer();
            if (_player == null || e.attacker != _player || e.victim == _player || e.damage <= 0f) return;

            bool head = e.zone == HitZone.Head;
            float now = Time.unscaledTime;
            _streak = now - _hitTime < 0.25f ? Mathf.Min(_streak + 1, 6) : 0;
            _hitTime = now;
            _color = e.killed ? killColor : head ? headColor : bodyColor;
            _scale = e.killed ? 1.45f : head ? 1.18f : 1f + _streak * 0.03f;
            _thick = e.killed ? 1.5f : head ? 1.2f : 1f;
            if (e.killed)
            {
                _killTime = now;
                _skullRim.color = head ? new Color(1f, 0.72f, 0.2f, 1f) : new Color(0f, 0f, 0f, 0f);
            }

            if (LR2Settings.HitmarkerSound) PlaySounds(head, e.killed);
        }

        private void Update()
        {
            float now = Time.unscaledTime;

            // X hitmarker: punch in (fast overshoot), hold, fade
            float t = now - _hitTime;
            float a = t < holdTime ? 1f : 1f - Mathf.Clamp01((t - holdTime) / fadeTime);
            float punch = 1f + 0.55f * Mathf.Exp(-t * 28f);           // starts big, snaps to size
            float gap = armGap * _scale * (1f + 0.35f * Mathf.Exp(-t * 20f));
            float len = armLength * _scale * punch;
            float thick = armThickness * _thick;
            for (int i = 0; i < 4; i++)
            {
                float ang = 45f + 90f * i;
                Vector2 dir = new Vector2(Mathf.Cos(ang * Mathf.Deg2Rad), Mathf.Sin(ang * Mathf.Deg2Rad));
                var rt = _arms[i].rectTransform;
                rt.sizeDelta = new Vector2(len, thick);
                rt.anchoredPosition = dir * (gap + len * 0.5f);
                rt.localRotation = Quaternion.Euler(0f, 0f, ang);
                var c = _color;
                c.a = a;
                _arms[i].color = c;
            }

            // kill marker: pop with overshoot, hold, shrink + fade
            float k = now - _killTime;
            float ka = k < skullHold ? 1f : 1f - Mathf.Clamp01((k - skullHold) / skullFade);
            float pop = k < 0.12f ? Mathf.Lerp(1.9f, 0.92f, k / 0.12f) : k < 0.2f ? Mathf.Lerp(0.92f, 1f, (k - 0.12f) / 0.08f) : 1f;
            if (k > skullHold) pop *= Mathf.Lerp(1f, 0.75f, Mathf.Clamp01((k - skullHold) / skullFade));
            _skullRT.localScale = Vector3.one * pop;
            _skullRT.anchoredPosition = new Vector2(0f, 46f + 6f * Mathf.Clamp01(k / (skullHold + skullFade)));
            var sc = killColor;
            sc.a = ka;
            _skull.color = sc;
            var rc = _skullRim.color;
            rc.a = Mathf.Min(rc.a > 0f ? 1f : 0f, ka);
            _skullRim.color = rc;
        }

        private void SetAlpha(float x, float skull)
        {
            for (int i = 0; i < 4; i++) if (_arms[i] != null) _arms[i].color = new Color(1f, 1f, 1f, x);
            if (_skull != null) _skull.color = new Color(1f, 1f, 1f, skull);
            if (_skullRim != null) _skullRim.color = new Color(1f, 1f, 1f, 0f);
        }

        // ------------------------------------------------------------------ audio
        private void PlaySounds(bool head, bool killed)
        {
            EnsureClips();
            float v = volume;
            if (killed)
            {
                Play(_killThump, 1.0f * v, 1f, 0f);
                Play(head ? _headTink : _bodyTick, 0.8f * v, 1f, 0f);
                Play(_killChime, 0.75f * v, 1f, 0.035f);
                if (head) Play(_headKillDing, 0.6f * v, 1f, 0.07f);
            }
            else if (head)
            {
                Play(_headTink, 0.95f * v, Random.Range(0.98f, 1.03f), 0f);
                Play(_bodyTick, 0.55f * v, 0.9f, 0f);
            }
            else
            {
                Play(_bodyTick, 0.9f * v, Random.Range(0.95f, 1.06f) + _streak * 0.015f, 0f);
            }
        }

        private static void Play(AudioClip clip, float vol, float pitch, float delay)
        {
            LR2Audio.PlayClip(clip, Vector3.zero, vol, pitch, 1f, 500f, 0f, AudioCategory.UI, 0, delay);
        }

        private static void EnsureClips()
        {
            if (_bodyTick != null) return;
            const int sr = 44100;
            var rng = new System.Random(1337);

            // body hit: very short noise click + 1.9 kHz blip + small low knock
            _bodyTick = Synth("LR2_HF_BodyTick", 0.07f, sr, t =>
            {
                float n = (float)(rng.NextDouble() * 2.0 - 1.0);
                return n * Mathf.Exp(-t * 420f) * 0.55f
                     + Mathf.Sin(2f * Mathf.PI * 1900f * t) * Mathf.Exp(-t * 90f) * 0.45f
                     + Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(180f, 70f, t * 14f) * t) * Mathf.Exp(-t * 55f) * 0.5f;
            });

            // headshot: metallic inharmonic "tink" (helmet ping)
            _headTink = Synth("LR2_HF_HeadTink", 0.26f, sr, t =>
            {
                float n = (float)(rng.NextDouble() * 2.0 - 1.0);
                return n * Mathf.Exp(-t * 500f) * 0.4f
                     + Mathf.Sin(2f * Mathf.PI * 2950f * t) * Mathf.Exp(-t * 16f) * 0.38f
                     + Mathf.Sin(2f * Mathf.PI * 4730f * t) * Mathf.Exp(-t * 24f) * 0.26f
                     + Mathf.Sin(2f * Mathf.PI * 7150f * t) * Mathf.Exp(-t * 38f) * 0.16f
                     + Mathf.Sin(2f * Mathf.PI * 1210f * t) * Mathf.Exp(-t * 30f) * 0.2f;
            });

            // kill: deep punchy thump (pitch-dropping sine + click), felt more than heard
            _killThump = Synth("LR2_HF_KillThump", 0.22f, sr, t =>
            {
                float f = Mathf.Lerp(150f, 46f, Mathf.Clamp01(t * 9f));
                float body = Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-t * 17f);
                float click = (float)(rng.NextDouble() * 2.0 - 1.0) * Mathf.Exp(-t * 300f) * 0.5f;
                return Mathf.Clamp(body * 1.25f, -0.9f, 0.9f) + click;
            });

            // kill confirm chime: two quick rising bell tones
            _killChime = Synth("LR2_HF_KillChime", 0.5f, sr, t =>
            {
                float s = Bell(t, 1318.5f, 7f) * 0.42f;                       // E6
                float t2 = t - 0.07f;
                if (t2 > 0f) s += Bell(t2, 1975.5f, 6f) * 0.38f;             // B6
                return s;
            });

            // extra sparkle for headshot kills
            _headKillDing = Synth("LR2_HF_HeadKillDing", 0.45f, sr, t => Bell(t, 2637f, 8f) * 0.35f); // E7
        }

        private static float Bell(float t, float f, float decay)
        {
            float env = Mathf.Exp(-t * decay) * Mathf.Clamp01(t * 600f);
            return (Mathf.Sin(2f * Mathf.PI * f * t) + 0.35f * Mathf.Sin(2f * Mathf.PI * f * 2.76f * t) * Mathf.Exp(-t * 6f)) * env;
        }

        private static AudioClip Synth(string name, float seconds, int sr, System.Func<float, float> fn)
        {
            int n = Mathf.RoundToInt(seconds * sr);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                float fadeOut = Mathf.Clamp01((n - i) / (sr * 0.01f)); // 10 ms tail fade, no clicks
                data[i] = Mathf.Clamp(fn(t) * fadeOut, -0.98f, 0.98f);
            }
            var clip = AudioClip.Create(name, n, 1, sr, false);
            clip.SetData(data, 0);
            return clip;
        }

        // ------------------------------------------------------------------ skull sprite (procedural, white = tintable)
        private static void BuildSkullSprites()
        {
            const int S = 128;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "LR2_KillSkull" };
            var rim = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "LR2_KillSkullRim" };
            var px = new Color32[S * S];
            var rpx = new Color32[S * S];
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                // centred coords, y up, unit = half size
                float u = (x + 0.5f) / S * 2f - 1f;
                float v = (y + 0.5f) / S * 2f - 1f;
                float d = SkullSdf(u, v);
                float aa = 2f / S;
                byte a = (byte)(Mathf.Clamp01(0.5f - d / aa) * 255f);
                px[y * S + x] = new Color32(255, 255, 255, a);
                // rim = ring around the skull silhouette
                float ring = Mathf.Abs(Mathf.Sqrt(u * u + v * v) - 0.93f) - 0.045f;
                byte ra = (byte)(Mathf.Clamp01(0.5f - ring / aa) * 255f);
                rpx[y * S + x] = new Color32(255, 255, 255, ra);
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            rim.SetPixels32(rpx);
            rim.Apply(false, true);
            _skullSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
            _skullRimSprite = Sprite.Create(rim, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>Signed distance (negative inside) of a stylised skull: cranium + jaw minus eyes, nose and tooth gaps.</summary>
        private static float SkullSdf(float u, float v)
        {
            float cranium = Ellipse(u, v - 0.18f, 0.62f, 0.56f);
            float jaw = Box(u, v + 0.42f, 0.34f, 0.2f, 0.12f);
            float cheeks = Box(u, v + 0.12f, 0.48f, 0.2f, 0.18f);
            float shape = Mathf.Min(cranium, Mathf.Min(jaw, cheeks));

            float eyeL = Ellipse(u + 0.24f, v - 0.08f, 0.17f, 0.15f);
            float eyeR = Ellipse(u - 0.24f, v - 0.08f, 0.17f, 0.15f);
            float nose = Triangle(u, v + 0.17f, 0.075f, 0.1f);
            float holes = Mathf.Min(Mathf.Min(eyeL, eyeR), nose);

            // tooth gaps: thin vertical slots + a horizontal bite line on the jaw
            float teeth = 1f;
            for (int i = -2; i <= 2; i++)
                teeth = Mathf.Min(teeth, Box(u - i * 0.13f, v + 0.45f, 0.018f, 0.13f, 0f));
            float bite = Box(u, v + 0.4f, 0.3f, 0.016f, 0f);
            holes = Mathf.Min(holes, Mathf.Min(teeth, bite));

            return Mathf.Max(shape, -holes);
        }

        private static float Ellipse(float x, float y, float rx, float ry)
        {
            float k = Mathf.Sqrt((x * x) / (rx * rx) + (y * y) / (ry * ry));
            return (k - 1f) * Mathf.Min(rx, ry);
        }

        private static float Box(float x, float y, float hx, float hy, float r)
        {
            float qx = Mathf.Abs(x) - hx + r, qy = Mathf.Abs(y) - hy + r;
            float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }

        /// <summary>Nasal cavity: triangle with the point at the top, half-width w at the bottom, half-height h.</summary>
        private static float Triangle(float x, float y, float w, float h)
        {
            float k = (h - y) / (2f * h); // 0 at the top, 1 at the bottom
            float edge = Mathf.Abs(x) - w * Mathf.Clamp01(k);
            return Mathf.Max(edge, Mathf.Max(-h - y, y - h));
        }
    }
}
