// pattern: Functional Core
using System;
using System.Collections.Generic;
using System.Linq;

namespace YARG.Core.Engine.Drums
{

    /// <summary>Shared hand cadence with chart-derived, bounded participation windows.</summary>
    internal sealed class AuthoredHandCadence
    {
        internal sealed class Lane
        {
            public readonly int Pad;
            public readonly double[] Times;
            public readonly double InputStart;
            public readonly double InputEnd;
            public Lane(int pad, double[] times, double? inputStart = null, double? inputEnd = null)
            {
                Pad = pad;
                Times = times;
                InputStart = inputStart ?? times.FirstOrDefault();
                InputEnd = inputEnd ?? times.LastOrDefault();
            }
        }
        private const double ESTIMATE_RADIUS_SECONDS = 1;
        private const int ESTIMATE_EVENT_LIMIT = 32;
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private readonly Lane[] _lanes;
        private readonly (int Pad, double Time)[] _events;
        private readonly Dictionary<int, double> _participation = new();
        private double _expiry = double.NegativeInfinity;
        private int? _lastPad;
        private double _lastTime = double.NegativeInfinity;

        public AuthoredHandCadence(IEnumerable<Lane> lanes)
        {
            if (lanes is null) throw new ArgumentNullException(nameof(lanes));
            _lanes = lanes.Select(l => l is null || l.Times is null
                ? throw new ArgumentException("Cadence lane data must not be null.", nameof(lanes))
                : new Lane(l.Pad, (double[])l.Times.Clone(), l.InputStart, l.InputEnd)).ToArray();
            foreach (var lane in _lanes)
            {
                if (lane.Times.Length < 2 || !Finite(lane.InputStart) || !Finite(lane.InputEnd) ||
                    lane.InputStart > lane.Times[0] || lane.InputEnd < lane.Times[^1] || lane.Times.Any(t => !Finite(t)) ||
                    !lane.Times.SequenceEqual(lane.Times.OrderBy(t => t)))
                    throw new ArgumentException("Cadence lanes require ordered finite member times.", nameof(lanes));
            }
            _events = _lanes.SelectMany(l => l.Times.Select(t => (l.Pad, Time: t)))
                .Distinct().OrderBy(n => n.Time).ThenBy(n => n.Pad).ToArray();
        }

        public void Reset()
        {
            _participation.Clear();
            _expiry = _lastTime = double.NegativeInfinity;
            _lastPad = null;
        }

        private Lane[] Active(double time) => _lanes.Where(l =>
            l.InputStart <= time && l.InputEnd >= time).ToArray();

        public void Input(int pad, double time, double window)
        {
            if (!Finite(time) || !Finite(window) || window < 0)
                throw new ArgumentOutOfRangeException(nameof(time));
            if (time == _lastTime && pad == _lastPad) return;
            var active = Active(time);
            if (!active.Any(l => l.Pad == pad)) return;
            // A same-pad repetition is protected but does not advance a multi-pad trill.
            if (_lastPad != pad || active.Select(l => l.Pad).Distinct().Count() == 1)
            {
                _expiry = Math.Max(_expiry, time + window);
                _lastPad = pad;
            }
            _lastTime = time;
            // Use at most 32 nearby chart events, within one second either side. This
            // adapts to density without deriving a required tempo from player inputs.
            var nearby = Nearby(time);
            int count = nearby.Count(n => n.Pad == pad);
            double multiplier = count == 0 ? active.Select(l => l.Pad).Distinct().Count()
                : (double)nearby.Count / count;
            double allowance = window * Math.Max(1, multiplier);
            _participation[pad] = Math.Max(_participation.GetValueOrDefault(pad, double.NegativeInfinity),
                time + allowance);
        }

        private List<(int Pad, double Time)> Nearby(double time)
        {
            // Binary search once, then inspect at most 32 closest events.
            int low = 0, high = _events.Length;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (_events[middle].Time < time) low = middle + 1;
                else high = middle;
            }
            int left = low - 1, right = low;
            var result = new List<(int Pad, double Time)>(ESTIMATE_EVENT_LIMIT);
            while (result.Count < ESTIMATE_EVENT_LIMIT && (left >= 0 || right < _events.Length))
            {
                bool takeLeft = left >= 0 && (right == _events.Length ||
                    time - _events[left].Time <= _events[right].Time - time);
                var item = takeLeft ? _events[left--] : _events[right++];
                if (Math.Abs(item.Time - time) > ESTIMATE_RADIUS_SECONDS) break;
                result.Add(item);
            }
            return result;
        }

        public bool Covers(int pad, double noteTime)
        {
            if (noteTime > _expiry) return false;
            // Two pads exactly retain classic alternation. Three or more additionally
            // require bounded direct participation so BC cannot sustain an abandoned A.
            var active = Active(noteTime);
            if (!active.Any(l => l.Pad == pad)) return false;
            return active.Select(l => l.Pad).Distinct().Count() <= 2 ||
                (_participation.TryGetValue(pad, out var expiry) && noteTime <= expiry);
        }
    }
}
