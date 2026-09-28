using System;
using System.Collections.Generic;
using YARG.Core.Extensions;
using YARG.Core.Engine.Drums;

namespace YARG.Core.Chart
{
    /// <summary>
    /// A single difficulty of an instrument track.
    /// </summary>
    public class InstrumentDifficulty<TNote> : ICloneable<InstrumentDifficulty<TNote>>
        where TNote : Note<TNote>
    {
        public Instrument Instrument { get; }
        public Difficulty Difficulty { get; }

        public List<TNote>      Notes            { get; } = new();
        public List<Phrase>     Phrases          { get; } = new();
        public List<TextEvent>  TextEvents       { get; } = new();
        public List<RangeShift> RangeShiftEvents { get; } = new();

        /// <summary>
        /// Immutable final generated Elite Drums visual descriptors. Every published
        /// descriptor carries an explicitly resolved final output identity
        /// (<see cref="EliteDrumVisualDescriptorV1.IsFinalIdentityResolved"/>). Native,
        /// legacy, disabled, and Beginner difficulties expose an empty collection.
        /// </summary>
        public IReadOnlyList<EliteDrumVisualDescriptorV1> EliteDrumVisualDescriptors { get; private set; }
            = Array.Empty<EliteDrumVisualDescriptorV1>();

        public void SetEliteDrumVisualDescriptors(IEnumerable<EliteDrumVisualDescriptorV1> descriptors)
        {
            EliteDrumVisualDescriptors = new List<EliteDrumVisualDescriptorV1>(
                descriptors ?? throw new ArgumentNullException(nameof(descriptors))).AsReadOnly();
        }

        /// <summary>
        /// Authored Elite hand-lane phrase provenance records, preserved separately from
        /// the published descriptor collection. Stage 2 resolves each valid record's
        /// final output identity from its surviving final DrumNote children, so resolved
        /// records carry true final identities; malformed records (mixed final pads
        /// without lane-supporting membership) keep the explicit unresolved state
        /// (<see cref="EliteDrumVisualDescriptorV1.IsFinalIdentityResolved"/> is false)
        /// and are never eligible for descriptor publish/spawn gating. Native, legacy,
        /// disabled, and Beginner difficulties expose an empty collection.
        /// </summary>
        public IReadOnlyList<EliteDrumVisualDescriptorV1> EliteDrumAuthoredLanePhraseRecords { get; private set; }
            = Array.Empty<EliteDrumVisualDescriptorV1>();

        public void SetEliteDrumAuthoredLanePhraseRecords(IEnumerable<EliteDrumVisualDescriptorV1> records)
        {
            EliteDrumAuthoredLanePhraseRecords = new List<EliteDrumVisualDescriptorV1>(
                records ?? throw new ArgumentNullException(nameof(records))).AsReadOnly();
        }

        /// <summary>Native Elite Drums authored pad-lane phrases and their source-gem membership.</summary>
        public IReadOnlyList<EliteDrumNativeAuthoredLaneRecord> EliteDrumNativeAuthoredLaneRecords { get; private set; }
            = Array.Empty<EliteDrumNativeAuthoredLaneRecord>();

        /// <summary>Set only on a native gameplay clone whose authored pedal gems were intentionally filtered.</summary>
        public bool NativeElitePedalsFiltered { get; internal set; }

        public void SetNativeElitePedalsFiltered(bool filtered) => NativeElitePedalsFiltered = filtered;

        public void SetEliteDrumNativeAuthoredLaneRecords(IEnumerable<EliteDrumNativeAuthoredLaneRecord> records)
        {
            EliteDrumNativeAuthoredLaneRecords = new List<EliteDrumNativeAuthoredLaneRecord>(
                records ?? throw new ArgumentNullException(nameof(records))).AsReadOnly();
        }

        /// <summary>Project native authored lanes onto a half-open practice interval.</summary>
        public IReadOnlyList<EliteDrumNativeAuthoredLaneRecord> SliceEliteDrumNativeAuthoredLaneRecords(
            uint startTick, uint endTick)
        {
            if (endTick < startTick) throw new ArgumentOutOfRangeException(nameof(endTick));
            var records = new List<EliteDrumNativeAuthoredLaneRecord>();
            foreach (var record in EliteDrumNativeAuthoredLaneRecords)
            {
                var sliced = record.Slice(startTick, endTick);
                if (sliced is not null) records.Add(sliced);
            }
            return records.AsReadOnly();
        }

        /// <summary>
        /// Whether or not this difficulty contains any data.
        /// </summary>
        /// <remarks>
        /// This should *not* be used to determine whether or not the chart is present!
        /// Use <see cref="InstrumentTrack{TNote}.TryGetDifficulty(Difficulty, out InstrumentDifficulty{TNote}?)"/> instead.
        /// </remarks>
        public bool IsEmpty => Notes.Count == 0 && Phrases.Count == 0 && TextEvents.Count == 0;

        public InstrumentDifficulty(Instrument instrument, Difficulty difficulty)
        {
            Instrument = instrument;
            Difficulty = difficulty;
        }

        public InstrumentDifficulty(Instrument instrument, Difficulty difficulty,
            List<TNote> notes, List<Phrase> phrases, List<TextEvent> text)
            : this(instrument, difficulty)
        {
            Notes = notes;
            Phrases = phrases;
            TextEvents = text;
            RangeShiftEvents = new List<RangeShift>();
        }

        public InstrumentDifficulty(Instrument instrument, Difficulty difficulty,
            List<TNote> notes, List<Phrase> phrases, List<TextEvent> text, List<RangeShift> shifts)
            : this(instrument, difficulty)
        {
            Notes = notes;
            Phrases = phrases;
            TextEvents = text;
            RangeShiftEvents = shifts;
        }

        public InstrumentDifficulty(InstrumentDifficulty<TNote> other)
            : this(other.Instrument, other.Difficulty, other.Notes.DuplicateNotes(), other.Phrases.Duplicate(),
                other.TextEvents.Duplicate(), other.RangeShiftEvents.Duplicate())
        {
            EliteDrumVisualDescriptors = new List<EliteDrumVisualDescriptorV1>(other.EliteDrumVisualDescriptors).AsReadOnly();
            EliteDrumAuthoredLanePhraseRecords = new List<EliteDrumVisualDescriptorV1>(
                other.EliteDrumAuthoredLanePhraseRecords).AsReadOnly();
            EliteDrumNativeAuthoredLaneRecords = new List<EliteDrumNativeAuthoredLaneRecord>(
                other.EliteDrumNativeAuthoredLaneRecords).AsReadOnly();
            NativeElitePedalsFiltered = other.NativeElitePedalsFiltered;
        }

        /// <summary>
        /// Gets the start time of the first event in this difficulty
        /// </summary>
        /// <returns>double</returns>
        /// <remarks>This returns double.MaxValue if there are no events</remarks>
        public double GetStartTime()
        {
            double totalStartTime = double.MaxValue;

            totalStartTime = Math.Min(Notes.GetStartTime(), totalStartTime);
            totalStartTime = Math.Min(Phrases.GetStartTime(), totalStartTime);
            totalStartTime = Math.Min(TextEvents.GetStartTime(), totalStartTime);

            return totalStartTime;
        }

        public double GetEndTime()
        {
            double totalEndTime = 0;

            totalEndTime = Math.Max(Notes.GetNoteEndTime(), totalEndTime);

            totalEndTime = Math.Max(Phrases.GetEndTime(), totalEndTime);
            totalEndTime = Math.Max(TextEvents.GetEndTime(), totalEndTime);

            return totalEndTime;
        }

        public double? GetFirstNoteStartTime()
        {
            return Notes.GetStartTime();
        }

        public double GetLastNoteEndTime()
        {
            return Notes.GetNoteEndTime();
        }

        public uint GetFirstTick()
        {
            uint totalFirstTick = 0;

            totalFirstTick = Math.Min(Notes.GetFirstTick(), totalFirstTick);
            totalFirstTick = Math.Min(Phrases.GetFirstTick(), totalFirstTick);
            totalFirstTick = Math.Min(TextEvents.GetFirstTick(), totalFirstTick);

            return totalFirstTick;
        }

        public uint GetLastTick()
        {
            uint totalLastTick = 0;

            totalLastTick = Math.Max(Notes.GetLastTick(), totalLastTick);
            totalLastTick = Math.Max(Phrases.GetLastTick(), totalLastTick);
            totalLastTick = Math.Max(TextEvents.GetLastTick(), totalLastTick);

            return totalLastTick;
        }

        public InstrumentDifficulty<TNote> Clone()
        {
            return new(this);
        }

        public int GetTotalNoteCount()
        {
            var noteCount = 0;
            foreach (var note in Notes)
            {
                noteCount += note.ChildNotes.Count + 1;
            }

            return noteCount;
        }
    }
}