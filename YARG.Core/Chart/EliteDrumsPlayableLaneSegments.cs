// pattern: Functional Core
using System;
using System.Collections.Generic;
using System.Linq;

namespace YARG.Core.Chart
{
    /// <summary>Immutable membership of one contiguous authored roll on an actual playable target.</summary>
    public sealed class EliteDrumsPlayableLaneSegment
    {
        public EliteDrumNativeAuthoredLaneRecord AuthoredRecord => AuthoredRecords[0];
        public IReadOnlyList<EliteDrumNativeAuthoredLaneRecord> AuthoredRecords { get; }
        public int Pad { get; }
        public IReadOnlyList<EliteDrumNote> Members { get; }
        public double StartTime { get; }
        public double EndTime { get; }

        internal EliteDrumsPlayableLaneSegment(EliteDrumNativeAuthoredLaneRecord record,
            IEnumerable<EliteDrumNote> members)
        {
            var notes = members.ToArray();
            AuthoredRecords = Array.AsReadOnly(new[] { record });
            Pad = notes[0].Pad;
            Members = Array.AsReadOnly(notes);
            StartTime = notes[0].Time;
            EndTime = notes[^1].Time;
        }
        internal EliteDrumsPlayableLaneSegment(IEnumerable<EliteDrumsPlayableLaneSegment> segments)
        {
            var parts = segments.ToArray();
            var notes = parts.SelectMany(segment => segment.Members).OrderBy(note => note.Time).ThenBy(note => note.Tick)
                .GroupBy(note => (note.Pad, note.Tick)).Select(group => group.First()).ToArray();
            AuthoredRecords = Array.AsReadOnly(parts.SelectMany(segment => segment.AuthoredRecords).Distinct().ToArray());
            Pad = notes[0].Pad;
            Members = Array.AsReadOnly(notes);
            StartTime = notes[0].Time;
            EndTime = notes[^1].Time;
        }

    }

    /// <summary>Shared by native judging and rendering, including half-open practice slices.</summary>
    public static class EliteDrumsPlayableLaneSegments
    {
        public static IReadOnlyList<EliteDrumsPlayableLaneSegment> Resolve(InstrumentDifficulty<EliteDrumNote> chart)
        {
            if (chart is null) throw new ArgumentNullException(nameof(chart));
            var sources = new Dictionary<EliteDrumSourceDefinition, EliteDrumNote>();
            foreach (var parent in chart.Notes)
                foreach (var note in parent.AllNotes)
                    if (!note.IsInvisibleTerminator)
                        foreach (var source in note.SourceDefinitions) sources.TryAdd(source, note);
            var segments = new List<EliteDrumsPlayableLaneSegment>();
            foreach (var record in chart.EliteDrumNativeAuthoredLaneRecords)
            {
                var members = new List<EliteDrumNote>();
                bool malformed = false;
                foreach (var source in record.MemberSources)
                {
                    if (source is null || source.Pad != (int)record.AuthoredPad ||
                        !record.ContainsTick(source.StartTick))
                    {
                        malformed = true;
                        break;
                    }
                    if (!sources.TryGetValue(source, out var note))
                    {
                        // Only the explicit No Pedal transform may remove an authored member.
                        if (!chart.NativeElitePedalsFiltered || record.AuthoredPad != EliteDrumNote.EliteDrumPad.HatPedal)
                            malformed = true;
                        continue;
                    }
                    if (!members.Contains(note)) members.Add(note);
                }
                if (malformed || members.Count < 2) continue;
                members.Sort((a, b) => a.Time != b.Time ? a.Time.CompareTo(b.Time) : a.Tick.CompareTo(b.Tick));
                // Keep physical activity intervals separate across target excursions, including
                // singleton boundaries. Logical entry and covered returns are judged by the engine;
                // the renderer may omit a zero-length singleton strip without discarding membership.
                int start = 0;
                while (start < members.Count)
                {
                    int end = start + 1;
                    while (end < members.Count && members[end].Pad == members[start].Pad) end++;
                    segments.Add(new EliteDrumsPlayableLaneSegment(record, members.GetRange(start, end - start)));
                    start = end;
                }
            }
            return segments.AsReadOnly();
        }
        /// <summary>
        /// Inclusive physical intervals involving projected cymbals, with shared boundaries
        /// and onsets represented once. Unprojected authored rolls retain their own records.
        /// </summary>
        public static IReadOnlyList<EliteDrumsPlayableLaneSegment> ResolvePhysical(InstrumentDifficulty<EliteDrumNote> chart) =>
            Union(Resolve(chart));

        private static IReadOnlyList<EliteDrumsPlayableLaneSegment> Union(IReadOnlyList<EliteDrumsPlayableLaneSegment> segments)
        {
            var result = new List<EliteDrumsPlayableLaneSegment>();
            foreach (var pad in segments.GroupBy(segment => segment.Pad))
            {
                var current = new List<EliteDrumsPlayableLaneSegment>();
                double end = double.NegativeInfinity;
                foreach (var segment in pad.OrderBy(segment => segment.StartTime).ThenBy(segment => segment.EndTime))
                {
                    if (current.Count > 0 && segment.StartTime > end)
                    {
                        AddPhysicalInterval(result, current);
                        current.Clear();
                    }
                    current.Add(segment);
                    end = current.Count == 1 ? segment.EndTime : Math.Max(end, segment.EndTime);
                }
                if (current.Count > 0) AddPhysicalInterval(result, current);
            }
            return result.OrderBy(segment => segment.StartTime).ThenBy(segment => segment.Pad).ToList().AsReadOnly();
        }

        private static void AddPhysicalInterval(List<EliteDrumsPlayableLaneSegment> result,
            List<EliteDrumsPlayableLaneSegment> interval)
        {
            bool projected = interval.SelectMany(segment => segment.Members).Any(note => note.Pad != note.OriginalPad ||
                note.SourceDefinitions.Any(source => source.Pad != note.Pad));
            if (projected) result.Add(new EliteDrumsPlayableLaneSegment(interval));
            else result.AddRange(interval);
        }

        /// <summary>
        /// Collapse only coincident members of overlapping physical roll records. Prefer the
        /// actual authored target over a moved source, then saved note order; retain every source alias.
        /// Ordinary notes and simultaneous expanded targets on different pads remain independent.
        /// </summary>
        internal static void CollapsePhysicalDuplicates(InstrumentDifficulty<EliteDrumNote> chart)
        {
            var raw = Resolve(chart);
            var eligible = new HashSet<(int Pad, uint Tick)>(Union(raw).Where(segment => segment.AuthoredRecords.Count > 1)
                .SelectMany(segment => segment.Members).Select(note => (note.Pad, note.Tick)));
            var ordered = chart.Notes.SelectMany(parent => parent.ChildNotes.Prepend(parent)).ToArray();
            var order = ordered.Select((note, index) => (note, index)).ToDictionary(entry => entry.note, entry => entry.index);
            var replacements = new Dictionary<EliteDrumNote, EliteDrumNote?>();
            foreach (var group in raw.SelectMany(segment => segment.Members).Distinct()
                .Where(note => eligible.Contains((note.Pad, note.Tick))).GroupBy(note => (note.Pad, note.Tick)))
            {
                var duplicates = group.OrderByDescending(note => note.Pad == note.OriginalPad).ThenBy(note => order[note]).ToArray();
                if (duplicates.Length < 2) continue;
                replacements[duplicates[0]] = new EliteDrumNote(duplicates[0], duplicates.SelectMany(note => note.SourceDefinitions));
                foreach (var duplicate in duplicates.Skip(1)) replacements[duplicate] = null;
            }
            if (replacements.Count == 0) return;
            chart.Notes.Clear();
            foreach (var onset in ordered.GroupBy(note => note.Tick))
            {
                EliteDrumNote? parent = null;
                foreach (var original in onset)
                {
                    var selected = replacements.TryGetValue(original, out var replacement) ? replacement : original;
                    if (selected == null) continue;
                    var note = selected.CloneWithoutChildNotes();
                    if (parent == null) parent = note;
                    else parent.AddChildNote(note);
                }
                if (parent != null) chart.Notes.Add(parent);
            }
        }

    }
}
