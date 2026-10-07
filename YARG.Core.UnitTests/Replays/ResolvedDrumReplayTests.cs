using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Engine;
using YARG.Core.Engine.Drums;
using YARG.Core.Engine.Drums.Engines;
using YARG.Core.Game;
using YARG.Core.Input;
using YARG.Core.Replays;

namespace YARG.Core.UnitTests.Replays;

[TestFixture]
public sealed class ResolvedDrumReplayTests
{
    private static ResolvedDrumPlayback State(DrumSourceFormat source, Instrument output,
        DrumExtraKickPolicy policy = DrumExtraKickPolicy.Include, Difficulty tier = Difficulty.Expert) =>
        new(output, source, tier, tier == Difficulty.Beginner ? Difficulty.Easy : tier, policy,
            policy == DrumExtraKickPolicy.Include, ResolvedDrumPlayback.CategoryFor(output, source));

    private static SongChart Chart(DrumSourceFormat source, Difficulty tier = Difficulty.Expert)
    {
        var chart = NativeEliteReplayFixture.Chart();
        var sources = new AuthoredDrumSourceCollection();
        var facts = new DrumSourceTierFacts(source, tier, true, true, false, true, true);
        if (source == DrumSourceFormat.Elite)
        {
            var notes = new InstrumentDifficulty<EliteDrumNote>(Instrument.EliteDrums, tier,
                new List<EliteDrumNote>
                {
                    NativeEliteReplayFixture.EliteNote(EliteDrumNote.EliteDrumPad.Snare, 1),
                    new(EliteDrumNote.EliteDrumPad.Kick, DrumNoteType.Neutral,
                        EliteDrumNote.EliteDrumsHatState.Indifferent, EliteDrumNote.EliteDrumsHatPedalType.Stomp,
                        false, DrumNoteFlags.None, NoteFlags.None, EliteDrumNote.EliteDrumsChannelFlag.None,
                        2, 1920, true),
                    NativeEliteReplayFixture.EliteNote(EliteDrumNote.EliteDrumPad.Snare, 3),
                }, new(), new());
            sources.Add(new AuthoredDrumSourceTier(facts, null, notes, Array.Empty<uint>()));
        }
        else
        {
            var instrument = source == DrumSourceFormat.FiveLane ? Instrument.FiveLaneDrums : Instrument.ProDrums;
            int red = source == DrumSourceFormat.FiveLane ? (int)FiveLaneDrumPad.Red : (int)FourLaneDrumPad.RedDrum;
            var notes = new InstrumentDifficulty<DrumNote>(instrument, tier, new List<DrumNote>
            {
                new(red, DrumNoteType.Neutral, DrumNoteFlags.None, NoteFlags.None, 1, 960),
                new(0, DrumNoteType.Neutral, DrumNoteFlags.None, NoteFlags.None, 2, 1920, true),
                new(red, DrumNoteType.Neutral, DrumNoteFlags.None, NoteFlags.None, 3, 2880),
            }, new(), new());
            sources.Add(new AuthoredDrumSourceTier(facts, notes, null, Array.Empty<uint>()));
        }
        chart.AuthoredDrumSources = sources;
        return chart;
    }

    private static DrumsStats Play(BaseEngine engine, GameInput[] inputs)
    {
        engine.Update(-2);
        foreach (var recorded in inputs)
        {
            var input = recorded;
            engine.QueueInput(ref input);
            engine.Update(input.Time + 0.02);
        }
        engine.Update(3.5);
        return new DrumsStats((DrumsStats)engine.BaseStats);
    }

    private static ReplayData Data(ReplayFrame frame) => new(new Dictionary<Guid, ColorProfile>(),
        new Dictionary<Guid, CameraPreset>(), new Dictionary<Guid, RockMeterPreset>(), true,
        new[] { frame }, Array.Empty<double>());

    [TestCase(DrumSourceFormat.Elite)]
    [TestCase(DrumSourceFormat.FourLane)]
    [TestCase(DrumSourceFormat.FiveLane)]
    public void RecordedSourceAndKickPolicySurviveLiveProfileMutation(DrumSourceFormat source)
    {
        var chart = Chart(source);
        var state = State(source, Instrument.EliteDrums, DrumExtraKickPolicy.Remove);
        var profile = NativeEliteReplayFixture.Profile(Instrument.EliteDrums);
        var parameters = NativeEliteReplayFixture.Parameters();
        var prepared = DrumPlaybackPreparer.Prepare(chart, state,
            n => profile.ApplyModifiers(n, chart.SyncTrack), n => profile.ApplyModifiers(n, chart.SyncTrack)).Elite!;
        prepared.SetDrumActivationFlags(profile.StarPowerActivationType);
        var inputs = new[] { GameInput.Create(1, EliteDrumsAction.EliteSnare, 0.8f) };
        var live = Play(new EliteDrumsEngine(prepared, chart.SyncTrack, parameters, false, true), inputs);
        var frame = new ReplayFrame(profile, parameters, live, inputs, state);
        profile.CurrentInstrument = Instrument.FiveLaneDrums;
        profile.CurrentDifficulty = Difficulty.Beginner;
        profile.AddSingleModifier(Modifier.NoKicks);
        profile.AddSingleModifier(Modifier.Enable2xKicks);
        profile.AddSingleModifier(Modifier.PreferEliteDowncharts);
        profile.ReplayDrumPlayback = State(DrumSourceFormat.Elite, Instrument.FiveLaneDrums);
        // Public historical tracks deliberately disagree with the captured source.
        chart.EliteDrums = NativeEliteReplayFixture.EliteChart().EliteDrums;
        var data = NativeEliteReplayFixture.RoundTrip(Data(frame));
        Assert.Multiple(() =>
        {
            Assert.That(frame.Profile, Is.Not.SameAs(profile));
            Assert.That(frame.Profile.ReplayDrumPlayback, Is.Not.SameAs(state));
            Assert.That(data.Frames[0].Profile.ReplayDrumPlayback.SourceFormat, Is.EqualTo(source));
            Assert.That(live.NotesHit, Is.EqualTo(1));
            Assert.That(live.NotesMissed, Is.EqualTo(1));
            Assert.That(live.TotalNotes, Is.EqualTo(2));
            Assert.That(live.CommittedScore, Is.GreaterThan(0));
            Assert.That(data.GetEliteDrumsDownchartOutputs(), Is.Null);
        });
        NativeEliteReplayFixture.AssertLiveMatchesReplay(live, NativeEliteReplayFixture.Analyze(chart, data));
    }

    [TestCase(Instrument.ProDrums)]
    [TestCase(Instrument.FourLaneDrums)]
    [TestCase(Instrument.FiveLaneDrums)]
    public void RecordedEliteSourceReconstructsClassicOutput(Instrument output)
    {
        var chart = Chart(DrumSourceFormat.Elite);
        var state = State(DrumSourceFormat.Elite, output, DrumExtraKickPolicy.Remove);
        var profile = NativeEliteReplayFixture.Profile(output);
        var parameters = EnginePreset.Default.Drums.Create(NativeEliteReplayFixture.Thresholds,
            NativeEliteReplayFixture.Thresholds, output == Instrument.FiveLaneDrums
                ? DrumsEngineParameters.DrumMode.FiveLane : DrumsEngineParameters.DrumMode.ProFourLane);
        var notes = DrumPlaybackPreparer.Prepare(chart, state,
            n => profile.ApplyModifiers(n, chart.SyncTrack), n => profile.ApplyModifiers(n, chart.SyncTrack)).Classic!;
        notes.SetDrumActivationFlags(profile.StarPowerActivationType);
        var inputs = new[] { GameInput.Create(1, output == Instrument.FiveLaneDrums
            ? EliteDrumsAction.FiveLaneRedDrum : EliteDrumsAction.FourLaneRedDrum, 0.8f) };
        var live = Play(new YargDrumsEngine(notes, chart.SyncTrack, parameters, false, true), inputs);
        var data = NativeEliteReplayFixture.RoundTrip(Data(new ReplayFrame(profile, parameters, live, inputs, state)));
        Assert.That(live.NotesHit, Is.EqualTo(1));
        Assert.That(live.NotesMissed, Is.EqualTo(1));
        NativeEliteReplayFixture.AssertLiveMatchesReplay(live, NativeEliteReplayFixture.Analyze(chart, data));
    }

    [TestCase(DrumSourceFormat.Elite)]
    [TestCase(DrumSourceFormat.FourLane)]
    [TestCase(DrumSourceFormat.FiveLane)]
    public void BeginnerIsDerivedAfterKickRemovalAndModifiers(DrumSourceFormat source)
    {
        var chart = Chart(source, Difficulty.Easy);
        var state = State(source, Instrument.EliteDrums, DrumExtraKickPolicy.NormalizeExtraOnly, Difficulty.Beginner);
        var profile = NativeEliteReplayFixture.Profile(Instrument.EliteDrums);
        profile.CurrentDifficulty = Difficulty.Beginner;
        profile.AddSingleModifier(Modifier.NoKicks);
        var parameters = NativeEliteReplayFixture.Parameters();
        var notes = DrumPlaybackPreparer.Prepare(chart, state,
            n => profile.ApplyModifiers(n, chart.SyncTrack), n => profile.ApplyModifiers(n, chart.SyncTrack)).Elite!;
        notes.SetDrumActivationFlags(profile.StarPowerActivationType);
        var inputs = new[] { GameInput.Create(1, EliteDrumsAction.EliteSnare, 0.8f) };
        var live = Play(new EliteDrumsEngine(notes, chart.SyncTrack, parameters, false, true), inputs);
        var data = NativeEliteReplayFixture.RoundTrip(Data(new ReplayFrame(profile, parameters, live, inputs, state)));
        Assert.That(live.TotalNotes, Is.EqualTo(2));
        Assert.That(live.NotesHit, Is.EqualTo(1));
        NativeEliteReplayFixture.AssertLiveMatchesReplay(live, NativeEliteReplayFixture.Analyze(chart, data));
    }

    [TestCase(false, false, Difficulty.Expert)]
    [TestCase(false, true, Difficulty.Expert)]
    [TestCase(true, false, Difficulty.Expert)]
    [TestCase(true, false, Difficulty.ExpertPlus)]
    [TestCase(true, false, Difficulty.Beginner)]
    public void HistoricalNullStateUsesRecordedTrackAndTier(bool downchart, bool converted, Difficulty tier)
    {
        var chart = NativeEliteReplayFixture.Chart();
        var profile = NativeEliteReplayFixture.Profile(downchart ? Instrument.ProDrums : Instrument.EliteDrums);
        profile.CurrentDifficulty = tier;
        var parameters = NativeEliteReplayFixture.Parameters();
        var inputs = new[] { GameInput.Create(1, downchart ? EliteDrumsAction.FourLaneRedDrum
            : EliteDrumsAction.EliteSnare, 0.8f) };
        BaseEngine engine;
        if (downchart || converted)
        {
            var notes = new InstrumentDifficulty<DrumNote>(Instrument.ProDrums, tier,
                new List<DrumNote>
                {
                    new(FourLaneDrumPad.RedDrum, DrumNoteType.Neutral, DrumNoteFlags.None, NoteFlags.None, 1, 960),
                    new(FourLaneDrumPad.RedDrum, DrumNoteType.Neutral, DrumNoteFlags.None, NoteFlags.None, 3, 2880),
                }, new(), new());
            if (downchart)
            {
                var generated = new InstrumentTrack<DrumNote>(Instrument.ProDrums);
                generated.AddDifficulty(tier, notes);
                chart.EliteDrumsDowncharts = new Dictionary<Instrument, InstrumentTrack<DrumNote>>
                {
                    [Instrument.ProDrums] = generated,
                };
                profile.EliteDrumsDownchartTarget = Instrument.ProDrums;
                // A conflicting native track must not replace an explicit recorded target.
                chart.ProDrums.AddDifficulty(tier, new InstrumentDifficulty<DrumNote>(Instrument.ProDrums, tier));
                engine = new YargDrumsEngine(notes.Clone(), chart.SyncTrack, parameters, false, true);
            }
            else
            {
                chart.ProDrums.AddDifficulty(tier, notes);
                engine = new EliteDrumsEngine(notes.ConvertToEliteDrums(), chart.SyncTrack, parameters, false, true);
            }
        }
        else
        {
            var notes = new InstrumentDifficulty<EliteDrumNote>(Instrument.EliteDrums, tier,
                new List<EliteDrumNote>
                {
                    NativeEliteReplayFixture.EliteNote(EliteDrumNote.EliteDrumPad.Snare, 1),
                    NativeEliteReplayFixture.EliteNote(EliteDrumNote.EliteDrumPad.Snare, 3),
                }, new(), new());
            chart.EliteDrums.AddDifficulty(tier, notes);
            engine = new EliteDrumsEngine(notes.Clone(), chart.SyncTrack, parameters, false, true);
        }
        var live = Play(engine, inputs);
        var data = NativeEliteReplayFixture.RoundTrip(Data(new ReplayFrame(profile, parameters, live, inputs)));
        Assert.That(data.Frames[0].Profile.ReplayDrumPlayback, Is.Null);
        Assert.That(live.NotesHit, Is.EqualTo(1));
        Assert.That(live.NotesMissed, Is.EqualTo(1));
        Assert.That(live.CommittedScore, Is.GreaterThan(0));
        if (downchart) Assert.That(data.GetEliteDrumsDownchartOutputs(), Does.Contain(Instrument.ProDrums));
        NativeEliteReplayFixture.AssertLiveMatchesReplay(live, NativeEliteReplayFixture.Analyze(chart, data));
    }

    [Test]
    public void MissingRecordedSourceCannotUseOtherAuthoredOrHistoricalSources()
    {
        var chart = Chart(DrumSourceFormat.Elite);
        var frame = new ReplayFrame(NativeEliteReplayFixture.Profile(Instrument.EliteDrums),
            NativeEliteReplayFixture.Parameters(), new DrumsStats(), Array.Empty<GameInput>(),
            State(DrumSourceFormat.Elite, Instrument.EliteDrums));
        chart.AuthoredDrumSources = Chart(DrumSourceFormat.FourLane).AuthoredDrumSources;
        chart.EliteDrums = NativeEliteReplayFixture.EliteChart().EliteDrums;
        Assert.Throws<InvalidDataException>(() => NativeEliteReplayFixture.Analyze(chart, Data(frame)));
    }

    [Test]
    public void InMemoryMalformedProfileStateFailsWithoutHistoricalFallback()
    {
        var chart = Chart(DrumSourceFormat.Elite);
        var frame = new ReplayFrame(NativeEliteReplayFixture.Profile(Instrument.EliteDrums),
            NativeEliteReplayFixture.Parameters(), new DrumsStats(), Array.Empty<GameInput>(),
            State(DrumSourceFormat.Elite, Instrument.EliteDrums));
        frame.Profile.CurrentInstrument = Instrument.ProDrums;
        frame.Profile.EliteDrumsDownchartTarget = Instrument.ProDrums;
        Assert.That(Data(frame).GetEliteDrumsDownchartOutputs(), Is.Null);
        Assert.Throws<InvalidDataException>(() => NativeEliteReplayFixture.Analyze(chart, Data(frame)));
    }
}
