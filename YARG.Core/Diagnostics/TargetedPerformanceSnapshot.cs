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
        PoolCreate,
        NoteSpawn,
        ElementEnable,
        ElementSetActive,
        ElementInitialize,
        ElementFirstVisualUpdate,
        NoteGroupInitialize,
        MaterialAccess,
        NoteGroupSetActive,
        PedalMeshBuild,
        GameplayAwake,
        GameplayManagerDiscovery,
        EngineUpdate,
        EliteDrumHit,
        ClassicDrumHit,
        DrumHitBookkeeping,
        DrumHitGemReturn,
        EliteHitFeedback,
        ClassicHitFeedback,
        FretHitAnimator,
        FretHitColor,
        FretHitEffect,
        HitParticlePlay,
        HitLightPlay,
        GameManagerUpdate,
        SongRunnerUpdate,
        AudioSynchronize,
        AudioClockQuery,
        AudioClockCorrection,
        GameplaySpeedUpdate,
        GameplayBeatEvents,
        GameplayCrowdEvents,
        GameplayPlayerUpdate,
        GameplayScoreUpdate,
        GameplaySongEnd,
        AudioClockMixerLock,
        AudioClockNativeQuery,
        AudioClockBassConvert
    }

    public struct TargetedTiming
    {
        public long Calls, ElapsedTicks, MaxTicks, MaxStartTicks;
        public int MaxContext;

        internal void Add(long ticks, long start, int context)
        {
            Calls++;
            ElapsedTicks += ticks;
            if (Calls == 1 || ticks > MaxTicks)
            {
                MaxTicks = ticks;
                MaxStartTicks = start;
                MaxContext = context;
            }
        }
    }

    public struct AudioClockNativeTiming
    {
        public long Samples, Attempts, CallbackYields, InvalidatedAttempts;
        public long SourceLockNs, CallbackBusyNs, InvalidatedNs, BassDecodePositionNs, BassHeardPositionNs;

        internal void Add(in AudioClockNativeTiming sample)
        {
            Samples += sample.Samples;
            Attempts += sample.Attempts;
            CallbackYields += sample.CallbackYields;
            InvalidatedAttempts += sample.InvalidatedAttempts;
            SourceLockNs += sample.SourceLockNs;
            CallbackBusyNs += sample.CallbackBusyNs;
            InvalidatedNs += sample.InvalidatedNs;
            BassDecodePositionNs += sample.BassDecodePositionNs;
            BassHeardPositionNs += sample.BassHeardPositionNs;
        }
    }

    /// <summary>A value copy of one sampling interval; timings are inclusive and can overlap.</summary>
    public struct TargetedPerformanceSnapshot
    {
        public bool Enabled;
        public long Generation;
        public TargetedTiming RollSchedule, RollResolve, LaneMapBuild, HiHatActivation, HiHatDiscovery,
            PoolPrewarm, PoolCreate,
            NoteSpawn, ElementEnable, ElementSetActive, ElementInitialize, ElementFirstVisualUpdate, NoteGroupInitialize, MaterialAccess, NoteGroupSetActive, PedalMeshBuild, GameplayAwake, GameplayManagerDiscovery;
        public TargetedTiming EngineUpdate, EliteDrumHit, ClassicDrumHit, DrumHitBookkeeping, DrumHitGemReturn,
            EliteHitFeedback, ClassicHitFeedback, FretHitAnimator, FretHitColor, FretHitEffect, HitParticlePlay, HitLightPlay;
        public TargetedTiming GameManagerUpdate, SongRunnerUpdate, AudioSynchronize, AudioClockQuery,
            AudioClockCorrection, GameplaySpeedUpdate, GameplayBeatEvents, GameplayCrowdEvents,
            GameplayPlayerUpdate, GameplayScoreUpdate, GameplaySongEnd;
        public TargetedTiming AudioClockMixerLock, AudioClockNativeQuery, AudioClockBassConvert;
        public AudioClockNativeTiming AudioClockNative;
        public long ScheduleNotesExamined, ResolveNotesExamined, LaneLookups, LaneMembers,
            DeadlinesQueued, Continuations, FirstElementEnables, FirstNoteGroupInitializations;
    }
}
