// pattern: Functional Core
using System;
using System.Collections.Generic;
using System.Linq;

namespace YARG.Core.Input.Diagnostics
{
    public enum MidiTestMessageKind
    {
        NoteOn = 0, NoteOff = 1, ControlChange = 2,
        PolyphonicAftertouch = 3, ChannelAftertouch = 4
    }

    /// <summary>One physical-port callback; timing is supplied by the capture owner.</summary>
    public readonly struct MidiTestMessage
    {
        public long PortId { get; }
        public string PortName { get; }
        public double ReceiptMilliseconds { get; }
        public double DriverDeltaSeconds { get; }
        public byte Status { get; }
        public byte Data1 { get; }
        public byte Data2 { get; }
        public int Channel => Status & 0x0F;
        /// <summary>Wire data bytes, excluding the status byte. Data2 is padding for channel pressure.</summary>
        public int DataLength => (Status & 0xF0) == 0xD0 ? 1 : 2;
        public int Value => (Status & 0xF0) == 0xD0 ? Data1 : Data2;

        public MidiTestMessage(long portId, string portName, double receiptMilliseconds,
            double driverDeltaSeconds, byte status, byte data1, byte data2)
        {
            PortId = portId;
            PortName = portName ?? throw new ArgumentNullException(nameof(portName));
            ReceiptMilliseconds = receiptMilliseconds;
            DriverDeltaSeconds = driverDeltaSeconds;
            Status = status;
            Data1 = data1;
            Data2 = data2;
        }
    }

    public readonly struct MidiTestAddress : IEquatable<MidiTestAddress>
    {
        public long PortId { get; }
        public int Channel { get; }
        public int Number { get; }

        public MidiTestAddress(long portId, int channel, int number)
        {
            if (portId < 0) throw new ArgumentOutOfRangeException(nameof(portId));
            if (channel < 0 || channel > 15) throw new ArgumentOutOfRangeException(nameof(channel));
            if (number < 0 || number > 127) throw new ArgumentOutOfRangeException(nameof(number));
            PortId = portId;
            Channel = channel;
            Number = number;
        }

        public bool Equals(MidiTestAddress other) => PortId == other.PortId && Channel == other.Channel &&
            Number == other.Number;
        public override bool Equals(object? obj) => obj is MidiTestAddress other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(PortId, Channel, Number);
        public static bool operator ==(MidiTestAddress left, MidiTestAddress right) => left.Equals(right);
        public static bool operator !=(MidiTestAddress left, MidiTestAddress right) => !left.Equals(right);
    }

    public readonly struct MidiTestEndpoint
    {
        public long PortId { get; }
        public string PortName { get; }
        /// <summary>Zero-based channel, or -1 for all channels on this port.</summary>
        public int Channel { get; }

        public MidiTestEndpoint(long portId, string portName, int channel = -1)
        {
            if (portId < 0) throw new ArgumentOutOfRangeException(nameof(portId));
            if (channel < -1 || channel > 15) throw new ArgumentOutOfRangeException(nameof(channel));
            PortId = portId;
            PortName = portName ?? throw new ArgumentNullException(nameof(portName));
            Channel = channel;
        }
    }

    public readonly struct MidiTestBinding
    {
        public long PortId { get; }
        public int Channel { get; }
        public bool IsController { get; }
        public int Number { get; }
        public string Key { get; }
        public string Name { get; }
        public int Action { get; }
        public bool IsPedal { get; }
        public bool Inverted { get; }
        public float PressPoint { get; }

        public MidiTestBinding(long portId, int channel, bool isController, int number,
            string key, string name, int action, bool isPedal = false, bool inverted = false,
            float pressPoint = .5f)
        {
            if (portId < 0) throw new ArgumentOutOfRangeException(nameof(portId));
            if (channel < -1 || channel > 15) throw new ArgumentOutOfRangeException(nameof(channel));
            if (number < 0 || number > 127) throw new ArgumentOutOfRangeException(nameof(number));
            if (float.IsNaN(pressPoint) || float.IsInfinity(pressPoint))
                throw new ArgumentOutOfRangeException(nameof(pressPoint));
            PortId = portId;
            Channel = channel;
            IsController = isController;
            Number = number;
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Action = action;
            IsPedal = isPedal;
            Inverted = inverted;
            PressPoint = pressPoint;
        }
    }

    public sealed class MidiTestConfiguration
    {
        public static MidiTestConfiguration Empty { get; } = new(Array.Empty<MidiTestEndpoint>(),
            Array.Empty<MidiTestBinding>());
        public IReadOnlyList<MidiTestEndpoint> Endpoints { get; }
        public IReadOnlyList<MidiTestBinding> Bindings { get; }

        public MidiTestConfiguration(IEnumerable<MidiTestEndpoint> endpoints, IEnumerable<MidiTestBinding> bindings)
        {
            Endpoints = Array.AsReadOnly((endpoints ?? throw new ArgumentNullException(nameof(endpoints))).ToArray());
            Bindings = Array.AsReadOnly((bindings ?? throw new ArgumentNullException(nameof(bindings))).ToArray());
        }
    }

    public readonly struct MidiTestRecord
    {
        public long Sequence { get; }
        public MidiTestMessage Message { get; }
        public MidiTestMessageKind Kind { get; }
        public IReadOnlyList<MidiTestBinding> Matches { get; }
        public MidiTestAddress? PedalController { get; }
        public int? PedalValue { get; }
        public bool? PedalClosed { get; }

        public MidiTestRecord(long sequence, MidiTestMessage message, MidiTestMessageKind kind,
            IEnumerable<MidiTestBinding> matches, MidiTestAddress? pedalController, int? pedalValue,
            bool? pedalClosed)
        {
            Sequence = sequence;
            Message = message;
            Kind = kind;
            Matches = Array.AsReadOnly((matches ?? throw new ArgumentNullException(nameof(matches))).ToArray());
            PedalController = pedalController;
            PedalValue = pedalValue;
            PedalClosed = pedalClosed;
        }
    }
}
