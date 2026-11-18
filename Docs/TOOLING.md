# LeadRush 2 Tooling

Run commands from the Unity project root unless a command explicitly names a project path.

## Builds and generators

| Tool | Purpose | Command / entry | Output |
|---|---|---|---|
| LR2 build pipeline | Builds all generated LR2 content and scenes | Unity menu `LR2 > Build Everything`; `LR2.EditorTools.LR2BuildPipeline.BuildAllBatch` | Generated prefabs, scenes, UI, maps and derived assets |
| LR2 content builder | Builds FX, audio, weapons and soldier content | `LR2ContentBuilder` methods through `LR2BuildPipeline` | Runtime assets under `Assets/LR2` |
| LR2 map builder | Creates the map scenes and map presentation | `LR2MapBuilder` / `LR2BuildPipeline` | Four map scenes plus preview assets |
| LR2 glass builder | Creates clean, dirty and cracked HDRP glass materials | Unity menu `LR2 > Build Glass Materials` | `M_LR2_Glass_*` materials and mask textures |
| README/media builders | Creates and refreshes in-game screenshots and previews | `LR2MapPreviewCapture` and QA capture helpers | `Docs/Media` and map preview PNGs |

## QA and smoke tests

| Tool | Purpose | Command / entry | Output / acceptance |
|---|---|---|---|
| LR2 QA bot | Runs measurable four-map checks for visibility, glare, SFX, hit feedback, motion and settings | `LR2.EditorTools.LR2QABot.RunBatch` | `Logs/LR2_QABot/qa_<scene>.txt` with PASS/FAIL lines |
| LR2 extra QA checks | Adds reload baseline, tactical ammo/charge, foot planting, hands, trauma and FOV invariants | Called by `LR2QABotRunner` | Extra `CheckExtra` PASS/FAIL lines in QA reports |
| LR2 smoke test | Runs a timed visible or batch play session and records runtime errors | `LR2.EditorTools.LR2SmokeTest.RunBatch` | `Logs/LR2_SmokeTest.txt` plus captured scene state |
| First-person pose check | Samples reload, idle, ADS, gait, hand and rig alignment | `LR2SmokeTest` `-lr2PoseCheck` flags | `Logs/LR2_PoseCheck` reports and screenshots |
| HDRP fidelity screenshot runner | Captures separated lighting, texture and VFX views | `LR2.EditorTools.LR2FidelityScreenshotRunner` when available | PNG captures and scene diagnostics |

## Bot explainer manual and media

- [`Docs/Bot/bot-brain.html`](Bot/bot-brain.html) is the three-tab interactive manual:
  Bot Brain/Aim Lab, Game Modes/CTF/Chat, and AI Build/Research Lab.
- `Tools\make_bot_gifs.py` regenerates the nine looping 1:1 explainer GIFs in
  `Docs\Media\bot\`, including `watch-a-bot-play.gif`, perception, Fitts flick,
  human miss pattern, recoil, adaptive skill, dual pathfinding, CTF/chat, and the
  multi-agent workflow.
- Select a weapon for a pose run with `-lr2Weapon <id>` (`AK12`, `Mk23`, `AK74N`,
  `Mk18`, or `FAL`). The harness captures hand close-ups plus idle, ADS, gait and
  reload views for the selected weapon.
- The game-mode builder includes Free-for-All CTF (`GameModeType.FreeForAllCaptureTheFlag`);
  `FlagZoneController` tracks each combatant's independent capture progress and
  `MatchManager` awards the capture to that individual.

## Compile and visible Unity checks

- Runtime assembly:
  `dotnet build LR2.Runtime.csproj -nologo -v q`
- Editor assembly:
  `dotnet build LR2.Editor.csproj -nologo -v q`
- Visible QA bot:
  launch Unity with `-executeMethod LR2.EditorTools.LR2QABot.RunBatch` in a
  1120x630 window.
- Visible pose check:
  launch Unity with `LR2SmokeTest.RunBatch -lr2PoseCheck
  -lr2Scene Assets\LR2\Scenes\LR2_TestRange.unity
  -lr2OutDir "<project>\Logs\LR2_PoseCheck" -lr2Duration 52`.

## Agent coordination tools

| Tool | Purpose | Command / file |
|---|---|---|
| Agent board | Single shared state for ownership, tracker status, requests and logs | `AGENT_COORDINATION.md` |
| Agent bridge | Lets an open Unity editor execute agent commands without acquiring a second process | `Temp/LR2_AgentCommand.txt`; results in `Temp/LR2_AgentResult.txt` |
| Agent bridge commands | Refresh, rebuild characters/maps, play a scene or stop play mode | `refresh`, `rebuild-chars`, `rebuild-all`, `play <scene> <seconds>`, `stop` |
| Copilot prompt | Continue Copilot in VS Code agent mode | `code chat -m agent -r -a AGENT_COORDINATION.md '<prompt>'` |
| Gemini prompt | Continue or start the Gemini/Antigravity work conversation | `agy --continue --mode auto -p '<prompt>'` or the Hydrogen model command from the board |

## Collaboration research ledgers

- `Docs/AI_Collaboration/data/tasks.csv`: delivered work by agent/model.
- `Docs/AI_Collaboration/data/bugs.csv`: escaped, reviewed and pre-push defects.
- `Docs/AI_Collaboration/analysis/analyze.py`: computes model/task quality summaries.
- `Docs/AI_Collaboration/RESEARCH_LOG.md`: narrative research and protocol evidence.
