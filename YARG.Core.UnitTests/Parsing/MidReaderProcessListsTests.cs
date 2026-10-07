using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using MoonscraperChartEditor.Song;
using MoonscraperChartEditor.Song.IO;
using NUnit.Framework;
using YARG.Core.Chart;

namespace YARG.Core.UnitTests.Parsing;

using static MoonSong;
using static MoonNote;
using static YARG.Core.UnitTests.Parsing.MoonNoteAssertions;
using MidiTextEvent = Melanchall.DryWetMidi.Core.TextEvent;

public class MidReaderProcessListsTests
{
    private const short Resolution = 192;

    private readonly record struct TimedMidiEvent(long Tick, MidiEvent Event);

    private static SevenBitNumber S(int number) => (SevenBitNumber) (byte) number;
    private static FourBitNumber F(int number) => (FourBitNumber) (byte) number;

    [Test]
    public void ReadMidi_ThrowsForMidiWithNoTracks()
    {
        var midi = new MidiFile
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(Resolution),
        };

        Assert.Throws<InvalidDataException>(() => MidReader.ReadMidi(midi));
    }

    [Test]
    public void ReadMidi_ThrowsForNonTicksPerQuarterNoteTimeDivision()
    {
        var midi = new MidiFile(MakeSyncTrack())
        {
            TimeDivision = new SmpteTimeDivision(SmpteFormat.Thirty, 80),
        };

        Assert.Throws<InvalidDataException>(() => MidReader.ReadMidi(midi));
    }

    [Test]
    public void ReadMidi_ParsesSyncTrackFromFirstTrack()
    {
        var midi = MakeMidi();

        var song = MidReader.ReadMidi(midi);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(song.resolution, Is.EqualTo(Resolution));
            Assert.That(song.syncTrack.Tempos, Has.Count.EqualTo(1));
            Assert.That(song.syncTrack.Tempos[0].BeatsPerMinute, Is.EqualTo(150).Within(0.001));
            Assert.That(song.syncTrack.TimeSignatures, Has.Count.EqualTo(1));
            Assert.That(song.syncTrack.TimeSignatures[0].Numerator, Is.EqualTo(7));
            Assert.That(song.syncTrack.TimeSignatures[0].Denominator, Is.EqualTo(8));
        }
    }

    [Test]
    public void GuitarOpenNotes_AreIgnoredUntilEnhancedOpensTextEvent()
    {
        var openNote = MidIOHelper.GUITAR_DIFF_START_LOOKUP[Difficulty.Expert] - 1;
        var withoutEnhancedOpens = MidReader.ReadMidi(MakeMidi(
            MakeTrack(MidIOHelper.GUITAR_TRACK, Note(10, 100, openNote))));
        var withEnhancedOpens = MidReader.ReadMidi(MakeMidi(
            MakeTrack(MidIOHelper.GUITAR_TRACK,
                Text(0, $"[{MidIOHelper.ENHANCED_OPENS_TEXT}]"),
                Note(10, 100, openNote))));

        var withoutNotes = withoutEnhancedOpens.GetChart(MoonInstrument.Guitar, Difficulty.Expert).notes;
        var withNotes = withEnhancedOpens.GetChart(MoonInstrument.Guitar, Difficulty.Expert).notes;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(withoutNotes, Is.Empty);
            Assert.That(withNotes, Has.Count.EqualTo(1));
            Assert.That(withNotes[0].guitarFret, Is.EqualTo(GuitarFret.Open));
        }
    }

    [Test]
    public void DrumVelocities_SetAccentAndGhostFlagsAfterChartDynamicsTextEvent()
    {
        var red = MidIOHelper.DRUMS_DIFF_START_LOOKUP[Difficulty.Expert] + 1;
        var blue = MidIOHelper.DRUMS_DIFF_START_LOOKUP[Difficulty.Expert] + 3;
        var midi = MakeMidi(MakeTrack(MidIOHelper.DRUMS_TRACK,
            Text(0, $"[{MidIOHelper.CHART_DYNAMICS_TEXT}]"),
            Note(10, 100, red, velocity: MidIOHelper.VELOCITY_ACCENT),
            Note(110, 200, blue, velocity: MidIOHelper.VELOCITY_GHOST)));

        var song = MidReader.ReadMidi(midi);
        var notes = song.GetChart(MoonInstrument.Drums, Difficulty.Expert).notes;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(notes, Has.Count.EqualTo(2));
            Assert.That(notes[0].drumPad, Is.EqualTo(DrumPad.Red));
            AssertHasFlag(notes[0], Flags.ProDrums_Accent);
            Assert.That(notes[1].drumPad, Is.EqualTo(DrumPad.Blue));
            AssertHasFlag(notes[1], Flags.ProDrums_Cymbal);
            AssertHasFlag(notes[1], Flags.ProDrums_Ghost);
        }
    }

    [TestCase(0, false)]
    [TestCase(1, true)]
    [TestCase(2, false)]
    public void EliteDrumsFlamModifier_ChannelOneSelectsFlatFlam(int channel, bool isFlatFlam)
    {
        var start = MidIOHelper.ELITE_DRUMS_DIFF_START_LOOKUP[Difficulty.Expert];
        var note = start + 2;
        var modifier = start + 13;
        var song = MidReader.ReadMidi(MakeMidi(MakeTrack(MidIOHelper.ELITE_DRUMS_TRACK,
            Note(10, 20, modifier, channel: channel),
            Note(12, 30, note, velocity: MidIOHelper.VELOCITY_ACCENT))));
        var raw = song.GetChart(MoonInstrument.EliteDrums, Difficulty.Expert).notes.Single();
        var loaded = new MoonSongLoader(song, ParseSettings.Default)
            .LoadEliteDrumsTrack(YARG.Core.Instrument.EliteDrums).GetDifficulty(YARG.Core.Difficulty.Expert).Notes.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(raw.flags.HasFlag(Flags.EliteDrums_FlatFlam), Is.EqualTo(isFlatFlam));
            Assert.That(raw.flags.HasFlag(Flags.EliteDrums_Flam), Is.EqualTo(!isFlatFlam));
            Assert.That(loaded.IsFlatFlam, Is.EqualTo(isFlatFlam));
            Assert.That(loaded.IsFlam, Is.EqualTo(!isFlatFlam));
            Assert.That(loaded.Dynamics, Is.EqualTo(DrumNoteType.Accent));
        }
    }

    [Test]
    public void EliteDrumsFlatFlamModifier_ExcludesEndTick()
    {
        var start = MidIOHelper.ELITE_DRUMS_DIFF_START_LOOKUP[Difficulty.Expert];
        var song = MidReader.ReadMidi(MakeMidi(MakeTrack(MidIOHelper.ELITE_DRUMS_TRACK,
            Note(10, 20, start + 13, channel: MidIOHelper.ELITE_DRUMS_CHANNEL_FLAT_FLAM),
            Note(20, 30, start + 2))));
        var note = song.GetChart(MoonInstrument.EliteDrums, Difficulty.Expert).notes.Single();
        Assert.That(note.flags, Is.Not.EqualTo(Flags.EliteDrums_FlatFlam));
    }

    [Test]
    public void EliteDrumsFlatFlamModifier_IsIgnoredInsideSamePadAuthoredRollLane()
    {
        var start = MidIOHelper.ELITE_DRUMS_DIFF_START_LOOKUP[Difficulty.Expert];
        var song = MidReader.ReadMidi(MakeMidi(MakeTrack(MidIOHelper.ELITE_DRUMS_TRACK,
            Note(10, 20, MidIOHelper.ELITE_DRUMS_SNARE_ROLL_LANE_NOTE),
            Note(10, 20, start + 13, channel: MidIOHelper.ELITE_DRUMS_CHANNEL_FLAT_FLAM),
            Note(12, 30, start + 1))));
        var loaded = new MoonSongLoader(song, ParseSettings.Default)
            .LoadEliteDrumsTrack(YARG.Core.Instrument.EliteDrums).GetDifficulty(YARG.Core.Difficulty.Expert).Notes.Single();
        Assert.That(loaded.IsFlatFlam, Is.False);
        Assert.That(loaded.IsFlam, Is.False);
    }

    [Test]
    public void EliteDrumsStrictHatPedalStateTextEvent_SetsStrictHatPedalFlag()
    {
        var hatPedal = MidIOHelper.ELITE_DRUMS_DIFF_START_LOOKUP[Difficulty.Expert] - 2;
        var midi = MakeMidi(MakeTrack(MidIOHelper.ELITE_DRUMS_TRACK,
            Text(0, $"[{MidIOHelper.STRICT_HAT_PEDAL_STATE}]"),
            Note(10, 100, hatPedal)));

        var song = MidReader.ReadMidi(midi);
        var notes = song.GetChart(MoonInstrument.EliteDrums, Difficulty.Expert).notes;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(notes, Has.Count.EqualTo(1));
            Assert.That(notes[0].eliteDrumPad, Is.EqualTo(EliteDrumPad.HatPedal));
            AssertHasFlag(notes[0], Flags.EliteDrums_StrictHatState);
        }
    }

    [Test]
    public void EliteDrumsNonStrictHatPedalChordedWithHiHat_SuppressedAsInvisibleTerminator()
    {
        // The loader-side rule the scan-time preparser must mirror (see
        // EliteDrumsDownchartScanTests): a non-strict hat pedal chorded with a
        // hi-hat that is not forced-indifferent becomes an invisible terminator,
        // which the downchart builder never converts — even when channel flagged.
        var hatPedal = MidIOHelper.ELITE_DRUMS_DIFF_START_LOOKUP[Difficulty.Expert] - 2;
        var hiHat = MidIOHelper.ELITE_DRUMS_DIFF_START_LOOKUP[Difficulty.Expert] + 2;
        var midi = MakeMidi(MakeTrack(MidIOHelper.ELITE_DRUMS_TRACK,
            Note(10, 100, hatPedal, channel: MidIOHelper.ELITE_DRUMS_CHANNEL_FLAG_YELLOW),
            Note(10, 100, hiHat)));

        var song = MidReader.ReadMidi(midi);
        var notes = song.GetChart(MoonInstrument.EliteDrums, Difficulty.Expert).notes;

        var pedal = notes.Single(note => note.eliteDrumPad is EliteDrumPad.HatPedal);
        var hat = notes.Single(note => note.eliteDrumPad is EliteDrumPad.HiHat);

        using (Assert.EnterMultipleScope())
        {
            AssertHasFlag(pedal, Flags.EliteDrums_InvisibleTerminator);
            AssertHasFlag(pedal, Flags.EliteDrums_ChannelFlagYellow);
            AssertDoesNotHaveFlag(hat, Flags.EliteDrums_InvisibleTerminator);
        }
    }

    [Test]
    public void EliteDrumsNonStrictHatPedalChordedWithIndifferentHiHat_NotSuppressed()
    {
        // The forced-indifferent variant: an "indifferent hat" marker covering the
        // chord leaves the hi-hat non-suppressing, so the pedal survives for the
        // downchart builder and the scan-time mask must advertise the difficulty.
        var hatPedal = MidIOHelper.ELITE_DRUMS_DIFF_START_LOOKUP[Difficulty.Expert] - 2;
        var hiHat = MidIOHelper.ELITE_DRUMS_DIFF_START_LOOKUP[Difficulty.Expert] + 2;
        var indifferentMarker = MidIOHelper.ELITE_DRUMS_DIFF_START_LOOKUP[Difficulty.Expert] + 14;
        var midi = MakeMidi(MakeTrack(MidIOHelper.ELITE_DRUMS_TRACK,
            Note(0, 200, indifferentMarker),
            Note(10, 100, hatPedal, channel: MidIOHelper.ELITE_DRUMS_CHANNEL_FLAG_YELLOW),
            Note(10, 100, hiHat)));

        var song = MidReader.ReadMidi(midi);
        var notes = song.GetChart(MoonInstrument.EliteDrums, Difficulty.Expert).notes;

        var pedal = notes.Single(note => note.eliteDrumPad is EliteDrumPad.HatPedal);
        var hat = notes.Single(note => note.eliteDrumPad is EliteDrumPad.HiHat);

        using (Assert.EnterMultipleScope())
        {
            AssertHasFlag(hat, Flags.EliteDrums_ForcedIndifferent);
            AssertDoesNotHaveFlag(pedal, Flags.EliteDrums_InvisibleTerminator);
        }
    }

    [Test]
    public void EliteDrumsStrictHatPedalChordedWithHiHat_NotSuppressed()
    {
        // Strict hat pedal state wins over chord context: the pedal keeps
        // converting for the downchart, so the scan mask must advertise.
        var hatPedal = MidIOHelper.ELITE_DRUMS_DIFF_START_LOOKUP[Difficulty.Expert] - 2;
        var hiHat = MidIOHelper.ELITE_DRUMS_DIFF_START_LOOKUP[Difficulty.Expert] + 2;
        var midi = MakeMidi(MakeTrack(MidIOHelper.ELITE_DRUMS_TRACK,
            Text(0, $"[{MidIOHelper.STRICT_HAT_PEDAL_STATE}]"),
            Note(10, 100, hatPedal, channel: MidIOHelper.ELITE_DRUMS_CHANNEL_FLAG_YELLOW),
            Note(10, 100, hiHat)));

        var song = MidReader.ReadMidi(midi);
        var notes = song.GetChart(MoonInstrument.EliteDrums, Difficulty.Expert).notes;

        var pedal = notes.Single(note => note.eliteDrumPad is EliteDrumPad.HatPedal);

        using (Assert.EnterMultipleScope())
        {
            AssertHasFlag(pedal, Flags.EliteDrums_StrictHatState);
            AssertDoesNotHaveFlag(pedal, Flags.EliteDrums_InvisibleTerminator);
        }
    }

    [Test]
    public void EliteDrumsStrictPedal_AppliesDurationBasedClosedFlag()
    {
        var start = MidIOHelper.ELITE_DRUMS_DIFF_START_LOOKUP[Difficulty.Expert];
        var song = MidReader.ReadMidi(MakeMidi(MakeTrack(MidIOHelper.ELITE_DRUMS_TRACK,
            Text(0, $"[{MidIOHelper.STRICT_HAT_PEDAL_STATE}]"),
            Note(10, 20, start - 2),
            Note(15, 30, start + 2))));
        var notes = song.GetChart(MoonInstrument.EliteDrums, Difficulty.Expert).notes;

        using (Assert.EnterMultipleScope())
        {
            AssertHasFlag(notes.Single(note => note.eliteDrumPad is EliteDrumPad.HatPedal), Flags.EliteDrums_StrictHatState);
            AssertHasFlag(notes.Single(note => note.eliteDrumPad is EliteDrumPad.HiHat), Flags.EliteDrums_ForcedClosed);
        }
    }

    [Test]
    public void EliteDrumsHatPedalChordDoesNotCrossDifficulties()
    {
        // Chord context is per-difficulty: an Expert hat pedal at the same tick as
        // a Hard-only hi-hat is NOT chorded with it, so it must survive.
        var hatPedal = MidIOHelper.ELITE_DRUMS_DIFF_START_LOOKUP[Difficulty.Expert] - 2;
        var hardHiHat = MidIOHelper.ELITE_DRUMS_DIFF_START_LOOKUP[Difficulty.Hard] + 2;
        var midi = MakeMidi(MakeTrack(MidIOHelper.ELITE_DRUMS_TRACK,
            Note(10, 100, hatPedal, channel: MidIOHelper.ELITE_DRUMS_CHANNEL_FLAG_YELLOW),
            Note(10, 100, hardHiHat)));

        var song = MidReader.ReadMidi(midi);
        var expertNotes = song.GetChart(MoonInstrument.EliteDrums, Difficulty.Expert).notes;

        var pedal = expertNotes.Single(note => note.eliteDrumPad is EliteDrumPad.HatPedal);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pedal.isChord, Is.False,
                "a Hard-only hi-hat is not part of the Expert pedal's chord");
            AssertDoesNotHaveFlag(pedal, Flags.EliteDrums_InvisibleTerminator);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void EliteKickPair_ProducesPlainExpertAndFlamExpertPlus(bool plusFirst)
    {
        const int plain = 74;
        const int plus = 73;
        var eliteTrack = MakeTrack(MidIOHelper.ELITE_DRUMS_TRACK,
            Note(10, 20, plus), Note(10, 20, plain),
            Note(10, 20, 75), Note(30, 40, plus), Note(50, 60, plain));
        if (!plusFirst)
        {
            // MakeTrack sorts equal-tick events by pitch; explicitly reverse their
            // MIDI order to exercise plain-first insertion as well.
            (eliteTrack.Events[1], eliteTrack.Events[2]) = (eliteTrack.Events[2], eliteTrack.Events[1]);
            eliteTrack.Events[1].DeltaTime = 10;
            eliteTrack.Events[2].DeltaTime = 0;
        }
        var midi = MakeMidi(eliteTrack,
            MakeTrack(MidIOHelper.DRUMS_TRACK, Note(10, 20, 96), Note(10, 20, 96)));

        var song = MidReader.ReadMidi(midi);
        var rawKicks = song.GetChart(MoonInstrument.EliteDrums, Difficulty.Expert).notes
            .Where(note => note.eliteDrumPad is EliteDrumPad.Kick).ToArray();
        var pair = rawKicks.Single(note => note.tick == 10);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(rawKicks, Has.Length.EqualTo(3));
            Assert.That(song.GetChart(MoonInstrument.EliteDrums, Difficulty.Expert).notes
                .Count(note => note.tick == 10 && note.eliteDrumPad is EliteDrumPad.Snare), Is.EqualTo(1));
            Assert.That(song.GetChart(MoonInstrument.Drums, Difficulty.Expert).notes, Has.Count.EqualTo(1));
            Assert.That(pair.rawNote, Is.EqualTo((int) EliteDrumPad.Kick));
            Assert.That(pair.pairedExtraKick, Is.Not.Null);
            Assert.That(pair.pairedExtraKick!.tick, Is.EqualTo(pair.tick));
            AssertHasFlag(pair.pairedExtraKick, Flags.InstrumentPlus);
            Assert.That(pair.Clone().pairedExtraKick, Is.Not.SameAs(pair.pairedExtraKick));
            AssertHasFlag(pair, Flags.EliteDrums_Flam);
            AssertDoesNotHaveFlag(pair, Flags.InstrumentPlus);
            AssertDoesNotHaveFlag(rawKicks.Single(note => note.tick == 30), Flags.EliteDrums_Flam);
            AssertHasFlag(rawKicks.Single(note => note.tick == 30), Flags.InstrumentPlus);
            AssertDoesNotHaveFlag(rawKicks.Single(note => note.tick == 50), Flags.EliteDrums_Flam);
        }

        var track = new MoonSongLoader(song, ParseSettings.Default).LoadEliteDrumsTrack(YARG.Core.Instrument.EliteDrums);
        foreach (var difficulty in new[] { YARG.Core.Difficulty.Expert, YARG.Core.Difficulty.ExpertPlus })
        {
            var kicks = track.GetDifficulty(difficulty).Notes
                .SelectMany(note => note.ChildNotes.Prepend(note))
                .Where(note => note.Pad == (int) YARG.Core.Chart.EliteDrumNote.EliteDrumPad.Kick).ToArray();
            var loadedPair = kicks.Single(note => note.Tick == 10);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(loadedPair.IsFlam, Is.EqualTo(difficulty == YARG.Core.Difficulty.ExpertPlus));
                Assert.That(loadedPair.IsDoubleKick, Is.False);
                Assert.That(kicks.Single(note => note.Tick == 50).IsFlam, Is.False);
                Assert.That(kicks.Any(note => note.Tick == 30), Is.EqualTo(difficulty == YARG.Core.Difficulty.ExpertPlus));
                if (difficulty == YARG.Core.Difficulty.ExpertPlus)
                    Assert.That(kicks.Single(note => note.Tick == 30).IsDoubleKick, Is.True);
            }
        }
    }

    [Test]
    public void GuitarForcingMarkers_ApplyAfterNotesAreParsed()
    {
        var green = MidIOHelper.GUITAR_DIFF_START_LOOKUP[Difficulty.Expert];
        var forcedStrum = green + 6;
        var tap = MidIOHelper.TAP_NOTE_CH;
        var midi = MakeMidi(MakeTrack(MidIOHelper.GUITAR_TRACK,
            Note(10, 100, forcedStrum),
            Note(20, 30, tap),
            Note(20, 100, green)));

        var song = MidReader.ReadMidi(midi);
        var notes = song.GetChart(MoonInstrument.Guitar, Difficulty.Expert).notes;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(notes, Has.Count.EqualTo(1));
            AssertHasFlag(notes[0], Flags.Tap);
            AssertDoesNotHaveFlag(notes[0], Flags.Forced);
            AssertDoesNotHaveFlag(notes[0], Flags.Forced_Strum);
        }
    }

    [Test]
    public void CodaPostProcessing_SetsCodaEndAndConvertsDrumFillToBigRockEnding()
    {
        var red = MidIOHelper.DRUMS_DIFF_START_LOOKUP[Difficulty.Expert] + 1;
        var midi = MakeMidi(
            MakeEventsTrack(
                Text(50, $"[{MidIOHelper.MIDCODA_START}]"),
                Text(250, $"[{MidIOHelper.MIDCODA_END}]")),
            MakeTrack(MidIOHelper.DRUMS_TRACK,
                Note(40, 45, red),
                Note(100, 105, red),
                Note(200, 205, red),
                Note(300, 305, red),
                Note(100, 220, MidIOHelper.DRUM_FILL_NOTE_0)));

        var song = MidReader.ReadMidi(midi);
        var chart = song.GetChart(MoonInstrument.Drums, Difficulty.Expert);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(chart.notes, Has.Count.EqualTo(4));
            AssertHasFlag(chart.notes.Single(note => note.tick == 200), Flags.CodaEnd);
            Assert.That(chart.specialPhrases, Has.One.Matches<MoonPhrase>(phrase =>
                phrase.type == MoonPhrase.Type.BigRockEnding && phrase.tick == 100 && phrase.length == 120));
            Assert.That(chart.specialPhrases, Has.None.Matches<MoonPhrase>(phrase =>
                phrase.type == MoonPhrase.Type.ProDrums_Activation));
        }
    }

    [Test]
    public void LegacyStarPowerFixup_ConvertsSoloToStarPowerWhenStarPowerOverrideIsUnset()
    {
        var settings = ParseSettings.Default_Midi;
        settings.StarPowerNote = ParseSettings.SETTING_DEFAULT;
        var midi = MakeMidi(MakeTrack(MidIOHelper.GUITAR_TRACK,
            Note(10, 100, MidIOHelper.SOLO_NOTE)));

        var song = MidReader.ReadMidi(ref settings, midi);
        var phrases = song.GetChart(MoonInstrument.Guitar, Difficulty.Expert).specialPhrases;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(phrases, Has.One.Matches<MoonPhrase>(phrase => phrase.type == MoonPhrase.Type.Starpower));
            Assert.That(phrases, Has.None.Matches<MoonPhrase>(phrase => phrase.type == MoonPhrase.Type.Solo));
        }
    }

    [Test]
    public void ProKeysSplitDifficultyTrack_OnlyPopulatesMatchingDifficulty()
    {
        var key = MidIOHelper.PRO_KEYS_RANGE_START;
        var midi = MakeMidi(MakeTrack(MidIOHelper.PRO_KEYS_HARD,
            Note(10, 100, key),
            Note(110, 200, MidIOHelper.PRO_KEYS_GLISSANDO)));

        var song = MidReader.ReadMidi(midi);
        var expert = song.GetChart(MoonInstrument.ProKeys, Difficulty.Expert);
        var hard = song.GetChart(MoonInstrument.ProKeys, Difficulty.Hard);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(expert.notes, Is.Empty);
            Assert.That(expert.specialPhrases, Is.Empty);
            Assert.That(hard.notes, Has.Count.EqualTo(1));
            Assert.That(hard.notes[0].proKeysKey, Is.EqualTo(0));
            Assert.That(hard.specialPhrases, Has.One.Matches<MoonPhrase>(phrase =>
                phrase.type == MoonPhrase.Type.ProKeys_Glissando && phrase.tick == 110 && phrase.length == 90));
        }
    }

    [Test]
    public void GetCodaRanges_ReturnsBalancedRanges()
    {
        var song = new MoonSong((uint) Resolution);
        song.InsertText(new MoonText(MidIOHelper.MIDCODA_START, 50));
        song.InsertText(new MoonText(MidIOHelper.MIDCODA_END, 100));
        song.InsertText(new MoonText(MidIOHelper.MIDCODA_START, 150));
        song.InsertText(new MoonText(MidIOHelper.MIDCODA_END, 250));

        var ranges = MidReader.GetCodaRanges(song);

        Assert.That(ranges, Is.EqualTo(new List<(uint start, uint end)>
        {
            (50, 100),
            (150, 250),
        }));
    }

    [Test]
    public void GetCodaRanges_ReturnsOpenRangeWhenFinalCodaEndIsMissing()
    {
        var song = new MoonSong((uint) Resolution);
        song.InsertText(new MoonText(MidIOHelper.CODA_START, 50));

        var ranges = MidReader.GetCodaRanges(song);

        Assert.That(ranges, Is.EqualTo(new List<(uint start, uint end)>
        {
            (50, uint.MaxValue),
        }));
    }

    private static MidiFile MakeMidi(params TrackChunk[] tracks)
    {
        var midi = new MidiFile(MakeSyncTrack())
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(Resolution),
        };

        foreach (var track in tracks)
        {
            midi.Chunks.Add(track);
        }

        return midi;
    }

    private static TrackChunk MakeSyncTrack()
    {
        return MakeTrack("TEMPO_TRACK",
            new TimedMidiEvent(0, new SetTempoEvent(TempoChange.BpmToMicroSeconds(150))),
            new TimedMidiEvent(0, new TimeSignatureEvent(7, 8)));
    }

    private static TrackChunk MakeEventsTrack(params TimedMidiEvent[] events)
    {
        return MakeTrack(MidIOHelper.EVENTS_TRACK, events);
    }

    private static TimedMidiEvent Text(long tick, string text)
    {
        return new TimedMidiEvent(tick, new MidiTextEvent(text));
    }

    private static TimedMidiEvent[] Note(long startTick, long endTick, int noteNumber,
        int velocity = MidIOHelper.VELOCITY, int channel = 0)
    {
        return new[]
        {
            new TimedMidiEvent(startTick, new NoteOnEvent
            {
                NoteNumber = S(noteNumber),
                Velocity = S(velocity),
                Channel = F(channel),
            }),
            new TimedMidiEvent(endTick, new NoteOffEvent
            {
                NoteNumber = S(noteNumber),
                Velocity = S(0),
                Channel = F(channel),
            }),
        };
    }

    private static TrackChunk MakeTrack(string trackName, params object[] eventItems)
    {
        return MakeTrack(trackName, FlattenEvents(eventItems));
    }

    private static TrackChunk MakeTrack(string trackName, IEnumerable<TimedMidiEvent> events)
    {
        var ordered = events
            .OrderBy(item => item.Tick)
            .ThenBy(item => EventPriority(item.Event))
            .ThenBy(item => item.Event is NoteEvent note ? (int) note.NoteNumber : 0)
            .ToArray();

        long previousTick = 0;
        foreach (var (tick, midiEvent) in ordered)
        {
            midiEvent.DeltaTime = tick - previousTick;
            previousTick = tick;
        }

        var chunk = new TrackChunk(new SequenceTrackNameEvent(trackName));
        chunk.Events.AddRange(ordered.Select(item => item.Event));
        return chunk;
    }

    private static int EventPriority(MidiEvent midiEvent)
    {
        return midiEvent switch
        {
            NoteOffEvent => 0,
            BaseTextEvent => 1,
            NoteOnEvent => 2,
            _ => 1,
        };
    }

    private static IEnumerable<TimedMidiEvent> FlattenEvents(IEnumerable<object> eventItems)
    {
        foreach (var item in eventItems)
        {
            switch (item)
            {
                case TimedMidiEvent timedEvent:
                    yield return timedEvent;
                    break;
                case IEnumerable<TimedMidiEvent> eventGroup:
                    foreach (var timedEvent in eventGroup)
                    {
                        yield return timedEvent;
                    }
                    break;
                default:
                    throw new ArgumentException($"Unsupported MIDI test event item: {item.GetType()}");
            }
        }
    }
}
