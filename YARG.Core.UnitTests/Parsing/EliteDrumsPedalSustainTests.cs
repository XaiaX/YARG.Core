using System;
using System.Linq;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using MoonscraperChartEditor.Song.IO;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Parsing;
using static YARG.Core.Chart.EliteDrumNote;
using MidiTextEvent = Melanchall.DryWetMidi.Core.TextEvent;

namespace YARG.Core.UnitTests.Parsing;

[TestFixture]
public class EliteDrumsPedalSustainTests
{
    private const int EXPERT_PEDAL = 72;
    private const int EXPERT_HAT = 76;

    [Test]
    public void StrictPedal_ClosesHatOnlyWhileHeld_ThenUsesAuthoredOpenState()
    {
        var track = MakeTrack(strict: true,
            Note(30, 60, EXPERT_PEDAL),
            Note(0, 1, EXPERT_HAT),
            Note(40, 41, EXPERT_HAT),
            Note(60, 61, EXPERT_HAT),
            Note(90, 91, EXPERT_HAT));

        var notes = Load(track);
        var hats = notes.Where(note => note.Pad == (int) EliteDrumPad.HiHat).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(hats.Select(note => note.Tick), Is.EqualTo(new uint[] { 0, 40, 60, 90 }));
            Assert.That(hats.Select(note => note.HatState), Is.EqualTo(new[]
            {
                EliteDrumsHatState.Open,
                EliteDrumsHatState.Closed,
                EliteDrumsHatState.Open,
                EliteDrumsHatState.Open,
            }));
        }
    }

    [Test]
    public void NonStrictPedalChordedWithHat_SuppressesStompAndClosesOnlyDuringSustain()
    {
        var track = MakeTrack(strict: false,
            Note(10, 20, EXPERT_PEDAL),
            Note(10, 11, EXPERT_HAT),
            Note(15, 16, EXPERT_HAT),
            Note(20, 21, EXPERT_HAT),
            Note(25, 26, EXPERT_HAT));

        var notes = Load(track);
        var pedal = notes.Single(note => note.Pad == (int) EliteDrumPad.HatPedal);
        var hats = notes.Where(note => note.Pad == (int) EliteDrumPad.HiHat).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pedal.HatPedalType, Is.EqualTo(EliteDrumsHatPedalType.InvisibleTerminator));
            Assert.That(hats.Select(note => note.HatState), Is.EqualTo(new[]
            {
                EliteDrumsHatState.Closed,
                EliteDrumsHatState.Closed,
                EliteDrumsHatState.Open,
                EliteDrumsHatState.Open,
            }));
        }
    }

    [Test]
    public void NonStrictPedal_ClosesOnlyOriginDifficultyAndUsesHalfOpenRange()
    {
        var track = MakeTrack(strict: false,
            Note(10, 30, 0), // Easy pedal
            Note(10, 11, 4), // Easy hat: pedal start is inclusive
            Note(20, 21, 28), // Medium hat: overlapping pedal from Easy must not leak
            Note(20, 21, 4), // Easy hat: inside pedal interval
            Note(30, 31, 4), // Easy hat: pedal end is exclusive
            Note(15, 16, 76)); // Expert hat: unrelated tier

        var midi = new MidiFile(new TrackChunk(), track)
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(192)
        };
        var song = MidReader.ReadMidi(midi);
        var loaded = new MoonSongLoader(song, ParseSettings.Default).LoadEliteDrumsTrack(Instrument.EliteDrums);
        var easyHats = Notes(loaded, Difficulty.Easy).Where(note => note.Pad == (int) EliteDrumPad.HiHat).ToArray();
        var mediumHat = Notes(loaded, Difficulty.Medium).Single(note => note.Pad == (int) EliteDrumPad.HiHat);
        var expertHat = Notes(loaded, Difficulty.Expert).Single(note => note.Pad == (int) EliteDrumPad.HiHat);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(easyHats.Select(note => note.HatState), Is.EqualTo(new[]
            {
                EliteDrumsHatState.Closed,
                EliteDrumsHatState.Closed,
                EliteDrumsHatState.Open,
            }));
            Assert.That(mediumHat.HatState, Is.EqualTo(EliteDrumsHatState.Open));
            Assert.That(expertHat.HatState, Is.EqualTo(EliteDrumsHatState.Open));
        }
    }

    private static EliteDrumNote[] Notes(InstrumentTrack<EliteDrumNote> track, Difficulty difficulty) =>
        track.GetDifficulty(difficulty).Notes.SelectMany(note => note.ChildNotes.Prepend(note)).ToArray();

    private static EliteDrumNote[] Load(TrackChunk track)
    {
        var midi = new MidiFile(new TrackChunk(), track)
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(192)
        };
        var song = MidReader.ReadMidi(midi);
        var loaded = new MoonSongLoader(song, ParseSettings.Default).LoadEliteDrumsTrack(Instrument.EliteDrums);
        return loaded.GetDifficulty(Difficulty.Expert).Notes.SelectMany(note => note.ChildNotes.Prepend(note)).ToArray();
    }

    private static TrackChunk MakeTrack(bool strict, params (long Start, long End, int Key)[] notes)
    {
        var events = notes.SelectMany(note => new (long Tick, MidiEvent Event)[]
        {
            (note.Start, new NoteOnEvent { NoteNumber = S(note.Key), Velocity = S(MidIOHelper.VELOCITY) }),
            (note.End, new NoteOffEvent { NoteNumber = S(note.Key), Velocity = S(0) }),
        });
        if (strict)
            events = events.Prepend((0, (MidiEvent) new MidiTextEvent($"[{MidIOHelper.STRICT_HAT_PEDAL_STATE}]")));

        var ordered = events.OrderBy(item => item.Tick)
            .ThenBy(item => item.Event is NoteOffEvent ? 0 : item.Event is MidiTextEvent ? 1 : 2)
            .ThenBy(item => item.Event is NoteEvent note ? (int) note.NoteNumber : 0)
            .ToArray();
        var track = new TrackChunk(new SequenceTrackNameEvent(MidIOHelper.ELITE_DRUMS_TRACK));
        long previousTick = 0;
        foreach (var (tick, midiEvent) in ordered)
        {
            midiEvent.DeltaTime = tick - previousTick;
            previousTick = tick;
            track.Events.Add(midiEvent);
        }

        return track;
    }

    private static (long Start, long End, int Key) Note(long start, long end, int key) => (start, end, key);
    private static SevenBitNumber S(int value) => (SevenBitNumber) (byte) value;
}
