using NUnit.Framework;
using Newtonsoft.Json;
using System;

namespace YARG.Core.UnitTests.Input;

[TestFixture]
public sealed class BindingSerializationTests
{
    // NOTE: The actual v3→v4 migration testing cannot be implemented in YARG.Core.UnitTests
    // because the serialization classes (SerializedProfileBindings, SerializedProfileBindingsV3)
    // live in the Unity project and cannot be referenced from YARG.Core.UnitTests.
    //
    // The v3→v4 migration path can only be tested via Unity Editor integration tests.
    // This test file has been simplified to only test what can actually be verified:
    // - The SerializedMic data structure itself (which lives in YARG.Core)
    // - Basic functionality of the core data structures

    // Test for SerializedMic basic functionality
    [Test]
    public void SerializedMic_Creation_PreservesName()
    {
        // Arrange & Act
        var mic = new YARG.Core.Audio.SerializedMic("TestDevice");

        // Assert
        Assert.That(mic.Name, Is.EqualTo("TestDevice"));
    }

    // Test for SerializedMic equality (based on name)
    [Test]
    public void SerializedMic_Equality_SameName_ReturnsTrue()
    {
        // Arrange
        var mic1 = new YARG.Core.Audio.SerializedMic("TestDevice");
        var mic2 = new YARG.Core.Audio.SerializedMic("TestDevice");

        // Act & Assert
        Assert.That(mic1.Name, Is.EqualTo(mic2.Name));
    }

    [Test]
    public void SerializedMic_Equality_DifferentNames_ReturnsFalse()
    {
        // Arrange
        var mic1 = new YARG.Core.Audio.SerializedMic("Device1");
        var mic2 = new YARG.Core.Audio.SerializedMic("Device2");

        // Act & Assert
        Assert.That(mic1.Name, Is.Not.EqualTo(mic2.Name));
    }

    [Test]
    public void SerializedMic_LegacyJson_MigratesDisplayName()
    {
        var mic = JsonConvert.DeserializeObject<YARG.Core.Audio.SerializedMic>("{\"Name\":\"Device - Channel 2\",\"StableId\":\"legacy\"}");

        Assert.That(mic, Is.Not.Null);
        Assert.That(mic!.BaseName, Is.EqualTo("Device"));
        Assert.That(mic.Channel, Is.EqualTo(1));
        Assert.That(mic.StableId, Is.EqualTo("legacy"));
    }

    [Test]
    public void SerializedMic_NewJson_RoundTrips()
    {
        var original = new YARG.Core.Audio.SerializedMic("Device", 1, "stable");
        var restored = JsonConvert.DeserializeObject<YARG.Core.Audio.SerializedMic>(JsonConvert.SerializeObject(original));

        Assert.That(restored!.Name, Is.EqualTo(original.Name));
        Assert.That(restored.StableId, Is.EqualTo(original.StableId));
    }

    [Test]
    public void SerializedMic_InvalidJson_IsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            JsonConvert.DeserializeObject<YARG.Core.Audio.SerializedMic>("{\"StableId\":\"missing-identity\"}"));
    }
}