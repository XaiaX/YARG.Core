using System;
using System.IO;
using NUnit.Framework;
using YARG.Core.Game;

namespace YARG.Core.UnitTests.Game;

[TestFixture]
public sealed class DrumOutputResolverTests
{
    [Test]
    public void DrumOutputResolutionMatrix()
    {
        var formats = new[] { DrumSourceFormat.FourLane, DrumSourceFormat.FiveLane, DrumSourceFormat.Elite };
        var outputs = new[] { Instrument.FourLaneDrums, Instrument.ProDrums, Instrument.FiveLaneDrums, Instrument.EliteDrums };
        for (int mask = 0; mask < 8; mask++)
        foreach (var output in outputs)
        foreach (bool upconvert in new[] { false, true })
        foreach (bool preferElite in new[] { false, true })
        {
            var facts = new System.Collections.Generic.List<DrumSourceTierFacts>();
            for (int i = 0; i < 3; i++)
                if ((mask & (1 << i)) != 0) facts.Add(new(formats[i], Difficulty.Hard, true, true, true, false, true));
            var resolved = DrumOutputResolver.Resolve(facts, output, Difficulty.Hard, upconvert, false, preferElite);
            var native = output == Instrument.EliteDrums ? DrumSourceFormat.Elite :
                output == Instrument.FiveLaneDrums ? DrumSourceFormat.FiveLane : DrumSourceFormat.FourLane;
            bool nativePresent = (mask & (1 << (int) native)) != 0;
            if (nativePresent)
            {
                Assert.That(resolved, Is.Not.Null);
                Assert.That(resolved.SourceFormat, Is.EqualTo(native));
            }
            else if (output == Instrument.EliteDrums && !upconvert || mask == 0)
                Assert.That(resolved, Is.Null);
            else
            {
                Assert.That(resolved, Is.Not.Null);
                var expected = output == Instrument.EliteDrums
                    ? (mask & 1) != 0 ? DrumSourceFormat.FourLane : DrumSourceFormat.FiveLane
                    : preferElite && (mask & 4) != 0 ? DrumSourceFormat.Elite
                    : (mask & (1 << (native == DrumSourceFormat.FourLane ? 1 : 0))) != 0
                        ? native == DrumSourceFormat.FourLane ? DrumSourceFormat.FiveLane : DrumSourceFormat.FourLane
                        : DrumSourceFormat.Elite;
                Assert.That(resolved.SourceFormat, Is.EqualTo(expected));
            }
            if (resolved != null) Assert.That(resolved.RequestedOutput, Is.EqualTo(output));
        }
    }

    [TestCase(false, false, true, false, "", DrumExtraKickPolicy.Remove)]
    [TestCase(true, true, true, false, "(1x Kick)", DrumExtraKickPolicy.Remove)]
    [TestCase(false, true, true, false, "(0x Kick)", DrumExtraKickPolicy.Remove)]
    [TestCase(false, true, false, false, "(1x Kick)", DrumExtraKickPolicy.NormalizeExtraOnly)]
    [TestCase(false, false, true, true, "(1x Kick)", DrumExtraKickPolicy.Remove)]
    public void DrumKickTierLabelMatrix(bool ordinary, bool extra, bool other, bool paired,
        string suffix, DrumExtraKickPolicy policy)
    {
        var facts = new DrumSourceTierFacts(DrumSourceFormat.Elite, Difficulty.Easy, ordinary, extra, other, paired, true);
        Assert.That(facts.KickSuffix(false), Is.EqualTo(suffix));
        Assert.That(facts.KickPolicy(false), Is.EqualTo(policy));
        Assert.That(facts.KickSuffix(true), Is.EqualTo(facts.ExtraKick ? "(2x Kick)" : ""));
    }

    [Test]
    public void ResolvedDrumReplayRoundTrip()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        var state = new ResolvedDrumPlayback(Instrument.EliteDrums, DrumSourceFormat.FiveLane,
            Difficulty.Beginner, Difficulty.Easy, DrumExtraKickPolicy.Include, true, DrumScoreCategory.FiveLaneDerivedElite);
        state.Serialize(writer);
        writer.Write(0x12345678);
        stream.Position = 0;
        using var reader = new BinaryReader(stream);
        var restored = ResolvedDrumPlayback.Deserialize(reader);
        Assert.That(restored.SourceFormat, Is.EqualTo(state.SourceFormat));
        Assert.That(restored.BaseDifficulty, Is.EqualTo(Difficulty.Beginner));
        Assert.That(stream.Position, Is.EqualTo(8));
        Assert.That(reader.ReadInt32(), Is.EqualTo(0x12345678));
        Assert.That(state.Copy(), Is.Not.SameAs(state));
    }

    [Test]
    public void UnsupportedPolicyAndInconsistentTierRejected()
    {
        Assert.Throws<InvalidDataException>(() => new ResolvedDrumPlayback(Instrument.EliteDrums,
            DrumSourceFormat.Elite, Difficulty.Beginner, Difficulty.Beginner, DrumExtraKickPolicy.Remove,
            false, DrumScoreCategory.NativeElite));
        Assert.Throws<InvalidDataException>(() => new ResolvedDrumPlayback(Instrument.EliteDrums,
            DrumSourceFormat.Elite, Difficulty.Expert, Difficulty.Expert, DrumExtraKickPolicy.Remove,
            false, DrumScoreCategory.NativeElite, 2));
    }
}
