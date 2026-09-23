using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using YARG.Core.Chart;
using YARG.Core.Logging;

namespace YARG.Core.Engine.Drums
{
public enum EliteDrumComponentKind
{
    Drum,
    Cymbal,
    Kick,
    Pedal,
}

/// <summary>
/// Immutable visual/gameplay identity for one final physical generated event.
/// The record is intentionally detached from mutable DrumNote hit state.
/// </summary>
public sealed class EliteDrumVisualDescriptorV1
{
    private readonly ReadOnlyCollection<string> _sourceIds;
    private readonly ReadOnlyCollection<EliteDrumConversionOrigin> _origins;

    public string GameplayId { get; }
    public IReadOnlyList<string> SourceIds => _sourceIds;
    public IReadOnlyList<EliteDrumConversionOrigin> Origins => _origins;
    /// <summary>
    /// Final output pad identity. Stage 2 resolves authored hand-lane phrase records
    /// from their surviving final DrumNote children, so every published descriptor
    /// carries a resolved identity. Records that cannot be resolved (mixed final pads
    /// without lane-supporting membership) keep the explicit unresolved state
    /// (<see cref="EliteDrumFinalPadIdentity.UnresolvedFor"/>) and are never published.
    /// The identity's resolution state is explicit: consumers must gate on
    /// <see cref="IsFinalIdentityResolved"/>, never on the pad value, because pad 0 is a
    /// valid resolved Kick final pad.
    /// </summary>
    public EliteDrumFinalPadIdentity FinalPad { get; }
    /// <summary>
    /// Explicit descriptor eligibility for the final-pad contract: true only when
    /// <see cref="FinalPad"/> carries the resolved state. Published descriptor output
    /// only ever contains descriptors whose final identity is resolved; consumers that
    /// cannot read this property are protected by producers never publishing unresolved
    /// records.
    /// </summary>
    public bool IsFinalIdentityResolved => FinalPad.IsResolved;
    public EliteDrumComponentVisualDescriptor VisualIdentity { get; }
    public uint AuthoredStartTick { get; }
    public uint AuthoredEndTick { get; }
    public double AuthoredStartTime { get; }
    public double AuthoredEndTime { get; }
    public double FirstPhysicalEventTime { get; }
    public double LastPhysicalEventTime { get; }
    public bool RulesetEligible { get; }
    public bool CodaExcluded { get; }

    public EliteDrumVisualDescriptorV1(string gameplayId, IEnumerable<string> sourceIds,
        IEnumerable<EliteDrumConversionOrigin> origins, EliteDrumFinalPadIdentity finalPad,
        EliteDrumComponentVisualDescriptor visualIdentity, uint authoredStartTick,
        uint authoredEndTick, double authoredStartTime, double authoredEndTime,
        double firstPhysicalEventTime, double lastPhysicalEventTime, bool rulesetEligible,
        bool codaExcluded)
    {
        if (string.IsNullOrWhiteSpace(gameplayId)) throw new ArgumentException("A gameplay ID is required.", nameof(gameplayId));
        if (authoredEndTick < authoredStartTick) throw new ArgumentOutOfRangeException(nameof(authoredEndTick));
        GameplayId = gameplayId;
        _sourceIds = new ReadOnlyCollection<string>((sourceIds ?? throw new ArgumentNullException(nameof(sourceIds))).ToArray());
        _origins = new ReadOnlyCollection<EliteDrumConversionOrigin>((origins ?? throw new ArgumentNullException(nameof(origins))).ToArray());
        FinalPad = finalPad;
        VisualIdentity = visualIdentity ?? throw new ArgumentNullException(nameof(visualIdentity));
        AuthoredStartTick = authoredStartTick;
        AuthoredEndTick = authoredEndTick;
        AuthoredStartTime = authoredStartTime;
        AuthoredEndTime = authoredEndTime;
        FirstPhysicalEventTime = firstPhysicalEventTime;
        LastPhysicalEventTime = lastPhysicalEventTime;
        RulesetEligible = rulesetEligible;
        CodaExcluded = codaExcluded;
    }
}

/// <summary>Builds final descriptors from the immutable conversion ledger and physical notes.</summary>
public static class EliteDrumVisualDescriptorV1Builder
{
    public static IReadOnlyList<EliteDrumVisualDescriptorV1> Build(
        InstrumentDifficulty<DrumNote> difficulty, EliteDrumConversionLedger ledger,
        Func<EliteDrumFinalPadIdentity, EliteDrumComponentVisualDescriptor>? visualIdentity = null)
    {
        if (difficulty is null) throw new ArgumentNullException(nameof(difficulty));
        if (ledger is null) throw new ArgumentNullException(nameof(ledger));
        if (difficulty.Difficulty == Difficulty.Beginner || difficulty.Notes.Count == 0)
            return Array.Empty<EliteDrumVisualDescriptorV1>();

        var physicalNotes = EnumeratePhysicalNotes(difficulty.Notes).ToArray();

        // Authored Elite hand-lane phrase records are resolved from the actual final
        // DrumNote children (see BuildAuthoredLanePhraseRecords): a phrase whose
        // survivors agree on one final output pad publishes a descriptor carrying that
        // true final identity, while malformed phrases stay unresolved and unpublished.
        // Every record claims its member origins, so the pad-derived path below
        // partitions deterministically and can never emit a duplicate descriptor for the
        // same source gems. Both paths run for a mixed difficulty; the early return that
        // used to suppress unrelated resolved pad-derived descriptors is gone.
        var (authoredRecords, authoredPublished) = BuildAuthoredLanePhraseRecords(
            difficulty, ledger.AuthoredLanePhrases, physicalNotes, visualIdentity);
        var claimedOrigins = new HashSet<EliteDrumConversionOrigin>();
        foreach (var record in authoredRecords)
        {
            foreach (var origin in record.Origins)
            {
                claimedOrigins.Add(origin);
            }
        }

        // Pad-derived components never run for chart conversions that carry authored
        // phrase records over the same origins, so membership is never re-inferred from
        // final pads for claimed origins.
        var components = ledger.Components.Count > 0
            ? ledger.Components
            : EliteDrumFinalPadComponentBuilder.Build(
                physicalNotes
                    .Where(note => note.ConversionOrigin is not null &&
                        !claimedOrigins.Contains(note.ConversionOrigin))
                    .GroupBy(note => note.ConversionOrigin!)
                    .Select(group => new EliteDrumSourceMembership(group.Key,
                        new EliteDrumFinalPadIdentity(difficulty.Instrument, group.First().Pad),
                        group.Key.Source.StartTick, group.Key.Source.EndTick)));
        var descriptors = new List<EliteDrumVisualDescriptorV1>();
        foreach (var component in components)
        {
            // A component is the final gameplay unit. It may contain several
            // physical gems (and flam-expanded output notes), but must produce one
            // visual lane. Do not fall back to one descriptor per physical note.
            var memberNotes = physicalNotes
                .Where(note => note.ConversionOrigin is not null &&
                    !claimedOrigins.Contains(note.ConversionOrigin) &&
                    component.Origins.Contains(note.ConversionOrigin))
                .ToArray();
            // A provenance origin is eligible only when a surviving physical note
            // still carries the authored Elite hand-lane marker. Pad identity alone
            // is not sufficient: ordinary notes and legacy/conversion artifacts can
            // share a tom or cymbal pad without belonging to a hand-fill phrase.
            var eligibleNotes = memberNotes
                .Where(IsEligibleHandFillNote)
                .ToArray();
            var eligibleSources = eligibleNotes
                .Select(note => note.ConversionOrigin!.Source)
                .Distinct()
                .ToArray();
            var handOrigins = component.Origins
                .Where(origin => eligibleSources.Contains(origin.Source))
                .ToArray();
            var visualNotes = memberNotes
                .Where(note => eligibleSources.Contains(note.ConversionOrigin!.Source))
                .ToArray();
            if (handOrigins.Length == 0 || visualNotes.Length == 0)
            {
                // Kick, authored hat-pedal, ordinary, and fully dropped components
                // must never allocate a hand visual lane.
                continue;
            }

            // The ledger component is a grouping of emitted origins; its published
            // identity is resolved from the group's actual final DrumNote children,
            // never from the ledger's intermediate Moon-space grouping pad (which is
            // derived from MoonNote.rawNote during downchart conversion). Mixed final
            // pads inside one component are malformed for publication: the component is
            // skipped with a diagnostic instead of choosing a pad arbitrarily.
            var componentPads = memberNotes
                .Select(note => new EliteDrumFinalPadIdentity(difficulty.Instrument, note.Pad))
                .Distinct()
                .ToArray();
            if (componentPads.Length != 1)
            {
                var padSummary = string.Join(", ", componentPads.Select(pad =>
                    $"{pad.Instrument} pad {pad.Pad}"));
                YargLogger.LogWarning(
                    $"Elite Drums conversion component over ticks [{component.StartTick},{component.EndTick}) on {difficulty.Instrument} mixes final output pads ({padSummary}); it is not published and no pad is chosen arbitrarily.");
                continue;
            }

            var finalPad = componentPads[0];
            var visual = visualIdentity?.Invoke(finalPad) ??
                new EliteDrumComponentVisualDescriptor($"pad:{finalPad.Pad}", true,
                    handOrigins[0].Source.SourceId, finalPad.Pad,
                    finalPad.Instrument.ToString(), Array.Empty<EliteDrumComponentMetadata>());
            var sources = handOrigins.Select(item => item.Source.SourceId).Distinct().ToArray();
            var authoredStartTime = handOrigins.Min(origin => origin.Source.StartTime);
            var authoredEndTime = handOrigins.Max(origin => origin.Source.EndTime);

            // DrumNote deliberately has no sustain length. Use source timing for
            // those zero-length notes, while still honoring a nonzero output
            // interval if a future converter supplies one.
            var physicalIntervals = visualNotes.Select(note =>
            {
                var origin = note.ConversionOrigin!;
                var start = note.Time;
                var end = note.Time + note.TimeLength;
                if (end <= start && origin.Source.EndTime > origin.Source.StartTime)
                {
                    start = origin.Source.StartTime;
                    end = origin.Source.EndTime;
                }
                return (start, end);
            }).ToArray();
            var first = physicalIntervals.Min(interval => interval.start);
            var last = physicalIntervals.Max(interval => interval.end);
            if (last < first)
            {
                last = first;
            }

            var codaExcluded = handOrigins.Any(origin => origin.Source.IsCodaEnd) ||
                memberNotes.Any(note => note.IsCodaEnd);
            descriptors.Add(new EliteDrumVisualDescriptorV1(
                $"{finalPad.Instrument}:{finalPad.Pad}:{component.StartTick}:{component.EndTick}",
                sources, handOrigins, finalPad, visual, component.StartTick, component.EndTick,
                authoredStartTime, authoredEndTime, first, last, true, codaExcluded));
        }

        // Deterministic publish: only descriptors whose final identity is explicitly
        // resolved may be published. The authored pipeline already excludes unresolved
        // (malformed) records from its published output, so consumers that still gate on
        // the pad value are protected without relying on it. Ordering is total (tick
        // span, then gameplay ID) and duplicate gameplay IDs collapse to their first
        // occurrence.
        return new ReadOnlyCollection<EliteDrumVisualDescriptorV1>(descriptors
            .Concat(authoredPublished)
            .GroupBy(record => record.GameplayId, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(record => record.AuthoredStartTick)
            .ThenBy(record => record.AuthoredEndTick)
            .ThenBy(record => record.GameplayId, StringComparer.Ordinal)
            .ToList());
    }

    /// <summary>
    /// Builds the phrase-spanning provenance record for every valid authored hand-lane
    /// phrase instance, independently of descriptor publication. A phrase with fewer than
    /// three surviving final physical members is malformed: no record is built and the
    /// provenance stays on the surviving notes for diagnostics.
    ///
    /// Stage 2 resolves each valid phrase's true final output identity from its surviving
    /// final DrumNote children (their actual final <see cref="DrumNote.Pad"/> in the
    /// owning instrument's pad space) — never from the intermediate Moon pad
    /// (<c>MoonNote.rawNote</c>) and never from the authored Elite pad. Survivors that
    /// agree on one final pad yield one resolved record. Survivors that split into
    /// several final pads are separated into one record per final pad only when every
    /// final group independently satisfies the surviving-member minimum (separate final
    /// groups supported by phrase membership); otherwise the whole phrase is malformed:
    /// its record keeps the explicit unresolved identity
    /// (<see cref="EliteDrumFinalPadIdentity.UnresolvedFor"/>),
    /// <see cref="EliteDrumVisualDescriptorV1.IsFinalIdentityResolved"/> stays false, a
    /// diagnostic is logged, and no final pad is chosen arbitrarily. Unresolved records
    /// are provenance only and are never publish-eligible; consumers must gate on the
    /// explicit resolution state rather than the pad value (pad 0 is a valid final Kick
    /// pad). The authored lane pad stays on the record's provenance visual; it never
    /// leaks into any final-pad identity.
    /// </summary>
    public static IReadOnlyList<EliteDrumVisualDescriptorV1> BuildAuthoredLanePhraseRecords(
        InstrumentDifficulty<DrumNote> difficulty, EliteDrumConversionLedger ledger)
    {
        if (difficulty is null) throw new ArgumentNullException(nameof(difficulty));
        if (ledger is null) throw new ArgumentNullException(nameof(ledger));
        if (difficulty.Difficulty == Difficulty.Beginner || difficulty.Notes.Count == 0)
            return Array.Empty<EliteDrumVisualDescriptorV1>();

        return BuildAuthoredLanePhraseRecords(difficulty, ledger.AuthoredLanePhrases,
            EnumeratePhysicalNotes(difficulty.Notes).ToArray(), null).Records;
    }

    /// <summary>
    /// Core authored-phrase record construction shared by the public provenance API and
    /// the publish pipeline. Returns both the provenance records (resolved where the
    /// phrase's final pads agree, unresolved for malformed phrases) and the publishable
    /// variants whose appearance is derived strictly from the true final identity.
    /// Both lists use a total deterministic order (tick span, then gameplay ID).
    /// </summary>
    private static (IReadOnlyList<EliteDrumVisualDescriptorV1> Records,
        IReadOnlyList<EliteDrumVisualDescriptorV1> Published) BuildAuthoredLanePhraseRecords(
        InstrumentDifficulty<DrumNote> difficulty,
        IReadOnlyList<EliteDrumAuthoredLanePhrase> authoredLanePhrases,
        DrumNote[] physicalNotes,
        Func<EliteDrumFinalPadIdentity, EliteDrumComponentVisualDescriptor>? visualIdentity)
    {
        var records = new List<EliteDrumVisualDescriptorV1>();
        var published = new List<EliteDrumVisualDescriptorV1>();
        foreach (var phrase in authoredLanePhrases)
        {
            var memberNotes = physicalNotes
                .Where(note => phrase.ContainsOrigin(note.ConversionOrigin) &&
                    EliteDrumAuthoredLanePhraseTypes.IsHandPad(note.ConversionOrigin!.Source.Pad))
                .ToArray();
            if (memberNotes.Length < EliteDrumAuthoredLanePhraseTypes.MinimumSurvivingMembers)
            {
                continue;
            }

            // Stage 2: the phrase's final output identity comes from the actual converted
            // final DrumNote children. Each survivor's final Pad is the converter's true
            // final output for that gem in the owning instrument's pad space (Pro/Four
            // lane pads for ProDrums/FourLaneDrums, five lane pads for FiveLaneDrums —
            // including every intermediate-to-final remap the target's creation mapping
            // performed). The intermediate Moon pad and the authored Elite pad are never
            // consulted.
            var finalGroups = memberNotes
                .GroupBy(note => new EliteDrumFinalPadIdentity(difficulty.Instrument, note.Pad))
                .OrderBy(group => group.Key.Pad)
                .ToArray();

            if (finalGroups.Length > 1 && finalGroups.Any(group =>
                    group.Count() < EliteDrumAuthoredLanePhraseTypes.MinimumSurvivingMembers))
            {
                // Mixed final pads whose split is not supported by phrase membership:
                // mark the whole record malformed (explicitly unresolved), log a
                // diagnostic, and publish nothing. No majority pad is chosen.
                var groupSummary = string.Join(", ", finalGroups.Select(group =>
                    $"{DescribeFinalPad(group.Key)} x{group.Count()}"));
                YargLogger.LogWarning(
                    $"Authored Elite {phrase.LaneType} phrase at ticks [{phrase.StartTick},{phrase.EndTick}) resolves to mixed final output pads ({groupSummary}); no final group reaches {EliteDrumAuthoredLanePhraseTypes.MinimumSurvivingMembers} surviving members, so the lane is malformed and stays unpublished.");
                records.Add(BuildRecord(difficulty, phrase, memberNotes,
                    EliteDrumFinalPadIdentity.UnresolvedFor(difficulty.Instrument)));
                continue;
            }

            foreach (var group in finalGroups)
            {
                // One agreed final pad, or a supported split: every final group is itself
                // a valid >= minimum surviving-member lane, so each publishes its own
                // descriptor under a pad-qualified gameplay ID.
                var isSplit = finalGroups.Length > 1;
                var gameplayId = isSplit
                    ? $"{difficulty.Instrument}:authored-lane:{phrase.LaneType}:{phrase.StartTick}:{phrase.EndTick}:pad:{group.Key.Pad}"
                    : null;
                var record = BuildRecord(difficulty, phrase, group.ToArray(), group.Key, gameplayId);
                records.Add(record);
                published.Add(BuildPublished(record, visualIdentity));
            }
        }

        return (OrderByDescriptorOrder(records), OrderByDescriptorOrder(published));

        static List<EliteDrumVisualDescriptorV1> OrderByDescriptorOrder(
            List<EliteDrumVisualDescriptorV1> source) => source
            .OrderBy(record => record.AuthoredStartTick)
            .ThenBy(record => record.AuthoredEndTick)
            .ThenBy(record => record.GameplayId, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Builds one provenance record for a phrase (or a supported final-pad group of it).
    /// The record keeps the authored-lane visual: it is provenance identity, and the
    /// published variant resolves appearance strictly from the final identity instead.
    /// </summary>
    private static EliteDrumVisualDescriptorV1 BuildRecord(InstrumentDifficulty<DrumNote> difficulty,
        EliteDrumAuthoredLanePhrase phrase, DrumNote[] memberNotes, EliteDrumFinalPadIdentity finalPad,
        string? gameplayId = null)
    {
        var authoredPad = EliteDrumAuthoredLanePhraseTypes.ToAuthoredPad(phrase.LaneType);
        var visual = new EliteDrumComponentVisualDescriptor($"authored-lane:{phrase.LaneType}", true,
            phrase.LaneType.ToString(), (int) authoredPad, authoredPad.ToString(),
            Array.Empty<EliteDrumComponentMetadata>());

        var orderedOrigins = memberNotes
            .Select(note => note.ConversionOrigin!)
            .Distinct()
            .OrderBy(origin => origin.Source.StartTick)
            .ThenBy(origin => origin.Source.PhysicalOrdinal)
            .ThenBy(origin => origin.ExpansionOrdinal)
            .ToArray();
        var sources = orderedOrigins
            .Select(origin => origin.Source.SourceId)
            .Distinct()
            .ToArray();

        var authoredStartTime = orderedOrigins.Min(origin => origin.Source.StartTime);
        var authoredEndTime = orderedOrigins.Max(origin => origin.Source.EndTime);

        // DrumNote deliberately has no sustain length. Use source timing for
        // those zero-length notes, while still honoring a nonzero output
        // interval if a future converter supplies one.
        var physicalIntervals = memberNotes.Select(note =>
        {
            var origin = note.ConversionOrigin!;
            var start = note.Time;
            var end = note.Time + note.TimeLength;
            if (end <= start && origin.Source.EndTime > origin.Source.StartTime)
            {
                start = origin.Source.StartTime;
                end = origin.Source.EndTime;
            }
            return (start, end);
        }).ToArray();
        var first = physicalIntervals.Min(interval => interval.start);
        var last = physicalIntervals.Max(interval => interval.end);
        if (last < first)
        {
            last = first;
        }

        var codaExcluded = orderedOrigins.Any(origin => origin.Source.IsCodaEnd) ||
            memberNotes.Any(note => note.IsCodaEnd);
        return new EliteDrumVisualDescriptorV1(
            gameplayId ?? $"{difficulty.Instrument}:authored-lane:{phrase.LaneType}:{phrase.StartTick}:{phrase.EndTick}",
            sources, orderedOrigins, finalPad, visual, phrase.StartTick, phrase.EndTick,
            authoredStartTime, authoredEndTime, first, last, true, codaExcluded);
    }

    /// <summary>
    /// Publishable variant of an authored record: identical provenance, but the
    /// appearance is resolved strictly from the record's true final identity. The visual
    /// resolver is invoked only with the resolved final pad, and the default visual
    /// carries the final pad in its color slot and model key, so no authored or
    /// intermediate source pad value can leak into published appearance data.
    /// </summary>
    private static EliteDrumVisualDescriptorV1 BuildPublished(EliteDrumVisualDescriptorV1 record,
        Func<EliteDrumFinalPadIdentity, EliteDrumComponentVisualDescriptor>? visualIdentity)
    {
        var finalPad = record.FinalPad;
        var visual = visualIdentity?.Invoke(finalPad) ?? new EliteDrumComponentVisualDescriptor(
            record.VisualIdentity.LaneId, record.VisualIdentity.Enabled,
            $"{finalPad.Instrument}:pad:{finalPad.Pad}", finalPad.Pad, DescribeFinalPad(finalPad),
            Array.Empty<EliteDrumComponentMetadata>());
        return new(record.GameplayId, record.SourceIds, record.Origins, finalPad, visual,
            record.AuthoredStartTick, record.AuthoredEndTick, record.AuthoredStartTime,
            record.AuthoredEndTime, record.FirstPhysicalEventTime, record.LastPhysicalEventTime,
            record.RulesetEligible, record.CodaExcluded);
    }

    /// <summary>Final output pad name in the owning instrument's pad space.</summary>
    private static string DescribeFinalPad(EliteDrumFinalPadIdentity finalPad) => finalPad.Instrument switch
    {
        Instrument.FiveLaneDrums => Enum.IsDefined(typeof(FiveLaneDrumPad), finalPad.Pad)
            ? ((FiveLaneDrumPad) finalPad.Pad).ToString()
            : finalPad.Pad.ToString(),
        _ => Enum.IsDefined(typeof(FourLaneDrumPad), finalPad.Pad)
            ? ((FourLaneDrumPad) finalPad.Pad).ToString()
            : finalPad.Pad.ToString(),
    };

    private static bool IsEligibleHandFillNote(DrumNote note)
    {
        if (!note.IsTremolo || note.ConversionOrigin is null)
        {
            return false;
        }

        var sourcePad = note.ConversionOrigin.Source.Pad;
        return sourcePad is not (int)EliteDrumNote.EliteDrumPad.Kick and
            not (int)EliteDrumNote.EliteDrumPad.HatPedal;
    }

    private static IEnumerable<DrumNote> EnumeratePhysicalNotes(IEnumerable<DrumNote> notes)
    {
        foreach (var note in notes)
        {
            foreach (var physical in note.AllNotes)
            {
                yield return physical;
            }
        }
    }
}

/// <summary>Hand-authored metadata fixture shape supplied by the eventual converter.</summary>
public sealed class EliteDrumComponentMetadata
{
    public string Id { get; }
    public EliteDrumComponentKind Kind { get; }
    public int ColorSlot { get; }
    public string ModelKey { get; }
    public bool Enabled { get; }
    public bool IsAppearanceOwner { get; }

    public EliteDrumComponentMetadata(string id, EliteDrumComponentKind kind, int colorSlot,
        string modelKey, bool enabled = true, bool isAppearanceOwner = false)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A component ID is required.", nameof(id));
        if (colorSlot < 0) throw new ArgumentOutOfRangeException(nameof(colorSlot));
        if (string.IsNullOrWhiteSpace(modelKey)) throw new ArgumentException("A model key is required.", nameof(modelKey));
        Id = id;
        Kind = kind;
        ColorSlot = colorSlot;
        ModelKey = modelKey;
        Enabled = enabled;
        IsAppearanceOwner = isAppearanceOwner;
    }
}

public sealed class EliteDrumComponentVisualDescriptor
{
    private readonly ReadOnlyCollection<EliteDrumComponentMetadata> _components;
    public string LaneId { get; }
    public bool Enabled { get; }
    public string AppearanceOwnerId { get; }
    public int AppearanceColorSlot { get; }
    public string AppearanceModelKey { get; }
    public IReadOnlyList<EliteDrumComponentMetadata> Components => _components;

    public EliteDrumComponentVisualDescriptor(string laneId, bool enabled, string appearanceOwnerId,
        int appearanceColorSlot, string appearanceModelKey, IEnumerable<EliteDrumComponentMetadata> components)
    {
        LaneId = laneId ?? throw new ArgumentNullException(nameof(laneId));
        Enabled = enabled;
        AppearanceOwnerId = appearanceOwnerId ?? throw new ArgumentNullException(nameof(appearanceOwnerId));
        AppearanceColorSlot = appearanceColorSlot;
        AppearanceModelKey = appearanceModelKey ?? throw new ArgumentNullException(nameof(appearanceModelKey));
        _components = new ReadOnlyCollection<EliteDrumComponentMetadata>(
            (components ?? throw new ArgumentNullException(nameof(components))).ToArray());
    }
}

public static class EliteDrumComponentVisualDescriptorBuilder
{
    /// <summary>
    /// Merges enabled metadata only. Disabled members remain excluded from the render descriptor,
    /// and explicit appearance ownership wins over source ordering.
    /// </summary>
    public static EliteDrumComponentVisualDescriptor Build(
        string laneId, IEnumerable<EliteDrumComponentMetadata> metadata)
    {
        if (string.IsNullOrWhiteSpace(laneId)) throw new ArgumentException("A lane ID is required.", nameof(laneId));
        var source = (metadata ?? throw new ArgumentNullException(nameof(metadata))).ToArray();
        if (source.Length == 0) throw new ArgumentException("At least one component is required.", nameof(metadata));
        if (source.Any(component => component is null)) throw new ArgumentException("Metadata cannot contain null.", nameof(metadata));

        var enabled = source.Where(component => component.Enabled).ToArray();
        if (enabled.Length == 0)
        {
            return new EliteDrumComponentVisualDescriptor(laneId, false, string.Empty, 0,
                string.Empty, Array.Empty<EliteDrumComponentMetadata>());
        }

        var owners = enabled.Where(component => component.IsAppearanceOwner).ToArray();
        var owner = owners.Length > 0 ? owners[0] : enabled[0];
        return new EliteDrumComponentVisualDescriptor(laneId, true, owner.Id, owner.ColorSlot,
            owner.ModelKey, enabled);
    }
}
}
