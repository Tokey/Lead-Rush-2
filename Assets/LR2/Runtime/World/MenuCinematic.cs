using System;
using System.Collections.Generic;
using LR2.Characters;
using LR2.Combat;
using LR2.Game;
using UnityEngine;

namespace LR2.World
{
    /// <summary>
    /// Main-menu cinematic (B110, user 11-19: "while changing menu, change camera of bg too. maybe for TDM show two soldiers,
    /// for 1v1 show current, free for all show soldiers fighting or something in slomo. infact add slomo to the menu and add
    /// motion to the enemies. 3p view, sometimes 1p"). Every screen has its own set of shots; the camera glides between them
    /// in real time while the world runs in slow motion (0.3x in a fight, 0.6x on the title). Extra soldiers are clones of
    /// the scene's HeroSoldier driven by <see cref="MenuActor"/>; they are invulnerable and their bursts are aimed just wide.
    /// </summary>
    public class MenuCinematic : MonoBehaviour
    {
        public const int OptionsShot = 99;
        public static MenuCinematic Instance { get; private set; }

        private struct Shot
        {
            public Func<(Vector3 pos, Vector3 look)> pose;
            public float fov, seconds;
            public bool firstPerson;
        }

        private Camera _cam;
        private SoldierController _hero;
        private MenuActor _heroB, _ally, _e1, _e2, _e3;
        private readonly List<MenuActor> _all = new List<MenuActor>();
        private readonly List<Shot> _shots = new List<Shot>();
        private int _state = int.MinValue, _shot;
        private float _shotEnds, _blendStart, _blendDur = 1.4f, _slow = 0.6f, _baseFixed = 0.02f;
        private Vector3 _fromPos, _mainPos;
        private Quaternion _fromRot, _mainRot;
        private float _fromFov = 60f, _mainFov = 38f;
        private bool _released, _headHidden;

        private static readonly Vector3 HeroHome = new Vector3(0.4f, 0f, 1.6f);
        private static readonly Vector3 AllyHome = new Vector3(-0.75f, 0f, 0.95f);
        private static readonly Vector3 AllyOffstage = new Vector3(-14f, 0f, 14f);

        public static void Ensure()
        {
            if (Instance == null) new GameObject("[LR2 MenuCinematic]").AddComponent<MenuCinematic>();
        }

        /// <summary>Back to real time before a match loads (the loading routine must not run in slow motion).</summary>
        public static void Release()
        {
            if (Instance == null) return;
            Instance._released = true;
            Time.timeScale = 1f;
            Time.fixedDeltaTime = Instance._baseFixed;
        }

        private void Awake()
        {
            Instance = this;
            _baseFixed = Time.fixedDeltaTime > 0f ? Time.fixedDeltaTime : 0.02f;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Time.timeScale = 1f;
            Time.fixedDeltaTime = _baseFixed;
            LR2.UI.MainMenuController.OnMenuShot -= ShowScreen;
        }

        private void Start()
        {
            var heroGo = GameObject.Find("HeroSoldier");
            _cam = Camera.main;
            if (heroGo == null || _cam == null) { enabled = false; return; }
            var drift = _cam.GetComponent<MenuCameraDrift>();
            if (drift != null) drift.enabled = false;
            _mainPos = _cam.transform.position;
            _mainRot = _cam.transform.rotation;
            _fromPos = _mainPos; _fromRot = _mainRot; _fromFov = _mainFov = _cam.fieldOfView;

            _hero = heroGo.GetComponent<SoldierController>();
            _heroB = Brain(heroGo, HeroHome, 205f);
            _ally = Clone(heroGo, "Ally", AllyOffstage, 200f, TeamId.Alpha, false);
            _e1 = Clone(heroGo, "Hostile1", new Vector3(1.8f, 0f, -7.2f), 10f, TeamId.Bravo, true);
            _e2 = Clone(heroGo, "Hostile2", new Vector3(-3.4f, 0f, -6.2f), 25f, TeamId.Bravo, true);
            _e3 = Clone(heroGo, "Hostile3", new Vector3(4.8f, 0f, -4.6f), -40f, TeamId.Bravo, true);
            if (_e1 != null) _e1.phase = 1.3f;
            if (_e2 != null) _e2.phase = 2.6f;
            if (_e3 != null) _e3.phase = 4.1f;

            LR2.UI.MainMenuController.OnMenuShot += ShowScreen;
            ShowScreen(-1);
        }

        private MenuActor Clone(GameObject template, string name, Vector3 at, float yaw, TeamId team, bool hostileLook)
        {
            var go = Instantiate(template, at, Quaternion.Euler(0f, yaw, 0f));
            go.name = "Menu" + name;
            if (go.TryGetComponent<Combatant>(out var c)) { c.DisplayName = name.ToUpperInvariant(); c.Team = team; }
            if (hostileLook && go.TryGetComponent<SoldierVisuals>(out var sv)) sv.ApplyMaterialsNow(false);
            return Brain(go, at, yaw);
        }

        private MenuActor Brain(GameObject go, Vector3 home, float yaw)
        {
            if (!go.TryGetComponent<MenuActor>(out var b)) b = go.AddComponent<MenuActor>();
            b.home = home;
            b.idleYaw = yaw;
            b.sc = go.GetComponent<SoldierController>();
            if (b.sc != null) { b.sc.InputSource = b; b.sc.ControlsEnabled = true; }
            if (go.TryGetComponent<Health>(out var h)) h.SetInvulnerable(1e7f);
            _all.Add(b);
            return b;
        }

        private static void Place(MenuActor a, Vector3 pos, float yaw)
        {
            if (a == null) return;
            a.home = pos;
            a.idleYaw = yaw;
            var cc = a.GetComponent<CharacterController>();
            bool was = cc != null && cc.enabled;
            if (cc != null) cc.enabled = false;
            a.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            if (cc != null) cc.enabled = was;
        }

        // ------------------------------------------------------------------ screens
        /// <summary>-1 title, a GameModeType for the lobby, <see cref="OptionsShot"/> for options.</summary>
        public void ShowScreen(int screen)
        {
            if (!enabled || _hero == null || screen == _state) return;
            _state = screen;
            foreach (var a in _all) { a.fight = false; a.raise = false; a.strafeAmp = 0f; a.aimAt = null; a.aimOffset = Vector3.zero; }
            // the hostiles always patrol in the distance, rifles up
            Patrol(_e1, 1.5f, _heroB, new Vector3(0.9f, 0.1f, 0f));
            Patrol(_e2, 1.8f, _heroB, new Vector3(-1.0f, 0.2f, 0f));
            Patrol(_e3, 1.2f, _e2, new Vector3(0.8f, 0f, 0f));
            bool allyOn = screen == (int)GameModeType.TeamDeathmatch || screen == (int)GameModeType.CaptureTheFlag || screen == (int)GameModeType.FreeForAll;
            if (allyOn && Vector3.Distance(_ally.home, AllyHome) > 1f) Place(_ally, AllyHome, 200f);
            if (!allyOn && Vector3.Distance(_ally.home, AllyOffstage) > 1f) Place(_ally, AllyOffstage, 200f);

            _shots.Clear();
            switch (screen)
            {
                case OptionsShot:
                    _slow = 0.5f;
                    Add(WeaponClose, 30f, 9f); Add(HeroOrbit, 36f, 8f);
                    break;
                case (int)GameModeType.Duel:
                    _slow = 0.5f;
                    _heroB.raise = true; _heroB.aimAt = _e1.transform; _heroB.aimOffset = new Vector3(0.8f, 0f, 0f);
                    Add(HeroClose, 32f, 6.5f); Add(() => OverShoulder(_heroB), 42f, 6f); Add(() => FrontOf(_e1), 36f, 5.5f);
                    break;
                case (int)GameModeType.TeamDeathmatch:
                    _slow = 0.45f;
                    _ally.raise = true; _ally.aimAt = _e2.transform; _ally.aimOffset = new Vector3(-0.9f, 0f, 0f);
                    _heroB.raise = true; _heroB.aimAt = _e1.transform; _heroB.aimOffset = new Vector3(0.9f, 0f, 0f);
                    Add(TwoShot, 40f, 7f); Add(() => OverShoulder(_ally), 42f, 6f); Add(() => FirstPerson(_heroB), 58f, 5f, true);
                    break;
                case (int)GameModeType.CaptureTheFlag:
                case (int)GameModeType.FreeForAllCaptureTheFlag:
                    _slow = 0.4f;
                    Fight(_heroB, _e1, 0.9f, 0.7f); Fight(_ally, _e2, -0.9f, 0.9f); Fight(_e1, _heroB, 1.0f, 1.4f); Fight(_e2, _ally, -1.0f, 1.6f);
                    Add(TwoShot, 40f, 6f); Add(FightWide, 46f, 6.5f); Add(() => OverShoulder(_heroB), 42f, 5.5f);
                    break;
                case (int)GameModeType.FreeForAll:
                    _slow = 0.3f;
                    Fight(_heroB, _e1, 0.8f, 0.8f); Fight(_ally, _e2, -0.9f, 1.0f); Fight(_e1, _heroB, 1.0f, 1.5f);
                    Fight(_e2, _e3, -0.9f, 1.8f); Fight(_e3, _ally, 0.8f, 1.2f);
                    Add(FightWide, 46f, 6.5f); Add(() => OverShoulder(_heroB), 42f, 5.5f); Add(() => FirstPerson(_heroB), 58f, 4.5f, true);
                    Add(() => FrontOf(_e1), 36f, 5f);
                    break;
                default:
                    _slow = 0.6f;
                    Add(() => (_mainPos, _mainPos + _mainRot * Vector3.forward * 4f), 0f, 11f); Add(HeroOrbit, 36f, 8f);
                    break;
            }
            _shot = -1;
            NextShot();
        }

        private void Patrol(MenuActor a, float amp, MenuActor target, Vector3 offset)
        {
            if (a == null) return;
            a.strafeAmp = amp; a.raise = true; a.aimAt = target != null ? target.transform : null; a.aimOffset = offset;
        }

        private void Fight(MenuActor a, MenuActor target, float wide, float amp)
        {
            if (a == null || target == null) return;
            a.fight = true; a.aimAt = target.transform; a.aimOffset = new Vector3(wide, 0.15f, 0f); a.strafeAmp = amp;
        }

        private void Add(Func<(Vector3, Vector3)> pose, float fov, float seconds, bool fp = false) =>
            _shots.Add(new Shot { pose = pose, fov = fov, seconds = seconds, firstPerson = fp });

        private void NextShot()
        {
            if (_shots.Count == 0) return;
            _fromPos = _cam.transform.position;
            _fromRot = _cam.transform.rotation;
            _fromFov = _cam.fieldOfView;
            _shot = (_shot + 1) % _shots.Count;
            _blendStart = Time.unscaledTime;
            _blendDur = _shots[_shot].firstPerson ? 0.7f : 1.5f;
            _shotEnds = Time.unscaledTime + _shots[_shot].seconds;
        }

        // ------------------------------------------------------------------ shot poses
        private static Vector3 Eye(MenuActor a) =>
            a.sc != null && a.sc.EyeTransform != null ? a.sc.EyeTransform.position : a.transform.position + Vector3.up * 1.6f;
        private static Quaternion Aim(MenuActor a) => a.sc != null ? Quaternion.Euler(a.sc.Pitch, a.sc.Yaw, 0f) : a.transform.rotation;
        private static Quaternion YawRot(MenuActor a) => Quaternion.Euler(0f, a.sc != null ? a.sc.Yaw : a.transform.eulerAngles.y, 0f);

        private (Vector3, Vector3) HeroOrbit()
        {
            float ang = 205f + 24f * Mathf.Sin(Time.unscaledTime * 0.09f);
            Vector3 c = _heroB.transform.position + Vector3.up * 1.3f;
            return (c + Quaternion.Euler(0f, ang, 0f) * new Vector3(0f, -0.55f, 2.7f), c + Vector3.up * 0.1f);
        }

        private (Vector3, Vector3) HeroClose()
        {
            Vector3 e = Eye(_heroB);
            return (_heroB.transform.position + Quaternion.Euler(0f, 205f, 0f) * new Vector3(0.3f, 1.55f, 1.5f), e - Vector3.up * 0.08f);
        }

        private (Vector3, Vector3) WeaponClose()
        {
            var gun = _hero != null ? _hero.ActiveFirearm : null;
            Vector3 g = gun != null && gun.Muzzle != null ? gun.Muzzle.position : Eye(_heroB) - Vector3.up * 0.3f;
            Vector3 body = Vector3.Lerp(g, Eye(_heroB) - Vector3.up * 0.25f, 0.6f);
            return (body + YawRot(_heroB) * new Vector3(0.75f, 0.12f, 0.35f), body);
        }

        private (Vector3, Vector3) OverShoulder(MenuActor a)
        {
            Vector3 e = Eye(a);
            return (e + YawRot(a) * new Vector3(0.5f, 0.12f, -1.3f), e + Aim(a) * Vector3.forward * 12f);
        }

        private (Vector3, Vector3) FirstPerson(MenuActor a)
        {
            Vector3 e = Eye(a);
            return (e + Aim(a) * new Vector3(0f, 0f, 0.05f), e + Aim(a) * Vector3.forward * 10f);
        }

        private (Vector3, Vector3) FrontOf(MenuActor a)
        {
            Vector3 chest = a.transform.position + Vector3.up * 1.35f;
            return (a.transform.position + YawRot(a) * new Vector3(-1.1f, 1.45f, 2.3f), chest);
        }

        private (Vector3, Vector3) TwoShot()
        {
            Vector3 mid = (_heroB.transform.position + _ally.transform.position) * 0.5f;
            float drift = Mathf.Sin(Time.unscaledTime * 0.11f) * 0.5f;
            return (mid + Quaternion.Euler(0f, 205f, 0f) * new Vector3(0.3f + drift, 0.9f, 3.3f), mid + Vector3.up * 1.25f);
        }

        private (Vector3, Vector3) FightWide()
        {
            float drift = Mathf.Sin(Time.unscaledTime * 0.08f) * 0.9f;
            return (new Vector3(-6.0f + drift, 1.15f, -2.2f), new Vector3(0.6f, 1.15f, -3.3f));
        }

        // ------------------------------------------------------------------ per frame
        private void LateUpdate()
        {
            if (_released || _shots.Count == 0 || _shot < 0) return;
            float dt = Time.unscaledDeltaTime;
            Time.timeScale = Mathf.MoveTowards(Time.timeScale, _slow, dt * 0.8f);
            Time.fixedDeltaTime = _baseFixed * Mathf.Max(0.05f, Time.timeScale);
            if (Time.unscaledTime >= _shotEnds) NextShot();

            var shot = _shots[_shot];
            var (pos, look) = shot.pose();
            float u = Mathf.Clamp01((Time.unscaledTime - _blendStart) / _blendDur);
            float e = u * u * (3f - 2f * u);
            // handheld breathing on top (none in first person: the head bob is already in the eye)
            float t = Time.unscaledTime * 0.17f;
            Vector3 n = new Vector3(Mathf.PerlinNoise(t, 1.3f) - 0.5f, Mathf.PerlinNoise(2.7f, t) - 0.5f, Mathf.PerlinNoise(t, 5.1f) - 0.5f) * 2f;
            Vector3 r = new Vector3(Mathf.PerlinNoise(t * 0.8f, 9.1f) - 0.5f, Mathf.PerlinNoise(4.4f, t * 0.8f) - 0.5f, 0f) * 2f;
            float hand = shot.firstPerson ? 0f : 1f;
            Vector3 p = pos + Vector3.Scale(n, new Vector3(0.05f, 0.035f, 0.03f)) * hand;
            Quaternion q = Quaternion.LookRotation((look - pos).sqrMagnitude > 1e-4f ? look - pos : Vector3.forward, Vector3.up)
                           * Quaternion.Euler(Vector3.Scale(r, new Vector3(0.5f, 0.8f, 0f)) * hand);
            _cam.transform.SetPositionAndRotation(Vector3.Lerp(_fromPos, p, e), Quaternion.Slerp(_fromRot, q, e));
            float fov = shot.fov > 1f ? shot.fov : _mainFov;
            _cam.fieldOfView = Mathf.Lerp(_fromFov, fov, e);
            bool hide = shot.firstPerson && u > 0.8f;
            if (_hero != null && hide != _headHidden) { _headHidden = hide; _hero.SetHeadHidden(hide); }
        }
    }
}
