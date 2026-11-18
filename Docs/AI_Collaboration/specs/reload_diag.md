# Reload diagnosis and fix (B16, B78, D40, D43, D44)

Author: Claude (`claude-opus-5-5`), 2026-11-18. Evidence: visible pose checks C7-C10 (`Logs/LR2_PoseCheck_C*_mk18`).

## 1. "MK18 mag and charging handle never move" (B78, ex-B68): not a bug
The probe found the first transform named `Mag`/`Bolt`, which is the hidden duplicate under the inactive
`Mk18_Core_Unused` (first child of the gun). With inactive branches skipped (D43): mag 0.428 m, bolt 0.090 m on both the
tactical and the empty reload. Realism note: the bolt also moves on tactical reloads (they play the empty clip).

## 2. Measurement defect (D44)
The A/B block forced `WeaponBoneOverlap = 1` and never restored it, so after the default became 0 every later reload
measurement used the non-default value. The A/B now runs the alternative first and restores the default.

## 3. "The gun jumps at the end of the reload" (D40)
New metric `RELOAD-END-JUMP`: per-frame change of the gun's camera-relative pose in the last 0.4 s of the reload and 0.35 s
after it (max deg/frame, cm/frame, and the frame-to-frame jerk).

| Case | C9 (before) | C10 (after fix 1) |
|---|---|---|
| tactical | 3.68 deg/frame | 4.32 (at -0.02 s: the authored mag-seat hit, not the end) |
| empty | 0.97 | 1.40, jerk 0.49 (smooth) |
| walking tactical | 17.16 deg/frame | 5.26, but 11.3 cm/frame at +0.01 s |
| sprinting tactical | 8.61 | 10.54 |

Cause found: after an early (tactical) end the bridge kept reading the driver's `ik_hand_gun` helper while the playables
blended back to locomotion, measured against the clip's first frame at near-full weight. Fix 1 (C29): hold the last clip
pose and ease it out over 0.35 s (smoothstep); tactical blend-out 0.3 s. Walking and sprinting still snap at +0.01 s: the
remaining source is outside the bridge (Kinemation's own clip-driven weapon motion is about 14 deg while walking; under
investigation, next pose check).

## 4. Reload while sprinting
C9/C10: a reload started during a sprint ran hidden in the sprint carry (gun 47-50 deg off the view) and completed.
Fix (C29 + 09:43 follow-up): COD rule, the last press wins. Sprint pressed during a reload interrupts it (the rounds count
once the magazine has seated); a reload started while sprinting drops the sprint until it is done; bots finish reloads
before sprinting.

## 5. Gait (B23, D36)
Walk stance foot slide at 3.5 m/s: 1.92 m/s (C7) -> 0.69 (C8) -> -0.29 (C9) -> 0.36 (C10) with a stride table measured
from three blends. Remaining: per-foot asymmetry (left foot +3.1 m/s while "planted") points at phase misalignment between
the walk and sprint clips; a jog clip or phase-matched blend is the real fix.
