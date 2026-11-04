// pattern: Imperative Shell
using System;
using System.IO;
using System.Linq;
using MoonscraperChartEditor.Song;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Engine.Drums;
using YARG.Core.Engine.Drums.Engines;
using YARG.Core.Extensions;
using YARG.Core.Game;
using YARG.Core.Input;
using YARG.Core.IO;
using YARG.Core.Parsing;
using YARG.Core.Replays;
using YARG.Core.UnitTests.Engine;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Core.UnitTests.Replays;

public sealed class EliteCymbalMergeReplayTests
{
    private static System.Collections.Generic.IEnumerable<TestCaseData> RecordedLayoutCases()
    {
        foreach (string choice in EliteCymbalPrimaryTests.Choices.Concat(new[] {
            "left-crash+ride", "left-crash+right-crash", "ride+right-crash" }))
        foreach (bool resolved in new[] { false, true })
        foreach (bool bot in new[] { false, true })
        foreach (bool keep in new[] { false, true })
            yield return new TestCaseData(choice, resolved, bot, keep);
    }

    private static EliteDrumPad Pad(string source) => source switch
    {
        "left-crash" => EliteDrumPad.LeftCrash,
        "ride" => EliteDrumPad.Ride,
        "right-crash" => EliteDrumPad.RightCrash,
        _ => EliteDrumPad.HiHat
    };

    private static EliteDrumsAction Action(string source) => source switch
    {
        "left-crash" => EliteDrumsAction.EliteLeftCrash,
        "ride" => EliteDrumsAction.EliteRide,
        "right-crash" => EliteDrumsAction.EliteRightCrash,
        _ => EliteDrumsAction.EliteSizzleHiHat
    };

    [TestCaseSource(nameof(RecordedLayoutCases))]
    public void RecordedLayoutPolicyAndDisplayPreferenceReconstructLiveTargets(string choice, bool resolved, bool bot, bool keep)
    {
        var song = new MoonSong(480);
        var raw = song.GetChart(MoonSong.MoonInstrument.EliteDrums, MoonSong.Difficulty.Expert);
        bool recordedUnordered = !EliteDrumsCymbalPairs.TryGetChoice(choice, out var pair);
        if (recordedUnordered) EliteDrumsCymbalPairs.TryGet(choice, out pair);
        raw.Add(new MoonNote(960, (int)Pad(pair!.SecondarySource)));
        raw.Add(new MoonNote(1920, (int)Pad(pair.SecondarySource)));
        raw.Add(new MoonNote(1920, (int)Pad(pair.PrimarySource)));
        var loader = new MoonSongLoader(song, ParseSettings.Default);
        var chart = new SongChart(480) { SyncTrack = loader.LoadSyncTrack(),
            EliteDrums = loader.LoadEliteDrumsTrack(Instrument.EliteDrums),
            AuthoredDrumSources = loader.LoadAuthoredDrumSources() };
        var layout = recordedUnordered ? EliteCymbalMergeTests.Layout(choice) :
            EliteDrumsLayoutEditor.Place(EliteDrumsLayoutPreset.Full, 8, choice);
        if (recordedUnordered) layout.SchemaVersion = 1;
        var profile = NativeEliteReplayFixture.Profile(Instrument.EliteDrums);
        profile.IsBot = bot;
        profile.KeepOriginalCymbalColors = keep;
        profile.EliteDrumsLayout = layout.Id;
        profile.ReplayEliteDrumsLayout = layout;
        var policy = new ResolvedDrumPlayback(Instrument.EliteDrums, DrumSourceFormat.Elite,
            Difficulty.Expert, Difficulty.Expert, DrumExtraKickPolicy.Include, true,
            ResolvedDrumPlayback.CategoryFor(Instrument.EliteDrums, DrumSourceFormat.Elite));
        var template = NativeEliteReplayFixture.Parameters();
        byte recordedPolicy = recordedUnordered ? EliteDrumsCymbalProjection.UNORDERED_POLICY : EliteDrumsCymbalProjection.CURRENT_POLICY;
        var parameters = new DrumsEngineParameters(template.HitWindow, template.MaxMultiplier,
            template.StarMultiplierThresholds, template.SoloBonusStarMultiplierThresholds, template.Mode,
            template.NoStarPowerOverlap, template.EnableLanes, eliteCymbalMergePolicy: recordedPolicy);
        parameters.SongSpeed = 1;
        var notes = resolved ? DrumPlaybackPreparer.PrepareForScore(chart, policy, eliteCymbalLayout: layout, eliteCymbalMergePolicy: recordedPolicy).Elite!
            : chart.EliteDrums.GetDifficulty(Difficulty.Expert).Clone();
        var times = notes.Notes.Select(n => n.Time).ToArray();
        var inputs = bot ? Array.Empty<GameInput>() : new[] {
            GameInput.Create(times[0], Action(pair.PrimarySource), .8f),
            GameInput.Create(times[1], Action(pair.SecondarySource), .8f),
            GameInput.Create(times[1], Action(pair.DestinationSource), .8f) };
        var engine = new EliteDrumsEngine(notes, chart.SyncTrack, parameters, bot, true, cymbalLayout: layout);
        var live = NativeEliteReplayFixture.PlayLive(engine, inputs);
        Assert.That(live.NotesHit, Is.EqualTo(3));
        Assert.That(notes.Notes[0].GetColorPad(keep), Is.EqualTo((int)Pad(keep || recordedUnordered ? pair.SecondarySource : pair.PrimarySource)));
        if (resolved) profile.ReplayDrumPlayback = policy;
        var data = NativeEliteReplayFixture.Data(profile, parameters, live, inputs);
        // The replay owns its content, even if the caller edits the original layout afterward.
        layout.Slots = EliteDrumsLayoutPreset.Compact.Slots;
        layout.Merges = Array.Empty<EliteDrumsLayoutMerge>();
        var restored = NativeEliteReplayFixture.RoundTrip(data);
        Assert.That(restored.Frames[0].Profile.KeepOriginalCymbalColors, Is.EqualTo(keep));
        Assert.That(((DrumsEngineParameters)restored.Frames[0].EngineParameters).EliteCymbalMergePolicy, Is.EqualTo(recordedPolicy));
        Assert.That(EliteDrumsCymbalPairs.TryGet(restored.Frames[0].Profile.ReplayEliteDrumsLayout, out var restoredPair), Is.True);
        Assert.That(restoredPair!.ChoiceId, Is.EqualTo(pair.ChoiceId));
        NativeEliteReplayFixture.AssertLiveMatchesReplay(live, NativeEliteReplayFixture.Analyze(chart, restored));
        Assert.That(chart.EliteDrums.GetDifficulty(Difficulty.Expert).Notes[0].Pad, Is.EqualTo((int)Pad(pair.SecondarySource)));
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void RecordedAutoPedalSettingMatchesLiveWithMergedTargetsAndVisiblePedals(bool resolved, bool autoPedal)
    {
        var song = new MoonSong(480);
        var raw = song.GetChart(MoonSong.MoonInstrument.EliteDrums, MoonSong.Difficulty.Expert);
        raw.Add(new MoonNote(960, (int)EliteDrumPad.LeftCrash));
        raw.Add(new MoonNote(960, (int)EliteDrumPad.Ride));
        raw.Add(new MoonNote(1440, (int)EliteDrumPad.HatPedal));
        raw.Add(new MoonNote(1920, (int)EliteDrumPad.Snare));
        var loader = new MoonSongLoader(song, ParseSettings.Default);
        var chart = new SongChart(480) { SyncTrack = loader.LoadSyncTrack(),
            EliteDrums = loader.LoadEliteDrumsTrack(Instrument.EliteDrums),
            AuthoredDrumSources = loader.LoadAuthoredDrumSources() };
        var layout = EliteCymbalMergeTests.Layout("left-crash+ride");
        var profile = NativeEliteReplayFixture.Profile(Instrument.EliteDrums);
        profile.AutoHiHatPedal = autoPedal;
        profile.ReplayEliteDrumsLayout = layout;
        var playback = new ResolvedDrumPlayback(Instrument.EliteDrums, DrumSourceFormat.Elite,
            Difficulty.Expert, Difficulty.Expert, DrumExtraKickPolicy.Include, true,
            ResolvedDrumPlayback.CategoryFor(Instrument.EliteDrums, DrumSourceFormat.Elite));
        var parameters = NativeEliteReplayFixture.Parameters();
        parameters.SongSpeed = 1;
        var notes = resolved ? DrumPlaybackPreparer.Prepare(chart, playback, eliteCymbalLayout: layout).Elite!
            : chart.EliteDrums.GetDifficulty(Difficulty.Expert).Clone();
        var inputs = new[] { GameInput.Create(1, EliteDrumsAction.EliteRide, .8f),
            GameInput.Create(1, EliteDrumsAction.EliteClosedHiHat, .8f),
            GameInput.Create(2, EliteDrumsAction.EliteSnare, .8f) };
        var engine = new EliteDrumsEngine(notes, chart.SyncTrack, parameters, false, true,
            autoHiHatPedal: profile.EffectiveAutoHiHatPedal, cymbalLayout: layout);
        var live = NativeEliteReplayFixture.PlayLive(engine, inputs);
        Assert.That(live.AssistedPedalNotes, Is.EqualTo(autoPedal ? 1 : 0));
        if (resolved) profile.ReplayDrumPlayback = playback;
        var restored = NativeEliteReplayFixture.RoundTrip(NativeEliteReplayFixture.Data(profile, parameters, live, inputs));
        var analyzed = NativeEliteReplayFixture.Analyze(chart, restored);
        NativeEliteReplayFixture.AssertLiveMatchesReplay(live, analyzed);
        var replayed = (DrumsStats)analyzed.ResultStats;
        Assert.That(replayed.OptionalPedalAccuracyEnabled, Is.EqualTo(autoPedal));
        Assert.That(replayed.AssistedPedalNotes, Is.EqualTo(live.AssistedPedalNotes));
    }

    [Test]
    public void Version24ParametersReadLegacyPolicyWithoutConsumingNextPayload()
    {
        var parameters = NativeEliteReplayFixture.Parameters();
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory);
        parameters.Serialize(writer);
        memory.SetLength(memory.Length - 2); // Remove v25 cymbal policy and v26 sustain policy.
        memory.Position = memory.Length;
        writer.Write(0x12345678);
        var bytes = memory.ToArray();
        using var array = FixedArray<byte>.Alloc(bytes.Length);
        bytes.CopyTo(array.Span);
        var stream = new FixedArrayStream(array);
        var read = new DrumsEngineParameters(ref stream, 24);
        Assert.That(read.EliteCymbalMergePolicy, Is.Zero);
        Assert.That(read.ClassicArticulationRuleset, Is.EqualTo(2));
        Assert.That(stream.Read<int>(Endianness.Little), Is.EqualTo(0x12345678));
    }

    [Test]
    public void LegacyEnginePolicyDoesNotAliasInputsOrProjectAnOtherwiseSupportedLayout()
    {
        var template = NativeEliteReplayFixture.Parameters();
        var parameters = new DrumsEngineParameters(template.HitWindow, template.MaxMultiplier,
            template.StarMultiplierThresholds, template.SoloBonusStarMultiplierThresholds, template.Mode,
            template.NoStarPowerOverlap, template.EnableLanes, eliteCymbalMergePolicy: 0);
        var notes = EliteCymbalMergeTests.Chart(EliteCymbalMergeTests.Note(EliteDrumPad.LeftCrash));
        var chart = NativeEliteReplayFixture.Chart();
        var engine = new EliteDrumsEngine(notes, chart.SyncTrack, parameters, false, true,
            cymbalLayout: EliteCymbalMergeTests.Layout("left-crash+ride"));
        EliteCymbalMergeTests.Press(engine, 1, EliteDrumsAction.EliteRide);
        Assert.That(engine.EngineStats.NotesHit, Is.Zero);
        Assert.That(notes.Notes[0].Pad, Is.EqualTo((int)EliteDrumPad.LeftCrash));
        EliteCymbalMergeTests.Press(engine, 1, EliteDrumsAction.EliteLeftCrash);
        Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(1));
    }

    [TestCase(4)]
    [TestCase(255)]
    public void UnsupportedPolicyIsRejectedBeforeMutatingPlayerNotes(int policy)
    {
        var notes = EliteCymbalMergeTests.Chart(EliteCymbalMergeTests.Note(EliteDrumPad.LeftCrash));
        var original = notes.Notes[0];
        Assert.Throws<InvalidDataException>(() => EliteDrumsCymbalProjection.Apply(notes,
            EliteCymbalMergeTests.Layout("left-crash+ride"), (byte)policy));
        Assert.That(notes.Notes[0], Is.SameAs(original));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CurrentProfilePreferenceRoundTripsAndVersion18DefaultsOff(bool keep)
    {
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory);
        new YargProfile { KeepOriginalCymbalColors = keep }.Serialize(writer);
        var bytes = memory.ToArray();
        using (var array = FixedArray<byte>.Alloc(bytes.Length))
        {
            bytes.CopyTo(array.Span);
            var stream = new FixedArrayStream(array);
            Assert.That(new YargProfile(ref stream).KeepOriginalCymbalColors, Is.EqualTo(keep));
            Assert.That(stream.Remaining, Is.Zero);
        }
        memory.SetLength(memory.Length - 3);
        memory.Position = memory.Length;
        writer.Write(0x12345678);
        bytes = memory.ToArray();
        BitConverter.GetBytes(18).CopyTo(bytes, 0);
        using var old = FixedArray<byte>.Alloc(bytes.Length);
        bytes.CopyTo(old.Span);
        var legacy = new FixedArrayStream(old);
        Assert.That(new YargProfile(ref legacy).KeepOriginalCymbalColors, Is.False);
        Assert.That(legacy.Read<int>(Endianness.Little), Is.EqualTo(0x12345678));
    }
}
