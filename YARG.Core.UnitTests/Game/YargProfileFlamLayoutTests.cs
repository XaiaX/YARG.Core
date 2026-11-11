// pattern: Imperative Shell
using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using YARG.Core.Extensions;
using YARG.Core.Game;
using YARG.Core.IO;
using YARG.Core.Replays;

namespace YARG.Core.UnitTests.Game;

public sealed class YargProfileFlamLayoutTests
{
    private static int Value(YargProfile profile) => (int)profile.FlamLayout;
    private static void Set(YargProfile profile, int value) => profile.FlamLayout = (DrumFlamLayout)value;
    private static bool Pending(YargProfile profile) => profile.NeedsFlamLayoutMigration;
    private static bool Migrate(YargProfile profile, int value) => profile.MigrateFlamLayout((DrumFlamLayout)value);

    [Test]
    public void NewProfilesHaveAnExplicitVChoiceAndStableCoreEnum()
    {
        var profile = new YargProfile();
        Assert.That(typeof(DrumFlamLayout).FullName, Is.EqualTo("YARG.Core.DrumFlamLayout"));
        Assert.That(Enum.GetUnderlyingType(typeof(DrumFlamLayout)), Is.EqualTo(typeof(byte)));
        Assert.That(Enum.GetNames(typeof(DrumFlamLayout)), Is.EqualTo(new[] { "V", "Bucket", "Parallel" }));
        Assert.That(Value(profile), Is.Zero);
        Assert.That(Pending(profile), Is.False);
        Assert.That(JObject.Parse(JsonConvert.SerializeObject(profile))["FlamLayout"]!.Value<int>(), Is.Zero);
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void EveryChoiceSurvivesJsonPersistenceWithoutPendingMigration(int layout)
    {
        var profile = new YargProfile();
        Set(profile, layout);
        var json = JsonConvert.SerializeObject(profile);
        Assert.That(json, Does.Not.Contain("NeedsFlamLayoutMigration"));
        var restored = JsonConvert.DeserializeObject<YargProfile>(json)!;
        Assert.That(Value(restored), Is.EqualTo(layout));
        Assert.That(Pending(restored), Is.False);
        Assert.That(Migrate(restored, 2), Is.False, "An explicit V is as deliberate as any other choice.");
    }

    [TestCase("{\"Name\":\"Old profile\"}")]
    [TestCase("{\"Name\":\"Old profile\",\"FlamLayout\":null}")]
    public void MissingChoiceSurvivesStartupPresaveThenMigratesExactlyOnce(string json)
    {
        var legacy = JsonConvert.DeserializeObject<YargProfile>(json)!;
        Assert.That(Value(legacy), Is.Zero);
        Assert.That(Pending(legacy), Is.True);
        legacy.GrandfatherIn();
        var earlySave = JsonConvert.SerializeObject(legacy);
        Assert.That(JObject.Parse(earlySave).ContainsKey("FlamLayout"), Is.False,
            "Startup saves profiles before global settings have loaded.");
        var restored = JsonConvert.DeserializeObject<YargProfile>(earlySave)!;
        Assert.That(Pending(restored), Is.True);
        Assert.That(Migrate(restored, 2), Is.True);
        Assert.That(Value(restored), Is.EqualTo(2));
        Assert.That(Pending(restored), Is.False);
        Assert.That(Migrate(restored, 1), Is.False);
        Assert.That(Value(restored), Is.EqualTo(2));
        Assert.That(Value(JsonConvert.DeserializeObject<YargProfile>(JsonConvert.SerializeObject(restored))!), Is.EqualTo(2));
    }

    [Test]
    public void InvalidLegacyChoiceSafelyDefaultsToVOnlyForPendingProfiles()
    {
        var legacy = JsonConvert.DeserializeObject<YargProfile>("{}")!;
        Assert.That(Migrate(legacy, 255), Is.True);
        Assert.That(Value(legacy), Is.Zero);
        Assert.That(Pending(legacy), Is.False);
    }

    [TestCase(3)]
    [TestCase(255)]
    public void InvalidExplicitJsonChoiceIsRejected(int layout)
    {
        Assert.Throws<JsonSerializationException>(() =>
            JsonConvert.DeserializeObject<YargProfile>("{\"FlamLayout\":" + layout + "}"));
    }

    [Test]
    public void InvalidPublicChoiceIsRejectedWithoutChangingTheExistingChoice()
    {
        var profile = new YargProfile();
        Set(profile, 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => Set(profile, 255));
        Assert.That(Value(profile), Is.EqualTo(1));
    }

    [Test]
    public void PlayersAndReplaySnapshotsRetainTheirIndependentChoices()
    {
        var first = new YargProfile();
        var second = new YargProfile();
        Set(first, 1);
        Set(second, 2);
        var snapshot = first.CreateReplaySnapshot();
        Set(first, 0);
        Assert.That(Value(first), Is.Zero);
        Assert.That(Value(second), Is.EqualTo(2));
        Assert.That(Value(snapshot), Is.EqualTo(1));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void CurrentBinaryProfilesPreserveChoicesAndConsecutiveFrameBoundaries(int layout)
    {
        var profile = new YargProfile();
        Set(profile, layout);
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory);
        profile.Serialize(writer);
        profile.Serialize(writer);
        writer.Write(0x12345678);
        var bytes = memory.ToArray();
        Assert.That(BitConverter.ToInt32(bytes), Is.EqualTo(20));
        Assert.That(ReplayIO.REPLAY_VERSIONS.CURRENT, Is.EqualTo(27));
        using var array = FixedArray<byte>.Alloc(bytes.Length);
        bytes.CopyTo(array.Span);
        var stream = new FixedArrayStream(array);
        Assert.That(Value(new YargProfile(ref stream)), Is.EqualTo(layout));
        Assert.That(Value(new YargProfile(ref stream)), Is.EqualTo(layout));
        Assert.That(stream.Read<int>(Endianness.Little), Is.EqualTo(0x12345678));
    }

    [Test]
    public void Version17ProfileDefaultsToExplicitVWithoutConsumingTheNextFrame()
    {
        byte[] bytes = Serialize(new YargProfile());
        Assert.That(BitConverter.ToInt32(bytes), Is.EqualTo(20));
        Array.Resize(ref bytes, bytes.Length - 4);
        BitConverter.GetBytes(17).CopyTo(bytes, 0);
        using var memory = new MemoryStream();
        memory.Write(bytes);
        using var writer = new BinaryWriter(memory);
        writer.Write(0x12345678);
        var all = memory.ToArray();
        using var array = FixedArray<byte>.Alloc(all.Length);
        all.CopyTo(array.Span);
        var stream = new FixedArrayStream(array);
        var restored = new YargProfile(ref stream);
        Assert.That(Value(restored), Is.Zero);
        Assert.That(Pending(restored), Is.False, "Historical replay profiles never inherit current machine preferences.");
        Assert.That(stream.Read<int>(Endianness.Little), Is.EqualTo(0x12345678));
    }

    [Test]
    public void InvalidBinaryLayoutByteIsRejected()
    {
        var bytes = Serialize(new YargProfile());
        Assert.That(BitConverter.ToInt32(bytes), Is.EqualTo(20));
        bytes[^4] = 255;
        using var array = FixedArray<byte>.Alloc(bytes.Length);
        bytes.CopyTo(array.Span);
        var stream = new FixedArrayStream(array);
        Assert.Throws<InvalidDataException>(() => new YargProfile(ref stream));
    }

    private static byte[] Serialize(YargProfile profile)
    {
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory);
        profile.Serialize(writer);
        return memory.ToArray();
    }
}
