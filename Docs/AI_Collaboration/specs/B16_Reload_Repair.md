# B16 / B35 Reload Animation Repair

**Owner:** Claude (`claude-opus-5-5`, user handoff 04:10; Codex's earlier code reopened)  
**Reviewer:** Codex  
**Related work:** B16 reload swing/walk reload, B35 vertical rifle, B23 animation rig pass, B28 tactical reload, B49 MK18 asset setup.

## Findings

The reload bridge and the Kinemation playable can both move the visible weapon. `ReloadMotionBridge.Apply` already has a residual-motion calculation, `motion = gunWeight * (1 - weaponBoneWeight * WeaponBoneOverlap)`, but the runtime default is `WeaponBoneOverlap = 0`. That selects the known-bad A/B mode for every weapon, including clips whose `WeaponBoneWeight` is 1.

The visible pose-check evidence demonstrates the defect and correction:

| Weapon | Old overlap 0 | New overlap 1 | Result |
|---|---:|---:|---|
| AK-74N | 23.5° bridge angle; 17.3° gun/camera deviation | 0°; 1.3° | Duplicate gun motion removed |
| FAL | 11.1°; 4.5° | 0°; 0.3° | Duplicate gun motion removed |
| MK18 in the 02:02 capture | 23.3°; 20.4° | 22.4°; 20.1° | Its old clip reports `WeaponBoneWeight=0`, so overlap cannot correct that capture |

Sources: `Logs/LR2_PoseCheck_AK74N/report.txt`, `Logs/LR2_PoseCheck_FAL/report.txt`, and `Logs/LR2_PoseCheck_Mk18/report.txt`. The paired AK-74N screenshots show the old and new front-view poses. The current generated MK18 prefab now references the LeadRushHDRP `W_Mk18_ReloadEmpty` mesh and the Humanoid AK-12 reload clip; the 02:02 pose capture predates that update, so a fresh visible run is required to judge those current assets.

The old MK18 QA report's `chargeMaxDisp=0` is not conclusive: the prefab contains duplicate `Bolt` children under active and inactive rigs, and the pose check takes the first matching name, including inactive transforms. The active bolt path is `Mk18/Root/Bolt`; verify it in the visible reload before treating the old metric as a confirmed gameplay defect.

## Reopened by the user (04:10 ET)

The user reports that the current MK18 reload is still badly broken while walking or sprinting and that the gun jumps at the end of the reload. Treat the overlap change as incomplete until the current generated MK18 passes a visible reload run. The current builder assigns `AA_AK12_Reload_Empty` to the MK18, while the historical MK18 report measured `WeaponBoneWeight=0` on the older clip; inspect the actual current curve and the bridge's last 0.2 seconds instead of assuming the overlap removes its motion.

Capture idle, walking, and sprinting reloads, especially the final 0.3 seconds. Compare the driver's gun bone, visible MK18 transform, and clip weight every frame. Identify whether the discontinuity comes from the clip ending, the weapon animator's mag/bolt reset, the `ReloadMotionBridge` weight fade, or the return to locomotion. Keep the visible gun and both hands continuous through reload exit; preserve the current tactical reload timing unless the capture shows it causes this fault. Do not alter Copilot's B60b foot-plant code during this repair.

## Repair plan and acceptance

1. Make overlap-aware motion the default. Clips with full weapon-bone weight then contribute no duplicate bridge rotation; clips with zero weight retain the bridge fallback.
2. Reset clip calibration and transition weight when the active firearm or item transform changes, preventing a cancelled reload's clip/rest calibration from carrying into the next weapon.
3. Keep the generated MK18 on the existing LeadRushHDRP mesh and Humanoid rifle reload. Use the iron-sight aim target in the builder and rebuild the generated prefab.
4. Run the visible pose check on MK18, AK-74N, FAL, and AK-12. Record raw weight curve, bridge angle, weapon angle, hand residual, screenshots at rest/tactical/empty/walking reload, and empty-bolt travel from the active MK18 rig.
5. Review the iron-sight ADS capture and player/enemy laser colors in a visible run. Source review alone does not verify those visual results.

The repair follows the reload/transition and weapon-pose guidance in [B60 HyperMotion Animation](B60_HyperMotion_Animation.md), including the cited [EA FC 24 gameplay deep dive](https://www.ea.com/games/ea-sports-fc/fc-24/news/fc-24-gameplay-deepdive) and [EA GDC motion-matching slides](https://media.gdcvault.com/gdc2023/Slides/Motion+Matching+at+EA_Delannoy_JC.pdf). It keeps the procedural weapon solver and limits the bridge to motion the playable has not already supplied.

## Validation status

The old/new overlap comparison is visible-run evidence already present in the cited reports. The runtime default and active-item reset still require a clean build, followed by a fresh visible pose run. The user-owned Unity Editor is currently open, so current-turn runtime verification is pending its release.
