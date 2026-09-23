using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace YARG.Core.Engine.Drums
{
public enum EliteFillRuntimeEventKind
{
    Pending,
    Commit,
    BarrierLatched,
    BarrierRecovered,
}

public sealed class EliteFillTraceEvent
{
    public EliteFillRuntimeEventKind Kind { get; }
    public double Timestamp { get; }
    public int PendingCount { get; }
    public int CommittedCount { get; }

    public EliteFillTraceEvent(EliteFillRuntimeEventKind kind, double timestamp, int pendingCount, int committedCount)
    {
        EliteFillParameterValidation.RequireTimestamp(timestamp);
        if (pendingCount < 0) throw new ArgumentOutOfRangeException(nameof(pendingCount));
        if (committedCount < 0) throw new ArgumentOutOfRangeException(nameof(committedCount));
        Kind = kind;
        Timestamp = timestamp;
        PendingCount = pendingCount;
        CommittedCount = committedCount;
    }
}

/// <summary>Immutable snapshot of the pure fill harness. It has no references to mutable chart notes.</summary>
public sealed class EliteFillRuntimeState
{
    public double Timestamp { get; }
    public EliteFillPhase Phase { get; }
    public bool BarrierLatched { get; }
    public int PendingCount { get; }
    public int CommittedCount { get; }
    public int BarrierGeneration { get; }
    public double? PendingCommitAt { get; }
    public EliteFillBarrierPhase BarrierPhase { get; }

    public EliteFillRuntimeState(
        double timestamp,
        EliteFillPhase phase,
        bool barrierLatched,
        int pendingCount,
        int committedCount,
        int barrierGeneration,
        double? pendingCommitAt = null,
        EliteFillBarrierPhase barrierPhase = EliteFillBarrierPhase.Clear)
    {
        EliteFillParameterValidation.RequireTimestamp(timestamp);
        if (pendingCount < 0) throw new ArgumentOutOfRangeException(nameof(pendingCount));
        if (committedCount < 0) throw new ArgumentOutOfRangeException(nameof(committedCount));
        if (barrierGeneration < 0) throw new ArgumentOutOfRangeException(nameof(barrierGeneration));
        if (pendingCommitAt.HasValue) EliteFillParameterValidation.RequireTimestamp(pendingCommitAt.Value);
        Timestamp = timestamp;
        Phase = phase;
        BarrierLatched = barrierLatched;
        PendingCount = pendingCount;
        CommittedCount = committedCount;
        BarrierGeneration = barrierGeneration;
        PendingCommitAt = pendingCommitAt;
        BarrierPhase = barrierPhase;
    }
}

public enum EliteFillPhase
{
    Outside,
    Entry,
    Grace,
    Committed,
}

/// <summary>
/// Deterministic fixed-timestamp state/trace harness. Submit is intentionally pending;
/// only Advance at or after the policy commit deadline commits it.
/// </summary>
public sealed class EliteFillRuntimeHarness
{
    private readonly EliteFillPolicyV1 _policy;
    private readonly List<EliteFillTraceEvent> _trace = new();
    private EliteFillRuntimeState _state;
    private EliteFillDeadline? _deadline;
    private EliteFillBarrierWindow? _barrier;

    public EliteFillRuntimeState State => _state;
    public IReadOnlyList<EliteFillTraceEvent> Trace => new ReadOnlyCollection<EliteFillTraceEvent>(_trace.ToArray());

    public EliteFillRuntimeHarness(EliteFillPolicyV1 policy, double initialTimestamp = 0)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _state = policy.InitialState(initialTimestamp);
    }

    public EliteFillRuntimeState Submit(double candidateTimestamp)
    {
        EliteFillParameterValidation.RequireTimestamp(candidateTimestamp);
        var deadline = _deadline ?? _policy.DeadlineAt(candidateTimestamp);
        _deadline = deadline;
        var phase = deadline.Classify(candidateTimestamp) switch
        {
            EliteFillWindow.Entry => EliteFillPhase.Entry,
            EliteFillWindow.Grace => EliteFillPhase.Grace,
            _ => EliteFillPhase.Outside,
        };
        _state = new EliteFillRuntimeState(candidateTimestamp, phase, _state.BarrierLatched,
            _state.PendingCount + 1, _state.CommittedCount, _state.BarrierGeneration,
            deadline.CommitAt, _state.BarrierPhase);
        _trace.Add(new EliteFillTraceEvent(EliteFillRuntimeEventKind.Pending, candidateTimestamp,
            _state.PendingCount, _state.CommittedCount));
        return _state;
    }

    public EliteFillRuntimeState Advance(double timestamp)
    {
        EliteFillParameterValidation.RequireTimestamp(timestamp);
        if (timestamp < _state.Timestamp)
            throw new ArgumentOutOfRangeException(nameof(timestamp), "The harness clock cannot move backwards.");

        var barrierPhase = _barrier?.At(timestamp) ?? EliteFillBarrierPhase.Clear;
        var barrierLatched = barrierPhase != EliteFillBarrierPhase.Clear;
        var generation = _state.BarrierGeneration;
        if (_state.BarrierLatched && !barrierLatched)
        {
            _trace.Add(new EliteFillTraceEvent(EliteFillRuntimeEventKind.BarrierRecovered,
                timestamp, _state.PendingCount, _state.CommittedCount));
        }

        var pending = _state.PendingCount;
        var committed = _state.CommittedCount;
        if (_deadline is not null && pending > 0 && _deadline.IsCommitDue(timestamp))
        {
            committed += pending;
            pending = 0;
            _trace.Add(new EliteFillTraceEvent(EliteFillRuntimeEventKind.Commit,
                timestamp, pending, committed));
        }

        var phase = pending > 0 && _deadline is not null
            ? _deadline.Classify(timestamp) switch
            {
                EliteFillWindow.Entry => EliteFillPhase.Entry,
                EliteFillWindow.Grace => EliteFillPhase.Grace,
                _ => EliteFillPhase.Outside,
            }
            : committed > 0 ? EliteFillPhase.Committed : EliteFillPhase.Outside;
        _state = new EliteFillRuntimeState(timestamp, phase, barrierLatched, pending, committed,
            generation, pending > 0 ? _deadline?.CommitAt : null, barrierPhase);
        return _state;
    }

    public EliteFillRuntimeState LatchBarrier(double timestamp)
    {
        EliteFillParameterValidation.RequireTimestamp(timestamp);
        _barrier = EliteFillBarrierCalculator.Calculate(timestamp, _policy.Parameters);
        _state = Advance(timestamp);
        _state = new EliteFillRuntimeState(_state.Timestamp, _state.Phase, true,
            _state.PendingCount, _state.CommittedCount, _state.BarrierGeneration + 1,
            _state.PendingCommitAt, _state.BarrierPhase);
        _trace.Add(new EliteFillTraceEvent(EliteFillRuntimeEventKind.BarrierLatched,
            timestamp, _state.PendingCount, _state.CommittedCount));
        return _state;
    }
}
}
