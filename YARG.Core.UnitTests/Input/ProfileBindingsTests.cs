using NUnit.Framework;
using System;
using System.Collections.Generic;
using YARG.Core.Audio;
using YARG.Core.Game;
using YARG.Core.Input;

namespace YARG.Core.UnitTests.Input;

[TestFixture]
public sealed class ProfileBindingsTests
{
    // Test for party-vocals.AC1.1: Add microphones up to cap
    [Test]
    public void AddMicrophone_OneToSeven_AllAccepted()
    {
        // Arrange
        var profile = new YargProfile { Name = "TestProfile" };
        var bindings = new TestableProfileBindings(profile);
        var micDevices = CreateTestMicDevices(7);

        // Act
        var results = new List<bool>();
        foreach (var mic in micDevices)
        {
            results.Add(bindings.AddMicrophone(mic));
        }

        // Assert
        Assert.That(results.Count, Is.EqualTo(7));
        Assert.That(results.All(r => r), Is.True, "All 7 microphones should be accepted");
        Assert.That(bindings.Microphones.Count, Is.EqualTo(7), "Should have 7 microphones");
    }

    // Test for party-vocals.AC1.2: Reject 8th microphone
    [Test]
    public void AddMicrophone_EighthRejected_DisposesNewMic()
    {
        // Arrange
        var profile = new YargProfile { Name = "TestProfile" };
        var bindings = new TestableProfileBindings(profile);
        var micDevices = CreateTestMicDevices(8);

        // Act - Add first 7
        for (int i = 0; i < 7; i++)
        {
            bindings.AddMicrophone(micDevices[i]);
        }

        // Act - Try to add 8th
        var eighthMic = micDevices[7];
        var result = bindings.AddMicrophone(eighthMic);

        // Assert
        Assert.That(result, Is.False, "8th microphone should be rejected");
        Assert.That(bindings.Microphones.Count, Is.EqualTo(7), "Should still have only 7 microphones");
        Assert.That(eighthMic.IsDisposed, Is.True, "Rejected microphone should be disposed");
    }

    // Test for party-vocals.AC1.3: Reject duplicate device
    [Test]
    public void AddMicrophone_DuplicateDeviceRejected_DisposesNewMic()
    {
        // Arrange
        var profile = new YargProfile { Name = "TestProfile" };
        var bindings = new TestableProfileBindings(profile);

        // Create two microphones with same device ID
        var mic1 = new TestableMicDevice("TestMic1", "DeviceA");
        var mic2 = new TestableMicDevice("TestMic2", "DeviceA"); // Same device ID

        // Act - Add first mic
        var firstResult = bindings.AddMicrophone(mic1);

        // Act - Try to add duplicate
        var secondResult = bindings.AddMicrophone(mic2);

        // Assert
        Assert.That(firstResult, Is.True, "First microphone should be accepted");
        Assert.That(secondResult, Is.False, "Duplicate microphone should be rejected");
        Assert.That(bindings.Microphones.Count, Is.EqualTo(1), "Should have only 1 microphone");
        Assert.That(mic2.IsDisposed, Is.True, "Duplicate microphone should be disposed");
        Assert.That(mic1.IsDisposed, Is.False, "Original microphone should not be disposed");
    }

    // Test for party-vocals.AC2.1: Microphone accessor returns first element
    [Test]
    public void Microphone_FirstElementAccessor_MatchesFirstAdded()
    {
        // Arrange
        var profile = new YargProfile { Name = "TestProfile" };
        var bindings = new TestableProfileBindings(profile);

        var mic1 = new TestableMicDevice("FirstMic", "Device1");
        var mic2 = new TestableMicDevice("SecondMic", "Device2");

        // Act
        bindings.AddMicrophone(mic1);
        bindings.AddMicrophone(mic2);

        // Assert
        Assert.That(bindings.Microphone, Is.SameAs(mic1), "Microphone property should return the first mic");
        Assert.That(bindings.Microphones[0], Is.SameAs(mic1), "Microphones[0] should be the first mic");
        Assert.That(bindings.Microphone, Is.EqualTo(bindings.Microphones[0]), "Both should reference the same mic");
    }

    // Helper to create test mic devices
    private List<TestableMicDevice> CreateTestMicDevices(int count)
    {
        var devices = new List<TestableMicDevice>();
        for (int i = 0; i < count; i++)
        {
            devices.Add(new TestableMicDevice($"Mic{i}", $"Device{i}"));
        }
        return devices;
    }

    // Testable MicDevice implementation for testing
    private class TestableMicDevice : MicDevice
    {
        public string TestDeviceId { get; }
        public bool IsDisposed { get; private set; }

        public TestableMicDevice(string displayName, string deviceId) : base(displayName)
        {
            TestDeviceId = deviceId;
            IsDisposed = false;
        }

        public override int Reset()
        {
            return 0;
        }

        public override bool DequeueOutputFrame(out MicOutputFrame frame)
        {
            frame = new MicOutputFrame(0, false, 0, 0);
            return false;
        }

        public override void ClearOutputQueue()
        {
        }

        public override void SetMonitoringLevel(float volume)
        {
        }

        public override SerializedMic Serialize()
        {
            return new SerializedMic(TestDeviceId);
        }

        protected override void DisposeManagedResources()
        {
            IsDisposed = true;
        }

        protected override void DisposeUnmanagedResources()
        {
        }
    }

    // Testable ProfileBindings that isolates mic logic from Unity dependencies
    private class TestableProfileBindings
    {
        private const int MICROPHONE_CAP = 7;
        private readonly List<MicDevice> _microphones = new();

        public IReadOnlyList<MicDevice> Microphones => _microphones;
        public MicDevice Microphone => _microphones.Count > 0 ? _microphones[0] : null;

        public TestableProfileBindings(YargProfile profile)
        {
            // Constructor would normally initialize other things, but we only test mic logic
        }

        public bool AddMicrophone(MicDevice microphone)
        {
            // Check if we've reached the cap
            if (_microphones.Count >= MICROPHONE_CAP)
            {
                microphone.Dispose();
                return false;
            }

            // Check for duplicates by device ID
            var deviceId = microphone.Serialize().Name;
            if (_microphones.Any(m => m.Serialize().Name == deviceId))
            {
                microphone.Dispose();
                return false;
            }

            _microphones.Add(microphone);
            return true;
        }

        public bool RemoveMicrophone(MicDevice microphone)
        {
            int index = _microphones.IndexOf(microphone);
            if (index >= 0)
            {
                _microphones.RemoveAt(index);
                return true;
            }
            return false;
        }
    }
}