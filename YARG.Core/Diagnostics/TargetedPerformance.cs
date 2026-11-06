// pattern: Imperative Shell
using System;
using System.Diagnostics;

namespace YARG.Core.Diagnostics
{
    /// <summary>
    /// Opt-in, thread-local diagnostic collection. The Unity main thread enables and drains its own
    /// collector; background chart/replay analysis does not contribute. No Unity or disk dependency.
    /// </summary>
    public static class TargetedPerformance
    {
        [ThreadStatic] private static Collector? _current;
        [ThreadStatic] private static long _generation;
        public static Collector? Current => _current;
        public static bool Enabled => _current != null;

        public static void SetEnabled(bool enabled)
        {
            if (enabled == Enabled) return;
            _generation++;
            // An old in-flight scope retains the old collector, never the newly enabled one.
            _current = enabled ? new Collector() : null;
        }

        public static TimingScope Measure(TargetedOperation operation)
            => _current == null ? default : new TimingScope(_current, operation);

        public static TargetedPerformanceSnapshot TakeSnapshot()
        {
            var result = _current?.Drain() ?? default;
            result.Enabled = Enabled;
            result.Generation = _generation;
            return result;
        }

        public readonly struct TimingScope : IDisposable
        {
            private readonly Collector? _collector;
            private readonly TargetedOperation _operation;
            private readonly long _start;

            internal TimingScope(Collector collector, TargetedOperation operation)
            {
                _collector = collector;
                _operation = operation;
                _start = Stopwatch.GetTimestamp();
            }

            public void Dispose()
            {
                if (_collector != null)
                    _collector.Record(_operation, Stopwatch.GetTimestamp() - _start);
            }
        }

        public sealed class Collector
        {
            private TargetedPerformanceSnapshot _pending;

            internal Collector() { }

            public void RecordScheduling(long examined, long queued)
            {
                _pending.ScheduleNotesExamined += examined;
                _pending.DeadlinesQueued += queued;
            }

            public void RecordResolution(long examined)
            {
                _pending.ResolveNotesExamined += examined;
            }

            public void RecordContinuation() => _pending.Continuations++;

            public void RecordLaneLookup(bool member)
            {
                _pending.LaneLookups++;
                if (member) _pending.LaneMembers++;
            }

            internal void Record(TargetedOperation operation, long ticks)
            {
                switch (operation)
                {
                    case TargetedOperation.RollSchedule: _pending.RollSchedule.Add(ticks); break;
                    case TargetedOperation.RollResolve: _pending.RollResolve.Add(ticks); break;
                    case TargetedOperation.LaneMapBuild: _pending.LaneMapBuild.Add(ticks); break;
                    case TargetedOperation.HiHatActivation: _pending.HiHatActivation.Add(ticks); break;
                    case TargetedOperation.HiHatDiscovery: _pending.HiHatDiscovery.Add(ticks); break;
                    case TargetedOperation.PoolPrewarm: _pending.PoolPrewarm.Add(ticks); break;
                    case TargetedOperation.PoolCreate: _pending.PoolCreate.Add(ticks); break;
                }
            }

            internal TargetedPerformanceSnapshot Drain()
            {
                var result = _pending;
                _pending = default;
                return result;
            }
        }
    }
}
