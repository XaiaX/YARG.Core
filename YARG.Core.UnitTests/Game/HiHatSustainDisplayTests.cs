// pattern: Imperative Shell
using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using YARG.Core.Extensions;
using YARG.Core.Game;
using YARG.Core.IO;

namespace YARG.Core.UnitTests.Game;

public sealed class HiHatSustainDisplayTests
{
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void DisplayChoicesSurviveJsonBinaryAndReplaySnapshot(bool narrow, bool hidden)
    {
        var json = new JObject { ["OneFretHiHatSustains"] = narrow, ["HideHiHatSustains"] = hidden };
        var profile = json.ToObject<YargProfile>()!;
        void Check(YargProfile value)
        {
            var saved = JObject.FromObject(value);
            Assert.That(saved["OneFretHiHatSustains"]?.Value<bool>(), Is.EqualTo(narrow));
            Assert.That(saved["HideHiHatSustains"]?.Value<bool>(), Is.EqualTo(hidden));
        }
        Check(profile); Check(profile.CreateReplaySnapshot());
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory);
        profile.Serialize(writer); profile.Serialize(writer); writer.Write(0x12345678);
        using var bytes = FixedArray<byte>.Alloc((int)memory.Length);
        memory.ToArray().CopyTo(bytes.Span);
        var stream = new FixedArrayStream(bytes);
        Check(new YargProfile(ref stream)); Check(new YargProfile(ref stream));
        Assert.That(stream.Read<int>(Endianness.Little), Is.EqualTo(0x12345678));
    }

    [Test]
    public void Version19KeepsBroadVisibleDefaultsWithoutConsumingNextPayload()
    {
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory);
        new YargProfile().Serialize(writer);
        var bytes = memory.ToArray();
        Assert.That(BitConverter.ToInt32(bytes), Is.EqualTo(20));
        Array.Resize(ref bytes, bytes.Length - 2);
        BitConverter.GetBytes(19).CopyTo(bytes, 0);
        using var old = new MemoryStream();
        old.Write(bytes); using var oldWriter = new BinaryWriter(old); oldWriter.Write(0x12345678);
        using var array = FixedArray<byte>.Alloc((int)old.Length);
        old.ToArray().CopyTo(array.Span);
        var stream = new FixedArrayStream(array);
        var read = JObject.FromObject(new YargProfile(ref stream));
        Assert.That(read["OneFretHiHatSustains"]!.Value<bool>(), Is.False);
        Assert.That(read["HideHiHatSustains"]!.Value<bool>(), Is.False);
        Assert.That(stream.Read<int>(Endianness.Little), Is.EqualTo(0x12345678));
    }
}
