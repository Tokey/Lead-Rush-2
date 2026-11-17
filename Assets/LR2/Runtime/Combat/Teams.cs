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
        Hostile = 3
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
                default: return "FREE FOR ALL";
            }
        }

        /// <summary>Colour of <paramref name="other"/> as seen by <paramref name="viewer"/>.</summary>
        public static Color RelationColor(Combatant viewer, Combatant other)
        {
            if (other == null) return NeutralColor;
            if (viewer == other) return Color.white;
            return AreHostile(viewer, other) ? EnemyColor : FriendlyColor;
        }
    }
}
