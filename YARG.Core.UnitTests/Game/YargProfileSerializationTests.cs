using NUnit.Framework;
using System;
using System.IO;
using Newtonsoft.Json;
using YARG.Core.Chart;
using YARG.Core.Extensions;
using YARG.Core.Game;
using YARG.Core.IO;

namespace YARG.Core.UnitTests.Game;

[TestFixture]
public sealed class YargProfileSerializationTests
{
    // Round-trip a v11 profile (no _freeHarmony) through binary serialization.
    [Test]
    public void V12Profile_RoundTrip_PreservesAllFields()
    {
        var original = CreateTestProfile(instrument: Instrument.PartyVocals);
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

        Assert.Multiple(() =>
        {
            Assert.That(deserialized.CurrentInstrument, Is.EqualTo(Instrument.PartyVocals));
            Assert.That(deserialized.IsFreeVocals, Is.True);
            Assert.That(deserialized.CurrentDifficulty, Is.EqualTo(Difficulty.Expert));
            Assert.That(deserialized.NoteSpeed, Is.EqualTo(8.5f));
            Assert.That(deserialized.LeftyFlip, Is.True);
            Assert.That(deserialized.RangeEnabled, Is.False);
            Assert.That(deserialized.Name, Is.EqualTo("TestProfile"));
            Assert.That(deserialized.Version, Is.EqualTo(12));
        });
    }

    // v10 profiles (with _freeHarmony byte) deserialize cleanly — the byte is
    // consumed and discarded, IsFreeVocals depends only on CurrentInstrument.
    [Test]
    public void DeserializeVersion10_ConsumesFreeHarmonyByte()
    {
        byte[] bytes = BuildVersion10Stream(
            name: "V10Profile",
            instrument: Instrument.Vocals,
            harmonyIndex: 0,
            freeHarmony: true);

        var fixedArray = FixedArray<byte>.Alloc(bytes.Length);
        bytes.CopyTo(fixedArray.Span);
        var stream = new FixedArrayStream(fixedArray);
        var profile = new YargProfile(ref stream);

        Assert.Multiple(() =>
        {
            Assert.That(profile.CurrentInstrument, Is.EqualTo(Instrument.Vocals));
            Assert.That(profile.Name, Is.EqualTo("V10Profile"));
            // IsFreeVocals depends only on CurrentInstrument now
            Assert.That(profile.IsFreeVocals, Is.False);
        });
    }

    [Test]
    public void DeserializeVersion10_PartyVocals_IsFreeVocals()
    {
        byte[] bytes = BuildVersion10Stream(
            name: "V10PartyVocals",
            instrument: Instrument.PartyVocals,
            harmonyIndex: 1,
            freeHarmony: false);

        var fixedArray = FixedArray<byte>.Alloc(bytes.Length);
        bytes.CopyTo(fixedArray.Span);
        var stream = new FixedArrayStream(fixedArray);
        var profile = new YargProfile(ref stream);

        Assert.Multiple(() =>
        {
            Assert.That(profile.IsFreeVocals, Is.True);
            Assert.That(profile.HarmonyIndex, Is.EqualTo((byte)1));
        });
    }

    // v8 profiles (no _freeHarmony byte) deserialize cleanly.
    [Test]
    public void DeserializeVersion8_NoFreeHarmonyByte()
    {
        byte[] bytes = BuildVersion8Stream(
            name: "V8Profile",
            instrument: Instrument.Vocals,
            harmonyIndex: 0);

        var fixedArray = FixedArray<byte>.Alloc(bytes.Length);
        bytes.CopyTo(fixedArray.Span);
        var stream = new FixedArrayStream(fixedArray);
        var profile = new YargProfile(ref stream);

        Assert.Multiple(() =>
        {
            Assert.That(profile.CurrentInstrument, Is.EqualTo(Instrument.Vocals));
            Assert.That(profile.Name, Is.EqualTo("V8Profile"));
            Assert.That(profile.IsFreeVocals, Is.False);
        });
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

        Assert.That(profile.HarmonyIndex, Is.EqualTo((byte)2));
    }

    [Test]
    public void IsFreeVocals_OnlyTrueForPartyVocalsInstrument()
    {
        var partyVocals = new YargProfile { CurrentInstrument = Instrument.PartyVocals };
        Assert.That(partyVocals.IsFreeVocals, Is.True);

        var soloVocals = new YargProfile { CurrentInstrument = Instrument.Vocals };
        Assert.That(soloVocals.IsFreeVocals, Is.False);

        var harmony = new YargProfile { CurrentInstrument = Instrument.Harmony };
        Assert.That(harmony.IsFreeVocals, Is.False);

        var guitar = new YargProfile { CurrentInstrument = Instrument.FiveFretGuitar };
        Assert.That(guitar.IsFreeVocals, Is.False);
    }

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    private static YargProfile CreateTestProfile(Instrument instrument)
    {
        return new YargProfile
        {
            Name = "TestProfile",
            CurrentInstrument = instrument,
            CurrentDifficulty = Difficulty.Expert,
        };
    }

    private static byte[] BuildVersion10Stream(string name, Instrument instrument, byte harmonyIndex, bool freeHarmony)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // Version
        writer.Write(10);

        // Name
        writer.Write(name);

        // EnginePreset, ThemePreset, ColorProfile, CameraPreset, HighwayPreset
        writer.Write(Guid.Empty);
        writer.Write(Guid.Empty);
        writer.Write(Guid.Empty);
        writer.Write(Guid.Empty);
        writer.Write(Guid.Empty);

        // CurrentInstrument, CurrentDifficulty, CurrentModifiers, _harmonyIndex
        writer.Write((byte)instrument);
        writer.Write((byte)Difficulty.Expert);
        writer.Write((ulong)Modifier.None);
        writer.Write(harmonyIndex);

        // _freeHarmony (v9-v10 only, consumed and discarded by v11+ reader)
        writer.Write(freeHarmony);

        // NoteSpeed, HighwayLength, LeftyFlip
        writer.Write(6.0f);
        writer.Write(1.0f);
        writer.Write(false);

        // RangeEnabled
        writer.Write(true);

        // UseCymbalModels + 3 superseded bytes
        writer.Write(true);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((byte)0);

        // StarPowerActivationType, GameMode, OpenLaneDisplayType
        writer.Write((byte)StarPowerActivationType.RightmostNote);
        writer.Write((byte)instrument.ToNativeGameMode());
        writer.Write((byte)OpenLaneDisplayType.Never);

        // Highway orderings
        var fourLane = new DrumsHighwayItem[] {
            DrumsHighwayItem.Red, DrumsHighwayItem.Yellow,
            DrumsHighwayItem.Blue, DrumsHighwayItem.Green
        };
        writer.Write((byte)fourLane.Length);
        foreach (var item in fourLane) writer.Write((byte)item);

        writer.Write((byte)fourLane.Length);
        foreach (var item in fourLane) writer.Write((byte)item);

        var fiveLane = new DrumsHighwayItem[] {
            DrumsHighwayItem.Red, DrumsHighwayItem.Yellow,
            DrumsHighwayItem.Blue, DrumsHighwayItem.Orange,
            DrumsHighwayItem.Green
        };
        writer.Write((byte)fiveLane.Length);
        foreach (var item in fiveLane) writer.Write((byte)item);

        return ms.ToArray();
    }

    /// <summary>
    /// Builds a byte array that mimics a v8 profile serialization.
    /// v8 format does NOT include _freeHarmony.
    /// </summary>
    private static byte[] BuildVersion8Stream(string name, Instrument instrument, byte harmonyIndex)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // Version
        writer.Write(8);

        // Name
        writer.Write(name);

        // EnginePreset, ThemePreset, ColorProfile, CameraPreset, HighwayPreset
        writer.Write(Guid.Empty);
        writer.Write(Guid.Empty);
        writer.Write(Guid.Empty);
        writer.Write(Guid.Empty);
        writer.Write(Guid.Empty);

        // CurrentInstrument, CurrentDifficulty, CurrentModifiers, _harmonyIndex
        writer.Write((byte)instrument);
        writer.Write((byte)Difficulty.Expert);
        writer.Write((ulong)Modifier.None);
        writer.Write(harmonyIndex);

        // NoteSpeed, HighwayLength, LeftyFlip
        writer.Write(6.0f);
        writer.Write(1.0f);
        writer.Write(false);

        // RangeEnabled
        writer.Write(true);

        // UseCymbalModels + 3 superseded bytes
        writer.Write(true);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((byte)0);

        // StarPowerActivationType, GameMode, OpenLaneDisplayType
        writer.Write((byte)StarPowerActivationType.RightmostNote);
        writer.Write((byte)GameMode.FiveFretGuitar);
        writer.Write((byte)OpenLaneDisplayType.Never);

        // Highway orderings
        var fourLane = new DrumsHighwayItem[] {
            DrumsHighwayItem.Red, DrumsHighwayItem.Yellow,
            DrumsHighwayItem.Blue, DrumsHighwayItem.Green
        };
        writer.Write((byte)fourLane.Length);
        foreach (var item in fourLane) writer.Write((byte)item);

        writer.Write((byte)fourLane.Length);
        foreach (var item in fourLane) writer.Write((byte)item);

        var fiveLane = new DrumsHighwayItem[] {
            DrumsHighwayItem.Red, DrumsHighwayItem.Yellow,
            DrumsHighwayItem.Blue, DrumsHighwayItem.Orange,
            DrumsHighwayItem.Green
        };
        writer.Write((byte)fiveLane.Length);
        foreach (var item in fiveLane) writer.Write((byte)item);

        return ms.ToArray();
    }

    // Round-trip PartyVocalsChartPreference through binary serialization
    [Test]
    public void PartyVocalsChartPreference_Solo_RoundTrip()
    {
        var original = CreateTestProfile(instrument: Instrument.PartyVocals);
        original.PartyVocalsChartPreference = PartyVocalsChartPreference.Solo;

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

        Assert.That(deserialized.PartyVocalsChartPreference, Is.EqualTo(PartyVocalsChartPreference.Solo));
    }

    [Test]
    public void PartyVocalsChartPreference_Auto_RoundTrip()
    {
        var original = CreateTestProfile(instrument: Instrument.PartyVocals);
        original.PartyVocalsChartPreference = PartyVocalsChartPreference.Auto;

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

        Assert.That(deserialized.PartyVocalsChartPreference, Is.EqualTo(PartyVocalsChartPreference.Auto));
    }

    // Backward compatibility: pre-v12 profiles should default to Auto
    [Test]
    public void DeserializeVersion10_DefaultsPartyVocalsChartPreferenceToAuto()
    {
        byte[] bytes = BuildVersion10Stream(
            name: "V10Profile",
            instrument: Instrument.PartyVocals,
            harmonyIndex: 0,
            freeHarmony: true);

        var fixedArray = FixedArray<byte>.Alloc(bytes.Length);
        bytes.CopyTo(fixedArray.Span);
        var stream = new FixedArrayStream(fixedArray);
        var profile = new YargProfile(ref stream);

        Assert.That(profile.PartyVocalsChartPreference, Is.EqualTo(PartyVocalsChartPreference.Auto));
    }

    // JSON persistence test
    [Test]
    public void PartyVocalsChartPreference_JsonPersistence()
    {
        var original = CreateTestProfile(instrument: Instrument.PartyVocals);
        original.PartyVocalsChartPreference = PartyVocalsChartPreference.Solo;

        string json = JsonConvert.SerializeObject(original);

        var deserialized = JsonConvert.DeserializeObject<YargProfile>(json);

        Assert.That(deserialized.PartyVocalsChartPreference, Is.EqualTo(PartyVocalsChartPreference.Solo));
    }

    // Default value test
    [Test]
    public void NewProfile_HasDefaultPartyVocalsChartPreference()
    {
        var profile = new YargProfile();

        Assert.That(profile.PartyVocalsChartPreference, Is.EqualTo(PartyVocalsChartPreference.Auto));
    }
}
