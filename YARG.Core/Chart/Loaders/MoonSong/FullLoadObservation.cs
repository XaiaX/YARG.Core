using System;
using System.Threading;

namespace YARG.Core.Chart
{
    // Opt-in, process-wide observation: scans use worker threads. Only one scope may be active.
    internal sealed class FullLoadObservation : IDisposable
    {
        private static FullLoadObservation? _active;
        private int _midiLoads;
        private int _dotChartLoads;
        private int _authoredNoteBatches;
        private int _authoredNotes;

        internal int MidiLoads => Volatile.Read(ref _midiLoads);
        internal int DotChartLoads => Volatile.Read(ref _dotChartLoads);
        internal int AuthoredNoteBatches => Volatile.Read(ref _authoredNoteBatches);
        internal int AuthoredNotes => Volatile.Read(ref _authoredNotes);

        private FullLoadObservation() { }

        internal static FullLoadObservation Begin()
        {
            var observation = new FullLoadObservation();
            if (Interlocked.CompareExchange(ref _active, observation, null) != null)
                throw new InvalidOperationException("A full-load observation is already active.");
            return observation;
        }

        internal static void MidiLoadEntered()
        {
            var active = Volatile.Read(ref _active);
            if (active != null) Interlocked.Increment(ref active._midiLoads);
        }

        internal static void DotChartLoadEntered()
        {
            var active = Volatile.Read(ref _active);
            if (active != null) Interlocked.Increment(ref active._dotChartLoads);
        }

        internal static void AuthoredNoteBatchEntered()
        {
            var active = Volatile.Read(ref _active);
            if (active != null) Interlocked.Increment(ref active._authoredNoteBatches);
        }

        internal static void AuthoredNoteConstructing()
        {
            var active = Volatile.Read(ref _active);
            if (active != null) Interlocked.Increment(ref active._authoredNotes);
        }

        public void Dispose() => Interlocked.CompareExchange(ref _active, null, this);
    }
}
