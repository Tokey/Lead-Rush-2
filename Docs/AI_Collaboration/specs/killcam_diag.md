# Killcam diagnosis and fix (B14.1, D45-D48)

Author: Claude (`claude-opus-5-5`), 2026-11-18 09:18-09:45. Evidence: visible pose checks C9 (before) and C10 (after),
`Logs/LR2_PoseCheck_C9_mk18`, `Logs/LR2_PoseCheck_C10_mk18` (frames `kc_*`, report lines `KILLCAM*`). The harness lets a
hostile bot ADS and kill the player, then measures and captures eight moments of the replay.

## Requirement (user, PROMPTS.md 22:26 and 02:21)
"In the replay, the view should be exact as the player ... with recoil and stuff"; "show me dying at the end, show hitmarkers".

## Root causes found in C9

| # | Finding (measured) | Effect on screen | Cause |
|---|---|---|---|
| 1 | Lens at the SWAT skull estimate, 10.2 cm above the driver eye socket (KILLCAM-EYE) | ADS sight picture turned into a hip-fire view (probe `kc_00a` vs `kc_00b`) | D47 |
| 2 | Killer puppet copied with its head visible | head geometry at the lens | D45 |
| 3 | Synthetic gun kick + camera kick added on top of the recorded pose | double recoil, hands left behind by the extra gun kick (the recorded pitch already holds the real 0.18 deg view kick; the extra kick was 1.65 deg) | G34 |
| 4 | Victim collapse moved the puppet 0.82 m down | victim sank below the floor; only the dropped gun floated (`kc_08`) | D46 / G21 |
| 5 | Dead victim's low-health grade (CombatPostFX) | red, grey replay | D48 |

## Fix (C28)
Lens on the recorded driver eye socket (the camera transform is a child of the driver Head bone on every soldier);
stabilised aim rotation (it already holds the real per-shot view kick and the bot's own pull-down) plus Kinemation's
`FPSCameraShake` replayed per shot and the live `CameraShake` shot trauma; live ADS FOV formula; the killer copied with its
first-person renderers shadow-only (like the live player); body aligned to the lens like `RetargetProxy.AlignBody` with
both arms re-solved onto the recorded holds; recording continues 1.6 s past death so the victim's real ragdoll and dropped
gun replay (procedural collapse only as a fallback); replayed hitmarkers carry armour hit/break cues; neutral post-FX.
Follow-up after C10: puppets are copied under an inactive holder (no copied script wakes; `HeatHazeEmitter` NRE, D49) and
copied laser beams/dots are removed (frozen second beam).

## Result (C10)
Lens 1.49-1.53 m (was 1.59-1.62), killer head shadow-only, replay FOV 42.3 ADS / 58.7 hip (live formula), ADS sight
picture through the irons with the victim in the aperture (`kc_02`), killer hip view with the right hand on the grip and
the victim's ragdoll on the floor at the end (`kc_08`), no red grade. Open: the replay gun-bounds metric is polluted by
helper objects; use the muzzle transform next time.
