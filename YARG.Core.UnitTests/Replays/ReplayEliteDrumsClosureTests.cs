using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Engine;
using YARG.Core.Engine.Drums;
using YARG.Core.Game;
using YARG.Core.IO;
using YARG.Core.Replays;

namespace YARG.Core.UnitTests.Replays;

[TestFixture]
public sealed class ReplayEliteDrumsClosureTests
{
    private static readonly float[] StarThresholds = { 0.05f, 0.11f, 0.19f, 0.46f, 0.77f, 1.06f };
    private static readonly float[] SoloThresholds = { 0.05f, 0.1f, 0.2f, 0.35f, 0.65f, 0.95f };

    private static DrumsEngineParameters Parameters(byte ruleset = 1, float multiplier = 2f) =>
        new(new HitWindowSettings(0.1, 0.1, 1, false, 0, 1, 1, 0.15, 0.25), 4,
            StarThresholds, SoloThresholds, DrumsEngineParameters.DrumMode.Elite, false, true,
            ruleset, multiplier);

    private static DrumsEngineParameters ReadParameters(DrumsEngineParameters source, int version)
    {
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory);
        source.Serialize(writer);
        var bytes = memory.ToArray();
        var array = FixedArray<byte>.Alloc(bytes.Length);
        bytes.CopyTo(array.Span);
        var stream = new FixedArrayStream(array);
        return new DrumsEngineParameters(ref stream, version);
    }

    [Test]
    public void OldReplayParametersUseLegacyDefaultsAndNewParametersAlign()
    {
        var current = Parameters(1, 2.75f);
        var old = ReadParameters(current, ReplayIO.REPLAY_VERSIONS.CURRENT - 1);
        var newer = ReadParameters(current, ReplayIO.REPLAY_VERSIONS.CURRENT);

        Assert.Multiple(() =>
        {
            Assert.That(old.EliteFillRuleset, Is.EqualTo(DrumsEngineParameters.LEGACY_ELITE_FILL_RULESET));
            Assert.That(old.EntryGraceMultiplier, Is.EqualTo(DrumsEngineParameters.DEFAULT_ENTRY_GRACE_MULTIPLIER));
            Assert.That(newer.EliteFillRuleset, Is.EqualTo(1));
            Assert.That(newer.EntryGraceMultiplier, Is.EqualTo(2.75f));
            Assert.That(newer.Mode, Is.EqualTo(current.Mode));
            Assert.That(newer.EnableLanes, Is.EqualTo(current.EnableLanes));
        });
    }

    [Test]
    public void InvalidNewReplayParametersFailAsInvalidData()
    {
        Assert.That(() => ReadParameters(Parameters(2, 2), ReplayIO.REPLAY_VERSIONS.CURRENT),
            Throws.TypeOf<InvalidDataException>());
        Assert.That(() => ReadParameters(Parameters(1, 0), ReplayIO.REPLAY_VERSIONS.CURRENT),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void SelectorDistinguishesNativeAndGeneratedTracks()
    {
        var chart = new SongChart(192);
        var native = Track(Instrument.ProDrums, FourLaneDrumPad.RedDrum);
        var generated = Track(Instrument.ProDrums, FourLaneDrumPad.BlueDrum);
        chart.ProDrums = native;
        chart.EliteDrumsDowncharts = new Dictionary<Instrument, InstrumentTrack<DrumNote>>
        {
            [Instrument.ProDrums] = generated,
        };

        var profile = new YargProfile { GameMode = GameMode.EliteDrums, CurrentInstrument = Instrument.ProDrums };
        Assert.That(DrumDifficultySelector.SelectTrack(chart, profile), Is.SameAs(native));
        profile.EliteDrumsDownchartTarget = Instrument.ProDrums;
        Assert.That(DrumDifficultySelector.SelectTrack(chart, profile), Is.SameAs(generated));
    }

    [Test]
    public void ExplicitTargetWithoutGeneratedTrackErrors()
    {
        var chart = new SongChart(192);
        var profile = new YargProfile { GameMode = GameMode.EliteDrums, CurrentInstrument = Instrument.ProDrums,
            EliteDrumsDownchartTarget = Instrument.ProDrums };
        Assert.That(() => DrumDifficultySelector.SelectTrack(chart, profile), Throws.TypeOf<InvalidDataException>());
    }

    private static InstrumentTrack<DrumNote> Track(Instrument instrument, FourLaneDrumPad pad)
    {
        var track = new InstrumentTrack<DrumNote>(instrument);
        track.AddDifficulty(Difficulty.Expert, new InstrumentDifficulty<DrumNote>(instrument, Difficulty.Expert,
            new List<DrumNote> { new(pad, DrumNoteType.Neutral, DrumNoteFlags.None, NoteFlags.None, 0, 0) },
            new(), new()));
        return track;
    }
}
