using System;
using System.Collections.Generic;
using YARG.Core.Chart;

namespace YARG.Core.Engine.Vocals.Engines
{
    public sealed class PartyVocalsCoordinatorEngine : YargFreeVocalsEngine
    {
        // Suppress the base's legacy windowed-assignment cadence — the coordinator
        // owns meter computation via its greedy allocator instead.
        protected override void CommitWindowAssignment() { }

        // Direct credit per HARM, accumulated binary across mics per tick.
        private readonly double[] _harmDirectTicks;

        // Ambiguity buckets keyed by an integer mask over HARM indices.
        // Masks of interest for 3-HARM songs: 0b011 ({0,1}), 0b101 ({0,2}),
        // 0b110 ({1,2}), 0b111 ({0,1,2}). Indexed directly; entries for
        // singletons and empty mask are unused but cheap.
        private readonly double[] _ambiguityBuckets; // length = 1 << PartCount

        // Scratch for the per-tick classifier (mic → mask of HARMs it's
        // currently hitting). Reused each tick to avoid alloc. uint to match
        // _micCurrentlyHittingParts's element type and avoid a per-tick cast.
        private readonly uint[] _micHitMaskScratch;

        // Cached bucket-processing order (narrowest |S| first, then lex).
        // partCount is fixed for the engine's lifetime, so compute once.
        private readonly int[] _bucketOrder;

        private double _lastMeterRefreshTime;
        private const double METER_UPDATE_INTERVAL_SECONDS = 0.1;

        public PartyVocalsCoordinatorEngine(
            InstrumentDifficulty<VocalNote> noteTrack,
            IReadOnlyList<VocalsPart> allParts,
            SyncTrack syncTrack,
            VocalsEngineParameters engineParameters,
            bool isBot,
            int micCount,
            int botPartIndex = 0)
            : base(noteTrack, allParts, syncTrack, engineParameters, isBot,
                   micCount, botPartIndex)
        {
            int partCount = allParts.Count;
            _harmDirectTicks = new double[partCount];
            _ambiguityBuckets = new double[1 << partCount];
            _micHitMaskScratch = new uint[micCount];
            _bucketOrder = ComputeBucketOrder(partCount);
        }

        private static int[] ComputeBucketOrder(int partCount)
        {
            // Bucket indices are kept as int since they index _ambiguityBuckets[].
            // The masks themselves fit in a few bits regardless.
            var masks = new List<int>();
            for (int m = 0; m < (1 << partCount); m++)
            {
                if (PopCount((uint) m) >= 2) masks.Add(m);
            }
            masks.Sort((a, b) =>
            {
                int pa = PopCount((uint) a);
                int pb = PopCount((uint) b);
                if (pa != pb) return pa - pb;
                return a - b;
            });
            return masks.ToArray();
        }

        /// <summary>
        /// Per-tick: classify each mic into "hits HARM mask M" and update
        /// direct + bucket accumulators. Called from UpdateHitLogic AFTER
        /// the base has populated _phraseTicksTotalPerPart, run
        /// AccumulateMicPartHits (which still maintains _micPartHits for
        /// HUD/stats but is no longer the scoring input), and written
        /// per-mic ticksSinceLast into _lastTickMicDeltas.
        /// </summary>
        private void AccumulateAmbiguityScoring()
        {
            int partCount = _phraseTicksTotalPerPart.Length;

            // 1. Classify: for each mic, build the mask of HARMs it could be hitting.
            //    The base's AccumulateMicPartHits already keeps _micCurrentlyHittingParts
            //    populated as a per-tick bitmask — reuse it (uint, no cast).
            for (int i = 0; i < _micCount; i++)
            {
                uint rawMask = _micCurrentlyHittingParts[i];
                uint mask = 0u;
                for (int j = 0; j < partCount; j++)
                {
                    if ((rawMask & (1u << j)) != 0u && _phraseTicksTotalPerPart[j] > 0u)
                        mask |= 1u << j;
                }
                _micHitMaskScratch[i] = mask;
            }

            // 2. Direct credit: binary across mics, but credit per HARM j is the
            //    max of _lastTickMicDeltas[i] across mics that are unambiguously
            //    on HARM j this tick. Rationale: HARM j "was covered" for the
            //    longest span any covering mic can vouch for (per the leniency
            //    model in AccumulateMicPartHits). One mic with a 3-tick delta and
            //    another with a 1-tick delta both unambiguous on HARM j contribute
            //    3 ticks of HARM j coverage, not 4.
            for (int j = 0; j < partCount; j++)
            {
                double maxDelta = 0;
                for (int i = 0; i < _micCount; i++)
                {
                    uint m = _micHitMaskScratch[i];
                    if (PopCount(m) == 1 && (m & (1u << j)) != 0u)
                    {
                        double d = _lastTickMicDeltas[i];
                        if (d > maxDelta) maxDelta = d;
                    }
                }
                _harmDirectTicks[j] += maxDelta;
            }

            // 3. Ambiguity bucket credit: additive across mics. Each ambiguous
            //    mic contributes its own clamped delta to its set's bucket.
            for (int i = 0; i < _micCount; i++)
            {
                uint m = _micHitMaskScratch[i];
                if (PopCount(m) >= 2)
                    _ambiguityBuckets[(int) m] += _lastTickMicDeltas[i];
            }
        }

        private static int PopCount(uint m)
        {
            // 3 bits at most; intrinsic not worth it.
            return (int) (((m >> 0) & 1u) + ((m >> 1) & 1u) + ((m >> 2) & 1u));
        }

        // Per-tick accumulation runs inside the base's UpdateHitLogic, on the same
        // side of the phrase-end check as AccumulateMicPartHits. This ensures
        // boundary-tick credit attributes to the closing phrase (matching the base
        // convention) instead of leaking into the next phrase.
        protected override void OnAfterMicPartHitsAccumulated()
        {
            AccumulateAmbiguityScoring();

            // Speculative refresh on the 100ms throttle so the HUD live view updates.
            // Lives in the per-tick hook (not a separate UpdateHitLogic override)
            // because it's a per-tick concern and avoids needing two override sites.
            if (CurrentTime - _lastMeterRefreshTime >= METER_UPDATE_INTERVAL_SECONDS)
            {
                _lastMeterRefreshTime = CurrentTime;
                RunAllocatorIntoCanonicalMeters(commit: false);
            }
        }

        protected override void ProcessMultiMicPhraseEnd(
            VocalNote phrase, uint phraseTicksTotal, bool isLastPhrase)
        {
            // Final allocation into _canonicalMeters using all accumulated credit.
            RunAllocatorIntoCanonicalMeters(commit: true);

            int partCount = _phraseTicksTotalPerPart.Length;
            int awesomeCount = 0;
            double bestMeter = 0;
            for (int j = 0; j < partCount; j++)
            {
                if (_canonicalMeters[j] >= EngineParameters.PhraseHitPercent) awesomeCount++;
                if (_canonicalMeters[j] > bestMeter) bestMeter = _canonicalMeters[j];
            }

            var grade = awesomeCount switch
            {
                0 => PhraseGrade.Miss,
                1 => PhraseGrade.Awesome,
                2 => PhraseGrade.DoubleAwesome,
                _ => PhraseGrade.TripleAwesome,
            };

            // Snapshot meters for the event payload BEFORE we reset for the next phrase.
            var metersSnapshot = new double[partCount];
            Array.Copy(_canonicalMeters, metersSnapshot, partCount);

            if (grade == PhraseGrade.Miss)
                MissNote(phrase, bestMeter);
            else
                HitNote(phrase);

            OnPartyVocalsPhrase?.Invoke(grade, metersSnapshot, isLastPhrase);

            // Reset our own accumulators for the next phrase. _canonicalMeters and
            // _micPartHits / _phraseTicksTotalPerPart are cleared by the base at
            // lines 500-504 immediately after this method returns; that's a redundant
            // (but harmless) overwrite of _canonicalMeters since the allocator just
            // wrote into it. Letting the base do it keeps the base's reset contract
            // intact for any future maintenance — not worth a custom override path
            // to skip it.
            Array.Clear(_harmDirectTicks, 0, _harmDirectTicks.Length);
            Array.Clear(_ambiguityBuckets, 0, _ambiguityBuckets.Length);
        }

        private void RunAllocatorIntoCanonicalMeters(bool commit)
        {
            int partCount = _phraseTicksTotalPerPart.Length;

            // Working copy of credited[j] so the speculative path doesn't clobber
            // _canonicalMeters mid-computation.
            Span<double> credited = stackalloc double[partCount];
            for (int j = 0; j < partCount; j++)
            {
                uint cap = _phraseTicksTotalPerPart[j];
                credited[j] = cap == 0 ? 0 : Math.Min(_harmDirectTicks[j], cap);
            }

            // Work on a copy of the buckets so the speculative path is non-destructive.
            Span<double> bucketsCopy = stackalloc double[_ambiguityBuckets.Length];
            for (int i = 0; i < _ambiguityBuckets.Length; i++) bucketsCopy[i] = _ambiguityBuckets[i];

            // Bucket processing order: ascending |S|, then lex over included HARM indices.
            // For partCount=3 the order is: {0,1}=3, {0,2}=5, {1,2}=6, {0,1,2}=7.
            // _bucketOrder is computed once in the constructor (ComputeBucketOrder).
            foreach (int S in _bucketOrder)
            {
                if (bucketsCopy[S] <= 0) continue;

                while (bucketsCopy[S] > 0)
                {
                    // Find eligible j ∈ S with credited[j] < capacity[j], pick most-full
                    // (ties broken by lowest index).
                    int chosen = -1;
                    double chosenCredited = -1;
                    for (int j = 0; j < partCount; j++)
                    {
                        if ((S & (1 << j)) == 0) continue;
                        uint cap = _phraseTicksTotalPerPart[j];
                        if (cap == 0 || credited[j] >= cap) continue;
                        if (credited[j] > chosenCredited)
                        {
                            chosenCredited = credited[j];
                            chosen = j;
                        }
                    }

                    if (chosen < 0) break; // no eligible HARM in this bucket; remaining credit discarded

                    double transfer = Math.Min(bucketsCopy[S], _phraseTicksTotalPerPart[chosen] - credited[chosen]);
                    credited[chosen] += transfer;
                    bucketsCopy[S] -= transfer;
                }
            }

            for (int j = 0; j < partCount; j++)
            {
                uint cap = _phraseTicksTotalPerPart[j];
                _canonicalMeters[j] = cap == 0 ? 0 : credited[j] / cap;
            }

            // Mirror best meter into PhraseTicksHit so the HUD combo fill bar tracks.
            if (PhraseTicksTotal is { } total && total > 0)
            {
                double best = 0;
                for (int j = 0; j < partCount; j++)
                    if (_canonicalMeters[j] > best) best = _canonicalMeters[j];
                PhraseTicksHit = best * total;
            }

            if (commit)
            {
                // No state to commit beyond _canonicalMeters write — the buckets/direct
                // arrays are zeroed by ProcessMultiMicPhraseEnd after grading.
            }
        }
    }
}