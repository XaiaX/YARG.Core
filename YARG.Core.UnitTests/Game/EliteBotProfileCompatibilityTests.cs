using System;
using System.IO;
using NUnit.Framework;
using YARG.Core.Extensions;
using YARG.Core.Game;
using YARG.Core.IO;

namespace YARG.Core.UnitTests.Game;

[TestFixture]
public sealed class EliteBotProfileCompatibilityTests
{
    private static readonly byte[] ELITE_BOT_MARKER = { 0x59, 0x42, 1 };

    [Test]
    public void CurrentProfile_UsesReservedBytesForEliteBotWithoutChangingLengthOrVersion()
    {
        var human = CreateProfile(isBot: false);
        var bot = CreateProfile(isBot: true);
        byte[] humanBytes = Serialize(human);
        byte[] botBytes = Serialize(bot);
        int reservedByteOffset = GetReservedByteOffset(humanBytes);

        Assert.Multiple(() =>
        {
            Assert.That(BitConverter.ToInt32(humanBytes), Is.EqualTo(17));
            Assert.That(BitConverter.ToInt32(botBytes), Is.EqualTo(17));
            Assert.That(humanBytes.Length, Is.EqualTo(botBytes.Length));
            Assert.That(humanBytes.AsSpan(reservedByteOffset, 3).ToArray(), Is.EqualTo(new byte[3]));
            Assert.That(botBytes.AsSpan(reservedByteOffset, 3).ToArray(), Is.EqualTo(ELITE_BOT_MARKER));
            Assert.That(Deserialize(humanBytes).IsBot, Is.False);
            Assert.That(Deserialize(botBytes).IsBot, Is.True);
        });
    }

    [Test]
    public void CurrentProfile_NearMarkerAndUntaggedHistoricalProfile_AreNotBots()
    {
        byte[] nearMarker = Serialize(CreateProfile(isBot: false));
        int reservedByteOffset = GetReservedByteOffset(nearMarker);
        ELITE_BOT_MARKER.CopyTo(nearMarker, reservedByteOffset);
        nearMarker[reservedByteOffset + 1]++;

        byte[] untaggedHistoricalProfile = Serialize(CreateProfile(isBot: false));
        int historicalReservedByteOffset = GetReservedByteOffset(untaggedHistoricalProfile);
        untaggedHistoricalProfile.AsSpan(historicalReservedByteOffset, 3).Clear();

        Assert.That(Deserialize(nearMarker).IsBot, Is.False);
        Assert.That(Deserialize(untaggedHistoricalProfile).IsBot, Is.False);
    }

    [Test]
    public void PreVersion17BooleanReservedBytes_AreNotInterpretedAsBotMarker()
    {
        byte[] bytes = Serialize(CreateProfile(isBot: false));
        int reservedByteOffset = GetReservedByteOffset(bytes);
        bytes[0] = 16;
        bytes[1] = 0;
        bytes[2] = 0;
        bytes[3] = 0;
        bytes[reservedByteOffset] = 1;
        bytes[reservedByteOffset + 1] = 1;
        bytes[reservedByteOffset + 2] = 1;

        Assert.That(Deserialize(bytes).IsBot, Is.False);
    }

    [Test]
    public void CurrentProfile_EliteBotMarkerInNonEliteMode_IsNotBot()
    {
        var profile = CreateProfile(isBot: false);
        profile.CurrentInstrument = Instrument.FiveFretGuitar;
        profile.GameMode = GameMode.FiveFretGuitar;
        byte[] bytes = Serialize(profile);
        int reservedByteOffset = GetReservedByteOffset(bytes);
        ELITE_BOT_MARKER.CopyTo(bytes, reservedByteOffset);

        Assert.That(Deserialize(bytes).IsBot, Is.False);
    }

    private static YargProfile CreateProfile(bool isBot)
    {
        return new YargProfile
        {
            Name = "Elite bot compatibility",
            CurrentInstrument = Instrument.EliteDrums,
            GameMode = GameMode.EliteDrums,
            IsBot = isBot,
        };
    }

    private static byte[] Serialize(YargProfile profile)
    {
        using var memory = new MemoryStream();
        using (var writer = new BinaryWriter(memory))
        {
            profile.Serialize(writer);
        }

        return memory.ToArray();
    }

    private static YargProfile Deserialize(byte[] bytes)
    {
        var fixedArray = FixedArray<byte>.Alloc(bytes.Length);
        bytes.CopyTo(fixedArray.Span);
        var stream = new FixedArrayStream(fixedArray);
        return new YargProfile(ref stream);
    }

    private static int GetReservedByteOffset(byte[] bytes)
    {
        using var memory = new MemoryStream(bytes);
        using var reader = new BinaryReader(memory);
        reader.ReadInt32(); // Version
        reader.ReadString(); // Name
        reader.ReadBytes(16 * 5); // Engine, theme, color, camera, and highway presets
        reader.ReadByte(); // CurrentInstrument
        reader.ReadByte(); // CurrentDifficulty
        reader.ReadUInt64(); // CurrentModifiers
        reader.ReadByte(); // Harmony index
        reader.ReadSingle(); // Note speed
        reader.ReadSingle(); // Highway length
        reader.ReadBoolean(); // Lefty flip
        reader.ReadBoolean(); // Range enabled
        reader.ReadBoolean(); // Use cymbal models
        return checked((int) memory.Position);
    }
}
