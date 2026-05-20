using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Reflection;
using YARG.Core.Chart;
using YARG.Core.Engine;
using YARG.Core.Engine.Vocals;
using YARG.Core.Engine.Vocals.Engines;

namespace YARG.Core.UnitTests.Engine;

[TestFixture]
public sealed class FreeVocalsEngineTests
{
    // Pitch window: perfect <= 0.5 semitones, total window = 1.5 semitones
    private static readonly VocalsEngineParameters EngineParameters = new(
        new HitWindowSettings(0.1, 0.1, 1.0, false, 0, 1, 1, 0),
        4,
        VocalsEngineTests.StarMultiplierThresholds,
        VocalsEngineTests.SoloBonusStarMultiplierThresholds,
        1.5f,       // pitchWindow
        0.5f,       // pitchWindowPerfect
        0.75,       // phraseHitPercent
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

    // ================================================================
    // AC2.1: Singing HARM2 pitch (not HARM1) -> CanVocalNoteBeHit true
    // ================================================================
    [Test]
    public void SingHARM2Pitch_MatchesHARM2Note_ReturnsTrue()
    {
        var engine = CreateEngine(out var parts);

        var harm2Note = parts[1].NotePhrases[0].PhraseParentNote.ChildNotes[0];

        // Sing E4 = 64 (matches HARM2, not HARM1)
        var (hit, hitPercent) = InvokeCanVocalNoteBeHit(engine, harm2Note, sungPitch: 64f);

        Assert.That(hit, Is.True, "Should hit HARM2 note when singing matching pitch");
        Assert.That(hitPercent, Is.EqualTo(1f), "Perfect match should give full hit percent");
    }

    // ================================================================
    // AC2.1: Singing HARM2 pitch against HARM1 note -> no hit
    // ================================================================
    [Test]
    public void SingHARM2Pitch_AgainstHARM1Note_ReturnsFalse()
    {
        var engine = CreateEngine(out var parts);

        var harm1Note = parts[0].NotePhrases[0].PhraseParentNote.ChildNotes[0];

        // Sing E4 = 64 against HARM1 (C4 = 60). Distance = 4 semitones > pitchWindow (1.5)
        var (hit, _) = InvokeCanVocalNoteBeHit(engine, harm1Note, sungPitch: 64f);

        Assert.That(hit, Is.False, "Singing E4 against C4 note should not hit (4 semitones apart)");
    }

    // ================================================================
    // AC2.2: Unison -- both HARM1/HARM2 same pitch, both hittable
    // ================================================================
    [Test]
    public void SingUnisonPitch_BothPartsMatch()
    {
        var engine = CreateEngine(out var parts, harm2Pitch: 60);

        var harm1Note = parts[0].NotePhrases[0].PhraseParentNote.ChildNotes[0];
        var harm2Note = parts[1].NotePhrases[0].PhraseParentNote.ChildNotes[0];

        // Sing C4 = 60
        var (hit1, pct1) = InvokeCanVocalNoteBeHit(engine, harm1Note, sungPitch: 60f);
        var (hit2, pct2) = InvokeCanVocalNoteBeHit(engine, harm2Note, sungPitch: 60f);

        Assert.That(hit1, Is.True, "HARM1 note should be hittable for unison pitch");
        Assert.That(hit2, Is.True, "HARM2 note should be hittable for unison pitch");
        Assert.That(pct1, Is.EqualTo(1f));
        Assert.That(pct2, Is.EqualTo(1f));
    }

    // ================================================================
    // AC2.1: Octave-equivalent match (sung = expected + 12) -> hit
    // ================================================================
    [Test]
    public void SingOctaveAbove_MatchesHARM1Note_ReturnsTrue()
    {
        var engine = CreateEngine(out var parts);

        var harm1Note = parts[0].NotePhrases[0].PhraseParentNote.ChildNotes[0];

        // Sing C5 = 72 (one octave above C4 = 60)
        var (hit, hitPercent) = InvokeCanVocalNoteBeHit(engine, harm1Note, sungPitch: 72f);

        Assert.That(hit, Is.True, "Octave-equivalent pitch should hit");
        Assert.That(hitPercent, Is.EqualTo(1f), "Octave match should give full hit percent");
    }

    // ================================================================
    // AC2.1: Octave-equivalent match (sung = expected - 12) -> hit
    // ================================================================
    [Test]
    public void SingOctaveBelow_MatchesHARM1Note_ReturnsTrue()
    {
        var engine = CreateEngine(out var parts);

        var harm1Note = parts[0].NotePhrases[0].PhraseParentNote.ChildNotes[0];

        // Sing C3 = 48 (one octave below C4 = 60)
        var (hit, hitPercent) = InvokeCanVocalNoteBeHit(engine, harm1Note, sungPitch: 48f);

        Assert.That(hit, Is.True, "Octave-below pitch should hit");
        Assert.That(hitPercent, Is.EqualTo(1f));
    }

    // ================================================================
    // AC2.1: No match (pitch outside all windows) -> no hit
    // ================================================================
    [Test]
    public void SingDistantPitch_NoMatchOnEitherPart()
    {
        var engine = CreateEngine(out var parts);

        var harm1Note = parts[0].NotePhrases[0].PhraseParentNote.ChildNotes[0];
        var harm2Note = parts[1].NotePhrases[0].PhraseParentNote.ChildNotes[0];

        // Sing F#4 = 66. Distance to C4 = 6, to E4 = 2. Both > pitchWindow (1.5).
        var (hit1, pct1) = InvokeCanVocalNoteBeHit(engine, harm1Note, sungPitch: 66f);
        var (hit2, pct2) = InvokeCanVocalNoteBeHit(engine, harm2Note, sungPitch: 66f);

        Assert.That(hit1, Is.False, "F# should not match C4 (6 semitones apart)");
        Assert.That(pct1, Is.EqualTo(0f), "No percent when outside window");
        Assert.That(hit2, Is.False, "F# should not match E4 (2 semitones apart)");
        Assert.That(pct2, Is.EqualTo(0f), "No percent when outside window");
    }

    // ================================================================
    // Pitch within window but not perfect -> partial hit percent
    // ================================================================
    [Test]
    public void SingSlightlyOffPitch_WithinWindow_ReturnsPartialPercent()
    {
        var engine = CreateEngine(out var parts);

        var harm1Note = parts[0].NotePhrases[0].PhraseParentNote.ChildNotes[0];

        // Sing C#4 = 61 (distance = 1 semitone, within window but not perfect)
        var (hit, hitPercent) = InvokeCanVocalNoteBeHit(engine, harm1Note, sungPitch: 61f);

        Assert.That(hit, Is.True, "1 semitone off should still be within pitch window");
        Assert.That(hitPercent, Is.GreaterThan(0f).And.LessThan(1f),
            "Partial percent for slightly off pitch");
    }

    // ================================================================
    // Non-pitched note always hittable regardless of sung pitch
    // ================================================================
    [Test]
    public void NonPitchedNote_AlwaysHittable()
    {
        var engine = CreateEngine(out _);

        // Non-pitched note (pitch = -1)
        var nonPitchedNote = new VocalNote(-1, 0, VocalNoteType.Lyric, 0.0, 0.5, 0, 240);

        var (hit, hitPercent) = InvokeCanVocalNoteBeHit(engine, nonPitchedNote, sungPitch: 999f);

        Assert.That(hit, Is.True, "Non-pitched notes should always be hittable");
        Assert.That(hitPercent, Is.EqualTo(1f));
    }

    // ================================================================
    // Multiple octaves apart still match
    // ================================================================
    [Test]
    public void TwoOctavesApart_StillMatches()
    {
        var engine = CreateEngine(out var parts);

        var harm1Note = parts[0].NotePhrases[0].PhraseParentNote.ChildNotes[0];

        // Sing C6 = 84 (two octaves above C4 = 60)
        var (hit, _) = InvokeCanVocalNoteBeHit(engine, harm1Note, sungPitch: 84f);

        Assert.That(hit, Is.True, "Two octaves apart should still match via octave equivalence");
    }

    // ================================================================
    // 3-part track: singing HARM3 pitch matches only HARM3
    // ================================================================
    [Test]
    public void ThreePartTrack_SingHARM3_MatchesHARM3Only()
    {
        var parts = Create3Parts();
        var primaryChart = parts[0].CloneAsInstrumentDifficulty();
        var engine = new YargFreeVocalsEngine(primaryChart, parts, new SyncTrack(480), EngineParameters, false);

        var harm1Note = parts[0].NotePhrases[0].PhraseParentNote.ChildNotes[0];
        var harm2Note = parts[1].NotePhrases[0].PhraseParentNote.ChildNotes[0];
        var harm3Note = parts[2].NotePhrases[0].PhraseParentNote.ChildNotes[0];

        // Sing G4 = 67 (HARM3 pitch)
        var (hit1, _) = InvokeCanVocalNoteBeHit(engine, harm1Note, sungPitch: 67f);
        var (hit2, _) = InvokeCanVocalNoteBeHit(engine, harm2Note, sungPitch: 67f);
        var (hit3, pct3) = InvokeCanVocalNoteBeHit(engine, harm3Note, sungPitch: 67f);

        Assert.That(hit1, Is.False, "G4 should not match C4 (7 semitones)");
        Assert.That(hit2, Is.False, "G4 should not match E4 (3 semitones)");
        Assert.That(hit3, Is.True, "G4 should match G4 perfectly");
        Assert.That(pct3, Is.EqualTo(1f));
    }

    // ================================================================
    // CurrentTargetHarmonyIndex defaults to 0
    // ================================================================
    [Test]
    public void CurrentTargetHarmonyIndex_DefaultsToZero()
    {
        var engine = CreateEngine(out _);
        Assert.That(engine.CurrentTargetHarmonyIndex, Is.EqualTo(0));
    }

    // ================================================================
    // Engine creates with correct part count and harmony flags
    // ================================================================
    [Test]
    public void EngineCreation_TwoParts_CorrectFlags()
    {
        CreateEngine(out var parts);
        Assert.That(parts.Count, Is.EqualTo(2));
        Assert.That(parts[0].IsHarmony, Is.False, "HARM1 should not be flagged as harmony");
        Assert.That(parts[1].IsHarmony, Is.True, "HARM2 should be flagged as harmony");
    }

    // ================================================================
    // Bot mode defaults to HARM1 target
    // ================================================================
    [Test]
    public void BotMode_TargetsHARM1()
    {
        var engine = CreateEngine(out _, isBot: true);
        Assert.That(engine.CurrentTargetHarmonyIndex, Is.EqualTo(0));
    }

    // ================================================================
    // Pitch window boundary: 2 semitones off is outside window of 1.5
    // ================================================================
    [Test]
    public void SingAtPitchWindowBoundary_OutsideReturnsFalse()
    {
        var engine = CreateEngine(out var parts);
        var harm1Note = parts[0].NotePhrases[0].PhraseParentNote.ChildNotes[0];

        var (hit, _) = InvokeCanVocalNoteBeHit(engine, harm1Note, sungPitch: 62f);
        Assert.That(hit, Is.False, "2 semitones should be outside pitch window of 1.5");
    }

    // ================================================================
    // Well outside window -> hit percent exactly zero
    // ================================================================
    [Test]
    public void SingWellOutsideWindow_HitPercentIsExactlyZero()
    {
        var engine = CreateEngine(out var parts);
        var harm1Note = parts[0].NotePhrases[0].PhraseParentNote.ChildNotes[0];

        var (_, pct) = InvokeCanVocalNoteBeHit(engine, harm1Note, sungPitch: 80f);
        Assert.That(pct, Is.EqualTo(0f), "Hit percent should be exactly 0 when well outside window");
    }

    // ================================================================
    // Helpers
    // ================================================================

    private static YargFreeVocalsEngine CreateEngine(
        out List<VocalsPart> parts,
        int harm1Pitch = 60,
        int harm2Pitch = 64,
        bool isBot = false)
    {
        parts = new List<VocalsPart>
        {
            CreateVocalsPart(isHarmony: false),
            CreateVocalsPart(isHarmony: true),
        };

        AddPhraseWithPitch(parts[0], harm1Pitch, tickOffset: 0);
        AddPhraseWithPitch(parts[1], harm2Pitch, tickOffset: 0);

        var primaryChart = parts[0].CloneAsInstrumentDifficulty();
        var syncTrack = new SyncTrack(480);

        return new YargFreeVocalsEngine(primaryChart, parts, syncTrack, EngineParameters, isBot);
    }

    private static List<VocalsPart> Create3Parts()
    {
        var parts = new List<VocalsPart>
        {
            CreateVocalsPart(isHarmony: false),
            CreateVocalsPart(isHarmony: true),
            CreateVocalsPart(isHarmony: true),
        };

        AddPhraseWithPitch(parts[0], 60, tickOffset: 0);
        AddPhraseWithPitch(parts[1], 64, tickOffset: 0);
        AddPhraseWithPitch(parts[2], 67, tickOffset: 0);

        return parts;
    }

    private static VocalsPart CreateVocalsPart(bool isHarmony)
    {
        return new VocalsPart(isHarmony, new(), new(), new(), new());
    }

    private static void AddPhraseWithPitch(VocalsPart part, int midiPitch, uint tickOffset)
    {
        var note = new VocalNote(NoteFlags.None, false, 0.0, 1.0, tickOffset, 480);
        var lyricNote = new VocalNote(midiPitch, 0, VocalNoteType.Lyric, 0.0, 0.5, tickOffset, 240);
        note.AddChildNote(lyricNote);
        var lyrics = new List<LyricEvent>
        {
            new LyricEvent(LyricSymbolFlags.None, "Test", 0.0, tickOffset)
        };
        part.NotePhrases.Add(new VocalsPhrase(0.0, 1.0, tickOffset, 480, note, lyrics));
    }

    /// <summary>
    /// Invokes the engine's real CanVocalNoteBeHit method via reflection.
    /// Sets PitchSang and CurrentTime on the engine instance before calling.
    /// </summary>
    private static (bool hit, float hitPercent) InvokeCanVocalNoteBeHit(
        YargFreeVocalsEngine engine, VocalNote note, float sungPitch)
    {
        // Set PitchSang (protected setter on VocalsEngine)
        PitchSangProperty.SetValue(engine, sungPitch);

        // Set CurrentTime so note.PitchAtSongTime returns the note's pitch.
        // At time 0, a note at time 0 with timeLength > 0 returns its Pitch.
        CurrentTimeProperty.SetValue(engine, 0.0);

        // Call CanVocalNoteBeHit(note, out float hitPercent)
        var hitPercent = new object[2];
        hitPercent[0] = note;
        hitPercent[1] = 0f; // default

        var result = (bool)CanVocalNoteBeHitMethod.Invoke(engine, hitPercent)!;

        return (result, (float)hitPercent[1]);
    }
}
