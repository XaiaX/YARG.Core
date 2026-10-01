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
public class EliteDrumsIndifferentRangeTests
{
    private const int EXPERT_PEDAL = 72;
    private const int EXPERT_HAT = 76;
    private const int EXPERT_MARKER = 88;

    [TestCase(false)]
    [TestCase(true)]
    public void IndifferentRange_CoversMultipleHats_OverridesPedalClosure_AndRetainsPairedPedals(bool strict)
    {
        var notes = Load(strict,
            Note(10, 60, EXPERT_MARKER),
            Note(5, 15, EXPERT_PEDAL),
            Note(0, 1, EXPERT_HAT),
            Note(10, 11, EXPERT_HAT),
            Note(20, 21, EXPERT_HAT), Note(20, 21, EXPERT_PEDAL),
            Note(40, 41, EXPERT_HAT), Note(40, 41, EXPERT_PEDAL, MidIOHelper.VELOCITY_ACCENT),
            Note(55, 70, EXPERT_PEDAL), Note(59, 60, EXPERT_HAT),
            Note(60, 61, EXPERT_HAT),
            Note(70, 71, EXPERT_HAT));
        var hats = notes.Where(note => note.Pad == (int) EliteDrumPad.HiHat).ToArray();
        var pedals = notes.Where(note => note.Pad == (int) EliteDrumPad.HatPedal).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(hats.Select(note => note.Tick), Is.EqualTo(new uint[] { 0, 10, 20, 40, 59, 60, 70 }));
            Assert.That(hats.Select(note => note.HatState), Is.EqualTo(new[]
            {
                EliteDrumsHatState.Open,
                EliteDrumsHatState.Indifferent, EliteDrumsHatState.Indifferent,
                EliteDrumsHatState.Indifferent, EliteDrumsHatState.Indifferent,
                EliteDrumsHatState.Closed, EliteDrumsHatState.Open,
            }));
            Assert.That(pedals.Select(note => note.Tick), Is.EqualTo(new uint[] { 5, 20, 40, 55 }));
            Assert.That(pedals.Select(note => note.HatPedalType), Is.EqualTo(new[]
            {
                EliteDrumsHatPedalType.Stomp, EliteDrumsHatPedalType.Stomp,
                EliteDrumsHatPedalType.Splash, EliteDrumsHatPedalType.Stomp,
            }));
        }
    }

    [Test]
    public void OverlappingIndifferentRanges_DoNotCancelHatStateOrSuppressPairedPedals()
    {
        var notes = Load(false,
            Note(10, 50, EXPERT_MARKER), Note(20, 40, 64), // Hard marker overlaps Expert marker
            Note(10, 11, EXPERT_HAT),
            Note(20, 21, EXPERT_HAT), Note(20, 21, EXPERT_PEDAL),
            Note(30, 31, EXPERT_HAT), Note(30, 31, EXPERT_PEDAL, MidIOHelper.VELOCITY_ACCENT),
            Note(40, 41, EXPERT_HAT), Note(50, 51, EXPERT_HAT));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(notes.Where(note => note.Pad == (int) EliteDrumPad.HiHat).Select(note => note.HatState),
                Is.EqualTo(new[]
                {
                    EliteDrumsHatState.Indifferent, EliteDrumsHatState.Indifferent,
                    EliteDrumsHatState.Indifferent, EliteDrumsHatState.Indifferent, EliteDrumsHatState.Open,
                }));
            Assert.That(notes.Where(note => note.Pad == (int) EliteDrumPad.HatPedal).Select(note => note.HatPedalType),
                Is.EqualTo(new[] { EliteDrumsHatPedalType.Stomp, EliteDrumsHatPedalType.Splash }));
        }
    }

    private static EliteDrumNote[] Load(bool strict, params (long Start, long End, int Key, int Velocity)[] notes)
    {
        var events = notes.SelectMany(note => new (long Tick, MidiEvent Event)[]
        {
            (note.Start, new NoteOnEvent { NoteNumber = S(note.Key), Velocity = S(note.Velocity) }),
            (note.End, new NoteOffEvent { NoteNumber = S(note.Key), Velocity = S(0) }),
        });
        if (strict)
            events = events.Prepend((0, (MidiEvent) new MidiTextEvent($"[{MidIOHelper.STRICT_HAT_PEDAL_STATE}]")));
        var track = new TrackChunk(new SequenceTrackNameEvent(MidIOHelper.ELITE_DRUMS_TRACK));
        long previousTick = 0;
        foreach (var (tick, midiEvent) in events.OrderBy(item => item.Tick)
                     .ThenBy(item => item.Event is NoteOffEvent ? 0 : item.Event is MidiTextEvent ? 1 : 2)
                     .ThenBy(item => item.Event is NoteEvent note ? (int) note.NoteNumber : 0))
        {
            midiEvent.DeltaTime = tick - previousTick;
            previousTick = tick;
            track.Events.Add(midiEvent);
        }
        var midi = new MidiFile(new TrackChunk(), track)
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(192)
        };
        var song = MidReader.ReadMidi(midi);
        var loaded = new MoonSongLoader(song, ParseSettings.Default).LoadEliteDrumsTrack(Instrument.EliteDrums);
        return loaded.GetDifficulty(Difficulty.Expert).Notes.SelectMany(note => note.ChildNotes.Prepend(note)).ToArray();
    }

    private static (long Start, long End, int Key, int Velocity) Note(long start, long end, int key,
        int velocity = MidIOHelper.VELOCITY) => (start, end, key, velocity);
    private static SevenBitNumber S(int value) => (SevenBitNumber) (byte) value;
}
