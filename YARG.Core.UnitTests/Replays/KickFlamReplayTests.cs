// pattern: Imperative Shell
using System;
using System.Collections.Generic;
using System.Linq;
using MoonscraperChartEditor.Song;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Engine.Drums;
using YARG.Core.Engine.Drums.Engines;
using YARG.Core.Game;
using YARG.Core.Input;
using YARG.Core.Parsing;
using YARG.Core.Replays;

namespace YARG.Core.UnitTests.Replays;

public sealed class KickFlamReplayTests
{
    [TestCase(false, false, 1)]
    [TestCase(false, true, 1)]
    [TestCase(true, false, 1)]
    [TestCase(true, true, 1)]
    [TestCase(false, false, 2)]
    [TestCase(false, true, 2)]
    [TestCase(true, false, 2)]
    [TestCase(true, true, 2)]
    public void SerializedResolvedKickFlamsRetainRulesetScoreAndVelocityPolicy(bool eliteOutput, bool bot, int ruleset)
    {
        var song = new MoonSong(480);
        var raw = song.GetChart(MoonSong.MoonInstrument.EliteDrums, MoonSong.Difficulty.Expert);
        for (uint tick = 960; tick <= 2880; tick += 960)
            raw.Add(new MoonNote(tick, (int)MoonNote.EliteDrumPad.Kick, 0, MoonNote.Flags.EliteDrums_Flam | MoonNote.Flags.ProDrums_Accent));
        var loader = new MoonSongLoader(song, ParseSettings.Default);
        var chart = new SongChart(480) { SyncTrack = loader.LoadSyncTrack(), AuthoredDrumSources = loader.LoadAuthoredDrumSources() };
        var output = eliteOutput ? Instrument.EliteDrums : Instrument.ProDrums;
        var state = new ResolvedDrumPlayback(output, DrumSourceFormat.Elite, Difficulty.Expert, Difficulty.Expert,
            DrumExtraKickPolicy.Include, true, ResolvedDrumPlayback.CategoryFor(output, DrumSourceFormat.Elite));
        var profile = new YargProfile(Guid.NewGuid()) { GameMode = GameMode.EliteDrums,
            CurrentInstrument = output, PreferredInstrument = Instrument.EliteDrums, CurrentDifficulty = Difficulty.Expert, IsBot = bot };
        var template = EnginePreset.Default.Drums.Create(NativeEliteReplayFixture.Thresholds, NativeEliteReplayFixture.Thresholds,
            eliteOutput ? DrumsEngineParameters.DrumMode.Elite : DrumsEngineParameters.DrumMode.ProFourLane);
        var parameters = new DrumsEngineParameters(template.HitWindow, 4, NativeEliteReplayFixture.Thresholds,
            NativeEliteReplayFixture.Thresholds, template.Mode, false, true, classicArticulationRuleset: (byte)ruleset);
        parameters.SongSpeed = 1;
        var prepared = DrumPlaybackPreparer.Prepare(chart, state, classicArticulationRuleset: (byte)ruleset);
        var times = prepared.Elite is { } elite ? elite.Notes.Select(n => n.Time) : prepared.Classic!.Notes.Select(n => n.Time);
        var inputs = bot ? Array.Empty<GameInput>() : times.SelectMany(time => ruleset == 2
            ? new[] { GameInput.Create(time, EliteDrumsAction.Kick, .8f), GameInput.Create(time + .02, EliteDrumsAction.Kick, .8f) }
            : new[] { GameInput.Create(time, EliteDrumsAction.Kick, .8f) }).ToArray();
        DrumsStats stats;
        if (eliteOutput)
        {
            var engine = new EliteDrumsEngine(prepared.Elite!, chart.SyncTrack, parameters, bot, true);
            stats = NativeEliteReplayFixture.PlayLive(engine, inputs);
        }
        else
        {
            var engine = new YargDrumsEngine(prepared.Classic!, chart.SyncTrack, parameters, bot, true);
            engine.Update(-2);
            foreach (var recorded in inputs) { var input = recorded; engine.QueueInput(ref input); engine.Update(input.Time + .02); }
            engine.Update(3.5);
            stats = new DrumsStats(engine.EngineStats);
        }
        Assert.That(stats.NoteScore, Is.EqualTo(3 * (eliteOutput ? 50 : 60) * (ruleset == 2 ? 2 : 1)));
        Assert.That(stats.DynamicsBonus, Is.EqualTo(eliteOutput || ruleset == 1 ? 75 : 0));
        Assert.That(stats.Combo, Is.EqualTo(3));
        var data = new ReplayData(new Dictionary<Guid, ColorProfile>(), new Dictionary<Guid, CameraPreset>(),
            new Dictionary<Guid, RockMeterPreset>(), true,
            new[] { new ReplayFrame(profile, parameters, stats, inputs, resolvedDrumPlayback: state) }, Array.Empty<double>());
        var restored = NativeEliteReplayFixture.RoundTrip(data);
        Assert.That(((DrumsEngineParameters)restored.Frames[0].EngineParameters).ClassicArticulationRuleset, Is.EqualTo(ruleset));
        NativeEliteReplayFixture.AssertLiveMatchesReplay(stats, NativeEliteReplayFixture.Analyze(chart, restored));
    }
}
