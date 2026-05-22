using NUnit.Framework;
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
}