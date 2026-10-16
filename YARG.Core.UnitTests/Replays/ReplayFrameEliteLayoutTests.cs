using System;
using System.Collections.Generic;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Engine.Drums;
using YARG.Core.Engine.Drums.Engines;
using YARG.Core.Game;
using YARG.Core.Input;
using YARG.Core.Replays;

namespace YARG.Core.UnitTests.Replays;

/// <summary>
/// ReplayFrame capture of the resolved Elite Drums layout content: deep copy at
/// capture, survival through replay-data serialization, and proof that the added
/// snapshot does not disturb the recorded inputs, stats, or analyzed score.
/// </summary>
[TestFixture]
public sealed class ReplayFrameEliteLayoutTests
{
    private static readonly Guid CUSTOM_LAYOUT_ID = new("a34d92b2-6a4e-4d66-9a53-1cb54cf24d10");

    private static EliteDrumsLayoutPreset CustomLayout()
    {
        var preset = EliteDrumsLayoutPreset.Compact.Copy();
        preset.Id = CUSTOM_LAYOUT_ID;
        preset.Name = "My layout";
        return preset;
    }

    [Test]
    public void FrameCapture_DeepCopiesResolvedLayoutContent()
    {
        var profile = NativeEliteReplayFixture.Profile(Instrument.EliteDrums);
        profile.EliteDrumsLayout = CUSTOM_LAYOUT_ID;
        var layout = CustomLayout();

        var frame = new ReplayFrame(profile, NativeEliteReplayFixture.Parameters(), new DrumsStats(),
            Array.Empty<GameInput>(), eliteDrumsLayout: layout);

        layout.Name = "edited-after-capture";
        layout.Slots[0].Drum = "edited-after-capture";

        var captured = frame.Profile.ReplayEliteDrumsLayout!;
        Assert.Multiple(() =>
        {
            Assert.That(frame.Profile, Is.Not.SameAs(profile), "Frames capture a snapshot, never the live profile");
            Assert.That(captured, Is.Not.SameAs(layout));
            Assert.That(captured.Name, Is.EqualTo("My layout"));
            Assert.That(captured.Slots[0].Drum, Is.EqualTo("snare"));
            Assert.That(captured.Id, Is.EqualTo(CUSTOM_LAYOUT_ID));
            Assert.That(frame.Profile.EliteDrumsLayout, Is.EqualTo(CUSTOM_LAYOUT_ID),
                "The snapshot profile carries the layout identity alongside the content");
        });
    }

    [Test]
    public void FrameCapture_WithoutLayout_LeavesSnapshotNull()
    {
        var profile = NativeEliteReplayFixture.Profile(Instrument.EliteDrums);
        var frame = new ReplayFrame(profile, NativeEliteReplayFixture.Parameters(), new DrumsStats(),
            Array.Empty<GameInput>());

        Assert.Multiple(() =>
        {
            Assert.That(frame.Profile.ReplayEliteDrumsLayout, Is.Null);
            Assert.That(frame.Profile.EliteDrumsLayout, Is.EqualTo(EliteDrumsLayoutPreset.CompactId),
                "Frames of profiles that never selected a layout keep the Compact identity");
        });
    }

    // The snapshot must ride through replay serialization without disturbing the
    // recorded inputs, stats, or the score the analyzer reproduces from them.
    [Test]
    public void LayoutContent_SurvivesRoundTrip_AndPreservesEventsAndScores()
    {
        var chart = NativeEliteReplayFixture.EliteChart();
        var profile = NativeEliteReplayFixture.Profile(Instrument.EliteDrums);
        profile.EliteDrumsLayout = CUSTOM_LAYOUT_ID;
        var parameters = NativeEliteReplayFixture.Parameters();
        var inputs = NativeEliteReplayFixture.EliteInputs();
        var live = NativeEliteReplayFixture.PlayLive(new EliteDrumsEngine(
            chart.EliteDrums.GetDifficulty(Difficulty.Expert).Clone(), chart.SyncTrack, parameters, false, true),
            inputs);

        var frame = new ReplayFrame(profile, parameters, live, inputs, eliteDrumsLayout: CustomLayout());
        var data = new ReplayData(new Dictionary<Guid, ColorProfile>(), new Dictionary<Guid, CameraPreset>(),
            new Dictionary<Guid, RockMeterPreset>(), false, new[] { frame }, Array.Empty<double>());
        var restored = NativeEliteReplayFixture.RoundTrip(data).Frames[0];

        var snapshot = restored.Profile.ReplayEliteDrumsLayout!;
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Id, Is.EqualTo(CUSTOM_LAYOUT_ID));
            Assert.That(snapshot.Name, Is.EqualTo("My layout"));
            Assert.That(snapshot.Slots, Has.Length.EqualTo(EliteDrumsLayoutPreset.SLOT_COUNT));
            Assert.That(snapshot.Slots[2].Drum, Is.EqualTo("tom1"));
            Assert.That(restored.Profile.EliteDrumsLayout, Is.EqualTo(CUSTOM_LAYOUT_ID));
            Assert.That(restored.Inputs, Has.Length.EqualTo(inputs.Length));
            Assert.That(restored.Inputs[0].Action, Is.EqualTo(inputs[0].Action));
            Assert.That(restored.Stats.CommittedScore, Is.EqualTo(live.CommittedScore));
            Assert.That(restored.Stats.NotesHit, Is.EqualTo(live.NotesHit));
        });

        NativeEliteReplayFixture.AssertLiveMatchesReplay(live, NativeEliteReplayFixture.Analyze(chart,
            NativeEliteReplayFixture.RoundTrip(data)));
    }
}
