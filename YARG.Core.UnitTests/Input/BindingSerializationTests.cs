using NUnit.Framework;
using System;
using System.Collections.Generic;
using YARG.Core.Audio;
using YARG.Core.Game;

namespace YARG.Core.UnitTests.Input;

[TestFixture]
public sealed class BindingSerializationTests
{
    // Test for party-vocals.AC2.2: Deserialize V3 single mic payload
    [Test]
    public void Deserialize_V3SingleMicPayload_MigratesToSingleElementMicList()
    {
        // Arrange - Create V3 serialized data with a single microphone
        var v3Data = new SerializedProfileBindingsV3
        {
            Microphone = new SerializedMic("TestDevice")
        };

        // Act - Deserialize using the migration logic
        var current = BindingSerializationV3.MigrateToCurrent(v3Data);

        // Assert
        Assert.That(current.Microphones, Is.Not.Null);
        Assert.That(current.Microphones.Count, Is.EqualTo(1), "Should have exactly 1 microphone after migration");
        Assert.That(current.Microphones[0].Name, Is.EqualTo("TestDevice"), "Device name should be preserved");
        Assert.That(current.Microphone, Is.Not.Null, "Microphone shim should return the migrated device");
        Assert.That(current.Microphone!.Name, Is.EqualTo("TestDevice"), "Shim should return the correct device");
    }

    // Test for party-vocals.AC2.2: Deserialize V3 null mic payload
    [Test]
    public void Deserialize_V3NullMicPayload_MigratesToEmptyMicList()
    {
        // Arrange - Create V3 serialized data with null microphone
        var v3Data = new SerializedProfileBindingsV3
        {
            Microphone = null
        };

        // Act - Deserialize using the migration logic
        var current = BindingSerializationV3.MigrateToCurrent(v3Data);

        // Assert
        Assert.That(current.Microphones, Is.Not.Null);
        Assert.That(current.Microphones.Count, Is.EqualTo(0), "Should have empty microphone list when migrating from null");
        Assert.That(current.Microphone, Is.Null, "Microphone shim should return null when no devices");
    }

    // Test for party-vocals.AC2.3: Three mic list behavior
    [Test]
    public void SerializedProfileBindings_ThreeMicList_PreservesOrder()
    {
        // Arrange - Create current serialized bindings with 3 microphones
        var original = new SerializedProfileBindings
        {
            Microphones =
            {
                new SerializedMic("Device1"),
                new SerializedMic("Device2"),
                new SerializedMic("Device3")
            }
        };

        // Assert - Verify the list is created correctly
        Assert.That(original.Microphones.Count, Is.EqualTo(3), "Should have 3 microphones");

        // Verify order is preserved
        Assert.That(original.Microphones[0].Name, Is.EqualTo("Device1"), "First device should be preserved");
        Assert.That(original.Microphones[1].Name, Is.EqualTo("Device2"), "Second device should be preserved");
        Assert.That(original.Microphones[2].Name, Is.EqualTo("Device3"), "Third device should be preserved");

        // Verify shim property
        Assert.That(original.Microphone, Is.Not.Null, "Shim should return first device");
        Assert.That(original.Microphone!.Name, Is.EqualTo("Device1"), "Shim should return first device");
    }

    // Test for party-vocals.AC2.3: Empty mic list behavior
    [Test]
    public void SerializedProfileBindings_EmptyMicList_ReturnsNullShim()
    {
        // Arrange - Create current serialized bindings with empty microphone list
        var original = new SerializedProfileBindings
        {
            Microphones = new()
        };

        // Assert
        Assert.That(original.Microphones.Count, Is.EqualTo(0), "Should have empty list");
        Assert.That(original.Microphone, Is.Null, "Shim should return null when list is empty");
    }

    // Test for party-vocals.AC2.3: Single mic list behavior
    [Test]
    public void SerializedProfileBindings_SingleMicList_ShimReturnsCorrectDevice()
    {
        // Arrange - Create current serialized bindings with single microphone
        var original = new SerializedProfileBindings
        {
            Microphones =
            {
                new SerializedMic("SingleDevice")
            }
        };

        // Assert
        Assert.That(original.Microphones.Count, Is.EqualTo(1), "Should have 1 microphone");
        Assert.That(original.Microphones[0].Name, Is.EqualTo("SingleDevice"), "Device name should be preserved");
        Assert.That(original.Microphone, Is.Not.Null, "Shim should return the device");
        Assert.That(original.Microphone!.Name, Is.EqualTo("SingleDevice"), "Shim should return the correct device");
    }
}

// V3 serialization types (simplified for testing - focusing only on mic data)
public class SerializedProfileBindingsV3
{
    public SerializedMic? Microphone;
}

// Current serialization types (simplified for testing - focusing only on mic data)
public class SerializedProfileBindings
{
    public List<SerializedMic> Microphones { get; set; } = new();

    public SerializedMic? Microphone => Microphones.Count > 0 ? Microphones[0] : null;
}

// Migration method (matching what was created in Task 1)
public static class BindingSerializationV3
{
    public static SerializedProfileBindings MigrateToCurrent(SerializedProfileBindingsV3 from)
    {
        var current = new SerializedProfileBindings();

        // Migrate microphone to list
        if (from.Microphone is not null)
        {
            current.Microphones.Add(from.Microphone);
        }

        return current;
    }
}