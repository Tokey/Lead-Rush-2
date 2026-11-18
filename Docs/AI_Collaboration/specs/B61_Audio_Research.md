# B61 — Audio Realism and Reliability Research

## Goal

Make LR2's audio more convincing and robust across weapons, movement, voices, ambience, music, and scene transitions. The repo already has a pooled 72-source `LR2Audio`, weapon distance bands, occlusion, speed-of-sound delays, a tail router, adaptive music stems, reverb zones, ambient one-shots, `SoldierFoley`, and `PainVoice`. This plan emphasizes validating what exists, filling demonstrated gaps, and keeping repeatable evidence.

Current source review found two concrete risks: `LR2Audio.PlayClipCore` reserves a tail slot before `GetVoice` can reject playback, potentially leaking the tail count; and `PainVoice` chooses clips with `SoundSet.Pick()` but has no no-repeat history. `IsIndoors` uses two upward rays and should be treated as a heuristic until tested against authored points. Audio source clips are described by B36 as local and not in Git, so inventories must report missing/local-only assets rather than silently assuming they are present.

## Overlap and ownership

- **B12:** A34–A37 are follow-up verification and the explicit no-repeat shuffle bag/cooldown acceptance. B12 already centralizes pain/death playback; do not create a second voice system.
- **B36:** A38–A48 extend the existing music/ambience brief with capture-based mixing and transition checks. Gemini owns implementation; this backlog does not replace B36's spec.
- **B58:** A43–A44 are independent assertions/review for the gunfire-music regression. Gemini owns the fix; Codex owns the check and cross-review.
- **P1:** A19–A27 test and harden its indoor/outdoor tail design. They do not authorize duplicating its tail router or its per-map implementation.
- **B5:** A30 remeasures the existing footsteps-versus-weapons target because the current evidence is inconsistent.
- Ownership follows the board: Gemini owns audio and music runtime/content; Claude owns Foley tied to animation/weapon events; Copilot owns asset/tooling audits; Codex owns QA telemetry and cross-review. Request changes in another agent's files through the board.

## Research notes

Unity voice virtualization is separate from LR2's 72-source pool. Unity's priority scale uses 0 as highest priority; source distance curves, spatial blend, low-pass, and reverb mix can all affect perceived level. Therefore a configured source priority or a passing pool counter alone does not prove that a sound reached the listener. Use the Audio Profiler and captured output to check actual audibility, virtualization, CPU, and memory.

Use loudness and true-peak measurement methods from EBU/ITU to report captures, but do not treat B36's proposed absolute LUFS values as universal gameplay targets. Establish LR2 reference captures first, then tune. Gunshot recordings vary with weapon, distance, azimuth, environment, and microphone; compare controlled captures and preserve distinct direct report, reflections, and late decay rather than merely turning up reverb.

## Atomic backlog

Acceptance figures below are LR2 project checks, not universal psychoacoustic standards. “Calibrated target” means the project records a reference capture/settings before tuning.

### A. Asset audit and import policy

| ID | Owner | Action | Acceptance evidence |
|---|---|---|---|
| A01 | Copilot | Catalog audio clips, import settings, and `SoundSet` references. | Report path, duration, channel count, sample rate, load/compression mode, and every missing reference. |
| A02 | Copilot | Record provenance and license for every external audio asset. | Each external file has source/license metadata; assets barred from public release are explicitly flagged. |
| A03 | Gemini | Audit role coverage for all five weapons. | Each gun has valid player shot, first shot, mechanical, tail, NPC distance-band, and handling cues, or a documented exception. |
| A04 | Copilot | Scan source clips for clipping, silence, DC offset, and likely duplicates. | Produce a flagged-clip report; every flagged item is reviewed or excluded. |
| A05 | Copilot | Define mono/stereo import rules by playback role. | Position-based one-shots use intended spatial channels; stereo ambience/music exceptions are documented. |
| A06 | Gemini | Compare compression/import settings by audio category. | Record size and listening comparison for representative weapons, Foley, voice, ambience, and music clips. |
| A07 | Gemini | Test first-use clip loading after a cold launch. | First weapon, voice, and ambience events play without missing audio or a repeatable frame hitch. |
| A08 | Copilot | Track local-only and absent source files in an asset manifest. | Manifest distinguishes tracked, ignored/local, and missing assets without copying or silently licensing them. |

### B. Weapon reports and mechanics

| ID | Owner | Action | Acceptance evidence |
|---|---|---|---|
| A09 | Gemini | Compare local shot layers for every weapon. | Same-position captures show each layer is present and the close muzzle peak stays within 1 dB of its calibrated target. |
| A10 | Gemini | Test first-shot-to-burst transitions. | Single shots, first shots after a pause, and sustained fire have no clicks, duplicate transient, or abrupt level jump in captures. |
| A11 | Gemini | Verify mechanical-layer timing. | Waveform/capture review confirms mechanical onset falls in the authored timing window for that weapon. |
| A12 | Claude | Verify reload audio cues against animation and state. | Empty/tactical reload captures align mag, bolt, and stop cues with the actual gun state. |
| A13 | Claude | Cancel delayed weapon cues on death, swap, and interrupted reload. | No cue belonging to the old weapon plays after interruption or equip change. |
| A14 | Gemini | Verify distant-report travel time. | Captured onset matches `distance / 343 m/s` within 10 ms at measured test distances. |
| A15 | Gemini | Tune and test NPC report distance bands. | Captures at each authored boundary have no transition jump above 3 dB; clips remain distinguishable. |
| A16 | Gemini | Check projectile flyby direction and timing. | Near/far pass captures localize in the projectile direction and do not duplicate the report. |
| A17 | Gemini | Audit impact clips by surface. | Every configured surface resolves a valid sound; adjacent repeated impacts vary when alternatives exist. |
| A18 | Gemini | Verify suppressed and unsuppressed variants. | Local and remote captures remain distinguishable at matched positions without changing unrelated weapon layers. |

### C. Propagation, occlusion, and tails

| ID | Owner | Action | Acceptance evidence |
|---|---|---|---|
| A19 | Gemini | Run P1 indoor/outdoor tail comparison. | Indoor tail is 3–6 dB stronger while close muzzle punch differs by no more than 1 dB. |
| A20 | Gemini | Audit reverb-zone coverage in rooms, doors, courtyards, and open spaces. | Captures at authored points show expected decay and no obvious abrupt switch on ordinary traversal. |
| A21 | Codex | Validate `IsIndoors` against curated scene points. | Covered, doorway, overhang/tree, and open points match their labels; failures include point and ray results. |
| A22 | Codex | Measure clear, partial, and full occlusion. | Captures log gain and cutoff; each result falls within LR2's documented target range for that occlusion class. |
| A23 | Gemini | Profile audio raycast cost under rapid fire. | Record raycasts/second and compare with the four-map baseline; no unbounded growth with additional emitters. |
| A24 | Gemini | Audit reverb routing by source category. | Weapon, voice, ambience, UI, and music captures confirm intended routing and dry-source behavior. |
| A25 | Gemini | Check distance attenuation, filtering, and reverb curves together. | Fixed-distance captures change smoothly and do not unexpectedly mute or change tone at a boundary. |
| A26 | Gemini | Fix tail reservation lifetime on unavailable/stolen voices. | Tail count returns to zero after rejected, stolen, and completed playback; stress case leaves no reserved slots. |
| A27 | Gemini | Stress rapid-fire tails. | At 600 RPM, overlap never exceeds router cap; tail field decays after firing stops instead of forming a continuous wall. |

### D. Footsteps, Foley, and voices

| ID | Owner | Action | Acceptance evidence |
|---|---|---|---|
| A28 | Gemini | Audit footsteps by surface material. | Every configured surface plays a valid matching set, with missing clips reported. |
| A29 | Claude | Measure step onset against animation foot plants. | Detected plant-to-audio onset error is under 50 ms for tested walk, crouch, and sprint strides. |
| A30 | Claude | Re-measure footsteps relative to gunfire. | Fixed-position capture reports level separation against B5's 14–18 dB target and reconciles prior conflicting runs. |
| A31 | Claude | Test Foley variation across repeated movement. | Where alternate clips exist, no identical clip repeats immediately in the scripted sequence. |
| A32 | Claude | Check stance and traversal event counts. | Crouch/stand/jump/land/mantle tests produce one intended cue each, including interrupted transitions. |
| A33 | Claude | Verify breathing state gates. | Sprint/recovery/low-health/death/respawn sequence follows intended cadence; dead characters emit no breathing. |
| A34 | Gemini | Add no-repeat pain-voice shuffle history. | With at least two clips available, selected clip differs from the previous selection. |
| A35 | Gemini | Verify pain voice gain and cooldown response. | Light/heavy damage at high/low health logs selected clip, gain, and cooldown; cooldown remains 0.6–1.2 s. |
| A36 | Gemini | Compare local and remote pain/death voices. | Local capture is centered per design; bot capture spatializes from the emitter and respects distance attenuation. |
| A37 | Codex | Check voice intelligibility during combat. | Concurrent pain, death, steps, and rapid fire preserve critical voices without muting weapon reports. |

### E. Ambience and adaptive music

| ID | Owner | Action | Acceptance evidence |
|---|---|---|---|
| A38 | Gemini | Audit ambience identity and coverage on all maps. | Quiet-route captures list beds by map and flag missing or duplicated atmosphere. |
| A39 | Gemini | Check ambience loop seams. | Waveform and listening review show no boundary click or short-window level jump above 3 dB. |
| A40 | Gemini | Verify ambient one-shot positions and indoor/outdoor routing. | Test captures localize to plausible positions and use intended spatial settings. |
| A41 | Gemini | Check fire-hazard sound rolloff. | 1 m/10 m/50 m capture levels decrease monotonically and remain spatialized. |
| A42 | Gemini | Check ambience masking against gameplay cues. | In idle and firefight captures, footsteps, voices, and weapon transients remain recognizable. |
| A43 | Codex | Verify B58 music-source priority and effect bypass. | Under voice-pool saturation, music is not virtualized; priority and `bypassEffects` state are logged. |
| A44 | Codex | Verify music gain behavior during gunfire. | `EffectiveTotalGain` during gunfire does not fall below the calm baseline; capture source levels and state. |
| A45 | Gemini | Check stem synchronization and state transitions. | Calm/tension/combat capture has no click, dropped beat, or detectable stem phase drift. |
| A46 | Gemini | Measure loudness and true peak from actual game captures. | Report per-state integrated loudness and true peak before changing targets; no output reaches 0 dBFS. |
| A47 | Gemini | Blind-compare score-on and score-off play captures. | Weapon reports, footsteps, and voices remain recognizable in both captures; document any masking. |
| A48 | Gemini | Verify volume settings persistence and isolation. | Restart restores each setting; muting music, SFX, or UI changes only that category. |

### F. Reliability and repeatable QA

| ID | Owner | Action | Acceptance evidence |
|---|---|---|---|
| A49 | Codex | Stress LR2's audio pool with simultaneous categories. | Report occupancy, rejected requests, LR2 steals, Unity virtualization, and active-tail count. |
| A50 | Copilot | Capture Audio Profiler baselines on each map. | Save voice, audio CPU, and memory readings for a repeatable route and firefight on all four maps. |
| A51 | Gemini | Test changing the OS output device during play. | Audio resumes on the new device with no exception or persistent silence. |
| A52 | Gemini | Add actionable missing-resource diagnostics. | Each required missing resource emits one useful message, not a per-frame log storm. |
| A53 | Gemini | Test pause, death, killcam, and scene transitions. | Correct sounds stop or persist as specified; no duplicate music or ambience source accumulates. |
| A54 | Copilot | Compare standalone and Editor audio behavior. | Same scripted cases report missing clips, voice count, and output differences in both targets. |
| A55 | Codex | Prove each new audio assertion detects a bad case. | Deliberately forced broken behavior yields `FAIL`; restored behavior yields `PASS`. |
| A56 | Codex | Store repeatable QA capture metadata. | Every audio capture records route, map, weapon, settings, expected result, and actual result. |
| A57 | Gemini | Verify category slider boundaries. | Setting one category to zero mutes that category only; other categories retain their previous level. |
| A58 | Copilot | Check audio memory stability in a 10-minute mixed scenario. | Start/end audio and streaming memory are recorded; unexplained unbounded growth fails review. |
| A59 | Gemini | Check clipping under worst-case layering. | Maximum expected combat capture has measured true peak below 0 dBFS and no audible digital clipping. |
| A60 | Codex | Cross-review B12, B36, B58, and P1 claims against source and captures. | Review lists verified evidence and remaining gaps; compilation alone is not treated as audio verification. |

## Sources

- [Unity 6 Audio Source reference](https://docs.unity3d.com/Manual/AudioSource-reference.html) — spatial blend, distance curves, source priority, reverb routing, and effects.
- [Unity Audio Mixer manual](https://docs.unity3d.com/Manual/AudioMixer.html) — routing, effects, snapshots, and ducking.
- [Unity Audio Manager settings](https://docs.unity3d.com/Manual/class-AudioManager.html) and [AudioSource priority API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource-priority.html) — real/virtual voices and priority semantics; 0 is highest priority.
- [Unity Audio Profiler manual](https://docs.unity3d.com/Manual/ProfilerAudio.html) — audibility, virtualization, voice counts, CPU, and memory.
- [Unity AudioImporter API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioImporter.html) and [DSP buffer API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSettings.GetDSPBufferSize.html) — import policy and latency/performance tradeoffs.
- [Unity Audio Reverb Zone manual](https://docs.unity3d.com/Manual/class-AudioReverbZone.html) — room reverb configuration.
- [Maher et al., “Variations in recorded acoustic gunshot waveforms generated by small firearms”](https://pubmed.ncbi.nlm.nih.gov/21476632/) — firearm, distance, azimuth, environment, and recording conditions affect recorded shot signatures.
- [Deželak et al., “Determination of the sound energy level of a gunshot and its applications in room acoustics”](https://www.sciencedirect.com/science/article/pii/S0003682X15003539) — impulse-source measurement and room-acoustic analysis.
- [Moorer, “About This Reverberation Business”](https://ccrma.stanford.edu/~jos/pasp/Moorer_1979_Reverberation.html) and [Schroeder reverberator notes](https://ccrma.stanford.edu/~jos/pasp/Schroeder_Reverberator_sections.html) — reverberation design background.
- [ITU-R BS.1770](https://www.itu.int/rec/R-REC-BS.1770/en) and [EBU Tech 3341](https://tech.ebu.ch/docs/tech/tech3341.pdf) — loudness and true-peak measurement methods, not LR2-specific gameplay targets.
- Existing LR2 specs: [`B36.md`](B36.md) and [`P1.md`](P1.md). B36 also links the [GDC HDR audio presentation](https://www.gdcvault.com/play/1012302/Audio-Bootcamp-Putting-the-HDR) and [Battlefield 3 scoring discussion](https://www.soundonsound.com/people/creating-battlefield-3-soundtrack).
