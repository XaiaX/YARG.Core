// pattern: Functional Core
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Game;
using YARG.Core.Input;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Core.UnitTests.Engine;

public sealed class EliteCymbalLaneRoutingTests
{
    internal static EliteDrumNote[] Stream(EliteDrumPad pad, params uint[] ticks) => ticks.Select((tick, index) =>
        new EliteDrumNote(pad, DrumNoteType.Neutral, EliteDrumsHatState.Indifferent, EliteDrumsHatPedalType.Stomp,
            false, DrumNoteFlags.None, NoteFlags.None, EliteDrumsChannelFlag.None, tick / 1000d, tick, false,
            new EliteDrumSourceDefinition(pad.ToString(), index, (int)pad, tick, 0, tick / 1000d))).ToArray();

    internal static InstrumentDifficulty<EliteDrumNote> Chart(params EliteDrumNote[][] streams)
    {
        var chart = EliteCymbalMergeTests.Chart();
        foreach (var onset in streams.SelectMany(notes => notes).OrderBy(note => note.Tick).GroupBy(note => note.Tick))
        {
            var parent = onset.First();
            foreach (var child in onset.Skip(1)) parent.AddChildNote(child);
            chart.Notes.Add(parent);
        }
        chart.SetEliteDrumNativeAuthoredLaneRecords(streams.Select((notes, ordinal) =>
            new EliteDrumNativeAuthoredLaneRecord(ordinal, notes[0].OriginalPad switch
            {
                (int)EliteDrumPad.LeftCrash => PhraseType.EliteDrums_LeftCrashLane,
                (int)EliteDrumPad.Ride => PhraseType.EliteDrums_RideLane,
                (int)EliteDrumPad.RightCrash => PhraseType.EliteDrums_RightCrashLane,
                _ => PhraseType.EliteDrums_HiHatLane
            }, notes[0].Tick, notes[^1].Tick, notes.Select(note => note.SourceDefinition!))));
        return chart;
    }

    internal static EliteDrumNote[] Gems(InstrumentDifficulty<EliteDrumNote> chart) =>
        chart.Notes.SelectMany(note => note.ChildNotes.Prepend(note)).ToArray();

    [Test]
    public void InterleavedRideAndLeftCrashRollsUseTwoPhysicalLanesWithoutCoincidentOnsets()
    {
        var chart = Chart(Stream(EliteDrumPad.Ride, 1000, 1200, 1400, 1600),
            Stream(EliteDrumPad.LeftCrash, 1100, 1300, 1500));
        Project(chart, "ride>left-crash");
        Assert.That(Gems(chart).Where(note => note.OriginalPad == (int)EliteDrumPad.LeftCrash)
            .Select(note => note.Pad), Is.All.EqualTo((int)EliteDrumPad.HiHat));
        Assert.That(EliteDrumsPlayableLaneSegments.ResolvePhysical(chart).Select(segment => segment.Pad),
            Is.EquivalentTo(new[] { (int)EliteDrumPad.Ride, (int)EliteDrumPad.HiHat }));
    }

    [Test]
    public void SecondaryLaneReservesHiHatFromItsStartWhenRideJoinsLater()
    {
        var chart = Chart(Stream(EliteDrumPad.RightCrash, 900, 2100, 3300, 4500),
            Stream(EliteDrumPad.LeftCrash, 1000, 2000, 3000, 4000),
            Stream(EliteDrumPad.Ride, 2500, 3500, 4400));
        var source = chart.Clone();
        Project(chart, "ride>left-crash");
        var left = Gems(chart).Where(note => note.OriginalPad == (int)EliteDrumPad.LeftCrash).ToArray();
        Assert.That(left.Select(note => note.Pad), Is.All.EqualTo((int)EliteDrumPad.HiHat),
            "A later incoming roll must not force a mid-lane target change.");
        Assert.That(left.Select(note => note.HatState), Is.All.EqualTo(EliteDrumsHatState.Indifferent));
        Assert.That(left.Select(note => note.IsCymbalExpanded), Is.All.True);
        Assert.That(EliteDrumsPlayableLaneSegments.ResolvePhysical(chart).Select(segment => segment.Pad),
            Is.EquivalentTo(new[] { (int)EliteDrumPad.RightCrash, (int)EliteDrumPad.HiHat, (int)EliteDrumPad.Ride }));
        Assert.That(Gems(chart).SelectMany(note => note.SourceDefinitions),
            Is.EquivalentTo(Gems(source).SelectMany(note => note.SourceDefinitions)));
        var once = Gems(chart).Select(note => (note.Pad, note.Tick, note.Time, note.OriginalPad, note.ColorPad)).ToArray();
        Project(chart, "ride>left-crash");
        Assert.That(Gems(chart).Select(note => (note.Pad, note.Tick, note.Time, note.OriginalPad, note.ColorPad)), Is.EqualTo(once));
    }

    [Test]
    public void SecondaryLaneUsesMergedRideWhenNoPrimaryRollOverlaps()
    {
        var chart = Chart(Stream(EliteDrumPad.LeftCrash, 1000, 2000, 3000),
            Stream(EliteDrumPad.Ride, 3100, 4100));
        Project(chart, "ride>left-crash");
        Assert.That(Gems(chart).Where(note => note.OriginalPad == (int)EliteDrumPad.LeftCrash)
            .Select(note => note.Pad), Is.All.EqualTo((int)EliteDrumPad.Ride));
        Assert.That(Gems(chart).Any(note => note.IsCymbalExpanded), Is.False);
    }

    [TestCase("left-crash>right-crash")]
    [TestCase("left-crash>ride")]
    [TestCase("ride>left-crash")]
    [TestCase("ride>right-crash")]
    [TestCase("right-crash>left-crash")]
    [TestCase("right-crash>ride")]
    public void ThreeOverlappingCymbalRollsKeepThreeTargetsForEveryPrimary(string choice)
    {
        var chart = Chart(Stream(EliteDrumPad.LeftCrash, 1000, 1600, 2200),
            Stream(EliteDrumPad.Ride, 1100, 1700, 2300),
            Stream(EliteDrumPad.RightCrash, 1200, 1800, 2400));
        Project(chart, choice);
        EliteDrumsCymbalPairs.TryGetChoice(choice, out var pair);
        var secondary = Enum.Parse<EliteDrumPad>(pair!.SecondarySource.Replace("-", ""), true);
        Assert.That(Gems(chart).Where(note => note.OriginalPad == (int)secondary).Select(note => note.Pad),
            Is.All.EqualTo((int)EliteDrumPad.HiHat));
        Assert.That(EliteDrumsPlayableLaneSegments.ResolvePhysical(chart), Has.Count.EqualTo(3));
        Assert.That(Gems(chart).Select(note => note.Pad).Distinct().Count(), Is.EqualTo(3));
    }

    [Test]
    public void ThirdLaneReservesItsInteriorEvenWhenItHasNoOnsetInsideSecondarySpan()
    {
        var chart = Chart(Stream(EliteDrumPad.LeftCrash, 3000, 6000),
            Stream(EliteDrumPad.Ride, 3500, 4500), Stream(EliteDrumPad.RightCrash, 1000, 10000));
        Project(chart, "left-crash>ride");
        Assert.That(Gems(chart).Where(note => note.OriginalPad == (int)EliteDrumPad.Ride)
            .Select(note => note.Pad), Is.All.EqualTo((int)EliteDrumPad.HiHat));
    }

    [Test]
    public void StableFallbackKeepsThreeCymbalRollsAndUnionsTheEarlierHiHatOverlap()
    {
        var chart = Chart(Stream(EliteDrumPad.HiHat, 1000, 2000),
            Stream(EliteDrumPad.RightCrash, 1500, 2500, 3500, 4500),
            Stream(EliteDrumPad.LeftCrash, 2400, 3400, 4400),
            Stream(EliteDrumPad.Ride, 3200, 4200));
        Project(chart, "left-crash>right-crash");
        Assert.That(Gems(chart).Where(note => note.OriginalPad == (int)EliteDrumPad.RightCrash)
            .Select(note => note.Pad), Is.All.EqualTo((int)EliteDrumPad.HiHat));
        var strips = EliteDrumsPlayableLaneSegments.ResolvePhysical(chart);
        Assert.That(strips, Has.Count.EqualTo(3));
        Assert.That(strips.Single(segment => segment.Pad == (int)EliteDrumPad.HiHat).AuthoredRecords, Has.Count.EqualTo(2),
            "Stable fallback intentionally shares earlier hi-hat coverage instead of changing a roll target mid-phrase.");
    }

    [Test]
    public void SparseRollDoesNotCollapseAcrossPrimaryIntervalBetweenItsMemberOnsets()
    {
        var chart = Chart(Stream(EliteDrumPad.Ride, 2000, 4000),
            Stream(EliteDrumPad.LeftCrash, 1000, 1800, 4200, 5000));
        chart.Notes.AddRange(Stream(EliteDrumPad.HiHat, 1200).Concat(Stream(EliteDrumPad.RightCrash, 4800)));
        chart.Notes.Sort((a, b) => a.Tick.CompareTo(b.Tick));
        Project(chart, "ride>left-crash");
        Assert.That(Gems(chart).Where(note => note.OriginalPad == (int)EliteDrumPad.LeftCrash)
            .Select(note => note.Pad), Is.All.EqualTo((int)EliteDrumPad.HiHat));
        Assert.That(EliteDrumsPlayableLaneSegments.ResolvePhysical(chart), Has.Count.EqualTo(2));
        Assert.That(Gems(chart).Where(note => note.Tick == 1200 || note.Tick == 4800).Select(note => note.IsCymbalExpanded),
            Is.All.False, "Ordinary noncoincident target notes retain their own judging and identity.");
    }

    [Test]
    public void RecordedPolicyTwoKeepsInterleavedRollUnion()
    {
        var chart = Chart(Stream(EliteDrumPad.Ride, 1000, 1200, 1400),
            Stream(EliteDrumPad.LeftCrash, 1100, 1300, 1500));
        Project(chart, "ride>left-crash", 2);
        Assert.That(Gems(chart).Select(note => note.Pad), Is.All.EqualTo((int)EliteDrumPad.Ride));
        Assert.That(EliteDrumsPlayableLaneSegments.ResolvePhysical(chart), Has.Count.EqualTo(1));
    }

    [Test]
    public void InterleavedRollsRequireIndependentPhysicalEntriesUnderCurrentPolicy()
    {
        var chart = Chart(Stream(EliteDrumPad.Ride, 1000, 1080, 1160),
            Stream(EliteDrumPad.LeftCrash, 1040, 1120, 1200));
        var layout = EliteDrumsLayoutEditor.Place(EliteDrumsLayoutPreset.Full, 2, "ride>left-crash");
        var engine = EliteCymbalMergeTests.Engine(chart, layout);
        EliteCymbalMergeTests.Press(engine, 1d, EliteDrumsAction.EliteRide);
        engine.Update(1.4d);
        Assert.That(Gems(chart).Where(note => note.OriginalPad == (int)EliteDrumPad.Ride).All(note => note.WasHit), Is.True);
        Assert.That(Gems(chart).Where(note => note.OriginalPad == (int)EliteDrumPad.LeftCrash).All(note => note.WasMissed), Is.True);
        engine.Reset();
        EliteCymbalMergeTests.Press(engine, 1d, EliteDrumsAction.EliteRide);
        EliteCymbalMergeTests.Press(engine, 1.04d, EliteDrumsAction.EliteClosedHiHat);
        engine.Update(1.4d);
        Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(6));
    }

    [Test]
    public void PracticeBeforeFuturePrimaryEntryKeepsFullSongReservedTargets()
    {
        var chart = Chart(Stream(EliteDrumPad.LeftCrash, 1000, 2000, 3000, 4000),
            Stream(EliteDrumPad.Ride, 2500, 3500, 4500));
        Project(chart, "ride>left-crash");
        var practice = chart.Clone();
        practice.Notes.RemoveAll(note => note.Tick < 1000 || note.Tick >= 2400);
        practice.SetEliteDrumNativeAuthoredLaneRecords(chart.SliceEliteDrumNativeAuthoredLaneRecords(1000, 2400));
        var layout = EliteDrumsLayoutEditor.Place(EliteDrumsLayoutPreset.Full, 2, "ride>left-crash");
        _ = EliteCymbalMergeTests.Engine(practice, layout);
        Assert.That(Gems(practice).Select(note => note.Pad), Is.All.EqualTo((int)EliteDrumPad.HiHat),
            "Practice must use the same pads as the full song even when the upcoming primary lane is outside the selection.");
        Assert.That(Gems(practice).Select(note => note.Tick), Is.EqualTo(new[] { 1000u, 2000u }));
    }

    private static void Project(InstrumentDifficulty<EliteDrumNote> chart, string choice,
        byte policy = EliteDrumsCymbalProjection.CURRENT_POLICY) =>
        EliteDrumsCymbalProjection.Apply(chart, EliteDrumsLayoutEditor.Place(EliteDrumsLayoutPreset.Full, 2, choice), policy);
}
