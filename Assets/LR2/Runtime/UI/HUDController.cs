using System.Collections.Generic;
using LR2.Audio;
using LR2.Characters;
using LR2.Combat;
using LR2.Core;
using LR2.Game;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LR2.UI
{
    /// <summary>
    /// The in-game HUD. Auto-binds to the local player and MatchManager (works without one, too). All element
    /// references are assigned by the LR2 UI builder and remain editable in the Inspector.
    /// </summary>
    public class HUDController : MonoBehaviour
    {
        [Header("Root")]
        public CanvasGroup hudGroup;

        [Header("Crosshair")]
        public RectTransform[] crosshairLines = new RectTransform[4]; // up, down, left, right
        public Image crosshairDot;
        public float crosshairMinGap = 7f;
        public CanvasGroup crosshairGroup;

        [Header("Hitmarker")]
        public Image hitmarker;
        public float hitmarkerDuration = 0.28f;

        [Header("Ammo")]
        public TMP_Text weaponName;
        public TMP_Text magText;
        public TMP_Text reserveText;
        public TMP_Text fireModeText;
        public TMP_Text reloadPrompt;
        public RectTransform reloadBarRoot;
        public Image reloadBarFill;

        [Header("Health")]
        public Image healthFill;
        public TMP_Text healthText;
        public Image damageVignette;
        public Image lowHealthOverlay;

        [Header("Damage direction")]
        public RectTransform damageIndicatorRoot;
        public Image damageIndicatorTemplate;

        [Header("Kill feed")]
        public RectTransform killFeedRoot;
        public TMP_Text killFeedTemplate;
        public int killFeedMax = 5;
        public float killFeedLifetime = 5.5f;

        [Header("Score popups / medals")]
        public RectTransform popupRoot;
        public TMP_Text popupTemplate;
        public TMP_Text medalText;
        public CanvasGroup medalGroup;

        [Header("Match status")]
        public TMP_Text modeText;
        public TMP_Text timerText;
        public TMP_Text myScoreText;
        public TMP_Text enemyScoreText;
        public Image myScoreBar;
        public Image enemyScoreBar;
        public TMP_Text limitText;

        [Header("Center notifications")]
        public TMP_Text centerText;
        public TMP_Text subText;

        [Header("Minimap")]
        public RectTransform minimapRotator;
        public RawImage minimapImage;
        public RectTransform minimapIconRoot;
        public Image minimapIconTemplate;
        public float minimapRadius = 110f;
        public float minimapRange = 32f;

        [Header("Compass")]
        public RectTransform compassRoot;
        public TMP_Text compassTemplate;
        public float compassWidth = 640f;
        public float compassFov = 160f;

        [Header("Misc")]
        public TMP_Text fpsText;
        public TMP_Text bodycamOverlay;
        public string unitId = "";

        private UITheme _theme;
        private SoldierController _soldier;
        private Combatant _player;
        private float _hitmarkerTime = -10f;
        private Color _hitmarkerColor = Color.white;
        private float _hitmarkerScale = 1f;
        private float _damageFlash;
        private float _nextHeartbeat;
        private float _playingStartTime = -10f;
        private float _fpsTimer;
        private int _fpsFrames;

        private readonly List<(TMP_Text text, float time)> _feed = new List<(TMP_Text, float)>();
        private readonly List<(TMP_Text text, float time)> _popups = new List<(TMP_Text, float)>();
        private readonly Queue<string> _medalQueue = new Queue<string>();
        private float _medalShownAt = -10f;
        private readonly List<(Image img, Vector3 source, float time)> _damageIndicators = new List<(Image, Vector3, float)>();
        private readonly List<Image> _iconPool = new List<Image>();
        private readonly List<(TMP_Text label, float angle)> _compassLabels = new List<(TMP_Text, float)>();

        private void Awake()
        {
            _theme = Resources.Load<UITheme>("LR2/UITheme");
            if (killFeedTemplate != null) killFeedTemplate.gameObject.SetActive(false);
            if (popupTemplate != null) popupTemplate.gameObject.SetActive(false);
            if (damageIndicatorTemplate != null) damageIndicatorTemplate.gameObject.SetActive(false);
            if (minimapIconTemplate != null) minimapIconTemplate.gameObject.SetActive(false);
            if (hitmarker != null) hitmarker.color = new Color(1f, 1f, 1f, 0f);
            if (medalGroup != null) medalGroup.alpha = 0f;
            BuildCompass();
            // BF6-style hit feedback (thick X, kill skull, layered hit/kill sounds) replaces the old small hitmarker
            _hitFeedback = GetComponentInChildren<HitFeedbackHUD>(true);
            if (_hitFeedback == null)
            {
                Transform parent = hitmarker != null ? hitmarker.rectTransform.parent : transform;
                _hitFeedback = HitFeedbackHUD.Create(parent);
                if (hitmarker != null) _hitFeedback.transform.SetSiblingIndex(hitmarker.transform.GetSiblingIndex() + 1);
            }
            if (hitmarker != null) hitmarker.enabled = false;
        }

        private HitFeedbackHUD _hitFeedback;

        private void OnEnable()
        {
            GameEvents.Hit += OnHit;
            GameEvents.Killed += OnKilled;
            GameEvents.ScoreAwarded += OnScore;
            GameEvents.MedalAwarded += OnMedal;
            GameEvents.MatchStateChanged += OnMatchState;
        }

        private void OnDisable()
        {
            GameEvents.Hit -= OnHit;
            GameEvents.Killed -= OnKilled;
            GameEvents.ScoreAwarded -= OnScore;
            GameEvents.MedalAwarded -= OnMedal;
            GameEvents.MatchStateChanged -= OnMatchState;
        }

        private Color Friendly => _theme != null ? _theme.friendly : TeamUtil.FriendlyColor;
        private Color Enemy => _theme != null ? _theme.enemy : TeamUtil.EnemyColor;
        private Color Accent => _theme != null ? _theme.accent : new Color(1f, 0.71f, 0.28f);

        private void Bind()
        {
            if (_player != null && _soldier != null) return;
            _player = Combatant.FindLocalPlayer();
            _soldier = _player != null ? _player.GetComponent<SoldierController>() : null;
        }

        private void Update()
        {
            Bind();
            float dt = Time.unscaledDeltaTime;
            UpdateCrosshair();
            UpdateHitmarker();
            UpdateAmmo();
            UpdateHealth(dt);
            UpdateDamageIndicators();
            UpdateFeed();
            UpdatePopups();
            UpdateMatchStatus(dt);
            UpdateCenter();
            UpdateMinimap();
            UpdateCompass();
            UpdateMisc(dt);
        }

        // ------------------------------------------------------------------ crosshair / hitmarker
        private void UpdateCrosshair()
        {
            if (crosshairGroup == null) return;
            var gun = _soldier != null ? _soldier.ActiveFirearm : null;
            if (_soldier == null || gun == null || _soldier.IsDead)
            {
                crosshairGroup.alpha = 0f;
                return;
            }
            float fov = _soldier.ViewCamera != null ? _soldier.ViewCamera.fieldOfView : 90f;
            float canvasHalfHeight = 540f;
            float spreadPx = Mathf.Tan(gun.CurrentSpread * Mathf.Deg2Rad) / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * canvasHalfHeight;
            float gap = crosshairMinGap + spreadPx;
            Vector2[] dirs = { Vector2.up, Vector2.down, Vector2.left, Vector2.right };
            for (int i = 0; i < crosshairLines.Length && i < 4; i++)
                if (crosshairLines[i] != null) crosshairLines[i].anchoredPosition = dirs[i] * (gap + 6f);

            float target = 1f - _soldier.AimBlend;
            if (_soldier.IsSprinting || _soldier.IsReloading) target *= 0.25f;
            crosshairGroup.alpha = Mathf.MoveTowards(crosshairGroup.alpha, target, Time.unscaledDeltaTime * 8f);
        }

        private void UpdateHitmarker()
        {
            if (hitmarker == null || _hitFeedback != null) return;
            float t = (Time.unscaledTime - _hitmarkerTime) / hitmarkerDuration;
            float a = t < 1f ? 1f - t * t : 0f;
            var c = _hitmarkerColor;
            c.a = a;
            hitmarker.color = c;
            hitmarker.rectTransform.localScale = Vector3.one * Mathf.Lerp(_hitmarkerScale * 1.15f, _hitmarkerScale, Mathf.Clamp01(t * 3f));
        }

        private void OnHit(HitEvent e)
        {
            if (_player == null) return;
            if (_hitFeedback == null && e.attacker == _player && e.victim != _player && e.damage > 0f)
            {
                _hitmarkerTime = Time.unscaledTime;
                bool head = e.zone == HitZone.Head;
                _hitmarkerColor = e.killed ? Enemy : head ? new Color(1f, 0.85f, 0.4f) : Color.white;
                _hitmarkerScale = e.killed ? 1.35f : head ? 1.15f : 1f;
                var s = LR2CommonSounds.Instance;
                if (LR2Settings.HitmarkerSound)
                {
                    if (s != null)
                    {
                        var mainSet = e.killed && s.killConfirm.IsValid ? s.killConfirm
                            : (head && s.hitmarkerHeadshot.IsValid ? s.hitmarkerHeadshot : s.hitmarker);
                        LR2Audio.Play2D(mainSet, e.killed ? 1.4f : 1.25f);
                        if (e.killed && s.hitmarker.IsValid) LR2Audio.Play2D(s.hitmarker, 1.15f);
                    }
                    PlayPunchHitLayer(head, e.killed);
                }
            }
            if (e.victim == _player && e.damage > 0f)
            {
                _damageFlash = Mathf.Min(1f, _damageFlash + e.damage / 45f);
                if (e.attacker != null && e.attacker != _player) SpawnDamageIndicator(e.attacker.Position);
                else if (e.direction.sqrMagnitude > 0.01f) SpawnDamageIndicator(_player.Position - e.direction * 10f);
            }
        }

        private static AudioClip _punchClip, _headPunchClip;

        private static void PlayPunchHitLayer(bool headshot, bool killed)
        {
            if (_punchClip == null) _punchClip = CreateHitPunchClip(false);
            if (_headPunchClip == null) _headPunchClip = CreateHitPunchClip(true);
            float vol = killed ? 1.0f : headshot ? 0.95f : 0.85f;
            float pitch = killed ? 0.95f : headshot ? 1.08f : Random.Range(0.96f, 1.05f);
            LR2Audio.PlayClip(headshot ? _headPunchClip : _punchClip, Vector3.zero, vol, pitch, 1f, 500f, 0f, AudioCategory.UI, 0);
        }

        private static AudioClip CreateHitPunchClip(bool headshot)
        {
            const int sr = 44100;
            int samples = Mathf.RoundToInt(sr * (headshot ? 0.095f : 0.065f));
            var data = new float[samples];
            float phaseLow = 0f, phaseHi = 0f;
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)sr;
                float envLow = Mathf.Exp(-t * 48f);
                float envHi = Mathf.Exp(-t * 140f);
                float freqLow = Mathf.Lerp(headshot ? 210f : 155f, 52f, Mathf.Clamp01(t * 28f));
                float freqHi = headshot ? 3600f : 2650f;
                phaseLow += 2f * Mathf.PI * freqLow / sr;
                phaseHi += 2f * Mathf.PI * freqHi / sr;
                float transient = i < 90 ? (1f - i / 90f) * 0.45f : 0f;
                float sample = Mathf.Sin(phaseLow) * envLow * 0.72f + Mathf.Sin(phaseHi) * envHi * (headshot ? 0.52f : 0.36f) + transient;
                data[i] = Mathf.Clamp(sample, -0.95f, 0.95f);
            }
            var clip = AudioClip.Create(headshot ? "LR2_HeadHitPunch" : "LR2_HitPunch", samples, 1, sr, false);
            clip.SetData(data, 0);
            return clip;
        }

        // ------------------------------------------------------------------ ammo / health
        private void UpdateAmmo()
        {
            var gun = _soldier != null ? _soldier.ActiveFirearm : null;
            bool show = gun != null && !_soldier.IsDead;
            if (magText != null) magText.enabled = show;
            if (reserveText != null) reserveText.enabled = show;
            if (weaponName != null) weaponName.enabled = show;
            if (fireModeText != null) fireModeText.enabled = show;
            if (!show)
            {
                if (reloadPrompt != null) reloadPrompt.text = "";
                if (reloadBarRoot != null) reloadBarRoot.gameObject.SetActive(false);
                return;
            }

            float ratio = (float)gun.AmmoInMagazine / Mathf.Max(1, gun.MagazineSize);
            if (weaponName != null) weaponName.text = gun.DisplayName.ToUpperInvariant();
            if (magText != null)
            {
                magText.text = gun.AmmoInMagazine.ToString();
                magText.color = gun.AmmoInMagazine == 0 ? Enemy : ratio <= 0.3f ? Accent : Color.white;
            }
            if (reserveText != null) reserveText.text = gun.ReserveAmmo.ToString();
            if (fireModeText != null) fireModeText.text = gun.FireMode.ToString().ToUpperInvariant();
            if (reloadBarRoot != null) reloadBarRoot.gameObject.SetActive(gun.IsReloading);
            if (reloadBarFill != null && gun.IsReloading) reloadBarFill.fillAmount = gun.ReloadProgress;
            if (reloadPrompt != null)
            {
                string prompt = "";
                if (gun.IsReloading) prompt = "RELOADING";
                else if (gun.AmmoInMagazine == 0 && gun.ReserveAmmo == 0) prompt = "NO AMMO";
                else if (ratio <= 0.3f && gun.ReserveAmmo > 0) prompt = "RELOAD [R]";
                reloadPrompt.text = prompt;
                reloadPrompt.color = gun.IsReloading ? Accent : gun.AmmoInMagazine == 0 ? Enemy : Accent;
            }
        }

        private void UpdateHealth(float dt)
        {
            var health = _player != null ? _player.Health : null;
            float n = health != null ? health.Normalized : 1f;
            bool alive = health != null && health.IsAlive;
            if (healthFill != null)
            {
                healthFill.fillAmount = Mathf.MoveTowards(healthFill.fillAmount, n, dt * 2.5f);
                healthFill.color = n < 0.35f ? Enemy : Color.Lerp(Accent, Color.white, n);
            }
            if (healthText != null)
            {
                healthText.text = Mathf.CeilToInt(n * 100f).ToString();
                healthText.color = alive && n < 0.35f
                    ? Color.Lerp(Enemy, Color.white, 0.25f + 0.25f * Mathf.Sin(Time.unscaledTime * 7f))
                    : Color.white;
            }

            _damageFlash = Mathf.MoveTowards(_damageFlash, 0f, dt * 1.6f);
            float low = alive ? Mathf.Clamp01((0.55f - n) / 0.55f) : 0f;
            if (damageVignette != null) damageVignette.color = new Color(0.55f, 0f, 0f, Mathf.Clamp01(_damageFlash * 0.75f + low * 0.55f));
            if (lowHealthOverlay != null)
            {
                float pulse = low > 0.4f ? (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6.5f)) * low : 0f;
                lowHealthOverlay.color = new Color(0.4f, 0f, 0f, pulse * 0.35f);
            }
            if (alive && low > 0.45f && Time.unscaledTime >= _nextHeartbeat && !AudioListener.pause)
            {
                _nextHeartbeat = Time.unscaledTime + Mathf.Lerp(1.25f, 0.75f, low);
                var s = LR2CommonSounds.Instance;
                if (s != null) LR2Audio.Play2D(s.lowHealthLoop, 0.5f + low * 0.5f);
            }
        }

        // ------------------------------------------------------------------ damage direction
        private void SpawnDamageIndicator(Vector3 source)
        {
            if (damageIndicatorTemplate == null || damageIndicatorRoot == null) return;
            Image img = null;
            foreach (var d in _damageIndicators)
                if (Time.unscaledTime - d.time > 2f) { img = d.img; break; }
            if (img == null)
            {
                img = Instantiate(damageIndicatorTemplate, damageIndicatorRoot);
                img.gameObject.SetActive(true);
            }
            _damageIndicators.RemoveAll(d => d.img == img);
            _damageIndicators.Add((img, source, Time.unscaledTime));
        }

        private void UpdateDamageIndicators()
        {
            if (_damageIndicators.Count == 0 || _soldier == null) return;
            Vector3 fwd = _soldier.AimDirection;
            fwd.y = 0f;
            foreach (var d in _damageIndicators)
            {
                float age = Time.unscaledTime - d.time;
                Vector3 to = d.source - _soldier.transform.position;
                to.y = 0f;
                float angle = Vector3.SignedAngle(fwd, to, Vector3.up);
                d.img.rectTransform.localEulerAngles = new Vector3(0f, 0f, -angle);
                d.img.color = new Color(0.9f, 0.08f, 0.05f, Mathf.Clamp01(1f - age / 2f) * 0.85f);
            }
        }

        // ------------------------------------------------------------------ kill feed / popups / medals
        private string ColoredName(Combatant c)
        {
            if (c == null) return "";
            Color col = c == _player ? Accent : (_player != null && !TeamUtil.AreHostile(_player, c)) ? Friendly : Enemy;
            return $"<color=#{ColorUtility.ToHtmlStringRGB(col)}>{c.DisplayName}</color>";
        }

        private void OnKilled(KillEvent e)
        {
            if (killFeedTemplate == null || killFeedRoot == null || e.victim == null) return;
            string weapon = string.IsNullOrEmpty(e.weaponName) ? "" : e.weaponName.ToUpperInvariant();
            string line = e.killer != null && e.killer != e.victim
                ? $"{ColoredName(e.killer)}  <color=#C8C8C8><size=80%>[{weapon}]</size></color>{(e.headshot ? " <color=#FFD27A><size=75%>HEADSHOT</size></color>" : "")}  {ColoredName(e.victim)}"
                : $"{ColoredName(e.victim)}  <color=#C8C8C8><size=80%>[{weapon}]</size></color>";
            var t = Instantiate(killFeedTemplate, killFeedRoot);
            t.gameObject.SetActive(true);
            t.text = line;
            t.transform.SetAsFirstSibling();
            _feed.Insert(0, (t, Time.unscaledTime));
            while (_feed.Count > killFeedMax)
            {
                Destroy(_feed[_feed.Count - 1].text.gameObject);
                _feed.RemoveAt(_feed.Count - 1);
            }
        }

        private void UpdateFeed()
        {
            for (int i = _feed.Count - 1; i >= 0; i--)
            {
                float age = Time.unscaledTime - _feed[i].time;
                if (age > killFeedLifetime)
                {
                    Destroy(_feed[i].text.gameObject);
                    _feed.RemoveAt(i);
                    continue;
                }
                _feed[i].text.alpha = Mathf.Clamp01((killFeedLifetime - age) / 0.6f);
            }
        }

        private void OnScore(ScoreEvent e)
        {
            if (e.combatant != _player || _player == null || popupTemplate == null) return;
            AddPopup($"<color=#{ColorUtility.ToHtmlStringRGB(Accent)}>+{e.points}</color>  {e.reason}");
        }

        private void OnMedal(MedalEvent e)
        {
            if (e.combatant != _player || _player == null) return;
            _medalQueue.Enqueue(e.title);
            if (e.points > 0) AddPopup($"<color=#{ColorUtility.ToHtmlStringRGB(Accent)}>+{e.points}</color>  {e.title}");
        }

        private void AddPopup(string text)
        {
            if (popupTemplate == null || popupRoot == null) return;
            var t = Instantiate(popupTemplate, popupRoot);
            t.gameObject.SetActive(true);
            t.text = text;
            _popups.Add((t, Time.unscaledTime));
        }

        private void UpdatePopups()
        {
            for (int i = _popups.Count - 1; i >= 0; i--)
            {
                float age = Time.unscaledTime - _popups[i].time;
                if (age > 2.2f)
                {
                    Destroy(_popups[i].text.gameObject);
                    _popups.RemoveAt(i);
                    continue;
                }
                var t = _popups[i].text;
                t.alpha = age < 0.12f ? age / 0.12f : Mathf.Clamp01((2.2f - age) / 0.5f);
                int stackIndex = _popups.Count - 1 - i;
                t.rectTransform.anchoredPosition = new Vector2(0f, stackIndex * -30f + age * 6f);
            }

            if (medalText == null || medalGroup == null) return;
            float since = Time.unscaledTime - _medalShownAt;
            if (since > 1.9f && _medalQueue.Count > 0)
            {
                medalText.text = _medalQueue.Dequeue();
                _medalShownAt = Time.unscaledTime;
                since = 0f;
            }
            medalGroup.alpha = since < 0.15f ? since / 0.15f : Mathf.Clamp01((1.8f - since) / 0.4f);
            medalGroup.transform.localScale = Vector3.one * (since < 0.2f ? Mathf.Lerp(1.35f, 1f, since / 0.2f) : 1f);
        }

        // ------------------------------------------------------------------ match status / center text
        private void OnMatchState(MatchStateEvent e)
        {
            if (e.current == MatchState.Playing) _playingStartTime = Time.unscaledTime;
        }

        private void UpdateMatchStatus(float dt)
        {
            var mm = MatchManager.Instance;
            bool has = mm != null && mm.Settings != null;
            if (modeText != null) modeText.text = has ? (mm.IsSuddenDeath ? "SUDDEN DEATH" : mm.Settings.displayName.ToUpperInvariant()) : "TRAINING";
            if (!has) return;
            float tr = Mathf.Max(0f, mm.State == MatchState.Countdown ? mm.Settings.timeLimitSeconds : mm.TimeRemaining);
            if (timerText != null)
            {
                timerText.text = $"{Mathf.FloorToInt(tr / 60f)}:{Mathf.FloorToInt(tr % 60f):00}";
                timerText.color = tr < 30f && mm.State == MatchState.Playing ? Enemy : Color.white;
            }
            int mine = mm.GetDisplayScore(_player);
            int theirs = mm.GetBestOpponentScore(_player);
            int limit = Mathf.Max(1, mm.ScoreLimit);
            if (myScoreText != null) myScoreText.text = mine.ToString();
            if (enemyScoreText != null) enemyScoreText.text = theirs.ToString();
            if (myScoreBar != null) myScoreBar.fillAmount = Mathf.MoveTowards(myScoreBar.fillAmount, (float)mine / limit, dt * 3.5f);
            if (enemyScoreBar != null) enemyScoreBar.fillAmount = Mathf.MoveTowards(enemyScoreBar.fillAmount, (float)theirs / limit, dt * 3.5f);
            if (limitText != null) limitText.text = limit.ToString();
        }

        private void UpdateCenter()
        {
            if (centerText == null) return;
            var mm = MatchManager.Instance;
            string main = "", sub = "";
            if (mm != null && mm.State == MatchState.Countdown)
            {
                main = Mathf.CeilToInt(mm.CountdownRemaining).ToString();
                sub = "MATCH STARTS IN";
            }
            else if (mm != null && mm.State == MatchState.Playing && Time.unscaledTime - _playingStartTime < 1.4f)
            {
                main = "FIGHT";
            }
            else if (_player != null && !_player.IsAlive && (mm == null || mm.State == MatchState.Playing))
            {
                var killer = _player.Health != null ? _player.Health.LastDamage.attacker : null;
                main = killer != null && killer != _player ? $"KILLED BY {ColoredName(killer)}" : "YOU DIED";
                float r = mm != null ? mm.GetRespawnRemaining(_player) : 0f;
                sub = r > 0f ? $"RESPAWNING IN {Mathf.CeilToInt(r)}" : "";
            }
            centerText.text = main;
            if (subText != null) subText.text = sub;
        }

        // ------------------------------------------------------------------ minimap / compass
        private Image GetIcon(int index)
        {
            while (_iconPool.Count <= index)
            {
                var img = Instantiate(minimapIconTemplate, minimapIconRoot);
                _iconPool.Add(img);
            }
            return _iconPool[index];
        }

        private void UpdateMinimap()
        {
            var data = MinimapData.Current;
            if (minimapRotator == null || _soldier == null) return;
            float yaw = _soldier.Yaw;
            minimapRotator.localEulerAngles = new Vector3(0f, 0f, yaw);

            if (data != null && minimapImage != null && data.mapTexture != null)
            {
                minimapImage.texture = data.mapTexture;
                float ppm = minimapRadius / Mathf.Max(5f, minimapRange);
                Vector2 size = data.WorldSize * ppm;
                minimapImage.rectTransform.sizeDelta = size;
                Vector2 uv = data.WorldToUV(_soldier.transform.position);
                minimapImage.rectTransform.anchoredPosition = new Vector2((0.5f - uv.x) * size.x, (0.5f - uv.y) * size.y);
            }

            if (minimapIconTemplate == null || minimapIconRoot == null) return;
            int used = 0;
            float scale = minimapRadius / Mathf.Max(5f, minimapRange);
            Quaternion inv = Quaternion.Euler(0f, -yaw, 0f);
            foreach (var c in Combatant.All)
            {
                if (c == null || c == _player || !c.IsAlive) continue;
                bool friendly = _player != null && !TeamUtil.AreHostile(_player, c);
                bool drone = c.Team == TeamId.Hostile;
                Vector3 offset = c.Position - _soldier.transform.position;
                float dist = new Vector2(offset.x, offset.z).magnitude;
                bool visible = friendly || (Time.time - c.LastLoudShotTime < 2.5f) || (drone && dist < 15f);
                if (!visible) continue;
                Vector3 local = inv * offset;
                Vector2 p = new Vector2(local.x, local.z) * scale;
                if (p.magnitude > minimapRadius - 6f) p = p.normalized * (minimapRadius - 6f);
                var icon = GetIcon(used++);
                icon.gameObject.SetActive(true);
                icon.rectTransform.anchoredPosition = p;
                float pingAge = Time.time - c.LastLoudShotTime;
                float fade = friendly || drone ? 1f : Mathf.Clamp01(1f - Mathf.Max(0f, pingAge - 1.4f) / 1.1f);
                Color baseCol = drone ? new Color(1f, 0.85f, 0.2f) : friendly ? Friendly : Enemy;
                baseCol.a *= fade;
                icon.color = baseCol;
            }
            for (int i = used; i < _iconPool.Count; i++) _iconPool[i].gameObject.SetActive(false);
        }

        private void BuildCompass()
        {
            if (compassTemplate == null || compassRoot == null) return;
            compassTemplate.gameObject.SetActive(false);
            string[] cardinals = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            for (int i = 0; i < 24; i++)
            {
                float angle = i * 15f;
                var t = Instantiate(compassTemplate, compassRoot);
                t.gameObject.SetActive(true);
                bool cardinal = i % 3 == 0;
                t.text = cardinal ? cardinals[i / 3] : angle.ToString("0");
                t.fontSize = cardinal ? compassTemplate.fontSize * 1.25f : compassTemplate.fontSize * 0.8f;
                _compassLabels.Add((t, angle));
            }
        }

        private void UpdateCompass()
        {
            if (_compassLabels.Count == 0) return;
            float yaw = _soldier != null ? _soldier.Yaw : 0f;
            float half = compassFov * 0.5f;
            foreach (var (label, angle) in _compassLabels)
            {
                float rel = Mathf.DeltaAngle(yaw, angle);
                bool vis = Mathf.Abs(rel) < half;
                label.enabled = vis;
                if (!vis) continue;
                label.rectTransform.anchoredPosition = new Vector2(rel / half * compassWidth * 0.5f, 0f);
                label.alpha = 1f - Mathf.Pow(Mathf.Abs(rel) / half, 3f);
            }
        }

        // ------------------------------------------------------------------ misc
        private void UpdateMisc(float dt)
        {
            if (fpsText != null)
            {
                fpsText.enabled = LR2Settings.ShowFps;
                _fpsFrames++;
                _fpsTimer += dt;
                if (_fpsTimer >= 0.5f)
                {
                    int fps = Mathf.RoundToInt(_fpsFrames / _fpsTimer);
                    fpsText.text = $"{fps} FPS";
                    fpsText.color = fps >= 58 ? new Color(0.85f, 0.9f, 0.85f, 0.72f) : fps >= 30 ? Accent : Enemy;
                    _fpsFrames = 0;
                    _fpsTimer = 0f;
                }
            }
            if (bodycamOverlay != null)
            {
                bodycamOverlay.enabled = false; // REC overlay removed (user request)
            }
            if (hudGroup != null)
            {
                var mm = MatchManager.Instance;
                float target = mm != null && mm.State == MatchState.Ended ? 0f : 1f;
                hudGroup.alpha = Mathf.MoveTowards(hudGroup.alpha, target, dt * 3f);
            }
        }
    }
}
