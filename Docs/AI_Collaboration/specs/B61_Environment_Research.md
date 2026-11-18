# B61 — Environment asset realism research and task breakdown

## Goal

Make LR2's four playable environments look grounded in believable scale, light, material response, weather, and use while preserving combat readability, traversal, and frame time. Realism comes from coherent relationships between these systems, not detail density alone. Calibrate first, improve one measurable system at a time, and retain paired before/after evidence.

The current renderer is Unity 6000.5 / HDRP 17.5. HDRP physical light units assume one Unity unit equals one meter. Unity's light-unit documentation gives reference illuminance and luminous-flux ranges, which gives the art pass a repeatable starting point instead of arbitrary brightness. For surface response, Disney's measured-material BRDF work and Adobe's OpenPBR guidance support treating conductors and dielectrics differently and using roughness to describe surface variation. Use those principles as authoring checks, not as hard-coded values for every asset.

## Existing work and overlap boundaries

- **B1 — readability is a gate.** The board currently reports B1 reopened: required visibility floor is 0.05 and enemy contrast is 0.025, with recent map results below those values. Fog, grime, weather darkening, SSAO, decals, and exposure changes must be measured against the same B1 checkpoints. No visual task should be marked complete while its affected map fails B1.
- **B30 — glass materials already exist.** `LR2GlassMaterials` generates clean, dirty, and cracked HDRP materials; the map builder references dirty and cracked glass. ENV-053 is a visible validation pass and integration follow-up, not a duplicate glass-material builder.
- **B40 — atmosphere code and scene placement already exist.** `AtmosphereFX` appears in the four playable scene YAMLs, and the map builder adds it. ENV-038 through ENV-041 validate placement, boundaries, visibility, and performance; do not add a second atmosphere component per map.
- **B42 — environment framework already exists.** `EnvironmentRealismProfile`, `AtmosphereFX`, and `SurfaceDecalPolicy` are present; the B42 spec defines the material, lighting, clutter, and atmosphere direction. At the time of this source scan, `EnvironmentRealismProfile` had no scene/builder/runtime consumer, so ENV-009 checks whether to wire it in or retire it. The four-map atmosphere hookup should be reviewed, not repeated.
- **P2 — decal policy is wired, art coverage remains.** `SurfaceDecalPolicy` is used by `ImpactSystem`. ENV-051 through ENV-053 cover distinct material variants, correct routing, bounded decal counts, and authored environmental wear. Do not replace the existing impact ring with persistent per-hit objects.
- **P3 — performance is a release gate.** The board reports Foundry above provisional 33.3 ms average / 50 ms p99 targets and incomplete captures for three maps. ENV-007 records the baseline; ENV-056 requires complete post-pass captures and reports regressions by map/effect. Treat the targets as provisional until the P3 owner confirms them.

Source scan was read-only in a worktree with active map edits. Recheck files and scene contents immediately before implementation because another agent may have changed them since this spec was drafted.

## How to scale this into hundreds of leaf tasks

The 56 rows below are workstream templates. Instantiate map-scoped rows separately for Saltworks (`SAL`), Deadsector (`DSC`), Foundry (`FND`), and TestRange (`TR`); instantiate material/decal rows once per relevant surface type and asset family. Use IDs such as `B61-E-051-FND-METAL`. Record `parent`, `map`, `material/asset`, `owner`, `dependency`, `artifact`, `acceptance`, and `status` for each leaf. This yields well over 200 independently verifiable map/surface tasks without padding the central tracker with broad duplicate rows.

## Prioritized work register

Owners below are proposals based on the current coordination board: Gemini owns map builders, maps, lighting, weather, and environment placement; Copilot owns research and focused tooling; Codex owns cross-cutting QA and documentation; Claude owns weapon/animation/footstep integration. Each row has a concrete pass condition.

### P0 — Baselines and asset integrity

| ID | Task | Proposed owner | Acceptance |
|---|---|---|---|
| B61-E-001 | Inventory environment meshes, prefabs, materials, textures, and references in the four playable maps. | Copilot | A machine-readable inventory maps each asset to its scene and reference chain; missing references are called out. |
| B61-E-002 | Capture controlled baseline images of matching viewpoints in each map. | Gemini | 1920×1080 captures use recorded scene, camera, route, and exposure settings so before/after comparisons are repeatable. |
| B61-E-003 | Build a reference sheet for each map's location, construction, materials, and weather. | Gemini | Each map has cited visual references and concise notes connecting them to specific asset or lighting decisions; external images are not committed without permission/license. |
| B61-E-004 | Audit landmark scale against player height and one-meter references. | Gemini | Doors, windows, cover, stairs, and representative props have measured dimensions and a correction list. |
| B61-E-005 | Confirm the 1 Unity unit = 1 meter convention in scene roots, light profiles, and environment prefabs. | Gemini | Any scale mismatches are corrected or documented; HDRP physical light values are judged at the verified scale. |
| B61-E-006 | Record the B1 readability baseline at the existing fixed test points. | Codex | All four maps have preserved visibility-floor and enemy-contrast results, including failures; tests use the board thresholds (0.05 and 0.025). |
| B61-E-007 | Record a P3 CPU/GPU timing baseline on fixed routes. | Copilot | Each of the four maps has a complete timing capture and the report identifies missing data and over-budget values. |
| B61-E-008 | Add an asset-integrity audit for playable scenes. | Copilot | The report finds zero unresolved material/texture references and zero missing or unsupported shaders in the audited scenes. |

### P1 — Geometry and set dressing

| ID | Task | Proposed owner | Acceptance |
|---|---|---|---|
| B61-E-009 | Apply `EnvironmentRealismProfile` per map, or remove it if it is not the selected tuning contract. | Gemini | Each map has an explicit, traceable source for realism values, with no unused profile pretending to control runtime or builder behavior. |
| B61-E-010 | Correct measured landmark proportions in each map. | Gemini | One or more landmark corrections per map are backed by measurements and before/after route captures. |
| B61-E-011 | Add appropriate edge geometry to close-range hero props that currently have razor-sharp silhouettes. | Gemini | Close and gameplay-distance captures show plausible edge highlights without visible bevel artifacts. |
| B61-E-012 | Fix floating or unintended intersecting environment props. | Gemini | Route-height screenshots show no visually floating assets or accidental geometry intersections in the selected audit set. |
| B61-E-013 | Make visible structural supports and attachments plausible. | Gemini | Major pipes, cables, lights, and signs have credible endpoints, brackets, or supports in the close-range audit views. |
| B61-E-014 | Break up obviously repeated prop instances. | Gemini | Repeated families vary in rotation, state, or material; no obvious identical sequence remains along audited primary routes. |
| B61-E-015 | Add small route-side clutter without obstructing traversal or competitive sightlines. | Gemini | Player and bot routes remain clear and the selected combat lanes retain their current visibility and target silhouettes. |
| B61-E-016 | Check body and weapon clipping around tight doors, corners, and cover. | Claude | Standing, crouched, prone, and ADS captures show no visible weapon/body penetration through audited environment geometry. |
| B61-E-017 | Match ground surface types to footsteps on the routes players actually traverse. | Claude | Each audited ground material resolves to the intended footstep set, with the existing animation timing checks still passing. |
| B61-E-018 | Verify collision and navigation behavior for newly placed or moved props. | Gemini | New geometry does not create unintended blocking, stuck routes, or navmesh gaps on the audited player and bot paths. |

### P1 — Materials and surfaces

| ID | Task | Proposed owner | Acceptance |
|---|---|---|---|
| B61-E-019 | Audit shader/material classes for concrete, asphalt, wood, fabric, plastic, coated metal, and bare metal. | Gemini | Each audited asset uses an appropriate HDRP material response; obvious cases such as metallic concrete or fully glossy fabric are corrected. |
| B61-E-020 | Calibrate representative base colors under stable reference lighting. | Copilot + Gemini | Nonmetal base colors describe the material rather than baked lighting; reference-light screenshots and changed material IDs are recorded. |
| B61-E-021 | Add restrained, surface-specific roughness variation to large flat materials. | Gemini | Raking-light and gameplay captures show breakup without noisy shimmer or glitter at ordinary viewing distance. |
| B61-E-022 | Set normal-map strength to a believable physical scale. | Gemini | Close and gameplay views show no oversized “pebbled” wall appearance or lost surface relief; material/property values are recorded. |
| B61-E-023 | Audit UV scale, stretching, and texel-density consistency on walls, floors, containers, and props. | Copilot + Gemini | Each sampled asset has a documented correction or pass; texture detail does not visibly stretch across audited surfaces. |
| B61-E-024 | Add lower-frequency material breakup to large surfaces. | Gemini | Macro variation reduces obvious tiling in route captures without creating repeating patterns or visible blend seams. |
| B61-E-025 | Separate clean, painted/coated, worn, and oxidized metal material states. | Gemini | Representative metal families show distinct, coherent base-color and roughness behavior under identical lighting. |
| B61-E-026 | Make terrain surface families visually and materially distinguishable. | Gemini | Mud, dirt, asphalt, concrete, gravel, and salt differ in color, normal, and roughness response, not tint alone. |
| B61-E-027 | Place grime where runoff, contact, and shelter would produce it. | Gemini | Before/after captures show location-specific buildup; no uniform full-surface noise pass is used as a substitute. |
| B61-E-028 | Audit representative materials under warm, cool, and neutral lighting. | Copilot | A reference capture set identifies and fixes materials that become implausibly glossy, metallic, or dark under one of the tested conditions. |

### P1 — Lighting and reflections

| ID | Task | Proposed owner | Acceptance |
|---|---|---|---|
| B61-E-029 | Calibrate representative practical-light intensity using HDRP physical units. | Gemini | Room, work-light, and exterior-fixture examples have recorded unit/intensity values and reference captures at verified scale. |
| B61-E-030 | Record and stabilize per-map exposure and grading references. | Gemini | Repeat captures hold stable brightness and playable interiors are not crushed; profile values are recorded. |
| B61-E-031 | Make sky, key-light direction, color, and shadow direction coherent per map. | Gemini | The audited outdoor viewpoints show no conflicting sun/sky/shadow cues. |
| B61-E-032 | Check exposure adaptation through interior/exterior transitions. | Gemini | Traversing each audited doorway causes no visible exposure pumping that hides enemies or obscures navigation. |
| B61-E-033 | Give visible fixtures plausible illumination and falloff. | Gemini | Fixture-lit geometry shows useful local illumination; emissive appearance alone is not counted as lighting the environment. |
| B61-E-034 | Tune SSAO without halos or excess darkening. | Gemini | Close-contact screenshots show grounded contact without black outlines; affected scenes pass B1. |
| B61-E-035 | Tune contact shadows selectively and measure the cost. | Copilot + Gemini | Selected hero geometry gains contact detail without visible shadow seams; the capture records which lights/cameras use the effect and its frame-time impact. |
| B61-E-036 | Evaluate baked indirect lighting or Adaptive Probe Volumes in representative interiors. | Gemini | Moving characters receive stable local indirect light, and the audit shows no new dark blotches or light leaks after bake/build. |
| B61-E-037 | Place reflection probes at important reflective surfaces. | Gemini | Metal, glass, and wet-floor route captures no longer show obvious black or stale reflections; probe coverage and update mode are recorded. |

### P1 — Atmosphere and weather

| ID | Task | Proposed owner | Acceptance |
|---|---|---|---|
| B61-E-038 | Review existing B40 `AtmosphereFX` placement on all four playable maps. | Gemini | Existing instances are retained, moved, or tuned based on map captures; no duplicate atmosphere components are added. |
| B61-E-039 | Measure target visibility through fog at fixed distances. | Codex | A repeatable report logs visibility and enemy contrast at the existing test distances; all affected maps pass B1. |
| B61-E-040 | Remove visible fog-volume edges and abrupt local-fog transitions. | Gemini | Walking and sprinting captures across every audited volume boundary show no hard edge or sudden density jump. |
| B61-E-041 | Tune dust, ash, and ember size, rate, lifetime, and wind. | Gemini | Gameplay captures show environment-scaled particles with no oversized specks, persistent screen noise, or visibly conflicting motion. |
| B61-E-042 | Unify wind cues across foliage, particles, smoke, and flags. | Gemini | In the same map, all reviewed cues move in a consistent documented direction/speed. |
| B61-E-043 | Make weather wetness response surface-specific. | Gemini | Dry-to-wet captures show plausible darkening/reflection changes per surface, without making every material uniformly glossy. |
| B61-E-044 | Place puddles in plausible drainage lows. | Gemini | Puddles appear at supported low points, with broken-up boundaries and readable reflections; no uniform puddle overlay is used. |

### P2 — Terrain and vegetation

| ID | Task | Proposed owner | Acceptance |
|---|---|---|---|
| B61-E-045 | Audit terrain-layer transitions using the HDRP Terrain Lit material. | Gemini | Terrain captures show no abrupt blend seam or inappropriate layer transition on the audited map routes. |
| B61-E-046 | Place terrain materials according to slope, drainage, and map use. | Gemini | Rock, soil, salt, mud, and concrete transitions follow the visible terrain geometry; before/after route captures are attached. |
| B61-E-047 | Test sparse, HDRP-compatible vegetation details. | Gemini | Instanced mesh details render with the intended HDRP material, do not turn pink, and preserve combat silhouettes and sightlines. |
| B61-E-048 | Cluster vegetation with map-specific density and height variation. | Gemini | Captures show natural patches rather than grids; density does not conceal target silhouettes on audited lanes. |
| B61-E-049 | Tune vegetation LOD transitions. | Copilot + Gemini | Fixed-distance captures show no distracting pop, billboard rotation, or material/color discontinuity on audited routes. |
| B61-E-050 | Make terrain edges meet roads, walls, and structures cleanly. | Gemini | Route captures show no terrain cut-through, unsupported ledge, or visible gap in the audited boundaries. |

### P2 — Decals, glass, and water

| ID | Task | Proposed owner | Acceptance |
|---|---|---|---|
| B61-E-051 | Author and validate P2 bullet-impact variants per material type. | Gemini | TestRange captures show distinct expected responses for metal, concrete, wood, dirt, glass, plastic, fabric, and water. |
| B61-E-052 | Verify surface routing and bounded decal growth. | Codex | Every surface maps to the intended policy; inappropriate water/fabric bullet holes are rejected; impact decals remain within the existing ring-buffer cap. |
| B61-E-053 | Place environmental grime and damage decals with plausible orientation and scale. | Gemini | Leaks/streaks follow gravity or flow; chips, tire marks, and traffic appear at plausible locations and scale in route captures. |
| B61-E-054 | Visually validate B30 glass and representative water surfaces. | Gemini | Clean, dirty, and cracked glass read correctly under map lighting; refraction, transparency, and damage remain readable in combat; water reflections are visible but not distracting. |

### P0 — Final acceptance

| ID | Task | Proposed owner | Acceptance |
|---|---|---|---|
| B61-E-055 | Run the post-pass B1 gate. | Codex + Gemini | All four maps are recaptured at the baseline viewpoints; visibility is ≥0.05 and enemy contrast is ≥0.025 at the same test points. |
| B61-E-056 | Run the post-pass P3 gate. | Copilot + Gemini | All four maps have complete captures; report changes against the provisional 33.3 ms average and 50 ms p99 targets by map/effect, with any target miss explicitly unresolved. |

## Research sources

Primary or vendor documentation used for implementation facts:

- Unity HDRP 17.5, [physical light units](https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@17.5/manual/Physical-Light-Units.html): one-unit-per-meter convention and reference natural/artificial light levels.
- Unity Graphics repository, [global fog](https://github.com/Unity-Technologies/Graphics/blob/master/Packages/com.unity.render-pipelines.high-definition/Documentation~/create-a-global-fog-effect.md): global height fog, volumetric grid, and performance/quality guidance.
- Unity Graphics repository, [local volumetric fog](https://github.com/Unity-Technologies/Graphics/blob/master/Packages/com.unity.render-pipelines.high-definition/Documentation~/create-a-local-fog-effect.md) and [local fog reference](https://github.com/Unity-Technologies/Graphics/blob/master/Packages/com.unity.render-pipelines.high-definition/Documentation~/local-volumetric-fog-volume-reference.md): volume boundaries, blending, masking, and density controls.
- Unity Graphics repository, [contact shadows](https://github.com/Unity-Technologies/Graphics/blob/master/Packages/com.unity.render-pipelines.high-definition/Documentation~/Override-Contact-Shadows.md): screen-space behavior, enablement, 24-light limit, and performance caveats.
- Unity Graphics repository, [HDRP feature overview](https://github.com/Unity-Technologies/Graphics/blob/master/Packages/com.unity.render-pipelines.high-definition/Documentation~/HDRP-Features.md): Lit/Layered Lit, terrain, decals, SSAO, Adaptive Probe Volumes, and material/debug tools.
- Unity Graphics repository, [decal projector reference](https://github.com/Unity-Technologies/Graphics/blob/master/Packages/com.unity.render-pipelines.high-definition/Documentation~/decal-projector-reference.md): projection bounds, layers, draw distance, angle fade, and transparency limitations.
- Unity Graphics repository, [reflection probes](https://github.com/Unity-Technologies/Graphics/blob/master/Packages/com.unity.render-pipelines.high-definition/Documentation~/Reflection-Probe.md) and [reflection/refraction color hierarchy](https://github.com/Unity-Technologies/Graphics/blob/master/Packages/com.unity.render-pipelines.high-definition/Documentation~/how-hdrp-calculates-color-for-reflection-and-refraction.md): probe update modes and reflection fallbacks.
- Unity Graphics repository, [Adaptive Probe Volume workflow](https://github.com/Unity-Technologies/Graphics/blob/master/Packages/com.unity.render-pipelines.high-definition/Documentation~/probevolumes-use.md): baking workflow and scene setup.
- Unity HDRP 17.5, [Terrain Lit material](https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@17.5/manual/terrain-lit-material.html); Unity 6.0, [grass and terrain details](https://docs.unity3d.com/6000.0/Documentation/Manual/terrain-Grass.html): terrain layering and detail modes, including instanced details.
- Disney Animation, [Physically-Based Shading at Disney](https://www.disneyanimation.com/publications/physically-based-shading-at-disney/): measured-material BRDF design and production shading observations.
- Adobe Substance 3D, [OpenPBR overview](https://experienceleague.adobe.com/en/docs/substance-3d/general-knowledge/openpbr/openpbr-overview): conductor/dielectric distinction, metalness, roughness, and layered surface guidance.
- Unity Graphics repository, [Rendering Debugger reference](https://github.com/Unity-Technologies/Graphics/blob/master/Packages/com.unity.render-pipelines.high-definition/Documentation~/rendering-debugger-window-reference.md): volume and APV inspection tools for diagnosing lighting.

These sources support general implementation choices. Per-map light, weather, clutter, and material values still require LR2-specific visible review and B1/P3 measurements.
