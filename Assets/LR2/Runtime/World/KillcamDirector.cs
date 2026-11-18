using System.Collections.Generic;
using LR2.Audio;
using LR2.Characters;
using LR2.Combat;
using LR2.Core;
using LR2.FX;
using LR2.Game;
using LR2.UI;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

namespace LR2.World
{
    /// <summary>
    /// CoD-style killcam: records a rolling 5s trajectory buffer for every soldier at 30 Hz.
    /// When the local player is killed, after a brief death pause it cuts back 2.0s before the fatal shot
    /// and replays the killer's recorded aim, movement, gunshots and tracers up to the kill moment.
    /// </summary>
    [DefaultExecutionOrder(300)]
    public class KillcamDirector : MonoBehaviour
    {
        public static KillcamDirector Instance { get; private set; }
        public static bool IsPlaying => Instance != null && Instance._playing;

        [SerializeField] private bool killcamEnabled = true;
        [Tooltip("Seconds of death cam before cutting to the killcam replay.")]
        [SerializeField, Min(0f)] private float startDelay = 0.65f;
        [SerializeField, Min(0.5f)] private float duration = 3.2f;
        [SerializeField, Min(0.5f)] private float preKillWindow = 2.0f;
        [SerializeField, Min(0f)] private float postKillWindow = 0.95f;
        [SerializeField] private float fieldOfView = 72f;

        public float PreKillWindow => preKillWindow;
        public float PostKillWindow => postKillWindow;

        private struct Snapshot
        {
            public float time;
            public Vector3 bodyPos;
            public Quaternion bodyRot;
            public Vector3 eyePos;
            public Quaternion eyeRot;
            public float fov;
            public float pitch;
            public float yaw;
            public bool localPlayer;
        }

        private struct RecordedShot
        {
            public float time;
            public Vector3 origin;
            public Vector3 direction;
            public bool played;
        }

        private struct RecordedHit
        {
            public float time;
            public Combatant attacker;
            public Combatant victim;
            public Vector3 point;
            public Vector3 direction;
            public bool headshot;
            public bool killed;
            public bool played;
        }

        private const int MaxSamplesPerSoldier = 330;

        // ---- full-body replay: every visual bone of every soldier, 60 Hz ring buffer (no per-frame allocations)
        private class PoseSample
        {
            public float time;
            public Vector3 rootPos;
            public Quaternion rootRot;
            public Vector3[] localPos;
            public Quaternion[] localRot;
            public bool hasWeapon;
            public Vector3 weaponPos;
            public Quaternion weaponRot;
        }

        private class PoseTrack
        {
            public Transform root;
            public Transform[] bones;
            public PoseSample[] ring;
            public int head, count;
        }

        private readonly Dictionary<SoldierController, PoseTrack> _poses = new Dictionary<SoldierController, PoseTrack>();
        private readonly List<PoseSample> _killerPoseReplay = new List<PoseSample>(96);
        private readonly List<PoseSample> _victimPoseReplay = new List<PoseSample>(96);
        private GameObject _killerPuppet, _victimPuppet, _killerGunPuppet, _victimGunPuppet;
        private Transform[] _killerPuppetBones, _victimPuppetBones;
        private Transform _puppetMuzzle;
        private MuzzleFlash[] _puppetMuzzleFlashes;
        private readonly List<Renderer> _hiddenLive = new List<Renderer>();
        private bool _puppets;
        private const float SampleInterval = 1f / 60f;

        private readonly Dictionary<SoldierController, List<Snapshot>> _histories = new Dictionary<SoldierController, List<Snapshot>>();
        private readonly List<RecordedShot> _shotLog = new List<RecordedShot>(128);
        private float _nextSampleAt;

        private readonly List<Snapshot> _killerReplay = new List<Snapshot>(128);
        private readonly List<Snapshot> _victimReplay = new List<Snapshot>(128);
        private readonly List<RecordedShot> _replayShots = new List<RecordedShot>(32);
        private readonly List<RecordedHit> _hitLog = new List<RecordedHit>(128);
        private readonly List<RecordedHit> _replayHits = new List<RecordedHit>(32);
        private Vector3 _victimDeathDir = Vector3.forward;

        private SoldierController _killer;
        private SoldierController _victim;
        private KillEvent _event;
        private bool _pending, _playing;
        private float _startAt, _endAt, _killTime, _replayStartTime, _replayEndTime, _savedFov;
        private Vector3 _killerSavedPos;
        private Quaternion _killerSavedRot;
        private bool _killerPoseSaved, _killerAgentWasEnabled;
        private NavMeshAgent _killerAgent;
        private Canvas _canvas;
        private TMP_Text _title, _info;

        /// <summary>Extra respawn time so the killcam can play out before the player is put back in.</summary>
        public static float ExtraRespawnDelay(in KillEvent e, float baseDelay)
        {
            if (Instance == null || !Instance.killcamEnabled) return 0f;
            if (e.victim == null || !e.victim.IsLocalPlayer) return 0f;
            if (e.killer == null || e.killer == e.victim) return 0f;
            if (e.killer.GetComponent<SoldierController>() == null) return 0f;
            return Mathf.Max(0f, (Instance.startDelay + Instance.duration) - baseDelay);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Instance != null || FindAnyObjectByType<MatchManager>() == null) return;
            new GameObject("[LR2 Killcam]").AddComponent<KillcamDirector>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            BuildOverlay();
        }

        private void OnEnable()
        {
            GameEvents.Killed += OnKilled;
            GameEvents.Hit += OnHitRecorded;
            GameEvents.ShotFired += OnShotFired;
            GameEvents.CombatantSpawned += OnSpawned;
        }

        private void OnDisable()
        {
            GameEvents.Killed -= OnKilled;
            GameEvents.Hit -= OnHitRecorded;
            GameEvents.ShotFired -= OnShotFired;
            GameEvents.CombatantSpawned -= OnSpawned;
            End();
            if (Instance == this) Instance = null;
        }

        private void OnHitRecorded(HitEvent e)
        {
            if (_playing || e.attacker == null || e.victim == null || e.damage <= 0f) return;
            _hitLog.Add(new RecordedHit
            {
                time = Time.time,
                attacker = e.attacker,
                victim = e.victim,
                point = e.point,
                direction = e.direction,
                headshot = e.zone == HitZone.Head,
                killed = e.killed,
                played = false
            });
            float cutoff = Time.time - 6f;
            while (_hitLog.Count > 0 && _hitLog[0].time < cutoff) _hitLog.RemoveAt(0);
        }

        private void OnShotFired(ShotEvent e)
        {
            if (_playing || e.shooter == null) return;
            _shotLog.Add(new RecordedShot { time = Time.time, origin = e.origin, direction = e.direction, played = false });
            float cutoff = Time.time - 6f;
            while (_shotLog.Count > 0 && _shotLog[0].time < cutoff) _shotLog.RemoveAt(0);
        }

        private void RecordSnapshots()
        {
            if (Time.time < _nextSampleAt) return;
            _nextSampleAt = Time.time + SampleInterval;
            var all = Combatant.All;
            float cutoff = Time.time - 5.5f;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c == null || !c.IsAlive) continue;
                var sc = c.GetComponent<SoldierController>();
                if (sc == null) continue;
                if (!_histories.TryGetValue(sc, out var list))
                {
                    list = new List<Snapshot>(MaxSamplesPerSoldier);
                    _histories[sc] = list;
                }
                var proxy = sc.GetComponent<RetargetProxy>();
                bool hasLiveCam = sc.ViewCamera != null && sc.ViewCamera.isActiveAndEnabled;
                Vector3 eye = hasLiveCam
                    ? sc.ViewCamera.transform.position
                    : (proxy != null ? proxy.VisualEyePosition : (sc.ViewTransform != null ? sc.ViewTransform.position : c.HeadPosition));
                Quaternion eyeRot = hasLiveCam
                    ? sc.ViewCamera.transform.rotation
                    : Quaternion.Euler(sc.Pitch, sc.Yaw, 0f);
                float baseFov = LR2Settings.VerticalFov(LR2Settings.FieldOfView, Camera.main != null ? Camera.main.aspect : 16f / 9f);
                float fov = hasLiveCam
                    ? sc.ViewCamera.fieldOfView
                    : Mathf.Lerp(baseFov, baseFov * 0.78f, sc.AimBlend);
                list.Add(new Snapshot
                {
                    time = Time.time,
                    bodyPos = sc.transform.position,
                    bodyRot = sc.transform.rotation,
                    eyePos = eye,
                    eyeRot = eyeRot,
                    fov = fov,
                    pitch = sc.Pitch,
                    yaw = sc.Yaw,
                    localPlayer = sc.IsLocalPlayer
                });
                while (list.Count > MaxSamplesPerSoldier || (list.Count > 0 && list[0].time < cutoff))
                    list.RemoveAt(0);
                RecordPose(sc);
            }
        }

        private static Transform VisualRoot(SoldierController sc)
        {
            var proxy = sc.GetComponent<RetargetProxy>();
            return proxy != null && proxy.Visual != null ? proxy.Visual.transform : null;
        }

        private void RecordPose(SoldierController sc)
        {
            if (!_poses.TryGetValue(sc, out var track) || track.root == null)
            {
                var root = VisualRoot(sc);
                if (root == null) return;
                track = new PoseTrack { root = root, bones = root.GetComponentsInChildren<Transform>(true), ring = new PoseSample[MaxSamplesPerSoldier] };
                for (int i = 0; i < track.ring.Length; i++)
                    track.ring[i] = new PoseSample { localPos = new Vector3[track.bones.Length], localRot = new Quaternion[track.bones.Length] };
                _poses[sc] = track;
            }
            var smp = track.ring[track.head];
            track.head = (track.head + 1) % track.ring.Length;
            track.count = Mathf.Min(track.count + 1, track.ring.Length);
            smp.time = Time.time;
            smp.rootPos = track.root.position;
            smp.rootRot = track.root.rotation;
            for (int b = 0; b < track.bones.Length; b++)
            {
                var t = track.bones[b];
                if (t == null) continue;
                smp.localPos[b] = t.localPosition;
                smp.localRot[b] = t.localRotation;
            }
            var gun = sc.ActiveFirearm;
            smp.hasWeapon = gun != null;
            if (gun != null)
            {
                smp.weaponPos = gun.transform.position;
                smp.weaponRot = gun.transform.rotation;
            }
        }

        private void CopyPoseWindow(SoldierController sc, float tMin, float tMax, List<PoseSample> dst)
        {
            dst.Clear();
            if (sc == null || !_poses.TryGetValue(sc, out var track)) return;
            int n = track.count, len = track.ring.Length;
            for (int k = 0; k < n; k++)
            {
                var smp = track.ring[(track.head - n + k + len) % len];
                if (smp.time >= tMin - 0.1f && smp.time <= tMax + 0.1f) dst.Add(smp);
            }
        }

        private void OnKilled(KillEvent e)
        {
            if (!killcamEnabled || e.victim == null || !e.victim.IsLocalPlayer || e.killer == null || e.killer == e.victim) return;
            var killer = e.killer.GetComponent<SoldierController>();
            var victim = e.victim.GetComponent<SoldierController>();
            if (killer == null || victim == null) return;

            End();
            _killer = killer;
            _victim = victim;
            _event = e;
            _killTime = Time.time;
            _replayStartTime = _killTime - preKillWindow;
            _replayEndTime = _killTime + postKillWindow;

            CopyWindow(killer, _replayStartTime, _replayEndTime, _killerReplay);
            CopyWindow(victim, _replayStartTime, _replayEndTime, _victimReplay);
            CopyPoseWindow(killer, _replayStartTime, _replayEndTime, _killerPoseReplay);
            CopyPoseWindow(victim, _replayStartTime, _replayEndTime, _victimPoseReplay);

            _replayShots.Clear();
            for (int i = 0; i < _shotLog.Count; i++)
            {
                var s = _shotLog[i];
                if (s.time >= _replayStartTime && s.time <= _replayEndTime)
                    _replayShots.Add(s);
            }

            _replayHits.Clear();
            bool hasKillHit = false;
            Vector3 deathPush = (victim.transform.position - killer.transform.position);
            for (int i = 0; i < _hitLog.Count; i++)
            {
                var h = _hitLog[i];
                if (h.attacker == e.killer && h.victim == e.victim && h.time >= _replayStartTime && h.time <= _replayEndTime)
                {
                    _replayHits.Add(h);
                    if (h.killed) hasKillHit = true;
                    if (h.direction.sqrMagnitude > 0.01f) deathPush = h.direction;
                }
            }
            if (!hasKillHit)
            {
                _replayHits.Add(new RecordedHit
                {
                    time = _killTime,
                    attacker = e.killer,
                    victim = e.victim,
                    point = victim.transform.position + Vector3.up * 1.4f,
                    direction = deathPush,
                    headshot = e.headshot,
                    killed = true,
                    played = false
                });
            }
            deathPush.y = 0f;
            _victimDeathDir = deathPush.sqrMagnitude > 0.01f ? deathPush.normalized : killer.transform.forward;

            _pending = true;
            _startAt = Time.time + startDelay;
        }

        private void CopyWindow(SoldierController sc, float tMin, float tMax, List<Snapshot> dst)
        {
            dst.Clear();
            if (sc != null && _histories.TryGetValue(sc, out var src))
            {
                for (int i = 0; i < src.Count; i++)
                {
                    if (src[i].time >= tMin - 0.1f && src[i].time <= tMax + 0.1f)
                        dst.Add(src[i]);
                }
            }
            if (dst.Count == 0 && sc != null)
            {
                Vector3 eye = sc.ViewTransform != null ? sc.ViewTransform.position : sc.transform.position + Vector3.up * 1.5f;
                dst.Add(new Snapshot
                {
                    time = _killTime,
                    bodyPos = sc.transform.position,
                    bodyRot = sc.transform.rotation,
                    eyePos = eye,
                    eyeRot = Quaternion.Euler(sc.Pitch, sc.Yaw, 0f),
                    fov = LR2Settings.VerticalFov(LR2Settings.FieldOfView, 16f / 9f),
                    pitch = sc.Pitch,
                    yaw = sc.Yaw,
                    localPlayer = sc.IsLocalPlayer
                });
            }
        }

        private void OnSpawned(Combatant c)
        {
            if (c != null && c.IsLocalPlayer) { _pending = false; End(); }
        }

        private void LateUpdate()
        {
            if (!_playing) RecordSnapshots();

            if (_pending && Time.time >= _startAt)
            {
                _pending = false;
                Begin();
            }
            if (!_playing) return;
            if (_victim == null || _victim.ViewCamera == null || Time.time >= _endAt)
            {
                End();
                return;
            }

            float norm = 1f - Mathf.Clamp01((_endAt - Time.time) / duration);
            float sampleTime = Mathf.Lerp(_replayStartTime, _replayEndTime, norm);
            Snapshot kSnap = Evaluate(_killerReplay, sampleTime);

            Quaternion aimRot = Quaternion.Euler(kSnap.pitch, kSnap.yaw, 0f);
            Quaternion recordedYawRot = Quaternion.Euler(0f, kSnap.yaw, 0f);
            if (_puppets)
            {
                ApplyPose(_killerPuppet, _killerPuppetBones, _killerGunPuppet, _killerPoseReplay, sampleTime);
                ApplyPose(_victimPuppet, _victimPuppetBones, _victimGunPuppet, _victimPoseReplay, sampleTime);
                if (sampleTime >= _killTime)
                    ApplyVictimCollapse(_victimPuppet, _victimGunPuppet, Mathf.Clamp01((sampleTime - _killTime) / Mathf.Max(0.25f, postKillWindow * 0.72f)), _victimDeathDir);
                // Match first-person camera-back separation identically while keeping hands & weapon locked together rigidly
                if (!kSnap.localPlayer)
                {
                    Vector3 bodyBack = -(recordedYawRot * Vector3.forward) * 0.025f;
                    if (_killerPuppet != null) _killerPuppet.transform.position += bodyBack;
                    if (_killerGunPuppet != null) _killerGunPuppet.transform.position += bodyBack;
                }
                var fpCam = _victim.ViewCamera;
                fpCam.transform.SetPositionAndRotation(kSnap.eyePos, kSnap.eyeRot);
                fpCam.fieldOfView = kSnap.fov > 10f ? kSnap.fov : fieldOfView;
                ReplayShots(sampleTime, kSnap.eyeRot);
                ReplayHits(sampleTime);
                if (_info != null) _info.alpha = Mathf.Clamp01(norm * 6f);
                return;
            }
            if (_killer != null && !_killer.IsDead)
            {
                _killer.transform.SetPositionAndRotation(kSnap.bodyPos, recordedYawRot);
            }

            // Position camera just over the killer's right shoulder so the killer's Ghost mask, rifle and fatal tracers are clearly framed.
            Vector3 desiredCamPos = kSnap.eyePos + aimRot * new Vector3(0.26f, 0.08f, -0.95f);
            if (Physics.Linecast(kSnap.eyePos, desiredCamPos, out var camBlock, LR2Layers.EnvironmentMask, QueryTriggerInteraction.Ignore))
                desiredCamPos = camBlock.point + camBlock.normal * 0.22f;
            Vector3 camPos = desiredCamPos;
            if (_victimReplay.Count > 0)
            {
                Snapshot vSnap = Evaluate(_victimReplay, sampleTime);
                Vector3 toVictim = vSnap.eyePos - camPos;
                if (toVictim.sqrMagnitude > 0.25f)
                {
                    Quaternion trackRot = Quaternion.LookRotation(toVictim.normalized);
                    aimRot = Quaternion.Slerp(aimRot, trackRot, 0.55f);
                }
            }

            var cam = _victim.ViewCamera;
            cam.transform.SetPositionAndRotation(camPos, aimRot);
            cam.fieldOfView = fieldOfView;

            ReplayShots(sampleTime, aimRot);
            ReplayHits(sampleTime);
            if (_info != null) _info.alpha = Mathf.Clamp01(norm * 6f);
        }

        private void ReplayShots(float sampleTime, Quaternion aimRot)
        {
            for (int i = 0; i < _replayShots.Count; i++)
            {
                var s = _replayShots[i];
                if (s.played || sampleTime < s.time) continue;
                s.played = true;
                _replayShots[i] = s;
                Vector3 muzzle = _puppetMuzzle != null ? _puppetMuzzle.position : s.origin + aimRot * new Vector3(0.15f, -0.10f, 0.55f);
                TracerSystem.Spawn(muzzle, s.origin + s.direction * 40f);
                if (_puppetMuzzleFlashes != null)
                {
                    for (int m = 0; m < _puppetMuzzleFlashes.Length; m++)
                        if (_puppetMuzzleFlashes[m] != null) _puppetMuzzleFlashes[m].Play();
                }
                var gun = _killer != null ? _killer.ActiveFirearm : null;
                if (gun != null && gun.Definition != null && gun.Definition.audio != null)
                    LR2Audio.PlayAt(gun.Definition.audio.firePlayer, muzzle, 0.85f);
            }
        }

        private void ReplayHits(float sampleTime)
        {
            for (int i = 0; i < _replayHits.Count; i++)
            {
                var h = _replayHits[i];
                if (h.played || sampleTime < h.time) continue;
                h.played = true;
                _replayHits[i] = h;
                HUDController.ShowReplayHit(h.headshot, h.killed, h.point);
            }
        }

        private static void ApplyVictimCollapse(GameObject victimPuppet, GameObject gunPuppet, float progress, Vector3 deathDir)
        {
            if (victimPuppet == null || progress <= 0f) return;
            float smooth = progress * progress * (3f - 2f * progress);
            Vector3 fallAxis = Vector3.Cross(Vector3.up, deathDir);
            if (fallAxis.sqrMagnitude < 0.01f) fallAxis = Vector3.right;
            else fallAxis.Normalize();

            // Knock body back & collapse down to prone level cleanly on the floor
            Vector3 drift = deathDir * (0.34f * smooth) + Vector3.down * (0.82f * smooth);
            victimPuppet.transform.position += drift;
            Quaternion dropRot = Quaternion.AngleAxis(82f * smooth, fallAxis) * Quaternion.Euler(0f, 0f, 16f * smooth);
            victimPuppet.transform.rotation = dropRot * victimPuppet.transform.rotation;

            if (gunPuppet != null)
            {
                gunPuppet.transform.position += deathDir * (0.28f * smooth) + Vector3.down * (0.95f * smooth) + fallAxis * (0.22f * smooth);
                gunPuppet.transform.rotation = Quaternion.Euler(65f * smooth, 35f * smooth, -55f * smooth) * gunPuppet.transform.rotation;
            }
        }

        private static void ApplyPose(GameObject puppet, Transform[] bones, GameObject gunPuppet, List<PoseSample> list, float time)
        {
            if (puppet == null || bones == null || list.Count == 0) return;
            PoseSample a = list[0], b = list[0];
            float t = 0f;
            if (time >= list[list.Count - 1].time) a = b = list[list.Count - 1];
            else if (time > list[0].time)
                for (int i = 0; i < list.Count - 1; i++)
                    if (time <= list[i + 1].time)
                    {
                        a = list[i];
                        b = list[i + 1];
                        t = Mathf.InverseLerp(a.time, b.time, time);
                        break;
                    }
            puppet.transform.SetPositionAndRotation(Vector3.Lerp(a.rootPos, b.rootPos, t), Quaternion.Slerp(a.rootRot, b.rootRot, t));
            int n = Mathf.Min(bones.Length, a.localRot.Length);
            for (int i = 1; i < n; i++) // 0 = root (set above)
            {
                var bone = bones[i];
                if (bone == null) continue;
                bone.localPosition = Vector3.Lerp(a.localPos[i], b.localPos[i], t);
                bone.localRotation = Quaternion.Slerp(a.localRot[i], b.localRot[i], t);
            }
            if (gunPuppet != null && a.hasWeapon)
                gunPuppet.transform.SetPositionAndRotation(Vector3.Lerp(a.weaponPos, b.weaponPos, t), Quaternion.Slerp(a.weaponRot, b.weaponRot, t));
        }

        /// <summary>Render-only copy (no colliders, physics, scripts, animators) of a soldier's SWAT body or weapon; preserves MuzzleFlash if requested.</summary>
        private static GameObject MakePuppet(GameObject source, string name, bool hideHead, bool keepMuzzleFlash = false)
        {
            if (source == null) return null;
            var p = Instantiate(source);
            p.name = name;
            foreach (var mb in p.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (keepMuzzleFlash && mb is MuzzleFlash) continue;
                DestroyImmediate(mb);
            }
            foreach (var j in p.GetComponentsInChildren<Joint>(true)) DestroyImmediate(j);
            foreach (var rb in p.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(rb);
            foreach (var c in p.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
            foreach (var an in p.GetComponentsInChildren<Animator>(true)) DestroyImmediate(an);
            foreach (var au in p.GetComponentsInChildren<AudioSource>(true)) DestroyImmediate(au);
            if (!keepMuzzleFlash)
            {
                foreach (var l in p.GetComponentsInChildren<Light>(true)) l.enabled = false;
                foreach (var ps in p.GetComponentsInChildren<ParticleSystem>(true)) ps.gameObject.SetActive(false);
            }
            else
            {
                foreach (var ps in p.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = ps.main;
                    main.playOnAwake = false;
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }
            foreach (var r in p.GetComponentsInChildren<Renderer>(true))
            {
                r.enabled = true;
                bool head = r.name.ToLowerInvariant().Contains("head");
                r.shadowCastingMode = hideHead && head ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly : UnityEngine.Rendering.ShadowCastingMode.On;
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
            }
            p.SetActive(true);
            return p;
        }

        private void BuildPuppets()
        {
            _puppets = false;
            var kRoot = VisualRoot(_killer);
            var vRoot = VisualRoot(_victim);
            if (kRoot == null || vRoot == null || _killerPoseReplay.Count == 0 || _victimPoseReplay.Count == 0) return;
            _killerPuppet = MakePuppet(kRoot.gameObject, "KillcamPuppet_Killer", true);
            _victimPuppet = MakePuppet(vRoot.gameObject, "KillcamPuppet_Victim", false);
            _killerPuppetBones = _killerPuppet.GetComponentsInChildren<Transform>(true);
            _victimPuppetBones = _victimPuppet.GetComponentsInChildren<Transform>(true);
            var kGun = _killer.ActiveFirearm;
            var vGun = _victim.ActiveFirearm;
            _killerGunPuppet = kGun != null ? MakePuppet(kGun.gameObject, "KillcamPuppet_KillerGun", false, keepMuzzleFlash: true) : null;
            _victimGunPuppet = vGun != null ? MakePuppet(vGun.gameObject, "KillcamPuppet_VictimGun", false) : null;
            _puppetMuzzle = null;
            _puppetMuzzleFlashes = _killerGunPuppet != null ? _killerGunPuppet.GetComponentsInChildren<MuzzleFlash>(true) : null;
            if (_killerGunPuppet != null)
                foreach (var t in _killerGunPuppet.GetComponentsInChildren<Transform>(true))
                    if (t.name == "Muzzle") { _puppetMuzzle = t; break; }
            // hide the live bodies (killer keeps playing unseen, victim is a ragdoll) until the replay ends
            _hiddenLive.Clear();
            foreach (var sc in new[] { _killer, _victim })
                foreach (var r in sc.GetComponentsInChildren<Renderer>(true))
                    if (r.enabled) { r.enabled = false; _hiddenLive.Add(r); }
            _puppets = true;
        }

        private void DestroyPuppets()
        {
            foreach (var r in _hiddenLive) if (r != null) r.enabled = true;
            _hiddenLive.Clear();
            foreach (var p in new[] { _killerPuppet, _victimPuppet, _killerGunPuppet, _victimGunPuppet })
                if (p != null) Destroy(p);
            _killerPuppet = _victimPuppet = _killerGunPuppet = _victimGunPuppet = null;
            _killerPuppetBones = _victimPuppetBones = null;
            _puppetMuzzle = null;
            _puppetMuzzleFlashes = null;
            _puppets = false;
        }

        private static Snapshot Evaluate(List<Snapshot> list, float time)
        {
            if (list.Count == 1 || time <= list[0].time) return list[0];
            int last = list.Count - 1;
            if (time >= list[last].time) return list[last];
            for (int i = 0; i < last; i++)
            {
                if (time <= list[i + 1].time)
                {
                    Snapshot a = list[i], b = list[i + 1];
                    float t = Mathf.InverseLerp(a.time, b.time, time);
                    return new Snapshot
                    {
                        time = time,
                        bodyPos = Vector3.Lerp(a.bodyPos, b.bodyPos, t),
                        bodyRot = Quaternion.Slerp(a.bodyRot, b.bodyRot, t),
                        eyePos = Vector3.Lerp(a.eyePos, b.eyePos, t),
                        eyeRot = Quaternion.Slerp(a.eyeRot, b.eyeRot, t),
                        fov = Mathf.Lerp(a.fov, b.fov, t),
                        pitch = Mathf.LerpAngle(a.pitch, b.pitch, t),
                        yaw = Mathf.LerpAngle(a.yaw, b.yaw, t),
                        localPlayer = a.localPlayer
                    };
                }
            }
            return list[last];
        }

        private void Begin()
        {
            if (_killer == null || _victim == null || _victim.ViewCamera == null) return;
            _playing = true;
            _endAt = Time.time + duration;
            _savedFov = _victim.ViewCamera.fieldOfView;

            BuildPuppets();
            if (!_puppets)
            {
                _killerSavedPos = _killer.transform.position;
                _killerSavedRot = _killer.transform.rotation;
                _killerPoseSaved = true;
                _killerAgent = _killer.GetComponent<NavMeshAgent>();
                if (_killerAgent != null)
                {
                    _killerAgentWasEnabled = _killerAgent.enabled;
                    if (_killerAgent.enabled) _killerAgent.enabled = false;
                }
            }
            if (_canvas != null) _canvas.enabled = true;
            if (_info != null)
                _info.text = $"{_event.killer.DisplayName}   <color=#9AA0A6>{_event.weaponName}  -  {_event.distance:0} m{(_event.headshot ? "  -  HEADSHOT" : "")}  -  REPLAY</color>";
        }

        private void End()
        {
            if (!_playing) return;
            _playing = false;
            DestroyPuppets();
            if (_killer != null)
            {
                _killer.SetHeadHidden(false);
                if (_killerPoseSaved && !_killer.IsDead)
                    _killer.transform.SetPositionAndRotation(_killerSavedPos, _killerSavedRot);
                if (_killerAgent != null && _killerAgentWasEnabled && !_killer.IsDead)
                {
                    _killerAgent.enabled = true;
                    _killerAgent.Warp(_killerSavedPos);
                }
            }
            _killerPoseSaved = false;
            _killerAgent = null;
            if (_victim != null && _victim.ViewCamera != null) _victim.ViewCamera.fieldOfView = _savedFov;
            if (_canvas != null) _canvas.enabled = false;
        }

        private void BuildOverlay()
        {
            var go = new GameObject("KillcamOverlay");
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 400;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            void Bar(float yMin, float yMax)
            {
                var b = new GameObject("Bar", typeof(RectTransform), typeof(Image));
                b.transform.SetParent(go.transform, false);
                var rt = (RectTransform)b.transform;
                rt.anchorMin = new Vector2(0f, yMin);
                rt.anchorMax = new Vector2(1f, yMax);
                rt.offsetMin = rt.offsetMax = Vector2.zero;
                b.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.85f);
            }
            Bar(0f, 0.09f);
            Bar(0.91f, 1f);
            TMP_Text Label(string n, float y, float size, Color c)
            {
                var t = new GameObject(n, typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
                t.transform.SetParent(go.transform, false);
                var rt = (RectTransform)t.transform;
                rt.anchorMin = new Vector2(0f, y);
                rt.anchorMax = new Vector2(1f, y);
                rt.sizeDelta = new Vector2(0f, size * 1.6f);
                t.alignment = TextAlignmentOptions.Center;
                t.fontSize = size;
                t.color = c;
                t.characterSpacing = 8f;
                t.raycastTarget = false;
                return t;
            }
            _title = Label("Title", 0.955f, 46f, new Color(1f, 0.32f, 0.26f));
            _title.text = "KILLCAM";
            _title.fontStyle = FontStyles.Bold;
            _info = Label("Info", 0.045f, 30f, Color.white);
            _canvas.enabled = false;
        }
    }
}
