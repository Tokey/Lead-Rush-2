using LR2.Combat;
using LR2.Core;
using LR2.Game;
using UnityEngine;

namespace LR2.AI
{
    /// <summary>
    /// Live Adaptive Difficulty Engine (Difficulty index 4 = "ADAPTIVE"):
    /// Observes human player combat telemetry without touching static presets (Recruit..Veteran) and
    /// computes a rolling <see cref="Skill01"/> + playstyle counters (anti-rush vs anti-camp vs evasion)
    /// so Adaptive bots naturally learn and match the player's intensity throughout each match.
    /// </summary>
    public static class AdaptiveDifficultyTracker
    {
        public static float Skill01 { get; private set; } = 0.48f;
        public static float PlayerAccuracy01 { get; private set; } = 0.25f;
        public static float PlayerHeadshotRate01 { get; private set; } = 0.12f;
        public static float PlayerRushTendency01 { get; private set; } = 0.50f;
        public static float PlayerCrouchRate01 { get; private set; } = 0.25f;

        private static int _playerShots;
        private static int _playerHits;
        private static int _playerHeadshots;
        private static int _playerKills;
        private static int _playerDeaths;
        private static int _playerStreak;
        private static int _botStreak;
        private static int _cqbKills;
        private static int _totalEncounters;
        private static bool _subscribed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Skill01 = 0.48f;
            PlayerAccuracy01 = 0.25f;
            PlayerHeadshotRate01 = 0.12f;
            PlayerRushTendency01 = 0.50f;
            PlayerCrouchRate01 = 0.25f;
            _playerShots = _playerHits = _playerHeadshots = 0;
            _playerKills = _playerDeaths = _playerStreak = _botStreak = 0;
            _cqbKills = _totalEncounters = 0;
            if (_subscribed)
            {
                GameEvents.ShotFired -= OnShotFired;
                GameEvents.Hit -= OnHit;
                GameEvents.Killed -= OnKilled;
                GameEvents.MatchStateChanged -= OnMatchState;
                _subscribed = false;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureSubscribed()
        {
            if (_subscribed) return;
            _subscribed = true;
            GameEvents.ShotFired += OnShotFired;
            GameEvents.Hit += OnHit;
            GameEvents.Killed += OnKilled;
            GameEvents.MatchStateChanged += OnMatchState;
        }

        private static void OnMatchState(MatchStateEvent e)
        {
            if (e.current == MatchState.Countdown)
            {
                _playerShots = _playerHits = _playerHeadshots = 0;
                _playerKills = _playerDeaths = _playerStreak = _botStreak = 0;
                _cqbKills = _totalEncounters = 0;
                Skill01 = 0.48f;
            }
        }

        private static void OnShotFired(ShotEvent e)
        {
            if (e.shooter != null && e.shooter.IsLocalPlayer)
                _playerShots++;
        }

        private static void OnHit(HitEvent e)
        {
            if (e.attacker != null && e.attacker.IsLocalPlayer)
            {
                _playerHits++;
                if (e.zone == HitZone.Head) _playerHeadshots++;
                PlayerAccuracy01 = Mathf.Lerp(PlayerAccuracy01, Mathf.Clamp01((float)_playerHits / Mathf.Max(8, _playerShots)), 0.15f);
                PlayerHeadshotRate01 = Mathf.Clamp01((float)_playerHeadshots / Mathf.Max(4, _playerHits));
            }
        }

        private static void OnKilled(KillEvent e)
        {
            if (e.victim == null || e.killer == null) return;
            bool playerWonDuel = e.killer.IsLocalPlayer;
            bool playerDied = e.victim.IsLocalPlayer;

            if (playerWonDuel)
            {
                _playerKills++;
                _playerStreak++;
                _botStreak = 0;
                _totalEncounters++;
                if (e.distance < 15f) _cqbKills++;
                PlayerRushTendency01 = Mathf.Lerp(PlayerRushTendency01, e.distance < 15f ? 1f : 0f, 0.25f);

                // Player performing strongly -> ramp adaptive bot skill smoothly up
                float boost = 0.045f + (_playerStreak >= 2 ? 0.03f : 0f) + (e.headshot ? 0.02f : 0f) + PlayerAccuracy01 * 0.03f;
                Skill01 = Mathf.Clamp(Skill01 + boost, 0.12f, 0.96f);
            }
            else if (playerDied)
            {
                _playerDeaths++;
                _botStreak++;
                _playerStreak = 0;
                _totalEncounters++;

                // Player losing consecutive duels -> ease adaptive pressure slightly so fights stay competitive
                float ease = 0.040f + (_botStreak >= 2 ? 0.035f : 0f);
                Skill01 = Mathf.Clamp(Skill01 - ease, 0.12f, 0.96f);
            }

            if (e.victim != null && e.victim.IsLocalPlayer)
            {
                var sc = e.victim.GetComponent<LR2.Characters.SoldierController>();
                if (sc != null) PlayerCrouchRate01 = Mathf.Lerp(PlayerCrouchRate01, sc.IsCrouching ? 1f : 0f, 0.25f);
            }
        }

        /// <summary>Populates a runtime BotDifficulty copy tailored to live player performance &amp; tactics.</summary>
        public static void FillAdaptiveProfile(BotDifficulty dst, BotDifficulty fallbackTemplate)
        {
            if (dst == null) return;
            EnsureSubscribed();

            float s = Skill01;
            float accBonus = Mathf.Clamp01((PlayerAccuracy01 - 0.20f) * 1.4f);
            float hsBonus = Mathf.Clamp01(PlayerHeadshotRate01 * 1.5f);
            float effectiveS = Mathf.Clamp01(s * 0.78f + accBonus * 0.22f);

            dst.displayName = "Adaptive";
            float rMin = Mathf.Lerp(0.52f, 0.16f, effectiveS);
            float rMax = Mathf.Lerp(0.76f, 0.26f, effectiveS);
            dst.reactionTime = new Vector2(rMin, rMax);
            dst.fieldOfView = Mathf.Lerp(95f, 132f, effectiveS);
            dst.visionRange = fallbackTemplate != null ? fallbackTemplate.visionRange : 70f;
            dst.detectionSpeed = Mathf.Lerp(0.80f, 1.38f, effectiveS);
            dst.hearingMultiplier = Mathf.Lerp(0.85f, 1.35f, effectiveS);

            dst.aimSpeed = Mathf.Lerp(4.4f, 10.2f, effectiveS);
            dst.initialAimError = Mathf.Lerp(6.2f, 2.2f, effectiveS);
            dst.settledAimError = Mathf.Lerp(1.35f, 0.28f, effectiveS);
            dst.errorDecay = Mathf.Lerp(1.35f, 3.5f, effectiveS);
            dst.headshotChance = Mathf.Clamp(Mathf.Lerp(0.06f, 0.30f, effectiveS * 0.7f + hsBonus * 0.3f), 0.05f, 0.34f);
            dst.fireTolerance = Mathf.Lerp(4.8f, 2.2f, effectiveS);

            // Counter player playstyle: high rush players face slightly lower blind aggression (more crossfires/cover);
            // slow passive players face stronger flank push!
            float antiCampFlank = Mathf.Clamp01(0.25f + (1f - PlayerRushTendency01) * 0.35f);
            dst.aggression = Mathf.Clamp(Mathf.Lerp(0.45f, 0.78f, effectiveS) - PlayerRushTendency01 * 0.12f, 0.25f, 0.85f);
            dst.strafeChance = Mathf.Lerp(0.28f, 0.76f, effectiveS * 0.75f + accBonus * 0.25f);
            dst.strafeInterval = new Vector2(Mathf.Lerp(0.75f, 0.32f, effectiveS), Mathf.Lerp(1.45f, 0.75f, effectiveS));
            dst.crouchPeekChance = Mathf.Lerp(0.10f, 0.32f, effectiveS * 0.65f + PlayerCrouchRate01 * 0.35f);
            dst.slideChance = Mathf.Lerp(0.04f, 0.26f, effectiveS);
            dst.flankChance = Mathf.Lerp(0.18f, 0.48f, effectiveS * 0.6f + antiCampFlank * 0.4f);
            dst.burstSize = new Vector2Int(Mathf.RoundToInt(Mathf.Lerp(2, 4, effectiveS)), Mathf.RoundToInt(Mathf.Lerp(5, 8, effectiveS)));
            dst.burstPause = new Vector2(Mathf.Lerp(0.28f, 0.12f, effectiveS), Mathf.Lerp(0.55f, 0.24f, effectiveS));
            dst.retreatHealth = Mathf.Lerp(0.28f, 0.44f, effectiveS);
            dst.useCoverChance = Mathf.Lerp(0.42f, 0.82f, effectiveS);
        }
    }
}
