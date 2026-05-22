using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Reflection;
using YARG.Core.Chart;
using YARG.Core.Engine;
using YARG.Core.Engine.Vocals;
using YARG.Core.Engine.Vocals.Engines;
using YARG.Core.Input;

namespace YARG.Core.UnitTests.Engine;

[TestFixture]
public sealed class PartyVocalsScenarioTests
{
    // Match the Python test suite's pitch window and scoring thresholds
    private static readonly VocalsEngineParameters ScenarioParams = new(
        new HitWindowSettings(0.1, 0.1, 1.0, false, 0, 1, 1, 0),
        4,
        VocalsEngineTests.StarMultiplierThresholds,
        VocalsEngineTests.SoloBonusStarMultiplierThresholds,
        1.5f,       // pitchWindow - 1.5 semitones total window
        0.5f,       // pitchWindowPerfect - 0.5 semitones for perfect
        0.75,       // phraseHitPercent - 75% needed for AWESOME
        60.0,       // approximateVocalFps
        true,       // singToActivateStarPower
        1000);      // pointsPerPhrase

    // Cached reflection accessors for YargFreeVocalsEngine
    private static readonly MethodInfo CanVocalNoteBeHitMethod =
        typeof(YargFreeVocalsEngine).GetMethod("CanVocalNoteBeHit",
            BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("Could not find CanVocalNoteBeHit on YargFreeVocalsEngine");

    private static readonly PropertyInfo PitchSangProperty =
        typeof(VocalsEngine).GetProperty("PitchSang",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("Could not find PitchSang property");

    private static readonly PropertyInfo CurrentTimeProperty =
        typeof(YargFreeVocalsEngine).BaseType.BaseType.GetProperty("CurrentTime",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("Could not find CurrentTime property");

    private static readonly FieldInfo MicPitchesField =
        typeof(YargFreeVocalsEngine).GetField("_micPitches",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
        ?? throw new InvalidOperationException("Could not find _micPitches field");

    private static readonly FieldInfo MicPartHitsField =
        typeof(YargFreeVocalsEngine).GetField("_micPartHits",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
        ?? throw new InvalidOperationException("Could not find _micPartHits field");

    private static readonly FieldInfo CanonicalMetersField =
        typeof(YargFreeVocalsEngine).GetField("_canonicalMeters",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
        ?? throw new InvalidOperationException("Could not find _canonicalMeters field");

    private static readonly PropertyInfo BaseStatsProperty =
        typeof(YargFreeVocalsEngine).BaseType.BaseType.GetProperty("BaseStats",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("Could not find BaseStats property");

    /// <summary>
    /// Construct a VocalsPart with phrases containing notes at specified pitches and tick ranges.
    /// Note definitions: (startTick, endTick, pitch) where pitch is MIDI note number.
    /// </summary>
    private static VocalsPart CreatePartWithPhrases(params (uint startTick, uint endTick, float pitch)[] noteDefs)
    {
        var part = new VocalsPart(false, new(), new(), new(), new());

        foreach (var (startTick, endTick, pitch) in noteDefs)
        {
            var note = new VocalNote(NoteFlags.None, false, 0.0, 1.0, startTick, endTick - startTick);

            if (pitch >= 0)
            {
                // Pitched note
                var lyricNote = new VocalNote(pitch, 0, VocalNoteType.Lyric, 0.0, 0.5, startTick, (endTick - startTick) / 2);
                note.AddChildNote(lyricNote);

                var lyrics = new List<LyricEvent>
                {
                    new LyricEvent(LyricSymbolFlags.None, "Test", 0.0, startTick)
                };
                part.NotePhrases.Add(new VocalsPhrase(0.0, 1.0, startTick, endTick - startTick, note, lyrics));
            }
            else
            {
                // Talkie note (non-pitched)
                var lyricNote = new VocalNote(-1, 0, VocalNoteType.Lyric, 0.0, 0.5, startTick, (endTick - startTick) / 2);
                note.AddChildNote(lyricNote);

                var lyrics = new List<LyricEvent>
                {
                    new LyricEvent(LyricSymbolFlags.NonPitched, "Talk", 0.0, startTick)
                };
                part.NotePhrases.Add(new VocalsPhrase(0.0, 1.0, startTick, endTick - startTick, note, lyrics));
            }
        }

        return part;
    }

    /// <summary>
    /// Run the engine through a sequence of (time, micPitches) inputs and return
    /// the collected phrase grades and final score.
    /// </summary>
    private static (List<PhraseGrade> grades, int finalScore) RunScenario(
        IReadOnlyList<VocalsPart> parts,
        int micCount,
        IEnumerable<(double time, float[] micPitches)> inputs)
    {
        // Create sync track with 120 BPM (2 beats per second, 480 ticks per beat)
        var syncTrack = new SyncTrack(480);

        // Create primary chart from first part
        var primaryChart = parts[0].CloneAsInstrumentDifficulty();

        // Create engine with specified mic count
        var engine = new YargFreeVocalsEngine(primaryChart, parts, syncTrack, ScenarioParams, false, micCount);

        var grades = new List<PhraseGrade>();

        // Subscribe to phrase events
        engine.OnPartyVocalsPhrase += (grade, meters, isLast) =>
        {
            grades.Add(grade);
        };

        // Feed inputs to the engine
        foreach (var (time, micPitches) in inputs)
        {
            // Set pitch for each microphone
            for (int i = 0; i < micCount && i < micPitches.Length; i++)
            {
                engine.SetMicPitch(i, micPitches[i]);
            }

            // Update engine to current time
            engine.Update(time);
        }

        // Advance past all phrases to ensure phrase completion events are fired
        var maxTime = inputs.Any() ? inputs.Max(i => i.time) + 1.0 : 0.1;
        engine.Update(maxTime + 1.0);

        return (grades, engine.BaseStats.NoteScore);
    }

    /// <summary>
    /// Create a simple sync track with 120 BPM tempo.
    /// </summary>
    private static SyncTrack CreateSyncTrackWithTempo()
    {
        var syncTrack = new SyncTrack(480);

        // Add tempo at time 0: 120 BPM = 2 beats per second
        // 120 BPM = 0.5 seconds per beat = 240000 microseconds per beat
        syncTrack.Tempos.Add(new TempoChange(120, 0, 0));

        return syncTrack;
    }

    /// <summary>
    /// Helper to create a 3-part chart for testing.
    /// </summary>
    private static List<VocalsPart> Create3PartChart()
    {
        var parts = new List<VocalsPart>
        {
            CreatePartWithPhrases((0, 480, 60)),  // HARM1: C4
            CreatePartWithPhrases((0, 480, 64)),  // HARM2: E4
            CreatePartWithPhrases((0, 480, 67)),  // HARM3: G4
        };

        return parts;
    }

    /// <summary>
    /// Helper to create a 2-part chart for testing.
    /// </summary>
    private static List<VocalsPart> Create2PartChart()
    {
        var parts = new List<VocalsPart>
        {
            CreatePartWithPhrases((0, 480, 60)),  // HARM1: C4
            CreatePartWithPhrases((0, 480, 64)),  // HARM2: E4
        };

        return parts;
    }

    /// <summary>
    /// Helper method to invoke CanVocalNoteBeHit via reflection.
    /// </summary>
    private static (bool hit, float hitPercent) InvokeCanVocalNoteBeHit(
        YargFreeVocalsEngine engine, VocalNote note, float sungPitch)
    {
        // Set PitchSang (protected setter on VocalsEngine)
        PitchSangProperty.SetValue(engine, sungPitch);

        // Set CurrentTime so note.PitchAtSongTime returns the note's pitch.
        CurrentTimeProperty.SetValue(engine, 0.0);

        // Call CanVocalNoteBeHit(note, out float hitPercent)
        var hitPercent = new object[2];
        hitPercent[0] = note;
        hitPercent[1] = 0f; // default

        var result = (bool)CanVocalNoteBeHitMethod.Invoke(engine, hitPercent)!;

        return (result, (float)hitPercent[1]);
    }

    /// <summary>
    /// Helper to get the canonical meters via reflection for testing.
    /// </summary>
    private static double[] GetCanonicalMeters(YargFreeVocalsEngine engine)
    {
        return (double[])CanonicalMetersField.GetValue(engine)!;
    }

    /// <summary>
    /// Helper to get the mic part hits via reflection for testing.
    /// </summary>
    private static double[,] GetMicPartHits(YargFreeVocalsEngine engine)
    {
        return (double[,])MicPartHitsField.GetValue(engine)!;
    }

    /// <summary>
    /// Helper to get the mic pitches via reflection for testing.
    /// </summary>
    private static float[] GetMicPitches(YargFreeVocalsEngine engine)
    {
        return (float[])MicPitchesField.GetValue(engine)!;
    }
}