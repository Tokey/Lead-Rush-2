using UnityEngine;

namespace LR2.Combat
{
    /// <summary>
    /// Team affiliation. <see cref="None"/> = free-for-all (hostile to everyone except itself).
    /// <see cref="Hostile"/> = world threats (orb drones) that attack every soldier.
    /// </summary>
    public enum TeamId
    {
        None = 0,
        Alpha = 1,
        Bravo = 2,
        Hostile = 3,
        // Skirmish lobby (user 11-19: "3 way, 4 way ... which team they can be in")
        Charlie = 4,
        Delta = 5
    }

    public static class TeamUtil
    {
        public static readonly Color FriendlyColor = new Color(0.30f, 0.66f, 1.00f);
        public static readonly Color EnemyColor = new Color(1.00f, 0.30f, 0.27f);
        public static readonly Color NeutralColor = new Color(0.95f, 0.75f, 0.25f);

        public static bool AreHostile(Combatant a, Combatant b)
        {
            if (a == null || b == null) return true;
            if (a == b) return false;
            return AreHostile(a.Team, b.Team);
        }

        public static bool AreHostile(TeamId a, TeamId b)
        {
            if (a == TeamId.None || b == TeamId.None) return true;
            return a != b;
        }

        public static string DisplayName(TeamId team)
        {
            switch (team)
            {
                case TeamId.Alpha: return "TASK FORCE";
                case TeamId.Bravo: return "SYNDICATE";
                case TeamId.Hostile: return "DRONES";
                case TeamId.Charlie: return "VANGUARD";
                case TeamId.Delta: return "REAPERS";
                default: return "FREE FOR ALL";
            }
        }

        public static readonly Color DroneColor = new Color(1f, 0.85f, 0.2f);

        /// <summary>
        /// B116 (user 11-19 "make the colors consistent through the game"): one fixed colour per team everywhere, from the lobby
        /// to uniforms, lasers, kill feed, minimap and scoreboard. The player's team is always Alpha, so blue always means your
        /// side and red the main enemy; Charlie is green, Delta gold.
        /// </summary>
        public static Color TeamColor(TeamId team)
        {
            switch (team)
            {
                case TeamId.Alpha: return FriendlyColor;
                case TeamId.Bravo: return EnemyColor;
                case TeamId.Charlie: return new Color(0.42f, 0.86f, 0.38f);
                case TeamId.Delta: return new Color(1.00f, 0.80f, 0.25f);
                default: return new Color(0.85f, 0.85f, 0.85f);
            }
        }

        /// <summary>Short team label for lobby rows ("ALPHA", "FFA").</summary>
        public static string ShortName(TeamId team) => team == TeamId.None ? "FFA" : team.ToString().ToUpperInvariant();

        /// <summary>
        /// The team whose colours a soldier shows: his own in team games; in free-for-all Alpha (blue) for the viewer himself
        /// (the local player when there is no viewer) and Bravo (red) for everyone else.
        /// </summary>
        public static TeamId ColorTeam(Combatant viewer, Combatant other)
        {
            if (other == null) return TeamId.None;
            if (other.Team != TeamId.None) return other.Team;
            bool self = viewer != null ? viewer == other : other.IsLocalPlayer;
            return self ? TeamId.Alpha : TeamId.Bravo;
        }

        /// <summary>The one colour rule (B116): team colour, drones gold-yellow, free-for-all you blue and the rest red.</summary>
        public static Color ColorFor(Combatant viewer, Combatant other)
        {
            if (other == null) return NeutralColor;
            if (other.Team == TeamId.Hostile) return DroneColor;
            return TeamColor(ColorTeam(viewer, other));
        }

        /// <summary>Saturated laser beam colour with the same hue as the team colour.</summary>
        public static Color LaserColor(TeamId team)
        {
            switch (team)
            {
                case TeamId.Alpha: return new Color(0.14f, 0.52f, 1f, 0.96f);
                case TeamId.Charlie: return new Color(0.05f, 1f, 0.22f, 0.96f);
                case TeamId.Delta: return new Color(1f, 0.70f, 0.04f, 0.96f);
                default: return new Color(1f, 0.08f, 0.025f, 0.94f);
            }
        }

        /// <summary>Colour of <paramref name="other"/> as seen by <paramref name="viewer"/> (white for the viewer himself).</summary>
        public static Color RelationColor(Combatant viewer, Combatant other)
        {
            if (other == null) return NeutralColor;
            if (viewer == other) return Color.white;
            return ColorFor(viewer, other);
        }
    }
}
