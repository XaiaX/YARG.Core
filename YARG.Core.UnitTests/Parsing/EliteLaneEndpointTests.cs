using System;
using System.Linq;
using Melanchall.DryWetMidi.Core;
using YARG.Core.Game;
using YARG.Core.Engine.Drums;
using YARG.Core.Engine.Drums.Engines;
using YARG.Core.Input;
using System.Collections.Generic;
using MoonscraperChartEditor.Song;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Parsing;
using Pad = YARG.Core.Chart.EliteDrumNote.EliteDrumPad;

namespace YARG.Core.UnitTests.Parsing;

public sealed class EliteLaneEndpointTests
{
    [TestCase(Pad.Kick, false)]
    [TestCase(Pad.Kick, true)]
    [TestCase(Pad.Tom1, false)]
    [TestCase(Pad.Tom1, true)]
    [TestCase(Pad.Tom2, false)]
    [TestCase(Pad.Tom2, true)]
    [TestCase(Pad.Tom3, false)]
    [TestCase(Pad.Tom3, true)]
    public void EndpointBelongsToLaneUnlessSamePadLaneStartsThere(Pad pad, bool touching)
    {
        var song = new MoonSong(480);
        var moon = song.GetChart(MoonSong.MoonInstrument.EliteDrums, MoonSong.Difficulty.Expert);
        var type = Enum.Parse<MoonPhrase.Type>($"EliteDrums_{pad}Lane");
        moon.Insert(new MoonPhrase(0, 480, type));
        // An unrelated lane at the boundary must not steal this pad's endpoint.
        moon.Insert(new MoonPhrase(480, 480, MoonPhrase.Type.EliteDrums_SnareLane));
        if (touching) moon.Insert(new MoonPhrase(480, 480, type));
        for (uint tick = 0; tick <= 960; tick += 120) moon.Insert(new MoonNote(tick, (int)pad));
        var settings = ParseSettings.Default;
        settings.DrumsType = DrumsType.FourLane;
        var loader = new MoonSongLoader(song, settings);
        var track = loader.LoadEliteDrumsTrack(Instrument.EliteDrums);
        var chart = track.GetDifficulty(Difficulty.Expert);
        var lanes = chart.EliteDrumNativeAuthoredLaneRecords.Where(r => r.AuthoredPad == pad).ToArray();
        Assert.That(lanes[0].MemberSources.Any(n => n.StartTick == 480), Is.EqualTo(!touching));
        if (touching)
        {
            Assert.That(lanes[1].MemberSources.First().StartTick, Is.EqualTo(480));
            Assert.That(lanes[1].MemberSources.Last().StartTick, Is.EqualTo(960));
        }
        var sliced = lanes.Last().Slice(0, 2000)!;
        Assert.That(sliced.MemberSources.Last(), Is.SameAs(lanes.Last().MemberSources.Last()));
        Assert.That(lanes[0].Slice(0, 480)!.MemberSources.Any(n => n.StartTick == 480), Is.False,
            "Practice windows remain end-exclusive even when a phrase endpoint is inclusive.");
        if (pad == Pad.Kick) return;
        var pro = loader.LoadEliteDrumsDownchartTrack(Instrument.ProDrums, track).GetDifficulty(Difficulty.Expert);
        var records = pro.EliteDrumAuthoredLanePhraseRecords.Where(r => r.AuthoredStartTick == 0).ToArray();
        Assert.That(records, Has.Length.EqualTo(1));
        Assert.That(records[0].Origins.Any(o => o.Source.StartTick == 480), Is.EqualTo(!touching));
    }
    [TestCase(false)]
    [TestCase(true)]
    public void LocalMidiEndpointsSurvivePlaybackPreparation(bool extraKicks)
    {
        var path = Environment.GetEnvironmentVariable("YARG_ELITE_LANE_ENDPOINT_MIDI");
        if (string.IsNullOrEmpty(path)) Assert.Ignore("Set YARG_ELITE_LANE_ENDPOINT_MIDI to inspect a local chart.");
        var settings = ParseSettings.Default_Midi;
        settings.DrumsType = DrumsType.FourLane;
        var chart = SongChart.FromMidi(in settings, MidiFile.Read(path!));
        var state = new ResolvedDrumPlayback(Instrument.EliteDrums, DrumSourceFormat.Elite,
            Difficulty.Expert, Difficulty.Expert, extraKicks ? DrumExtraKickPolicy.Include : DrumExtraKickPolicy.Remove,
            extraKicks, ResolvedDrumPlayback.CategoryFor(Instrument.EliteDrums, DrumSourceFormat.Elite));
        var prepared = DrumPlaybackPreparer.Prepare(chart, state).Elite!;
        var notes = prepared.Notes.SelectMany(n => n.ChildNotes.Prepend(n)).ToArray();
        var records = prepared.EliteDrumNativeAuthoredLaneRecords;
        foreach (var record in records)
        {
            bool touching = records.Any(next => next.AuthoredPad == record.AuthoredPad &&
                next.StartTick == record.EndTick && next.StartTick > record.StartTick);
            var expected = notes.Where(n => n.Pad == (int)record.AuthoredPad && n.Tick >= record.StartTick &&
                (n.Tick < record.EndTick || !touching && n.Tick == record.EndTick))
                .Select(n => n.SourceDefinition).Distinct().ToArray();
            Assert.That(record.MemberSources, Is.EquivalentTo(expected));
            if (record.AuthoredPad is Pad.Kick or Pad.Tom1 or Pad.Tom2 or Pad.Tom3)
                TestContext.Out.WriteLine($"extraKicks={extraKicks} pad={record.AuthoredPad} range={record.StartTick}..{record.EndTick} touching={touching} members={expected.Length} last={expected.LastOrDefault()?.StartTick}");
        }
        Assert.That(notes.Any(n => n.Pad == (int)Pad.Kick && n.IsDoubleKick), Is.EqualTo(extraKicks));
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void LocalMidiKickTremoloHasIndependentCadence(bool extraKicks, bool stopKicks)
    {
        var path = Environment.GetEnvironmentVariable("YARG_ELITE_LANE_ENDPOINT_MIDI");
        if (string.IsNullOrEmpty(path)) Assert.Ignore("Set YARG_ELITE_LANE_ENDPOINT_MIDI to inspect a local chart.");
        var settings = ParseSettings.Default_Midi;
        settings.DrumsType = DrumsType.FourLane;
        var song = SongChart.FromMidi(in settings, MidiFile.Read(path!));
        var state = new ResolvedDrumPlayback(Instrument.EliteDrums, DrumSourceFormat.Elite,
            Difficulty.Expert, Difficulty.Expert, extraKicks ? DrumExtraKickPolicy.Include : DrumExtraKickPolicy.Remove,
            extraKicks, ResolvedDrumPlayback.CategoryFor(Instrument.EliteDrums, DrumSourceFormat.Elite));
        var chart = DrumPlaybackPreparer.Prepare(song, state).Elite!;
        var kickRecords = chart.EliteDrumNativeAuthoredLaneRecords.Where(r => r.AuthoredPad == Pad.Kick).ToArray();
        Assert.That(kickRecords, Has.Length.EqualTo(2), "Raining Blood outro has two touching kick lanes.");
        uint start = kickRecords.Min(r => r.StartTick);
        uint end = kickRecords.Max(r => r.EndTick);
        chart.Notes.RemoveAll(n => n.Tick < start || n.Tick > end);
        DrumPlaybackPreparer.Rebuild(chart);
        var notes = chart.Notes.SelectMany(n => n.ChildNotes.Prepend(n)).ToArray();
        var kicks = notes.Where(n => n.Pad == (int)Pad.Kick).ToArray();
        Assert.That(kicks.Any(n => n.IsDoubleKick), Is.EqualTo(extraKicks));
        var parameters = EnginePreset.Default.Drums.Create([1f, 2f, 3f, 4f, 5f, 6f],
            [1f, 2f, 3f, 4f, 5f, 6f], DrumsEngineParameters.DrumMode.ProFourLane);
        var engine = new EliteDrumsEngine(chart, song.SyncTrack, parameters, false, true);
        var strikes = new List<(double time, EliteDrumsAction action)>();
        foreach (var note in notes.Where(n => n.Pad != (int)Pad.Kick))
        {
            var action = (Pad)note.Pad switch
            {
                Pad.Snare => EliteDrumsAction.EliteSnare,
                Pad.HiHat => note.IsOpen ? EliteDrumsAction.EliteOpenHiHat : EliteDrumsAction.EliteClosedHiHat,
                Pad.LeftCrash => EliteDrumsAction.EliteLeftCrash,
                Pad.RightCrash => EliteDrumsAction.EliteRightCrash,
                Pad.Ride => EliteDrumsAction.EliteRide,
                Pad.Tom1 => EliteDrumsAction.EliteTom1,
                Pad.Tom2 => EliteDrumsAction.EliteTom2,
                Pad.Tom3 => EliteDrumsAction.EliteTom3,
                _ => throw new InvalidOperationException($"Unexpected outro hand pad: {note.Pad}")
            };
            strikes.Add((note.Time, action));
        }
        foreach (var record in kickRecords)
        {
            var members = kicks.Where(n => record.MemberSources.Contains(n.SourceDefinition!)).ToArray();
            strikes.Add((members[0].Time, EliteDrumsAction.Kick));
            if (stopKicks) continue;
            // Deliberately leave a charted kick unstruck, refreshing outside its
            // ordinary hit window but inside tremolo forgiveness instead.
            int omitted = members.Length / 2;
            foreach (var note in members.Skip(1).Where(n => n != members[omitted]))
                strikes.Add((note.Time, EliteDrumsAction.Kick));
            strikes.Add((members[omitted].Time - 0.14, EliteDrumsAction.Kick));
        }
        foreach (var strike in strikes.OrderBy(s => s.time))
        {
            var input = GameInput.Create(strike.time, strike.action, 0.8f);
            engine.QueueInput(ref input);
        }
        engine.Update(notes.Max(n => n.Time) + 1);
        int misses = kicks.Count(n => n.WasMissed);
        TestContext.Out.WriteLine($"Raining Blood extraKicks={extraKicks} stopKicks={stopKicks}: " +
            $"kick hits={kicks.Count(n => n.WasHit)}/{kicks.Length}, hand misses={notes.Count(n => n.Pad != (int)Pad.Kick && n.WasMissed)}, overhits={engine.EngineStats.Overhits}");
        Assert.That(misses, stopKicks ? Is.GreaterThan(0) : Is.Zero);
        Assert.That(notes.Where(n => n.Pad != (int)Pad.Kick).All(n => n.WasHit), Is.True);
        Assert.That(engine.EngineStats.Overhits, Is.Zero);
    }

}
