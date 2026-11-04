// pattern: Functional Core
using System;
using System.Collections.Generic;
using System.Linq;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Core.Chart
{
    /// <summary>A ringing interval; lengths and fade boundaries are musical chart ticks, never score ticks.</summary>
    public sealed class EliteHiHatSustain
    {
        public uint StartTick { get; }
        public uint EndTick { get; }
        public uint FadeStartTick { get; }
        public double StartTime { get; }
        public double EndTime { get; }
        public double FadeStartTime { get; }
        public bool HasFade => FadeStartTick < EndTick;
        public IReadOnlyList<EliteDrumNote> Generators { get; }

        private EliteHiHatSustain(EliteDrumNote[] generators, uint end, uint fade, SyncTrack sync)
        {
            Generators = Array.AsReadOnly(generators);
            StartTick = generators[0].Tick;
            StartTime = generators[0].Time;
            EndTick = end;
            FadeStartTick = fade;
            double offset = StartTime - sync.TickToTime(StartTick);
            EndTime = sync.TickToTime(end) + offset;
            FadeStartTime = sync.TickToTime(fade) + offset;
        }

        public static bool IsGenerator(EliteDrumNote note) => !note.IsBigRockEnding &&
            (note.OriginalPad == (int)EliteDrumPad.HiHat && note.IsOpen ||
             note.OriginalPad == (int)EliteDrumPad.HatPedal && note.IsSplash);

        public static bool IsTerminator(EliteDrumNote note) =>
            note.OriginalPad == (int)EliteDrumPad.HatPedal ||
            note.OriginalPad == (int)EliteDrumPad.HiHat && !note.IsIndifferent;

        /// <summary>Build after profile modifiers, from surviving native sources, without mutating the chart.</summary>
        public static IReadOnlyList<EliteHiHatSustain> Generate(
            InstrumentDifficulty<EliteDrumNote> chart, SyncTrack sync)
        {
            if (chart == null) throw new ArgumentNullException(nameof(chart));
            if (sync == null || sync.Resolution == 0 || sync.Tempos.Count == 0)
                throw new ArgumentException("A timed sync track is required.", nameof(sync));
            if (chart.NativeElitePedalsFiltered) return Array.Empty<EliteHiHatSustain>();
            var notes = chart.Notes.SelectMany(note => note.ChildNotes.Prepend(note)).OrderBy(note => note.Tick).ToArray();
            var terminators = notes.Where(IsTerminator).Select(note => note.Tick).Distinct().ToArray();
            var result = new List<EliteHiHatSustain>();
            uint eighth = Math.Max(1u, sync.Resolution / 2);
            foreach (var group in notes.Where(IsGenerator).GroupBy(note => note.Tick))
            {
                var generators = group.ToArray();
                uint start = group.Key;
                ulong duration = generators.Max(note =>
                {
                    uint length = note.SourceDefinition is { } source ? source.EndTick - source.StartTick : 0;
                    return length >= eighth ? length : (ulong)sync.Resolution * 3 / 2;
                });
                uint end = (uint)Math.Min(uint.MaxValue, (ulong)start + duration);
                int next = Array.BinarySearch(terminators, start);
                next = next >= 0 ? next + 1 : ~next;
                bool clean = next < terminators.Length && terminators[next] <= end;
                if (clean) end = terminators[next];
                if (end <= start) continue;
                uint fade = clean ? end : Math.Max(start, end - Math.Min(eighth, end));
                result.Add(new EliteHiHatSustain(generators, end, fade, sync));
            }
            // A run of renewed intervals is omitted only if the entire connected run
            // is at most a sixteenth; individual short renewals remain in longer runs.
            var retained = new List<EliteHiHatSustain>();
            for (int i = 0; i < result.Count;)
            {
                int end = i;
                while (end + 1 < result.Count && result[end].EndTick == result[end + 1].StartTick) end++;
                if ((ulong)(result[end].EndTick - result[i].StartTick) * 4 > sync.Resolution)
                    for (int j = i; j <= end; j++) retained.Add(result[j]);
                i = end + 1;
            }
            return retained.AsReadOnly();
        }
    }
}
