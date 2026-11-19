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

        /// <summary>Fixed colour per team for lobby rows, the spectator overlay and scoreboards.</summary>
        public static Color TeamColor(TeamId team)
        {
            switch (team)
            {
                case TeamId.Alpha: return new Color(0.30f, 0.66f, 1.00f);
                case TeamId.Bravo: return new Color(1.00f, 0.33f, 0.28f);
                case TeamId.Charlie: return new Color(0.42f, 0.86f, 0.38f);
                case TeamId.Delta: return new Color(1.00f, 0.80f, 0.25f);
                default: return new Color(0.85f, 0.85f, 0.85f);
            }
        }

        /// <summary>Short team label for lobby rows ("ALPHA", "FFA").</summary>
        public static string ShortName(TeamId team) => team == TeamId.None ? "FFA" : team.ToString().ToUpperInvariant();

        /// <summary>Colour of <paramref name="other"/> as seen by <paramref name="viewer"/>.</summary>
        public static Color RelationColor(Combatant viewer, Combatant other)
        {
            if (other == null) return NeutralColor;
            if (viewer == other) return Color.white;
            return AreHostile(viewer, other) ? EnemyColor : FriendlyColor;
        }
    }
}
