# LeadRush 2 Bot AI Architecture & Tuning Guide

LeadRush 2 bots drive the exact same `SoldierController`, `SoldierMovement`, `LR2Firearm`, and hitbox/retarget stack as the local player. Every bot implements `ISoldierInputSource` inside `BotController` (`Assets/LR2/Runtime/AI/BotController.cs`), producing frame-by-frame `SoldierInput` (movement stick, look angles, sprint, crouch, slide, jump, ADS, reload, and trigger) rather than teleporting or bypassing recoil and physics.

---

## 1. High-Level Data Flow

```
GameEvents (Noise, NearMiss, Health.Damaged, TeamSighting)
        │
        ▼
  BotPerception          (10 Hz vision rays + hearing + memory + delayed team share)
        │
        ▼
  BotBlackboard          (self health/ammo/pose, best target, last-known pos, noise, directives)
        │
        ├─▶ IBotDirectiveProvider (slow 1.5 s commander hook, e.g. LLM strategic orders)
        │
        ▼
  IBotBrain / TacticalBotBrain (4 Hz utility goal selection -> BotIntent)
        │
        ▼
  BotController          (NavMesh steering, critically-damped aim spring, burst/ADS/crouch/slide)
        │
        ▼
  SoldierController      (same physics, weapon ballistics, recoil, reload, and footstep audio as player)
```

---

## 2. Perception (`Assets/LR2/Runtime/AI/BotPerception.cs`)

`BotPerception` simulates human-like situational awareness with staggered updates (`VisionInterval = 0.1 s`, randomized start offset per bot):

- **Vision cone & partial stealth**:
  - Checks hostile `Combatant`s within the active `BotDifficulty.visionRange` (the runtime fallback is `70 m`) and `fieldOfView` (the runtime fallback is `110°` total, halved for the cone and widened to `100°` total at distances below `6 m`).
  - Line-of-sight tests both `HeadPosition` and `ChestPosition` against `LR2Layers.VisionMask`.
  - Visibility builds a continuous `detection` meter (`0..1.25`, spotted at `>= 1.0`): faster at closer range (`0.9 + 9 / dist`), boosted for sprinting/moving targets (`1.35x`), recent loud shots within `1 s` (`2.2x`), tight center-view angles `< 12°` (`1.4x`), and penalized for crouched targets (`0.7x`). When sight is lost, detection decays at `0.5/s` and threats expire after `MemorySeconds = 12 s`.
- **Hearing (`GameEvents.Noise`)**:
  - Gunshots, explosions, reloads, and loud footsteps within `e.radius * hearingMultiplier` create/update `ThreatRecord` awareness with distance-scaled spatial uncertainty (`fuzz <= min(6 m, 0.08 * dist)`).
- **Under-fire & near-miss (`OnDamaged`, `OnNearMiss`)**:
  - Taking damage immediately marks the attacker as `aware`, `attackedMe = true`, `detection >= 1`, and shares their position to teammates. Near-misses (`GameEvents.NearMiss`) raise `detection` to `>= 0.9`.
- **Team callouts**:
  - Team modes share newly spotted enemies across `TeamId` after a realistic `ShareDelay = 0.6 s` comms lag (partial `detection = 0.5`).
- **Target scoring (`PickBest`)**:
  - Scores aware threats using `+100` if visible, `-0.6 * dist`, `-4 * age`, `+18` hysteresis for current target, `+25` if they recently damaged this bot, and `-10` de-prioritization for `TeamId.Hostile` seeker drones vs enemy soldiers.

---

## 3. Tactical Decision Layer (`Assets/LR2/Runtime/AI/TacticalBotBrain.cs`)

Evaluated every `~0.25 s` (`thinkInterval` jittered `0.85x..1.15x`), `TacticalBotBrain` selects one priority-ordered `BotGoal`:

1. **Retreat (`BotGoal.Retreat`)**: Triggers when `health01 < retreatHealth` (`0.38`) while `recentlyDamaged` or `underFire` with an active target. Searches `CoverPoint.All` within `22 m` for line-of-sight-blocking cover (or moves away from threat up to `7 s` or until health recovers `> 0.85`). Fights back only if cornered (`targetDistance < 15 m`).
2. **External Directives (`BotOrder`)**: Honors non-`FreeRoam` commander orders (`HoldPosition`, `MoveTo`, `Attack`, `Retreat`, `Flank`, `FollowLeader`) until `directiveUntil` expires.
3. **Reload Behind Cover (`BotGoal.TakeCover`)**: When reloading in view of an enemy (`useCoverChance = 0.6`), seeks nearby cover within `10 m` before switching back to fire.
4. **Opportunistic Flank (`BotGoal.Flank`)**: If a visible/recent target stays within `3 m` of the same anchor for `> 6 s` (`flankChance`, `18 s` cooldown), picks a `TacticalPoint` roughly `90°` off the current engagement vector.
5. **Engage (`BotGoal.Engage`)**: Closes distance if beyond `desiredRange * 1.35`, backs slightly if `< 5 m` with low aggression (`< 0.5`), and crouches behind `crouchCover` points within `1.2 m`.
6. **Hunt (`BotGoal.Hunt`)**: Pursues `targetLastKnown` for up to `10 s` after losing sight while pre-aiming (`lookPoint`).
7. **Investigate (`BotGoal.Investigate`)**: Moves toward recent high-priority noises (`< 8 s`).
8. **Follow Squad Leader (`BotGoal.FollowLeader`)**: In team modes when `squadLeader` is set, holds a staggered rear/flank offset (`2.5..5 m` lateral, `4..8 m` back) and watches the flanking `±75°` sector.
9. **Patrol (`BotGoal.Patrol`)**: Scores `TacticalPoint.All` by `weight`, time since last visit, and comfortable ~`18 m` travel distance (falling back to random reachable `NavMesh` samples).

---

## 4. Navigation, Aiming, and Combat Execution (`Assets/LR2/Runtime/AI/BotController.cs`)

- **NavMesh Navigation + Unstuck**:
  - `NavMeshAgent` runs in decoupled mode (`updatePosition = false`, `updateRotation = false`, `autoTraverseOffMeshLink = false`), feeding desired direction vectors into `SoldierController` / `CharacterController`.
  - If off-mesh, samples within `6 m` to step/warp back onto the NavMesh.
  - `UpdateStuck` tracks horizontal speed `< 0.4 m/s` while movement input is active: triggers a jump/mantle (`input.jump = true`) at `1.1–1.2 s` and clears/repaths at `2.4 s`.
- **Aim Spring & Human Error**:
  - Uses a critically/under-damped second-order spring (`aimSpeed`, `zeta = 0.80` while engaging and `1.0` otherwise) driven from `Soldier.Yaw` / `Soldier.Pitch`, so weapon recoil and flinch visibly affect bot aim and must be tracked back onto the target.
  - Adds Perlin aim wander shrinking over time (`initialAimError -> settledAimError` at `errorDecay/s`, inflated by target lateral speed, own movement speed `> 2 m/s`, and `recentlyDamaged`) plus a finite target-acquisition reaction delay (`reactionTime`).
- **Weapon Handling & Movement Stance**:
  - **Fire discipline**: Full auto under `13 m` once aim error `< fireTolerance` (scaled with target angular width); controlled bursts (`burstSize`, `burstPause`) beyond `13 m`.
  - **ADS**: Engages beyond `9 m` or while pre-aiming corners in `Hunt` (`6..25 m`).
  - **Reloads**: Reloads when empty (auto-replenishing bot reserve when completely out) or opportunistically out of sight when mag `< 35%` and unseen for `> 1.5 s`.
  - **Strafe, Slide & Crouch Peek**: Alternates lateral strafe (`strafeChance`), slides into close-to-mid engagements when sprinting (`slideChance`, `6–14 m`), and periodically stands/crouches to peek behind waist-high cover.

---

## 5. Difficulty Presets (`Assets/LR2/Runtime/AI/BotDifficulty.cs`)

Recruit through Veteran are authored `BotDifficulty` assets. Their exact values are
data, not constants in the runtime, so the runtime does not claim that all presets share
one FOV, range, reaction interval, or burst table. The active profile reads reaction
time, perception, aim error, fire tolerance, aggression, cover, movement, and burst
settings from the selected asset.

Adaptive mode is the exception: `AdaptiveDifficultyTracker.FillAdaptiveProfile` creates
a bounded runtime profile. Its current code maps effective skill to:

- reaction time `0.52..0.16 s` minimum and `0.76..0.26 s` maximum;
- FOV `95..132°`, aim speed `4.4..10.2`, initial error `6.2..2.2°`, settled error
  `1.35..0.28°`;
- detection speed `0.80..1.38`, hearing multiplier `0.85..1.35`, and headshot chance
  clamped to `0.05..0.34`;
- burst size `2..4` minimum and `5..8` maximum, with pause ranges interpolated from
  `0.28..0.12 s` and `0.55..0.24 s`.

The adaptive profile is refreshed every `0.75 s` and leaves authored preset assets
unchanged.

---

## 6. External / LLM Commander Integration Hooks (`Assets/LR2/Runtime/AI/BotTypes.cs`)

Because low-level aiming, cover, and pathing stay inside `BotController` + `TacticalBotBrain`, external AI or LLM commanders can plug in at two clean levels:

1. **Strategic Orders (`IBotDirectiveProvider`, recommended)**:
   - Implement `TryGetDirective(BotController bot, BotBlackboard blackboard, out BotDirective directive)` on a `MonoBehaviour` assigned to `BotController.directiveProvider` (polled every `directivePollInterval = 1.5 s`), or push async results anytime via `bot.IssueDirective(directive)`.
   - Serialize compact game state for prompts with `bot.Blackboard.ToJson(bot.Brain is TacticalBotBrain tb ? tb.CurrentGoal : BotGoal.Idle)` (`BotGoal`, integer health/ammo `%`, `reloading`, `underFire`, target name/visibility/distance/position, `secondsSinceSeen`, `knownEnemies`, and active `directive`), or inspect `bot.DescribeState()`.
   - Available `BotOrder` values: `None`, `FreeRoam`, `HoldPosition`, `MoveTo`, `Attack`, `FollowLeader`, `Flank`, `Retreat`.
2. **Full Brain Replacement (`IBotBrain`)**:
   - Implement `Initialize(BotController)`, `Think(BotBlackboard, float deltaTime) -> BotIntent`, and `Reset()`, then assign `bot.Brain = yourBrain;` for complete custom tactical control while still reusing `BotController`'s NavMesh motor, aim spring, and trigger logic.

---

## 7. Human-Like Motor Control

The bot does not write camera rotations directly. It emits look deltas through
`ISoldierInputSource`, so aiming, recoil, body motion, and weapon sway pass through the
same `SoldierController` path as the human player.

### Target acquisition

For large angular errors, `BotController` uses a two-stage movement:

1. A ballistic flick whose duration follows Fitts's law:
   `MT = a + b * log2(1 + D / W)`, where `D` is angular target distance and
   `W` is the perceived angular width of the target.
2. A closed-loop correction after a short visual-feedback refractory period.

The ballistic segment uses the minimum-jerk quintic profile
`10u^3 - 15u^4 + 6u^5`. This avoids constant-speed snaps and gives the handoff to
tracking a continuous velocity. Endpoint scatter and corrective motion increase with
combat stress, movement, and target eccentricity.

### Recoil and motor noise

Recoil compensation is intentionally delayed by a human-scale reaction window rather
than cancelling recoil on the firing frame. Pull-down strength is reduced by sustained
spray heat, movement, and combat stress. Small tracking tremor is signal-dependent:
large rotational corrections create more noise than slow fine tracking, following the
Harris-Wolpert motor-noise principle.

### Why this matters

These constraints preserve counter-play. A bot can still acquire a target quickly, but
it cannot rotate instantaneously, perfectly cancel recoil, or produce identical
frame-perfect aim corrections across difficulty levels.

## 8. Adaptive Difficulty

When `LR2Settings.BotDifficulty == 4`, `AdaptiveDifficultyTracker` maintains a runtime
profile without changing the authored Recruit, Regular, Hardened, or Veteran assets.
It tracks player accuracy, headshot rate, close-range/rush tendency, crouch tendency,
and win/loss momentum using event-driven updates. `BotController` refreshes the runtime
profile every `0.75 s`; the resulting reaction, aim, aggression, cover, strafe, burst,
and flank parameters remain bounded so difficulty changes are gradual rather than
teleporting between presets.

## 9. Killcam Fidelity

`KillcamDirector` records soldier snapshots and full visual-puppet poses at `60 Hz`.
Replay uses the recorded eye position, eye rotation, ADS field of view, weapon pose,
tracers, muzzle flashes, and hit events. The victim puppet is collapsed after the fatal
hit so the replay ends with the actual death rather than cutting away at the shot.

## References

1. Fitts, P. M. (1954), *The information capacity of the human motor system in
   controlling the amplitude of movement*, Journal of Experimental Psychology 47(6).
2. MacKenzie, I. S. (1992), *Fitts' law as a research and design tool in human-computer
   interaction*, Human-Computer Interaction 7(1).
3. Flash, T. & Hogan, N. (1985), *The coordination of arm movements: an experimentally
   confirmed mathematical model*, Journal of Neuroscience 5(7).
4. Meyer, D. E. et al. (1988), *Optimality in human motor performance: ideal control of
   rapid aimed movements*, Psychological Review 95(3).
5. Harris, C. M. & Wolpert, D. M. (1998), *Signal-dependent noise determines motor
   planning*, Nature 394.
6. Todorov, E. (2004), *Optimality principles in sensorimotor control*, Nature
   Neuroscience 7.
7. Schrum, J., Karpov, I. V. & Miikkulainen, R. (2011/2012), *UT2: Human-like
   behavior via neuroevolution of combat behavior and replay of human traces*.
