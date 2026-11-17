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
  - Checks hostile `Combatant`s within `BotDifficulty.visionRange` (`75 m` across current presets) and `fieldOfView` (`110°` half-angle `55°`, widened to `100°` half-angle when closer than `6 m`).
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
  - Uses a critically/under-damped second-order spring (`aimSpeed`, `zeta = 0.82` in combat for realistic micro-overshoot) driven from `Soldier.Yaw` / `Soldier.Pitch`, so weapon recoil and flinch visibly affect bot aim and must be tracked back onto the target.
  - Adds Perlin aim wander shrinking over time (`initialAimError -> settledAimError` at `errorDecay/s`, inflated by target lateral speed, own movement speed `> 2 m/s`, and `recentlyDamaged`) plus a finite target-acquisition reaction delay (`reactionTime`).
- **Weapon Handling & Movement Stance**:
  - **Fire discipline**: Full auto under `13 m` once aim error `< fireTolerance` (scaled with target angular width); controlled bursts (`burstSize`, `burstPause`) beyond `13 m`.
  - **ADS**: Engages beyond `9 m` or while pre-aiming corners in `Hunt` (`6..25 m`).
  - **Reloads**: Reloads when empty (auto-replenishing bot reserve when completely out) or opportunistically out of sight when mag `< 35%` and unseen for `> 1.5 s`.
  - **Strafe, Slide & Crouch Peek**: Alternates lateral strafe (`strafeChance`), slides into close-to-mid engagements when sprinting (`slideChance`, `6–14 m`), and periodically stands/crouches to peek behind waist-high cover.

---

## 5. Difficulty Presets (`Assets/LR2/Data/AI/BotDifficulty_*.asset`)

All 4 presets currently share `fieldOfView: 110°`, `visionRange: 75 m`, `detectionSpeed: 1`, `hearingMultiplier: 1`, `retreatHealth: 0.38`, `useCoverChance: 0.6`, `strafeInterval: 0.45..1.2 s`, and `burstPause: 0.18..0.4 s`. They differentiate feel and lethality as follows:

| Preset | Reaction (s) | Aim Speed | Init / Settled Error (°) | Error Decay (°/s) | Headshot % | Fire Tol (°) | Aggression | Strafe % | Slide % | Flank % | Burst Size |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| **Recruit** | 0.55–0.80 | 4.2 | 7.0 / 1.60 | 1.3 | 5% | 4.5 | 0.35 | 12% | 0% | 8% | 2–4 |
| **Regular** | 0.34–0.52 | 6.0 | 4.8 / 0.90 | 1.9 | 13% | 3.5 | 0.55 | 40% | 6% | 22% | 3–6 |
| **Hardened** | 0.24–0.38 | 7.8 | 3.4 / 0.55 | 2.6 | 22% | 3.0 | 0.70 | 62% | 15% | 35% | 4–7 |
| **Veteran** | 0.17–0.27 | 10.0 | 2.4 / 0.32 | 3.4 | 32% | 2.4 | 0.82 | 85% | 30% | 50% | 5–9 |

---

## 6. External / LLM Commander Integration Hooks (`Assets/LR2/Runtime/AI/BotTypes.cs`)

Because low-level aiming, cover, and pathing stay inside `BotController` + `TacticalBotBrain`, external AI or LLM commanders can plug in at two clean levels:

1. **Strategic Orders (`IBotDirectiveProvider`, recommended)**:
   - Implement `TryGetDirective(BotController bot, BotBlackboard blackboard, out BotDirective directive)` on a `MonoBehaviour` assigned to `BotController.directiveProvider` (polled every `directivePollInterval = 1.5 s`), or push async results anytime via `bot.IssueDirective(directive)`.
   - Serialize compact game state for prompts with `bot.Blackboard.ToJson(bot.Brain is TacticalBotBrain tb ? tb.CurrentGoal : BotGoal.Idle)` (`BotGoal`, integer health/ammo `%`, `reloading`, `underFire`, target name/visibility/distance/position, `secondsSinceSeen`, `knownEnemies`, and active `directive`), or inspect `bot.DescribeState()`.
   - Available `BotOrder` values: `None`, `FreeRoam`, `HoldPosition`, `MoveTo`, `Attack`, `FollowLeader`, `Flank`, `Retreat`.
2. **Full Brain Replacement (`IBotBrain`)**:
   - Implement `Initialize(BotController)`, `Think(BotBlackboard, float deltaTime) -> BotIntent`, and `Reset()`, then assign `bot.Brain = yourBrain;` for complete custom tactical control while still reusing `BotController`'s NavMesh motor, aim spring, and trigger logic.
