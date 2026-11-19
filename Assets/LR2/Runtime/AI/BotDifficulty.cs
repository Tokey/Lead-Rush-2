using UnityEngine;

namespace LR2.AI
{
    /// <summary>Human-likeness knobs for bots. Presets: Recruit, Regular, Hardened, Veteran.</summary>
    [CreateAssetMenu(menuName = "LR2/AI/Bot Difficulty", fileName = "BotDifficulty")]
    public class BotDifficulty : ScriptableObject
    {
        public string displayName = "Regular";

        [Header("Perception")]
        [Tooltip("Seconds from spotting a target to the first shot (random in range).")]
        public Vector2 reactionTime = new Vector2(0.32f, 0.5f);
        [Range(40f, 180f)] public float fieldOfView = 110f;
        public float visionRange = 70f;
        [Tooltip("How fast the detection meter fills (1 = normal).")]
        public float detectionSpeed = 1f;
        public float hearingMultiplier = 1f;

        [Header("Aim")]
        [Tooltip("Aim spring frequency. Higher = snappier flicks.")]
        public float aimSpeed = 6.5f;
        [Tooltip("Aim error (degrees) right after acquiring a target.")]
        public float initialAimError = 4.5f;
        [Tooltip("Aim error remaining after settling on a target (degrees).")]
        public float settledAimError = 0.6f;
        [Tooltip("How fast the error shrinks while tracking (per second).")]
        public float errorDecay = 2f;
        [Range(0f, 1f)] public float headshotChance = 0.14f;
        [Tooltip("Max angle (deg) between aim and target before the bot pulls the trigger.")]
        public float fireTolerance = 3.5f;

        [Header("Combat behaviour")]
        [Range(0f, 1f)] public float aggression = 0.55f;
        [Range(0f, 1f)] public float strafeChance = 0.45f;
        public Vector2 strafeInterval = new Vector2(0.45f, 1.2f);
        [Range(0f, 1f)] public float crouchPeekChance = 0.15f;
        [Range(0f, 1f)] public float slideChance = 0.1f;
        [Range(0f, 1f)] public float flankChance = 0.25f;
        [Tooltip("Bullets per burst beyond close range.")]
        public Vector2Int burstSize = new Vector2Int(3, 6);
        public Vector2 burstPause = new Vector2(0.18f, 0.4f);
        [Range(0f, 1f)] public float retreatHealth = 0.38f;
        [Range(0f, 1f)] public float useCoverChance = 0.6f;

        [Header("Composure (D61)")]
        [Tooltip("0..1: how little being shot at, a strafing target and the bot's own movement widen its aim (1 = unaffected). " +
                 "Before D61 every bot stacked about 5 deg of extra error in a real fight, so even Veterans rarely won.")]
        [Range(0f, 1f)] public float composure = 0.25f;

        public float RandomReaction() => Random.Range(reactionTime.x, reactionTime.y);
    }
}
