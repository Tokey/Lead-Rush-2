using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LR2.AI;
using LR2.Combat;
using LR2.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace LR2.Characters
{
    /// <summary>
    /// Test harness for the first-person body, gun hold and weapon animations. Inert unless the game/editor is started
    /// with <c>-lr2PoseCheck</c> (e.g. through <c>LR2SmokeTest.RunBatch</c> without <c>-lr2Robot</c>). It freezes every other
    /// soldier, drives the local player through scripted poses (idle, look down/up, ADS, reload, pistol swap, walk,
    /// sprint) and saves first-person + third-person screenshots with alignment numbers to <c>Logs/LR2_PoseCheck</c>
    /// (or <c>-lr2OutDir</c>). A red marker shows the camera position in third-person shots.
    /// </summary>
    [DefaultExecutionOrder(1200)] // after RetargetProxy (60) and BodycamRig (1100): captures the final frame
    public class FirstPersonPoseCheck : MonoBehaviour, ISoldierInputSource
    {
        public static bool Requested => Array.IndexOf(Environment.GetCommandLineArgs(), "-lr2PoseCheck") >= 0;

        private const int Width = 1280, Height = 720;

        private enum View { FirstPerson, Front, Side, OverShoulder, HeadClose, Enemy }

        private struct Shot
        {
            public string name;
            public View view;
            public bool showHead;
        }

        private SoldierInput _cmd = SoldierInput.Empty;
        private SoldierController _soldier;
        private RetargetProxy _proxy;
        private SoldierController _enemy;
        private string _outDir;
        private int _errors;
        private bool _done;
        private readonly StringBuilder _log = new StringBuilder();
        private readonly Queue<Shot> _pending = new Queue<Shot>();
        private GameObject _marker, _eyeMarker;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!Requested || FindAnyObjectByType<FirstPersonPoseCheck>() != null) return;
            var go = new GameObject("[LR2 PoseCheck]");
            DontDestroyOnLoad(go);
            go.AddComponent<FirstPersonPoseCheck>();
        }

        public void GetInput(SoldierController soldier, ref SoldierInput input)
        {
            input = _cmd;
            _cmd.reload = false; // one-shots: hand them out for exactly one frame
            _cmd.weaponSlot = -1;
            _cmd.jump = false;
            _cmd.crouch = false;
            _cmd.prone = false;
        }

        private void Note(string s) => _log.AppendLine($"[{Time.time,7:0.00}] {s}");

        private void OnLog(string msg, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception) return;
            if (msg.Contains("PlasticSCM") || msg.Contains("unityplastic")) return; // editor package noise, not the game
            _errors++;
            if (_errors <= 20) Note($"{type}: {msg} | {(stack ?? "").Split('\n')[0]}");
        }

        private IEnumerator Start()
        {
            _outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "LR2_PoseCheck"));
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-lr2OutDir" && !string.IsNullOrWhiteSpace(args[i + 1]))
                    _outDir = Path.GetFullPath(Path.IsPathRooted(args[i + 1]) ? args[i + 1] : Path.Combine(Application.dataPath, "..", args[i + 1]));
            Directory.CreateDirectory(_outDir);
            foreach (var f in Directory.GetFiles(_outDir, "*.png")) File.Delete(f);
            Application.logMessageReceived += OnLog;
            Note("PoseCheck started");

            float waitUntil = Time.time + 20f;
            while (_soldier == null && Time.time < waitUntil)
            {
                var local = Combatant.FindLocalPlayer();
                _soldier = local != null ? local.GetComponent<SoldierController>() : null;
                yield return null;
            }
            if (_soldier == null) { Note("No local soldier found"); Finish(); yield break; }
            yield return Wait(3.5f); // spawn, countdown, first equip

            FreezeOthers();
            var pis = _soldier.GetComponent<PlayerInputSource>();
            if (pis != null) pis.enabled = false;
            _soldier.InputSource = this;
            _proxy = _soldier.GetComponent<RetargetProxy>();
            _cmd = SoldierInput.Empty;
            _cmd.absoluteLook = true;
            _cmd.aimYaw = OpenYaw();
            _cmd.aimPitch = 0f;
            Note($"Local soldier '{_soldier.name}' at {_soldier.transform.position}, facing yaw {_cmd.aimYaw:0}");
            yield return Wait(1.2f);

            LogDriverWeaponBones("idle");
            yield return Capture("fp_01_idle", "idle");
            yield return Capture("tp_01_front", null, View.Front, true);
            yield return Capture("tp_02_side", null, View.Side, true);
            yield return Capture("tp_03_overshoulder", null, View.OverShoulder, false);
            yield return Capture("tp_04_head_close", null, View.HeadClose, true);

            _cmd.aimPitch = 60f;
            yield return Wait(0.9f);
            yield return Capture("fp_02_lookdown", "look down 60");
            _cmd.aimPitch = -35f;
            yield return Wait(0.8f);
            yield return Capture("fp_03_lookup", "look up 35");
            _cmd.aimPitch = 0f;
            yield return Wait(0.6f);

            _cmd.aim = true;
            yield return Wait(1.0f);
            yield return Capture("fp_04_ads", "ADS rifle");
            yield return Capture("tp_05_ads_side", null, View.Side, true);
            _cmd.aim = false;
            yield return Wait(0.6f);

            // reload: spend a few rounds first so a tactical reload is allowed, then verify & photograph BOTH mag-out and charge-rack
            _cmd.fire = true;
            yield return Wait(0.3f);
            _cmd.fire = false;
            yield return Wait(0.4f);
            var akTf = _soldier.ActiveFirearm != null ? _soldier.ActiveFirearm.transform : null;
            var magTf = akTf != null ? FindDeep(akTf, "Mag") : null;
            var chargeTf = akTf != null ? FindDeep(akTf, "Charge") : null;
            Vector3 magRestLocal = magTf != null && akTf != null ? akTf.InverseTransformPoint(magTf.position) : Vector3.zero;
            Vector3 chargeRestLocal = chargeTf != null && akTf != null ? akTf.InverseTransformPoint(chargeTf.position) : Vector3.zero;
            float maxMagDisp = 0f, maxChargeDisp = 0f;

            void SampleReloadParts()
            {
                if (akTf == null) return;
                if (magTf != null) maxMagDisp = Mathf.Max(maxMagDisp, Vector3.Distance(magRestLocal, akTf.InverseTransformPoint(magTf.position)));
                if (chargeTf != null) maxChargeDisp = Mathf.Max(maxChargeDisp, Vector3.Distance(chargeRestLocal, akTf.InverseTransformPoint(chargeTf.position)));
            }
            IEnumerator WaitAndSample(float sec)
            {
                float end = Time.time + sec;
                while (Time.time < end) { SampleReloadParts(); yield return null; }
            }

            _cmd.reload = true;
            yield return WaitAndSample(0.3f);
            DiagnoseReload("+0.3s");
            yield return WaitAndSample(0.55f); // ~0.85s real (~0.72s clip time): left hand grasps & detaches AK-12 mag visibly in view
            yield return Capture("fp_05_reload_a", "reload mag out (+0.85s)");
            yield return Capture("tp_06_reload_front", null, View.Front, true);
            LogDriverWeaponBones("reload +0.85s");
            yield return WaitAndSample(2.65f); // ~3.55s real (~3.02s clip time): AK-12 charging handle pulled back
            yield return Capture("fp_06_reload_b", "reload charge rack (+3.55s)");
            DiagnoseReload("+3.55s");
            yield return WaitAndSample(1.15f);
            Note($"RELOAD-PARTS-AK12 magMaxDisp={maxMagDisp:0.000}m chargeMaxDisp={maxChargeDisp:0.000}m (expect mag>=0.05m, charge>=0.03m)");
            if (magTf != null && maxMagDisp < 0.05f) Note("ERROR [PoseCheck] Mag did not detach during AK-12 reload!");
            if (chargeTf != null && maxChargeDisp < 0.02f) Note("ERROR [PoseCheck] Charging handle (Charge) did not cycle during AK-12 reload!");

            // weapon swap to the sidearm and back
            _cmd.weaponSlot = 1;
            yield return Wait(0.3f);
            yield return Capture("fp_07_swap_mid", "swap to slot 2 +0.3s");
            yield return Wait(1.1f);
            yield return Capture("fp_08_pistol", "pistol idle");
            yield return Capture("tp_07_pistol_front", null, View.Front, true);
            _cmd.aim = true;
            yield return Wait(0.8f);
            yield return Capture("fp_09_pistol_ads", "pistol ADS");
            _cmd.aim = false;
            yield return Wait(0.5f);
            _cmd.weaponSlot = 0;
            yield return Wait(1.4f);
            yield return Capture("fp_10_rifle_back", "rifle back");

            // movement
            _cmd.move = new Vector2(0f, 1f);
            yield return Wait(1.0f);
            yield return MeasureGait("walk", 1.6f);
            yield return Capture("fp_11_walk", "walk");
            yield return Capture("tp_08_walk_side", null, View.Side, true);
            _cmd.sprint = true;
            yield return Wait(1.0f);
            yield return MeasureGait("sprint", 1.6f);
            yield return Capture("fp_12_sprint", "sprint");
            yield return Capture("tp_09_sprint_side", null, View.Side, true);
            _cmd.sprint = false;
            _cmd.move = Vector2.zero;
            yield return Wait(0.8f);

            // Reset to open floor lane after walking/sprinting so stances and the enemy aren't against the perimeter wall.
            _soldier.Teleport(new Vector3(24f, 0.05f, 0f), OpenYaw());
            yield return Wait(0.25f);

            // crouch, prone, and jump stances
            _cmd.crouch = true;
            yield return null;
            _cmd.crouch = false;
            yield return Wait(0.6f);
            yield return Capture("fp_14_crouch", $"crouch ({_soldier.Movement?.Stance})");
            yield return Capture("tp_11_crouch_side", null, View.Side, true);

            // Exit crouch to standing cleanly, then test full prone transition
            _cmd.crouch = true;
            yield return null;
            _cmd.crouch = false;
            yield return Wait(0.45f);
            _cmd.prone = true;
            yield return null;
            _cmd.prone = false;
            yield return Wait(1.75f);
            yield return Capture("fp_15_prone", $"prone ({_soldier.Movement?.Stance})");
            yield return Capture("tp_12_prone_side", null, View.Side, true);

            _cmd.jump = true;
            yield return null;
            _cmd.jump = false;
            yield return Wait(1.25f);
            _cmd.jump = true;
            yield return null;
            _cmd.jump = false;
            yield return Wait(0.22f);
            yield return Capture("fp_16_jump", $"jump mid-air ({_soldier.Movement?.State})");
            yield return Wait(1.2f);

            // the enemy soldier (third person: retarget, hips alignment, gun hold)
            if (_enemy != null)
            {
                Vector3 fwd = Quaternion.Euler(0f, _cmd.aimYaw, 0f) * Vector3.forward;
                _enemy.Teleport(_soldier.transform.position + fwd * 4.5f, _cmd.aimYaw + 180f);
                yield return Wait(0.8f);
                yield return Capture("fp_13_enemy", "enemy 4.5 m ahead");
                yield return Capture("tp_10_enemy_close", null, View.Enemy, false);
                var ep = _enemy.GetComponent<RetargetProxy>();
                if (ep != null)
                    Note($"ENEMY proxy rawHipsDelta={ep.RawHipsDelta:0.000} rawHeadDelta={ep.RawHeadDelta.magnitude:0.000} shift={ep.AppliedShift} " +
                         $"handErr L={ep.HandErrorLeft:0.000} R={ep.HandErrorRight:0.000} firstPerson={ep.FirstPersonActive}");
            }
            Finish();
        }

        /// <summary>
        /// Foot-slide probe on the DRIVER (pure animation): while moving forward, the lower foot is the planted one, so its
        /// world speed along the move direction is the slide. Natural clip speed = (move speed - slide) / LocoSpeed;
        /// set SoldierMovement's *AnimReferenceSpeed to that value for zero foot sliding.
        /// </summary>
        private IEnumerator MeasureGait(string label, float seconds)
        {
            var drv = _proxy != null ? _proxy.Driver : null;
            var lf = drv != null ? drv.GetBoneTransform(HumanBodyBones.LeftFoot) : null;
            var rf = drv != null ? drv.GetBoneTransform(HumanBodyBones.RightFoot) : null;
            if (lf == null || rf == null || _soldier.Movement == null) yield break;
            Vector3 pl = lf.position, pr = rf.position;
            var hl = new List<float>(); var hr = new List<float>();
            var sl = new List<float>(); var sr = new List<float>();
            float speed = 0f, loco = 0f;
            int n = 0;
            float end = Time.time + seconds;
            yield return null;
            while (Time.time < end)
            {
                float dt = Time.deltaTime;
                if (dt > 1e-4f)
                {
                    Vector3 move = _soldier.Movement.Velocity;
                    move.y = 0f;
                    float vm = move.magnitude;
                    if (vm > 0.2f)
                    {
                        move /= vm;
                        float ground = _soldier.transform.position.y;
                        hl.Add(lf.position.y - ground); sl.Add(Vector3.Dot((lf.position - pl) / dt, move));
                        hr.Add(rf.position.y - ground); sr.Add(Vector3.Dot((rf.position - pr) / dt, move));
                        speed += vm;
                        loco += drv.GetFloat("LocoSpeed");
                        n++;
                    }
                }
                pl = lf.position;
                pr = rf.position;
                yield return null;
            }
            if (n == 0) { Note($"GAIT [{label}] not moving"); yield break; }
            speed /= n; loco /= n;
            // stance = foot within 1.5 cm of its lowest point in the window (planted, not swinging or airborne)
            float StanceSlide(List<float> h, List<float> s, out int count)
            {
                float min = float.MaxValue, sum = 0f;
                foreach (var v in h) min = Mathf.Min(min, v);
                count = 0;
                for (int i = 0; i < h.Count; i++)
                    if (h[i] < min + 0.015f) { sum += s[i]; count++; }
                return count > 0 ? sum / count : 0f;
            }
            float slideL = StanceSlide(hl, sl, out int cl), slideR = StanceSlide(hr, sr, out int cr);
            float slide = cl + cr > 0 ? (slideL * cl + slideR * cr) / (cl + cr) : 0f;
            float natural = loco > 0.01f ? (speed - slide) / loco : 0f;
            Note($"GAIT [{label}] move={speed:0.00} m/s LocoSpeed={loco:0.00} stanceFootSlide={slide:0.00} m/s (L {slideL:0.00} n{cl}, R {slideR:0.00} n{cr}) " +
                 $"-> clip natural speed ~{natural:0.00} m/s (state {_soldier.Movement.State}, {n} frames)");
        }

        /// <summary>Why does (or doesn't) the reload animation show? Clip, playables controller, layer weight inputs, curves.</summary>
        private void DiagnoseReload(string label)
        {
            var sb = new StringBuilder($"RELOAD-DIAG [{label}]");
            var fa = _soldier.ActiveFirearm;
            const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
            if (fa != null)
            {
                var t = fa.GetType();
                object clip = t.GetField("reloadClip", F)?.GetValue(fa);
                object runtime = t.GetField("_reloadRuntime", F)?.GetValue(fa);
                object playables = t.GetField("_playables", F)?.GetValue(fa);
                var clipObj = clip as UnityEngine.Object;
                sb.Append($" gun={fa.name} reloading={fa.IsReloading} clip={(clipObj != null ? clipObj.name : "NULL")} runtimeCopy={(runtime as UnityEngine.Object != null)} playables={(playables != null ? playables.GetType().Name : "NULL")}");
                var animClip = clip?.GetType().GetField("clip", F)?.GetValue(clip) as AnimationClip;
                sb.Append($" animClip={(animClip != null ? animClip.name + " " + animClip.length.ToString("0.00") + "s" : "NULL")}");
            }
            var ui = _soldier.UserInput;
            if (ui != null)
            {
                int idx = ui.GetPropertyIndex(KINEMATION.FPSAnimationFramework.Runtime.Core.FPSANames.PlayablesWeight);
                sb.Append($" input.PlayablesWeight={(idx >= 0 ? ui.GetValue<float>(idx).ToString("0.00") : "missing")}");
            }
            if (_soldier.Animator != null) sb.Append($" animator.FullBodyWeight={_soldier.Animator.GetFloat("FullBodyWeight"):0.00}");
            var pc = _soldier.Playables;
            if (pc != null)
            {
                sb.Append($" controller={pc.GetType().Name} graphPlaying={pc.IsPlaying()}");
                foreach (var curve in new[] { "MaskAttachHand", "WeaponBoneWeight", "MaskLeftHand", "Overlay" })
                {
                    try { sb.Append($" curve.{curve}={pc.GetCurveValue(curve):0.00}"); } catch (Exception) { }
                }
                var mixField = pc.GetType().GetField("_masterMixer", F);
                if (mixField != null && mixField.GetValue(pc) is UnityEngine.Animations.AnimationMixerPlayable mix
                    && UnityEngine.Playables.PlayableExtensions.IsValid(mix) && UnityEngine.Playables.PlayableExtensions.GetInputCount(mix) > 1)
                    sb.Append($" masterMixer.input1Weight={UnityEngine.Playables.PlayableExtensions.GetInputWeight(mix, 1):0.00}");
                var cw = pc.GetType().GetField("_controllerWeight", F);
                if (cw != null) sb.Append($" controllerWeight={cw.GetValue(pc)}");
            }
            Note(sb.ToString());
            LogDriverWeaponBones(label);
        }

        /// <summary>Animated source bones (what the clip writes) vs procedural results (what the layers output), camera space.</summary>
        private void LogDriverWeaponBones(string label)
        {
            var drv = _proxy != null ? _proxy.Driver : null;
            var cam = _soldier.ViewCamera;
            if (drv == null || cam == null) return;
            var sb = new StringBuilder($"WEAPON-BONES [{label}] (camera space)");
            foreach (var n in new[] { "WeaponBone", "IK_Hand_Left", "IK_Hand_Right", "IK WeaponBone", "Left_Hand", "Right_Hand" })
            {
                var t = FindDeep(drv.transform, n);
                if (t == null) { sb.Append($" {n}=missing"); continue; }
                Vector3 lp = cam.transform.InverseTransformPoint(t.position);
                sb.Append($"\n      {n,-14} cam({lp.x,6:0.000},{lp.y,6:0.000},{lp.z,6:0.000}) local({t.localPosition.x:0.000},{t.localPosition.y:0.000},{t.localPosition.z:0.000}) localRot{t.localEulerAngles}");
            }
            Note(sb.ToString());
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform c in root)
            {
                if (c.name.StartsWith("SWAT_Visual")) continue;
                var r = FindDeep(c, name);
                if (r != null) return r;
            }
            return null;
        }

        private static IEnumerator Wait(float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end) yield return null;
        }

        private IEnumerator Capture(string name, string metricsLabel, View view = View.FirstPerson, bool showHead = false)
        {
            if (metricsLabel != null) Metrics(metricsLabel);
            _pending.Enqueue(new Shot { name = name, view = view, showHead = showHead });
            while (_pending.Count > 0) yield return null; // LateUpdate renders it at the end of this frame
        }

        private void LateUpdate()
        {
            while (_pending.Count > 0) Render(_pending.Dequeue());
        }

        private void FreezeOthers()
        {
            foreach (var c in Combatant.All)
            {
                if (c == null) continue;
                var sc = c.GetComponent<SoldierController>();
                if (sc == null || sc == _soldier) continue;
                var bot = sc.GetComponent<BotController>();
                if (bot != null) bot.enabled = false;
                sc.ControlsEnabled = false;
                if (_enemy == null) _enemy = sc;
            }
            Note($"Froze other soldiers (enemy: {(_enemy != null ? _enemy.name : "none")})");
        }

        /// <summary>Yaw with the most open space at eye height, so third-person cameras don't land inside walls.</summary>
        private float OpenYaw()
        {
            float best = _soldier.transform.eulerAngles.y, bestDist = -1f;
            Vector3 eye = _soldier.transform.position + Vector3.up * 1.5f;
            for (int i = 0; i < 16; i++)
            {
                float yaw = i * 22.5f;
                Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                float d = Physics.Raycast(eye, dir, out var hit, 30f, LR2Layers.EnvironmentMask, QueryTriggerInteraction.Ignore) ? hit.distance : 30f;
                float back = Physics.Raycast(eye, -dir, out var hb, 6f, LR2Layers.EnvironmentMask, QueryTriggerInteraction.Ignore) ? hb.distance : 6f;
                float score = d + back * 0.5f;
                if (score > bestDist) { bestDist = score; best = yaw; }
            }
            return best;
        }

        private void Metrics(string label)
        {
            var cam = _soldier.ViewCamera;
            var sb = new StringBuilder($"METRICS [{label}]");
            if (cam != null)
                sb.Append($" camPos={cam.transform.position} camFwd={cam.transform.forward} vFov={cam.fieldOfView:0.0} aspect={cam.aspect:0.00} near={cam.nearClipPlane:0.000}");
            if (_proxy != null)
            {
                sb.Append($"\n    proxy: rawHipsDelta={_proxy.RawHipsDelta:0.000} rawHeadDelta={_proxy.RawHeadDelta} ({_proxy.RawHeadDelta.magnitude:0.000}) " +
                          $"shift={_proxy.AppliedShift} firstPerson={_proxy.FirstPersonActive} handErr L={_proxy.HandErrorLeft:0.000} R={_proxy.HandErrorRight:0.000}");
                var v = _proxy.Visual;
                if (cam != null && v != null)
                {
                    sb.Append("\n    visual bones in camera space (x right, y up, z fwd; in view = z>near and inside frustum):");
                    foreach (var b in new[] { HumanBodyBones.Head, HumanBodyBones.Neck, HumanBodyBones.UpperChest, HumanBodyBones.Chest,
                                 HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm,
                                 HumanBodyBones.LeftHand, HumanBodyBones.RightHand })
                    {
                        var t = v.GetBoneTransform(b);
                        if (t == null) continue;
                        Vector3 lp = cam.transform.InverseTransformPoint(t.position);
                        Vector3 vp = cam.WorldToViewportPoint(t.position);
                        bool inView = vp.z > cam.nearClipPlane && vp.x > 0f && vp.x < 1f && vp.y > 0f && vp.y < 1f;
                        sb.Append($"\n      {b,-14} ({lp.x,6:0.000},{lp.y,6:0.000},{lp.z,6:0.000}){(inView ? "  IN VIEW" : "")}");
                    }
                    Vector3 eyeLp = cam.transform.InverseTransformPoint(_proxy.VisualEyePosition);
                    sb.Append($"\n      visual EYE     ({eyeLp.x,6:0.000},{eyeLp.y,6:0.000},{eyeLp.z,6:0.000})");
                    var d = _proxy.Driver;
                    if (d != null)
                    {
                        sb.Append("\n    DRIVER bones in camera space:");
                        foreach (var b in new[] { HumanBodyBones.Head, HumanBodyBones.Neck, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm, HumanBodyBones.Hips })
                        {
                            var t = d.GetBoneTransform(b);
                            if (t == null) continue;
                            Vector3 lp = cam.transform.InverseTransformPoint(t.position);
                            sb.Append($"\n      {b,-14} ({lp.x,6:0.000},{lp.y,6:0.000},{lp.z,6:0.000})");
                        }
                        var vh = v.GetBoneTransform(HumanBodyBones.Hips);
                        var dh = d.GetBoneTransform(HumanBodyBones.Hips);
                        if (vh != null && dh != null)
                            sb.Append($"\n      hips height: driver {dh.position.y - _soldier.transform.position.y:0.000}  visual {vh.position.y - _soldier.transform.position.y:0.000}  (camera {cam.transform.position.y - _soldier.transform.position.y:0.000})");
                    }
                    foreach (var b in new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot })
                    {
                        var t = v.GetBoneTransform(b);
                        if (t == null) continue;
                        float h = Physics.Raycast(t.position + Vector3.up * 0.5f, Vector3.down, out var hit, 3f, LR2Layers.EnvironmentMask, QueryTriggerInteraction.Ignore)
                            ? t.position.y - hit.point.y : -1f;
                        sb.Append($"\n      {b,-14} height above ground {h:0.000} m");
                    }
                }
            }
            var gun = _soldier.ActiveItem;
            if (gun != null && cam != null)
            {
                Vector3 gp = cam.transform.InverseTransformPoint(gun.transform.position);
                sb.Append($"\n    active item {gun.name} at camera-space ({gp.x:0.000},{gp.y:0.000},{gp.z:0.000}) aiming={_soldier.IsAiming} aimBlend={_soldier.AimBlend:0.00} reloading={_soldier.IsReloading} switching={_soldier.IsSwitchingWeapon}");
            }
            sb.Append($"\n    move state={_soldier.Movement?.State} speed={(_soldier.Movement != null ? _soldier.Movement.Speed : 0f):0.00} pitch={_soldier.Pitch:0.0}");
            Note(sb.ToString());
        }

        private void Render(Shot shot)
        {
            var fp = _soldier != null ? _soldier.ViewCamera : null;
            if (fp == null) { Note($"skip {shot.name}: no view camera"); return; }
            if (shot.view == View.FirstPerson) { RenderCamera(fp, shot.name); return; }

            var t = _soldier.transform;
            Vector3 eye = fp.transform.position;
            Vector3 pos, look;
            switch (shot.view)
            {
                case View.Front: pos = t.position + t.forward * 2.3f + t.right * 0.55f + Vector3.up * 1.55f; look = t.position + Vector3.up * 1.3f; break;
                case View.Side: pos = t.position + t.right * 2.4f + t.forward * 0.4f + Vector3.up * 1.5f; look = t.position + t.forward * 0.25f + Vector3.up * 1.3f; break;
                case View.OverShoulder: pos = t.position - t.forward * 1.5f + t.right * 0.55f + Vector3.up * 2.0f; look = t.position + t.forward * 1.6f + Vector3.up * 1.35f; break;
                case View.HeadClose: pos = eye + t.right * 0.85f + t.forward * 0.25f + Vector3.up * 0.05f; look = eye - Vector3.up * 0.12f; break;
                default:
                    if (_enemy == null) return;
                    var e = _enemy.transform;
                    pos = e.position + e.forward * 2.0f + e.right * 0.9f + Vector3.up * 1.6f; look = e.position + Vector3.up * 1.25f; break;
            }
            if (_marker == null)
            {
                _marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _marker.name = "PoseCheck_CameraMarker";
                Destroy(_marker.GetComponent<Collider>());
                _marker.transform.localScale = Vector3.one * 0.025f;
                var mat = new Material(Shader.Find("HDRP/Unlit")) { color = Color.red };
                _marker.GetComponent<Renderer>().sharedMaterial = mat;
            }
            _marker.SetActive(shot.view != View.Enemy);
            _marker.transform.position = eye;
            if (_eyeMarker == null)
            {
                _eyeMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _eyeMarker.name = "PoseCheck_EyeMarker";
                Destroy(_eyeMarker.GetComponent<Collider>());
                _eyeMarker.transform.localScale = Vector3.one * 0.018f;
                _eyeMarker.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("HDRP/Unlit")) { color = Color.green };
            }
            _eyeMarker.SetActive(shot.view != View.Enemy && _proxy != null);
            if (_proxy != null) _eyeMarker.transform.position = _proxy.VisualEyePosition;
            if (shot.showHead) _soldier.SetHeadHidden(false);

            var go = new GameObject("PoseCheckCam");
            try
            {
                go.transform.position = pos;
                go.transform.LookAt(look);
                var cam = go.AddComponent<Camera>();
                cam.fieldOfView = shot.view == View.HeadClose ? 40f : 52f;
                cam.nearClipPlane = 0.02f;
                go.AddComponent<HDAdditionalCameraData>();
                RenderCamera(cam, shot.name);
            }
            finally
            {
                Destroy(go);
                if (shot.showHead) _soldier.SetHeadHidden(true);
                _marker.SetActive(false);
                if (_eyeMarker != null) _eyeMarker.SetActive(false);
            }
        }

        private void RenderCamera(Camera cam, string name)
        {
            var rt = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            try
            {
                var request = new RenderPipeline.StandardRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(cam, request))
                    for (int p = 0; p < 3; p++) RenderPipeline.SubmitRenderRequest(cam, request);
                else
                {
                    var prev = cam.targetTexture;
                    cam.targetTexture = rt;
                    cam.Render();
                    cam.targetTexture = prev;
                }
                var prevActive = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                tex.Apply();
                RenderTexture.active = prevActive;
                File.WriteAllBytes(Path.Combine(_outDir, name + ".png"), tex.EncodeToPNG());
                Destroy(tex);
                Note($"Captured {name}.png");
            }
            catch (Exception ex) { Note($"Capture {name} failed: {ex.Message}"); }
            finally { RenderTexture.ReleaseTemporary(rt); }
        }

        private void Finish()
        {
            if (_done) return;
            _done = true;
            var sb = new StringBuilder("=== LR2 POSE CHECK ===\n");
            sb.AppendLine($"Errors (game): {_errors}");
            sb.Append(_log);
            File.WriteAllText(Path.Combine(_outDir, "report.txt"), sb.ToString());
            Debug.Log($"[LR2] PoseCheck finished: {_outDir} (errors {_errors})");
        }

        private void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
            Finish();
        }
    }
}
