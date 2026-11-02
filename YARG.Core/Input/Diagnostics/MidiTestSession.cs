// pattern: Functional Core
using System;
using System.Collections.Generic;
using System.Linq;

namespace YARG.Core.Input.Diagnostics
{
    /// <summary>Deterministic diagnostic collector. The adapter owns synchronization.</summary>
    public sealed class MidiTestSession
    {
        private MidiTestConfiguration _configuration;
        private readonly int _historyCapacity;
        private readonly Queue<MidiTestRecord> _history = new();
        private readonly Dictionary<MidiTestAddress, int> _controllerValues = new();
        private readonly List<MidiTestAddress> _observedControllers = new();
        private MidiTestAddress? _explicitPedalController;
        private MidiTestAddress? _automaticPedalController;
        private long _sequence;

        public MidiTestConfiguration Configuration
        {
            get => _configuration;
            set
            {
                _configuration = value ?? throw new ArgumentNullException(nameof(value));
                foreach (var address in _observedControllers.Where(address => !IsAssigned(address.PortId,
                    address.Channel)).ToArray())
                {
                    _controllerValues.Remove(address);
                    _observedControllers.Remove(address);
                }
                if (_explicitPedalController is { } selected && !IsAssigned(selected.PortId, selected.Channel))
                    _explicitPedalController = null;
                _automaticPedalController = FindAutomaticPedalController();
            }
        }

        /// <summary>The selected physical address; assigning null returns to automatic selection.</summary>
        public MidiTestAddress? PedalController
        {
            get => _explicitPedalController ?? _automaticPedalController;
            set
            {
                if (value is { } selected && !IsAssigned(selected.PortId, selected.Channel))
                    throw new ArgumentException("The pedal controller must belong to an assigned MIDI endpoint.",
                        nameof(value));
                _explicitPedalController = value;
            }
        }

        public MidiTestRecord? LatestNote { get; private set; }
        public MidiTestRecord? LatestRecord { get; private set; }
        public MidiTestRecord? LastControlChange { get; private set; }
        public int? CurrentPedalValue => PedalController is { } selected &&
            _controllerValues.TryGetValue(selected, out int value) ? value : null;
        public bool? CurrentPedalClosed => PedalController is { } selected && CurrentPedalValue is { } value
            ? GetPedalClosed(selected, value) : null;
        public MidiTestRecord[] History => _history.ToArray();
        public MidiTestAddress[] ObservedControllers => _observedControllers.ToArray();

        public MidiTestSession(MidiTestConfiguration configuration, int historyCapacity = 200)
        {
            if (historyCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(historyCapacity));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _historyCapacity = historyCapacity;
        }

        public MidiTestRecord? Receive(MidiTestMessage message)
        {
            if (!IsAssigned(message.PortId, message.Channel) || message.Data1 > 127 || message.Data2 > 127)
                return null;
            MidiTestMessageKind kind;
            switch (message.Status & 0xF0)
            {
                case 0x80: kind = MidiTestMessageKind.NoteOff; break;
                case 0x90: kind = message.Data2 == 0 ? MidiTestMessageKind.NoteOff : MidiTestMessageKind.NoteOn; break;
                case 0xA0: kind = MidiTestMessageKind.PolyphonicAftertouch; break;
                case 0xB0: kind = MidiTestMessageKind.ControlChange; break;
                case 0xD0: kind = MidiTestMessageKind.ChannelAftertouch; break;
                default: return null;
            }
            if (kind == MidiTestMessageKind.ControlChange)
            {
                var address = new MidiTestAddress(message.PortId, message.Channel, message.Data1);
                if (!_controllerValues.ContainsKey(address)) _observedControllers.Add(address);
                _controllerValues[address] = message.Data2;
                _automaticPedalController = FindAutomaticPedalController();
            }
            var matches = kind == MidiTestMessageKind.ChannelAftertouch ? Enumerable.Empty<MidiTestBinding>() :
                MatchingBindings(message, kind == MidiTestMessageKind.ControlChange);
            var record = new MidiTestRecord(++_sequence, message, kind, matches, PedalController, CurrentPedalValue,
                CurrentPedalClosed);
            LatestRecord = record;
            if (kind == MidiTestMessageKind.NoteOn) LatestNote = record;
            if (kind == MidiTestMessageKind.ControlChange) LastControlChange = record;
            _history.Enqueue(record);
            if (_history.Count > _historyCapacity) _history.Dequeue();
            return record;
        }

        /// <summary>Clears only the displayed history, not received controller state or event numbering.</summary>
        public void ClearHistory() => _history.Clear();

        private bool IsAssigned(long portId, int channel) => _configuration.Endpoints.Any(endpoint =>
            endpoint.PortId == portId && (endpoint.Channel == -1 || endpoint.Channel == channel));

        private IEnumerable<MidiTestBinding> MatchingBindings(MidiTestMessage message, bool controller)
        {
            var seen = new HashSet<int>();
            foreach (var binding in _configuration.Bindings)
            {
                if (binding.IsController == controller && binding.PortId == message.PortId &&
                    binding.Number == message.Data1 && (binding.Channel == -1 || binding.Channel == message.Channel) &&
                    seen.Add(binding.Action))
                    yield return binding;
            }
        }

        private static bool MatchesController(MidiTestBinding binding, MidiTestAddress address) =>
            binding.IsController && binding.IsPedal && binding.PortId == address.PortId &&
            binding.Number == address.Number && (binding.Channel == -1 || binding.Channel == address.Channel);

        private MidiTestAddress? FindAutomaticPedalController()
        {
            var mappings = _configuration.Bindings.Where(binding => binding.IsController && binding.IsPedal &&
                _configuration.Endpoints.Any(endpoint => endpoint.PortId == binding.PortId &&
                    (endpoint.Channel == -1 || binding.Channel == -1 || endpoint.Channel == binding.Channel)))
                .ToArray();
            if (mappings.Length == 0) return null;
            MidiTestAddress? selected = null;
            foreach (var address in _observedControllers)
            {
                if (!mappings.Any(binding => MatchesController(binding, address))) continue;
                // More than one mapped CC source, number, or observed channel is ambiguous.
                if (selected.HasValue || !mappings.All(binding => MatchesController(binding, address))) return null;
                selected = address;
            }
            return selected;
        }

        private bool? GetPedalClosed(MidiTestAddress address, int value)
        {
            MidiTestBinding? first = null;
            foreach (var binding in _configuration.Bindings)
            {
                if (!MatchesController(binding, address)) continue;
                if (first is { } previous && (previous.Inverted != binding.Inverted ||
                    previous.PressPoint != binding.PressPoint)) return null;
                first = binding;
            }
            if (first is not { } mapping) return null;
            // SingleButtonBinding negates an inverted control; it does not complement 0..1.
            float normalized = value / 127f * (mapping.Inverted ? -1 : 1);
            return normalized >= mapping.PressPoint;
        }
    }
}
