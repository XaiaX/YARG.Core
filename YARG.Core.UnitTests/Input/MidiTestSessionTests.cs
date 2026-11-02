// pattern: Functional Core
using NUnit.Framework;
using YARG.Core.Input.Diagnostics;

namespace YARG.Core.UnitTests.Input;

public sealed class MidiTestSessionTests
{
    private static MidiTestMessage Message(byte status = 0x90, byte number = 42, byte value = 90,
        long port = 1, double time = 100) => new(port, "Same kit", time, .002, status, number, value);

    private static MidiTestBinding Binding(int action, int channel = -1, bool controller = false,
        int number = 42, long port = 1, bool pedal = false, bool inverted = false, float threshold = .5f,
        string? name = null) => new(port, channel, controller, number, "part-" + action,
        name ?? "Part " + action, action, pedal, inverted, threshold);

    private static MidiTestConfiguration Configuration(params MidiTestBinding[] bindings) =>
        new(new[] { new MidiTestEndpoint(1, "Same kit") }, bindings);

    [Test]
    public void PositiveNoteOnIsCaptured()
    {
        var session = new MidiTestSession(Configuration());
        Assert.That(session.Receive(Message()).HasValue, Is.True);
    }

    [TestCase(0xA9, 42, 73, "PolyphonicAftertouch", 2, 73)]
    [TestCase(0xD9, 73, 0, "ChannelAftertouch", 1, 73)]
    [TestCase(0xA9, 42, 0, "PolyphonicAftertouch", 2, 0)]
    [TestCase(0xD9, 0, 0, "ChannelAftertouch", 1, 0)]
    public void AftertouchPreservesRawDataAndPedalSnapshotWithoutBecomingANote(
        int status, int data1, int data2, string kind, int dataLength, int value)
    {
        var session = new MidiTestSession(Configuration(Binding(4, channel: 9),
            Binding(5, controller: true, number: 4, pedal: true)));
        session.Receive(Message(status: 0xB9, number: 4, value: 100));
        var note = session.Receive(Message(status: 0x99))!.Value;
        var record = session.Receive(Message(status: (byte)status, number: (byte)data1,
            value: (byte)data2, time: 101));
        Assert.That(record.HasValue, Is.True);
        Assert.That(record!.Value.Kind.ToString(), Is.EqualTo(kind));
        Assert.That(record.Value.Message.Status, Is.EqualTo(status));
        Assert.That(record.Value.Message.Data1, Is.EqualTo(data1));
        Assert.That(record.Value.Message.Data2, Is.EqualTo(data2));
        Assert.That(record.Value.Message.DataLength, Is.EqualTo(dataLength));
        Assert.That(record.Value.Message.Value, Is.EqualTo(value));
        Assert.That(record.Value.Matches.Select(m => m.Action),
            Is.EqualTo(status == 0xA9 ? new[] { 4 } : Array.Empty<int>()));
        Assert.That(record.Value.PedalValue, Is.EqualTo(100));
        Assert.That(record.Value.PedalClosed, Is.True);
        Assert.That(session.LatestNote!.Value.Sequence, Is.EqualTo(note.Sequence));
        Assert.That(session.LastControlChange!.Value.Kind, Is.EqualTo(MidiTestMessageKind.ControlChange));
        Assert.That(session.ObservedControllers, Has.Length.EqualTo(1));
        Assert.That(session.CurrentPedalValue, Is.EqualTo(100));
    }

    [TestCase(0xA0)]
    [TestCase(0xD0)]
    public void AftertouchStillRequiresTheAssignedPortAndChannelAndCannotInventPedalPosition(int status)
    {
        var session = new MidiTestSession(new(new[] { new MidiTestEndpoint(1, "Same kit", 9) },
            Array.Empty<MidiTestBinding>()));
        Assert.That(session.Receive(Message(status: (byte)status)), Is.Null);
        Assert.That(session.Receive(Message(status: (byte)(status | 9), port: 2)), Is.Null);
        var record = session.Receive(Message(status: (byte)(status | 9)));
        Assert.That(record.HasValue, Is.True);
        Assert.That(record!.Value.PedalValue, Is.Null);
        Assert.That(record.Value.PedalClosed, Is.Null);
        Assert.That(session.LatestNote, Is.Null);
        Assert.That(session.ObservedControllers, Is.Empty);
    }

    [Test]
    public void AssignedPortAndChannelFilterAppliesBeforeAnyStateChange()
    {
        var session = new MidiTestSession(new(new[] { new MidiTestEndpoint(1, "Same kit", 9) },
            Array.Empty<MidiTestBinding>()));
        Assert.That(session.Receive(Message()), Is.Null);
        Assert.That(session.Receive(Message(status: 0x99, port: 2)), Is.Null);
        Assert.That(session.History, Is.Empty);
        Assert.That(session.LatestRecord, Is.Null);
        Assert.That(session.Receive(Message(status: 0x99))!.Value.Sequence, Is.EqualTo(1));
        Assert.That(session.LatestNote!.Value.Message.Channel, Is.EqualTo(9));
    }

    [Test]
    public void ChannelAliasesProduceOneRecordAndOneMatchPerAction()
    {
        var config = new MidiTestConfiguration(new[] { new MidiTestEndpoint(1, "Same kit"),
            new MidiTestEndpoint(1, "Same kit", 9) },
            new[] { Binding(4), Binding(4, channel: 9), Binding(7, channel: 9) });
        var session = new MidiTestSession(config);
        var record = session.Receive(Message(status: 0x99))!.Value;
        Assert.That(record.Matches.Select(b => b.Action), Is.EqualTo(new[] { 4, 7 }));
        Assert.That(session.History, Has.Length.EqualTo(1));
    }

    [Test]
    public void EveryMatchingActionIsRetainedWithoutConfusingNotesAndControllers()
    {
        var session = new MidiTestSession(Configuration(Binding(4), Binding(6), Binding(7, controller: true),
            Binding(8, number: 43), Binding(9, channel: 3)));
        var record = session.Receive(Message())!.Value;
        Assert.That(record.Matches.Select(b => b.Action), Is.EqualTo(new[] { 4, 6 }));
    }

    [Test]
    public void EqualPortNamesDoNotMergeSourceIdentities()
    {
        var session = new MidiTestSession(new(new[] { new MidiTestEndpoint(1, "Same kit"),
            new MidiTestEndpoint(2, "Same kit") }, new[] { Binding(4), Binding(6, port: 2) }));
        Assert.That(session.Receive(Message(port: 1))!.Value.Matches.Single().Action, Is.EqualTo(4));
        Assert.That(session.Receive(Message(port: 2))!.Value.Matches.Single().Action, Is.EqualTo(6));
    }

    [Test]
    public void NoteHistorySnapshotsControllerStateAtReceipt()
    {
        var session = new MidiTestSession(Configuration(Binding(1, controller: true, number: 11, pedal: true)));
        session.Receive(Message(status: 0xB0, number: 11, value: 100));
        var note = session.Receive(Message(time: 102))!.Value;
        session.Receive(Message(status: 0xB0, number: 11, value: 0, time: 103));
        Assert.That(note.PedalController, Is.EqualTo(new MidiTestAddress(1, 0, 11)));
        Assert.That(note.PedalValue, Is.EqualTo(100));
        Assert.That(note.PedalClosed, Is.True);
        Assert.That(session.CurrentPedalValue, Is.Zero);
        Assert.That(session.CurrentPedalClosed, Is.False);
        Assert.That(session.LatestNote!.Value.PedalValue, Is.EqualTo(100));
    }

    [Test]
    public void UnknownControllerStateIsDifferentFromObservedZero()
    {
        var session = new MidiTestSession(Configuration());
        session.PedalController = new(1, 0, 4);
        Assert.That(session.CurrentPedalValue, Is.Null);
        Assert.That(session.Receive(Message())!.Value.PedalValue, Is.Null);
        session.Receive(Message(status: 0xB0, number: 4, value: 0));
        Assert.That(session.CurrentPedalValue, Is.Zero);
        Assert.That(session.CurrentPedalClosed, Is.Null, "Unmapped CC has no closed-state threshold.");
    }

    [TestCase(false, 76, false)]
    [TestCase(false, 77, true)]
    [TestCase(true, 0, false)]
    [TestCase(true, 127, false)]
    public void PedalThresholdAndInversionUseItsMappedController(bool inverted, byte value, bool closed)
    {
        var session = new MidiTestSession(Configuration(Binding(1, controller: true, number: 6,
            pedal: true, inverted: inverted, threshold: .6f)));
        session.Receive(Message(status: 0xB0, number: 6, value: value));
        Assert.That(session.CurrentPedalClosed, Is.EqualTo(closed));
    }

    [Test]
    public void NegativePedalThresholdUsesTheSameSignedInversionAsLiveButtonBindings()
    {
        var session = new MidiTestSession(Configuration(Binding(1, controller: true, number: 6,
            pedal: true, inverted: true, threshold: -.6f)));
        session.Receive(Message(status: 0xB0, number: 6, value: 0));
        Assert.That(session.CurrentPedalClosed, Is.True);
        session.Receive(Message(status: 0xB0, number: 6, value: 127));
        Assert.That(session.CurrentPedalClosed, Is.False);
    }

    [Test]
    public void UnmappedControllerRemainsDiscoverableWithoutHardCodedPedalSelection()
    {
        var session = new MidiTestSession(Configuration());
        var cc = session.Receive(Message(status: 0xB2, number: 4, value: 77))!.Value;
        Assert.That(session.PedalController, Is.Null);
        Assert.That(session.CurrentPedalValue, Is.Null);
        Assert.That(session.LastControlChange, Is.EqualTo(cc));
        Assert.That(session.ObservedControllers, Is.EqualTo(new[] { new MidiTestAddress(1, 2, 4) }));
        session.PedalController = new(1, 2, 4);
        Assert.That(session.CurrentPedalValue, Is.EqualTo(77));
        Assert.That(session.CurrentPedalClosed, Is.Null);
    }

    [Test]
    public void MultipleMappedPedalControllersRequireExplicitSelection()
    {
        var session = new MidiTestSession(Configuration(
            Binding(1, controller: true, number: 4, pedal: true),
            Binding(2, controller: true, number: 11, pedal: true)));
        session.Receive(Message(status: 0xB0, number: 4, value: 77));
        Assert.That(session.PedalController, Is.Null);
        session.PedalController = new(1, 0, 4);
        Assert.That(session.CurrentPedalValue, Is.EqualTo(77));
        Assert.That(session.CurrentPedalClosed, Is.True);
    }

    [Test]
    public void ConflictingPedalMappingsDoNotInventClosedState()
    {
        var session = new MidiTestSession(Configuration(
            Binding(1, controller: true, number: 4, pedal: true),
            Binding(2, controller: true, number: 4, pedal: true, inverted: true)));
        session.PedalController = new(1, 0, 4);
        session.Receive(Message(status: 0xB0, number: 4, value: 127));
        Assert.That(session.CurrentPedalClosed, Is.Null);
    }

    [Test]
    public void ControllerValuesAreScopedByActualChannelAndControllerNumber()
    {
        var session = new MidiTestSession(Configuration());
        session.Receive(Message(status: 0xB0, number: 4, value: 100));
        session.PedalController = new(1, 1, 4);
        Assert.That(session.CurrentPedalValue, Is.Null);
        session.Receive(Message(status: 0xB1, number: 4, value: 25));
        session.Receive(Message(status: 0xB1, number: 11, value: 127));
        Assert.That(session.CurrentPedalValue, Is.EqualTo(25));
        session.PedalController = new(1, 0, 4);
        Assert.That(session.CurrentPedalValue, Is.EqualTo(100));
    }

    [Test]
    public void AllChannelPedalMappingBecomesAmbiguousWhenAnotherPhysicalChannelIsObserved()
    {
        var session = new MidiTestSession(Configuration(
            Binding(1, controller: true, number: 11, pedal: true)));
        session.Receive(Message(status: 0xB0, number: 11, value: 100));
        Assert.That(session.PedalController, Is.EqualTo(new MidiTestAddress(1, 0, 11)));
        session.Receive(Message(status: 0xB1, number: 11, value: 20));
        Assert.That(session.PedalController, Is.Null);
        Assert.That(session.CurrentPedalValue, Is.Null);
        session.PedalController = new(1, 1, 11);
        Assert.That(session.CurrentPedalValue, Is.EqualTo(20));
    }

    [Test]
    public void IdenticalPedalAliasesDoNotMakeAutomaticSelectionAmbiguous()
    {
        var session = new MidiTestSession(Configuration(
            Binding(1, controller: true, number: 11, pedal: true),
            Binding(1, channel: 9, controller: true, number: 11, pedal: true)));
        var record = session.Receive(Message(status: 0xB9, number: 11, value: 100))!.Value;
        Assert.That(record.Matches, Has.Count.EqualTo(1));
        Assert.That(record.PedalController, Is.EqualTo(new MidiTestAddress(1, 9, 11)));
        Assert.That(record.PedalClosed, Is.True);
    }

    [Test]
    public void NoteOnZeroNormalizesToOffWithoutChangingRawBytesOrLatestPositiveNote()
    {
        var session = new MidiTestSession(Configuration());
        var positive = session.Receive(Message(value: 64))!.Value;
        var zero = session.Receive(Message(value: 0))!.Value;
        Assert.That(zero.Kind, Is.EqualTo(MidiTestMessageKind.NoteOff));
        Assert.That(zero.Message.Status, Is.EqualTo(0x90));
        Assert.That(zero.Message.Data2, Is.Zero);
        session.Receive(Message(status: 0x80, value: 32));
        Assert.That(session.LatestNote, Is.EqualTo(positive));
    }

    [Test]
    public void ClearHistoryKeepsLiveControllerStateAndSequence()
    {
        var session = new MidiTestSession(Configuration());
        session.PedalController = new(1, 0, 4);
        session.Receive(Message(status: 0xB0, number: 4, value: 22));
        session.ClearHistory();
        Assert.That(session.History, Is.Empty);
        Assert.That(session.CurrentPedalValue, Is.EqualTo(22));
        Assert.That(session.Receive(Message())!.Value.Sequence, Is.EqualTo(2));
    }

    [Test]
    public void HistoryIsBoundedAndReturnedArraysCannotMutateSession()
    {
        var session = new MidiTestSession(Configuration(), 3);
        for (int i = 0; i < 8; i++) session.Receive(Message(value: (byte)(i + 1), time: i));
        var history = session.History;
        Assert.That(history.Select(r => r.Sequence), Is.EqualTo(new long[] { 6, 7, 8 }));
        history[0] = default;
        Assert.That(session.History[0].Sequence, Is.EqualTo(6));
    }

    [Test]
    public void DefaultHistoryRetainsOnlyTheLatestTwoHundredRecords()
    {
        var session = new MidiTestSession(Configuration());
        var random = new Random(731);
        for (int i = 0; i < 350; i++)
        {
            byte velocity = (byte)random.Next(1, 128);
            session.Receive(Message(value: velocity, time: i * .25));
        }
        var history = session.History;
        Assert.That(history, Has.Length.EqualTo(200));
        Assert.That(history.Select(r => r.Sequence), Is.EqualTo(Enumerable.Range(151, 200).Select(i => (long)i)));
        Assert.That(history.Select(r => r.Message.ReceiptMilliseconds), Is.Ordered);
        Assert.That(session.LatestNote, Is.EqualTo(history[^1]));
    }

    [Test]
    public void ConfigurationAndHistoricalMatchesAreImmutableSnapshots()
    {
        var endpoints = new[] { new MidiTestEndpoint(1, "Same kit") };
        var bindings = new[] { Binding(4, name: "Closed hat") };
        var config = new MidiTestConfiguration(endpoints, bindings);
        endpoints[0] = new(2, "Other kit");
        bindings[0] = Binding(9, name: "Changed");
        var session = new MidiTestSession(config);
        var previous = session.Receive(Message())!.Value;
        session.Configuration = Configuration(Binding(8, name: "Ride"));
        Assert.That(previous.Matches.Single().Name, Is.EqualTo("Closed hat"));
        Assert.That(session.Receive(Message())!.Value.Matches.Single().Name, Is.EqualTo("Ride"));
        Assert.That(() => ((IList<MidiTestBinding>)previous.Matches)[0] = Binding(7),
            Throws.TypeOf<NotSupportedException>());
    }

    [Test]
    public void ReconnectedPortWithSameNameDoesNotInheritDisconnectedControllerState()
    {
        var session = new MidiTestSession(Configuration(Binding(1, controller: true, number: 4, pedal: true)));
        session.Receive(Message(status: 0xB0, number: 4, value: 127));
        Assert.That(session.CurrentPedalClosed, Is.True);
        session.Configuration = new(new[] { new MidiTestEndpoint(2, "Same kit") },
            new[] { Binding(1, controller: true, number: 4, port: 2, pedal: true) });
        Assert.That(session.CurrentPedalValue, Is.Null);
        Assert.That(session.PedalController, Is.Null);
        Assert.That(session.ObservedControllers, Is.Empty);
        Assert.That(session.Receive(Message(status: 0xB0, number: 4, port: 1)), Is.Null);
        session.Receive(Message(status: 0xB0, number: 4, value: 0, port: 2));
        Assert.That(session.CurrentPedalValue, Is.Zero);
        Assert.That(session.PedalController!.Value.PortId, Is.EqualTo(2));
        Assert.That(session.History[0].PedalValue, Is.EqualTo(127));
    }

    [TestCase(0xF8, 0, 0)]
    [TestCase(0xE0, 42, 100)]
    [TestCase(0x90, 128, 100)]
    [TestCase(0xB0, 4, 255)]
    public void UnsupportedOrMalformedMessageCannotChangeSession(byte status, byte number, byte value)
    {
        var session = new MidiTestSession(Configuration());
        Assert.That(session.Receive(Message(status, number, value)), Is.Null);
        Assert.That(session.History, Is.Empty);
    }
}
