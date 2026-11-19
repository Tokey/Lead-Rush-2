using System.Collections.Generic;
using KINEMATION.FPSAnimationFramework.Runtime.Camera;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using LR2.Audio;
using LR2.Characters;
using LR2.Combat;
using LR2.Core;
using LR2.FX;
using LR2.Game;
using LR2.UI;
using LR2.Weapons;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

namespace LR2.World
{
    /// <summary>
    /// CoD-style killcam: records every soldier's full visual pose, gun pose, driver eye socket and aim at 60 Hz (and the
    /// victim's ragdoll for 1.6 s after death). When the local player is killed it cuts back 2.0 s before the fatal shot
    /// and replays the killer through its own eyes exactly as the live first-person player sees itself (B14.1): lens on the
    /// driver eye socket, first-person head hidden, body aligned to the lens with the hands re-solved onto the gun, the real
    /// recorded recoil plus the live camera shake, the live ADS FOV, then the victim's real ragdoll death and hitmarkers.
    /// </summary>
    [DefaultExecutionOrder(300)]
    public class KillcamDirector : MonoBehaviour
    {
        public static KillcamDirector Instance { get; private set; }
        public static bool IsPlaying => Instance != null && Instance._playing;

        [SerializeField] private bool killcamEnabled = true;
        [Tooltip("Seconds of death cam before cutting to the killcam replay.")]
        [SerializeField, Min(0f)] private float startDelay = 1.0f;
        [SerializeField, Min(0.5f)] private float duration = 3.2f;
        [SerializeField, Min(0.5f)] private float preKillWindow = 2.0f;
        [SerializeField, Min(0f)] private float postKillWindow = 0.95f;
        [SerializeField] private float fieldOfView = 72f;

        public float PreKillWindow => preKillWindow;
        public float PostKillWindow => postKillWindow;
        public float Duration => duration;

        // ---- read-only surface for the pose-check harness (B14.1 evidence: frames + numbers per replay moment)
        public float DebugSampleTime { get; private set; }
        public float DebugKillTime => _killTime;
        public bool DebugPuppets => _puppets;
        public int DebugReplayShots => _replayShots.Count;
        public int DebugReplayHits => _replayHits.Count;
        public GameObject DebugKillerPuppet => _killerPuppet;
        public GameObject DebugKillerGunPuppet => _killerGunPuppet;
        public GameObject DebugVictimPuppet => _victimPuppet;

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
            public float aimBlend;
            public bool localPlayer;
            public Vector3 visEyePos; // the SWAT model's own eye estimate (for the first-person body alignment)
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
            public bool armorHit;
            public bool armorBroken;
            public bool played;
        }

        private const int MaxSamplesPerSoldier = 330;
        private const float RecordAfterDeath = 1.6f; // keep recording the ragdoll and dropped gun this long

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

        // B14.1 live-player camera feel: Kinemation per-shot camera shake replayed on the recorded shots (the synthetic
        // gun/camera kick of B14 is gone: the recording already holds the real recoil animation and view kick)
        private FPSCameraShake _shakeAsset;
        private Vector3 _shake, _shakeTarget;
        private float _shakePlayback;
        private LR2.Weapons.WeaponDefinition _killerGunDef;
        private float _killerBodyBack;
        private Transform[] _kArm; // puppet L upper/lower/hand, R upper/lower/hand

        // B111 headshot bullet cam (user 11-19: "on killcams for headshots show a quick pan of the bullet hitting the head in
        // slomo, but dont make it too long"). When the replay reaches the killing headshot it cuts beside the bullet's path:
        // the round streaks the last 1.9 m in near-frozen time, the camera swings round to a side-on view as it hits, and the
        // impact plays out at ~0.5x. It replaces 0.34 s of the normal replay, so a headshot killcam is only ~0.75 s longer.
        private const float HsCamReal = 1.1f;     // real seconds of bullet cam
        private const float HsLead = 0.035f;      // recorded seconds before the impact
        private const float HsTail = 0.30f;       // recorded seconds after it
        private const float HsApproach = 0.42f;   // share of the bullet cam the round is in flight
        private bool _hsPending, _hsActive, _hsBloodDone;
        private float _hsStartReal, _hsHitTime, _hsSideSign = 1f;
        private Vector3 _hsPoint, _hsDir = Vector3.forward;
        private Transform _hsHead;
        private LineRenderer _hsBullet;

        /// <summary>Extra replay time a headshot kill adds (the bullet cam minus the replay time it stands in for).</summary>
        private float HeadshotExtra => HsCamReal - (HsLead + HsTail) * duration / Mathf.Max(0.1f, preKillWindow + postKillWindow);

        /// <summary>Extra respawn time so the killcam can play out before the player is put back in.</summary>
        public static float ExtraRespawnDelay(in KillEvent e, float baseDelay)
        {
            if (Instance == null || !Instance.killcamEnabled) return 0f;
            if (e.victim == null || !e.victim.IsLocalPlayer) return 0f;
            if (e.killer == null || e.killer == e.victim) return 0f;
            if (e.killer.GetComponent<SoldierController>() == null) return 0f;
            float hs = e.headshot ? Instance.HeadshotExtra : 0f;
            return Mathf.Max(0f, (Instance.EffectiveStartDelay + Instance.duration + hs) - baseDelay);
        }

        /// <summary>The replay starts only after the post-kill window has been recorded (the victim's real death).</summary>
        private float EffectiveStartDelay => Mathf.Max(startDelay, postKillWindow + 0.05f);

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
                armorHit = e.armorDamage > 0f,
                armorBroken = e.armorBroken,
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
                if (c == null) continue;
                if (!c.IsAlive && Time.time - c.LastDeathTime > RecordAfterDeath) continue; // ragdoll recorded briefly
                var sc = c.GetComponent<SoldierController>();
                if (sc == null) continue;
                if (!_histories.TryGetValue(sc, out var list))
                {
                    list = new List<Snapshot>(MaxSamplesPerSoldier);
                    _histories[sc] = list;
                }
                var proxy = sc.GetComponent<RetargetProxy>();
                bool hasLiveCam = sc.ViewCamera != null && sc.ViewCamera.isActiveAndEnabled;
                // The camera transform is a child of the driver's Head bone for every soldier (inactive on bots, but it still
                // follows the head): it is the exact eye socket the live player looks through and Kinemation aims the sights at.
                Vector3 visEye = proxy != null ? proxy.VisualEyePosition : (sc.ViewTransform != null ? sc.ViewTransform.position : c.HeadPosition);
                Vector3 eye = sc.ViewCamera != null ? sc.ViewCamera.transform.position : visEye;
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
                    aimBlend = sc.AimBlend,
                    localPlayer = sc.IsLocalPlayer,
                    visEyePos = visEye
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
            Transform gunTf = sc.IsDead ? (sc.DroppedWeapon != null ? sc.DroppedWeapon.transform : null) : (gun != null ? gun.transform : null);
            smp.hasWeapon = gunTf != null;
            if (gunTf != null)
            {
                smp.weaponPos = gunTf.position;
                smp.weaponRot = gunTf.rotation;
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
            // Body/aim windows are copied in Begin(), after the post-kill window (the victim's ragdoll) has been recorded.

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
                    armorHit = false,
                    armorBroken = false,
                    played = false
                });
            }
            deathPush.y = 0f;
            _victimDeathDir = deathPush.sqrMagnitude > 0.01f ? deathPush.normalized : killer.transform.forward;

            // B111: the killing headshot is where the bullet cam looks
            _hsPending = _hsActive = false;
            if (e.headshot)
            {
                for (int i = _replayHits.Count - 1; i >= 0; i--)
                {
                    if (!_replayHits[i].killed) continue;
                    _hsHitTime = _replayHits[i].time;
                    _hsPoint = _replayHits[i].point;
                    _hsDir = _replayHits[i].direction;
                    _hsPending = _hsHitTime - HsLead >= _replayStartTime && _hsHitTime + HsTail <= _replayEndTime + 0.05f;
                    break;
                }
                Vector3 shotLine = victim.transform.position - killer.transform.position;
                if (_hsDir.sqrMagnitude < 0.01f) _hsDir = shotLine;
                _hsDir = _hsDir.sqrMagnitude > 0.0001f ? _hsDir.normalized : Vector3.forward;
            }

            _pending = true;
            _startAt = Time.time + EffectiveStartDelay;
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
                    aimBlend = sc.AimBlend,
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
            bool hsCam = UpdateHeadshotClock(ref sampleTime);
            DebugSampleTime = sampleTime;
            Snapshot kSnap = Evaluate(_killerReplay, sampleTime);

            Quaternion aimRot = Quaternion.Euler(kSnap.pitch, kSnap.yaw, 0f);
            Quaternion recordedYawRot = Quaternion.Euler(0f, kSnap.yaw, 0f);
            if (_puppets)
            {
                ApplyPose(_killerPuppet, _killerPuppetBones, _killerGunPuppet, _killerPoseReplay, sampleTime);
                ApplyPose(_victimPuppet, _victimPuppetBones, _victimGunPuppet, _victimPoseReplay, sampleTime);
                // The victim's real ragdoll death is in the recording; the procedural collapse is only a fallback.
                bool victimDeathRecorded = _victimPoseReplay.Count > 0 && _victimPoseReplay[_victimPoseReplay.Count - 1].time > _killTime + 0.15f;
                if (sampleTime >= _killTime && !victimDeathRecorded)
                    ApplyVictimCollapse(_victimPuppet, _victimGunPuppet, Mathf.Clamp01((sampleTime - _killTime) / Mathf.Max(0.25f, postKillWindow * 0.72f)), _victimDeathDir);
                if (!kSnap.localPlayer) AlignKillerToLens(kSnap);

                // Lens: the recorded driver eye socket, stabilised aim (it already holds the real per-shot view kick and the
                // killer's own pull-down), plus the live Kinemation camera shake on each replayed shot.
                UpdateReplayShake(Time.deltaTime);
                Quaternion aim = kSnap.localPlayer ? kSnap.eyeRot : Quaternion.Euler(kSnap.pitch, kSnap.yaw, 0f);
                Quaternion camRot = aim * Quaternion.Euler(_shake);
                var fpCam = _victim.ViewCamera;
                fpCam.transform.SetPositionAndRotation(kSnap.eyePos, camRot);
                fpCam.fieldOfView = kSnap.localPlayer && kSnap.fov > 10f ? kSnap.fov : ReplayFov(fpCam.aspect, kSnap.aimBlend);
                ReplayShots(sampleTime, camRot, kSnap.aimBlend);
                ReplayHits(sampleTime);
                if (hsCam) ApplyBulletCam(fpCam);
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
            if (hsCam) ApplyBulletCam(cam);
            if (_info != null) _info.alpha = Mathf.Clamp01(norm * 6f);
        }

        // ------------------------------------------------------------------ B111 headshot bullet cam
        /// <summary>Takes over the replay clock for the bullet cam; true while it runs.</summary>
        private bool UpdateHeadshotClock(ref float sampleTime)
        {
            if (_hsPending && sampleTime >= _hsHitTime - HsLead)
            {
                _hsPending = false;
                _hsActive = true;
                _hsBloodDone = false;
                _hsStartReal = Time.time;
                _endAt += HsCamReal; // re-timed on exit
                _hsHead = FindHead(_puppets && _victimPuppet != null ? _victimPuppet.transform : _victim != null ? _victim.transform : null);
                _hsSideSign = PickBulletCamSide();
                var sounds = LR2CommonSounds.Instance; // the round cracks past the lens
                if (sounds != null) LR2Audio.PlayAt(sounds.bulletWhizz, _hsPoint - _hsDir * 0.8f, 1f);
            }
            if (!_hsActive) return false;
            float u = Mathf.Clamp01((Time.time - _hsStartReal) / HsCamReal);
            sampleTime = u < HsApproach
                ? Mathf.Lerp(_hsHitTime - HsLead, _hsHitTime, u / HsApproach)                       // near-frozen: the round flies
                : Mathf.Lerp(_hsHitTime, _hsHitTime + HsTail, (u - HsApproach) / (1f - HsApproach)); // ~0.5x: the impact
            if (u < 1f) return true;
            _hsActive = false;
            if (_hsBullet != null) _hsBullet.enabled = false;
            // carry on with the normal replay from where the slow motion stopped
            float exitNorm = Mathf.InverseLerp(_replayStartTime, _replayEndTime, sampleTime);
            _endAt = Time.time + (1f - exitNorm) * duration;
            return false;
        }

        private Vector3 BulletCamOffset(float e, float sign)
        {
            float ang = Mathf.Lerp(35f, 100f, e) * sign;
            float r = Mathf.Lerp(1.4f, 0.95f, e);
            return Quaternion.AngleAxis(ang, Vector3.up) * (-_hsDir) * r + Vector3.up * Mathf.Lerp(0.14f, 0.05f, e);
        }

        /// <summary>The orbit side with no wall between the camera and the head (right first).</summary>
        private float PickBulletCamSide()
        {
            foreach (float sign in new[] { 1f, -1f })
            {
                bool clear = true;
                foreach (float e in new[] { 0f, 0.5f, 1f })
                    if (Physics.Linecast(_hsPoint, _hsPoint + BulletCamOffset(e, sign), LR2Layers.EnvironmentMask, QueryTriggerInteraction.Ignore)) clear = false;
                if (clear) return sign;
            }
            return 1f;
        }

        private static Transform FindHead(Transform root)
        {
            if (root == null) return null;
            var anim = root.GetComponentInChildren<Animator>();
            if (anim != null && anim.isHuman)
            {
                var h = anim.GetBoneTransform(HumanBodyBones.Head);
                if (h != null) return h;
            }
            foreach (var t in root.GetComponentsInChildren<Transform>())
                if (t.name.EndsWith("Head", System.StringComparison.OrdinalIgnoreCase)) return t;
            return null;
        }

        private void ApplyBulletCam(Camera cam)
        {
            if (cam == null) return;
            float u = Mathf.Clamp01((Time.time - _hsStartReal) / HsCamReal);
            float e = Mathf.SmoothStep(0f, 1f, u);
            float fly = Mathf.Clamp01(u / HsApproach);
            Vector3 bullet = _hsPoint - _hsDir * Mathf.Lerp(1.9f, 0f, fly * fly * (3f - 2f * fly) * 0.25f + fly * 0.75f);
            Vector3 head = _hsHead != null ? _hsHead.position : _hsPoint;
            Vector3 camPos = _hsPoint + BulletCamOffset(e, _hsSideSign);
            Vector3 look = u < HsApproach ? Vector3.Lerp(bullet, _hsPoint, 0.5f + 0.5f * fly)
                                          : Vector3.Lerp(_hsPoint, head, Mathf.Clamp01((u - HsApproach) * 2.5f));
            cam.transform.SetPositionAndRotation(camPos, Quaternion.LookRotation(look - camPos, Vector3.up));
            cam.fieldOfView = Mathf.Lerp(46f, 34f, e);

            EnsureBullet();
            if (_hsBullet != null)
            {
                bool inFlight = u < HsApproach;
                _hsBullet.enabled = inFlight;
                if (inFlight)
                {
                    _hsBullet.SetPosition(0, bullet - _hsDir * 0.6f);
                    _hsBullet.SetPosition(1, bullet);
                }
            }
            if (!_hsBloodDone && u >= HsApproach)
            {
                _hsBloodDone = true;
                ImpactSystem.SpawnBlood(_hsPoint, -_hsDir, _hsDir, true);
            }
        }

        /// <summary>The round: a hot additive streak (core plus soft halo) on the keyword-free LR2/ParticleUnlit shader.</summary>
        private void EnsureBullet()
        {
            if (_hsBullet != null) return;
            var go = new GameObject("[Killcam Bullet]");
            go.transform.SetParent(transform, false);
            _hsBullet = go.AddComponent<LineRenderer>();
            _hsBullet.useWorldSpace = true;
            _hsBullet.positionCount = 2;
            _hsBullet.numCapVertices = 4;
            _hsBullet.startWidth = 0.002f;
            _hsBullet.endWidth = 0.016f;
            _hsBullet.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _hsBullet.receiveShadows = false;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.55f, 0.25f), 0f), new GradientColorKey(new Color(1f, 0.92f, 0.78f), 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.55f, 0.6f), new GradientAlphaKey(1f, 1f) });
            _hsBullet.colorGradient = g;
            var shader = Shader.Find("LR2/ParticleUnlit");
            if (shader != null)
            {
                var m = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                m.SetColor("_Color", new Color(1f, 0.82f, 0.6f, 1f));
                m.SetFloat("_Intensity", 3f);
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
                m.renderQueue = 3100;
                _hsBullet.sharedMaterial = m;
            }
            _hsBullet.enabled = false;
        }

        private void ReplayShots(float sampleTime, Quaternion aimRot, float aimBlend = 0f)
        {
            for (int i = 0; i < _replayShots.Count; i++)
            {
                var s = _replayShots[i];
                if (s.played || sampleTime < s.time) continue;
                s.played = true;
                _replayShots[i] = s;

                // The gun's recoil animation and the view kick are already in the recording (Kinemation RecoilAnimation runs
                // on bots, AddViewKick feeds their pitch/yaw). Add only what the live player's camera gets on top.
                PlayReplayShake();
                CameraShake.ReplayShot(_killerGunDef, aimBlend > 0.5f);

                Vector3 muzzle = _puppetMuzzle != null ? _puppetMuzzle.position : s.origin + aimRot * new Vector3(0.15f, -0.10f, 0.55f);
                Combatant killerCombatant = _killer != null ? _killer.Combatant : null;
                Combatant local = Combatant.FindLocalPlayer();
                bool killerFriendly = killerCombatant != null && (killerCombatant.IsLocalPlayer || (local != null && !TeamUtil.AreHostile(local, killerCombatant)));
                TracerSystem.Spawn(muzzle, s.origin + s.direction * 40f, killerFriendly);
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
                HUDController.ShowReplayHit(h.headshot, h.killed, h.point, h.armorHit, h.armorBroken);
            }
        }

        /// <summary>Kinemation FPSCameraController.PlayCameraShake, mirrored for the replay camera.</summary>
        private void PlayReplayShake()
        {
            if (_shakeAsset == null) return;
            _shakePlayback = 0f;
            _shakeTarget = new Vector3(FPSCameraShake.GetTarget(_shakeAsset.pitch), FPSCameraShake.GetTarget(_shakeAsset.yaw), FPSCameraShake.GetTarget(_shakeAsset.roll));
        }

        /// <summary>Kinemation FPSCameraController.UpdateCameraShake, mirrored (same curve, targets and smoothing).</summary>
        private void UpdateReplayShake(float dt)
        {
            if (_shakeAsset == null) { _shake = Vector3.zero; return; }
            float length = _shakeAsset.shakeCurve.GetCurveLength();
            _shakePlayback = Mathf.Clamp(_shakePlayback + dt * _shakeAsset.playRate, 0f, length);
            float alpha = KMath.ExpDecayAlpha(_shakeAsset.smoothSpeed, dt);
            if (!KAnimationMath.IsWeightRelevant(alpha)) alpha = 1f;
            Vector3 target = _shakeAsset.shakeCurve.GetValue(_shakePlayback);
            target.x *= _shakeTarget.x;
            target.y *= _shakeTarget.y;
            target.z *= _shakeTarget.z;
            _shake = Vector3.Lerp(_shake, target, alpha);
        }

        /// <summary>The live player's FOV: settings hip FOV (Hor+) x the weapon's ADS multiplier, eased by the aim blend.</summary>
        private float ReplayFov(float aspect, float aimBlend)
        {
            float hip = LR2Settings.VerticalFov(LR2Settings.FieldOfView, aspect);
            float mult = _killerGunDef != null ? _killerGunDef.adsFovMultiplier : 0.8f;
            float e = Mathf.Clamp01(aimBlend);
            e = e * e * (3f - 2f * e);
            return Mathf.Lerp(hip, hip * mult, e);
        }

        /// <summary>
        /// RetargetProxy.AlignBody's first-person eye lock for the killer puppet: the model's eye onto the lens, the body
        /// pulled back by the same amount, then both arms re-solved onto the recorded holds so the hands stay on the gun.
        /// </summary>
        private void AlignKillerToLens(Snapshot k)
        {
            if (_killerPuppet == null || _kArm == null) return;
            Vector3 fwd = Quaternion.Euler(0f, k.yaw, 0f) * Vector3.forward;
            Vector3 shift = k.eyePos - k.visEyePos - fwd * _killerBodyBack;
            if (shift.sqrMagnitude < 1e-8f || shift.sqrMagnitude > 0.25f * 0.25f) return; // nothing to do / teleport guard
            Vector3 lp = _kArm[2].position, rp = _kArm[5].position;
            Quaternion lr = _kArm[2].rotation, rr = _kArm[5].rotation;
            _killerPuppet.transform.position += shift;
            RetargetProxy.Solve(_kArm[0], _kArm[1], _kArm[2], lp, lr, 1f);
            RetargetProxy.Solve(_kArm[3], _kArm[4], _kArm[5], rp, rr, 1f);
        }

        private static Transform[] MapArmBones(Animator liveVisual, Transform liveRoot, Transform puppetRoot)
        {
            if (liveVisual == null || liveRoot == null || puppetRoot == null || !liveVisual.isHuman) return null;
            var ids = new[] { HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
                              HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand };
            var map = new Transform[ids.Length];
            for (int i = 0; i < ids.Length; i++)
            {
                var live = liveVisual.GetBoneTransform(ids[i]);
                if (live == null) return null;
                string path = RelativePath(liveRoot, live);
                map[i] = path == null ? null : (path.Length == 0 ? puppetRoot : puppetRoot.Find(path));
                if (map[i] == null) return null;
            }
            return map;
        }

        private static string RelativePath(Transform root, Transform t)
        {
            var parts = new List<string>();
            for (var c = t; c != null; c = c.parent)
            {
                if (c == root) { parts.Reverse(); return string.Join("/", parts); }
                parts.Add(c.name);
            }
            return null;
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
                // B59b: Drop weapon outward beside the collapsing puppet and down to the floor rather than backwards through chest.
                gunPuppet.transform.position += fallAxis * (0.34f * smooth) + Vector3.down * (0.92f * smooth);
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
        private static GameObject MakePuppet(GameObject source, string name, bool keepShadowsOnly, bool keepMuzzleFlash = false, bool isPlayerTeamLaser = false)
        {
            if (source == null) return null;
            // Copy under an inactive holder so no copied gameplay script runs Awake/OnEnable before it is stripped.
            var holder = new GameObject("KillcamPuppetHolder");
            holder.SetActive(false);
            var p = Instantiate(source, holder.transform);
            p.name = name;
            bool hasWeaponLaser = source.GetComponentInChildren<WeaponLaser>(true) != null;
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
            // scope cameras on the copied gun (MK18 has four) must not render a second picture-in-picture
            foreach (var cam in p.GetComponentsInChildren<Camera>(true)) { cam.enabled = false; cam.targetTexture = null; }
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
                // B14.1: Keep disabled attachments, sights, and laser helper renderers off instead of illuminating inactive parts.
                if (!r.enabled) continue;
                // B14.1: the killer keeps the live first-person head hiding (shadow only, like the player's own view);
                // everything else, and the whole victim, renders normally.
                if (!(keepShadowsOnly && r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly))
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
            }
            // The copied laser beam and dot keep their last world positions (frozen second beam, floating dot): remove them.
            foreach (var lr in p.GetComponentsInChildren<LineRenderer>(true)) DestroyImmediate(lr);
            foreach (var t in p.GetComponentsInChildren<Transform>(true))
                if (t != null && t != p.transform && t.name == "LaserDot") DestroyImmediate(t.gameObject);

            p.transform.SetParent(null, true);
            Destroy(holder);
            p.SetActive(true);
            if (hasWeaponLaser)
            {
                Transform muzzle = null;
                foreach (var t in p.GetComponentsInChildren<Transform>(true))
                    if (t.name == "Muzzle") { muzzle = t; break; }
                if (muzzle != null)
                {
                    var wl = p.AddComponent<WeaponLaser>();
                    wl.SetExternalMuzzle(muzzle, p.transform, isPlayerTeamLaser);
                }
            }
            return p;
        }

        private void BuildPuppets()
        {
            _puppets = false;
            var kRoot = VisualRoot(_killer);
            var vRoot = VisualRoot(_victim);
            if (kRoot == null || vRoot == null || _killerPoseReplay.Count == 0 || _victimPoseReplay.Count == 0) return;
            // copy the killer with its first-person renderers in their live first-person state (shadow only)
            _killer.SetHeadHidden(true);
            _killerPuppet = MakePuppet(kRoot.gameObject, "KillcamPuppet_Killer", true);
            _killer.SetHeadHidden(false);
            _victimPuppet = MakePuppet(vRoot.gameObject, "KillcamPuppet_Victim", false);
            var kProxy = _killer.GetComponent<RetargetProxy>();
            _kArm = kProxy != null ? MapArmBones(kProxy.Visual, kRoot, _killerPuppet.transform) : null;
            _killerBodyBack = kProxy != null ? kProxy.FirstPersonBodyBack : 0.02f;
            _killerPuppetBones = _killerPuppet.GetComponentsInChildren<Transform>(true);
            _victimPuppetBones = _victimPuppet.GetComponentsInChildren<Transform>(true);
            var kGun = _killer.ActiveFirearm;
            var vGun = _victim.ActiveFirearm;
            _killerGunDef = kGun != null ? kGun.Definition : null;
            _shakeAsset = kGun != null ? kGun.CameraShakeAsset : null;
            var local = Combat.Combatant.FindLocalPlayer();
            bool IsGreen(SoldierController sc)
            {
                if (sc == null || sc.Combatant == null) return false;
                if (sc.Combatant.IsLocalPlayer) return true;
                return local != null ? !sc.Combatant.IsHostileTo(local) : !sc.Combatant.IsBot;
            }
            bool killerGreenLaser = IsGreen(_killer);
            bool victimGreenLaser = IsGreen(_victim);
            _killerGunPuppet = kGun != null ? MakePuppet(kGun.gameObject, "KillcamPuppet_KillerGun", false, keepMuzzleFlash: true, isPlayerTeamLaser: killerGreenLaser) : null;
            _victimGunPuppet = vGun != null ? MakePuppet(vGun.gameObject, "KillcamPuppet_VictimGun", false, keepMuzzleFlash: false, isPlayerTeamLaser: victimGreenLaser) : null;
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
            if (_victim.DroppedWeapon != null)
                foreach (var r in _victim.DroppedWeapon.GetComponentsInChildren<Renderer>(true))
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
            _kArm = null;
            _shakeAsset = null;
            _killerGunDef = null;
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
                        aimBlend = Mathf.Lerp(a.aimBlend, b.aimBlend, t),
                        localPlayer = a.localPlayer,
                        visEyePos = Vector3.Lerp(a.visEyePos, b.visEyePos, t)
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
            _shake = _shakeTarget = Vector3.zero;
            _shakePlayback = 0f;

            CopyWindow(_killer, _replayStartTime, _replayEndTime, _killerReplay);
            CopyWindow(_victim, _replayStartTime, _replayEndTime, _victimReplay);
            CopyPoseWindow(_killer, _replayStartTime, _replayEndTime, _killerPoseReplay);
            CopyPoseWindow(_victim, _replayStartTime, _replayEndTime, _victimPoseReplay);

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
            _hsActive = _hsPending = false;
            if (_hsBullet != null) _hsBullet.enabled = false;
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
