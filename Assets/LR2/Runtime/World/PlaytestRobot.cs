using System;
using System.IO;
using System.Text;
using LR2.AI;
using LR2.Characters;
using LR2.Combat;
using LR2.Core;
using LR2.Game;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace LR2.World
{
    /// <summary>
    /// Automated playtester (in the spirit of InvasionFramerate's QaRobot): launch the game or editor with
    /// "-lr2Robot" and it takes over the local player with the bot AI, plays a real match, saves screenshots from the
    /// player's camera every few seconds and writes a report (kills, hits, errors) to Logs/LR2_Playtest.
    /// Inert without the flag. In a built player it quits after <see cref="duration"/> seconds.
    /// </summary>
    [DefaultExecutionOrder(500)]
    public class PlaytestRobot : MonoBehaviour
    {
        public static bool Requested => Array.IndexOf(Environment.GetCommandLineArgs(), "-lr2Robot") >= 0
            || (Application.isEditor && File.Exists(Path.Combine(Application.dataPath, "..", "Temp", "LR2_RobotRequest.flag")));

        [SerializeField] private float captureInterval = 4f;
        [SerializeField] private int captureWidth = 1280;
        [SerializeField] private int captureHeight = 720;
        [SerializeField] private float duration = 75f;

        private Combatant _player;
        private SoldierController _soldier;
        private bool _piloted;
        private bool _destructibleTested;
        private bool _reloadTested;
        private float _reloadCaptureAt = -1f;
        private float _reloadLateCaptureAt = -1f;
        private float _nextCapture;
        private float _start;
        private int _shots;
        private int _errors;
        private string _outDir;
        private string _lastMatchSummary = "no MatchManager";
        private string _lastParticipantsSummary = "";
        private readonly StringBuilder _log = new StringBuilder();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!Requested || FindAnyObjectByType<PlaytestRobot>() != null) return;
            var go = new GameObject("[LR2 PlaytestRobot]");
            DontDestroyOnLoad(go);
            go.AddComponent<PlaytestRobot>();
        }

        private void Start()
        {
            _start = Time.realtimeSinceStartup;
            _nextCapture = Time.time + 2f;
            _outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "LR2_Playtest"));
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-lr2OutDir" && !string.IsNullOrWhiteSpace(args[i + 1]))
                {
                    _outDir = Path.IsPathRooted(args[i + 1])
                        ? Path.GetFullPath(args[i + 1])
                        : Path.GetFullPath(Path.Combine(Application.dataPath, "..", args[i + 1]));
                }
            }
            Directory.CreateDirectory(_outDir);
            foreach (var f in Directory.GetFiles(_outDir, "*.png")) File.Delete(f);
            GameEvents.Killed += OnKilled;
            GameEvents.Hit += OnHit;
            GameEvents.MatchStateChanged += e => Note($"Match state {e.previous} -> {e.current}");
            Application.logMessageReceived += OnLog;
            Note("Robot started");
        }

        private void OnDestroy()
        {
            GameEvents.Killed -= OnKilled;
            GameEvents.Hit -= OnHit;
            Application.logMessageReceived -= OnLog;
            WriteReport();
        }

        private void OnLog(string msg, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception) return;
            _errors++;
            if (_errors <= 25) Note($"{type}: {msg} | {(stack ?? "").Split('\n')[0]}");
        }

        private void OnKilled(KillEvent e) =>
            Note($"KILL {(e.killer != null ? e.killer.DisplayName : "world")} -> {(e.victim != null ? e.victim.DisplayName : "?")} [{e.weaponName}] hs={e.headshot} dist={e.distance:0.0}");

        private void OnHit(HitEvent e)
        {
            if (e.damage > 0f) Note($"HIT {(e.attacker != null ? e.attacker.DisplayName : "world")} -> {(e.victim != null ? e.victim.DisplayName : "?")} {e.damage:0} {e.zone}");
        }

        private void Note(string s) => _log.AppendLine($"[{Time.time,7:0.00}] {s}");

        private void Update()
        {
            if (_player == null)
            {
                _player = Combatant.FindLocalPlayer();
                _soldier = _player != null ? _player.GetComponent<SoldierController>() : null;
            }
            if (_soldier != null && !_piloted) Pilot();

            var mm = MatchManager.Instance;
            if (mm != null)
            {
                _lastMatchSummary = $"{mm.State} / {(mm.Settings != null ? mm.Settings.displayName : "?")}";
                var ps = new StringBuilder();
                foreach (var c in Combatant.All)
                    if (c != null) ps.AppendLine($"  {c.DisplayName,-12} K {c.stats.kills} D {c.stats.deaths} shots {c.stats.shotsFired} hits {c.stats.shotsHit} alive {c.IsAlive}");
                _lastParticipantsSummary = ps.ToString();
            }
            bool playing = mm != null && mm.State == MatchState.Playing;
            if (playing && !_destructibleTested && Time.time > 3.5f && _soldier != null)
            {
                _destructibleTested = true;
                TestNearestDestructible();
            }
            if (playing && _piloted && !_reloadTested && Time.time > 6.5f && _soldier != null && !_soldier.IsDead && _soldier.ActiveFirearm != null)
            {
                var gun = _soldier.ActiveFirearm;
                var bot = _soldier.GetComponent<BotController>();
                if (bot != null && bot.enabled) bot.enabled = false;
                _soldier.Movement?.StopSprint();
                if (gun.AmmoInMagazine == gun.MagazineSize)
                {
                    gun.OnFirePressed();
                }
                else
                {
                    gun.OnFireReleased();
                    if (_soldier.RequestReload())
                    {
                        var vcam = _soldier.ViewCamera;
                        if (vcam != null && Physics.Raycast(vcam.transform.position, vcam.transform.forward, 3.0f, LR2Layers.EnvironmentMask))
                            _soldier.transform.Rotate(0f, 145f, 0f);
                        _reloadTested = true;
                        _reloadCaptureAt = Time.time + 0.55f;
                        _reloadLateCaptureAt = Time.time + 1.35f;
                        Note("Triggered tactical reload for animation verification");
                    }
                    else if (bot != null) bot.enabled = true;
                }
            }
            if (!Application.isEditor && Time.realtimeSinceStartup - _start > duration)
            {
                WriteReport();
                Application.Quit();
            }
        }

        private void TestNearestDestructible()
        {
            var props = FindObjectsByType<DestructibleProp>(FindObjectsSortMode.None);
            DestructibleProp best = null;
            float bestD2 = float.MaxValue;
            Vector3 refPos = _soldier != null ? _soldier.transform.position : Vector3.zero;
            foreach (var p in props)
            {
                if (p == null || !p.CanBeDamaged) continue;
                float d2 = (p.transform.position - refPos).sqrMagnitude;
                if (p.explosive && d2 < 64f) continue; // avoid blowing up the local playtest bot right at spawn
                if (d2 < bestD2) { bestD2 = d2; best = p; }
            }
            if (best == null) { Note("DESTRUCTIBLE_TEST none found"); return; }
            var info = new DamageInfo
            {
                amount = best.maxHealth + 25f,
                type = DamageType.Bullet,
                attacker = _player,
                point = best.transform.position + Vector3.up * 0.4f,
                normal = Vector3.up,
                direction = (best.transform.position - refPos).normalized,
                force = 4f
            };
            float applied = best.ApplyDamage(ref info);
            Note($"DESTRUCTIBLE_TEST {best.name} total={props.Length} dmg={applied:0} destroyed={!best.CanBeDamaged}");
        }

        private void LateUpdate()
        {
            if (_reloadCaptureAt > 0f && Time.time >= _reloadCaptureAt)
            {
                _reloadCaptureAt = -1f;
                Capture("shot_reload.png");
                CaptureThirdPerson("shot_third_reload.png");
                return;
            }
            if (_reloadLateCaptureAt > 0f && Time.time >= _reloadLateCaptureAt)
            {
                _reloadLateCaptureAt = -1f;
                Capture("shot_reload_late.png");
                var bot = _soldier != null ? _soldier.GetComponent<BotController>() : null;
                if (bot != null) bot.enabled = true;
                return;
            }
            if (Time.time < _nextCapture) return;
            _nextCapture = Time.time + captureInterval;
            Capture(null);
        }

        /// <summary>Hands the local player to the bot AI (same perception/aim/movement code the enemies use).</summary>
        private void Pilot()
        {
            _piloted = true;
            var go = _soldier.gameObject;
            var pis = go.GetComponent<PlayerInputSource>();
            if (pis != null) pis.enabled = false;
            var agent = go.GetComponent<NavMeshAgent>();
            if (agent == null)
            {
                agent = go.AddComponent<NavMeshAgent>();
                agent.radius = 0.35f;
                agent.height = 1.8f;
                agent.speed = 5f;
                agent.acceleration = 40f;
                agent.angularSpeed = 720f;
            }
            var bot = go.GetComponent<BotController>();
            if (bot == null) bot = go.AddComponent<BotController>();
            bot.enabled = true;
            Note($"Piloting local player '{_player.DisplayName}' with BotController");
        }

        /// <summary>Renders the local soldier from the front (arms, grip, skins, reload pose) with a temporary camera.</summary>
        private void CaptureThirdPerson(string fileName)
        {
            if (_soldier == null) return;
            var go = new GameObject("RobotThirdPersonCam");
            try
            {
                var t = _soldier.transform;
                go.transform.position = t.position + t.forward * 1.9f + t.right * 0.6f + Vector3.up * 1.55f;
                go.transform.LookAt(t.position + Vector3.up * 1.25f);
                var cam = go.AddComponent<Camera>();
                cam.fieldOfView = 55f;
                cam.nearClipPlane = 0.05f;
                go.AddComponent<HDAdditionalCameraData>();
                var rt = RenderTexture.GetTemporary(captureWidth, captureHeight, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                var request = new RenderPipeline.StandardRequest { destination = rt };
                for (int p = 0; p < 3; p++) RenderPipeline.SubmitRenderRequest(cam, request);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(captureWidth, captureHeight, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, captureWidth, captureHeight), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes(Path.Combine(_outDir, fileName), tex.EncodeToPNG());
                Destroy(tex);
                RenderTexture.ReleaseTemporary(rt);
                Note($"Captured {fileName} (third person)");
            }
            catch (Exception ex) { Note("Third-person capture failed: " + ex.Message); }
            finally { Destroy(go); }
        }

        private void Capture(string customFileName)
        {
            string tag = string.IsNullOrEmpty(customFileName) ? $"shot_{_shots++:00}_{Time.time:000}s.png" : customFileName;
            if (!Application.isBatchMode)
            {
                // Real game view (exposure history, post FX, UI) - exactly what a player sees.
                string shot = Path.Combine(_outDir, tag);
                ScreenCapture.CaptureScreenshot(shot);
                Note($"Captured {Path.GetFileName(shot)} (game view)");
                return;
            }
            var cam = _soldier != null && _soldier.ViewCamera != null && _soldier.ViewCamera.isActiveAndEnabled ? _soldier.ViewCamera : Camera.main;
            if (cam == null)
            {
                Note("Capture skipped: no active camera");
                return;
            }
            if (_soldier != null && !_soldier.IsDead && !KillcamDirector.IsPlaying && _soldier.CameraController != null && _soldier.CameraController.enabled)
                _soldier.CameraController.UpdateCamera();
            var rt = RenderTexture.GetTemporary(captureWidth, captureHeight, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var hd = cam.GetComponent<HDAdditionalCameraData>();
            var prevAa = hd != null ? hd.antialiasing : HDAdditionalCameraData.AntialiasingMode.None;
            try
            {
                if (hd != null) hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
                var request = new RenderPipeline.StandardRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(cam, request))
                {
                    int passes = _shots <= 1 ? 3 : 2;
                    for (int p = 0; p < passes; p++) RenderPipeline.SubmitRenderRequest(cam, request);
                }
                else
                {
                    var prev = cam.targetTexture;
                    cam.targetTexture = rt;
                    cam.Render();
                    cam.targetTexture = prev;
                }
                var prevActive = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(captureWidth, captureHeight, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, captureWidth, captureHeight), 0, 0);
                tex.Apply();
                RenderTexture.active = prevActive;
                string file = Path.Combine(_outDir, tag);
                File.WriteAllBytes(file, tex.EncodeToPNG());
                Destroy(tex);
                Note($"Captured {Path.GetFileName(file)} from {cam.name} at {cam.transform.position}");
            }
            catch (Exception ex)
            {
                Note("Capture failed: " + ex.Message);
            }
            finally
            {
                if (hd != null) hd.antialiasing = prevAa;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        private void WriteReport()
        {
            if (string.IsNullOrEmpty(_outDir)) return;
            var sb = new StringBuilder();
            sb.AppendLine("=== LR2 PLAYTEST ROBOT ===");
            var mm = MatchManager.Instance;
            if (mm != null)
            {
                sb.AppendLine($"Match: {mm.State} / {(mm.Settings != null ? mm.Settings.displayName : "?")}");
                foreach (var c in Combatant.All)
                    if (c != null) sb.AppendLine($"  {c.DisplayName,-12} K {c.stats.kills} D {c.stats.deaths} shots {c.stats.shotsFired} hits {c.stats.shotsHit} alive {c.IsAlive}");
            }
            else
            {
                sb.AppendLine($"Match: {_lastMatchSummary}");
                if (!string.IsNullOrEmpty(_lastParticipantsSummary)) sb.Append(_lastParticipantsSummary);
            }
            sb.AppendLine($"Screenshots: {_shots}   Errors: {_errors}");
            sb.AppendLine("--- timeline ---");
            sb.Append(_log);
            File.WriteAllText(Path.Combine(_outDir, "report.txt"), sb.ToString());
        }
    }
}
