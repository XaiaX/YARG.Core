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

        // Per-mic-per-bucket cumulative credit. At allocation time, the maximum
        // value over mics for a given bucket S is used to cap how much credit
        // any single HARM can receive from that bucket. This prevents the
        // ambiguity stacking shortcut: two mics on a fully-ambiguous (or
        // talkie-harmonized) passage for half a phrase contribute 2 × N/2 = N
        // to the aggregate bucket, but each mic only contributed N/2, so the
        // per-HARM cap of N/2 prevents that bucket from filling a single HARM
        // to capacity. Two mics on a true full-phrase unison still credit both
        // HARMs (bucket = 2N, per-mic max = N, each HARM filled to N).
        private readonly double[,] _bucketPerMic; // [mic, mask]

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
            _bucketPerMic = new double[micCount, 1 << partCount];
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
            //    mic contributes its own clamped delta to its set's bucket. The
            //    per-mic bookkeeping (_bucketPerMic) lets the allocator cap
            //    per-HARM credit at the longest single-mic span — preventing
            //    the stacking shortcut where N mics ambiguous on S for time T
            //    would otherwise allocate N×T total credit to one HARM.
            for (int i = 0; i < _micCount; i++)
            {
                uint m = _micHitMaskScratch[i];
                if (PopCount(m) >= 2)
                {
                    double delta = _lastTickMicDeltas[i];
                    _ambiguityBuckets[(int) m] += delta;
                    _bucketPerMic[i, (int) m] += delta;
                }
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
            // DEBUG
            Console.WriteLine($"=== PhraseEnd. bucket[3]={_ambiguityBuckets[3]:F2}, " +
                $"perMic[0,3]={_bucketPerMic[0,3]:F2}, perMic[1,3]={_bucketPerMic[1,3]:F2}, " +
                $"direct[0]={_harmDirectTicks[0]:F2}, direct[1]={_harmDirectTicks[1]:F2}");

            int partCount = _phraseTicksTotalPerPart.Length;

            // Empty phrase: no content to grade. Treat as a free hit (matches the
            // base engine's behavior and the single-mic path). Still fire the
            // OnPartyVocalsPhrase event so the HUD's banner pipeline stays in sync,
            // graded as Awesome with all-zero meters (the convention for empty
            // phrases — there's nothing to award, but nothing failed either).
            if (phraseTicksTotal == 0)
            {
                HitNote(phrase);
                OnPartyVocalsPhrase?.Invoke(
                    PhraseGrade.Awesome, new double[partCount], isLastPhrase);
                return;
            }

            // Final allocation into _canonicalMeters using all accumulated credit.
            RunAllocatorIntoCanonicalMeters(commit: true);

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
            bool hit = grade != PhraseGrade.Miss;

            // Snapshot meters for the event payload BEFORE we reset for the next phrase.
            var metersSnapshot = new double[partCount];
            Array.Copy(_canonicalMeters, metersSnapshot, partCount);

            // Mirror the base engine's TicksHit/TicksMissed accounting so the
            // end-of-song accuracy percent (VocalsStats.Percent = TicksHit / TotalTicks)
            // reflects real performance. Without these increments TotalTicks stays
            // at 0 and Percent defaults to 1.0 (= 100%) across the whole session.
            if (hit)
            {
                EngineStats.TicksHit += phraseTicksTotal;
                HitNote(phrase);
            }
            else
            {
                var ticksHit = (uint) Math.Round(PhraseTicksHit);
                EngineStats.TicksHit += ticksHit;
                EngineStats.TicksMissed += phraseTicksTotal - ticksHit;
                MissNote(phrase, bestMeter);
            }

            // OnPhraseHit drives VocalsPlayer.IsFc (flipped to false on !fullPoints
            // at VocalsPlayer.cs:486-489) and ShowTextNotifications. Without firing
            // this, the FC tile stays lit through misses.
            OnPhraseHit?.Invoke(bestMeter / EngineParameters.PhraseHitPercent, hit, isLastPhrase);

            OnPartyVocalsPhrase?.Invoke(grade, metersSnapshot, isLastPhrase);

            // Per-phrase resets (including this engine's _harmDirectTicks /
            // _ambiguityBuckets) are handled by ResetMultiMicPhraseState, which the
            // base UpdateHitLogic calls immediately after this method returns.
        }

        /// <summary>
        /// Extend the base per-phrase reset with the coordinator's own scoring
        /// accumulators. Called automatically by the base UpdateHitLogic after
        /// ProcessMultiMicPhraseEnd returns.
        /// </summary>
        protected override void ResetMultiMicPhraseState()
        {
            base.ResetMultiMicPhraseState();
            Array.Clear(_harmDirectTicks, 0, _harmDirectTicks.Length);
            Array.Clear(_ambiguityBuckets, 0, _ambiguityBuckets.Length);
            Array.Clear(_bucketPerMic, 0, _bucketPerMic.Length);
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

            // Per-HARM scratch tracking how much credit each HARM has already received
            // from the CURRENT bucket, to enforce the per-mic-span cap. Reset between
            // buckets.
            Span<double> receivedFromBucket = stackalloc double[partCount];

            // Bucket processing order: ascending |S|, then lex over included HARM indices.
            // For partCount=3 the order is: {0,1}=3, {0,2}=5, {1,2}=6, {0,1,2}=7.
            // _bucketOrder is computed once in the constructor (ComputeBucketOrder).
            foreach (int S in _bucketOrder)
            {
                if (bucketsCopy[S] <= 0) continue;

                // Per-mic-span cap: max credit any single mic contributed to this
                // bucket. Each HARM can receive at most this much credit from this
                // bucket — prevents the stacking shortcut where N mics ambiguous for
                // time T would otherwise pour N×T into one HARM.
                double perMicCap = 0;
                for (int i = 0; i < _micCount; i++)
                {
                    double v = _bucketPerMic[i, S];
                    if (v > perMicCap) perMicCap = v;
                }

                receivedFromBucket.Clear();

                while (bucketsCopy[S] > 0)
                {
                    // Find eligible j ∈ S with credited[j] < capacity[j] AND
                    // receivedFromBucket[j] < perMicCap; pick most-full (ties by lowest index).
                    int chosen = -1;
                    double chosenCredited = -1;
                    for (int j = 0; j < partCount; j++)
                    {
                        if ((S & (1 << j)) == 0) continue;
                        uint cap = _phraseTicksTotalPerPart[j];
                        if (cap == 0 || credited[j] >= cap) continue;
                        if (receivedFromBucket[j] >= perMicCap) continue;
                        if (credited[j] > chosenCredited)
                        {
                            chosenCredited = credited[j];
                            chosen = j;
                        }
                    }

                    if (chosen < 0) break; // no eligible HARM in this bucket; remaining credit discarded

                    double remainingCapacity = _phraseTicksTotalPerPart[chosen] - credited[chosen];
                    double remainingPerMicCap = perMicCap - receivedFromBucket[chosen];
                    double transfer = Math.Min(
                        Math.Min(bucketsCopy[S], remainingCapacity),
                        remainingPerMicCap);
                    if (transfer <= 0) break; // defensive — perMicCap could be 0 if no mics contributed
                    credited[chosen] += transfer;
                    bucketsCopy[S] -= transfer;
                    receivedFromBucket[chosen] += transfer;
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