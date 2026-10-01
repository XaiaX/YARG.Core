using System;
using System.IO;
using System.Linq;
using Melanchall.DryWetMidi.Core;
using MoonscraperChartEditor.Song;
using MoonscraperChartEditor.Song.IO;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Parsing;
using MoonDifficulty = MoonscraperChartEditor.Song.MoonSong.Difficulty;
using MoonInstrument = MoonscraperChartEditor.Song.MoonSong.MoonInstrument;
using ElitePad = YARG.Core.Chart.EliteDrumNote.EliteDrumPad;

namespace YARG.Core.UnitTests.Parsing;

[TestFixture]
[Explicit("Local diagnostic only; requires external Blondie Atomic MIDI")]
public sealed class BlondieAtomicMidiDiagnosticTests
{
    private const string PATH = "/Users/xaiax/Local/song_edits/DLC/exported/blondie-atomic__UGC_5001204.mid";

    [Test]
    public void InspectExactMidi()
    {
        Assert.That(File.Exists(PATH), Is.True);
        var midi = MidFileLoader.LoadMidiFile(PATH);
        var tracks = midi.GetTrackChunks().ToArray();
        for (int i = 0; i < tracks.Length; i++)
            TestContext.Progress.WriteLine($"TRACK {i}: {string.Join(" | ", tracks[i].Events.OfType<SequenceTrackNameEvent>().Select(e => e.Text))}");

        foreach (var track in tracks.Where(t => t.Events.OfType<SequenceTrackNameEvent>().Any(e => e.Text == MidIOHelper.ELITE_DRUMS_TRACK)))
        {
            long tick = 0;
            var kicks = track.Events.Select(e => { tick += e.DeltaTime; return (Tick: tick, Event: e); })
                .Where(x => x.Event is NoteOnEvent n && n.Velocity > 0 && (int)n.NoteNumber is 73 or 74)
                .Select(x => (x.Tick, Note: (int)((NoteOnEvent)x.Event).NoteNumber, Channel: (int)((NoteOnEvent)x.Event).Channel))
                .ToArray();
            foreach (var pair in kicks.GroupBy(x => x.Tick).Where(g => g.Any(x => x.Note == 74) && g.Any(x => x.Note == 73)))
                TestContext.Progress.WriteLine($"RAW_PAIR tick={pair.Key} notes={string.Join(",", pair.Select(x => $"{x.Note}:ch{x.Channel}"))}");
            TestContext.Progress.WriteLine($"RAW_KICKS n74={kicks.Count(x => x.Note == 74)} n73={kicks.Count(x => x.Note == 73)} pairTicks={kicks.GroupBy(x => x.Tick).Count(g => g.Any(x => x.Note == 74) && g.Any(x => x.Note == 73))}");
            tick = 0;
            foreach (var (at, ev) in track.Events.Select(e => { tick += e.DeltaTime; return (tick, e); }).Where(x => x.tick >= 144480 && x.tick <= 145440 && x.e is NoteEvent n && (int)n.NoteNumber is 73 or 74))
                TestContext.Progress.WriteLine($"RAW_EDGE tick={at} type={ev.EventType} pitch={((NoteEvent)ev).NoteNumber} vel={((NoteEvent)ev).Velocity}");
        }

        var song = MidReader.ReadMidi(PATH);
        TestContext.Progress.WriteLine($"SONG resolution={song.resolution}");
        var settings = ParseSettings.Default;
        settings.DrumsType = DrumsType.FourLane;
        var loader = new MoonSongLoader(song, settings);
        var elite = loader.LoadEliteDrumsTrack(Instrument.EliteDrums);
        foreach (var diff in new[] { Difficulty.Expert, Difficulty.ExpertPlus })
        {
            var raw = song.GetChart(MoonInstrument.EliteDrums, MoonDifficulty.Expert).notes;
            var all = elite.GetDifficulty(diff).Notes.SelectMany(n => n.ChildNotes.Prepend(n)).ToArray();
            var kicks = all.Where(n => n.Pad == (int)ElitePad.Kick).ToArray();
            TestContext.Progress.WriteLine($"ELITE {diff} moonNotes={raw.Count} roots={elite.GetDifficulty(diff).Notes.Count} allNotes={all.Length} kicks={kicks.Length} 1x={kicks.Count(n => !n.IsDoubleKick)} 2x={kicks.Count(n => n.IsDoubleKick)} flam={all.Count(n => n.IsFlam)}");
            foreach (var pair in kicks.GroupBy(n => n.Tick).Where(g => g.Any(n => n.IsDoubleKick) && g.Any(n => !n.IsDoubleKick)))
                TestContext.Progress.WriteLine($"PARSED_PAIR {diff} tick={pair.Key} time={pair.First().Time:F3} flags={string.Join(",", pair.Select(n => $"pad={n.Pad}:double={n.IsDoubleKick}:flam={n.IsFlam}"))}");
            foreach (var n in kicks.Where(n => n.IsFlam).Take(12))
                TestContext.Progress.WriteLine($"FLAM_KICK {diff} tick={n.Tick} time={n.Time:F3} double={n.IsDoubleKick} flam={n.IsFlam}");
        }
        var expertNotes = elite.GetDifficulty(Difficulty.Expert).Notes.SelectMany(n => n.ChildNotes.Prepend(n)).ToArray();
        var plusNotes = elite.GetDifficulty(Difficulty.ExpertPlus).Notes.SelectMany(n => n.ChildNotes.Prepend(n)).ToArray();
        var expertKickTicks = expertNotes.Where(n => n.Pad == (int)ElitePad.Kick).Select(n => n.Tick).ToHashSet();
        foreach (var kick in plusNotes.Where(n => n.Pad == (int)ElitePad.Kick && !expertKickTicks.Contains(n.Tick)))
            TestContext.Progress.WriteLine($"EXPERT_ABSENT_KICK tick={kick.Tick} time={kick.Time:F3} pad={(ElitePad)kick.Pad} double={kick.IsDoubleKick} flam={kick.IsFlam} rawAtTick={string.Join(",", song.GetChart(MoonInstrument.EliteDrums, MoonDifficulty.Expert).notes.Where(n => n.tick == kick.Tick).Select(n => $"{n.rawNote}:{n.eliteDrumPad}:{n.flags}"))}");
        foreach (var tick in new uint[] { 144960, 145920 })
            TestContext.Progress.WriteLine($"KICK_PAIR_DETAIL tick={tick} time={song.TickToTime(tick):F3} raw={string.Join(",", song.GetChart(MoonInstrument.EliteDrums, MoonDifficulty.Expert).notes.Where(n => n.tick == tick && n.eliteDrumPad == MoonscraperChartEditor.Song.MoonNote.EliteDrumPad.Kick).Select(n => $"raw={n.rawNote}:flags={n.flags}"))} expert={string.Join(",", expertNotes.Where(n => n.Tick == tick && n.Pad == (int)ElitePad.Kick).Select(n => $"double={n.IsDoubleKick}:flam={n.IsFlam}"))} plus={string.Join(",", plusNotes.Where(n => n.Tick == tick && n.Pad == (int)ElitePad.Kick).Select(n => $"double={n.IsDoubleKick}:flam={n.IsFlam}"))}");
        foreach (var flam in plusNotes.Where(n => n.IsFlam))
            TestContext.Progress.WriteLine($"FLAM_NOTE tick={flam.Tick} time={flam.Time:F3} pad={(ElitePad)flam.Pad} double={flam.IsDoubleKick} flam={flam.IsFlam}");
        foreach (var target in new[] { Instrument.ProDrums, Instrument.FourLaneDrums, Instrument.FiveLaneDrums })
        {
            var native = loader.LoadDrumsTrack(target, null);
            var generated = loader.LoadEliteDrumsDownchartTrack(target, elite);
            foreach (var diff in new[] { Difficulty.Expert, Difficulty.ExpertPlus })
            {
                var e = elite.GetDifficulty(diff).Notes.SelectMany(n => n.ChildNotes.Prepend(n)).Where(n => n.Pad == (int)ElitePad.Kick).ToArray();
                var n = native.GetDifficulty(diff).Notes.SelectMany(x => x.ChildNotes.Prepend(x)).ToArray();
                var g = generated.GetDifficulty(diff).Notes.SelectMany(x => x.ChildNotes.Prepend(x)).ToArray();
                TestContext.Progress.WriteLine($"TARGET {target} {diff} nativeRoots={native.GetDifficulty(diff).Notes.Count} nativeNotes={n.Length} nativeKicks={n.Count(x => x.Pad == 0)} generatedRoots={generated.GetDifficulty(diff).Notes.Count} generatedNotes={g.Length} generatedKicks={g.Count(x => x.Pad == 0)} generatedDouble={g.Count(x => x.IsDoubleKick)}");
                if (diff == Difficulty.Expert)
                {
                    foreach (var x in e.GroupBy(x => x.Tick).Where(gr => gr.Any(x => x.IsDoubleKick) && gr.Any(x => !x.IsDoubleKick)))
                    {
                        var final = g.Where(y => y.Tick == x.Key).ToArray();
                        TestContext.Progress.WriteLine($"TARGET_PAIR {target} tick={x.Key} time={x.First().Time:F3} sourceKicks={x.Count()} generatedAtTick={final.Length} generatedKicks={final.Count(y => y.Pad == 0)} generatedFlags={string.Join(",", final.Select(y => $"{y.Pad}:double={y.IsDoubleKick}"))}");
                    }
                }
            }
        }
    }
}
