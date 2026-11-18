using LR2.Core;
using UnityEngine;

namespace LR2.Game
{
    public enum GameModeType { Duel, FreeForAll, TeamDeathmatch, CaptureTheFlag }

    /// <summary>Per-mode customizable match rules stored persistently per GameModeType.</summary>
    [System.Serializable]
    public struct ModeRuleConfig
    {
        public int scoreLimit;
        public float timeLimitSeconds;
        public float respawnDelay;
        public float countdownSeconds;
        public int friendlyBots;
        public int enemyBots;
        [Tooltip("-1 = Use global Bot Difficulty setting, 0..3 = Override for this mode.")]
        public int botDifficulty;
        public bool spawnOrbs;
        public int maxOrbs;
        public float orbSpawnInterval;

        public static ModeRuleConfig FromSettings(MatchSettings s)
        {
            if (s == null)
            {
                return new ModeRuleConfig
                {
                    scoreLimit = 15,
                    timeLimitSeconds = 480f,
                    respawnDelay = 3f,
                    countdownSeconds = 5f,
                    friendlyBots = 0,
                    enemyBots = 1,
                    botDifficulty = -1,
                    spawnOrbs = true,
                    maxOrbs = 2,
                    orbSpawnInterval = 30f,
                };
            }

            return new ModeRuleConfig
            {
                scoreLimit = Mathf.Max(1, s.scoreLimit),
                timeLimitSeconds = Mathf.Max(60f, s.timeLimitSeconds),
                respawnDelay = Mathf.Max(0f, s.respawnDelay),
                countdownSeconds = Mathf.Max(0f, s.countdownSeconds),
                friendlyBots = Mathf.Max(0, s.friendlyBots),
                enemyBots = Mathf.Max(1, s.enemyBots),
                botDifficulty = -1,
                spawnOrbs = s.spawnOrbs,
                maxOrbs = Mathf.Max(1, s.maxOrbs),
                orbSpawnInterval = Mathf.Max(10f, s.orbSpawnInterval),
            };
        }

        public void ClampFor(GameModeType mode)
        {
            scoreLimit = Mathf.Clamp(scoreLimit, 1, 100);
            timeLimitSeconds = Mathf.Clamp(timeLimitSeconds, 60f, 1800f);
            respawnDelay = Mathf.Clamp(respawnDelay, 0f, 15f);
            countdownSeconds = Mathf.Clamp(countdownSeconds, 0f, 10f);
            enemyBots = mode == GameModeType.Duel ? 1 : Mathf.Clamp(enemyBots, 1, 8);
            friendlyBots = mode == GameModeType.TeamDeathmatch ? Mathf.Clamp(friendlyBots, 0, 5) : 0;
            botDifficulty = Mathf.Clamp(botDifficulty, -1, 3);
            maxOrbs = Mathf.Clamp(maxOrbs, 1, 6);
            orbSpawnInterval = Mathf.Clamp(orbSpawnInterval, 10f, 90f);
        }

        public void ApplyTo(MatchSettings target)
        {
            if (target == null) return;
            ClampFor(target.mode);
            target.scoreLimit = scoreLimit;
            target.timeLimitSeconds = timeLimitSeconds;
            target.respawnDelay = respawnDelay;
            target.countdownSeconds = countdownSeconds;
            target.friendlyBots = friendlyBots;
            target.enemyBots = enemyBots;
            target.spawnOrbs = spawnOrbs;
            target.maxOrbs = maxOrbs;
            target.orbSpawnInterval = orbSpawnInterval;
        }
    }

    [CreateAssetMenu(menuName = "LR2/Game/Match Settings", fileName = "MatchSettings")]
    public class MatchSettings : ScriptableObject
    {
        [Header("Mode")]
        public string displayName = "1V1 DEATHMATCH";
        [TextArea] public string description = "1v1 Deathmatch against an adaptive rival. First to 15 kills wins.";
        public GameModeType mode = GameModeType.Duel;

        [Header("Limits")]
        [Min(1)] public int scoreLimit = 15;
        [Min(30f)] public float timeLimitSeconds = 480f;
        [Min(0f)] public float countdownSeconds = 5f;
        [Min(0f)] public float respawnDelay = 3f;

        [Header("Participants")]
        [Tooltip("Bots on the player's team (TDM only).")]
        [Min(0)] public int friendlyBots = 1;
        [Tooltip("Enemy bots (Duel uses 1, FFA uses this as total opponents).")]
        [Min(0)] public int enemyBots = 1;

        [Header("Seeker Drones (legacy orb enemy)")]
        public bool spawnOrbs = true;
        [Min(0)] public int maxOrbs = 2;
        [Min(5f)] public float orbSpawnInterval = 30f;

        public bool IsTeamMode => mode == GameModeType.TeamDeathmatch;
    }

    /// <summary>Carries the menu's choice into the map scene (and across restarts).</summary>
    public static class LR2Session
    {
        public const string MenuScene = "LR2_MainMenu";
        public const string MapScene = "LR2_Saltworks";

        public static readonly string[] MapScenes =
        {
            "LR2_Saltworks",
            "LR2_Deadsector",
            "LR2_Foundry",
            "LR2_TestRange"
        };

        public static readonly string[] MapDisplayNames =
        {
            "SALTWORKS  -  RECLAIMED HARBOUR",
            "DEADSECTOR  -  WAR-TORN CITY",
            "FOUNDRY  -  PIPE REFINERY",
            "TEST RANGE  -  BOT ARENA"
        };

        public static readonly string[] MapDescriptions =
        {
            "Coastal salt-flat industrial harbour with shipping containers, flooded sightlines, and sniper lanes.",
            "Rubble-choked urban quarantine sector tuned for aggressive flanking, street fights, and interior breaches.",
            "Multi-level chemical pipe refinery featuring steel catwalks, heavy cover, and tight chokepoints.",
            "Open tactical proving ground with clean symmetrical cover for recoil drills and bot scrimmages."
        };

        private const string MapPrefKey = "LR2.SelectedMap";
        private const string WeaponPrefKey = "LR2.SelectedPrimaryWeapon";
        public static readonly string[] PrimaryWeaponNames = { "AK-12", "AK-74N", "MK18", "FAL" };
        private static MatchSettings _runtimeCopy;

        public static int SelectedPrimaryWeapon
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(WeaponPrefKey, 0), 0, PrimaryWeaponNames.Length - 1);
            set
            {
                int v = Mathf.Clamp(value, 0, PrimaryWeaponNames.Length - 1);
                PlayerPrefs.SetInt(WeaponPrefKey, v);
                PlayerPrefs.Save();
            }
        }

        public static int SelectedMapIndex
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(MapPrefKey, 0), 0, MapScenes.Length - 1);
            set
            {
                int v = Mathf.Clamp(value, 0, MapScenes.Length - 1);
                PlayerPrefs.SetInt(MapPrefKey, v);
                PlayerPrefs.Save();
                NextScene = MapScenes[v];
            }
        }

        public static string CurrentMapScene => MapScenes[SelectedMapIndex];

        public static string PreviewResourcePath(int index)
        {
            int clamped = Mathf.Clamp(index, 0, MapScenes.Length - 1);
            return "LR2/MapPreviews/Preview_" + MapScenes[clamped];
        }

        public static MatchSettings PendingSettings;
        public static MatchSettings ActiveSettings;
        public static string NextScene = MapScene;

        private static string ModePrefix(GameModeType mode) => $"LR2.ModeRules.{mode}.";

        public static bool HasCustomModeRules(GameModeType mode) => PlayerPrefs.GetInt(ModePrefix(mode) + "Custom", 0) == 1;

        public static ModeRuleConfig LoadModeRules(MatchSettings template)
        {
            ModeRuleConfig cfg = ModeRuleConfig.FromSettings(template);
            GameModeType mode = template != null ? template.mode : GameModeType.Duel;
            string p = ModePrefix(mode);
            if (PlayerPrefs.GetInt(p + "Custom", 0) == 1)
            {
                cfg.scoreLimit = PlayerPrefs.GetInt(p + "ScoreLimit", cfg.scoreLimit);
                cfg.timeLimitSeconds = PlayerPrefs.GetFloat(p + "TimeLimit", cfg.timeLimitSeconds);
                cfg.respawnDelay = PlayerPrefs.GetFloat(p + "RespawnDelay", cfg.respawnDelay);
                cfg.countdownSeconds = PlayerPrefs.GetFloat(p + "Countdown", cfg.countdownSeconds);
                cfg.friendlyBots = PlayerPrefs.GetInt(p + "FriendlyBots", cfg.friendlyBots);
                cfg.enemyBots = PlayerPrefs.GetInt(p + "EnemyBots", cfg.enemyBots);
                cfg.botDifficulty = PlayerPrefs.GetInt(p + "BotDifficulty", cfg.botDifficulty);
                cfg.spawnOrbs = PlayerPrefs.GetInt(p + "SpawnOrbs", cfg.spawnOrbs ? 1 : 0) == 1;
                cfg.maxOrbs = PlayerPrefs.GetInt(p + "MaxOrbs", cfg.maxOrbs);
                cfg.orbSpawnInterval = PlayerPrefs.GetFloat(p + "OrbInterval", cfg.orbSpawnInterval);
            }
            cfg.ClampFor(mode);
            return cfg;
        }

        public static void SaveModeRules(GameModeType mode, ModeRuleConfig cfg)
        {
            cfg.ClampFor(mode);
            string p = ModePrefix(mode);
            PlayerPrefs.SetInt(p + "Custom", 1);
            PlayerPrefs.SetInt(p + "ScoreLimit", cfg.scoreLimit);
            PlayerPrefs.SetFloat(p + "TimeLimit", cfg.timeLimitSeconds);
            PlayerPrefs.SetFloat(p + "RespawnDelay", cfg.respawnDelay);
            PlayerPrefs.SetFloat(p + "Countdown", cfg.countdownSeconds);
            PlayerPrefs.SetInt(p + "FriendlyBots", cfg.friendlyBots);
            PlayerPrefs.SetInt(p + "EnemyBots", cfg.enemyBots);
            PlayerPrefs.SetInt(p + "BotDifficulty", cfg.botDifficulty);
            PlayerPrefs.SetInt(p + "SpawnOrbs", cfg.spawnOrbs ? 1 : 0);
            PlayerPrefs.SetInt(p + "MaxOrbs", cfg.maxOrbs);
            PlayerPrefs.SetFloat(p + "OrbInterval", cfg.orbSpawnInterval);
            PlayerPrefs.Save();
        }

        public static void ResetModeRules(GameModeType mode)
        {
            string p = ModePrefix(mode);
            string[] keys =
            {
                "Custom", "ScoreLimit", "TimeLimit", "RespawnDelay", "Countdown",
                "FriendlyBots", "EnemyBots", "BotDifficulty", "SpawnOrbs", "MaxOrbs", "OrbInterval",
            };
            foreach (string k in keys) PlayerPrefs.DeleteKey(p + k);
            PlayerPrefs.Save();
        }

        /// <summary>Creates a persistent runtime copy of the template with saved per-mode rules applied.</summary>
        public static MatchSettings PrepareConfiguredMatch(MatchSettings template)
        {
            if (_runtimeCopy != null)
            {
                Object.Destroy(_runtimeCopy);
                _runtimeCopy = null;
            }

            MatchSettings copy = template != null
                ? Object.Instantiate(template)
                : ScriptableObject.CreateInstance<MatchSettings>();
            copy.name = template != null ? $"{template.name}_Runtime" : "MatchSettings_Runtime";
            copy.hideFlags = HideFlags.DontSave;

            ModeRuleConfig cfg = LoadModeRules(template);
            cfg.ApplyTo(copy);
            if (cfg.botDifficulty >= 0) LR2Settings.BotDifficulty = cfg.botDifficulty;

            _runtimeCopy = copy;
            PendingSettings = copy;
            return copy;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            PendingSettings = null;
            ActiveSettings = null;
            _runtimeCopy = null;
            NextScene = CurrentMapScene;
        }
    }
}
