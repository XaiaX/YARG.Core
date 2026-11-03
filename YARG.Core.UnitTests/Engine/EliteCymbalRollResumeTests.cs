// pattern: Imperative Shell
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Engine.Drums.Engines;
using YARG.Core.Input;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Core.UnitTests.Engine;

public sealed class EliteCymbalRollResumeTests
{
    private static EliteDrumNote[] Stream(string id, EliteDrumPad pad, params double[] times) =>
        times.Select((time, ordinal) => new EliteDrumNote(pad, DrumNoteType.Neutral,
            EliteDrumsHatState.Indifferent, EliteDrumsHatPedalType.Stomp, false, DrumNoteFlags.None,
            NoteFlags.None, EliteDrumsChannelFlag.None, time, (uint)(time * 960), false,
            new EliteDrumSourceDefinition(id, ordinal, (int)pad, (uint)(time * 960), 0, time))).ToArray();

    private static InstrumentDifficulty<EliteDrumNote> Chart(EliteDrumNote[][] streams,
        params EliteDrumNote[] ordinary)
    {
        var chart = EliteCymbalMergeTests.Chart();
        foreach (var onset in streams.SelectMany(s => s).Concat(ordinary).OrderBy(n => n.Time).GroupBy(n => n.Tick))
        {
            var parent = onset.First();
            foreach (var child in onset.Skip(1)) parent.AddChildNote(child);
            chart.Notes.Add(parent);
        }
        chart.SetEliteDrumNativeAuthoredLaneRecords(streams.Select((stream, i) =>
            new EliteDrumNativeAuthoredLaneRecord(i, stream[0].Pad switch
            {
                (int)EliteDrumPad.LeftCrash => PhraseType.EliteDrums_LeftCrashLane,
                (int)EliteDrumPad.Ride => PhraseType.EliteDrums_RideLane,
                (int)EliteDrumPad.RightCrash => PhraseType.EliteDrums_RightCrashLane,
                _ => PhraseType.EliteDrums_HiHatLane
            }, stream[0].Tick, stream[^1].Tick, stream.Select(n => n.SourceDefinition!))));
        return chart;
    }

    private static IEnumerable<EliteDrumNote> Gems(InstrumentDifficulty<EliteDrumNote> chart) =>
        chart.Notes.SelectMany(n => n.ChildNotes.Prepend(n));
    private static EliteDrumNote Member(InstrumentDifficulty<EliteDrumNote> chart, string id, int ordinal) =>
        Gems(chart).Single(n => n.SourceDefinition is { } source && source.SourceId == id && source.PhysicalOrdinal == ordinal);
    private static EliteDrumsEngine Engine(InstrumentDifficulty<EliteDrumNote> chart, string pair = "left-crash+ride", bool bot = false,
        byte policy = EliteDrumsCymbalProjection.CURRENT_POLICY) =>
        EliteCymbalMergeTests.Engine(chart, EliteCymbalMergeTests.Layout(pair), bot, policy);
    private static void Hit(EliteDrumsEngine engine, double time, EliteDrumsAction action) => EliteCymbalMergeTests.Press(engine, time, action);

    [TestCase("left-crash+ride")]
    [TestCase("left-crash+right-crash")]
    [TestCase("ride+right-crash")]
    public void InterleavedThreeSourceRollsKeepTheirMembersAndEndpointsForResetAndPractice(string pair)
    {
        var chart = Chart(new[] {
            Stream("lc", EliteDrumPad.LeftCrash, 1, 1.12, 1.24, 1.36),
            Stream("ride", EliteDrumPad.Ride, 1.04, 1.16, 1.28, 1.4),
            Stream("rc", EliteDrumPad.RightCrash, 1.08, 1.2, 1.32, 1.44) });
        var records = chart.EliteDrumNativeAuthoredLaneRecords.ToArray();
        var bot = Engine(chart, pair, true);
        Assert.That(Gems(chart).Count(), Is.EqualTo(12));
        Assert.That(Gems(chart).Any(n => n.IsCymbalExpanded), Is.False);
        Assert.That(Gems(chart).Select(n => n.Pad).Distinct().Count(), Is.EqualTo(2));
        foreach (var note in chart.Notes) bot.Update(note.Time);
        bot.Update(2);
        Assert.That(bot.EngineStats.NotesHit, Is.EqualTo(12));
        Assert.That(records.Select(r => r.MemberSources.Count), Is.EqualTo(new[] { 4, 4, 4 }));
        bot.Reset();
        foreach (var note in chart.Notes) bot.Update(note.Time);
        bot.Update(2);
        Assert.That(bot.EngineStats.NotesHit, Is.EqualTo(12));
        var practice = new InstrumentDifficulty<EliteDrumNote>(chart.Instrument, chart.Difficulty,
            chart.Notes.Where(n => n.Tick >= 1100).ToList(), new(), new());
        practice.SetEliteDrumNativeAuthoredLaneRecords(chart.SliceEliteDrumNativeAuthoredLaneRecords(1100, 1600));
        var practiceBot = Engine(practice, pair, true);
        foreach (var note in practice.Notes) practiceBot.Update(note.Time);
        practiceBot.Update(2);
        Assert.That(practiceBot.EngineStats.NotesHit, Is.EqualTo(Gems(practice).Count()));
    }

    [TestCase("left-crash+ride")]
    [TestCase("left-crash+right-crash")]
    [TestCase("ride+right-crash")]
    public void HumanInterleavedRollsContinueWithTwoPhysicalPadsThenMissAfterSilence(string pair)
    {
        var chart = Chart(new[] {
            Stream("lc", EliteDrumPad.LeftCrash, Enumerable.Range(0, 8).Select(i => 1 + i * .12).ToArray()),
            Stream("ride", EliteDrumPad.Ride, Enumerable.Range(0, 8).Select(i => 1.04 + i * .12).ToArray()),
            Stream("rc", EliteDrumPad.RightCrash, Enumerable.Range(0, 8).Select(i => 1.08 + i * .12).ToArray()) });
        var endpoints = chart.EliteDrumNativeAuthoredLaneRecords.Select(r => (r.StartTick, r.EndTick)).ToArray();
        var engine = Engine(chart, pair);
        Hit(engine, 1, EliteDrumsAction.EliteLeftCrash);
        Hit(engine, 1.04, EliteDrumsAction.EliteRide);
        Hit(engine, 1.08, EliteDrumsAction.EliteRightCrash);
        YARG.Core.Game.EliteDrumsCymbalPairs.TryGet(pair, out var definition);
        var keeper = definition!.KeeperSource == "ride" ? EliteDrumsAction.EliteRide : EliteDrumsAction.EliteRightCrash;
        var other = pair == "left-crash+ride" ? EliteDrumsAction.EliteRightCrash
            : pair == "left-crash+right-crash" ? EliteDrumsAction.EliteRide : EliteDrumsAction.EliteLeftCrash;
        Hit(engine, 1.20, keeper);
        Hit(engine, 1.28, other);
        engine.Update(2.3);
        Assert.That(engine.EngineStats.NotesHit, Is.GreaterThan(5), "Entered records retain authored roll leniency beyond the physical strikes.");
        Assert.That(engine.EngineStats.NotesHit, Is.LessThan(24), "Coverage cannot survive indefinite silence.");
        Assert.That(new[] { "lc", "ride", "rc" }.All(id => Member(chart, id, 0).WasHit), Is.True);
        Assert.That(new[] { "lc", "ride", "rc" }.All(id => Member(chart, id, 7).WasMissed), Is.True);
        Assert.That(chart.EliteDrumNativeAuthoredLaneRecords.Select(r => (r.StartTick, r.EndTick)), Is.EqualTo(endpoints));
    }

    [Test]
    public void RecordedUnorderedEachLogicalRecordNeedsItsOwnInitialRealHitEvenOnAnAlreadyRollingKeeper()
    {
        var chart = Chart(new[] { Stream("lc", EliteDrumPad.LeftCrash, 1, 1.08, 1.16),
            Stream("ride", EliteDrumPad.Ride, 1.04, 1.12, 1.2) });
        var engine = Engine(chart, policy: EliteDrumsCymbalProjection.UNORDERED_POLICY);
        Hit(engine, 1, EliteDrumsAction.EliteLeftCrash);
        engine.Update(1.4);
        Assert.That(Enumerable.Range(0, 3).All(i => Member(chart, "lc", i).WasHit), Is.True);
        Assert.That(Enumerable.Range(0, 3).All(i => Member(chart, "ride", i).WasMissed), Is.True);
    }

    [Test]
    public void AlignedMergedRollsRequireBothPhysicalEntriesThenRetainOrdinaryCadence()
    {
        var chart = Chart(new[] { Stream("lc", EliteDrumPad.LeftCrash, 1, 1.06, 1.12),
            Stream("ride", EliteDrumPad.Ride, 1, 1.06, 1.12) });
        var engine = Engine(chart);
        Hit(engine, 1, EliteDrumsAction.EliteRide);
        engine.Update(1.4);
        Assert.That(Enumerable.Range(0, 3).All(i => Member(chart, "lc", i).WasMissed), Is.True);
        Assert.That(Enumerable.Range(0, 3).All(i => Member(chart, "ride", i).WasHit), Is.True);
        engine.Reset();
        Hit(engine, 1, EliteDrumsAction.EliteClosedHiHat);
        Hit(engine, 1, EliteDrumsAction.EliteRide);
        engine.Update(1.4);
        Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(6));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ReturningKeeperResumesOnlyWhilePhysicalCoverageRemainsValid(bool expired)
    {
        double returning = expired ? 1.6 : 1.12;
        var chart = Chart(new[] { Stream("lc", EliteDrumPad.LeftCrash, 1, 1.04, 1.08, returning, returning + .04) },
            EliteCymbalMergeTests.Note(EliteDrumPad.Ride, 1.08));
        var engine = Engine(chart);
        Hit(engine, 1, EliteDrumsAction.EliteLeftCrash);
        Hit(engine, 1.08, EliteDrumsAction.EliteClosedHiHat);
        Hit(engine, 1.08, EliteDrumsAction.EliteRide);
        engine.Update(2);
        Assert.That(Member(chart, "lc", 2).WasHit, Is.True);
        Assert.That(Member(chart, "lc", 3).WasHit, Is.EqualTo(!expired));
        Assert.That(Member(chart, "lc", 4).WasHit, Is.EqualTo(!expired));
    }

    [Test]
    public void OrdinaryKeeperNoteCannotBeAutoCompletedByAuthoredSourceCadence()
    {
        var chart = Chart(new[] { Stream("lc", EliteDrumPad.LeftCrash, 1, 1.04, 1.08, 1.12, 1.16) },
            EliteCymbalMergeTests.Note(EliteDrumPad.Ride, 1.08));
        var engine = Engine(chart);
        Hit(engine, 1, EliteDrumsAction.EliteLeftCrash);
        Hit(engine, 1.08, EliteDrumsAction.EliteClosedHiHat);
        engine.Update(1.5);
        Assert.That(Gems(chart).Single(n => n.SourceDefinition == null).WasMissed, Is.True);
        Assert.That(Member(chart, "lc", 3).WasHit, Is.True);
        Assert.That(Member(chart, "lc", 4).WasHit, Is.True);
    }

    [Test]
    public void RecordedUnorderedDestinationOwnRollCannotBootstrapMovedSourceAndLaterRealMemberCanEnterThatTarget()
    {
        var chart = Chart(new[] {
            Stream("lc", EliteDrumPad.LeftCrash, 1, 1.08, 1.16, 1.2, 1.28, 1.30),
            Stream("hat", EliteDrumPad.HiHat, .98, 1.06, 1.14, 1.22, 1.26) },
            EliteCymbalMergeTests.Note(EliteDrumPad.Ride, 1.08),
            EliteCymbalMergeTests.Note(EliteDrumPad.Ride, 1.16),
            EliteCymbalMergeTests.Note(EliteDrumPad.Ride, 1.2));
        var engine = Engine(chart, policy: EliteDrumsCymbalProjection.UNORDERED_POLICY);
        Hit(engine, .98, EliteDrumsAction.EliteClosedHiHat);
        Hit(engine, 1, EliteDrumsAction.EliteRide);
        Hit(engine, 1.08, EliteDrumsAction.EliteRide);
        engine.Update(1.155); // Settle the unentered first destination before the later real strikes.
        Hit(engine, 1.16, EliteDrumsAction.EliteClosedHiHat);
        Hit(engine, 1.16, EliteDrumsAction.EliteClosedHiHat);
        Hit(engine, 1.16, EliteDrumsAction.EliteRide);
        Hit(engine, 1.2, EliteDrumsAction.EliteRide);
        engine.Update(1.6);
        Assert.That(Member(chart, "lc", 1).WasMissed, Is.True, "Another record's entered hat cannot bootstrap this source's new target.");
        Assert.That(Member(chart, "lc", 2).WasHit, Is.True, "A later real member can enter the new target after an earlier miss.");
        Assert.That(Member(chart, "lc", 3).WasHit, Is.True);
        Assert.That(Member(chart, "lc", 4).WasHit, Is.True);
        Assert.That(Member(chart, "lc", 5).WasHit, Is.True);
        Assert.That(Member(chart, "hat", 4).WasHit, Is.True);
    }

    [Test]
    public void OpenRideRollDoesNotExpandAnLCAndHatChordWithoutAnExactRideOnset()
    {
        var chart = Chart(new[] { Stream("lc", EliteDrumPad.LeftCrash, 1, 1.08, 1.16),
            Stream("ride", EliteDrumPad.Ride, 1.04, 1.12, 1.20) }, EliteCymbalMergeTests.Note(EliteDrumPad.HiHat, 1.08));
        _ = Engine(chart);
        var chord = chart.Notes.Single(n => n.Tick == (uint)(1.08 * 960));
        Assert.That(chord.ChildNotes.Prepend(chord).Select(n => n.Pad), Is.EquivalentTo(new[] { 8, 3 }));
        Assert.That(chord.ChildNotes.Prepend(chord).Any(n => n.IsCymbalExpanded), Is.False);
    }

    [Test]
    public void MalformedTripleOnsetRetainsThreeIndependentObligations()
    {
        var chart = Chart(Array.Empty<EliteDrumNote[]>(), EliteCymbalMergeTests.Note(EliteDrumPad.LeftCrash),
            EliteCymbalMergeTests.Note(EliteDrumPad.Ride), EliteCymbalMergeTests.Note(EliteDrumPad.HiHat));
        var engine = Engine(chart);
        Assert.That(Gems(chart).Count(), Is.EqualTo(3));
        Hit(engine, 1, EliteDrumsAction.EliteClosedHiHat);
        Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(1));
        Hit(engine, 1, EliteDrumsAction.EliteOpenHiHat); // The untouched authored hat remains open.
        Hit(engine, 1, EliteDrumsAction.EliteRide);
        Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(3));
    }
}
