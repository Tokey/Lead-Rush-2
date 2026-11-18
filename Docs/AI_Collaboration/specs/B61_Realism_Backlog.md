# B61 — Sourced realism and reliability backlog

**Status:** Ready for cross-review as a task plan. Research and project scan are complete; implementation remains open and follows the existing bug and QA queue.

## Goal

Improve the game's visual and audio assets, character and weapon presentation, movement feel, and reliability as one coherent first-person experience. Realism comes from consistent scale, material response, lighting, motion, sound propagation, and stable behavior. Added detail must preserve enemy readability, fair sightlines, responsive input, and frame time.

## What the research recommends first

1. **Capture baselines before tuning.** Use repeatable per-map views, per-weapon poses, and audio routes. Record the current failure evidence and capture settings.
2. **Keep readability and performance as gates.** B1 is reopened. Fog, darker materials, clutter, vegetation, and grading must pass its visibility and contrast checks. P3 is incomplete and Foundry is over its provisional timing target, so expensive rendering changes need before/after timings.
3. **Correct asset scale, rig, and material response before adding detail.** Check meter scale, pivots, mesh intersections, hand/grip transforms, UV/detail scale, plausible dielectric/conductor response, and believable wear placement.
4. **Treat character/weapon motion and audio as measurable systems.** Check each gun and required stance separately. Validate movement against foot contact, hand IK, muzzle/aim alignment, reload state, and action cleanup. Preserve distinct direct weapon reports, mechanical layers, distance/occlusion behavior, tails, Foley, voices, ambience, and music.
5. **Use reproducible regression checks.** New checks must fail on a known-bad fixture and pass after the fix. Track missing media, asset references, performance, and audio virtualization rather than relying on compilation or visual impression alone.

The cited sources support production methods and Unity APIs. Game-specific targets such as 3 cm hand error, 0.5 m/s foot slide, B1 contrast, and mix thresholds come from LR2's current acceptance criteria and remain subject to visible playtest.

## Research workstreams

- [Environment assets, lighting, materials, terrain, and map presentation](B61_Environment_Research.md): 56 source templates expanded into **181 per-map and per-surface tasks**.
- [Character, weapon, animation, and movement](B61_Character_Weapon_Research.md): 50 source templates expanded into **93 per-weapon tasks**.
- [Audio assets, propagation, Foley, voices, music, and reliability](B61_Audio_Research.md): 60 source templates expanded into **116 per-weapon, per-map, and per-surface tasks**.

## Assigned leaf register

[`data/B61_task_register.csv`](../data/B61_task_register.csv) contains **390 independently claimable leaf tasks**: 181 environment, 93 character/gunplay, and 116 audio. Each row has a unique ID, one primary owner, optional support owner, parent research row, measurable acceptance evidence, dependency/gate, related existing tracker item, and status.

Primary-owner allocation is Gemini 244, Claude 82, Copilot 39, and Codex 25. These are long-run candidate tasks, not 390 simultaneous work items. The split follows the current file ownership: Gemini takes map/material/audio implementation, Claude takes character/weapon/animation and animation-linked Foley, Copilot takes asset/import/performance tooling, and Codex takes cross-cutting metrics, independent checks, and reviews. All rows start `OPEN`; agents should claim no more than two at a time under the current WIP rule. Existing B1/B30/B40/B42/P2/P3/B12/B36/B58 and B55/B59 work is linked in the rows so it can be verified or extended without silently duplicating the parent issue.

The register is reproducible with [`analysis/expand_b61_tasks.py`](../analysis/expand_b61_tasks.py). It expands map templates across Saltworks, Deadsector, Foundry, and TestRange; material checks across eight surface families; and weapon checks across AK-12, Mk23, AK-74N, Mk18, and FAL (rifle-only rows omit Mk23).

## Gates and sequencing

- **P0:** resolve B55/B58/B59 and establish B1/P3 baselines; catalog licensed/local assets; preserve known-bad evidence.
- **P1:** fix scale, geometry, material/lighting coherence, per-gun rig/aim/hand fit, and gun reports/propagation; capture one variable at a time.
- **P2:** extend map-specific set dressing, wear, weather, vegetation, glass, decals, Foley variation, ambience, and spatial mix while preserving B1.
- **P3:** run all-map, all-weapon, stance, and audio stress checks; compare to baselines and retain rollbacks for changes that hurt readability, feel, or frame time.

Never mark a source template complete without its task's evidence. Keep the B1 checks (visibility at least 0.05, enemy contrast at least 0.025) and P3 timings visible in every visual/performance pass; these are project gates, not universal realism standards.

## Primary sources

- Unity HDRP [physical light units](https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@17.5/manual/Physical-Light-Units.html), [global fog](https://github.com/Unity-Technologies/Graphics/blob/master/Packages/com.unity.render-pipelines.high-definition/Documentation~/create-a-global-fog-effect.md), [local fog](https://github.com/Unity-Technologies/Graphics/blob/master/Packages/com.unity.render-pipelines.high-definition/Documentation~/create-a-local-fog-effect.md), [contact shadows](https://github.com/Unity-Technologies/Graphics/blob/master/Packages/com.unity.render-pipelines.high-definition/Documentation~/Override-Contact-Shadows.md), [decals](https://github.com/Unity-Technologies/Graphics/blob/master/Packages/com.unity.render-pipelines.high-definition/Documentation~/decal-projector-reference.md), and [terrain materials](https://github.com/Unity-Technologies/Graphics/blob/master/Packages/com.unity.render-pipelines.high-definition/Documentation~/terrain-lit-material.md).
- Brent Burley, Disney Animation Studios, SIGGRAPH 2012, [Physically-Based Shading at Disney](https://www.disneyanimation.com/publications/physically-based-shading-at-disney/); Adobe [OpenPBR](https://experienceleague.adobe.com/en/docs/substance-3d/general-knowledge/openpbr/openpbr-overview).
- Unity [Humanoid Avatar](https://docs.unity3d.com/Manual/ConfiguringtheAvatar.html), [Two Bone IK](https://docs.unity3d.com/Packages/com.unity.animation.rigging@1.2/manual/constraints/TwoBoneIKConstraint.html), [CharacterController.Move](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/CharacterController.Move.html), and Microsoft [Xbox Accessibility Guideline 117: Motion Sickness](https://learn.microsoft.com/en-us/gaming/accessibility/xbox-accessibility-guidelines/117).
- Unity [AudioSource priority](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource-priority.html), [Audio Profiler](https://docs.unity3d.com/Manual/ProfilerAudio.html), [Audio Mixer](https://docs.unity3d.com/Manual/AudioMixer.html), ITU-R [BS.1770 loudness and true-peak measurement](https://www.itu.int/rec/R-REC-BS.1770/en), and Maher et al. [recorded firearm waveform variation](https://pubmed.ncbi.nlm.nih.gov/21476632/).

The three linked workstream specifications contain the project scan, detailed acceptance rationale, and additional source lists.
