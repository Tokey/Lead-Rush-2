# LeadRush 2 Project History

## Origins: LeadRush HD

LeadRush 2 is the Unity 6 HDRP successor/rebuild of the older **LeadRush HD** mother
project. The mother repository is
[Tokey/LeadRushHD](https://github.com/Tokey/LeadRushHD), and the local legacy build is
`H:/Unity Projects/Builds/Lead Rush HDRP/LeadRushHDRP.exe`.

The legacy project established the original arena-shooter direction: first-person combat,
enemy and round management, HUD/minimap concepts, local player/enemy CSV data, and
standalone HDRP build workflows. LeadRush 2 deliberately preserves some legacy entry
points in `Assets/` for compatibility while moving active gameplay onto the LR2 runtime.

## Legacy scripts still in the repository root

- `Assets/GameManager.cs`, `Assets/RoundManager.cs`, and
  `Assets/InGameScripts/EnemyManager.cs` belong to the earlier project generation.
- `Assets/PlayerStats.cs`, `Assets/GameUI.cs`, `Assets/LightSwapper.cs`,
  `Assets/SkySwapper.cs`, `Assets/TextureSwapper.cs`, and
  `Assets/EnvironmentPEDisabler.cs` remain from legacy scenes/builds or utility passes.
- Active LR2 work lives under `Assets/LR2/Runtime`, `Assets/LR2/Editor`, and
  `Assets/LR2/Scenes`; derived/generated content should be produced through LR2 builders
  rather than editing generated scenes or prefabs by hand.

## Rebuild timeline

- **2026-11-16:** LeadRush 2 rebuild began, with Claude establishing generated maps,
  weapons, soldier rigs, bots and the initial AI workflow.
- **2026-11-17, morning:** HumanPose and shoulder-camera diagnosis, Kinemation
  retargeting, reload motion, recoil/TTK tuning and Hor+ FOV work.
- **2026-11-17, afternoon/evening:** procedural and atmospheric map work, weapon FX,
  menu/HUD work, automated QA, visible pose checks and cross-agent reviews.
- **2026-11-17 late evening:** Copilot joined for laser, audio, bot-AI documentation,
  glass, typography research, QA and world/atmosphere components.
- **2026-11-18:** protocol v3 added clocked work, process locks, measured cross-review
  ledgers and bug-first lane priorities.

## What LR2 added

LR2 is a Unity 6000.5.0f1 HDRP project organized around generated content and reusable
runtime contracts. It adds humanoid retargeting and full-body first-person/third-person
presentation, procedural reload and recoil, surface-aware impacts, killcams, bot utility
AI, map-specific lighting/weather, HDRP glass and environment realism components,
camera shake, persistent settings, automated QA and pose-check tooling.
