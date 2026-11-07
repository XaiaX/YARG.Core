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

        public static TimingScope MeasureWithContext(TargetedOperation operation, int context)
            => _current == null ? default : new TimingScope(_current, operation, context);

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
            private readonly int _context;

            internal TimingScope(Collector collector, TargetedOperation operation, int context = 0)
            {
                _collector = collector;
                _operation = operation;
                _context = context;
                _start = Stopwatch.GetTimestamp();
            }

            public void Dispose()
            {
                if (_collector != null)
                    _collector.Record(_operation, Stopwatch.GetTimestamp() - _start, _start, _context);
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

            public void RecordFirstElementEnable() => _pending.FirstElementEnables++;
            public void RecordFirstNoteGroupInitialization() => _pending.FirstNoteGroupInitializations++;

            public void RecordAudioClockNative(in AudioClockNativeTiming sample)
                => _pending.AudioClockNative.Add(sample);

            internal void Record(TargetedOperation operation, long ticks, long start, int context)
            {
                switch (operation)
                {
                    case TargetedOperation.RollSchedule: _pending.RollSchedule.Add(ticks, start, context); break;
                    case TargetedOperation.RollResolve: _pending.RollResolve.Add(ticks, start, context); break;
                    case TargetedOperation.LaneMapBuild: _pending.LaneMapBuild.Add(ticks, start, context); break;
                    case TargetedOperation.HiHatActivation: _pending.HiHatActivation.Add(ticks, start, context); break;
                    case TargetedOperation.HiHatDiscovery: _pending.HiHatDiscovery.Add(ticks, start, context); break;
                    case TargetedOperation.PoolPrewarm: _pending.PoolPrewarm.Add(ticks, start, context); break;
                    case TargetedOperation.PoolCreate: _pending.PoolCreate.Add(ticks, start, context); break;
                    case TargetedOperation.NoteSpawn: _pending.NoteSpawn.Add(ticks, start, context); break;
                    case TargetedOperation.ElementEnable: _pending.ElementEnable.Add(ticks, start, context); break;
                    case TargetedOperation.ElementSetActive: _pending.ElementSetActive.Add(ticks, start, context); break;
                    case TargetedOperation.ElementInitialize: _pending.ElementInitialize.Add(ticks, start, context); break;
                    case TargetedOperation.ElementFirstVisualUpdate: _pending.ElementFirstVisualUpdate.Add(ticks, start, context); break;
                    case TargetedOperation.NoteGroupInitialize: _pending.NoteGroupInitialize.Add(ticks, start, context); break;
                    case TargetedOperation.MaterialAccess: _pending.MaterialAccess.Add(ticks, start, context); break;
                    case TargetedOperation.NoteGroupSetActive: _pending.NoteGroupSetActive.Add(ticks, start, context); break;
                    case TargetedOperation.PedalMeshBuild: _pending.PedalMeshBuild.Add(ticks, start, context); break;
                    case TargetedOperation.GameplayAwake: _pending.GameplayAwake.Add(ticks, start, context); break;
                    case TargetedOperation.GameplayManagerDiscovery: _pending.GameplayManagerDiscovery.Add(ticks, start, context); break;
                    case TargetedOperation.EngineUpdate: _pending.EngineUpdate.Add(ticks, start, context); break;
                    case TargetedOperation.EliteDrumHit: _pending.EliteDrumHit.Add(ticks, start, context); break;
                    case TargetedOperation.ClassicDrumHit: _pending.ClassicDrumHit.Add(ticks, start, context); break;
                    case TargetedOperation.DrumHitBookkeeping: _pending.DrumHitBookkeeping.Add(ticks, start, context); break;
                    case TargetedOperation.DrumHitGemReturn: _pending.DrumHitGemReturn.Add(ticks, start, context); break;
                    case TargetedOperation.EliteHitFeedback: _pending.EliteHitFeedback.Add(ticks, start, context); break;
                    case TargetedOperation.ClassicHitFeedback: _pending.ClassicHitFeedback.Add(ticks, start, context); break;
                    case TargetedOperation.FretHitAnimator: _pending.FretHitAnimator.Add(ticks, start, context); break;
                    case TargetedOperation.FretHitColor: _pending.FretHitColor.Add(ticks, start, context); break;
                    case TargetedOperation.FretHitEffect: _pending.FretHitEffect.Add(ticks, start, context); break;
                    case TargetedOperation.HitParticlePlay: _pending.HitParticlePlay.Add(ticks, start, context); break;
                    case TargetedOperation.HitLightPlay: _pending.HitLightPlay.Add(ticks, start, context); break;
                    case TargetedOperation.GameManagerUpdate: _pending.GameManagerUpdate.Add(ticks, start, context); break;
                    case TargetedOperation.SongRunnerUpdate: _pending.SongRunnerUpdate.Add(ticks, start, context); break;
                    case TargetedOperation.AudioSynchronize: _pending.AudioSynchronize.Add(ticks, start, context); break;
                    case TargetedOperation.AudioClockQuery: _pending.AudioClockQuery.Add(ticks, start, context); break;
                    case TargetedOperation.AudioClockCorrection: _pending.AudioClockCorrection.Add(ticks, start, context); break;
                    case TargetedOperation.GameplaySpeedUpdate: _pending.GameplaySpeedUpdate.Add(ticks, start, context); break;
                    case TargetedOperation.GameplayBeatEvents: _pending.GameplayBeatEvents.Add(ticks, start, context); break;
                    case TargetedOperation.GameplayCrowdEvents: _pending.GameplayCrowdEvents.Add(ticks, start, context); break;
                    case TargetedOperation.GameplayPlayerUpdate: _pending.GameplayPlayerUpdate.Add(ticks, start, context); break;
                    case TargetedOperation.GameplayScoreUpdate: _pending.GameplayScoreUpdate.Add(ticks, start, context); break;
                    case TargetedOperation.GameplaySongEnd: _pending.GameplaySongEnd.Add(ticks, start, context); break;
                    case TargetedOperation.AudioClockMixerLock: _pending.AudioClockMixerLock.Add(ticks, start, context); break;
                    case TargetedOperation.AudioClockNativeQuery: _pending.AudioClockNativeQuery.Add(ticks, start, context); break;
                    case TargetedOperation.AudioClockBassConvert: _pending.AudioClockBassConvert.Add(ticks, start, context); break;
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
