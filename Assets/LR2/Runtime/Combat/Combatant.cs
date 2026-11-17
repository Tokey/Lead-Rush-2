using System;
using System.Collections.Generic;
using LR2.Core;
using UnityEngine;

namespace LR2.Combat
{
    [Serializable]
    public class CombatantStats
    {
        public int kills;
        public int deaths;
        public int assists;
        public int score;
        public int headshots;
        public int shotsFired;
        public int shotsHit;
        public int currentStreak;
        public int bestStreak;
        public float damageDealt;

        public float Accuracy => shotsFired > 0 ? (float)shotsHit / shotsFired : 0f;
        public float KillDeathRatio => deaths > 0 ? (float)kills / deaths : kills;

        public void Reset()
        {
            kills = deaths = assists = score = headshots = shotsFired = shotsHit = currentStreak = bestStreak = 0;
            damageDealt = 0f;
        }
    }

    /// <summary>
    /// Identity of anything that fights: the human player, humanoid bots and orb drones. Holds team, name, stats
    /// and aim points; keeps a global registry used by AI perception, spawning, minimap and scoreboard.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-200)]
    public class Combatant : MonoBehaviour
    {
        private static readonly List<Combatant> Registry = new List<Combatant>();
        public static IReadOnlyList<Combatant> All => Registry;

        [Header("Identity")]
        [SerializeField] private string displayName = "Soldier";
        [SerializeField] private TeamId team = TeamId.None;
        [SerializeField] private bool isLocalPlayer;
        [SerializeField] private bool isBot;

        [Header("Aim Points (used by AI + aim assist)")]
        [Tooltip("Eye / camera position. Used for line of sight checks.")]
        [SerializeField] private Transform eyePoint;
        [SerializeField] private Transform headPoint;
        [SerializeField] private Transform chestPoint;

        [Header("Runtime")]
        public CombatantStats stats = new CombatantStats();

        public Health Health { get; private set; }
        public string DisplayName { get => displayName; set => displayName = value; }
        public TeamId Team { get => team; set => team = value; }
        public bool IsLocalPlayer { get => isLocalPlayer; set => isLocalPlayer = value; }
        public bool IsBot { get => isBot; set => isBot = value; }
        public bool IsAlive => Health == null || Health.IsAlive;

        /// <summary>World velocity, written by the movement component each frame.</summary>
        public Vector3 Velocity { get; set; }

        /// <summary>Direction the combatant is looking (camera/aim direction). Defaults to transform.forward.</summary>
        public Vector3 ViewDirection { get; set; }

        /// <summary>Last time this combatant fired an unsuppressed weapon (minimap "red dot" rule).</summary>
        public float LastLoudShotTime { get; set; } = -999f;

        public float LastDeathTime { get; set; } = -999f;
        public float SpawnTime { get; set; }

        public Vector3 EyePosition => eyePoint != null ? eyePoint.position : transform.position + Vector3.up * 1.62f;
        public Vector3 HeadPosition => headPoint != null ? headPoint.position : transform.position + Vector3.up * 1.68f;
        public Vector3 ChestPosition => chestPoint != null ? chestPoint.position : transform.position + Vector3.up * 1.3f;
        public Vector3 Position => transform.position;

        public Transform EyePoint => eyePoint;
        public Transform HeadPoint => headPoint;
        public Transform ChestPoint => chestPoint;

        public void SetAimPoints(Transform eye, Transform head, Transform chest)
        {
            eyePoint = eye;
            headPoint = head;
            chestPoint = chest;
        }

        public bool IsHostileTo(Combatant other) => TeamUtil.AreHostile(this, other);

        protected virtual void Awake()
        {
            Health = GetComponent<Health>();
            ViewDirection = transform.forward;
            SpawnTime = Time.time;
        }

        protected virtual void OnEnable()
        {
            if (!Registry.Contains(this)) Registry.Add(this);
        }

        protected virtual void OnDisable()
        {
            Registry.Remove(this);
        }

        public static Combatant FindLocalPlayer()
        {
            foreach (var c in Registry)
                if (c != null && c.isLocalPlayer) return c;
            return null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Registry.Clear();
    }
}
