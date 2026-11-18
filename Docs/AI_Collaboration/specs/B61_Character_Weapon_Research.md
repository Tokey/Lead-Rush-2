# B61: Character, weapon, movement, and combat-feel research backlog

## Scope and method

This is the character-and-gunplay slice of the broader realism and reliability backlog. It turns known animation, rig, weapon, and state bugs into small reviewable tasks, then adds concrete regression coverage for states that are easy to miss. It is not a claim that a numeric threshold by itself makes motion realistic: where LR2 already has a threshold, preserve it; where the result depends on feel, capture evidence and get a player review.

The project already has overlapping parent rows: **B23** (rig/animation pass), **B26** (weapon hold), **B35** (reload angle), **B37** (motion feel), **B47** (recoil model), **B49** (per-gun hand IK), **B50** (walking weapon motion), **B55** (default weapon and Humanoid setup), and **B59** (death ADS/weapon collision). Rows below are proposed decompositions or acceptance gaps for those parents. They should not be added to the tracker as duplicate defects. New work should be recorded as subtasks under the appropriate parent or a new parent only when the scope is genuinely distinct.

### Research findings

- Unity’s Humanoid Avatar and muscle tools support validating bone mapping, retargeting, and deformation on the target rig. The Animation Rigging two-bone IK and multi-aim constraints are available mechanisms for hand and aim alignment; they do not prescribe LR2’s weapon grip offsets.
- Unity 6 exposes collision-constrained `CharacterController.Move`, `Physics.CapsuleCast`, and damped `Animator.SetFloat` APIs. These can be used to probe weapon/body clearance, ground interactions, and state transitions without inventing transforms by eye.
- Microsoft’s Xbox Accessibility Guideline 117 explicitly includes camera bob, weapon/arm sway, shake, and motion blur as motion effects that should be adjustable or disableable. LR2’s motion controls should be tested at zero as well as at the default value.
- First-person camera-motion studies demonstrate that motion preferences and discomfort vary across users. They do not establish a universal “realistic” bob or shake amplitude. Treat the user’s feel check and an A/B capture as required evidence for motion tuning.
- First-person shooter kinematics research and FirstPersonScience show that aim and shot tasks can be logged quantitatively. LR2 should report measured movement, angle, timing, and error instead of claiming human-like behavior from an unvalidated heuristic.
- Acceptance values below use current LR2 criteria where available: hand-to-contact distance `< 3 cm`, planted-foot slide `< 0.5 m/s`, tactical charging-handle displacement `< 0.02 m`, and the B57 weapon-behind-eye plane floor `dot(weaponPos - eyePos, aimFwd) >= -0.08 m`. Other thresholds must be established in the relevant parent spec and accompanied by captured evidence.

## Proposed task register

Owners follow the current coordination board: Claude owns animation and weapon handling; Gemini owns character/gameplay state and builders; Copilot owns QA tooling and review work; Codex owns independent checks and research/docs. A row marked with an existing B-number is an overlap or decomposition of that row, not a new tracker item.

### Rig, body, and weapon assets

| ID | Owner | Existing parent / relation | Atomic task and acceptance evidence |
|---|---|---|---|
| C01 | Gemini | B55; decomposition | Audit player and bot Humanoid Avatars for required-bone and mapping errors. Record per-avatar validation; no required bone is unmapped. |
| C02 | Gemini | B23; gap | Check soldier rig scale and proportions against the measured camera, capsule, and weapon grip anchors. Record measurements and correct any rig-to-gameplay scale mismatch. |
| C03 | Claude | B23/B49; decomposition | Preview idle, walk, crouch, prone, reload, and death clips on the LR2 rig. Log and fix visible twist, stretch, or self-overlap before accepting the rig. |
| C04 | Claude | B23; gap | Preview shoulder, elbow, wrist, and finger muscle limits at animation extremes. No glove, sleeve, or hand mesh visibly collapses or overlaps the weapon. |
| C05 | Gemini | B23; gap | Inspect helmet, mask, gloves, vest, and uniform fit on player and bot bodies through all supported stances. Capture close and full-body views; no visible mesh intersections. |
| C06 | Gemini | B23; gap | Check first-person and third-person visibility, layer, and camera-culling setup. The local view must not show the player’s head or unexpectedly hide hands/weapons. |
| C07 | Gemini | B49/B55; decomposition | Verify each weapon prefab’s muzzle, sight, grip, magazine, and charging-handle transforms against the visible mesh. Record offsets for all five guns. |
| C08 | Claude | B49/B55; decomposition | Compare the five weapon rigs’ Humanoid controller/profile assignments and rest poses. Remove Generic-only layer/profile mismatches; capture the per-gun validation. |

### Grip, aiming, and close-range collision

| ID | Owner | Existing parent / relation | Atomic task and acceptance evidence |
|---|---|---|---|
| C09 | Claude | B26/B49; decomposition | Capture left- and right-hand distance to grip/handguard for all five guns at idle and ADS. Both contacts meet LR2’s existing `< 3 cm` target. |
| C10 | Claude | B26; decomposition | Inspect trigger-finger placement on every gun in idle, ADS, firing, and reload poses. Finger does not pass through the receiver and only curls onto the trigger in the intended pose. |
| C11 | Claude | B26; decomposition | Inspect support thumb and palm on each rifle in idle, ADS, walking, and reload. No thumb protrudes through the receiver or floats visibly away from the handguard. |
| C12 | Claude | B49; decomposition | Validate per-weapon grip offsets in camera-space and weapon-space. Switching weapons causes no visible hand jump; retain per-gun pose captures. |
| C13 | Claude | B49/B55; gap | Measure optic alignment at each gun’s ADS target. Record sight picture, aim point, and shot direction; they agree within a tolerance set in the weapon spec. |
| C14 | Claude | B37/B49; gap | Check ADS entry and exit on every gun. Aim blend reaches endpoints without snap or overshoot, and fully releases when aim input ends. |
| C15 | Gemini | B53/B49; gap | Verify player and bot muzzle direction against intended hit direction at near, middle, and long range. Captured target and ray data agree within a declared angular tolerance. |
| C16 | Claude | B59; decomposition | Sweep each weapon against walls, door frames, and cover at several approach angles. Weapon does not vanish through geometry or jump behind the eye plane. |
| C17 | Gemini | B59; decomposition | Test close-range death with an enemy inside barrel reach. Weapon stays in front of the eye plane under the existing B57 dot-product limit. |
| C18 | Gemini | B59; gap | Repeat close-range death, respawn, and weapon re-equip. No stale weapon transform or collision mask survives into the next life. |

### Locomotion, stance, and foot contact

| ID | Owner | Existing parent / relation | Atomic task and acceptance evidence |
|---|---|---|---|
| C19 | Claude | B37; decomposition | Compare root movement speed with each walk/run clip’s stride speed for forward, backward, and lateral movement. Report the measured mismatch for each direction. |
| C20 | Claude | B9/B37; decomposition | Measure planted-foot sliding while walking and sprinting on flat ground. Keep the planted-foot result below LR2’s existing `0.5 m/s` target. |
| C21 | Claude | B23/B37; gap | Measure foot contact separately for crouch and prone movement; report each stance and each foot so one passing stance cannot mask another failing. |
| C22 | Claude | B37; gap | Repeat gait capture at 30, 60, and 120 FPS. Animation speed and planted-foot displacement remain within the same declared acceptance limits. |
| C23 | Claude | B9; decomposition | Compare each foot-plant frame with its footstep timestamp. Report per-foot timing error from captured animation samples; stride distance alone does not satisfy this check. |
| C24 | Claude | B23/B37; gap | Walk and sprint over slopes, steps, and uneven ground. Feet follow terrain without persistent floating, sinking, or knee snapping. |
| C25 | Claude | B37; decomposition | Test starts, stops, sharp turns, and direction reversals. Movement and animation blend without skating or a one-frame pose pop. |
| C26 | Gemini | B37; gap | Verify normalized diagonal movement. Cardinal and diagonal movement inputs produce equal maximum horizontal speed within the movement test’s measurement tolerance. |
| C27 | Claude | B37/B50; gap | Inspect walk-to-sprint and sprint-to-ADS transitions with all five weapons. No weapon/hand pop; ADS settles to its expected pose. |
| C28 | Claude | B50/B44; decomposition | Measure body bob and weapon bob separately in walk, sprint, and ADS. Sprint remains stronger than walk, ADS is nearly still, and the relevant settings reduce motion to zero. |

### Reload, death, and action cleanup

| ID | Owner | Existing parent / relation | Atomic task and acceptance evidence |
|---|---|---|---|
| C29 | Claude | B35; decomposition | Measure absolute gun-to-camera angle during tactical and empty reloads for each gun. Log idle, midpoint, and end; meet the accepted LR2 angle limit. |
| C30 | Claude | B23/B35; gap | Verify hand contact with magazine and charging handle during each reload. Capture midpoint frames; hands neither miss the part nor clip through it. |
| C31 | Claude | B35; decomposition | Verify only one system drives reload weapon motion on the current rig. Record bridge/weapon-bone overlap and visual angle for both A/B variants. |
| C32 | Claude | B28/B34; decomposition | Test tactical reload with a round chambered. Ammo ends at magazine capacity plus one; charging-handle displacement stays below the existing `0.02 m` target. |
| C33 | Claude | B28/B35; gap | Test empty reload. Ammo, magazine insertion, charging-handle motion, and fire lockout agree with the clip. |
| C34 | Gemini | B59; gap | Cancel reload by death, weapon switch, and match reset. No stuck reload state, duplicated ammo, or blocked input remains. |
| C35 | Gemini | B57/B59; decomposition | On death while ADS, verify aiming state, animator parameter, input value, and recoil aiming state all clear within one frame. |
| C36 | Gemini | B59; gap | Test death/respawn after aim, reload, sprint, crouch, prone, and weapon swap. No action state leaks into the next life. |

### Shooting, recoil, and motion feedback

| ID | Owner | Existing parent / relation | Atomic task and acceptance evidence |
|---|---|---|---|
| C37 | Claude | B47; decomposition | For each gun, compare logged shot intervals with configured RPM over a fixed-duration burst. No measurable frame-rate-dependent fire-rate drift. |
| C38 | Claude | B47; decomposition | Record ten-shot hip and ADS recoil displacement per gun against B47’s accepted targets, including recovery-to-80% time and repeated seeded runs. |
| C39 | Claude | B47; gap | Repeat the same recoil sequence at 30, 60, and 120 FPS. Pattern and recovery remain within a tolerance declared in the recoil spec. |
| C40 | Claude | B47/B49; gap | Compare camera aim, muzzle forward, and projectile/hit ray at idle and while moving. Report any unexplained angular divergence. |
| C41 | Gemini | B13/B17; gap | Check muzzle flash, shell ejection, and shot sound against each shot event. No duplicate or missing effect during semiautomatic or automatic fire. |
| C42 | Gemini | B13; gap | Inspect shell ejection direction and camera clearance for every weapon. No case spawns inside the camera or the shooter. |
| C43 | Claude | B29/B44; decomposition | Measure camera-shake peak displacement and decay after shots, hits, and reload cues. Verify the zero setting removes nonessential motion. |
| C44 | Copilot | B34/B57; gap | Add a regression capture for recoil, gun angle, hands, and gait. The report identifies the exact gun, state, and failing metric. |

### Bots, third-person poses, and regression coverage

| ID | Owner | Existing parent / relation | Atomic task and acceptance evidence |
|---|---|---|---|
| C45 | Gemini | B23/B49; gap | Capture bot third-person idle, aim, fire, reload, walk, sprint, crouch, prone, and death poses. Report missing or invalid states per gun. |
| C46 | Gemini | B53/B49; gap | Verify bot muzzle direction follows its target across distance changes. Compare captured muzzle and target directions against a declared angular tolerance. |
| C47 | Gemini | B53; gap | Check bot target changes turn torso and weapon without snapping or exceeding the pose limits established for the rig. |
| C48 | Gemini | B53/B59; gap | Interrupt bot aim/reload/sprint with damage, target loss, and death. Every action state exits and no weapon or limb freezes. |
| C49 | Claude | B23/B59; gap | Inspect ragdoll transitions at close and medium range. No persistent body/weapon intersection; capture first and settled frames. |
| C50 | Copilot | B34/B49/B57; decomposition | Run pose and behavior checks for all five guns, required stances, and player/bot paths. Report exact failing gun, state, and metric. |

## Primary and authoritative sources

1. Unity, [Humanoid Avatar configuration](https://docs.unity3d.com/Manual/ConfiguringtheAvatar.html), [Avatar muscle definitions](https://docs.unity3d.com/Manual/MuscleDefinitions.html), [Humanoid animation retargeting](https://docs.unity3d.com/Manual/Retargeting.html), and [root motion](https://docs.unity3d.com/Manual/RootMotion.html). These describe Avatar mapping, retargeting, muscle ranges, and root-motion representation.
2. Unity Animation Rigging 1.2, [Two Bone IK Constraint](https://docs.unity3d.com/Packages/com.unity.animation.rigging@1.2/manual/constraints/TwoBoneIKConstraint.html) and [Multi-Aim Constraint](https://docs.unity3d.com/Packages/com.unity.animation.rigging@1.2/manual/constraints/MultiAimConstraint.html). These are candidate mechanisms only; check the package version already used by LR2 before adding dependencies.
3. Unity 6 Scripting API, [`CharacterController.Move`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/CharacterController.Move.html), [`Physics.CapsuleCast`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Physics.CapsuleCast.html), and [`Animator.SetFloat`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animator.SetFloat.html). These support collision-constrained movement, capsule sweeps, and damped state values.
4. Microsoft, [Xbox Accessibility Guideline 117: Visual distractions and motion settings](https://learn.microsoft.com/en-us/xbox/accessibility/xbox-accessibility-guidelines/117). It includes camera shake, bob, motion blur, and arm sway, and describes testing that such effects can be disabled or adjusted. This supports the zero-setting check; it does not set LR2’s default motion amplitude.
5. De Barros et al., [“A Study of First-Person Camera Movement in Video Games”](https://web.cs.wpi.edu/~gogo/papers/deBarros_chi_wip_2009.pdf). Its camera-motion preference results support visible A/B checks rather than a single assumed ideal bob curve.
6. Lubeck et al., [“Motion in images is essential to cause motion sickness symptoms, but not to increase postural sway”](https://research.vu.nl/ws/portalfiles/portal/1278189/lubeck%202015.pdf). The work studies visual motion including FPS footage; it supports treating comfort effects as user-variable.
7. [“Kinematic markers of skill in first-person shooter video games”](https://pmc.ncbi.nlm.nih.gov/articles/PMC10411933/) and [FirstPersonScience](https://arxiv.org/abs/2202.06429). These support capturing aim movement and shot-task measures. LR2 must validate its own metrics and should not label an untested bot heuristic as human behavior.

## Handoff notes

- B55/B49 overlap on Humanoid profiles, gun setup, and grip transforms; the rows above make per-asset acceptance explicit.
- B26/B49 overlap on hand placement. Their acceptance should be checked across all five guns and multiple poses, not inferred from one AK-12 capture.
- B35/B28/B34 overlap on reload timing, angle, charge-handle displacement, and ammo. Do not count a bridge-only angle check as evidence of absolute gun orientation.
- B37/B50/B9 overlap on movement feel, bob, gait, and footstep timing. Keep comfort settings independently testable and measure foot plants rather than stride distance alone.
- B47 owns recoil tuning. Its existing sourced table is the source of per-gun target values; this spec adds validation, frame-rate repeatability, and aim-to-hit checks rather than replacing those values.
- B57 should own the independent death/clip/music assertions it is already creating. C35/C17 clarify expected behavior; they do not authorize a second duplicate implementation of those assertions.
- The current code and asset state may change while parent rows are in progress. Review the live diffs before assigning any of these decompositions as fresh implementation tasks.
