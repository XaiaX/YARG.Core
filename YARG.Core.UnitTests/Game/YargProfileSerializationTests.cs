using NUnit.Framework;
using System;
using System.IO;
using YARG.Core.Chart;
using YARG.Core.Extensions;
using YARG.Core.Game;
using YARG.Core.IO;

namespace YARG.Core.UnitTests.Game;

[TestFixture]
public sealed class YargProfileSerializationTests
{
    // ----------------------------------------------------------------
    // CRITICAL #2: Real round-trip serialization test
    // Serialize a profile with FreeHarmony=true, deserialize from bytes,
    // verify the flag survives. If serialization breaks, this fails.
    // ----------------------------------------------------------------
    [Test]
    public void FreeHarmonyFlag_RoundTripThroughSerialization()
    {
        // Arrange
        var original = CreateTestProfile(freeHarmony: true, instrument: Instrument.Vocals);

        // Act: serialize to bytes
        byte[] bytes;
        using (var ms = new MemoryStream())
        using (var writer = new BinaryWriter(ms))
        {
            original.Serialize(writer);
            bytes = ms.ToArray();
        }

        // Deserialize from those bytes using the actual deserializing constructor
        var fixedArray = FixedArray<byte>.Alloc(bytes.Length);
        bytes.CopyTo(fixedArray.Span);
        var stream = new FixedArrayStream(fixedArray);
        var deserialized = new YargProfile(ref stream);

        // Assert
        Assert.That(deserialized.FreeHarmony, Is.True,
            "FreeHarmony should survive serialization round-trip");
        Assert.That(deserialized.CurrentInstrument, Is.EqualTo(Instrument.Vocals),
            "Instrument should survive serialization round-trip");
        Assert.That(deserialized.IsFreeVocals, Is.True,
            "IsFreeVocals should be true when FreeHarmony + Vocals");
        Assert.That(deserialized.Name, Is.EqualTo("TestProfile"),
            "Name should survive serialization round-trip");
        Assert.That(deserialized.Version, Is.EqualTo(9),
            "Profile version should be 9 after round-trip serialization");
    }

    // Round-trip with FreeHarmony=false should also work
    [Test]
    public void FreeHarmonyFlag_RoundTrip_WhenFalse()
    {
        var original = CreateTestProfile(freeHarmony: false, instrument: Instrument.Vocals);

        byte[] bytes;
        using (var ms = new MemoryStream())
        using (var writer = new BinaryWriter(ms))
        {
            original.Serialize(writer);
            bytes = ms.ToArray();
        }

        var fixedArray = FixedArray<byte>.Alloc(bytes.Length);
        bytes.CopyTo(fixedArray.Span);
        var stream = new FixedArrayStream(fixedArray);
        var deserialized = new YargProfile(ref stream);

        Assert.That(deserialized.FreeHarmony, Is.False);
        Assert.That(deserialized.IsFreeVocals, Is.False);
    }

    // Round-trip with non-vocals instrument: FreeHarmony=true but IsFreeVocals=false
    [Test]
    public void FreeHarmonyFlag_RoundTrip_WhenNotVocalsInstrument()
    {
        var original = CreateTestProfile(freeHarmony: true, instrument: Instrument.FiveFretGuitar);

        byte[] bytes;
        using (var ms = new MemoryStream())
        using (var writer = new BinaryWriter(ms))
        {
            original.Serialize(writer);
            bytes = ms.ToArray();
        }

        var fixedArray = FixedArray<byte>.Alloc(bytes.Length);
        bytes.CopyTo(fixedArray.Span);
        var stream = new FixedArrayStream(fixedArray);
        var deserialized = new YargProfile(ref stream);

        Assert.That(deserialized.FreeHarmony, Is.True, "Flag should survive round-trip");
        Assert.That(deserialized.IsFreeVocals, Is.False,
            "IsFreeVocals should be false for non-vocals instrument even with FreeHarmony=true");
    }

    // ----------------------------------------------------------------
    // CRITICAL #3: v8 backwards compatibility
    // Construct a v8 byte stream (no _freeHarmony byte), deserialize,
    // verify FreeHarmony defaults to false.
    // ----------------------------------------------------------------
    [Test]
    public void DeserializeVersion8_FreeHarmonyDefaultsToFalse()
    {
        // Build a v8 stream manually
        byte[] bytes = BuildVersion8Stream(
            name: "V8Profile",
            instrument: Instrument.Vocals,
            harmonyIndex: 0);

        var fixedArray = FixedArray<byte>.Alloc(bytes.Length);
        bytes.CopyTo(fixedArray.Span);
        var stream = new FixedArrayStream(fixedArray);
        var profile = new YargProfile(ref stream);

        // Assert
        Assert.That(profile.FreeHarmony, Is.False,
            "v8 profiles should default FreeHarmony to false");
        Assert.That(profile.CurrentInstrument, Is.EqualTo(Instrument.Vocals));
        Assert.That(profile.Name, Is.EqualTo("V8Profile"));
    }

    [Test]
    public void DeserializeVersion8_WithHarmonyIndex_PreservesIndex()
    {
        byte[] bytes = BuildVersion8Stream(
            name: "V8Harmony",
            instrument: Instrument.Harmony,
            harmonyIndex: 2);

        var fixedArray = FixedArray<byte>.Alloc(bytes.Length);
        bytes.CopyTo(fixedArray.Span);
        var stream = new FixedArrayStream(fixedArray);
        var profile = new YargProfile(ref stream);

        Assert.That(profile.HarmonyIndex, Is.EqualTo((byte)2),
            "HarmonyIndex should survive v8 deserialization");
        Assert.That(profile.FreeHarmony, Is.False);
    }

    // ----------------------------------------------------------------
    // CRITICAL #4: Tests that would actually fail if logic were broken
    // ----------------------------------------------------------------

    [Test]
    public void FreeHarmony_DefaultIsFalse()
    {
        var profile = new YargProfile();
        Assert.That(profile.FreeHarmony, Is.False);
        Assert.That(profile.IsFreeVocals, Is.False);
    }

    [Test]
    public void IsFreeVocals_RequiresBothVocalsAndFreeHarmony()
    {
        // Only Vocals instrument, no FreeHarmony
        var noFlag = new YargProfile { CurrentInstrument = Instrument.Vocals };
        Assert.That(noFlag.IsFreeVocals, Is.False);

        // FreeHarmony but wrong instrument
        var wrongInstrument = new YargProfile
        {
            FreeHarmony = true,
            CurrentInstrument = Instrument.FiveFretGuitar
        };
        Assert.That(wrongInstrument.IsFreeVocals, Is.False);

        // Both required conditions
        var both = new YargProfile
        {
            FreeHarmony = true,
            CurrentInstrument = Instrument.Vocals
        };
        Assert.That(both.IsFreeVocals, Is.True);
    }

    // Verify that serializing then deserializing preserves all key fields,
    // not just FreeHarmony. This would fail if any field shifted position.
    [Test]
    public void FullProfile_RoundTrip_PreservesAllFields()
    {
        var original = CreateTestProfile(freeHarmony: true, instrument: Instrument.Vocals);
        original.CurrentDifficulty = Difficulty.Expert;
        original.NoteSpeed = 8.5f;
        original.LeftyFlip = true;
        original.RangeEnabled = false;

        byte[] bytes;
        using (var ms = new MemoryStream())
        using (var writer = new BinaryWriter(ms))
        {
            original.Serialize(writer);
            bytes = ms.ToArray();
        }

        var fixedArray = FixedArray<byte>.Alloc(bytes.Length);
        bytes.CopyTo(fixedArray.Span);
        var stream = new FixedArrayStream(fixedArray);
        var deserialized = new YargProfile(ref stream);

        Assert.That(deserialized.CurrentDifficulty, Is.EqualTo(Difficulty.Expert));
        Assert.That(deserialized.NoteSpeed, Is.EqualTo(8.5f));
        Assert.That(deserialized.LeftyFlip, Is.True);
        Assert.That(deserialized.RangeEnabled, Is.False);
        Assert.That(deserialized.FreeHarmony, Is.True);
    }

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    private static YargProfile CreateTestProfile(bool freeHarmony, Instrument instrument)
    {
        var profile = new YargProfile
        {
            Name = "TestProfile",
            FreeHarmony = freeHarmony,
            CurrentInstrument = instrument,
            CurrentDifficulty = Difficulty.Expert,
        };
        return profile;
    }

    /// <summary>
    /// Builds a byte array that mimics a v8 profile serialization.
    /// v8 format does NOT include _freeHarmony. The deserializer should
    /// default it to false.
    ///
    /// Based on the Serialize() method layout for PROFILE_VERSION = 8:
    ///   int32   Version (= 8)
    ///   string  Name (7-bit-encoded-length + UTF-8)
    ///   Guid    EnginePreset
    ///   Guid    ThemePreset
    ///   Guid    ColorProfile
    ///   Guid    CameraPreset
    ///   Guid    HighwayPreset  (v2+)
    ///   byte    CurrentInstrument
    ///   byte    CurrentDifficulty
    ///   uint64  CurrentModifiers
    ///   byte    _harmonyIndex
    ///   -- no _freeHarmony in v8 --
    ///   float32 NoteSpeed
    ///   float32 HighwayLength
    ///   bool    LeftyFlip
    ///   bool    RangeEnabled (v3+)
    ///   bool    UseCymbalModels (v4+)
    ///   byte    superseded (v4+)
    ///   byte    superseded (v4+)
    ///   byte    superseded (v4+)
    ///   byte    StarPowerActivationType (v5+)
    ///   byte    GameMode (v6+)
    ///   byte    OpenLaneDisplayType (v7+)
    ///   byte[]  FourLaneDrumsHighwayOrdering (v8+: length + items)
    ///   byte[]  ProDrumsHighwayOrdering (v8+: length + items)
    ///   byte[]  FiveLaneDrumsHighwayOrdering (v8+: length + items)
    /// </summary>
    private static byte[] BuildVersion8Stream(string name, Instrument instrument, byte harmonyIndex)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // Version
        writer.Write(8);

        // Name
        writer.Write(name);

        // EnginePreset (Guid)
        writer.Write(Guid.Empty);

        // ThemePreset, ColorProfile, CameraPreset, HighwayPreset
        writer.Write(Guid.Empty);
        writer.Write(Guid.Empty);
        writer.Write(Guid.Empty);
        writer.Write(Guid.Empty);

        // CurrentInstrument (byte)
        writer.Write((byte)instrument);

        // CurrentDifficulty (byte)
        writer.Write((byte)Difficulty.Expert);

        // CurrentModifiers (uint64)
        writer.Write((ulong)Modifier.None);

        // _harmonyIndex (byte)
        writer.Write(harmonyIndex);

        // NOTE: No _freeHarmony byte in v8!

        // NoteSpeed (float32)
        writer.Write(6.0f);

        // HighwayLength (float32)
        writer.Write(1.0f);

        // LeftyFlip (bool)
        writer.Write(false);

        // RangeEnabled (bool, v3+)
        writer.Write(true);

        // UseCymbalModels (bool, v4+)
        writer.Write(true);

        // Superseded fields (3 bytes, v4+)
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((byte)0);

        // StarPowerActivationType (byte, v5+)
        writer.Write((byte)StarPowerActivationType.RightmostNote);

        // GameMode (byte, v6+)
        writer.Write((byte)GameMode.FiveFretGuitar);

        // OpenLaneDisplayType (byte, v7+)
        writer.Write((byte)OpenLaneDisplayType.Never);

        // FourLaneDrumsHighwayOrdering (v8+: length byte + items)
        var fourLane = new DrumsHighwayItem[] {
            DrumsHighwayItem.Red,
            DrumsHighwayItem.Yellow,
            DrumsHighwayItem.Blue,
            DrumsHighwayItem.Green
        };
        writer.Write((byte)fourLane.Length);
        foreach (var item in fourLane)
        {
            writer.Write((byte)item);
        }

        // ProDrumsHighwayOrdering (v8+: length byte + items)
        writer.Write((byte)fourLane.Length);
        foreach (var item in fourLane)
        {
            writer.Write((byte)item);
        }

        // FiveLaneDrumsHighwayOrdering (v8+: length byte + items)
        var fiveLane = new DrumsHighwayItem[] {
            DrumsHighwayItem.Red,
            DrumsHighwayItem.Yellow,
            DrumsHighwayItem.Blue,
            DrumsHighwayItem.Orange,
            DrumsHighwayItem.Green
        };
        writer.Write((byte)fiveLane.Length);
        foreach (var item in fiveLane)
        {
            writer.Write((byte)item);
        }

        return ms.ToArray();
    }
}
