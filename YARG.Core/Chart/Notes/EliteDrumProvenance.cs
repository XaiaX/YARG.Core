using System;
using System.Collections.Generic;
using System.Linq;

namespace YARG.Core.Chart
{
    /// <summary>
    /// Immutable identity and timing for one authored Elite Drums source gem.
    /// Source IDs are not required to be unique; PhysicalOrdinal is the stable
    /// occurrence identity used when a chart repeats an ID.
    /// </summary>
    public sealed class EliteDrumSourceDefinition : IEquatable<EliteDrumSourceDefinition>
    {
        public string SourceId { get; }
        public string Id => SourceId;
        public int PhysicalOrdinal { get; }
        public int Ordinal => PhysicalOrdinal;
        public int Pad { get; }
        public uint StartTick { get; }
        public uint EndTick { get; }
        public NoteFlags Flags { get; }
        public bool IsCodaEnd => (Flags & NoteFlags.CodaEnd) != 0;
        public double StartTime { get; }
        public double EndTime { get; }

        public EliteDrumSourceDefinition(string sourceId, int physicalOrdinal, int pad,
            uint startTick, uint tickLength, double startTime = 0, double timeLength = 0)
        {
            SourceId = sourceId ?? throw new ArgumentNullException(nameof(sourceId));
            if (physicalOrdinal < 0) throw new ArgumentOutOfRangeException(nameof(physicalOrdinal));
            if (tickLength > uint.MaxValue - startTick) throw new ArgumentOutOfRangeException(nameof(tickLength));
            PhysicalOrdinal = physicalOrdinal;
            Pad = pad;
            StartTick = startTick;
            EndTick = startTick + tickLength;
            StartTime = startTime;
            EndTime = startTime + timeLength;
        }

        public bool Equals(EliteDrumSourceDefinition? other)
            => other is not null && SourceId == other.SourceId && PhysicalOrdinal == other.PhysicalOrdinal;
        public override bool Equals(object? obj) => Equals(obj as EliteDrumSourceDefinition);
        public override int GetHashCode() => HashCode.Combine(SourceId, PhysicalOrdinal);
    }

    /// <summary>One immutable emitted occurrence of a source, including flam expansion.</summary>
    public sealed class EliteDrumConversionOrigin : IEquatable<EliteDrumConversionOrigin>
    {
        public EliteDrumSourceDefinition Source { get; }
        public EliteDrumSourceDefinition Definition => Source;
        public int ExpansionOrdinal { get; }
        public bool IsFlamExpansion => ExpansionOrdinal != 0;

        public EliteDrumConversionOrigin(EliteDrumSourceDefinition source, int expansionOrdinal = 0)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            if (expansionOrdinal < 0) throw new ArgumentOutOfRangeException(nameof(expansionOrdinal));
            ExpansionOrdinal = expansionOrdinal;
        }

        public bool Equals(EliteDrumConversionOrigin? other)
            => other is not null && ExpansionOrdinal == other.ExpansionOrdinal && Source.Equals(other.Source);
        public override bool Equals(object? obj) => Equals(obj as EliteDrumConversionOrigin);
        public override int GetHashCode() => HashCode.Combine(Source, ExpansionOrdinal);
    }

    /// <summary>Immutable membership of an emitted origin in a final target pad interval.</summary>
    public sealed class EliteDrumSourceMembership
    {
        public EliteDrumConversionOrigin Origin { get; }
        public EliteDrumFinalPadIdentity Target { get; }
        public uint StartTick { get; }
        public uint EndTick { get; }
        public bool IsEmpty => StartTick == EndTick;

        public EliteDrumSourceMembership(EliteDrumConversionOrigin origin, EliteDrumFinalPadIdentity target,
            uint startTick, uint endTick)
        {
            Origin = origin ?? throw new ArgumentNullException(nameof(origin));
            if (endTick < startTick) throw new ArgumentOutOfRangeException(nameof(endTick));
            Target = target;
            StartTick = startTick;
            EndTick = endTick;
        }

        /// <summary>Membership is half-open: start is included and end is excluded.</summary>
        public bool Contains(uint tick) => StartTick <= tick && tick < EndTick;

        /// <summary>Two intervals overlap strictly; touching endpoints do not overlap.</summary>
        public bool StrictlyOverlaps(EliteDrumSourceMembership other)
            => StartTick < other.EndTick && other.StartTick < EndTick;
    }

    /// <summary>Explicit resolution state of a final output pad identity.</summary>
    public enum EliteDrumFinalPadResolution
    {
        /// <summary>The final output identity has not been computed yet.</summary>
        Unresolved = 0,
        /// <summary>The identity names a concrete final output pad.</summary>
        Resolved = 1,
    }

    public readonly struct EliteDrumFinalPadIdentity : IEquatable<EliteDrumFinalPadIdentity>
    {
        /// <summary>
        /// Pad value carried by identities whose final output pad has not been computed.
        /// The pad value is never a resolution signal: 0 is also the valid final Kick pad
        /// (<see cref="FourLaneDrumPad.Kick"/> / <see cref="FiveLaneDrumPad.Kick"/>).
        /// Consumers must gate on the explicit <see cref="Resolution"/> state, and
        /// published descriptor producers must never emit unresolved identities.
        /// </summary>
        public const int UnresolvedPad = 0;

        public Instrument Instrument { get; }
        public int Pad { get; }
        public EliteDrumFinalPadResolution Resolution { get; }

        /// <summary>Creates a resolved identity naming a concrete final output pad.</summary>
        public EliteDrumFinalPadIdentity(Instrument instrument, int pad)
        {
            Instrument = instrument;
            Pad = pad;
            Resolution = EliteDrumFinalPadResolution.Resolved;
        }

        private EliteDrumFinalPadIdentity(Instrument instrument, int pad,
            EliteDrumFinalPadResolution resolution)
        {
            Instrument = instrument;
            Pad = pad;
            Resolution = resolution;
        }

        /// <summary>Canonical unresolved identity: no instrument context and no final pad. <see langword="default"/> is unresolved.</summary>
        public static EliteDrumFinalPadIdentity Unresolved => default;

        /// <summary>
        /// Unresolved identity that retains the owning instrument for diagnostics. The
        /// resolution state is explicit, so this never compares equal to a resolved
        /// identity — including a resolved identity with the same pad value.
        /// </summary>
        public static EliteDrumFinalPadIdentity UnresolvedFor(Instrument instrument)
            => new(instrument, UnresolvedPad, EliteDrumFinalPadResolution.Unresolved);

        /// <summary>Whether this identity names a concrete final output pad. Never inferred from the pad value.</summary>
        public bool IsResolved => Resolution == EliteDrumFinalPadResolution.Resolved;

        /// <summary>Whether the final output identity has not been computed yet.</summary>
        public bool IsUnresolved => !IsResolved;

        public bool Equals(EliteDrumFinalPadIdentity other)
            => Instrument == other.Instrument && Pad == other.Pad && Resolution == other.Resolution;
        public override bool Equals(object? obj) => obj is EliteDrumFinalPadIdentity other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Instrument, Pad, Resolution);
        public override string ToString() => IsResolved
            ? $"{Instrument} pad {Pad}"
            : $"{Instrument} unresolved";
    }

    public sealed class EliteDrumFinalPadComponent
    {
        private readonly EliteDrumConversionOrigin[] _origins;
        public EliteDrumFinalPadIdentity Target { get; }
        public uint StartTick { get; }
        public uint EndTick { get; }
        public IReadOnlyList<EliteDrumConversionOrigin> Origins => _origins;

        internal EliteDrumFinalPadComponent(EliteDrumFinalPadIdentity target, uint startTick, uint endTick,
            IEnumerable<EliteDrumConversionOrigin> origins)
        {
            Target = target;
            StartTick = startTick;
            EndTick = endTick;
            _origins = origins.Distinct().ToArray();
        }
    }

    public sealed class EliteDrumDroppedOrigin
    {
        public EliteDrumConversionOrigin Origin { get; }
        public string Reason { get; }

        public EliteDrumDroppedOrigin(EliteDrumConversionOrigin origin, string reason)
        {
            Origin = origin ?? throw new ArgumentNullException(nameof(origin));
            Reason = reason ?? throw new ArgumentNullException(nameof(reason));
        }
    }

    /// <summary>
    /// One authored Elite hand-lane phrase instance (Snare/Tom/Cymbal lanes) with the
    /// explicit membership of authored source gems that were authored inside its
    /// interval. Membership is computed at downchart-conversion time from the authored
    /// track; it is never inferred from final pads and never includes nearby ordinary
    /// same-pad notes. Kick and hat-pedal pads are excluded (V1).
    /// </summary>
    public sealed class EliteDrumAuthoredLanePhrase
    {
        private readonly EliteDrumSourceDefinition[] _memberSources;

        public PhraseType LaneType { get; }
        public uint StartTick { get; }
        public uint EndTick { get; }
        /// <summary>Authored gems inside the phrase interval; every conversion expansion matches.</summary>
        public IReadOnlyList<EliteDrumSourceDefinition> MemberSources => _memberSources;

        public EliteDrumAuthoredLanePhrase(PhraseType laneType, uint startTick, uint endTick,
            IEnumerable<EliteDrumSourceDefinition> memberSources)
        {
            if (endTick < startTick) throw new ArgumentOutOfRangeException(nameof(endTick));
            LaneType = laneType;
            StartTick = startTick;
            EndTick = endTick;
            _memberSources = (memberSources ?? throw new ArgumentNullException(nameof(memberSources))).Distinct().ToArray();
        }

        /// <summary>The phrase interval is half-open: start included, end excluded.</summary>
        public bool Contains(uint tick) => StartTick <= tick && tick < EndTick;

        /// <summary>Whether an emitted conversion occurrence belongs to this authored phrase instance.</summary>
        public bool ContainsOrigin(EliteDrumConversionOrigin? origin) =>
            origin is not null && Array.IndexOf(_memberSources, origin.Source) >= 0;
    }

    /// <summary>Authored Elite hand-lane phrase types that participate in V1 lanes.</summary>
    public static class EliteDrumAuthoredLanePhraseTypes
    {
        /// <summary>Surviving final physical members an authored hand lane needs to be valid.</summary>
        public const int MinimumSurvivingMembers = 3;

        public static bool IsAuthoredHandLane(PhraseType type) => type switch
        {
            PhraseType.EliteDrums_SnareLane or PhraseType.EliteDrums_HiHatLane or
            PhraseType.EliteDrums_LeftCrashLane or PhraseType.EliteDrums_Tom1Lane or
            PhraseType.EliteDrums_Tom2Lane or PhraseType.EliteDrums_Tom3Lane or
            PhraseType.EliteDrums_RideLane or PhraseType.EliteDrums_RightCrashLane => true,
            // Kick and hat-pedal lanes are excluded from V1.
            _ => false,
        };

        /// <summary>Authored Elite pads that may belong to a V1 hand lane.</summary>
        public static bool IsHandPad(int pad) => pad is not (int)EliteDrumNote.EliteDrumPad.Kick
            and not (int)EliteDrumNote.EliteDrumPad.HatPedal;

        /// <summary>Authored lane identity of a hand-lane phrase, for V1 diagnostics/visuals.</summary>
        public static EliteDrumNote.EliteDrumPad ToAuthoredPad(PhraseType type) => type switch
        {
            PhraseType.EliteDrums_SnareLane => EliteDrumNote.EliteDrumPad.Snare,
            PhraseType.EliteDrums_HiHatLane => EliteDrumNote.EliteDrumPad.HiHat,
            PhraseType.EliteDrums_LeftCrashLane => EliteDrumNote.EliteDrumPad.LeftCrash,
            PhraseType.EliteDrums_Tom1Lane => EliteDrumNote.EliteDrumPad.Tom1,
            PhraseType.EliteDrums_Tom2Lane => EliteDrumNote.EliteDrumPad.Tom2,
            PhraseType.EliteDrums_Tom3Lane => EliteDrumNote.EliteDrumPad.Tom3,
            PhraseType.EliteDrums_RideLane => EliteDrumNote.EliteDrumPad.Ride,
            PhraseType.EliteDrums_RightCrashLane => EliteDrumNote.EliteDrumPad.RightCrash,
            _ => throw new ArgumentOutOfRangeException(nameof(type), "Not an authored hand-lane phrase type."),
        };
    }

    /// <summary>Stable, immutable conversion record consumed by selectors/gameplay diagnostics.</summary>
    public sealed class EliteDrumConversionLedger
    {
        private readonly EliteDrumFinalPadComponent[] _components;
        private readonly EliteDrumDroppedOrigin[] _droppedOrigins;
        private readonly EliteDrumAuthoredLanePhrase[] _authoredLanePhrases;
        public IReadOnlyList<EliteDrumFinalPadComponent> Components => _components;
        public IReadOnlyList<EliteDrumDroppedOrigin> DroppedOrigins => _droppedOrigins;
        /// <summary>Authored hand-lane phrase instances recorded during downchart conversion.</summary>
        public IReadOnlyList<EliteDrumAuthoredLanePhrase> AuthoredLanePhrases => _authoredLanePhrases;

        public EliteDrumConversionLedger(IEnumerable<EliteDrumFinalPadComponent> components,
            IEnumerable<EliteDrumDroppedOrigin> droppedOrigins,
            IEnumerable<EliteDrumAuthoredLanePhrase>? authoredLanePhrases = null)
        {
            _components = components?.ToArray() ?? throw new ArgumentNullException(nameof(components));
            _droppedOrigins = droppedOrigins?.ToArray() ?? throw new ArgumentNullException(nameof(droppedOrigins));
            _authoredLanePhrases = authoredLanePhrases?.ToArray() ?? Array.Empty<EliteDrumAuthoredLanePhrase>();
        }
    }

    /// <summary>Pure final-pad component construction. It never mutates source or membership objects.</summary>
    public static class EliteDrumFinalPadComponentBuilder
    {
        public static IReadOnlyList<EliteDrumFinalPadComponent> Build(
            IEnumerable<EliteDrumSourceMembership> memberships)
        {
            if (memberships is null) throw new ArgumentNullException(nameof(memberships));
            var result = new List<EliteDrumFinalPadComponent>();
            foreach (var targetGroup in memberships.Where(m => !m.IsEmpty).GroupBy(m => m.Target))
            {
                var ordered = targetGroup.OrderBy(m => m.StartTick).ThenBy(m => m.EndTick).ToList();
                var origins = new List<EliteDrumConversionOrigin>();
                var start = 0u;
                var end = 0u;
                var first = true;
                foreach (var membership in ordered)
                {
                    if (first || membership.StartTick >= end)
                    {
                        if (!first) result.Add(new(targetGroup.Key, start, end, origins));
                        first = false;
                        start = membership.StartTick;
                        end = membership.EndTick;
                        origins = new();
                    }
                    else
                    {
                        end = Math.Max(end, membership.EndTick);
                    }
                    origins.Add(membership.Origin);
                }
                if (!first) result.Add(new(targetGroup.Key, start, end, origins));
            }
            return result.OrderBy(c => c.StartTick).ThenBy(c => c.Target.Instrument).ThenBy(c => c.Target.Pad).ToArray();
        }
    }
}
