using System;
using System.Diagnostics;
using System.Threading;

namespace YARG.Core.Diagnostics
{
    public enum CoreDiagnosticMarker
    {
        BaseEngineUpdate,
        BaseEngineRunQueuedUpdates,
        BaseEngineGenerateAndSortQueuedUpdates,
        BaseEngineRunEngineLoop,
        GuitarCheckForNoteHit
    }

    public readonly struct CoreDiagnosticSnapshot
    {
        public CoreDiagnosticSnapshot(
            long runQueuedUpdatesCalls,
            long scheduledBefore,
            long scheduledGenerated,
            long scheduledAfter,
            long scheduledSortTicks,
            long engineLoopIterations,
            long hitChecks,
            long hitNotesInspected,
            long hitNotesInspectedMax)
        {
            RunQueuedUpdatesCalls = runQueuedUpdatesCalls;
            ScheduledBefore = scheduledBefore;
            ScheduledGenerated = scheduledGenerated;
            ScheduledAfter = scheduledAfter;
            ScheduledSortTicks = scheduledSortTicks;
            EngineLoopIterations = engineLoopIterations;
            HitChecks = hitChecks;
            HitNotesInspected = hitNotesInspected;
            HitNotesInspectedMax = hitNotesInspectedMax;
        }

        public long RunQueuedUpdatesCalls { get; }
        public long ScheduledBefore { get; }
        public long ScheduledGenerated { get; }
        public long ScheduledAfter { get; }
        public long ScheduledSortTicks { get; }
        public long EngineLoopIterations { get; }
        public long HitChecks { get; }
        public long HitNotesInspected { get; }
        public long HitNotesInspectedMax { get; }
    }

    /// <summary>
    /// Plain-.NET instrumentation bridge for Core. Unity's ProfilerMarker cannot be referenced from
    /// YARG.Core, so Unity consumes these counters and stopwatch ticks at its frame boundary.
    /// </summary>
    public static class CorePerformanceDiagnostics
    {
        public const string BASE_ENGINE_UPDATE_MARKER = "YARG.BaseEngine.Update";
        public const string BASE_ENGINE_RUN_QUEUED_UPDATES_MARKER = "YARG.BaseEngine.RunQueuedUpdates";
        public const string BASE_ENGINE_GENERATE_AND_SORT_MARKER = "YARG.BaseEngine.GenerateAndSortQueuedUpdates";
        public const string BASE_ENGINE_LOOP_MARKER = "YARG.BaseEngine.RunEngineLoop";
        public const string GUITAR_CHECK_FOR_NOTE_HIT_MARKER = "YARG.Guitar.CheckForNoteHit";

        private static long _runQueuedUpdatesCalls;
        private static long _scheduledBefore;
        private static long _scheduledGenerated;
        private static long _scheduledAfter;
        private static long _scheduledSortTicks;
        private static long _engineLoopIterations;
        private static long _hitChecks;
        private static long _hitNotesInspected;
        private static long _hitNotesInspectedMax;

        private static int _enabled;

        public static bool Enabled
        {
            get => Volatile.Read(ref _enabled) != 0;
            set => Volatile.Write(ref _enabled, value ? 1 : 0);
        }

        public static CoreDiagnosticScope Scope(CoreDiagnosticMarker marker)
        {
            if (!Enabled)
            {
                return default;
            }

            return new CoreDiagnosticScope(marker, Stopwatch.GetTimestamp(), true);
        }

        public static void RecordRunQueuedUpdates(int scheduledBefore)
        {
            if (!Enabled)
            {
                return;
            }

            Interlocked.Increment(ref _runQueuedUpdatesCalls);
            UpdateMaximum(ref _scheduledBefore, scheduledBefore);
        }

        public static void RecordScheduledGenerated(int generated)
        {
            if (Enabled && generated > 0)
            {
                Interlocked.Add(ref _scheduledGenerated, generated);
            }
        }

        public static void RecordScheduledAfter(int scheduledAfter)
        {
            if (Enabled)
            {
                UpdateMaximum(ref _scheduledAfter, scheduledAfter);
            }
        }

        public static void RecordSortTicks(long ticks)
        {
            if (Enabled)
            {
                Interlocked.Add(ref _scheduledSortTicks, ticks);
            }
        }

        public static void RecordEngineLoopIteration()
        {
            if (Enabled)
            {
                Interlocked.Increment(ref _engineLoopIterations);
            }
        }

        public static void RecordHitCheck()
        {
            if (Enabled)
            {
                Interlocked.Increment(ref _hitChecks);
            }
        }

        public static void RecordInspectedNotes(int count)
        {
            if (!Enabled || count <= 0)
            {
                return;
            }

            Interlocked.Add(ref _hitNotesInspected, count);
            UpdateMaximum(ref _hitNotesInspectedMax, count);
        }

        public static CoreDiagnosticSnapshot TakeSnapshot()
        {
            if (!Enabled)
            {
                return default;
            }

            return new CoreDiagnosticSnapshot(
                Interlocked.Exchange(ref _runQueuedUpdatesCalls, 0),
                Interlocked.Exchange(ref _scheduledBefore, 0),
                Interlocked.Exchange(ref _scheduledGenerated, 0),
                Interlocked.Exchange(ref _scheduledAfter, 0),
                Interlocked.Exchange(ref _scheduledSortTicks, 0),
                Interlocked.Exchange(ref _engineLoopIterations, 0),
                Interlocked.Exchange(ref _hitChecks, 0),
                Interlocked.Exchange(ref _hitNotesInspected, 0),
                Interlocked.Exchange(ref _hitNotesInspectedMax, 0));
        }

        private static void UpdateMaximum(ref long target, long value)
        {
            long current;
            do
            {
                current = Volatile.Read(ref target);
                if (value <= current)
                {
                    return;
                }
            } while (Interlocked.CompareExchange(ref target, value, current) != current);
        }

        public readonly struct CoreDiagnosticScope : IDisposable
        {
            private readonly CoreDiagnosticMarker _marker;
            private readonly long _startTicks;
            private readonly bool _enabled;

            internal CoreDiagnosticScope(CoreDiagnosticMarker marker, long startTicks, bool enabled)
            {
                _marker = marker;
                _startTicks = startTicks;
                _enabled = enabled;
            }

            public void Dispose()
            {
                if (!_enabled)
                {
                    return;
                }

                long elapsed = Stopwatch.GetTimestamp() - _startTicks;
                if (_marker == CoreDiagnosticMarker.BaseEngineGenerateAndSortQueuedUpdates)
                {
                    RecordSortTicks(elapsed);
                }
            }
        }
    }
}
