// pattern: Imperative Shell
using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using YARG.Core.Chart;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Core.UnitTests.Engine;

public sealed class ElitePlayableLaneSegmentsTests
{
    private static InstrumentDifficulty<EliteDrumNote> ProjectedRoll()
    {
        var members = Enumerable.Range(0, 5).Select(i =>
        {
            double time = 1 + i * .04;
            uint tick = 960 + (uint)(i * 40);
            var source = new EliteDrumSourceDefinition("segment", i, (int)EliteDrumPad.LeftCrash, tick, 0, time);
            return new EliteDrumNote(EliteDrumPad.LeftCrash, DrumNoteType.Neutral, EliteDrumsHatState.Indifferent,
                EliteDrumsHatPedalType.Stomp, false, DrumNoteFlags.None, NoteFlags.None,
                EliteDrumsChannelFlag.None, time, tick, false, source);
        }).ToArray();
        members[2].AddChildNote(new EliteDrumNote(EliteDrumPad.Ride, DrumNoteType.Neutral,
            EliteDrumsHatState.Indifferent, EliteDrumsHatPedalType.Stomp, false, DrumNoteFlags.None,
            NoteFlags.None, EliteDrumsChannelFlag.None, members[2].Time, members[2].Tick, false));
        var chart = EliteCymbalMergeTests.Chart(members);
        chart.SetEliteDrumNativeAuthoredLaneRecords(new[] { new EliteDrumNativeAuthoredLaneRecord(0,
            PhraseType.EliteDrums_LeftCrashLane, 960, 1200, members.Select(n => n.SourceDefinition!)) });
        EliteDrumsCymbalProjection.Apply(chart, EliteCymbalMergeTests.Layout("left-crash+ride"));
        return chart;
    }

    private static object[] Resolve(InstrumentDifficulty<EliteDrumNote> chart)
    {
        var resolver = typeof(EliteDrumNote).Assembly.GetType("YARG.Core.Chart.EliteDrumsPlayableLaneSegments");
        Assert.That(resolver, Is.Not.Null, "Rendering and engine cadence need the same physical lane segments.");
        return ((IEnumerable)resolver!.GetMethod("Resolve")!.Invoke(null, new object[] { chart })!).Cast<object>().ToArray();
    }
    private static T Property<T>(object segment, string name) =>
        (T)segment.GetType().GetProperty(name)!.GetValue(segment)!;

    [Test]
    public void SharedLookupKeepsActualTargetRunsAndAuthoredIdentityWithoutBridgingTransitions()
    {
        var chart = ProjectedRoll();
        var record = chart.EliteDrumNativeAuthoredLaneRecords[0];
        var segments = Resolve(chart);
        Assert.That(segments, Has.Length.EqualTo(3));
        Assert.That(segments.Select(s => Property<int>(s, "Pad")), Is.EqualTo(new[] { 8, 3, 8 }));
        Assert.That(segments.Select(s => Property<double>(s, "StartTime")), Is.EqualTo(new[] { 1, 1.08, 1.12 }).Within(.000001));
        Assert.That(segments.Select(s => Property<double>(s, "EndTime")), Is.EqualTo(new[] { 1.04, 1.08, 1.16 }).Within(.000001));
        Assert.That(segments.All(s => ReferenceEquals(Property<EliteDrumNativeAuthoredLaneRecord>(s, "AuthoredRecord"), record)), Is.True);
        var ordinals = segments.Select(s => Property<System.Collections.Generic.IReadOnlyList<EliteDrumNote>>(s, "Members")
            .Select(n => n.SourceDefinition!.PhysicalOrdinal).ToArray()).ToArray();
        Assert.That(ordinals, Is.EqualTo(new[] { new[] { 0, 1 }, new[] { 2 }, new[] { 3, 4 } }));
        Assert.That(record.MemberSources.Count, Is.EqualTo(5));
    }

    [Test]
    public void PracticeSliceAndMissingSourcesUseTheSameFailClosedSegmentBoundary()
    {
        var chart = ProjectedRoll();
        var sliced = new InstrumentDifficulty<EliteDrumNote>(chart.Instrument, chart.Difficulty,
            chart.Notes.Where(n => n.Tick >= 1000).ToList(), new(), new());
        sliced.SetEliteDrumNativeAuthoredLaneRecords(chart.SliceEliteDrumNativeAuthoredLaneRecords(1000, 1200));
        var segments = Resolve(sliced);
        Assert.That(segments, Has.Length.EqualTo(3));
        Assert.That(segments.Select(s => Property<double>(s, "StartTime")),
            Is.EqualTo(new[] { 1.04, 1.08, 1.12 }).Within(.000001));
        sliced.Notes.RemoveAt(sliced.Notes.Count - 1);
        Assert.That(Resolve(sliced), Is.Empty, "An unresolved source invalidates the authored record, rather than inventing a shorter lane.");
    }
}
