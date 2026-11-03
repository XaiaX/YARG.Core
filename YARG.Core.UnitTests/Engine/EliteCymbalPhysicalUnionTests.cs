// pattern: Functional Core
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Engine.Drums;
using YARG.Core.Engine.Drums.Engines;
using YARG.Core.Game;
using YARG.Core.Input;
using YARG.Core.UnitTests.Replays;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Core.UnitTests.Engine;

public sealed class EliteCymbalPhysicalUnionTests
{
    private static EliteDrumNote[] Stream(string id, EliteDrumPad pad, params int[] offsets) =>
        offsets.Select((offset, index) =>
        {
            uint tick = (uint)(1000 + offset);
            double time = 1 + offset / 1000.0;
            var source = new EliteDrumSourceDefinition(id, index, (int)pad, tick, 0, time);
            return new EliteDrumNote(pad, DrumNoteType.Neutral, EliteDrumsHatState.Indifferent,
                EliteDrumsHatPedalType.Stomp, false, DrumNoteFlags.None, NoteFlags.None,
                EliteDrumsChannelFlag.None, time, tick, false, source);
        }).ToArray();

    private static InstrumentDifficulty<EliteDrumNote> Chart(EliteDrumNote[][] streams, params EliteDrumNote[] ordinary)
    {
        var chart = EliteCymbalMergeTests.Chart();
        foreach (var onset in streams.SelectMany(stream => stream).Concat(ordinary).OrderBy(note => note.Time).GroupBy(note => note.Tick))
        {
            var parent = onset.First();
            foreach (var note in onset.Skip(1)) parent.AddChildNote(note);
            chart.Notes.Add(parent);
        }
        chart.SetEliteDrumNativeAuthoredLaneRecords(streams.Select((stream, ordinal) =>
            new EliteDrumNativeAuthoredLaneRecord(ordinal, stream[0].Pad switch
            {
                (int)EliteDrumPad.Snare => PhraseType.EliteDrums_SnareLane,
                (int)EliteDrumPad.LeftCrash => PhraseType.EliteDrums_LeftCrashLane,
                (int)EliteDrumPad.Ride => PhraseType.EliteDrums_RideLane,
                _ => PhraseType.EliteDrums_HiHatLane
            }, stream[0].Tick, stream[^1].Tick, stream.Select(note => note.SourceDefinition!))));
        return chart;
    }

    private static IEnumerable<EliteDrumNote> Gems(InstrumentDifficulty<EliteDrumNote> chart) =>
        chart.Notes.SelectMany(note => note.ChildNotes.Prepend(note));

    private static EliteDrumsEngine Engine(InstrumentDifficulty<EliteDrumNote> chart, byte policy, bool merged = true)
    {
        float[] thresholds = { 1, 2, 3, 4, 5, 6 };
        var template = EnginePreset.Default.Drums.Create(thresholds, thresholds, DrumsEngineParameters.DrumMode.Elite);
        var parameters = new DrumsEngineParameters(template.HitWindow, template.MaxMultiplier,
            template.StarMultiplierThresholds, template.SoloBonusStarMultiplierThresholds, template.Mode,
            template.NoStarPowerOverlap, template.EnableLanes, eliteCymbalMergePolicy: policy);
        var sync = new SyncTrack(480);
        sync.Tempos.Add(new TempoChange(120, 0, 0));
        return new EliteDrumsEngine(chart, sync, parameters, false, true,
            cymbalLayout: merged ? EliteCymbalMergeTests.Layout("left-crash+ride") : null);
    }

    [TestCase(1, 3)]
    [TestCase(2, 6)]
    public void OverlappingPhysicalRollsShareOneEntranceOnlyUnderCurrentPolicy(int policy, int hits)
    {
        var chart = Chart(new[] { Stream("lc", EliteDrumPad.LeftCrash, 0, 80, 160),
            Stream("ride", EliteDrumPad.Ride, 40, 120, 150) });
        var engine = Engine(chart, (byte)policy);
        EliteCymbalMergeTests.Press(engine, 1, EliteDrumsAction.EliteLeftCrash);
        engine.Update(1.4);
        Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(hits));
    }

    [Test]
    public void DuplicateRollDestinationCollapsesWithoutRemovingDistinctExpandedPrimary()
    {
        var chart = Chart(new[] { Stream("lc", EliteDrumPad.LeftCrash, 0, 80, 160),
            Stream("ride", EliteDrumPad.Ride, 40, 80, 200),
            Stream("hat", EliteDrumPad.HiHat, 20, 80, 180) });
        EliteDrumsCymbalProjection.Apply(chart, EliteCymbalMergeTests.Layout("left-crash+ride"));
        Assert.That(Gems(chart).Count(), Is.EqualTo(8), "Only the coincident physical hat roll member is redundant.");
        Assert.That(chart.Notes.Single(note => note.Tick == 1080).ChildNotes.Prepend(chart.Notes.Single(note => note.Tick == 1080))
            .Select(note => note.Pad), Is.EquivalentTo(new[] { (int)EliteDrumPad.HiHat, (int)EliteDrumPad.Ride }));
    }

    [Test]
    public void OrdinaryDestinationCollisionAndPolicyOneKeepTheirIndependentObligations()
    {
        var chart = Chart(new[] { Stream("lc", EliteDrumPad.LeftCrash, 0, 80, 160),
            Stream("ride", EliteDrumPad.Ride, 40, 80, 200),
            Stream("hat", EliteDrumPad.HiHat, 20, 80, 180) });
        var legacy = chart.Clone();
        EliteDrumsCymbalProjection.Apply(legacy, EliteCymbalMergeTests.Layout("left-crash+ride"), 1);
        Assert.That(Gems(legacy).Count(), Is.EqualTo(9));
        var ordinary = chart.Notes.Single(note => note.Tick == 1080).ChildNotes.Single(note => note.Pad == (int)EliteDrumPad.HiHat);
        var mixed = Chart(new[] { Stream("lc", EliteDrumPad.LeftCrash, 0, 80, 160),
            Stream("ride", EliteDrumPad.Ride, 40, 80, 200) }, ordinary.CloneWithoutChildNotes());
        EliteDrumsCymbalProjection.Apply(mixed, EliteCymbalMergeTests.Layout("left-crash+ride"));
        Assert.That(Gems(mixed).Count(), Is.EqualTo(7));
    }
    [TestCase(1, 9)]
    [TestCase(2, 8)]
    public void RecordedUnionPolicyRetainsItsOwnScoringDenominator(int policy, int expected)
    {
        var raw = Chart(new[] { Stream("lc", EliteDrumPad.LeftCrash, 0, 80, 160),
            Stream("ride", EliteDrumPad.Ride, 40, 80, 200),
            Stream("hat", EliteDrumPad.HiHat, 20, 80, 180) });
        var song = NativeEliteReplayFixture.Chart();
        song.EliteDrums.AddDifficulty(Difficulty.Expert, raw);
        var template = NativeEliteReplayFixture.Parameters();
        var parameters = new DrumsEngineParameters(template.HitWindow, template.MaxMultiplier,
            template.StarMultiplierThresholds, template.SoloBonusStarMultiplierThresholds, template.Mode,
            template.NoStarPowerOverlap, template.EnableLanes, eliteCymbalMergePolicy: (byte)policy);
        parameters.SongSpeed = 1;
        var layout = EliteCymbalMergeTests.Layout("left-crash+ride");
        var profile = NativeEliteReplayFixture.Profile(Instrument.EliteDrums);
        profile.IsBot = true;
        profile.ReplayEliteDrumsLayout = layout;
        var engine = new EliteDrumsEngine(raw.Clone(), song.SyncTrack, parameters, true, true, cymbalLayout: layout);
        var live = NativeEliteReplayFixture.PlayLive(engine, Array.Empty<GameInput>());
        Assert.That(live.NotesHit, Is.EqualTo(expected));
        var replay = NativeEliteReplayFixture.RoundTrip(NativeEliteReplayFixture.Data(profile, parameters, live, Array.Empty<GameInput>()));
        NativeEliteReplayFixture.AssertLiveMatchesReplay(live, NativeEliteReplayFixture.Analyze(song, replay));
        Assert.That(Gems(raw).Count(), Is.EqualTo(9), "Player-local projection leaves the authored difficulty intact.");
    }

    private static IReadOnlyList<EliteDrumsPlayableLaneSegment> Physical(InstrumentDifficulty<EliteDrumNote> chart)
    {
        var method = typeof(EliteDrumsPlayableLaneSegments).GetMethod("ResolvePhysical");
        Assert.That(method, Is.Not.Null, "Engine and renderer need one shared physical union.");
        return (IReadOnlyList<EliteDrumsPlayableLaneSegment>)method!.Invoke(null, new object[] { chart })!;
    }

    private static IReadOnlyList<EliteDrumSourceDefinition> Sources(EliteDrumNote note)
    {
        var property = typeof(EliteDrumNote).GetProperty("SourceDefinitions");
        Assert.That(property, Is.Not.Null, "Collapsed physical notes retain all immutable authored source aliases.");
        return (IReadOnlyList<EliteDrumSourceDefinition>)property!.GetValue(note)!;
    }

    [TestCase(200, 1)]
    [TestCase(160, 1)]
    [TestCase(159, 2)]
    public void PhysicalUnionJoinsOverlappingOrTouchingIntervalsButKeepsGaps(int firstEnd, int count)
    {
        var first = Stream("first", EliteDrumPad.LeftCrash, 0, firstEnd).Select(note =>
            new EliteDrumNote(EliteDrumPad.Ride, note.Dynamics, note.HatState, note.HatPedalType, false,
                note.DrumFlags, note.Flags, note.ChannelFlag, note.Time, note.Tick, false,
                note.SourceDefinition, originalPad: (int)EliteDrumPad.LeftCrash)).ToArray();
        var second = Stream("second", EliteDrumPad.Ride, 160, 240);
        var chart = EliteCymbalMergeTests.Chart(first.Concat(second).OrderBy(note => note.Time).ToArray());
        chart.SetEliteDrumNativeAuthoredLaneRecords(new[] {
            new EliteDrumNativeAuthoredLaneRecord(0, PhraseType.EliteDrums_LeftCrashLane,
                first[0].Tick, first[^1].Tick, first.Select(note => note.SourceDefinition!)),
            new EliteDrumNativeAuthoredLaneRecord(1, PhraseType.EliteDrums_RideLane,
                second[0].Tick, second[^1].Tick, second.Select(note => note.SourceDefinition!)) });
        var segments = Physical(chart);
        Assert.That(segments, Has.Count.EqualTo(count));
        Assert.That(segments.All(segment => segment.Pad == (int)EliteDrumPad.Ride), Is.True);
        Assert.That(segments.SelectMany(segment => segment.Members).Count(), Is.EqualTo(firstEnd == 160 ? 3 : 4));
    }

    [Test]
    public void CollapsedMemberPrefersAuthoredDestinationAndAliasesSurviveClonePracticeAndReprojection()
    {
        var chart = Chart(new[] { Stream("lc", EliteDrumPad.LeftCrash, 0, 80, 160),
            Stream("ride", EliteDrumPad.Ride, 40, 80, 200),
            Stream("hat", EliteDrumPad.HiHat, 20, 80, 180) });
        // Assert the metadata contract before projection, so a missing alias mechanism fails directly.
        Assert.That(Sources(chart.Notes[0]).Count, Is.EqualTo(1));
        var expected = chart.EliteDrumNativeAuthoredLaneRecords.SelectMany(record => record.MemberSources).ToArray();
        var layout = EliteCymbalMergeTests.Layout("left-crash+ride");
        EliteDrumsCymbalProjection.Apply(chart, layout);
        var hat = Gems(chart).Single(note => note.Tick == 1080 && note.Pad == (int)EliteDrumPad.HiHat);
        Assert.That(hat.OriginalPad, Is.EqualTo((int)EliteDrumPad.HiHat));
        Assert.That(hat.SourceDefinition!.SourceId, Is.EqualTo("hat"));
        Assert.That(Sources(hat).Select(source => source.SourceId), Is.EquivalentTo(new[] { "hat", "lc" }));
        Assert.That(Gems(chart).SelectMany(Sources), Is.EquivalentTo(expected));
        var clone = chart.Clone();
        EliteDrumsCymbalProjection.Apply(clone, layout);
        Assert.That(Gems(clone).Count(), Is.EqualTo(8));
        Assert.That(Gems(clone).SelectMany(Sources), Is.EquivalentTo(expected));
        var practice = new InstrumentDifficulty<EliteDrumNote>(chart.Instrument, chart.Difficulty,
            clone.Notes.Where(note => note.Tick >= 1080 && note.Tick < 1200).ToList(), new(), new());
        practice.SetEliteDrumNativeAuthoredLaneRecords(chart.SliceEliteDrumNativeAuthoredLaneRecords(1080, 1200));
        Assert.That(Physical(practice), Is.Not.Empty);
        Assert.That(Gems(practice).SelectMany(Sources), Is.EquivalentTo(expected.Where(source => source.StartTick >= 1080 && source.StartTick < 1200)));
    }

    [TestCase(EliteDrumPad.Ride, EliteDrumsAction.EliteRide, false)]
    [TestCase(EliteDrumPad.Snare, EliteDrumsAction.EliteSnare, false)]
    [TestCase(EliteDrumPad.Snare, EliteDrumsAction.EliteSnare, true)]
    public void UnprojectedRollsRetainIndependentEntryEvenUnderCurrentPolicy(EliteDrumPad pad, EliteDrumsAction action, bool merged)
    {
        var chart = Chart(new[] { Stream("first", pad, 0, 80, 160), Stream("second", pad, 40, 120, 150) });
        var engine = Engine(chart, EliteDrumsCymbalProjection.CURRENT_POLICY, merged);
        EliteCymbalMergeTests.Press(engine, 1, action);
        engine.Update(1.4);
        Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(3));
        Assert.That(Physical(chart), Has.Count.EqualTo(2));
    }

}
