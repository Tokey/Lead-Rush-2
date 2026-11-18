# B71: Post-death weapon and hand pose recovery

**Owner:** Claude (`claude-opus-5-5`, user handoff 04:10 ET)  
**Reviewer:** Codex  
**Status:** implementation and visible reproduction needed

## User report

After death/respawn, the weapon and character animation can be badly misaligned. Sprinting, firing, or reloading sometimes makes the pose recover. The user also reports a MK18 reload jump at clip end and worse reload motion while walking or sprinting; that repair is tracked separately in B16.

## Source findings to verify

1. `RetargetProxy.LateUpdate` returns while the soldier is dead. It does not clear the reload bridge's cached clip, calibration, or `_gunWeight`, nor the B60a hand-inertial offsets. On respawn, the first hand solve may consume state left by the interrupted action.
2. `SoldierController.ForceResetAimState` calls `ActiveItem.OnAimReleased()` before assigning hip FOV directly to the camera. Kinemation's `FPSCameraController.UpdateTargetFOV` caches the camera's current FOV; the later direct assignment can leave its cached start FOV at the pre-death ADS value. On re-enable, its `UpdateFOV` may replay stale zoom progress.
3. Foot-plant state also remains cached while the proxy skips its dead frames. The teleport guard normally clears it after a large respawn move, but explicit transient-state reset should cover same-position or short-distance respawns too. Preserve Copilot's active B60b solver and make only isolated reset changes.

## Work and acceptance

- Reproduce the issue by dying while aiming, reloading, and sprinting; respawn at both a nearby and distant spawn; inspect the first several seconds before and after sprint/fire/reload inputs.
- Reset reload bridge calibration/weight and hand inertial offsets when death interrupts pose updates. Reset foot-plant history at the same lifecycle boundary without rewriting the B60b planting algorithm.
- After directly assigning hip FOV, synchronize the camera controller's cached start/target FOV so re-enabling it cannot restore pre-death ADS zoom.
- Verify weapon and hands remain aligned before input, through ADS, sprint, fire, and reload after respawn. Capture before/after evidence and check Editor logs for animation or camera errors.
- Build Runtime and Editor assemblies before saving/starting the visible pose check, and run that check only after the user Editor lock is free.
