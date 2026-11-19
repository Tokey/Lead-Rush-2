using System.Collections.Generic;
using LR2.Characters;
using LR2.Combat;
using LR2.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.HighDefinition;

namespace LR2.Game
{
    /// <summary>
    /// AI vs AI spectator (user 11-19: "a AI vs AI mode where i can test various difficulties of AI"). Chase cam behind a
    /// soldier, their eye view, or a free camera; an auto director cuts to whoever just got a kill. The right-hand panel is
    /// the test bench: kills, deaths and K/D per difficulty and the team scores.
    /// Controls: LMB / RMB next / previous soldier, V cycles chase / eye / free, SPACE auto director, scroll zoom,
    /// free cam WASD + Q/E, SHIFT fast.
    /// </summary>
    public class SpectatorCamera : MonoBehaviour
    {
        public enum ViewMode { Chase, Eye, Free }

        public static SpectatorCamera Instance { get; private set; }
        /// <summary>The followed soldier (null in free cam): the HUD binds to it.</summary>
        public static Combatant Target => Instance != null && Instance._mode != ViewMode.Free ? Instance._target : null;
        public static bool HideCrosshair => Instance != null && Instance._mode != ViewMode.Eye;

        private Camera _cam;
        private Combatant _target;
        private ViewMode _mode = ViewMode.Chase;
        private float _yaw, _pitch = 10f, _dist = 3.4f;
        private Vector3 _pos, _vel;
        private bool _auto = true;
        private float _nextAutoCut, _targetDiedAt = -1f;
        private LR2.UI.GameMenuController _menu;
        private GUIStyle _box, _head, _row, _hint;
        private Texture2D _bg;

        /// <summary>0 chase, 1 eye, 2 free (media capture and UI).</summary>
        public void SetView(int mode) => _mode = (ViewMode)Mathf.Clamp(mode, 0, 2);

        public static void Ensure()
        {
            if (Instance != null) return;
            var go = new GameObject("[LR2 Spectator]") { tag = "MainCamera" };
            go.AddComponent<SpectatorCamera>();
        }

        private void Awake()
        {
            Instance = this;
            _cam = gameObject.AddComponent<Camera>();
            _cam.fieldOfView = 68f;
            _cam.nearClipPlane = 0.05f;
            _cam.farClipPlane = 1500f;
            gameObject.AddComponent<HDAdditionalCameraData>();
            gameObject.AddComponent<AudioListener>();
            GameEvents.Killed += OnKilled;
        }

        private void OnDestroy()
        {
            GameEvents.Killed -= OnKilled;
            if (Instance == this) Instance = null;
            if (_bg != null) Destroy(_bg);
        }

        private void Start()
        {
            _menu = FindAnyObjectByType<LR2.UI.GameMenuController>();
            Cycle(1);
            if (_target != null) { _yaw = _target.transform.eulerAngles.y; _pos = Pivot(_target) - Quaternion.Euler(_pitch, _yaw, 0f) * Vector3.forward * _dist; }
            transform.position = _pos;
        }

        private bool Paused => _menu != null && _menu.IsPaused;

        private void OnKilled(KillEvent e)
        {
            if (e.victim != null && e.victim == _target) _targetDiedAt = Time.time;
            // auto director: cut to the killer, at most every 5 s unless our soldier just died
            if (!_auto || e.killer == null || e.killer == e.victim || !e.killer.IsAlive) return;
            if (e.victim == _target || Time.time >= _nextAutoCut) { SetTarget(e.killer); _nextAutoCut = Time.time + 5f; }
        }

        private void SetTarget(Combatant c)
        {
            if (c == null || c == _target) return;
            _target = c;
            _targetDiedAt = -1f;
            _yaw = c.transform.eulerAngles.y;
        }

        private void Cycle(int dir)
        {
            var all = new List<Combatant>();
            foreach (var c in Combatant.All) if (c != null && c.isActiveAndEnabled && c.GetComponent<SoldierController>() != null) all.Add(c);
            if (all.Count == 0) return;
            all.Sort((a, b) => string.CompareOrdinal(a.Team + a.DisplayName, b.Team + b.DisplayName));
            int i = Mathf.Max(0, all.IndexOf(_target));
            for (int n = 0; n < all.Count; n++)
            {
                i = (i + dir + all.Count) % all.Count;
                if (all[i].IsAlive || n == all.Count - 1) { SetTarget(all[i]); return; }
            }
        }

        private static Vector3 Pivot(Combatant c)
        {
            var sc = c.GetComponent<SoldierController>();
            Vector3 eye = sc != null && sc.EyeTransform != null ? sc.EyeTransform.position : c.transform.position + Vector3.up * 1.6f;
            return Vector3.Lerp(c.transform.position + Vector3.up * 1.45f, eye, 0.5f);
        }

        private void LateUpdate()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            bool input = !Paused;
            if (input)
            {
                if (Cursor.lockState != CursorLockMode.Locked) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
                if (mouse != null)
                {
                    if (mouse.leftButton.wasPressedThisFrame) { _auto = false; Cycle(1); }
                    if (mouse.rightButton.wasPressedThisFrame) { _auto = false; Cycle(-1); }
                    Vector2 d = mouse.delta.ReadValue() * 0.12f;
                    _yaw += d.x;
                    _pitch = Mathf.Clamp(_pitch - d.y, -70f, 80f);
                    float scroll = mouse.scroll.ReadValue().y;
                    if (Mathf.Abs(scroll) > 0.01f) _dist = Mathf.Clamp(_dist - Mathf.Sign(scroll) * 0.4f, 1.4f, 9f);
                }
                if (kb != null)
                {
                    if (kb.vKey.wasPressedThisFrame) _mode = (ViewMode)(((int)_mode + 1) % 3);
                    if (kb.fKey.wasPressedThisFrame) _mode = _mode == ViewMode.Free ? ViewMode.Chase : ViewMode.Free;
                    if (kb.spaceKey.wasPressedThisFrame) _auto = !_auto;
                    if (kb.rightArrowKey.wasPressedThisFrame) { _auto = false; Cycle(1); }
                    if (kb.leftArrowKey.wasPressedThisFrame) { _auto = false; Cycle(-1); }
                }
            }

            if (_target == null || (!_target.IsAlive && _targetDiedAt > 0f && Time.time - _targetDiedAt > 2.2f)) Cycle(1);
            float dt = Time.unscaledDeltaTime;

            if (_mode == ViewMode.Free || _target == null)
            {
                Vector3 move = Vector3.zero;
                if (input && kb != null)
                {
                    if (kb.wKey.isPressed) move += Vector3.forward;
                    if (kb.sKey.isPressed) move += Vector3.back;
                    if (kb.dKey.isPressed) move += Vector3.right;
                    if (kb.aKey.isPressed) move += Vector3.left;
                    if (kb.eKey.isPressed) move += Vector3.up;
                    if (kb.qKey.isPressed) move += Vector3.down;
                }
                float speed = input && kb != null && kb.leftShiftKey.isPressed ? 24f : 8f;
                transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
                transform.position += transform.rotation * move * speed * dt;
                _pos = transform.position;
                _cam.fieldOfView = 68f;
                return;
            }

            if (_mode == ViewMode.Eye)
            {
                var sc = _target.GetComponent<SoldierController>();
                Transform eye = sc != null ? sc.EyeTransform : null;
                Quaternion look = sc != null ? Quaternion.Euler(sc.Pitch, sc.Yaw, 0f) : _target.transform.rotation;
                Vector3 p = (eye != null ? eye.position : Pivot(_target)) + look * new Vector3(0f, 0.02f, 0.14f);
                transform.SetPositionAndRotation(p, Quaternion.Slerp(transform.rotation, look, 1f - Mathf.Exp(-dt * 30f)));
                _pos = p;
                _cam.fieldOfView = Mathf.Lerp(_cam.fieldOfView, sc != null && sc.AimBlend > 0.5f ? 50f : 70f, 1f - Mathf.Exp(-dt * 10f));
                _cam.nearClipPlane = 0.08f;
                return;
            }

            // chase: orbit behind the soldier's shoulder, pushed in by walls
            _cam.nearClipPlane = 0.05f;
            Vector3 pivot = Pivot(_target);
            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 want = pivot + rot * new Vector3(0.45f, 0.15f, -_dist);
            if (Physics.SphereCast(pivot, 0.18f, (want - pivot).normalized, out var hit, Vector3.Distance(pivot, want), LR2Layers.EnvironmentMask, QueryTriggerInteraction.Ignore))
                want = pivot + (want - pivot).normalized * Mathf.Max(0.4f, hit.distance - 0.05f);
            _pos = Vector3.SmoothDamp(_pos, want, ref _vel, 0.07f, Mathf.Infinity, dt);
            transform.SetPositionAndRotation(_pos, Quaternion.LookRotation((pivot + rot * Vector3.forward * 6f) - _pos));
            _cam.fieldOfView = Mathf.Lerp(_cam.fieldOfView, 62f, 1f - Mathf.Exp(-dt * 6f));
        }

        // ------------------------------------------------------------------ overlay
        private void Styles()
        {
            if (_box != null) return;
            _bg = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            _bg.SetPixel(0, 0, new Color(0.03f, 0.04f, 0.05f, 0.72f));
            _bg.Apply();
            _box = new GUIStyle { normal = { background = _bg }, padding = new RectOffset(14, 14, 10, 10) };
            _head = new GUIStyle { fontSize = 15, fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.74f, 0.32f) } };
            _row = new GUIStyle { fontSize = 13, richText = true, normal = { textColor = new Color(0.88f, 0.90f, 0.92f) } };
            _hint = new GUIStyle(_row) { fontSize = 12, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.75f, 0.78f, 0.80f, 0.9f) } };
        }

        private static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

        private void OnGUI()
        {
            if (Paused || LR2.World.KillcamDirector.IsPlaying) return;
            var mm = MatchManager.Instance;
            if (mm == null || mm.State == MatchState.Ended) return;
            Styles();
            float s = Screen.height / 1080f;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1f));
            float w = Screen.width / s;

            // who we watch
            if (_target != null && _mode != ViewMode.Free)
            {
                string hp = _target.Health != null ? $"{Mathf.CeilToInt(_target.Health.Current)} HP" : "";
                string team = _target.Team == TeamId.None ? "FREE FOR ALL" : TeamUtil.DisplayName(_target.Team);
                GUILayout.BeginArea(new Rect(w * 0.5f - 230f, 104f, 460f, 64f), _box);
                GUILayout.Label($"<color=#{Hex(TeamUtil.ColorFor(null, _target))}>{team}</color>   {_target.DisplayName}   <color=#9AA0A6>{(_target.IsAlive ? hp : "KIA")}</color>", new GUIStyle(_row) { fontSize = 16, alignment = TextAnchor.MiddleCenter });
                GUILayout.Label($"{(_mode == ViewMode.Chase ? "CHASE CAM" : "EYE VIEW")}   {(_auto ? "AUTO DIRECTOR ON" : "MANUAL")}", _hint);
                GUILayout.EndArea();
            }

            // test bench: per difficulty and per team
            var diff = new Dictionary<string, (int k, int d, int n)>();
            foreach (var c in mm.Participants)
            {
                if (c == null) continue;
                string name = c.DisplayName ?? "";
                int a = name.LastIndexOf('['), b = name.LastIndexOf(']');
                string key = a >= 0 && b > a ? name.Substring(a + 1, b - a - 1) : "BOT";
                diff.TryGetValue(key, out var v);
                diff[key] = (v.k + c.stats.kills, v.d + c.stats.deaths, v.n + 1);
            }
            float h = 64f + diff.Count * 22f + 30f + 4 * 22f;
            GUILayout.BeginArea(new Rect(w - 330f, 300f, 310f, h), _box);
            GUILayout.Label("AI TEST BENCH", _head);
            GUILayout.Label("<color=#9AA0A6>DIFFICULTY       BOTS     K      D     K/D</color>", _row);
            foreach (var kv in diff)
            {
                float kd = kv.Value.d > 0 ? kv.Value.k / (float)kv.Value.d : kv.Value.k;
                GUILayout.Label($"{kv.Key,-14}  {kv.Value.n,5}  {kv.Value.k,5}  {kv.Value.d,5}  {kd,6:0.00}", _row);
            }
            if (mm.IsTeamMode)
            {
                GUILayout.Space(8f);
                foreach (var t in mm.TeamsInPlay)
                    GUILayout.Label($"<color=#{Hex(TeamUtil.TeamColor(t))}>{TeamUtil.DisplayName(t),-12}</color>  {mm.GetTeamScore(t),4} / {mm.ScoreLimit}", _row);
            }
            GUILayout.EndArea();

            GUI.Label(new Rect(0f, 1080f - 40f, w, 24f),
                "LMB / RMB  switch soldier     V  chase / eye / free     SPACE  auto director     SCROLL  zoom     ESC  menu", _hint);
        }
    }
}
