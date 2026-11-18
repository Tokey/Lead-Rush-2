# B67: Weapon heat, smoke, and heat shimmer

**Owner:** Codex (`gpt-6`, user handoff)  
**Reviewer:** Claude  
**Status:** implementation in progress

## Research and design

The Army's rifle/carbine manual describes retained heat radiating through the barrel, convection bringing cooler air in and creating an updraft, and heat along the barrel producing a mirage in the sight line. This points to a narrow, rising distortion plume near the muzzle, not a large colored volume around the whole weapon. [TC 3-22.9, §§2-19–2-23](https://fas.org/irp/doddir/army/tc3-22-9.pdf)

HDRP's Unlit Distortion Only mode exists for local heat distortion without drawing a colored surface; its camera Frame Settings must include the distortion pass. The project uses HDRP 17.5; its installed `Unlit.shader` exposes the same distortion-only and distortion-vector properties documented by Unity. [Unity Unlit material documentation](https://docs.unity3d.com/ja/Packages/com.unity.render-pipelines.high-definition%4010.5/manual/master-stack-unlit.html), [Frame Settings API](https://docs.unity3d.com/cn/Packages/com.unity.render-pipelines.high-definition%407.4/api/UnityEngine.Rendering.HighDefinition.FrameSettingsField.html)

The 20-round trigger is the user's readability rule, not a claim about a real rifle reaching visible incandescence after exactly 20 shots. A visible red cue will stay small, dim, and attached only to the muzzle area. Smoke is a brief, low-rate wisp after the same sustained burst; the soft noisy alpha mask replaces the untextured particle quads that currently read as blocks. Reloading clears accumulated heat and removes active smoke immediately, following the requested CoD-style reset.

## Acceptance targets

- Before 20 consecutive shots: no heat smoke or red glow. Pausing longer than two shot intervals breaks the consecutive-shot count.
- After 20 consecutive shots: a subtle localized red muzzle glow, faint barrel/muzzle shimmer, and sparse soft-edged smoke.
- Smoke particles live at most 0.45 seconds, have a non-square alpha edge, cast no shadows, and stop emitting when firing stops.
- The distortion is a small, faint upward plume at the muzzle and hot flame, without visible quads or whole-screen warping.
- Starting a reload clears heat, counters, smoke particles, glow, and weapon shimmer immediately.
- Run a visible first-person and third-person check after the current Unity editor is free; verify smoke remains readable against light and dark backgrounds and the distortion pass is enabled.
