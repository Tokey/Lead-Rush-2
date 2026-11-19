using System;
using System.Collections;
using System.IO;
using LR2.Combat;
using LR2.Core;
using LR2.Game;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.UI;

namespace LR2.World
{
    /// <summary>
    /// Media capture for the features page (B103, user 11-19: "That needs to be detailed with SS gifs and all"). Runs inside
    /// a visible <c>LR2SmokeTest.RunBatch</c> play session when one of these flags is on the command line, writing PNG frames
    /// to <c>-lr2OutDir</c> (Tools turn the frame runs into GIFs):
    /// <c>-lr2MenuMedia</c> (main menu cinematic, lobby and presets, then AI vs AI spectating),
    /// <c>-lr2KillcamMedia</c> (a bot's real shot is made a lethal headshot on the player: the bullet cam plays),
    /// <c>-lr2FxMedia</c> (Particle Pack steam, sparks and an explosion through a capture camera, HUD hidden).
    /// </summary>
    public class MediaDirector : MonoBehaviour
    {
        private string _mode, _out;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            string mode = Array.IndexOf(args, "-lr2AimBench") >= 0 ? "bench"
                        : Array.IndexOf(args, "-lr2MenuMedia") >= 0 ? "menu"
                        : Array.IndexOf(args, "-lr2KillcamMedia") >= 0 ? "killcam"
                        : Array.IndexOf(args, "-lr2FxMedia") >= 0 ? "fx" : null;
            if (mode == null || FindAnyObjectByType<MediaDirector>() != null) return;
            string outDir = "Logs/LR2_Media";
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-lr2OutDir") outDir = args[i + 1];
            Directory.CreateDirectory(outDir);
            var go = new GameObject("[LR2 MediaDirector]");
            DontDestroyOnLoad(go);
            var md = go.AddComponent<MediaDirector>();
            md._mode = mode;
            md._out = outDir;
            if (mode == "bench")
            {
                // before MatchManager.Start: AI vs AI, team Alpha and team Bravo each get one bot per difficulty
                SkirmishSetup.Active = true;
                SkirmishSetup.PlayerSpectates = true;
                SkirmishSetup.Mode = GameModeType.TeamDeathmatch;
                SkirmishSetup.Bots.Clear();
                foreach (var t in new[] { TeamId.Alpha, TeamId.Bravo })
                    for (int d = 0; d < 4; d++) SkirmishSetup.Bots.Add(new SkirmishBot { team = t, difficulty = d });
            }
        }

        private IEnumerator Start()
        {
            Note($"media run '{_mode}' -> {_out}");
            if (_mode == "menu") yield return MenuRun();
            else if (_mode == "killcam") yield return KillcamRun();
            else if (_mode == "fx") yield return FxRun();
            else if (_mode == "bench") yield return BenchRun();
            Note("media run done");
        }

        private void Note(string s)
        {
            Debug.Log("[LR2] MEDIA " + s);
            File.AppendAllText(Path.Combine(_out, "media_log.txt"), $"{DateTime.Now:HH:mm:ss} {s}\n");
        }

        private IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(_out, name + ".png"), tex.EncodeToPNG());
            Destroy(tex);
        }

        private IEnumerator Frames(string prefix, int count, float interval)
        {
            for (int i = 0; i < count; i++)
            {
                yield return Shot($"{prefix}_{i:000}");
                yield return new WaitForSecondsRealtime(interval);
            }
        }

        private static bool Click(Transform root, string buttonName)
        {
            foreach (var b in root.GetComponentsInChildren<Button>(true))
                if (b.name == "Btn_" + buttonName) { b.onClick.Invoke(); return true; }
            return false;
        }

        // ------------------------------------------------------------------ menu, lobby, AI vs AI
        private IEnumerator MenuRun()
        {
            yield return new WaitForSecondsRealtime(3.5f);
            yield return Frames("menu_title", 28, 0.12f);
            var menu = FindAnyObjectByType<LR2.UI.MainMenuController>();
            if (menu == null || menu.duelButton == null) { Note("no main menu"); yield break; }
            menu.duelButton.onClick.Invoke(); // PLAY
            yield return new WaitForSecondsRealtime(2.4f);
            var lobby = FindAnyObjectByType<LR2.UI.SkirmishLobby>();
            if (lobby == null) { Note("no lobby"); yield break; }
            yield return Shot("lobby_2teams");
            yield return Frames("lobby_tdm_bg", 18, 0.15f);
            Click(lobby.transform, "4-WAY"); yield return new WaitForSecondsRealtime(1.6f); yield return Shot("lobby_4way");
            Click(lobby.transform, "FFA"); yield return new WaitForSecondsRealtime(2.2f); yield return Frames("lobby_ffa_bg", 22, 0.15f);
            Click(lobby.transform, "AI VS AI"); yield return new WaitForSecondsRealtime(1.6f); yield return Shot("lobby_aivsai");
            Note($"starting AI vs AI: {SkirmishSetup.Bots.Count} bots, spectate={SkirmishSetup.PlayerSpectates}");
            Click(lobby.transform, "START");
            // the match scene loads; this object survives it
            float t0 = Time.realtimeSinceStartup;
            while (SpectatorCamera.Instance == null && Time.realtimeSinceStartup - t0 < 30f) yield return null;
            if (SpectatorCamera.Instance == null) { Note("no spectator camera after START"); yield break; }
            yield return new WaitForSecondsRealtime(9f); // countdown and the first contact
            yield return Frames("aivsai_chase", 30, 0.15f);
            SpectatorCamera.Instance.SetView(1);
            yield return new WaitForSecondsRealtime(1.2f);
            yield return Frames("aivsai_eye", 18, 0.15f);
            SpectatorCamera.Instance.SetView(0);
            yield return new WaitForSecondsRealtime(4f);
            yield return Shot("aivsai_bench");
        }

        // ------------------------------------------------------------------ accuracy and K/D bench
        /// <summary>Plays the AI vs AI bench for -lr2BenchSeconds (default 120) and writes shots, hits, kills and deaths per difficulty.</summary>
        private IEnumerator BenchRun()
        {
            float secs = 120f;
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-lr2BenchSeconds") float.TryParse(args[i + 1], out secs);
            float t0 = Time.time;
            while (Time.time - t0 < secs && (MatchManager.Instance == null || MatchManager.Instance.State != MatchState.Ended)) yield return null;
            var rows = new System.Collections.Generic.SortedDictionary<string, int[]>();
            foreach (var c in Combatant.All)
            {
                if (c == null || c.GetComponent<LR2.AI.BotController>() == null) continue;
                string n = c.DisplayName ?? "";
                int a = n.LastIndexOf('['), b = n.LastIndexOf(']');
                string key = a >= 0 && b > a ? n.Substring(a + 1, b - a - 1) : "BOT";
                if (!rows.TryGetValue(key, out var r)) rows[key] = r = new int[5];
                r[0] += c.stats.shotsFired; r[1] += c.stats.shotsHit; r[2] += c.stats.kills; r[3] += c.stats.deaths; r[4] += c.stats.headshots;
            }
            var sb = new System.Text.StringBuilder($"aim={(LR2.AI.BotController.LegacyAim ? "legacy" : "human (B118)")} seconds={Time.time - t0:0}\n");
            sb.AppendLine("difficulty,shots,hits,accuracy,kills,deaths,kd,headshots");
            foreach (var kv in rows)
            {
                var r = kv.Value;
                sb.AppendLine(FormattableString.Invariant($"{kv.Key},{r[0]},{r[1]},{(r[0] > 0 ? (float)r[1] / r[0] : 0f):0.000},{r[2]},{r[3]},{(r[3] > 0 ? (float)r[2] / r[3] : r[2]):0.00},{r[4]}"));
            }
            File.WriteAllText(Path.Combine(_out, "bench.csv"), sb.ToString());
            Note("bench written:\n" + sb);
        }

        // ------------------------------------------------------------------ headshot bullet cam
        private IEnumerator KillcamRun()
        {
            yield return new WaitForSecondsRealtime(7f); // countdown
            var player = Combatant.FindLocalPlayer();
            var psc = player != null ? player.GetComponent<LR2.Characters.SoldierController>() : null;
            LR2.Characters.SoldierController bot = null;
            foreach (var c in Combatant.All)
            {
                if (c == null || c == player || !c.IsAlive || !TeamUtil.AreHostile(c, player)) continue;
                var sc = c.GetComponent<LR2.Characters.SoldierController>();
                if (sc != null && c.GetComponent<LR2.AI.BotController>() != null) { bot = sc; break; }
            }
            if (player == null || psc == null || bot == null) { Note("no player or hostile bot"); yield break; }
            // stage a duel 14 m apart on open ground, facing each other
            float yaw = psc.Yaw;
            Vector3 fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            Vector3 bp = player.Position + fwd * 14f;
            if (Physics.Raycast(bp + Vector3.up * 3f, Vector3.down, out var gh, 8f, LR2Layers.GroundMask, QueryTriggerInteraction.Ignore)) bp = gh.point + Vector3.up * 0.05f;
            bot.Teleport(bp, yaw + 180f);
            var agent = bot.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agent != null && agent.isOnNavMesh) agent.Warp(bp);
            player.Health.SetInvulnerable(40f);
            bool armed = true, killed = false;
            void OnShot(ShotEvent e)
            {
                if (!armed || e.shooter != bot.Combatant) return;
                armed = false;
                player.Health.CancelSpawnProtection();
                Vector3 head = player.HeadPosition;
                var info = new DamageInfo
                {
                    amount = 500f, type = DamageType.Bullet, zone = HitZone.Head, attacker = bot.Combatant,
                    weaponName = bot.ActiveFirearm != null ? bot.ActiveFirearm.DisplayName : "rifle",
                    point = head, normal = -(head - e.origin).normalized, direction = (head - e.origin).normalized,
                    distance = Vector3.Distance(head, e.origin), force = 140f
                };
                player.Health.ApplyDamage(ref info);
                killed = true;
            }
            GameEvents.ShotFired += OnShot;
            float t0 = Time.realtimeSinceStartup;
            while (!killed && Time.realtimeSinceStartup - t0 < 25f) yield return null;
            GameEvents.ShotFired -= OnShot;
            if (!killed) { Note("the bot never fired"); yield break; }
            Note("headshot kill staged; recording the killcam");
            yield return new WaitForSecondsRealtime(1.9f);
            yield return Shot("killcam_replay");
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Frames("killcam_bullet", 42, 0.09f);
        }

        // ------------------------------------------------------------------ Particle Pack on the map
        private IEnumerator FxRun()
        {
            yield return new WaitForSecondsRealtime(5f);
            foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None)) c.enabled = false;
            var camGo = new GameObject("[Media Cam]");
            var cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<HDAdditionalCameraData>();
            cam.depth = 50f;
            cam.fieldOfView = 48f;
            cam.nearClipPlane = 0.05f;
            var fx = MapAmbientFx.Instance;
            if (fx != null && fx.SteamSpots.Count > 0)
            {
                var (p, n) = fx.SteamSpots[0];
                Vector3 side = Vector3.Cross(Vector3.up, n).normalized;
                camGo.transform.position = p + n * 1.3f + side * 1.2f + Vector3.up * 0.25f;
                camGo.transform.LookAt(p + n * 0.6f + Vector3.up * 0.25f);
                yield return new WaitForSecondsRealtime(1f);
                yield return Frames("fx_steam", 20, 0.1f);
            }
            else Note("no steam leaks on this map");
            if (fx != null && fx.SparkSpots.Count > 0)
            {
                Vector3 s = fx.SparkSpots[0];
                camGo.transform.position = s + new Vector3(1.8f, -1.2f, 1.4f);
                camGo.transform.LookAt(s - Vector3.up * 0.3f);
                yield return new WaitForSecondsRealtime(0.5f);
                fx.SparkNow();
                yield return Frames("fx_sparks", 16, 0.07f);
            }
            else Note("no sparking fittings on this map");
            DestructibleProp boom = null;
            foreach (var d in FindObjectsByType<DestructibleProp>(FindObjectsSortMode.None)) if (d != null && d.explosive && d.isActiveAndEnabled) { boom = d; break; }
            if (boom != null)
            {
                Vector3 c = boom.transform.position + Vector3.up * 0.6f;
                Vector3 view = c + new Vector3(6f, 1.4f, 4f);
                foreach (float a in new[] { 0f, 90f, 180f, 270f })
                {
                    Vector3 v = c + Quaternion.Euler(0f, a, 0f) * new Vector3(6.5f, 1.4f, 3f);
                    if (!Physics.Linecast(v, c, LR2Layers.EnvironmentMask & ~(1 << boom.gameObject.layer), QueryTriggerInteraction.Ignore)) { view = v; break; }
                }
                camGo.transform.position = view;
                camGo.transform.LookAt(c + Vector3.up * 0.8f);
                yield return new WaitForSecondsRealtime(0.6f);
                var info = new DamageInfo { amount = 9999f, type = DamageType.Bullet, zone = HitZone.Generic, point = c, direction = (c - view).normalized, normal = -(c - view).normalized, force = 50f };
                boom.ApplyDamage(ref info);
                yield return Frames("fx_explosion", 26, 0.06f);
            }
            else Note("no explosive props on this map");
        }
    }
}
