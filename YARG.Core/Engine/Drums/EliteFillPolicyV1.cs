using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace YARG.Core.Engine.Drums
{
/// <summary>
/// The deliberately small, Core-only contract for the first native Elite Drums fill
/// policy. It contains no chart, scheduler, score, replay, or Unity concepts.
/// </summary>
public sealed class EliteFillPolicyV1
{
    public const int VERSION = 1;

    public static EliteFillPolicyV1 Default { get; } = new(new EliteFillPolicyParameters());

    public EliteFillPolicyParameters Parameters { get; }

    public EliteFillPolicyV1(EliteFillPolicyParameters parameters)
    {
        Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        Parameters.Validate().ThrowIfInvalid();
    }

    public EliteFillDeadline DeadlineAt(double timestamp)
    {
        return EliteFillDeadlineCalculator.Calculate(timestamp, Parameters);
    }

    public EliteFillRuntimeState InitialState(double timestamp = 0)
    {
        EliteFillParameterValidation.RequireTimestamp(timestamp);
        return new EliteFillRuntimeState(timestamp, EliteFillPhase.Outside, false, 0, 0, 0);
    }
}

/// <summary>
/// Immutable numeric policy knobs. Every member is a duration, window, interval, or
/// step in seconds — never a chart timestamp — and must be finite and non-negative
/// (latch/recovery/cadence windows strictly positive). Chart and engine-clock
/// timestamps passed to the policy are signed coordinates and are validated by
/// <see cref="EliteFillParameterValidation.RequireTimestamp"/> instead.
/// </summary>
public sealed class EliteFillPolicyParameters
{
    public double EntryWindowSeconds { get; }
    public double GraceWindowSeconds { get; }
    public double PendingCommitDelaySeconds { get; }
    public double GroupingToleranceSeconds { get; }
    public double BarrierLatchSeconds { get; }
    public double BarrierRecoverySeconds { get; }
    public double CadenceStepSeconds { get; }

    public EliteFillPolicyParameters(
        double entryWindowSeconds = 0.050,
        double graceWindowSeconds = 0.050,
        double pendingCommitDelaySeconds = 0.025,
        double groupingToleranceSeconds = 0.001,
        double barrierLatchSeconds = 0.250,
        double barrierRecoverySeconds = 0.500,
        double cadenceStepSeconds = 0.100)
    {
        EntryWindowSeconds = entryWindowSeconds;
        GraceWindowSeconds = graceWindowSeconds;
        PendingCommitDelaySeconds = pendingCommitDelaySeconds;
        GroupingToleranceSeconds = groupingToleranceSeconds;
        BarrierLatchSeconds = barrierLatchSeconds;
        BarrierRecoverySeconds = barrierRecoverySeconds;
        CadenceStepSeconds = cadenceStepSeconds;
    }

    public EliteFillValidationResult Validate()
    {
        var errors = new List<string>();
        AddNonNegativeFinite(errors, nameof(EntryWindowSeconds), EntryWindowSeconds);
        AddNonNegativeFinite(errors, nameof(GraceWindowSeconds), GraceWindowSeconds);
        AddNonNegativeFinite(errors, nameof(PendingCommitDelaySeconds), PendingCommitDelaySeconds);
        AddNonNegativeFinite(errors, nameof(GroupingToleranceSeconds), GroupingToleranceSeconds);
        AddPositiveFinite(errors, nameof(BarrierLatchSeconds), BarrierLatchSeconds);
        AddPositiveFinite(errors, nameof(BarrierRecoverySeconds), BarrierRecoverySeconds);
        AddPositiveFinite(errors, nameof(CadenceStepSeconds), CadenceStepSeconds);
        return new EliteFillValidationResult(errors);
    }

    private static void AddNonNegativeFinite(List<string> errors, string name, double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0) errors.Add(name);
    }

    private static void AddPositiveFinite(List<string> errors, string name, double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0) errors.Add(name);
    }
}

public sealed class EliteFillValidationResult
{
    private readonly ReadOnlyCollection<string> _errors;

    public bool IsValid => _errors.Count == 0;
    public IReadOnlyList<string> Errors => _errors;

    public EliteFillValidationResult(IEnumerable<string> errors)
    {
        _errors = new ReadOnlyCollection<string>((errors ?? throw new ArgumentNullException(nameof(errors))).ToArray());
    }

    public void ThrowIfInvalid()
    {
        if (!IsValid) throw new ArgumentException("Invalid Elite Fill V1 parameters: " + string.Join(", ", Errors));
    }
}

public enum EliteFillWindow
{
    Outside,
    Entry,
    Grace,
}

public sealed class EliteFillDeadline
{
    public double AnchorTimestamp { get; }
    public double EntryUntil { get; }
    public double GraceUntil { get; }
    public double CommitAt { get; }

    /// <summary>
    /// Tests whether a fixed timestamp reaches the deadline. The tiny tolerance only
    /// compensates for decimal timestamps represented as binary doubles; it does not
    /// move a meaningful event across a policy window.
    /// </summary>
    public bool IsCommitDue(double timestamp)
    {
        EliteFillParameterValidation.RequireTimestamp(timestamp);
        return timestamp >= CommitAt || Math.Abs(timestamp - CommitAt) <= 1e-12;
    }

    public EliteFillDeadline(double anchorTimestamp, double entryUntil, double graceUntil, double commitAt)
    {
        AnchorTimestamp = anchorTimestamp;
        EntryUntil = entryUntil;
        GraceUntil = graceUntil;
        CommitAt = commitAt;
    }

    public EliteFillWindow Classify(double timestamp)
    {
        EliteFillParameterValidation.RequireTimestamp(timestamp);
        if (timestamp <= EntryUntil) return EliteFillWindow.Entry;
        if (timestamp <= GraceUntil) return EliteFillWindow.Grace;
        return EliteFillWindow.Outside;
    }
}

public static class EliteFillDeadlineCalculator
{
    public static EliteFillDeadline Calculate(double anchorTimestamp, EliteFillPolicyParameters parameters)
    {
        EliteFillParameterValidation.RequireTimestamp(anchorTimestamp);
        if (parameters is null) throw new ArgumentNullException(nameof(parameters));
        parameters.Validate().ThrowIfInvalid();
        var entryUntil = anchorTimestamp + parameters.EntryWindowSeconds;
        var graceUntil = entryUntil + parameters.GraceWindowSeconds;
        return new EliteFillDeadline(anchorTimestamp, entryUntil, graceUntil,
            graceUntil + parameters.PendingCommitDelaySeconds);
    }
}

public enum EliteCadenceSymbol
{
    Green,
    Yellow,
    Blue,
}

public sealed class EliteCadencePattern
{
    private readonly ReadOnlyCollection<EliteCadenceSymbol> _symbols;
    public string Name { get; }
    public IReadOnlyList<EliteCadenceSymbol> Symbols => _symbols;

    public EliteCadencePattern(string name, IEnumerable<EliteCadenceSymbol> symbols)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A cadence name is required.", nameof(name));
        Name = name;
        var copy = (symbols ?? throw new ArgumentNullException(nameof(symbols))).ToArray();
        if (copy.Length == 0) throw new ArgumentException("A cadence must contain a symbol.", nameof(symbols));
        _symbols = new ReadOnlyCollection<EliteCadenceSymbol>(copy);
    }

    public static EliteCadencePattern GYB { get; } = new("GYB", new[]
    {
        EliteCadenceSymbol.Green, EliteCadenceSymbol.Yellow, EliteCadenceSymbol.Blue,
    });

    public static EliteCadencePattern GYYB { get; } = new("GYYB", new[]
    {
        EliteCadenceSymbol.Green, EliteCadenceSymbol.Yellow, EliteCadenceSymbol.Yellow, EliteCadenceSymbol.Blue,
    });
}

public sealed class EliteCadencePoint
{
    public EliteCadenceSymbol Symbol { get; }
    public int Index { get; }
    public double Timestamp { get; }

    public EliteCadencePoint(EliteCadenceSymbol symbol, int index, double timestamp)
    {
        Symbol = symbol;
        Index = index;
        Timestamp = timestamp;
    }
}

public static class EliteFillCadenceCalculator
{
    public static IReadOnlyList<EliteCadencePoint> Calculate(
        EliteCadencePattern pattern, double startTimestamp, double stepSeconds)
    {
        if (pattern is null) throw new ArgumentNullException(nameof(pattern));
        EliteFillParameterValidation.RequireTimestamp(startTimestamp);
        EliteFillParameterValidation.RequirePositiveFinite(stepSeconds, nameof(stepSeconds));
        var result = new List<EliteCadencePoint>(pattern.Symbols.Count);
        for (var i = 0; i < pattern.Symbols.Count; i++)
            result.Add(new EliteCadencePoint(pattern.Symbols[i], i, startTimestamp + (stepSeconds * i)));
        return new ReadOnlyCollection<EliteCadencePoint>(result);
    }
}

public sealed class EliteFillCandidate
{
    public int Id { get; }
    public double Timestamp { get; }
    public EliteCadenceSymbol Symbol { get; }

    public EliteFillCandidate(int id, double timestamp, EliteCadenceSymbol symbol)
    {
        EliteFillParameterValidation.RequireTimestamp(timestamp);
        Id = id;
        Timestamp = timestamp;
        Symbol = symbol;
    }
}

public sealed class EliteFillTimestampGroup
{
    private readonly ReadOnlyCollection<EliteFillCandidate> _members;
    public double Timestamp { get; }
    public IReadOnlyList<EliteFillCandidate> Members => _members;

    public EliteFillTimestampGroup(double timestamp, IEnumerable<EliteFillCandidate> members)
    {
        Timestamp = timestamp;
        _members = new ReadOnlyCollection<EliteFillCandidate>((members ?? throw new ArgumentNullException(nameof(members))).ToArray());
    }
}

public static class EliteFillTimeGrouping
{
    public static bool AreEqual(double left, double right, double toleranceSeconds)
    {
        EliteFillParameterValidation.RequireTimestamp(left);
        EliteFillParameterValidation.RequireTimestamp(right);
        EliteFillParameterValidation.RequireNonNegativeFinite(toleranceSeconds, nameof(toleranceSeconds));
        return Math.Abs(left - right) <= toleranceSeconds;
    }

    public static IReadOnlyList<EliteFillTimestampGroup> Group(
        IEnumerable<EliteFillCandidate> candidates, double toleranceSeconds)
    {
        EliteFillParameterValidation.RequireNonNegativeFinite(toleranceSeconds, nameof(toleranceSeconds));
        var ordered = (candidates ?? throw new ArgumentNullException(nameof(candidates)))
            .OrderBy(candidate => candidate.Timestamp).ThenBy(candidate => candidate.Id).ToArray();
        var groups = new List<EliteFillTimestampGroup>();
        foreach (var candidate in ordered)
        {
            if (groups.Count == 0 || !AreEqual(groups[^1].Timestamp, candidate.Timestamp, toleranceSeconds))
            {
                groups.Add(new EliteFillTimestampGroup(candidate.Timestamp, new[] { candidate }));
                continue;
            }

            var old = groups[^1].Members.ToList();
            old.Add(candidate);
            groups[^1] = new EliteFillTimestampGroup(groups[^1].Timestamp, old);
        }
        return new ReadOnlyCollection<EliteFillTimestampGroup>(groups);
    }
}

public enum EliteFillBarrierPhase
{
    Clear,
    Latched,
    Recovering,
}

public sealed class EliteFillBarrierWindow
{
    public double TriggerTimestamp { get; }
    public double LatchedUntil { get; }
    public double RecoveredAt { get; }

    public EliteFillBarrierWindow(double triggerTimestamp, double latchedUntil, double recoveredAt)
    {
        TriggerTimestamp = triggerTimestamp;
        LatchedUntil = latchedUntil;
        RecoveredAt = recoveredAt;
    }

    public EliteFillBarrierPhase At(double timestamp)
    {
        EliteFillParameterValidation.RequireTimestamp(timestamp);
        if (timestamp < LatchedUntil) return EliteFillBarrierPhase.Latched;
        if (timestamp < RecoveredAt) return EliteFillBarrierPhase.Recovering;
        return EliteFillBarrierPhase.Clear;
    }
}

public static class EliteFillBarrierCalculator
{
    public static EliteFillBarrierWindow Calculate(double triggerTimestamp, EliteFillPolicyParameters parameters)
    {
        EliteFillParameterValidation.RequireTimestamp(triggerTimestamp);
        if (parameters is null) throw new ArgumentNullException(nameof(parameters));
        parameters.Validate().ThrowIfInvalid();
        return new EliteFillBarrierWindow(triggerTimestamp,
            triggerTimestamp + parameters.BarrierLatchSeconds,
            triggerTimestamp + parameters.BarrierLatchSeconds + parameters.BarrierRecoverySeconds);
    }
}

public static class EliteFillParameterValidation
{
    /// <summary>
    /// Validates a chart/pre-song coordinate timestamp or an engine clock reading.
    /// Timestamps are signed seconds: negative values are legitimate chart coordinates
    /// (a chart delay/offset or lead-in can place notes and the engine clock before the
    /// zero anchor), so only non-finite values are rejected. Durations, windows,
    /// intervals, steps, and tolerances are not timestamps; they keep using
    /// <see cref="RequireNonNegativeFinite"/>/<see cref="RequirePositiveFinite"/>.
    /// </summary>
    public static void RequireTimestamp(double timestamp)
    {
        if (double.IsNaN(timestamp) || double.IsInfinity(timestamp))
            throw new ArgumentOutOfRangeException(nameof(timestamp), timestamp,
                "Must be a finite timestamp in signed seconds (negative pre-song values are valid chart coordinates).");
    }

    /// <summary>Validates a strictly positive duration, step, or window parameter.</summary>
    public static void RequirePositiveFinite(double value, string name)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
            throw new ArgumentOutOfRangeException(name, value, "Must be finite and positive.");
    }

    /// <summary>Validates a non-negative duration, window, interval, or tolerance parameter.</summary>
    public static void RequireNonNegativeFinite(double value, string name)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
            throw new ArgumentOutOfRangeException(name, value, "Must be finite and non-negative.");
    }
}
}
