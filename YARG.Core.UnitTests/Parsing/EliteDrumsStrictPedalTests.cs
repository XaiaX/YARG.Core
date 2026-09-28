using System.Linq;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using MoonscraperChartEditor.Song.IO;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Parsing;
using static MoonscraperChartEditor.Song.MoonSong;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Core.UnitTests.Parsing;

[TestFixture]
public class EliteDrumsStrictPedalTests
{
    private static SevenBitNumber S(int value) => (SevenBitNumber) (byte) value;

    [Test]
    public void StrictPedal_DoesNotKeepHatClosedAfterPedalNoteOff_AndResetsPerDifficulty()
    {
        const int expertPedal = 72;
        const int expertHat = 76;
        const int hardHat = 52;
        var track = new TrackChunk(new SequenceTrackNameEvent(MidIOHelper.ELITE_DRUMS_TRACK),
            new Melanchall.DryWetMidi.Core.TextEvent($"[{MidIOHelper.STRICT_HAT_PEDAL_STATE}]"),
            On(10, expertPedal), Off(1, expertPedal),
            On(10, expertHat), Off(1, expertHat),
            On(10, expertPedal, MidIOHelper.VELOCITY_ACCENT), Off(1, expertPedal),
            On(10, expertHat), Off(1, expertHat),
            On(10, expertPedal), Off(1, expertPedal),
            On(10, expertHat), Off(1, expertHat),
            On(10, expertPedal, MidIOHelper.VELOCITY_GHOST), Off(1, expertPedal),
            On(10, expertHat), Off(1, expertHat),
            On(10, hardHat), Off(1, hardHat));
        var midi = new MidiFile(new TrackChunk(), track)
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(192)
        };
        var song = MidReader.ReadMidi(midi);
        var loaded = new MoonSongLoader(song, ParseSettings.Default).LoadEliteDrumsTrack(Instrument.EliteDrums);
        var expert = loaded.GetDifficulty(Difficulty.Expert).Notes.SelectMany(note => note.ChildNotes.Prepend(note)).ToArray();
        var hats = expert.Where(note => note.Pad == (int) EliteDrumPad.HiHat).ToArray();
        var pedals = expert.Where(note => note.Pad == (int) EliteDrumPad.HatPedal).ToArray();
        var hard = loaded.GetDifficulty(Difficulty.Hard).Notes.SelectMany(note => note.ChildNotes.Prepend(note))
            .Single(note => note.Pad == (int) EliteDrumPad.HiHat);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(hats.Select(note => note.HatState), Is.EqualTo(new[]
            {
                EliteDrumsHatState.Open, EliteDrumsHatState.Open,
                EliteDrumsHatState.Open, EliteDrumsHatState.Open
            }));
            Assert.That(pedals.Select(note => note.HatPedalType), Is.EqualTo(new[]
            {
                EliteDrumsHatPedalType.Stomp, EliteDrumsHatPedalType.Splash,
                EliteDrumsHatPedalType.Stomp, EliteDrumsHatPedalType.InvisibleTerminator
            }));
            Assert.That(hard.HatState, Is.EqualTo(EliteDrumsHatState.Open));
        }
    }

    private static NoteOnEvent On(long delta, int key, int velocity = MidIOHelper.VELOCITY) =>
        new() { DeltaTime = delta, NoteNumber = S(key), Velocity = S(velocity) };

    private static NoteOffEvent Off(long delta, int key) =>
        new() { DeltaTime = delta, NoteNumber = S(key) };
}
