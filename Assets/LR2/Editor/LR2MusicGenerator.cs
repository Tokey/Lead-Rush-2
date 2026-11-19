using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace LR2.EditorTools
{
    /// <summary>
    /// Offline procedural music composer for LeadRush 2.
    /// Generates 4 seamless 16-second stereo WAV stems (8 bars @ 120 BPM in D Minor / Phrygian) into
    /// <c>Assets/LR2/Resources/LR2/Music</c>:
    /// - <c>Music_Menu.wav</c>: moody analog tactical briefing dossier theme
    /// - <c>Music_Calm.wav</c>: stealth/recon drone + sparse plucks (mid-highs sculpted clear for footsteps)
    /// - <c>Music_Tension.wav</c>: driving 16th muted bass + frame-drum heartbeat + dark strings
    /// - <c>Music_Combat.wav</c>: heavy sub kick/tom percussion + saturated synth bass + urgent ostinato
    /// </summary>
    public static class LR2MusicGenerator
    {
        public const string OutputDir = "Assets/LR2/Resources/LR2/Music";
        private const int SampleRate = 44100;
        private const float Bpm = 120f;
        private const int Bars = 8;
        private const float DurationSec = Bars * (60f / Bpm) * 4f; // 16.0 s exact @ 120 BPM
        private static readonly int TotalSamples = SampleRate * 16;

        [MenuItem("LR2/Audio/Regenerate Procedural Music Stems", priority = 35)]
        public static void EnsureGeneratedMenu() => EnsureGenerated(true);

        public static void EnsureGenerated(bool force = false)
        {
            Directory.CreateDirectory(OutputDir);

            string menuPath = OutputDir + "/Music_Menu.wav";
            string calmPath = OutputDir + "/Music_Calm.wav";
            string tensionPath = OutputDir + "/Music_Tension.wav";
            string combatPath = OutputDir + "/Music_Combat.wav";

            bool anyWritten = false;
            if (force || !File.Exists(menuPath)) { WriteMenuStem(menuPath); anyWritten = true; }
            if (force || !File.Exists(calmPath)) { WriteCalmStem(calmPath); anyWritten = true; }
            if (force || !File.Exists(tensionPath)) { WriteTensionStem(tensionPath); anyWritten = true; }
            if (force || !File.Exists(combatPath)) { WriteCombatStem(combatPath); anyWritten = true; }

            if (anyWritten)
            {
                AssetDatabase.Refresh();
                foreach (var p in new[] { menuPath, calmPath, tensionPath, combatPath })
                    ConfigureMusicImporter(p);
                AssetDatabase.SaveAssets();
                Debug.Log("[LR2] Generated procedural adaptive music stems -> " + OutputDir);
            }
        }

        private static void ConfigureMusicImporter(string path)
        {
            var imp = AssetImporter.GetAtPath(path) as AudioImporter;
            if (imp == null) return;
            imp.forceToMono = false;
            imp.loadInBackground = false;
            var s = imp.defaultSampleSettings;
            s.loadType = AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.72f;
            imp.defaultSampleSettings = s;
            imp.SaveAndReimport();
        }

        // 8-bar D minor tactical progression: Dm -> Bb -> Gm -> A | Dm -> C -> Bb -> Asus4
        private static readonly float[] BassMidi = { 38f, 34f, 31f, 33f, 38f, 36f, 34f, 33f }; // D2, Bb1, G1, A1, D2, C2, Bb1, A1
        private static readonly float[][] PadChords =
        {
            new[] { 50f, 57f, 60f, 64f }, // Dm9: D3 A3 C4 E4
            new[] { 46f, 53f, 57f, 62f }, // Bbmaj7: Bb2 F3 A3 D4
            new[] { 43f, 50f, 55f, 58f }, // Gm7: G2 D3 G3 Bb3
            new[] { 45f, 52f, 57f, 61f }, // A: A2 E3 A3 C#4
            new[] { 50f, 57f, 60f, 65f }, // Dm7: D3 A3 C4 F4
            new[] { 48f, 55f, 60f, 64f }, // C: C3 G3 C4 E4
            new[] { 46f, 53f, 58f, 62f }, // Bb: Bb2 F3 Bb3 D4
            new[] { 45f, 52f, 57f, 62f }, // Asus4: A2 E3 A3 D4
        };
        private static readonly float[][] ArpNotes =
        {
            new[] { 62f, 65f, 69f, 76f, 69f, 65f, 64f, 69f }, // D4 F4 A4 E5 ...
            new[] { 58f, 62f, 65f, 69f, 65f, 62f, 65f, 74f }, // Bb3 D4 F4 A4 ...
            new[] { 55f, 58f, 62f, 69f, 62f, 58f, 62f, 67f }, // G3 Bb3 D4 A4 ...
            new[] { 57f, 64f, 67f, 73f, 69f, 64f, 61f, 64f }, // A3 E4 G4 C#5 ...
            new[] { 62f, 65f, 69f, 74f, 69f, 65f, 64f, 62f },
            new[] { 60f, 64f, 67f, 72f, 67f, 64f, 60f, 67f },
            new[] { 58f, 62f, 65f, 70f, 65f, 62f, 58f, 65f },
            new[] { 57f, 62f, 64f, 69f, 64f, 62f, 61f, 64f },
        };

        private static float MidiToHz(float midi) => 440f * Mathf.Pow(2f, (midi - 69f) / 12f);

        private static float Tri(float phase)
        {
            float f = phase - Mathf.Floor(phase);
            return 4f * Mathf.Abs(f - 0.5f) - 1f;
        }

        private static float Saw(float phase)
        {
            float f = phase - Mathf.Floor(phase);
            return 2f * f - 1f;
        }

        private static float SoftLowpass(ref float state, float x, float cutoffHz)
        {
            float a = Mathf.Clamp01(2f * Mathf.PI * cutoffHz / SampleRate);
            state += a * (x - state);
            return state;
        }

        // Seamless period-locked harmonic frequency ratio so oscillators loop without seam discontinuities at 16.0 s
        private static float LoopLockedPhaseHz(float targetHz)
        {
            float cycles = Mathf.Max(1f, Mathf.Round(targetHz * DurationSec));
            return cycles / DurationSec;
        }

        private static void WriteMenuStem(string path)
        {
            float[] L = new float[TotalSamples];
            float[] R = new float[TotalSamples];
            float lpL = 0f, lpR = 0f, padLpL = 0f, padLpR = 0f, radioLp = 0f, radioHp = 0f, radioPrev = 0f;
            float[] voicePhases = new float[8];
            var radioRng = new System.Random(1939);

            for (int i = 0; i < TotalSamples; i++)
            {
                float t = i / (float)SampleRate;
                int bar = Mathf.Clamp((int)(t / 2f), 0, 7);
                float barT = t - bar * 2f;

                // Warm analog pad chord (crossfaded softly near bar boundaries)
                float chordEnv = Mathf.Clamp01(barT / 0.14f) * Mathf.Clamp01((2f - barT) / 0.12f);
                var chord = PadChords[bar];
                float padSumL = 0f, padSumR = 0f;
                for (int v = 0; v < chord.Length; v++)
                {
                    float fBase = MidiToHz(chord[v]);
                    float det = v % 2 == 0 ? 1.0022f : 0.9978f;
                    voicePhases[v] += fBase / SampleRate;
                    voicePhases[v + 4] += (fBase * det) / SampleRate;
                    float oscA = Tri(voicePhases[v]) * 0.65f + Mathf.Sin(voicePhases[v] * 2f * Mathf.PI) * 0.35f;
                    float oscB = Tri(voicePhases[v + 4]) * 0.65f + Mathf.Sin(voicePhases[v + 4] * 2f * Mathf.PI) * 0.35f;
                    padSumL += oscA * (v % 2 == 0 ? 0.7f : 0.45f) + oscB * 0.35f;
                    padSumR += oscB * (v % 2 == 0 ? 0.45f : 0.7f) + oscA * 0.35f;
                }
                float sweep = 460f + 360f * (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * t / DurationSec));
                float pL = SoftLowpass(ref padLpL, padSumL * 0.14f * chordEnv, sweep);
                float pR = SoftLowpass(ref padLpR, padSumR * 0.14f * chordEnv, sweep);

                // Warm deep sub-bass + solemn war-drum heartbeat on bar downbeats (B36)
                float subF = LoopLockedPhaseHz(MidiToHz(BassMidi[bar]));
                float sub = Mathf.Sin(2f * Mathf.PI * subF * t) * 0.22f * Mathf.Clamp01(barT / 0.08f);
                float warDrum = 0f;
                if (barT < 0.36f)
                {
                    float df = Mathf.Lerp(96f, 41f, Mathf.Clamp01(barT * 9f));
                    warDrum = Mathf.Sin(2f * Mathf.PI * df * barT) * Mathf.Exp(-barT * 10.5f) * Mathf.Clamp01(barT / 0.004f) * 0.24f;
                }

                // Dusty analog field-radio / tape grit bed band-limited 300-1800 Hz (B36)
                float white = (float)(radioRng.NextDouble() * 2.0 - 1.0);
                float crackle = (radioRng.NextDouble() < 0.00055) ? white * 0.35f : 0f;
                float radioRaw = white * 0.022f + crackle;
                radioHp = 0.96f * (radioHp + radioRaw - radioPrev);
                radioPrev = radioRaw;
                float radioDust = SoftLowpass(ref radioLp, radioHp, 1650f);

                // Bowed war-cello / Phrygian minor 2nd tension motif (subtle analog grit)
                int step = Mathf.Clamp((int)(barT / 0.25f), 0, 7);
                float stepT = barT - step * 0.25f;
                float arpMidi = (bar % 2 == 1 && step == 6) ? (ArpNotes[bar][step] - 1f) : ArpNotes[bar][step];
                float noteHz = MidiToHz(arpMidi - 12f);
                float bowAtk = Mathf.Clamp01(stepT / 0.035f);
                float arpEnv = bowAtk * Mathf.Exp(-stepT * 4.8f);
                float bowed = (float)Math.Tanh((Saw(noteHz * stepT) * 0.55f + Tri(noteHz * 2f * stepT) * 0.28f) * 1.4f) * arpEnv * 0.18f;
                float pan = (step % 2 == 0) ? 0.65f : 0.35f;

                float dryL = pL + sub + warDrum + radioDust + bowed * pan;
                float dryR = pR + sub + warDrum + radioDust * 0.85f + bowed * (1f - pan);
                L[i] = SoftLowpass(ref lpL, dryL, 5200f);
                R[i] = SoftLowpass(ref lpR, dryR, 5200f);
            }

            AddWarTornOrchestralBed(L, R, brassWeight: 0.30f, gritDrive: 1.55f, withArtillery: true);
            AddCircularStereoEcho(L, R, Mathf.RoundToInt(0.375f * SampleRate), 0.34f);
            FinalizeStem(L, R, targetRms: 0.145f, maxPeak: 0.78f);
            WriteWav(path, L, R);
        }

        private static void WriteCalmStem(string path)
        {
            float[] L = new float[TotalSamples];
            float[] R = new float[TotalSamples];
            float lpL = 0f, lpR = 0f;

            for (int i = 0; i < TotalSamples; i++)
            {
                float t = i / (float)SampleRate;
                int bar = Mathf.Clamp((int)(t / 2f), 0, 7);
                float barT = t - bar * 2f;

                // Dark loop-locked tactical drone + warm sub octave (continuous phase across bars -> zero transition click)
                float fRoot = LoopLockedPhaseHz(MidiToHz(BassMidi[bar]));
                float barFade = Mathf.Clamp01(barT / 0.02f) * Mathf.Clamp01((2f - barT) / 0.02f);
                float sub = (Mathf.Sin(2f * Mathf.PI * fRoot * barT) * 0.24f + Tri(fRoot * barT) * 0.08f) * barFade;
                float fifth = Mathf.Sin(2f * Mathf.PI * (fRoot * 1.5f) * barT + 0.4f) * 0.08f * barFade * (0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * 2f * t / DurationSec));

                // Sparse sonar/kalimba pulse on beats 1 and 3.5 so footsteps remain completely exposed
                float pluck = 0f;
                if (barT < 0.85f || barT > 1.5f)
                {
                    float localT = barT < 0.85f ? barT : (barT - 1.5f);
                    float noteHz = MidiToHz(ArpNotes[bar][barT < 0.85f ? 0 : 4]);
                    pluck = Mathf.Sin(2f * Mathf.PI * noteHz * localT) * Mathf.Exp(-localT * 6.5f) * Mathf.Clamp01(localT / 0.004f) * 0.14f;
                }

                // Gentle stereo phase drift on fifth + low-pass keep mid-highs open for footsteps
                float xL = sub + fifth * 0.8f + pluck * 0.62f;
                float xR = sub + fifth * 1.0f + pluck * 0.38f;
                L[i] = SoftLowpass(ref lpL, xL, 3800f);
                R[i] = SoftLowpass(ref lpR, xR, 3800f);
            }

            AddWarTornOrchestralBed(L, R, brassWeight: 0.10f, gritDrive: 1.15f, withArtillery: true);
            AddCircularStereoEcho(L, R, Mathf.RoundToInt(0.375f * SampleRate), 0.28f);
            MixProgressiveGuitar(L, R, GuitarIntensity.CleanCalm);
            FinalizeStem(L, R, targetRms: 0.080f, maxPeak: 0.70f);
            WriteWav(path, L, R);
        }

        private static void WriteTensionStem(string path)
        {
            float[] L = new float[TotalSamples];
            float[] R = new float[TotalSamples];
            float bassLpL = 0f, bassLpR = 0f, strLpL = 0f, strLpR = 0f;

            for (int i = 0; i < TotalSamples; i++)
            {
                float t = i / (float)SampleRate;
                int bar = Mathf.Clamp((int)(t / 2f), 0, 7);
                float barT = t - bar * 2f;

                // 16th-note muted bass palm-mute pulse (0.125s steps)
                int s16 = Mathf.Clamp((int)(barT / 0.125f), 0, 15);
                float stepT = barT - s16 * 0.125f;
                float bassMidi = BassMidi[bar] + (s16 == 14 ? 2f : s16 == 15 ? 3f : 0f);
                float bassHz = MidiToHz(bassMidi);
                float pulseAcc = (s16 % 4 == 0) ? 1.0f : (s16 % 2 == 0) ? 0.72f : 0.48f;
                float pulseEnv = Mathf.Exp(-stepT * 18f) * Mathf.Clamp01(stepT / 0.002f) * pulseAcc;
                float bassRaw = (Saw(bassHz * stepT) * 0.55f + Tri(bassHz * 2f * stepT) * 0.35f) * pulseEnv;
                float cutoff = 580f + 500f * (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 2f * t / DurationSec));
                float bL = SoftLowpass(ref bassLpL, bassRaw * 0.38f, cutoff);
                float bR = SoftLowpass(ref bassLpR, bassRaw * 0.38f, cutoff * 1.04f);

                // Warm low taiko/frame-drum heartbeat on beats 1 and 2.5 (t=0, t=0.75) and beat 3.5 (t=1.375)
                float drum = 0f;
                foreach (float hitT in new[] { 0f, 0.75f, 1.375f })
                {
                    if (barT >= hitT && barT < hitT + 0.32f)
                    {
                        float dt = barT - hitT;
                        float df = Mathf.Lerp(105f, 44f, Mathf.Clamp01(dt * 12f));
                        drum += Mathf.Sin(2f * Mathf.PI * df * dt) * Mathf.Exp(-dt * 14f) * Mathf.Clamp01(dt / 0.003f) * 0.34f;
                    }
                }

                // Dark ensemble strings swell on bars 4-7 (smooth bar-edge crossfade eliminates step clicks)
                float strAmt = (bar >= 4 ? Mathf.Clamp01((bar - 3.5f) * 0.35f) : 0.25f) * Mathf.Clamp01(barT / 0.02f) * Mathf.Clamp01((2f - barT) / 0.02f);
                var ch = PadChords[bar];
                float strWaveL = (Saw(MidiToHz(ch[1]) * 1.003f * barT) + Saw(MidiToHz(ch[2]) * 0.997f * barT)) * 0.09f * strAmt;
                float strWaveR = (Saw(MidiToHz(ch[1]) * 0.997f * barT) + Saw(MidiToHz(ch[2]) * 1.003f * barT)) * 0.09f * strAmt;
                float sL = SoftLowpass(ref strLpL, strWaveL, 2600f);
                float sR = SoftLowpass(ref strLpR, strWaveR, 2600f);

                L[i] = bL + drum + sL;
                R[i] = bR + drum + sR;
            }

            AddWarTornOrchestralBed(L, R, brassWeight: 0.28f, gritDrive: 1.75f, withArtillery: true);
            AddCircularStereoEcho(L, R, Mathf.RoundToInt(0.25f * SampleRate), 0.18f);
            MixProgressiveGuitar(L, R, GuitarIntensity.OverdriveTension);
            FinalizeStem(L, R, targetRms: 0.145f, maxPeak: 0.80f);
            WriteWav(path, L, R);
        }

        private static void WriteCombatStem(string path)
        {
            float[] L = new float[TotalSamples];
            float[] R = new float[TotalSamples];
            float leadLpL = 0f, leadLpR = 0f, hatLp = 0f;
            var rng = new System.Random(2026);

            for (int i = 0; i < TotalSamples; i++)
            {
                float t = i / (float)SampleRate;
                int bar = Mathf.Clamp((int)(t / 2f), 0, 7);
                float barT = t - bar * 2f;

                // Driving 4-on-floor & syncopated combat kick (beat 0, 0.5, 1.0, 1.375, 1.5)
                float kick = 0f;
                foreach (float kt in new[] { 0f, 0.5f, 1.0f, 1.375f, 1.5f })
                {
                    if (barT >= kt && barT < kt + 0.22f)
                    {
                        float dt = barT - kt;
                        float kf = Mathf.Lerp(155f, 46f, Mathf.Clamp01(dt * 18f));
                        float amp = (kt == 1.375f) ? 0.65f : 1.0f;
                        kick += (float)Math.Tanh(Mathf.Sin(2f * Mathf.PI * kf * dt) * 1.6f) * Mathf.Exp(-dt * 16f) * amp * 0.42f;
                    }
                }

                // Tight dark combat industrial snare/rim on beats 2 & 4 (barT 0.5 and 1.5)
                float snare = 0f;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                foreach (float st in new[] { 0.5f, 1.5f })
                {
                    if (barT >= st && barT < st + 0.16f)
                    {
                        float dt = barT - st;
                        float body = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(220f, 110f, Mathf.Clamp01(dt * 24f)) * dt) * Mathf.Exp(-dt * 26f);
                        snare += (body * 0.45f + noise * Mathf.Exp(-dt * 32f) * 0.48f) * 0.36f;
                    }
                }

                // Soft dark hi-hat pulse rolled off safely below harsh footstep crunch bands
                int s16 = Mathf.Clamp((int)(barT / 0.125f), 0, 15);
                float hatDt = barT - s16 * 0.125f;
                float hatAmp = (s16 % 4 == 2) ? 0.16f : 0.08f;
                float hat = SoftLowpass(ref hatLp, noise * Mathf.Exp(-hatDt * 58f) * hatAmp, 5200f);

                // Saturated bass + urgent 8th-note staccato ostinato riff
                int s8 = Mathf.Clamp((int)(barT / 0.25f), 0, 7);
                float s8t = barT - s8 * 0.25f;
                float ostNote = MidiToHz(ArpNotes[bar][s8] - 12f);
                float ostEnv = Mathf.Exp(-s8t * 10f) * Mathf.Clamp01(s8t / 0.002f);
                float ostL = (Saw(ostNote * 1.004f * s8t) + Saw(ostNote * 0.5f * s8t)) * ostEnv * 0.24f;
                float ostR = (Saw(ostNote * 0.996f * s8t) + Saw(ostNote * 0.5f * s8t)) * ostEnv * 0.24f;
                float barEdge = Mathf.Clamp01(barT / 0.015f) * Mathf.Clamp01((2f - barT) / 0.015f);
                float bassDrive = (float)Math.Tanh(Saw(MidiToHz(BassMidi[bar]) * hatDt) * 1.7f) * 0.26f * Mathf.Exp(-hatDt * 6f) * barEdge;

                float fCut = 1800f + 1600f * (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 2f * t / DurationSec));

                // Driving firefight snare & field-tom drumrolls (escalating 32nd-note rolls on odd bar turnarounds + tension ghost fills)
                float rollL = 0f, rollR = 0f;
                if ((bar % 2 == 1 && barT >= 1.50f) || (bar % 2 == 0 && barT >= 1.75f))
                {
                    float rollStart = (bar % 2 == 1) ? 1.50f : 1.75f;
                    float rollLen = 2.0f - rollStart;
                    float localT = barT - rollStart;
                    float subStep = (bar == 3 || bar == 7) ? 0.05f : 0.0625f; // accelerating 20 Hz / 16 Hz sticks
                    int hitIdx = Mathf.FloorToInt(localT / subStep);
                    float dtR = localT - hitIdx * subStep;
                    if (dtR >= 0f && dtR < 0.075f)
                    {
                        float ramp = Mathf.Pow(Mathf.Clamp01(localT / rollLen), 1.35f);
                        float stickAmp = Mathf.Lerp(0.18f, 0.72f, ramp);
                        float drumFreq = (bar >= 6 && hitIdx % 3 == 0)
                            ? Mathf.Lerp(112f, 68f, Mathf.Clamp01(dtR * 18f)) // deep tom punch
                            : Mathf.Lerp(210f, 138f, Mathf.Clamp01(dtR * 24f)); // taut tactical snare
                        float hitSig = (Mathf.Sin(2f * Mathf.PI * drumFreq * dtR) * 0.58f + noise * Mathf.Exp(-dtR * 36f) * 0.42f)
                                       * Mathf.Exp(-dtR * 28f) * Mathf.Clamp01(dtR / 0.002f) * stickAmp;
                        float rollPan = (hitIdx % 2 == 0) ? 0.44f : 0.56f;
                        rollL = hitSig * (1f - rollPan * 0.25f);
                        rollR = hitSig * (0.75f + rollPan * 0.25f);
                    }
                }

                // B36: Heavy industrial metallic anvil / tank-chassis clangs on downbeat accents (band-limited < 2.6 kHz so footsteps stay crisp)
                float anvil = 0f;
                if (barT < 0.24f || (bar % 2 == 1 && barT >= 1.0f && barT < 1.22f))
                {
                    float adt = barT < 0.24f ? barT : (barT - 1.0f);
                    float aEnv = Mathf.Clamp01(adt / 0.002f) * Mathf.Exp(-adt * 15.5f);
                    float modes = Mathf.Sin(2f * Mathf.PI * 310f * adt) * 0.42f
                                + Mathf.Sin(2f * Mathf.PI * 478f * adt) * 0.30f
                                + Mathf.Sin(2f * Mathf.PI * 712f * adt) * 0.18f
                                + Mathf.Sin(2f * Mathf.PI * 1090f * adt) * 0.10f;
                    anvil = (float)Math.Tanh(modes * 1.55f) * aEnv * (barT < 0.24f ? 0.22f : 0.15f);
                }

                L[i] = kick + snare + hat + bassDrive + rollL + anvil * 0.92f + SoftLowpass(ref leadLpL, ostL, fCut);
                R[i] = kick + snare + hat + bassDrive + rollR + anvil * 1.04f + SoftLowpass(ref leadLpR, ostR, fCut);
            }

            AddWarTornOrchestralBed(L, R, brassWeight: 0.46f, gritDrive: 2.45f, withArtillery: true);
            MixProgressiveGuitar(L, R, GuitarIntensity.HeavyCombat);
            AddCircularStereoEcho(L, R, Mathf.RoundToInt(0.1875f * SampleRate), 0.16f);
            FinalizeStem(L, R, targetRms: 0.225f, maxPeak: 0.86f);
            WriteWav(path, L, R);
        }

        /// <summary>
        /// B36: Adds war-torn low bowed brass/cello D-Phrygian power fifths, subtle tape-grit saturation,
        /// and far-off muffled battlefield artillery thuds while carving upper-mids so footsteps cut cleanly through.
        /// </summary>
        private static void AddWarTornOrchestralBed(float[] L, float[] R, float brassWeight, float gritDrive, bool withArtillery)
        {
            int n = L.Length;
            float hornLpL = 0f, hornLpR = 0f, artLp = 0f;
            var rng = new System.Random(1944);
            float[] artStarts = { 0.40f, 5.15f, 9.85f, 13.70f };

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                int bar = Mathf.Clamp((int)(t / 2f), 0, 7);
                float barT = t - bar * 2f;
                float edge = Mathf.Clamp01(barT / 0.04f) * Mathf.Clamp01((2f - barT) / 0.04f);

                // Low bowed war brass (D3/A3 power fifths + Phrygian b2 tension in bars 3 & 7)
                float rootF = LoopLockedPhaseHz(MidiToHz(BassMidi[bar] + 12f));
                float fifthF = (bar == 3 || bar == 7) ? LoopLockedPhaseHz(MidiToHz(BassMidi[bar] + 13f)) : LoopLockedPhaseHz(MidiToHz(BassMidi[bar] + 19f));
                float swell = (0.35f + 0.65f * Mathf.Clamp01(bar / 7f)) * edge;
                float brassL = (float)Math.Tanh((Saw(rootF * 1.002f * t) + Saw(fifthF * 0.998f * t) * 0.72f) * gritDrive) * brassWeight * swell;
                float brassR = (float)Math.Tanh((Saw(rootF * 0.998f * t) + Saw(fifthF * 1.002f * t) * 0.72f) * gritDrive) * brassWeight * swell;
                float bCut = 880f + 620f * (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 0.5f * t / DurationSec));

                float art = 0f;
                if (withArtillery)
                {
                    float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                    for (int a = 0; a < artStarts.Length; a++)
                    {
                        float dt = t - artStarts[a];
                        if (dt >= 0f && dt < 1.15f)
                        {
                            float rumbleEnv = Mathf.Clamp01(dt / 0.025f) * Mathf.Exp(-dt * 3.4f);
                            float subBoom = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(74f, 34f, Mathf.Clamp01(dt * 1.8f)) * dt);
                            art += (subBoom * 0.62f + white * 0.55f) * rumbleEnv;
                        }
                    }
                    art = SoftLowpass(ref artLp, art * 0.16f, 220f);
                }

                L[i] += SoftLowpass(ref hornLpL, brassL, bCut) + art;
                R[i] += SoftLowpass(ref hornLpR, brassR, bCut) + art;
            }
        }

        private static void AddCircularStereoEcho(float[] L, float[] R, int delaySamples, float feedbackGain)
        {
            int n = L.Length;
            float[] eL = new float[n], eR = new float[n];
            for (int i = 0; i < n; i++)
            {
                int src = (i - delaySamples + n) % n;
                int src2 = (i - delaySamples * 2 + n) % n;
                // Ping-pong cross feedback keeping circular loop boundaries 100% seamless
                eL[i] = R[src] * feedbackGain + L[src2] * (feedbackGain * 0.42f);
                eR[i] = L[src] * feedbackGain + R[src2] * (feedbackGain * 0.42f);
            }
            for (int i = 0; i < n; i++)
            {
                L[i] += eL[i];
                R[i] += eR[i];
            }
        }

        /// <summary>Normalizes to a strict perceptual RMS hierarchy (Calm &lt; Tension &lt; Combat), soft-limits peaks, and cross-blends loop ends for zero seam click.</summary>
        private static void FinalizeStem(float[] L, float[] R, float targetRms, float maxPeak)
        {
            int n = L.Length;
            double sumSq = 0.0;
            for (int i = 0; i < n; i++)
                sumSq += 0.5 * (L[i] * (double)L[i] + R[i] * (double)R[i]);
            float curRms = Mathf.Sqrt((float)(sumSq / Mathf.Max(1, n)));
            float gain = curRms > 1e-5f ? targetRms / curRms : 1f;

            for (int i = 0; i < n; i++)
            {
                float xL = L[i] * gain;
                float xR = R[i] * gain;
                // Soft-clip only extreme transient spikes above 0.72 * maxPeak so RMS hierarchy stays intact without harsh digital clipping
                float softKnee = maxPeak * 0.72f;
                if (Mathf.Abs(xL) > softKnee) xL = Mathf.Sign(xL) * (softKnee + (maxPeak - softKnee) * (float)Math.Tanh((Mathf.Abs(xL) - softKnee) / Mathf.Max(0.05f, maxPeak - softKnee)));
                if (Mathf.Abs(xR) > softKnee) xR = Mathf.Sign(xR) * (softKnee + (maxPeak - softKnee) * (float)Math.Tanh((Mathf.Abs(xR) - softKnee) / Mathf.Max(0.05f, maxPeak - softKnee)));
                L[i] = xL;
                R[i] = xR;
            }

            // Smooth 256-sample wrap-around cosine taper to near-zero loop seam delta
            const int Seam = 256;
            for (int k = 0; k < Seam; k++)
            {
                float a = 0.5f - 0.5f * Mathf.Cos(Mathf.PI * k / Seam);
                L[k] *= a;
                R[k] *= a;
                L[n - 1 - k] *= a;
                R[n - 1 - k] *= a;
            }
        }

        private enum GuitarIntensity { CleanCalm, OverdriveTension, HeavyCombat }

        /// <summary>
        /// Synthesizes a slowly progressive double-tracked electric guitar riff (Karplus-Strong plucked
        /// strings through asymmetric tube overdrive + 2-pole guitar cabinet lowpass) evolving across
        /// all 8 bars while rolling off cleanly above 3.5 kHz so combat footsteps remain crisp.
        /// </summary>
        private static void MixProgressiveGuitar(float[] L, float[] R, GuitarIntensity mode)
        {
            int n = TotalSamples;
            float[] rL = new float[n];
            float[] rR = new float[n];
            float[] lead = new float[n];

            int stepSamples = Mathf.RoundToInt(SampleRate * (60f / Bpm) * 0.25f); // 16th note = 0.125 s
            int totalSteps = Bars * 16; // 128 16th-note steps

            // Heavy rhythmic chug accents across each 16-step bar: 1 . a & 1 e . a 1 . & . 1 e & .
            bool[] chugMask =
            {
                true, false, true, true,  false, true, true, false,
                true, false, true, false, true,  true, false, true
            };
            bool[] openAccentMask =
            {
                true, false, false, false, false, false, true, false,
                true, false, false, false, true,  false, false, false
            };

            // Ascending Bar 0..7 lead phrase (in MIDI; -1 = rest) building slowly from low motif to octave climax
            float[][] leadBars =
            {
                new float[] { 62f, -1, -1, -1,  -1, -1, 65f, -1,  64f, -1, -1, -1,  -1, -1, 62f, -1 }, // Bar 0: spare D4 -> F4 -> E4
                new float[] { 58f, -1, -1, -1,  62f, -1, -1, -1,  65f, -1, -1, -1,  64f, -1, 62f, -1 }, // Bar 1: Bb3 -> D4 -> F4 -> E4
                new float[] { 62f, -1, 65f, -1, 67f, -1, -1, -1,  65f, -1, 64f, -1, 62f, -1, -1, -1 }, // Bar 2: D4 F4 G4 F4 E4
                new float[] { 61f, -1, 64f, -1, 69f, -1, -1, -1,  67f, -1, 64f, -1, 61f, -1, 64f, -1 }, // Bar 3: A7 harmonic push C#4 E4 A4
                new float[] { 62f, -1, 65f, -1, 69f, -1, 70f, -1, 69f, -1, 67f, -1, 65f, -1, 64f, -1 }, // Bar 4: rising Dm + b6 Phrygian Bb4
                new float[] { 67f, -1, 69f, -1, 72f, -1, -1, -1,  71f, -1, 69f, -1, 67f, -1, 64f, -1 }, // Bar 5: C5 soaring climb
                new float[] { 70f, -1, 69f, -1, 67f, -1, 65f, -1, 69f, -1, 70f, -1, 72f, -1, 74f, -1 }, // Bar 6: intense climb to D5
                new float[] { 73f, -1, 74f, -1, 76f, -1, 74f, -1, 73f, -1, 69f, -1, 67f, -1, 64f, -1 }, // Bar 7: C#5->E5 harmonic minor climax resolving to D4
            };

            int doubleTrackOffset = Mathf.RoundToInt(0.0065f * SampleRate); // 6.5 ms tight twin guitar spread

            for (int step = 0; step < totalSteps; step++)
            {
                int bar = step / 16;
                int sInBar = step % 16;
                float barProg = (float)step / (totalSteps - 1);
                int startSample = step * stepSamples;
                float root = BassMidi[bar];

                if (mode == GuitarIntensity.CleanCalm)
                {
                    // Slowly opens from 2 atmospheric harmonics/bar (bars 0-3) to delicate picking (bars 4-7)
                    bool playClean = (bar < 4) ? (sInBar == 0 || sInBar == 10) : (sInBar == 0 || sInBar == 4 || sInBar == 8 || sInBar == 12);
                    if (!playClean) continue;
                    float cleanNote = ArpNotes[bar][sInBar % 8];
                    float vel = Mathf.Lerp(0.18f, 0.34f, barProg);
                    RenderKarplusNote(rL, startSample, cleanNote, 0.62f, vel, damping: 0.9965f, seed: 101 + step, vibratoCents: 6f);
                    RenderKarplusNote(rR, (startSample + 220) % n, cleanNote + 12f, 0.50f, vel * 0.45f, damping: 0.9955f, seed: 202 + step, vibratoCents: 9f);
                    continue;
                }

                if (mode == GuitarIntensity.OverdriveTension)
                {
                    if (!chugMask[sInBar] && bar < 5) continue;
                    bool muted = !(bar >= 4 && openAccentMask[sInBar]);
                    float noteMidi = (bar == 3 && sInBar >= 12) ? root + 1f : (bar == 7 && sInBar >= 12 ? root + 3f : root);
                    float damp = muted ? Mathf.Lerp(0.954f, 0.972f, barProg) : 0.994f;
                    float dur = muted ? 0.14f : 0.34f;
                    float vel = (muted ? 0.34f : 0.54f) * Mathf.Lerp(0.65f, 1.15f, barProg);
                    RenderKarplusNote(rL, startSample, noteMidi, dur, vel, damp, 301 + step, 0f);
                    RenderKarplusNote(rR, (startSample + doubleTrackOffset) % n, noteMidi, dur, vel * 0.96f, damp, 401 + step, 0f);
                    if (!muted)
                    {
                        RenderKarplusNote(rL, startSample, noteMidi + 7f, dur, vel * 0.78f, damp, 501 + step, 0f);
                        RenderKarplusNote(rR, (startSample + doubleTrackOffset) % n, noteMidi + 7f, dur, vel * 0.78f, damp, 601 + step, 0f);
                    }
                    continue;
                }

                // HeavyCombat: progressively denser power-chord riff + ascending harmonic-minor lead
                bool gallopExtra = bar >= 4 && (sInBar == 7 || sInBar == 14);
                if (chugMask[sInBar] || gallopExtra)
                {
                    bool open = openAccentMask[sInBar] || (bar >= 6 && sInBar % 4 == 0);
                    // Signature Phrygian b2 pull-off in bar 4 & chromatic climb in bar 7
                    float rNote = root;
                    if (bar == 3 && sInBar >= 14) rNote = root + 1f;
                    else if (bar == 4 && sInBar == 10) rNote = 39f; // Eb2 Phrygian snarl
                    else if (bar == 7 && sInBar >= 12) rNote = root + (sInBar - 12); // ascending chromatic turnaround into D2

                    float damp = open ? Mathf.Lerp(0.992f, 0.996f, barProg) : Mathf.Lerp(0.958f, 0.975f, barProg);
                    float dur = open ? 0.36f : 0.135f;
                    float vel = (open ? 0.68f : 0.44f) * Mathf.Lerp(0.75f, 1.18f, barProg);

                    // Left & Right double-tracked root + fifth (+ octave slam in second half)
                    RenderKarplusNote(rL, startSample, rNote, dur, vel, damp, 701 + step, open ? 4f : 0f);
                    RenderKarplusNote(rL, startSample, rNote + 7f, dur, vel * 0.85f, damp, 801 + step, 0f);
                    RenderKarplusNote(rR, (startSample + doubleTrackOffset) % n, rNote + 0.04f, dur, vel * 0.98f, damp, 901 + step, open ? -4f : 0f);
                    RenderKarplusNote(rR, (startSample + doubleTrackOffset) % n, rNote + 7.04f, dur, vel * 0.85f, damp, 1001 + step, 0f);
                    if (open && bar >= 3)
                    {
                        RenderKarplusNote(rL, startSample, rNote + 12f, dur * 0.85f, vel * 0.55f, damp, 1101 + step, 6f);
                        RenderKarplusNote(rR, (startSample + doubleTrackOffset) % n, rNote + 12.03f, dur * 0.85f, vel * 0.55f, damp, 1201 + step, -6f);
                    }
                }

                float leadMidi = leadBars[bar][sInBar];
                if (leadMidi > 0f)
                {
                    float leadGain = Mathf.Lerp(0.22f, 0.68f, barProg); // gradually intensifies Bar 0 -> Bar 7
                    float leadDur = (bar >= 6) ? 0.30f : 0.42f;
                    RenderKarplusNote(lead, startSample, leadMidi, leadDur, leadGain, damping: 0.9968f, seed: 1301 + step, vibratoCents: 14f);
                }
            }

            // Amp overdrive + cabinet EQ (keeps sub tight above 95 Hz and rolls highs gently below footstep transients)
            float hpL = 0f, hpPrevL = 0f, hpR = 0f, hpPrevR = 0f, hpLead = 0f, hpPrevLead = 0f;
            float lp1L = 0f, lp2L = 0f, lp1R = 0f, lp2R = 0f, lp1Lead = 0f, lp2Lead = 0f;
            float baseDrive = mode == GuitarIntensity.CleanCalm ? 0.85f : (mode == GuitarIntensity.OverdriveTension ? 1.55f : 2.55f);
            float endDrive = mode == GuitarIntensity.CleanCalm ? 1.15f : (mode == GuitarIntensity.OverdriveTension ? 3.10f : 4.65f);
            float baseCut = mode == GuitarIntensity.CleanCalm ? 1650f : (mode == GuitarIntensity.OverdriveTension ? 1200f : 2450f);
            float endCut = mode == GuitarIntensity.CleanCalm ? 2600f : (mode == GuitarIntensity.OverdriveTension ? 2250f : 3450f);
            float mixWeight = mode == GuitarIntensity.CleanCalm ? 0.16f : (mode == GuitarIntensity.OverdriveTension ? 0.24f : 0.34f);

            const float HpAlpha = 0.9865f; // ~95 Hz tight chug highpass
            for (int i = 0; i < n; i++)
            {
                float prog = (float)i / n;
                float drive = Mathf.Lerp(baseDrive, endDrive, prog);
                float cabCut = Mathf.Lerp(baseCut, endCut, prog);

                // Asymmetric tube waveshaper on twin rhythm guitars
                float xL = rL[i];
                float xR = rR[i];
                hpL = HpAlpha * (hpL + xL - hpPrevL); hpPrevL = xL;
                hpR = HpAlpha * (hpR + xR - hpPrevR); hpPrevR = xR;

                float satL = mode == GuitarIntensity.CleanCalm
                    ? (float)Math.Tanh(hpL * drive)
                    : (float)Math.Tanh(hpL * drive + 0.16f * hpL * hpL) / (1f + 0.22f * Mathf.Abs(hpL));
                float satR = mode == GuitarIntensity.CleanCalm
                    ? (float)Math.Tanh(hpR * drive)
                    : (float)Math.Tanh(hpR * drive + 0.16f * hpR * hpR) / (1f + 0.22f * Mathf.Abs(hpR));

                float cabL = SoftLowpass(ref lp2L, SoftLowpass(ref lp1L, satL, cabCut), cabCut * 0.95f);
                float cabR = SoftLowpass(ref lp2R, SoftLowpass(ref lp1R, satR, cabCut), cabCut * 0.95f);

                // Lead guitar layered with slight center-right focus + screaming harmonic tube sustain
                float xLd = lead[i];
                hpLead = HpAlpha * (hpLead + xLd - hpPrevLead); hpPrevLead = xLd;
                float satLd = (float)Math.Tanh(hpLead * (drive * 1.15f) + 0.18f * hpLead * hpLead);
                float cabLd = SoftLowpass(ref lp2Lead, SoftLowpass(ref lp1Lead, satLd, cabCut + 300f), cabCut + 200f);

                float wideL = (cabL * 0.82f + cabR * 0.18f + cabLd * 0.42f) * mixWeight;
                float wideR = (cabR * 0.82f + cabL * 0.18f + cabLd * 0.50f) * mixWeight;
                L[i] += wideL;
                R[i] += wideR;
            }
        }

        private static void RenderKarplusNote(float[] target, int startSample, float midi, float durationSec, float velocity, float damping, int seed, float vibratoCents)
        {
            int n = target.Length;
            float baseFreq = MidiToHz(midi);
            float periodSamples = SampleRate / Mathf.Max(40f, baseFreq);
            int delayLen = Mathf.Max(8, Mathf.CeilToInt(periodSamples) + 4);
            float[] ring = new float[delayLen];

            var rng = new System.Random(seed);
            // Filtered pick burst excitation
            float pickLp = 0f;
            for (int k = 0; k < delayLen; k++)
            {
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                pickLp += 0.42f * (white - pickLp);
                ring[k] = pickLp * velocity;
            }

            int totalNoteSamples = Mathf.Max(delayLen, Mathf.RoundToInt(durationSec * SampleRate));
            int ptr = 0;
            int releaseLen = Mathf.Max(256, totalNoteSamples / 5);

            for (int i = 0; i < totalNoteSamples; i++)
            {
                float curFreq = baseFreq;
                if (vibratoCents > 0.1f && i > SampleRate * 0.08f)
                {
                    float vib = Mathf.Sin(2f * Mathf.PI * 5.6f * (i / (float)SampleRate));
                    curFreq *= Mathf.Pow(2f, (vibratoCents * 0.01f * vib) / 12f);
                }

                float curDelay = Mathf.Clamp(SampleRate / curFreq, 4f, delayLen - 2f);
                float readPos = ptr - curDelay;
                while (readPos < 0f) readPos += delayLen;

                int i0 = Mathf.FloorToInt(readPos) % delayLen;
                int i1 = (i0 + 1) % delayLen;
                float frac = readPos - Mathf.Floor(readPos);
                float sample = Mathf.Lerp(ring[i0], ring[i1], frac);

                int prevIdx = (i0 - 1 + delayLen) % delayLen;
                float filtered = 0.5f * (sample + ring[prevIdx]) * damping;
                ring[ptr] = filtered;
                ptr = (ptr + 1) % delayLen;

                float env = 1f;
                int rem = totalNoteSamples - 1 - i;
                if (rem < releaseLen) env = (float)rem / releaseLen;

                int dst = (startSample + i) % n;
                target[dst] += sample * env;
            }
        }

        private static void WriteWav(string path, float[] L, float[] R)
        {
            int numSamples = L.Length;
            int dataBytes = numSamples * 2 * 2; // 2 channels, 16-bit
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            using var bw = new BinaryWriter(fs);
            bw.Write(new[] { 'R', 'I', 'F', 'F' });
            bw.Write(36 + dataBytes);
            bw.Write(new[] { 'W', 'A', 'V', 'E' });
            bw.Write(new[] { 'f', 'm', 't', ' ' });
            bw.Write(16);
            bw.Write((short)1); // PCM
            bw.Write((short)2); // Stereo
            bw.Write(SampleRate);
            bw.Write(SampleRate * 4);
            bw.Write((short)4);
            bw.Write((short)16);
            bw.Write(new[] { 'd', 'a', 't', 'a' });
            bw.Write(dataBytes);
            for (int i = 0; i < numSamples; i++)
            {
                short sL = (short)Mathf.Clamp(Mathf.RoundToInt(L[i] * 32767f), -32767, 32767);
                short sR = (short)Mathf.Clamp(Mathf.RoundToInt(R[i] * 32767f), -32767, 32767);
                bw.Write(sL);
                bw.Write(sR);
            }
        }
    }
}
