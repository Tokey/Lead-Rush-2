using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using LR2.Audio;
using LR2.Characters;
using LR2.Combat;
using LR2.Core;
using LR2.FX;
using LR2.Game;
using LR2.UI;
using LR2.Weapons;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace LR2.World
{
    /// <summary>
    /// Runtime automated QA bot activated by <c>-lr2QaBot</c> (run across maps via <c>LR2.EditorTools.LR2QABot.RunBatch</c>).
    /// Measures and asserts visibility/contrast at spawns and cover points, bloom/muzzle-flash glare, reload angle
    /// idle and while walking, ADS/hipfire recoil and weapon TTK, footstep stride timing and mix headroom beneath gunfire
    /// and adaptive music, faster prone speed/lock, 3D gun blood attachment, killcam replay hitmarkers, and zero errors.
    /// </summary>
    public class LR2QABotRunner : MonoBehaviour, ISoldierInputSource
    {
        public static bool Requested => Array.IndexOf(Environment.GetCommandLineArgs(), "-lr2QaBot") >= 0;

        private SoldierInput _cmd = SoldierInput.Empty;
        private readonly StringBuilder _report = new StringBuilder();
        private readonly List<string> _failures = new List<string>();
        private int _runtimeErrors;
        private int _footsteps;
        private float _distAccum;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!Requested || FindAnyObjectByType<LR2QABotRunner>() != null) return;
            var go = new GameObject("[LR2 QABot]");
            DontDestroyOnLoad(go);
            go.AddComponent<LR2QABotRunner>();
        }

        public void GetInput(SoldierController soldier, ref SoldierInput input)
        {
            input = _cmd;
            _cmd.reload = false;
            _cmd.prone = false;
            _cmd.crouch = false;
            _cmd.jump = false;
            _cmd.weaponSlot = -1;
        }

        private void OnLog(string msg, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            if (msg.Contains("PlasticSCM") || msg.Contains("unityplastic")) return;
            _runtimeErrors++;
            if (_runtimeErrors <= 15) _report.AppendLine($"  ERR: {type} {msg}");
        }

        private void Check(bool cond, string label, string detail)
        {
            _report.AppendLine($"  [{(cond ? "PASS" : "FAIL")}] {label}: {detail}");
            if (!cond) _failures.Add($"{label}: {detail}");
        }

        private void CheckExtra(string formattedLine)
        {
            if (string.IsNullOrEmpty(formattedLine)) return;
            bool pass = formattedLine.StartsWith("PASS ", StringComparison.Ordinal);
            string rest = formattedLine.StartsWith("PASS ", StringComparison.Ordinal) || formattedLine.StartsWith("FAIL ", StringComparison.Ordinal)
                ? formattedLine.Substring(5)
                : formattedLine;
            int colon = rest.IndexOf(':');
            string label = colon > 0 ? rest.Substring(0, colon).Trim() : rest.Trim();
            string detail = colon > 0 ? rest.Substring(colon + 1).Trim() : "";
            Check(pass, label, detail);
        }

        private static string OutDir()
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "LR2_QABot"));
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-lr2OutDir" && !string.IsNullOrWhiteSpace(args[i + 1]))
                    dir = Path.GetFullPath(Path.IsPathRooted(args[i + 1]) ? args[i + 1] : Path.Combine(Application.dataPath, "..", args[i + 1]));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private IEnumerator Start()
        {
            if (Application.isBatchMode) AudioListener.volume = 0f;
            Application.logMessageReceived += OnLog;
            string outDir = OutDir();
            string sceneName = SceneManager.GetActiveScene().name;
            _report.AppendLine($"=== LR2 QA BOT ({sceneName}) ===");

            // Pause-free deterministic test setup
            if (sceneName.Contains("MainMenu"))
            {
                yield return new WaitForSecondsRealtime(1.5f);
                yield return AuditMenuScene(outDir, sceneName);
                FinishReport(outDir, sceneName);
                yield break;
            }

            float waitUntil = Time.time + 18f;
            SoldierController player = null;
            while (player == null && Time.time < waitUntil)
            {
                var lp = Combatant.FindLocalPlayer();
                player = lp != null ? lp.GetComponent<SoldierController>() : null;
                yield return null;
            }
            if (player == null)
            {
                Check(false, "Spawn", "Local player never spawned");
                FinishReport(outDir, sceneName);
                yield break;
            }

            // Wait out match countdown if needed
            var mm = MatchManager.Instance;
            float cdWait = Time.time + 8f;
            while (mm != null && mm.State == MatchState.Countdown && Time.time < cdWait) yield return null;
            yield return new WaitForSeconds(0.5f);

            // Take input control and disable enemy AI temporarily for clean measurements
            var pis = player.GetComponent<PlayerInputSource>();
            if (pis != null) pis.enabled = false;
            player.InputSource = this;
            foreach (var bot in FindObjectsByType<AI.BotController>())
                if (bot != null) bot.enabled = false;

            yield return AuditVolumeAndVisibility(player, outDir, sceneName);

            if (sceneName.Contains("Saltworks"))
            {
                yield return AuditMuzzleFlashGlare(player);
                yield return AuditRecoilAndWeaponTTK(player);
                yield return AuditReloadIdleAndWalk(player);
                yield return AuditFootstepsAndProne(player);
                yield return AuditGunBloodAdhesion(player);
                yield return AuditBulletFlybySfx(player);
                yield return AuditProgressiveMusicAndHeadroom(player);
                yield return AuditWeatherThunderRainWetness(player);
                yield return AuditKillcamHitmarkersAndAudio();
                yield return AuditCtfAndAdaptiveBot(player);
            }

            FinishReport(outDir, sceneName);
        }

        private IEnumerator AuditMenuScene(string outDir, string sceneName)
        {
            var dir = LR2MusicDirector.Instance;
            Check(dir != null, "MusicDirector.MenuBoot", dir != null ? $"state={dir.StateLabel} menuVol={dir.MenuVolume:0.000}" : "null");
            if (dir != null)
                Check(dir.IsMenuMode && dir.MenuVolume > 0.01f, "MusicDirector.MenuPlaying", $"isMenu={dir.IsMenuMode} vol={dir.MenuVolume:0.000}");

            var common = LR2CommonSounds.Instance;
            Check(common != null, "CommonSounds", common != null ? "loaded" : "missing");
            if (common != null)
                Check(common.uiHover.volume <= 0.20f && common.uiClick.volume <= 0.25f, "MenuSFX.NotExtreme",
                    $"hover={common.uiHover.volume:0.00} click={common.uiClick.volume:0.00} back={common.uiBack.volume:0.00}");

            // B19: Main-menu soldier must be the masked SWAT soldier (M_FaceMask_GhostSkull) with mannequin driver hidden
            var hero = GameObject.Find("HeroSoldier");
            bool hasHero = hero != null;
            bool hasSkullMask = false;
            bool mannequinHidden = true;
            if (hero != null)
            {
                var swatVis = hero.transform.Find("SWAT_Visual");
                foreach (var smr in hero.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (swatVis != null && !smr.transform.IsChildOf(swatVis))
                    {
                        if (smr.enabled) mannequinHidden = false;
                        continue;
                    }
                    foreach (var mat in smr.sharedMaterials)
                        if (mat != null && mat.name.Contains("FaceMask_GhostSkull")) hasSkullMask = true;
                }
            }
            Check(hasHero && hasSkullMask && mannequinHidden, "MainMenu.HeroMaskedSwat",
                $"hero={hasHero} skullMask={hasSkullMask} driverHidden={mannequinHidden}");

            // B31: Starter title screen support before Main Menu
            Check(LR2LoadingIntro.HasTitlePromptSupport, "Intro.StarterTitleScreenSupported", "supported=True");

            // Capture main-menu visual screenshot for Pass 2 inspection
            var cam = Camera.main;
            if (cam != null)
            {
                yield return null;
                var tex = CaptureCamera(cam, 960, 540);
                File.WriteAllBytes(Path.Combine(outDir, $"{sceneName}_vis.png"), tex.EncodeToPNG());
                Destroy(tex);
            }
        }

        private IEnumerator AuditVolumeAndVisibility(SoldierController player, string outDir, string sceneName)
        {
            // Check HDRP Volume lighting parameters
            Volume globalVol = null;
            foreach (var v in FindObjectsByType<Volume>())
                if (v != null && v.isGlobal && v.priority <= 1f && v.sharedProfile != null && v.sharedProfile.Has<Exposure>()) { globalVol = v; break; }

            Check(globalVol != null, "HDRP.GlobalVolume", globalVol != null ? globalVol.sharedProfile.name : "missing");
            if (globalVol != null)
            {
                var prof = globalVol.sharedProfile;
                if (prof.TryGet<Exposure>(out var ex))
                    Check(ex.adaptationMode.value == AdaptationMode.Progressive && ex.limitMin.value <= 7.2f,
                        "Exposure.ProgressiveFloor", $"mode={ex.adaptationMode.value} limitMin={ex.limitMin.value:0.0} limitMax={ex.limitMax.value:0.0}");
                if (prof.TryGet<IndirectLightingController>(out var ilc))
                    Check(ilc.indirectDiffuseLightingMultiplier.value >= 1.25f, "IndirectLightingFloor", $"diffuseMult={ilc.indirectDiffuseLightingMultiplier.value:0.00}");
                if (prof.TryGet<Bloom>(out var bl))
                    Check(bl.threshold.value >= 2.5f && bl.intensity.value <= 0.12f && bl.dirtIntensity.value <= 0.01f,
                        "Bloom.AntiGlare", $"threshold={bl.threshold.value:0.00} intensity={bl.intensity.value:0.00} dirt={bl.dirtIntensity.value:0.00}");
            }

            // Measure spawn + cover/enemy luminance contrast using ViewCamera
            Camera cam = player.ViewCamera;
            SoldierController enemy = null;
            foreach (var c in Combatant.All)
                if (c != null && c != player.Combatant && c.IsAlive && c.Team != TeamId.Hostile) { enemy = c.GetComponent<SoldierController>(); if (enemy != null) break; }

            var spots = new List<Transform>();
            bool HasRoomAround(Vector3 p)
            {
                if (IsConfinedOrRoofed(p)) return false;
                if (Physics.CheckCapsule(p + Vector3.up * 0.55f, p + Vector3.up * 1.65f, 0.45f, LR2Layers.EnvironmentMask, QueryTriggerInteraction.Ignore))
                    return false;
                int openDirs = 0;
                for (int k = 0; k < 4; k++)
                {
                    Vector3 d = Quaternion.Euler(0f, k * 90f, 0f) * Vector3.forward;
                    if (!Physics.Raycast(p + Vector3.up * 1.2f, d, 2.2f, LR2Layers.EnvironmentMask, QueryTriggerInteraction.Ignore))
                        openDirs++;
                }
                return openDirs >= 3;
            }
            foreach (var fa in FlagAnchor.All)
                if (fa != null && spots.Count < 3 && fa.transform.position.y < 1.8f && HasRoomAround(fa.transform.position))
                    spots.Add(fa.transform);
            foreach (var sp in FindObjectsByType<SpawnPoint>())
                if (sp != null && spots.Count < 3 && sp.transform.position.y < 1.8f && HasRoomAround(sp.transform.position))
                    spots.Add(sp.transform);
            var tacPts = FindObjectsByType<LR2.AI.CoverPoint>();
            foreach (var cp in tacPts)
                if (cp != null && spots.Count < 3 && cp.transform.position.y < 1.8f && HasRoomAround(cp.transform.position))
                    spots.Add(cp.transform);
            if (spots.Count == 0 && !IsConfinedOrRoofed(player.transform.position)) spots.Add(player.transform);
            if (spots.Count == 0) spots.Add(player.transform);

            float worstMean = 1f, worstContrast = 1f;
            for (int i = 0; i < spots.Count; i++)
            {
                Vector3 basePos = spots[i] == player.transform ? player.transform.position : spots[i].position;
                // Aim down the longest clear corridor from this spot so walls/cover never pin the camera or target
                Vector3 pos = basePos;
                float bestYaw = player.Yaw;
                float bestDist = -1f;
                for (int k = 0; k < 16; k++)
                {
                    float y = k * 22.5f;
                    Vector3 d = Quaternion.Euler(0f, y, 0f) * Vector3.forward;
                    float dist = Physics.Raycast(basePos + Vector3.up * 1.4f, d, out var rh, 24f, LR2Layers.EnvironmentMask, QueryTriggerInteraction.Ignore) ? rh.distance : 24f;
                    if (dist > bestDist) { bestDist = dist; bestYaw = y; }
                }
                Vector3 lookDir = Quaternion.Euler(0f, bestYaw, 0f) * Vector3.forward;
                Vector3 lookRight = Vector3.Cross(Vector3.up, lookDir).normalized;

                _cmd = new SoldierInput { absoluteLook = true, aimYaw = bestYaw, aimPitch = 0f };
                player.Movement.Teleport(pos);
                // Place test enemy slightly left-of-center at eye elevation so the right-handed rifle model never occludes them
                if (enemy != null && cam != null)
                {
                    Vector3 enemyPos = pos + lookDir * Mathf.Clamp(bestDist * 0.55f, 4.2f, 7.5f);
                    float viewYaw = bestYaw;
                    bool foundClear = false;
                    foreach (float lat in new[] { -0.95f, -1.45f, 0f })
                    {
                        Vector3 cand = pos + lookDir * Mathf.Clamp(bestDist * 0.55f, 4.2f, 7.2f) + lookRight * lat;
                        bool sameFloor = Physics.Raycast(cand + Vector3.up * 2.5f, Vector3.down, out var flHit, 5f, LR2Layers.EnvironmentMask, QueryTriggerInteraction.Ignore)
                            && Mathf.Abs(flHit.point.y - pos.y) <= 0.85f;
                        if (sameFloor && !Physics.Linecast(pos + Vector3.up * 1.4f, cand + Vector3.up * 1.4f, LR2Layers.EnvironmentMask, QueryTriggerInteraction.Ignore))
                        {
                            enemyPos = flHit.point + Vector3.up * 0.05f;
                            if (Mathf.Approximately(lat, 0f)) viewYaw = bestYaw + 9f; // turn rifle slightly right of centered target
                            foundClear = true;
                            break;
                        }
                    }
                    if (!foundClear)
                    {
                        enemyPos = pos + lookDir * Mathf.Clamp(bestDist * 0.45f, 3.8f, 6.0f);
                        if (Physics.Raycast(enemyPos + Vector3.up * 3f, Vector3.down, out var gh, 8f, LR2Layers.EnvironmentMask, QueryTriggerInteraction.Ignore))
                            enemyPos = gh.point + Vector3.up * 0.05f;
                        viewYaw = bestYaw + 9f;
                    }
                    _cmd.aimYaw = viewYaw;
                    enemy.Movement.Teleport(enemyPos);
                    enemy.transform.LookAt(new Vector3(pos.x, enemyPos.y, pos.z));
                    enemy.GetComponent<SoldierVisuals>()?.ApplyMaterialsNow(false);
                    var eProxy = enemy.GetComponent<RetargetProxy>();
                    if (eProxy != null) eProxy.SyncPoseNow();
                }

                yield return new WaitForSeconds(0.55f); // let Progressive exposure adapt cleanly
                if (cam != null)
                {
                    Texture2D tex = CaptureCamera(cam, 640, 360);
                    float meanLum = SampleMeanLuminance(tex, out float p10Lum);
                    float contrast = enemy != null ? SampleTargetContrast(tex, cam, enemy.transform.position + Vector3.up * 1.25f) : meanLum;
                    worstMean = Mathf.Min(worstMean, meanLum);
                    worstContrast = Mathf.Min(worstContrast, contrast);
                    if (i == 0) File.WriteAllBytes(Path.Combine(outDir, $"{sceneName}_vis.png"), tex.EncodeToPNG());
                    Destroy(tex);
                    _report.AppendLine($"  Vis[{sceneName}#{i}] meanLum={meanLum:0.000} p10={p10Lum:0.000} enemyContrast={contrast:0.000}");
                }
            }
            Check(worstMean >= 0.05f, $"{sceneName}.VisibilityFloor", $"worstMeanLum={worstMean:0.000} (min 0.050)");
            Check(worstContrast >= 0.025f, $"{sceneName}.EnemyReadability", $"worstEnemyContrast={worstContrast:0.000}");
        }

        private IEnumerator AuditMuzzleFlashGlare(SoldierController player)
        {
            Camera cam = player.ViewCamera;
            var gun = player.ActiveFirearm;
            float beforeLum = 0f, duringLum = 0f;
            float flashLightMax = 0f;
            if (gun != null && gun.Definition != null && gun.Definition.muzzleFlashPrefab != null)
            {
                var l = gun.Definition.muzzleFlashPrefab.GetComponentInChildren<Light>(true);
                if (l != null) flashLightMax = l.intensity;
            }
            if (cam != null && gun != null)
            {
                LR2QAExtraChecks.TryGetCameraShakeTrauma(out float traumaBefore);
                var tBefore = CaptureCamera(cam, 480, 270);
                beforeLum = SampleMeanLuminance(tBefore, out _);
                Destroy(tBefore);

                _cmd = new SoldierInput { absoluteLook = true, aimYaw = player.Yaw, aimPitch = 0f, fire = true };
                yield return null;
                yield return null;
                LR2QAExtraChecks.TryGetCameraShakeTrauma(out float traumaAfter);
                var tDuring = CaptureCamera(cam, 480, 270);
                duringLum = SampleMeanLuminance(tDuring, out _);
                Destroy(tDuring);
                _cmd.fire = false;

                float t0 = Time.time;
                float traumaRecovered = traumaAfter;
                while (Time.time - t0 < 1.35f)
                {
                    if (LR2QAExtraChecks.TryGetCameraShakeTrauma(out traumaRecovered) && traumaRecovered <= 0.001f) break;
                    yield return null;
                }
                CheckExtra(LR2QAExtraChecks.CheckCameraShake(traumaBefore, traumaAfter, traumaRecovered, Time.time - t0));
            }
            CheckExtra(LR2QAExtraChecks.CheckHorizontalFov());
            float delta = duringLum - beforeLum;
            Check(flashLightMax <= 4500f && delta < 0.14f, "MuzzleFlash.SmallGlare",
                $"prefabLux={flashLightMax:0} before={beforeLum:0.000} during={duringLum:0.000} spike={delta:0.000}");
            yield return new WaitForSeconds(0.25f);
        }

        private IEnumerator AuditRecoilAndWeaponTTK(SoldierController player)
        {
            var primary = player.Items.Count > 0 ? player.Items[0] as LR2Firearm : null;
            var sidearm = player.Items.Count > 1 ? player.Items[1] as LR2Firearm : null;
            if (primary != null && primary.Definition != null && sidearm != null && sidearm.Definition != null)
            {
                float akDmg = primary.Definition.DamageAtDistance(15f);
                float pDmg = sidearm.Definition.DamageAtDistance(15f);
                float akDps = akDmg * (primary.Definition.rpm / 60f);
                float pDps = pDmg * (sidearm.Definition.rpm / 60f);
                Check(akDmg >= pDmg && akDps > pDps * 1.4f, "TTK.AK12StrongerThanPistol",
                    $"AK12@{15}m={akDmg:0} ({akDps:0}/s) Mk23={pDmg:0} ({pDps:0}/s)");
                Check(primary.Definition.adsRecoilMultiplier <= 0.6f, "Recoil.AdsControlReduction",
                    $"adsRecoilMult={primary.Definition.adsRecoilMultiplier:0.00} adsKickMult={primary.Definition.adsKickMultiplier:0.00}");
            }
            yield break;
        }

        private static Transform FindChildNamed(Transform root, string targetName)
        {
            if (root == null) return null;
            if (root.name == targetName) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var found = FindChildNamed(root.GetChild(i), targetName);
                if (found != null) return found;
            }
            return null;
        }

        private IEnumerator AuditReloadIdleAndWalk(SoldierController player)
        {
            var gun = player.ActiveFirearm;
            var proxy = player.GetComponent<RetargetProxy>();
            var bridgeField = typeof(RetargetProxy).GetField("_bridge", BindingFlags.Instance | BindingFlags.NonPublic);
            var bridge = proxy != null && bridgeField != null ? bridgeField.GetValue(proxy) as ReloadMotionBridge : null;

            Transform magTf = gun != null ? FindChildNamed(gun.transform, "Mag") : null;
            Transform chargeTf = gun != null ? (FindChildNamed(gun.transform, "Charge")
                ?? FindChildNamed(gun.transform, "ChargingHandle")
                ?? FindChildNamed(gun.transform, "Bolt")
                ?? FindChildNamed(gun.transform, "Slider")) : null;

            CheckExtra(LR2QAExtraChecks.CheckHandsOnGun(proxy, gun));
            LR2QAExtraChecks.CaptureReloadIdlePose(player);

            string worstAbsPose = LR2QAExtraChecks.CheckAbsoluteReloadPose(player);
            float worstAbsAng = 0f;

            IEnumerator RunReloadPass(bool walk, System.Action<float, float, float, float> onDone)
            {
                float maxAng = 0f, maxHand = 0f, maxMag = 0f, maxCharge = 0f;
                Vector3 magRest = magTf != null && gun != null ? gun.transform.InverseTransformPoint(magTf.position) : Vector3.zero;
                Vector3 chargeRest = chargeTf != null && gun != null ? gun.transform.InverseTransformPoint(chargeTf.position) : Vector3.zero;

                _cmd = new SoldierInput { absoluteLook = true, aimYaw = player.Yaw, fire = true };
                yield return new WaitForSeconds(0.22f);
                _cmd = new SoldierInput
                {
                    absoluteLook = true,
                    aimYaw = player.Yaw,
                    move = walk ? new Vector2(0f, 1f) : Vector2.zero,
                    reload = true
                };

                float elapsed = 0f;
                while (elapsed < 5.5f && ((gun != null && gun.IsReloading) || elapsed < 0.2f))
                {
                    elapsed += Time.deltaTime;
                    if (bridge != null)
                    {
                        maxAng = Mathf.Max(maxAng, bridge.LastGunAngle);
                        maxHand = Mathf.Max(maxHand, bridge.HandWeight);
                    }
                    if (!walk && gun != null && gun.IsReloading && player.ViewTransform != null)
                    {
                        float absAng = Vector3.Angle(gun.transform.forward, player.ViewTransform.forward);
                        if (absAng >= worstAbsAng)
                        {
                            worstAbsAng = absAng;
                            worstAbsPose = LR2QAExtraChecks.CheckAbsoluteReloadPose(player);
                        }
                    }
                    if (gun != null)
                    {
                        if (magTf != null) maxMag = Mathf.Max(maxMag, Vector3.Distance(magRest, gun.transform.InverseTransformPoint(magTf.position)));
                        if (chargeTf != null) maxCharge = Mathf.Max(maxCharge, Vector3.Distance(chargeRest, gun.transform.InverseTransformPoint(chargeTf.position)));
                    }
                    yield return null;
                }
                _cmd = new SoldierInput { absoluteLook = true, aimYaw = player.Yaw };
                while (gun != null && gun.IsReloading) yield return null;
                onDone?.Invoke(maxAng, maxHand, maxMag, maxCharge);
            }

            float peakIdleAng = 0f, peakIdleHand = 0f, peakIdleMag = 0f, peakIdleCharge = 0f;
            float peakWalkAng = 0f, peakWalkHand = 0f, peakWalkMag = 0f, peakWalkCharge = 0f;
            yield return RunReloadPass(false, (a, h, m, c) => { peakIdleAng = a; peakIdleHand = h; peakIdleMag = m; peakIdleCharge = c; });
            int ammoAfterTactical = gun != null ? gun.AmmoInMagazine : 0;
            yield return RunReloadPass(true, (a, h, m, c) => { peakWalkAng = a; peakWalkHand = h; peakWalkMag = m; peakWalkCharge = c; });

            Check(peakIdleAng > 5f && peakIdleAng <= 75f && peakIdleHand > 0.2f, "Reload.StationaryValid",
                $"maxAngle={peakIdleAng:0.0}deg maxHandW={peakIdleHand:0.00}");
            Check(peakWalkAng > 5f && peakWalkAng <= 75f && peakWalkHand > 0.2f, "Reload.WalkingNoFlip",
                $"maxAngle={peakWalkAng:0.0}deg maxHandW={peakWalkHand:0.00}");
            CheckExtra(worstAbsPose);
            CheckExtra(LR2QAExtraChecks.CheckTacticalReload(gun, ammoAfterTactical, peakIdleCharge));
            Check(magTf == null || (peakIdleMag >= 0.05f && peakWalkMag >= 0.05f),
                "Reload.MagAndChargeAnimate",
                $"idle(mag={peakIdleMag:0.000}m,charge={peakIdleCharge:0.000}m) walk(mag={peakWalkMag:0.000}m,charge={peakWalkCharge:0.000}m)");
        }

        private static bool IsConfinedOrRoofed(Vector3 pos)
        {
            return Physics.Raycast(pos + Vector3.up * 0.45f, Vector3.up, 22f, LR2Layers.EnvironmentMask, QueryTriggerInteraction.Ignore);
        }

        private static float FindOpenYawFrom(Vector3 origin, out float clearDist)
        {
            float bestYaw = 0f;
            clearDist = 0f;
            // Reject points whose starting collider capsule already overlaps walls, steps, or props
            if (Physics.CheckCapsule(origin + Vector3.up * 0.62f, origin + Vector3.up * 1.65f, 0.42f, LR2Layers.EnvironmentMask, QueryTriggerInteraction.Ignore))
                return 0f;

            Vector3 ankle = origin + Vector3.up * 0.44f;
            Vector3 chest = origin + Vector3.up * 1.15f;
            for (int i = 0; i < 24; i++)
            {
                float yaw = i * 15f;
                Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                float dAnkle = Physics.SphereCast(ankle, 0.28f, dir, out var hitA, 25f, LR2Layers.EnvironmentMask, QueryTriggerInteraction.Ignore) ? hitA.distance : 25f;
                float dChest = Physics.SphereCast(chest, 0.40f, dir, out var hitC, 25f, LR2Layers.EnvironmentMask, QueryTriggerInteraction.Ignore) ? hitC.distance : 25f;
                float d = Mathf.Min(dAnkle, dChest);
                for (float step = 1.6f; step <= Mathf.Min(6.4f, d); step += 1.6f)
                {
                    if (!Physics.Raycast(origin + dir * step + Vector3.up * 1.2f, Vector3.down, out var gh, 2.4f, LR2Layers.EnvironmentMask, QueryTriggerInteraction.Ignore) ||
                        Mathf.Abs(gh.point.y - origin.y) > 0.38f)
                    {
                        d = Mathf.Min(d, Mathf.Max(0.2f, step - 1.0f));
                        break;
                    }
                }
                if (d > clearDist) { clearDist = d; bestYaw = yaw; }
            }
            return bestYaw;
        }

        private IEnumerator AuditWeatherThunderRainWetness(SoldierController player)
        {
            var ws = WeatherSystem.Instance;
            if (ws == null) yield break;

            ws.SetWetnessForWarmup(0.10f);
            float startWet = ws.Wetness;
            ws.TriggerThunderRain(0.95f, 30f, withFlash: true);
            float tEnd = Time.time + 2.0f;
            while (Time.time < tEnd) yield return null;

            float rainEff = ws.EffectiveRain;
            bool rainPlaying = ws.rain != null && ws.rain.isPlaying;
            bool worldCol = ws.rain != null && ws.rain.collision.enabled;
            float naturalWetAfter2s = ws.Wetness;

            ws.SetWetnessForWarmup(0.88f);
            yield return null;
            yield return null;

            bool ok = rainEff >= 0.70f && rainPlaying && worldCol && naturalWetAfter2s > startWet + 0.05f && ws.Wetness >= 0.85f && ws.WetSurfaceCount > 0;
            Check(ok, "Weather.ThunderRainAndWetness",
                $"rain={rainEff:F2} playing={rainPlaying} col={worldCol} wet0={startWet:F2} wet2s={naturalWetAfter2s:F3} wetFinal={ws.Wetness:F2} surfaces={ws.WetSurfaceCount}");
        }

        private IEnumerator AuditFootstepsAndProne(SoldierController player)
        {
            var mv = player.Movement;
            // Relocate to an unobstructed SpawnPoint + longest open lane so walls/crates never block walk or prone crawl
            var spawns = FindObjectsByType<SpawnPoint>();
            Vector3 bestStart = player.transform.position;
            float bestYaw = FindOpenYawFrom(bestStart, out float bestLane);
            foreach (var sp in spawns)
            {
                if (sp == null) continue;
                float y = FindOpenYawFrom(sp.transform.position, out float d);
                if (d > bestLane) { bestLane = d; bestStart = sp.transform.position; bestYaw = y; }
            }
            if (bestLane < 12f)
            {
                foreach (var cp in FindObjectsByType<LR2.AI.CoverPoint>())
                {
                    if (cp == null) continue;
                    float y = FindOpenYawFrom(cp.transform.position, out float d);
                    if (d > bestLane) { bestLane = d; bestStart = cp.transform.position; bestYaw = y; }
                }
            }
            if (bestLane >= 8f)
            {
                mv.Teleport(bestStart + Vector3.up * 0.05f);
                _cmd = new SoldierInput { absoluteLook = true, aimYaw = bestYaw, aimPitch = 0f };
                yield return new WaitForSeconds(0.15f);
            }

            _footsteps = 0;
            _distAccum = 0f;
            void OnStep(float sp) => _footsteps++;
            mv.Footstep += OnStep;

            // Walk forward 2.2 seconds along open ground and measure real stride distance per step + planted-foot slide (B34)
            var visAnim = player.GetComponent<RetargetProxy>()?.Visual?.GetComponent<Animator>();
            Transform footL = visAnim != null ? visAnim.GetBoneTransform(HumanBodyBones.LeftFoot) : null;
            Transform footR = visAnim != null ? visAnim.GetBoneTransform(HumanBodyBones.RightFoot) : null;
            Vector3 prevFL = footL != null ? footL.position : Vector3.zero;
            Vector3 prevFR = footR != null ? footR.position : Vector3.zero;
            Vector3 minSlidePrev = Vector3.zero, minSlideCur = Vector3.zero;
            float minSlideDt = 0.016f, minSlideSpeed = float.MaxValue;

            _cmd = new SoldierInput { absoluteLook = true, aimYaw = bestYaw, move = new Vector2(0f, 1f) };
            Vector3 prev = player.transform.position;
            float elapsed = 0f;
            while (elapsed < 2.2f)
            {
                float dt = Time.deltaTime;
                elapsed += dt;
                Vector3 cur = player.transform.position;
                _distAccum += Vector2.Distance(new Vector2(prev.x, prev.z), new Vector2(cur.x, cur.z));
                prev = cur;
                if (elapsed > 0.45f && dt > 0.002f && footL != null && footR != null)
                {
                    Vector3 dL = footL.position - prevFL; dL.y = 0f;
                    Vector3 dR = footR.position - prevFR; dR.y = 0f;
                    float sL = dL.magnitude / dt, sR = dR.magnitude / dt;
                    bool pickLeft = footL.position.y <= footR.position.y || sL < sR;
                    float s = pickLeft ? sL : sR;
                    if (s < minSlideSpeed)
                    {
                        minSlideSpeed = s;
                        minSlideDt = dt;
                        minSlidePrev = pickLeft ? prevFL : prevFR;
                        minSlideCur = pickLeft ? footL.position : footR.position;
                    }
                }
                if (footL != null) prevFL = footL.position;
                if (footR != null) prevFR = footR.position;
                yield return null;
            }
            mv.Footstep -= OnStep;
            _cmd = new SoldierInput { absoluteLook = true, aimYaw = bestYaw };
            if (footL != null && footR != null)
                CheckExtra(LR2QAExtraChecks.CheckFootSlide(minSlidePrev, minSlideCur, minSlideDt, true));

            float measuredStride = _footsteps > 0 ? _distAccum / _footsteps : 99f;
            float weaponGain = LR2Audio.CategoryVolume(AudioCategory.Weapons);
            float stepGain = LR2Audio.CategoryVolume(AudioCategory.Footsteps);
            float dbRel = 20f * Mathf.Log10(Mathf.Max(0.0001f, stepGain) / Mathf.Max(0.0001f, weaponGain));

            Check(_footsteps >= 4 && measuredStride >= 0.70f && measuredStride <= 1.45f, "Footstep.TimingSynced",
                $"steps={_footsteps} dist={_distAccum:0.00}m stride={measuredStride:0.00}m lane={bestLane:0.0}m");
            Check(dbRel <= -14.0f && dbRel >= -18.5f, "Footstep.AudioMixUnderGuns",
                $"stepGain={stepGain:0.000} weaponGain={weaponGain:0.000} ({dbRel:0.0} dB)");

            // Test prone transition speed & crawl speed on the remaining open lane (or reverse back down spawn lane)
            float crawlYaw = FindOpenYawFrom(player.transform.position, out _);
            _cmd = new SoldierInput { absoluteLook = true, aimYaw = crawlYaw, aimPitch = 0f };
            yield return null;
            float lockSec = mv.Settings != null ? mv.Settings.proneTransitionDuration : -1f;
            _cmd = new SoldierInput { absoluteLook = true, aimYaw = crawlYaw, prone = true };
            yield return new WaitForSeconds(0.38f);
            bool enteredProne = mv.Stance == SoldierStance.Prone;
            _cmd = new SoldierInput { absoluteLook = true, aimYaw = crawlYaw, move = new Vector2(0f, 1f) };
            Vector3 p0 = player.transform.position;
            yield return new WaitForSeconds(0.8f);
            float crawlSpeed = Vector2.Distance(new Vector2(p0.x, p0.z), new Vector2(player.transform.position.x, player.transform.position.z)) / 0.8f;
            _cmd = new SoldierInput { absoluteLook = true, aimYaw = crawlYaw, prone = true };
            yield return new WaitForSeconds(0.38f);
            Check(lockSec <= 0.40f && enteredProne && crawlSpeed >= 1.0f, "Prone.FasterTransitionAndCrawl",
                $"transitionSetting={lockSec:0.00}s entered={enteredProne} crawlSpeed={crawlSpeed:0.00}m/s");
        }

        private IEnumerator AuditGunBloodAdhesion(SoldierController player)
        {
            var fx = GunBloodFX.Instance;
            Check(fx != null, "GunBloodFX.Active", fx != null ? "ACTIVE" : "NULL");
            if (fx != null && player.Health != null)
            {
                var d = new DamageInfo { amount = 34f, type = DamageType.Bullet, point = player.Combatant.ChestPosition, direction = -player.transform.forward, weaponName = "QA" };
                player.Health.ApplyDamage(ref d);
                yield return new WaitForSeconds(0.2f);
                int onWeapon = 0, nearLens = 0, detached = 0;
                Camera cam = player.ViewCamera;
                Transform itemTf = player.ActiveItem != null ? player.ActiveItem.transform : null;
                foreach (var r in FindObjectsByType<MeshRenderer>())
                {
                    if (r == null || (!r.gameObject.name.Contains("[LR2_BloodBead]") && !r.gameObject.name.Contains("[LR2_BloodSplat]"))) continue;
                    if (itemTf != null && !r.transform.IsChildOf(itemTf)) detached++;
                    else onWeapon++;
                    if (cam != null && cam.transform.InverseTransformPoint(r.transform.position).z < 0.20f) nearLens++;
                }
                Check(fx.ActiveGunDropletCount >= 3 && onWeapon >= 3 && detached == 0 && nearLens == 0, "GunBlood.StuckToWeapon",
                    $"count={fx.ActiveGunDropletCount} onWeapon={onWeapon} detached={detached} nearLens={nearLens}");
            }
        }

        private IEnumerator AuditBulletFlybySfx(SoldierController player)
        {
            var common = LR2CommonSounds.Instance;
            int flybyCount = 0;
            float measuredDist = -1f;
            void OnNear(NearMissEvent e)
            {
                if (e.listener == player.Combatant)
                {
                    flybyCount++;
                    measuredDist = e.distance;
                }
            }
            GameEvents.NearMiss += OnNear;
            Vector3 head = player.Combatant.HeadPosition;
            Vector3 shotOrigin = head + player.transform.forward * 14f + player.transform.right * 1.4f;
            Vector3 shotDir = ((head + player.transform.right * 1.4f) - player.transform.forward * 6f - shotOrigin).normalized;
            var gun = player.ActiveFirearm;
            Ballistics.Fire(null, gun != null ? gun.Definition : null, shotOrigin, shotDir, shotOrigin, false, null);
            yield return null;
            GameEvents.NearMiss -= OnNear;

            bool sfxValid = common != null && common.bulletWhizz != null && common.bulletWhizz.IsValid && common.bulletCrack != null && common.bulletCrack.IsValid;
            Check(sfxValid && flybyCount >= 1 && measuredDist >= 0.5f && measuredDist <= 4.2f, "Audio.BulletFlybySFX",
                $"events={flybyCount} dist={measuredDist:0.00}m whizzValid={common?.bulletWhizz?.IsValid} crackValid={common?.bulletCrack?.IsValid} maxDist={common?.bulletWhizz?.maxDistance:0}");
        }

        private IEnumerator AuditProgressiveMusicAndHeadroom(SoldierController player)
        {
            var dir = LR2MusicDirector.Instance;
            Check(dir != null, "MusicDirector.GameplayActive", dir != null ? $"state={dir.StateLabel}" : "missing");
            if (dir == null) yield break;

            // Reset any residual intensity from earlier gun/damage tests to measure true calm baseline cleanly
            dir.ResetToCalm();
            yield return new WaitForSeconds(0.15f);
            float calmGain = dir.EffectiveTotalGain;
            float calmIntensity = dir.Intensity;

            // Simulate active firefight + weapon ducking impulse (verify music surges upward and NEVER dims on gunfire!)
            LR2Audio.Duck(1f);
            for (int k = 0; k < 4; k++)
            {
                dir.NotifyCombatEvent(0.38f, 3.2f);
                LR2Audio.Duck(1f);
                yield return new WaitForSeconds(0.28f);
            }
            float fightIntensity = dir.Intensity;
            float fightGain = dir.EffectiveTotalGain;
            float combatStem = dir.CombatVolume;
            float stepCat = LR2Audio.CategoryVolume(AudioCategory.Footsteps);

            Check(fightIntensity > calmIntensity + 0.45f && fightGain > calmGain * 1.55f && combatStem > 0.08f && dir.LastDrumrollTime >= 0f, "Music.OrganicCombatRamp",
                $"calmI={calmIntensity:0.00} fightI={fightIntensity:0.00} combatStem={combatStem:0.000} calmGain={calmGain:0.000} fightGain={fightGain:0.000} drumrollAt={dir.LastDrumrollTime:0.00}s");
            Check(calmGain <= 0.14f && fightGain <= 0.40f, "Music.LeavesFootstepsClear",
                $"calmGain={calmGain:0.000} fightGain={fightGain:0.000} stepGain={stepCat:0.000}");
        }

        private IEnumerator AuditKillcamHitmarkersAndAudio()
        {
            var kd = KillcamDirector.Instance;
            var hud = FindAnyObjectByType<HUDController>();
            Check(kd != null, "KillcamDirector.Present", kd != null ? "ACTIVE" : "MISSING");

            // Hide main hudGroup just like killcam end-of-match/hidden HUD and verify replay hitmarker works cleanly (B4, B6, B8, B14)
            float prevAlpha = hud != null && hud.hudGroup != null ? hud.hudGroup.alpha : 1f;
            if (hud != null && hud.hudGroup != null) hud.hudGroup.alpha = 0f;
            HUDController.ShowReplayHit(true, true, Camera.main != null ? Camera.main.transform.position + Camera.main.transform.forward * 6f : Vector3.one);
            yield return null;
            var hf = FindObjectsByType<HitFeedbackHUD>();
            bool anyVisible = false;
            bool zoneOk = false;
            foreach (var h in hf)
            {
                if (h == null || !h.gameObject.activeInHierarchy) continue;
                bool hiddenParent = false;
                for (Transform p = h.transform; p != null; p = p.parent)
                {
                    var cg = p.GetComponent<CanvasGroup>();
                    if (cg != null && cg.alpha < 0.8f) { hiddenParent = true; break; }
                }
                if (!hiddenParent)
                {
                    anyVisible = true;
                    if (!string.IsNullOrEmpty(h.LastZoneText) && h.IsShowingWorldImpactPip) zoneOk = true;
                }
            }
            if (hud != null && hud.hudGroup != null) hud.hudGroup.alpha = prevAlpha;
            Check(anyVisible, "Killcam.ShowReplayHitWorksWhenHudHidden", $"overlays={hf.Length} visibleWhenHidden={anyVisible}");

            var cs = LR2CommonSounds.Instance;
            bool ruggedAudio = cs != null && cs.hitmarker.IsValid && cs.hitmarkerHeadshot.IsValid && cs.killConfirm.IsValid;
            Check(ruggedAudio && HitFeedbackHUD.HasZoneIndicator && zoneOk, "HitFeedback.RuggedAudioAndZoneIndicator",
                $"audio={ruggedAudio} zoneUI={HitFeedbackHUD.HasZoneIndicator} pipActive={zoneOk}");
            Check(kd != null && kd.PostKillWindow >= 0.70f, "Killcam.VictimCollapseWindow",
                kd != null ? $"postKillWindow={kd.PostKillWindow:0.00}s pre={kd.PreKillWindow:0.00}s" : "null");
        }

        private IEnumerator AuditCtfAndAdaptiveBot(SoldierController player)
        {
            // 1. Verify FlagAnchors exist on combat maps & BF6 proximity multiplier curves ("closer faster, far harder")
            float coreMult = FlagZoneController.ComputeProximitySpeedMultiplier(1.5f, 2.6f, 11.5f);
            float midMult = FlagZoneController.ComputeProximitySpeedMultiplier(6.5f, 2.6f, 11.5f);
            float edgeMult = FlagZoneController.ComputeProximitySpeedMultiplier(11.4f, 2.6f, 11.5f);
            float outMult = FlagZoneController.ComputeProximitySpeedMultiplier(12.2f, 2.6f, 11.5f);
            Check(FlagAnchor.All.Count >= 4 && coreMult >= 2.15f && midMult > edgeMult && edgeMult <= 0.45f && outMult == 0f,
                "CTF.BF6ProximityCaptureCurve",
                $"anchors={FlagAnchor.All.Count} core={coreMult:0.00}x mid={midMult:0.00}x edge={edgeMult:0.00}x out={outMult:0.00}x");

            // 2. Verify BotController generates pure human mouse deltas (never absoluteLook teleport) & supports Adaptive difficulty
            int prevDiff = LR2Settings.BotDifficulty;
            LR2Settings.BotDifficulty = 4; // Adaptive
            AI.BotController testBot = null;
            foreach (var b in FindObjectsByType<AI.BotController>())
                if (b != null && b.Soldier != null && b.Soldier != player) { testBot = b; break; }
            bool humanInputOk = false;
            bool adaptiveOk = false;
            if (testBot != null)
            {
                bool wasEnabled = testBot.enabled;
                testBot.enabled = true;
                yield return null;
                var cmd = SoldierInput.Empty;
                testBot.GetInput(testBot.Soldier, ref cmd);
                humanInputOk = !cmd.absoluteLook;
                adaptiveOk = testBot.IsAdaptiveMode && testBot.ActiveDifficulty != null;
                testBot.enabled = wasEnabled;
            }
            LR2Settings.BotDifficulty = prevDiff;
            Check(testBot != null && humanInputOk && adaptiveOk, "Bot.HumanMouseInputAndAdaptive",
                $"bot={(testBot != null ? testBot.name : "none")} humanMouseLook={humanInputOk} adaptive={adaptiveOk}");

            // 3. Verify HDRISky, RayTracingController, & realistic M_LR2_Glass (B30) readiness
            bool hasHdriSky = false;
            foreach (var v in FindObjectsByType<Volume>())
                if (v != null && v.sharedProfile != null && v.sharedProfile.Has<HDRISky>()) { hasHdriSky = true; break; }
            Check(hasHdriSky, "Lighting.HDRISkyPresent", $"hdri={hasHdriSky} rayTracingCtrl={(LR2RayTracingController.Instance != null)}");

            int glassCount = 0, glassRealistic = 0;
            foreach (var dp in FindObjectsByType<DestructibleProp>())
            {
                if (dp == null || dp.surface != SurfaceType.Glass) continue;
                glassCount++;
                var r = dp.GetComponentInChildren<Renderer>();
                if (r != null && r.sharedMaterial != null && r.sharedMaterial.HasProperty("_SurfaceType") &&
                    r.sharedMaterial.GetFloat("_SurfaceType") > 0.5f && r.sharedMaterial.GetTexture("_NormalMap") != null)
                    glassRealistic++;
            }
            Check(glassCount == 0 || glassRealistic == glassCount, "Glass.RealisticMaterialAndPanes",
                $"panes={glassCount} realisticPanes={glassRealistic}");
        }

        private static Texture2D CaptureCamera(Camera cam, int w, int h)
        {
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            try
            {
                var request = new RenderPipeline.StandardRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(cam, request))
                    for (int p = 0; p < 3; p++) RenderPipeline.SubmitRenderRequest(cam, request);
                else
                {
                    var prevTarget = cam.targetTexture;
                    cam.targetTexture = rt;
                    cam.Render();
                    cam.targetTexture = prevTarget;
                }
                var prevActive = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prevActive;
                return tex;
            }
            finally { RenderTexture.ReleaseTemporary(rt); }
        }

        private static float SampleMeanLuminance(Texture2D tex, out float p10)
        {
            var px = tex.GetPixels();
            var lums = new float[px.Length];
            double sum = 0.0;
            for (int i = 0; i < px.Length; i++)
            {
                float lum = 0.2126f * px[i].r + 0.7152f * px[i].g + 0.0722f * px[i].b;
                lums[i] = lum;
                sum += lum;
            }
            Array.Sort(lums);
            p10 = lums[Mathf.Clamp((int)(lums.Length * 0.1f), 0, lums.Length - 1)];
            return (float)(sum / Mathf.Max(1, px.Length));
        }

        private static float SampleTargetContrast(Texture2D tex, Camera cam, Vector3 worldPos)
        {
            Vector3 vp = cam.WorldToViewportPoint(worldPos);
            if (vp.z <= 0.1f || vp.x < 0.05f || vp.x > 0.95f || vp.y < 0.05f || vp.y > 0.95f) return SampleMeanLuminance(tex, out _);
            int cx = Mathf.RoundToInt(vp.x * tex.width);
            int cy = Mathf.RoundToInt(vp.y * tex.height);
            float LumAt(int x, int y)
            {
                Color c = tex.GetPixel(Mathf.Clamp(x, 0, tex.width - 1), Mathf.Clamp(y, 0, tex.height - 1));
                return 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
            }
            double targetSum = 0.0, bgSum = 0.0;
            int tCnt = 0, bCnt = 0;
            for (int dy = -6; dy <= 6; dy++)
            for (int dx = -4; dx <= 4; dx++)
            {
                targetSum += LumAt(cx + dx, cy + dy);
                tCnt++;
            }
            for (int dy = -22; dy <= 22; dy += 4)
            for (int dx = -22; dx <= 22; dx += 4)
            {
                if (Mathf.Abs(dx) < 8 && Mathf.Abs(dy) < 10) continue;
                bgSum += LumAt(cx + dx, cy + dy);
                bCnt++;
            }
            float tMean = (float)(targetSum / Mathf.Max(1, tCnt));
            float bMean = (float)(bgSum / Mathf.Max(1, bCnt));
            return Mathf.Abs(tMean - bMean);
        }

        private void FinishReport(string outDir, string sceneName)
        {
            Application.logMessageReceived -= OnLog;
            Check(_runtimeErrors == 0, $"{sceneName}.RuntimeErrors", $"count={_runtimeErrors}");
            _report.AppendLine($"Failures: {_failures.Count}");
            string path = Path.Combine(outDir, $"qa_{sceneName}.txt");
            File.WriteAllText(path, _report.ToString());
            Debug.Log(_report.ToString());
#if UNITY_EDITOR
            if (Application.isBatchMode || Requested)
            {
                string setupPath = Path.Combine(Directory.GetCurrentDirectory(), "Library", "LastSceneManagerSetup.txt");
                if (File.Exists("Assets/LR2/Scenes/LR2_MainMenu.unity"))
                    File.WriteAllText(setupPath, "sceneSetups:\n- path: Assets/LR2/Scenes/LR2_MainMenu.unity\n  isLoaded: 1\n  isActive: 1\n  isSubScene: 0\n");
                UnityEditor.EditorApplication.Exit(_failures.Count > 0 ? 1 : 0);
            }
#endif
        }
    }
}
