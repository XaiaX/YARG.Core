// pattern: Functional Core
namespace YARG.Core.Diagnostics
{
    public enum TargetedOperation
    {
        RollSchedule,
        RollResolve,
        LaneMapBuild,
        HiHatActivation,
        HiHatDiscovery,
        PoolPrewarm,
        PoolCreate
    }

    public struct TargetedTiming
    {
        public long Calls, ElapsedTicks, MaxTicks;

        internal void Add(long ticks)
        {
            Calls++;
            ElapsedTicks += ticks;
            if (ticks > MaxTicks) MaxTicks = ticks;
        }
    }

    /// <summary>A value copy of one sampling interval; timings are inclusive and can overlap.</summary>
    public struct TargetedPerformanceSnapshot
    {
        public bool Enabled;
        public long Generation;
        public TargetedTiming RollSchedule, RollResolve, LaneMapBuild, HiHatActivation, HiHatDiscovery,
            PoolPrewarm, PoolCreate;
        public long ScheduleNotesExamined, ResolveNotesExamined, LaneLookups, LaneMembers,
            DeadlinesQueued, Continuations;
    }
}
