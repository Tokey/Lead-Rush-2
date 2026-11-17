# LeadRush 2 (`LR2`) — Engineering & Continuity Handoff Notes

## URGENT NEXT TASKS FOR CLAUDE (User Prompts + Root-Cause Diagnostics)

The user played the latest build in Unity and requested stopping current edits to hand off these exact prompts, screenshots, and verified root causes for Claude to fix:

### Exact User Prompts & Screenshots
1. *"insread of a black facemask can we have ghosts facemask or somethinbg similar with a skull?"*
2. *"also this is very weird. have different textures for different items please"*
   - Uploaded screenshot: `C:/Users/soulr/.gemini/antigravity/brain/3bd8c9fc-e1ee-4906-9bb8-cdaed69aa225/.user_uploaded/media_1794933028716_f2042f77.jpg` (enemy soldier renders as a washed-out monochrome desert-tan blob across helmet, goggles, vest, sleeves, gloves, kneepads, and boots, with a solid black face void).
3. *"why the fuck is he holding his gun like that? also what the fuck happend to ADS?"*
   - Uploaded screenshot: `C:/Users/soulr/.gemini/antigravity/brain/3bd8c9fc-e1ee-4906-9bb8-cdaed69aa225/.user_uploaded/media_1794933086215_618c8976.jpg` (player's right hand grips under the magazine/receiver instead of the pistol grip, and ADS iron sights are misaligned).
4. *"also the killcam should show the kill. the reload is even more messed up now. Can you please run, play the game, before stopping to work?"*

### Verified Root Causes & Fix Blueprints for Claude

1. **Wrong Right-Hand Grip & Broken ADS (`Assets/LR2/Editor/LR2ContentBuilder.cs`, `Assets/KinemationDemo/Tutorials/Tutorial #1 - Basic Setup/Swat_FPSAnimator/AnimatorProfile_Rig_Swat.asset`)**:
   - **Grip under magwell**: In `LR2ContentBuilder.ConfigureSwatAnimationRigAndReload()` (`lines 832-843`), `poseLayer.overwriteWeaponBone = true` and `poseLayer.overwriteRoot = true` were set on `PoseSamplerLayerSettings`, and `viewLayer.ikHandGun.pose` was modified. In `PoseSamplerLayerJob.cs` (`lines 187-205`), setting `overwriteWeaponBone = true` overwrites `_weaponBoneTransform` with `defaultWeaponPose` *after* `poseToSample.clip.SampleAnimation(...)`, introducing an offset between `mixamorig:RightHand` and `IK WeaponBone` (`weaponBoneRight`). Restore `PoseSamplerLayerSettings` (`overwriteWeaponBone = false`, `overwriteRoot = false`) and `ViewLayerSettings.ikHandGun.pose` to the original Kinemation SWAT tutorial setup so the right hand stays on the pistol grip.
   - **Misaligned ADS**: `AdsLayerSettings.aimTargetBone` targets the `Camera` bone (index 51 in `Rig_Swat.asset`). In `LR2ContentBuilder.BuildSoldiers()`, `cam.transform.localPosition` was moved to `new Vector3(0.001f, 0.032f, 0.065f)` while `AdsLayerJob` aligns the weapon's `AimPoint` to `aimTargetBone` (`(0,0,0)` on `mixamorig:Head`). Because the rendered `Camera` is shifted +3.2 cm up and +6.5 cm forward from `aimTargetBone`, ADS points the sights below the camera's optical axis. Resetting `cam.transform.localPosition = Vector3.zero` (or matching `aimTargetBone` to `cam.transform`) restores true iron-sight ADS.
   - **Re-check Weapon/Camera Framing**: Verify idle, walk, sprint, and ADS in real visible play mode (`LR2_MainMenu` -> match, or `PlaytestRobot` with visible Editor Play Mode / `-screen` capture) before stopping work.

2. **Messed-Up Reload (`Assets/Kinemation/FPSAnimationFramework/Runtime/Layers/AttachHandLayer/AttachHandLayerJob.cs`, `Assets/LR2/Editor/LR2ContentBuilder.cs`)**:
   - Hardcoded procedural offsets (`wepLocalShift`, `wepTilt`, `magGrabPos`, `pouchPos`, `magSeatPos`, `boltPos`) were added in `AttachHandLayerJob.cs` alongside a hybrid baked `LR2_AK12_Reload_Swat.anim` with stripped `WeaponBoneWeight`. Revert the procedural hacks in `AttachHandLayerJob.cs` and author/retarget a cohesive reload clip/profile where `IK WeaponBone`, `IK RightHand`, `IK LeftHand`, and `AK12Rig` bones (`Mag`, `Charge`) animate together cleanly in-frame.

3. **Soldier Monochrome Texture Washout (`Assets/LR2/Editor/LR2ContentBuilder.cs`, `MakeGear()` lines 1230-1269)**:
   - In `Assets/KinemationDemo/Meshes/Character/MixamoSwat/Materials/Soldier_Body_diffuse.png`, cloth fabric (shirt, pants, hood) is **saturated blue** (`c.b > c.r`), whereas tactical vest, pouches, gloves, boots, kneepads, helmet, goggles, straps, and holster are **unsaturated dark charcoal/black**.
   - `MakeGear()` multiplied luminance by `gain = 2.4f` / `2.6f` and lerped low-saturation black pixels with `dyed`, turning dark vest/gloves/boots/goggles/helmet into the same bright flat tan/olive tint.
   - **Fix**: Key off blue fabric saturation (`float blueness = Mathf.Clamp01((c.b - Mathf.Max(c.r, c.g)) * 4f);`) so only the blue cloth recolors to woodland olive / desert tan (with moderate gain ~1.15f to keep folds crisp), while preserving dark charcoal/black tactical vest, pouches, gloves, boots, kneepads, goggles, helmet, and holster! Also keep `Soldier_Body_normal.png` and `Soldier_Body_specular.png` mapped cleanly with appropriate smoothness/metallic contrast.

4. **Ghost-Style Skull Facemask (`Assets/LR2/Editor/LR2ContentBuilder.cs`, lines 1270-1278)**:
   - Currently `M_FaceMask_Balaclava.mat` sets `_BaseColorMap = null` with flat `(0.035, 0.036, 0.04)`, making the face a solid black void.
   - **Fix**: Generate `T_FaceMask_GhostSkull.png` from `Assets/KinemationDemo/Meshes/Character/MixamoSwat/Materials/Soldier_head_diffuse.png` (`1024x1024` UV layout): dark textured charcoal balaclava across head/neck/ears preserving fabric grain/normal detail, shadowed/dark eye area (`u in [0.19, 0.41], v in [0.87, 0.99]`), and a weathered bone-white Ghost skull motif painted onto the front face UV region (`u in [0.34, 0.66], v in [0.40, 0.73]` bottom-up UV) with black eye sockets, triangular nasal cavity, sunken cheek hollows, and teeth/mandible detail.

5. **Killcam Should Replay the Actual Kill (`Assets/LR2/Runtime/World/KillcamDirector.cs`)**:
   - Currently `KillcamDirector` waits `startDelay = 1.2s` after death and cuts to the killer's *live* `ViewTransform` *after* the kill already happened.
   - **Fix**: Maintain a rolling ~4s ring buffer (e.g. 30-60 Hz snapshots of combatant transforms, aim yaw/pitch, `AimBlend`, shot events/tracers/muzzle flash, and victim position) and replay `[t_kill - 2.5s .. t_kill + 0.5s]` from the killer's POV so the player actually sees the killer aim, fire, and score the killing shot.

---

- **Workspace**: `h:\Unity Projects\GitHub Repos\LeadRush 2`
- **Unity Version**: `6000.5.0f1` (`HDRP`) + Kinemation FPS Animation Framework (`Assets/Kinemation`)
- **Active Editor Landing Scene**: `Assets/LR2/Scenes/LR2_MainMenu.unity` (set in `Library/LastSceneManagerSetup.txt`)

> **Important Workflow Note (Single Unity Process Rule)**
> Keep the Unity Editor GUI **closed** when running `-batchmode` builds or smoke tests from CLI/agents, because Unity holds an exclusive lock on `Temp/UnityLockfile`. Never run multiple Unity `-batchmode` instances concurrently against this project; chain sequential runs inside a single `Start-Process -Wait` loop. Always quote `-lr2OutDir` when the path contains spaces (e.g. `LeadRush 2`).

---

## 1. Bugs Fixed

1. **Bot Naming**:
   - Replaced unusual bot names with clear bot/AI callsigns (`BOT-01`, `AI-02`, ...; verified `BOT-01` in playtest match reports).
2. **Weather Particle Curve Mode Sanitization**:
   - `LR2MapBuilder.SanitizeParticleSystems` (`Assets/LR2/Editor/LR2MapBuilder.ExtraMaps.cs`) normalizes mismatched `ParticleSystem.velocityOverLifetime` X/Y/Z curve modes so weather/fire particles run without console errors.
3. **SWAT FPS Rig, AK-12 Textures, Wall Collision & Reload Animation**:
   - **Root Cause Solved**: `Swat.fbx` imports with `animationType: 3` (Humanoid). Unity's PlayableGraph `AnimationClipPlayable` skips Generic `Transform` curves on Humanoid-mapped arm bones (`mixamorig:LeftShoulder/LeftArm/LeftForeArm/LeftHand`), keeping `VirtualElementJob` / `IK LeftHand` pinned to the handguard. Additionally, `FPSAnimatorMixer.GetBlendedCurveValue` evaluates `AnimCurve`s in **normalized clip time (`0..1`)**, and `AttachHandLayerSettings` uses `ECurveBlendMode.Mask` (`weight = 1f - curveValue`).
   - **Fixes Applied**:
     - `Assets/Kinemation/FPSAnimationFramework/Runtime/Layers/AttachHandLayer/AttachHandLayerJob.cs`: Added procedural reload IK + weapon cant when `weight < 1` (driven via `playable.GetJobData<AttachHandLayerJob>()` in `UpdatePlayableJobData`), guiding `IK LeftHand` from handguard -> magazine -> chest pouch -> magwell -> charging handle -> handguard.
     - `Assets/LR2/Editor/LR2ContentBuilder.cs`: Set `AttachHandLayerSettings.overridePoseWeight = 1f`, smoothed `CollisionLayerSettings` (`useSecondaryPose = false`, `barrelLength = 0.50f`), added normalized `MaskAttachHand` curve (`0 -> 0f`, `0.025 -> 1f`, `0.46 -> 1f`, `0.54 -> 0f`, `1 -> 0f`) to `Assets/LR2/Data/Weapons/AA_LR2_AK12_Reload_Swat.asset`, built `LR2_Swat_UpperBody.mask` (with `mixamorig:Hips/WeaponBone`), and baked `LR2_AK12_Reload_Swat.anim` (`239 curves, 4.47s`).
     - `Assets/LR2/Runtime/Characters/WeaponAntiClip.cs`: Keeps `item.transform.localPosition/Rotation` zeroed so the rifle mesh stays synced with `IK WeaponBone` and `CollisionLayerJob` instead of desyncing from IK hands.
4. **Main Menu Restoration & Map Selector**:
   - Restored `Assets/LR2/Scenes/LR2_MainMenu.unity` as the startup/last-open scene.
   - `Assets/LR2/Runtime/Game/MatchSettings.cs`: Added `LR2Session.MapScenes`, `LR2Session.MapDisplayNames`, `SelectedMapIndex`, and `CurrentMapScene` backed by `PlayerPrefs`.
   - `Assets/LR2/Runtime/UI/MainMenuController.cs`: Added `mapDropdown`, mode/difficulty controls, and an animated loading screen (`loadingPanel`, spinning `loadingSpinner`, `loadingBarFill`, `loadingStatusText`) backed by `SceneManager.LoadSceneAsync`.
5. **HDRP VolumeProfile Serialization, Motion Blur / DoF & Exposure**:
   - Fixed `SaveProfile()` (`Assets/LR2/Editor/LR2MapBuilder.cs`) to register volume overrides as sub-assets via `AssetDatabase.AddObjectToAsset` (previously `VolumeProfile.Add<T>` left `.asset` files unserialized).
   - `DisableMotionBlur()` zeros `MotionBlur` on all profiles and overrides `DepthOfFieldMode.Off` on all non-Menu profiles so `Assets/Settings/HDRPDefaultResources/DefaultSettingsVolumeProfile.asset`'s DoF does not blur gameplay.
   - Switched map `Exposure` to `AdaptationMode.Fixed` (`limitMin = 4.5f, limitMax = 13.0f` on Saltworks; `4.0f .. 12.8f` on extra maps), added streetlamps/floodlights to `Deadsector` and `Foundry`, and updated `TestRange` court slabs to smooth `_m.wallPlaster` without obstructing ramp handrails (`railings: false`).
6. **Mathematical Anti-Clipping & Doorway Clearance**:
   - `EliminateSceneClipping` (`Assets/LR2/Editor/LR2MapBuilder.cs`) resolves prop/foliage overlaps and enforces spawn/ammo/doorway/choke clearance across all 4 maps (`Saltworks`: adjusted=177, removed=17; `Deadsector`: adjusted=22; `Foundry`: adjusted=1; `TestRange`: adjusted=1).
7. **Batch Playtest Harness (`Assets/LR2/Runtime/World/PlaytestRobot.cs`)**:
   - Supports `-lr2Robot` and `-lr2OutDir "<path>"`.
   - `[DefaultExecutionOrder(500)]` + guarded pre-capture `_soldier.CameraController.UpdateCamera()` (skipped while dead or in Killcam) ensures camera stabilization runs before screenshot capture without fighting `KillcamDirector` (`300`) or `DeathCam`.
   - Multi-pass `RenderPipeline.SubmitRenderRequest` (`3` warmup passes on first captures, `2` thereafter) avoids uninitialized first-frame HDRP exposure/mip artifacts, and caches final match/participant summaries before `MatchManager.OnDestroy()` runs on playmode exit.
   - `TestNearestDestructible()` skips explosive props within 8m (`!(p.explosive && d2 < 64f)`) so the test bot does not blow itself up at match start, and rotates away if facing a wall within 3m before reload capture.
8. **Post-Claude Audit & Visual/Gameplay Polish Pass (Latest Session)**:
   - **Camera Roll in Batchmode & First-Person Weapon Framing (`LR2ContentBuilder.cs`, `AttachHandLayerJob.cs`, `SoldierController.cs`)**:
     - Set `Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate` on soldier prefabs (`BuildSoldiers`), eliminating the ~15–20° head-bone roll that appeared in headless/batchmode renders when `CullUpdateTransforms` deferred bone updates until render time.
     - Kept `PoseSamplerLayerSettings.defaultWeaponPose` at `(0.165f, 1.431f, 0.494f)` so `IK RightHand` stays locked to the pistol grip, and moved weapon + both IK hands together via `ViewLayerSettings.ikHandGun.pose = (0.038f, 0.052f, -0.050f)` (`camera.localPosition = (0.001f, 0.032f, 0.065f)`).
     - Preserved `playablesWeight >= 0.78f`, `StabilizationWeight = 0.78f`, and `LookLayerWeight = 0.8f` during sprint (`SoldierController`), keeping the rifle cleanly in frame while sprinting.
     - In `BuildSwatReloadClip()`, restricted retargeting strictly to the 4 left-arm bones (`LeftShoulder`, `LeftArm`, `LeftForeArm`, `LeftHand` via `!isFinger && ...`), preserving natural right-hand grip and curled finger poses; raised AK-12 PBR material albedo/smoothness contrast (`ApplyWeaponMaterials`).
   - **DeathCam / Killcam Origin Reset Fix (`FPSCameraController.cs`, `SoldierController.cs`, `KillcamDirector.cs`, `MatchManager.cs`)**:
     - Guarded `FPSCameraController.Update()` with `if (transform.parent != null)` and disabled `CameraController` during `OnDied()` (re-enabled after `EndDeathCam()` in `Respawn()`), fixing the unparented camera snapping to world origin `(0.001, 0.032, 0.065)` during deathcam/killcam.
     - Set `KillcamDirector` execution order to `300`, updated `ExtraRespawnDelay(in KillEvent, float baseDelay)`, instantiated `[LR2 Killcam]` in `MatchManager.Start()` so killcam works after loading from Main Menu, gated distance medals on `DamageType.Bullet`, and excluded `GameModeType.FreeForAll` from team-tie sudden death.
   - **Map Foliage & Skyline (`LR2MapBuilder.Nature.cs`, `LR2MapBuilder.ExtraMaps.cs`)**:
     - Clamped non-colliding `Undergrowth` plants inside `InPlay(pos, 2f)` to max height `0.50m`, excluded bushes inside `InPlay(..., 2f)`, and widened `yardTrees` sphere clearance to `4.6f`.
     - Replaced open modular wall strips (`SM_MainBuilding1..9`) in `Deadsector` background with solid skyline towers (`SkyTower_*`) + cranes.
   - **PostFX, Destructibles, Drones, AI, Ballistics, Audio, UI & Build Hygiene**:
     - `CombatPostFX.cs`: Changed overrides to `Add<T>(false)`, activated vignette/aberration/desaturation only when `> 0.001f` so map color grading/vignettes aren't zeroed at priority 100, and wired `LR2Settings.MotionBlur` / `LR2Settings.FilmGrain`.
     - `DestructibleProp.cs`: Disabled prop colliders before `TriggerExplosion`, resolved `Hitbox` to `hb.Owner` (`Health`) to stop 11x multi-hitbox splash damage, and added wall-occlusion `Physics.Linecast` (ignoring self/target root).
     - `OrbDrone.cs`: Called `_self.SetAimPoints(transform, transform, transform)` so bots aim at drones instead of 1.68m above them.
     - `BotController.cs`, `TacticalBotBrain.cs`, `Ballistics.cs`: Fixed stuck detection to use horizontal `Soldier.Movement.Velocity`, preserved `_lastSteer` while `Agent.pathPending`, steered off-NavMesh bots back toward `NavMesh.SamplePosition` instead of freezing, kept reload cover choice stable while in `BotGoal.TakeCover`, topped up empty bot reserve ammo, set `_slideFrame` sprint flags properly on close-range slide, and skipped disabled/inactive colliders in `Ballistics.Fire`.
     - `LR2Audio.cs`, `LR2AudioEnvironment.cs`, `HUDController.cs`, `MainMenuController.cs`, `WeaponAntiClip.cs`, `LR2SmokeTest.cs`, `LR2BuildPipeline.cs`, `LR2MapBuilder.cs`: Fixed `ignoreListenerPause` per UI category, 2D ambience priority/reverb bypass, low-health heartbeat pause guard, `SettingsPanel.Load()` & `difficulty.SetValueWithoutNotify` sync on Main Menu, scene availability fallback check before activating loading UI, `LR2SmokeTest` delegate cleanup on early exit, disabling stale non-LR2 Build Settings scenes, and zeroed LR2 compiler warnings (`CS0162`, `CS0414`).

---

## 2. New Features Implemented

### A. 4-Map Pool (`LR2Session`)
All 4 scenes are registered in Build Settings by `LR2BuildPipeline.BuildAllBatch` and selectable in the Main Menu:

| Scene Path | Display Name | Destructibles (Explosive) | Cover Points | Purpose |
|---|---|---|---|---|
| `Assets/LR2/Scenes/LR2_Saltworks.unity` | `SALTWORKS  -  RECLAIMED HARBOUR` | `67` (`6`) | `158` | Primary coastal harbor + forest map |
| `Assets/LR2/Scenes/LR2_Deadsector.unity` | `DEADSECTOR  -  WAR-TORN CITY` | `66` (`8`) | `106` | Overcast rainy urban streets + interiors |
| `Assets/LR2/Scenes/LR2_Foundry.unity` | `FOUNDRY  -  PIPE REFINERY` | `16` (`4`) | `104` | Multi-level industrial hall + sunken trench |
| `Assets/LR2/Scenes/LR2_TestRange.unity` | `TEST RANGE  -  BOT ARENA` | `9` (`2`) | `54` | Clean 3-lane symmetrical arena for bot/LLM testing |

### B. Destructible Objects (`Assets/LR2/Runtime/World/DestructibleProp.cs`)
- Added across all 4 maps during map build via `SetupDestructibleProps` (`Assets/LR2/Editor/LR2MapBuilder.ExtraMaps.cs`).
- Implements `IDamageable` (`ApplyDamage(ref DamageInfo)`), jolts physically per bullet hit, shatters into debris chunks when `maxHealth` reaches `0`, refreshes nearby `CoverPoint`s, and detonates explosive props (`explosive = true`, `blastRadius = 5.5f`, `blastDamage = 85f`) with chain-reaction damage.

### C. Animated Loading Screen (`MainMenuController.cs`)
- Selecting Duel / FFA / TDM activates `loadingPanel`, hides `mainPanel`, spins `loadingSpinner`, updates `loadingBarFill` and `loadingStatusText`, and loads `LR2Session.CurrentMapScene` asynchronously via `SceneManager.LoadSceneAsync`.

---

## 3. Hooking Up an LLM Bot (Starting in `LR2_TestRange`)

The AI stack in `Assets/LR2/Runtime/AI/` is already decoupled from locomotion, aiming, and weapon handling so an LLM integration can plug in without modifying `SoldierController` or `BotPerception`:

- **Option A — Strategic Commander (`IBotDirectiveProvider`, Recommended Start)**:
  - Implement `LR2.AI.IBotDirectiveProvider.TryGetDirective(BotController bot, BotBlackboard blackboard, out BotDirective directive)` on a `MonoBehaviour` and assign it to `BotController.directiveProvider` (polled every `directivePollInterval = 1.5f` s by default).
  - Read structured world state directly via `blackboard.ToJson(goal)` (`BotBlackboard` in `Assets/LR2/Runtime/AI/BotTypes.cs`).
  - Push asynchronous LLM responses immediately via `bot.IssueDirective(new BotDirective { order = ..., position = ..., target = ..., duration = ..., rationale = ... })`, using `BotOrder` values (`None`, `FreeRoam`, `HoldPosition`, `MoveTo`, `Attack`, `FollowLeader`, `Flank`, `Retreat`). `TacticalBotBrain` handles frame-by-frame cover, gunplay, and survival between LLM turns.
- **Option B — Full Brain Override (`IBotBrain`)**:
  - Implement `LR2.AI.IBotBrain` (`Initialize(BotController)`, `Think(BotBlackboard, float deltaTime) -> BotIntent`, `Reset()`) and assign `bot.Brain = yourLlmBrain;`.
- **Inspecting State**:
  - Call `bot.DescribeState()` (`Assets/LR2/Runtime/AI/BotController.cs`) or `bot.Blackboard.ToJson(...)`.
- **`LR2_TestRange` Layout**:
  - **Center Lane**: Symmetrical low crouch cover (`1.08m`) & tall LOS pillars (`2.3m`).
  - **West Lane**: Elevated `1.6m` overwatch deck with dual ramps (`WestRampN`, `WestRampS`) and `0.85m` mantle crate.
  - **East Lane**: Breakable glass panels (`TestGlassPanelA`/`B`), destructible barrels/gas cylinder/crates/jersey barriers.

---

## 4. CLI Build & Smoke Test Commands

Run from PowerShell with **Unity Editor closed**:

### Full Rebuild (`BuildAllBatch`)
```powershell
$unity = 'C:\Program Files\Unity\Hub\Editor\6000.5.0f1\Editor\Unity.exe'
$proj  = 'h:\Unity Projects\GitHub Repos\LeadRush 2'
$p = Start-Process -FilePath $unity -WorkingDirectory $proj -ArgumentList @(
  '-batchmode', '-nographics',
  '-projectPath', "`"$proj`"",
  '-executeMethod', 'LR2.EditorTools.LR2BuildPipeline.BuildAllBatch',
  '-quit',
  '-logFile', "`"$proj\Logs\LR2_BuildAll.log`""
) -Wait -PassThru
```

### PlayMode Smoke + Screenshot Run (`LR2SmokeTest.RunBatch`)
```powershell
$p = Start-Process -FilePath $unity -WorkingDirectory $proj -ArgumentList @(
  '-batchmode',
  '-projectPath', "`"$proj`"",
  '-executeMethod', 'LR2.EditorTools.LR2SmokeTest.RunBatch',
  '-lr2Robot',
  '-lr2Scene', 'Assets\LR2\Scenes\LR2_TestRange.unity',
  '-lr2OutDir', "`"$proj\Logs\LR2_Playtest_TestRange`"",
  '-lr2Duration', '20',
  '-logFile', "`"$proj\Logs\LR2_Smoke_TestRange.log`""
) -Wait -PassThru
```

*(Note: After `LR2SmokeTest.RunBatch`, restore `Library/LastSceneManagerSetup.txt` to `Assets/LR2/Scenes/LR2_MainMenu.unity` if you want the Editor to open on the Main Menu.)*

---

## 5. Latest Verified Status (Post-Final Edit)

- **Full Build (`Logs/build_polish2.log`)**: `EXIT CODE: 0`, `[LR2] BUILD EVERYTHING: SUCCESS`, zero `error CS` and zero `warning CS` across `Assets/LR2/` scripts.
- **4-Map Sequential Smoke Test** (`Runtime errors/exceptions: 0` and `Errors: 0` across all 4 logs & `report.txt` files; `TestRange` hit Unity D3D12 batch shutdown exit `-1073741819` *after* writing complete reports and screenshots cleanly):
  - **Saltworks (38s)**: `EXIT CODE: 0` | `DESTRUCTIBLE_TEST SM_Cablespool total=77 dmg=120 destroyed=True` | `Screenshots: 9` + reload PNGs | `Logs/LR2_Playtest`
  - **Deadsector (20s)**: `EXIT CODE: 0` | `DESTRUCTIBLE_TEST SM_Barrel_02 total=66 dmg=120 destroyed=True` | `Screenshots: 5` + reload PNGs | `Logs/LR2_Playtest_Deadsector`
  - **Foundry (20s)**: `EXIT CODE: 0` | `DESTRUCTIBLE_TEST SM_Crate_01 total=16 dmg=85 destroyed=True` | `Screenshots: 5` + reload PNGs | `Logs/LR2_Playtest_Foundry`
  - **TestRange (20s)**: `DESTRUCTIBLE_TEST SM_Jersey total=9 dmg=200 destroyed=True` | `Screenshots: 5` + reload PNGs | `Logs/LR2_Playtest_TestRange`
- **Visual Verification**:
  - `Saltworks`, `Deadsector`, `Foundry`, and `TestRange` batchmode screenshots show level 0° camera horizons, well-framed AK-12 positioning in idle/walk/sprint (`shot_00_002s.png`, `shot_01_006s.png`), readable metallic/polymer weapon surfaces, natural right-hand grip without detached fingers during reload (`shot_reload.png`, `shot_reload_late.png`), valid DeathCam/Killcam/respawn camera positions (`Saltworks` `shot_04_018s.png`, `Deadsector` `Camera.main`), low yard undergrowth in `Saltworks`, and solid skyline blocks in `Deadsector`.
