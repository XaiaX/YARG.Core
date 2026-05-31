using System;
using System.Collections.Generic;
using System.Linq;
using YARG.Core.Chart;
using YARG.Core.Input;

namespace YARG.Core.Engine.Vocals.Engines
{
    public sealed class PartyVocalsCoordinatorEngine : VocalsEngine
    {
        // Multi-mic state owned directly by the coordinator (was hoisted into
        // YargFreeVocalsEngine in Phase 3 so the subclass could see them).
        private readonly int _micCount;
        private readonly VocalsPart[] _allParts;
        private readonly bool[] _partHasContent;
        private readonly double[] _canonicalMeters;
        private readonly uint[] _phraseTicksTotalPerPart;
        private readonly double[,] _micPartHits;
        private readonly double[,] _lastWindowSnapshot;
        private readonly double[] _cumulativeAssignedTicks;
        private readonly double[] _lastTickMicDeltas;

        // Per-mic bitmask of which parts each mic is currently hitting.
        // Populated by reading each sub-engine's GetMicHittingParts after driving its tick.
        private readonly uint[] _micCurrentlyHittingParts;

        // Unified per-mic "sang this tick" flag - source of truth for needle visibility
        private readonly bool[] _micSangThisTick;

        // Coordinator-specific ambiguity scoring state
        private readonly double[] _harmDirectTicks;
        private readonly double[] _ambiguityBuckets;
        private readonly double[,] _bucketPerMic;
        private readonly uint[] _micHitMaskScratch;
        private readonly int[] _bucketOrder;
        private double _lastMeterRefreshTime;
        private const double METER_UPDATE_INTERVAL_SECONDS = 0.1;

        // Sub-engines (one per mic, composition)
        private readonly YargFreeVocalsEngine[] _subEngines;

        // Per-phrase tracking for the coordinator's own phrase-end logic
        private uint? _coordinatorPhraseTicksTotal;

        public PartyVocalsCoordinatorEngine(
            InstrumentDifficulty<VocalNote> noteTrack,
            IReadOnlyList<VocalsPart> allParts,
            SyncTrack syncTrack,
            VocalsEngineParameters engineParameters,
            bool isBot,
            int micCount,
            int botPartIndex = 0)
            : base(noteTrack, syncTrack, engineParameters, isBot)
        {
            int partCount = allParts.Count;

            _micCount = micCount;
            _allParts = allParts.ToArray();
            _partHasContent = new bool[partCount];
            _canonicalMeters = new double[partCount];
            _phraseTicksTotalPerPart = new uint[partCount];
            _micPartHits = new double[micCount, partCount];
            _lastWindowSnapshot = new double[micCount, partCount];
            _cumulativeAssignedTicks = new double[partCount];
            _lastTickMicDeltas = new double[micCount];
            _micCurrentlyHittingParts = new uint[micCount];
            _micSangThisTick = new bool[micCount];

            _harmDirectTicks = new double[partCount];
            _ambiguityBuckets = new double[1 << partCount];
            _bucketPerMic = new double[micCount, 1 << partCount];
            _micHitMaskScratch = new uint[micCount];
            _bucketOrder = ComputeBucketOrder(partCount);

            // Build one sub-engine per mic (composition)
            _subEngines = new YargFreeVocalsEngine[micCount];
            for (int i = 0; i < micCount; i++)
            {
                _subEngines[i] = new YargFreeVocalsEngine(
                    noteTrack,
                    allParts,
                    syncTrack,
                    engineParameters,
                    isBot,
                    botPartIndex: i);
            }

            for (int j = 0; j < partCount; j++)
            {
                _partHasContent[j] = allParts[j].NotePhrases.Count > 0;
            }

            BuildCountdownsFromAllParts(allParts.ToList(), excludePercussion: true);
        }

        public IReadOnlyList<YargFreeVocalsEngine> SubEngines => _subEngines;

        public void SetMicPitch(int mic, float pitch)
        {
            if (mic < 0 || mic >= _micCount)
                throw new ArgumentOutOfRangeException(nameof(mic));
            _subEngines[mic].SetMicPitch(pitch);
        }

        public bool PartHasContent(int partIndex)
        {
            if (partIndex < 0 || partIndex >= _allParts.Length) return false;
            return _partHasContent[partIndex];
        }

        public int PartCount => _allParts.Length;

        public IReadOnlyList<double> CanonicalMeters => _canonicalMeters;

        public double AwesomeThreshold => EngineParameters.PhraseHitPercent;

        public uint GetMicHittingParts(int micIndex)
        {
            if (micIndex < 0 || micIndex >= _micCount) return 0u;
            return _micCurrentlyHittingParts[micIndex];
        }

        public float GetMicPitch(int micIndex)
        {
            if (micIndex < 0 || micIndex >= _micCount) return 0f;
            return _subEngines[micIndex].GetCurrentPitch();
        }

        public void ResetMicSangFlags() => Array.Clear(_micSangThisTick, 0, _micSangThisTick.Length);
        public bool DidMicSingThisTick(int mic) =>
            mic >= 0 && mic < _micCount && _micSangThisTick[mic];

        #region VocalsEngine Abstract Implementations

        protected override void MutateStateWithInput(GameInput gameInput)
        {
            int mic = PartyVocalsInput.UnpackMic(gameInput.Action);
            var action = PartyVocalsInput.UnpackAction(gameInput.Action);

            switch (action)
            {
                case VocalsAction.Pitch:
                    // Guarded sub-engine call — mirrors the silent-default pattern of
                    // GetMicPitch/GetMicHittingParts (a malformed/replayed mic index is
                    // dropped, not thrown). Do NOT call the coordinator's own
                    // SetMicPitch(int,float) here: it throws on out-of-range.
                    if (mic >= 0 && mic < _micCount)
                    {
                        _subEngines[mic].SetMicPitch(gameInput.Axis);
                        _micSangThisTick[mic] = true; // unified needle sang-state (see below)
                    }
                    break;
                case VocalsAction.Hit when gameInput.Button:
                    HasHit = true; // coordinator-level; percussion scored here (standalone fix)
                    break;
                case VocalsAction.StarPower:
                    IsStarPowerInputActive = gameInput.Button;
                    break;
            }
        }

        protected override void UpdateHitLogic(double time)
        {
            if (NoteIndex >= Notes.Count)
            {
                HasSang = false;
                return;
            }

            // Drive each sub-engine forward for this tick. Each sub-engine runs its
            // full single-mic lifecycle (MutateStateWithInput → UpdateHitLogic →
            // phrase-end). Sub-engine outputs nobody reads are dead data.
            for (int i = 0; i < _micCount; i++)
            {
                _subEngines[i].Update(time);
            }

            // Mirror time variables from sub-engines (they all share the same SyncTrack,
            // so any one would do — use the first).
            CurrentTime = _subEngines[0].CurrentTime;
            CurrentTick = _subEngines[0].CurrentTick;

            // Score percussion taps at the band level. The coordinator aggregates
            // any mic's Hit into a single HasHit (MutateStateWithInput), so this
            // scores one band hit per tap with no double-count.
            CheckPercussionHit();

            // Read per-tick credit from each sub-engine's LastTickPartDeltas and
            // per-mic hitting-parts bitmask.
            Array.Clear(_lastTickMicDeltas, 0, _lastTickMicDeltas.Length);
            for (int i = 0; i < _micCount; i++)
            {
                var deltas = _subEngines[i].LastTickPartDeltas;
                double totalDelta = 0;
                for (int j = 0; j < deltas.Count && j < _allParts.Length; j++)
                {
                    totalDelta += deltas[j];
                }
                _lastTickMicDeltas[i] = totalDelta;

                // Read the sub-engine's per-mic hitting-parts bitmask
                _micCurrentlyHittingParts[i] = _subEngines[i].GetMicHittingParts();
            }

            var phrase = Notes[NoteIndex];
            _coordinatorPhraseTicksTotal ??= GetTicksInPhrase(phrase);
            PhraseTicksTotal ??= _coordinatorPhraseTicksTotal;

            // Populate per-part tick totals for the current phrase
            for (int j = 0; j < _allParts.Length; j++)
            {
                _phraseTicksTotalPerPart[j] = GetTicksInPhraseForPart(_allParts[j]);
            }

            // Run the coordinator's per-tick ambiguity classifier
            AccumulateAmbiguityScoring();

            // Speculative refresh on the 100ms throttle for live HUD
            if (CurrentTime - _lastMeterRefreshTime >= METER_UPDATE_INTERVAL_SECONDS)
            {
                _lastMeterRefreshTime = CurrentTime;
                RunAllocatorIntoCanonicalMeters(commit: false);
            }

            // Drive visual events for real-mic multi-mic players
            if (!IsBot)
            {
                bool anyMicHit = false;
                for (int i = 0; i < _micCount; i++)
                {
                    if (_micCurrentlyHittingParts[i] != 0u)
                    {
                        anyMicHit = true;
                    }
                }

                OnHit?.Invoke(anyMicHit);
            }

            // Check for end of phrase
            if (CurrentTick > phrase.TickEnd)
            {
                bool hasNotes = _coordinatorPhraseTicksTotal.Value != 0;
                bool isLastPhrase = NoteIndex == Notes.Count - 1;
                uint phraseTicksTotal = _coordinatorPhraseTicksTotal.Value;

                if (phraseTicksTotal == 0)
                {
                    HitNote(phrase);
                    OnPartyVocalsPhrase?.Invoke(
                        PhraseGrade.Awesome, new double[_allParts.Length], isLastPhrase);
                }
                else
                {
                    ProcessPhraseEnd(phrase, phraseTicksTotal, isLastPhrase);
                }

                // Reset per-phrase state
                ResetPhraseState();

                UpdateCarriedNote(phrase);
            }
        }

        protected override void CheckForNoteHit()
        {
            // No-op: sub-engines handle their own hit detection.
        }

        protected override void UpdateBot(double songTime)
        {
            // No-op: each sub-engine runs its own UpdateBot via its own Update cycle.
        }

        protected override bool CanVocalNoteBeHit(VocalNote note, out float hitPercent)
        {
            hitPercent = 0f;
            throw new NotImplementedException(
                "CanVocalNoteBeHit is not called on the coordinator; sub-engines handle pitch matching.");
        }

        protected override bool CanNoteBeHit(VocalNote note) =>
            throw new NotImplementedException();

        #endregion

        // OnPartyVocalsPhrase is inherited from VocalsEngine — no re-declaration needed.

        #region Phrase-End Logic

        private void ProcessPhraseEnd(VocalNote phrase, uint phraseTicksTotal, bool isLastPhrase)
        {
            // Final allocation using all accumulated credit
            RunAllocatorIntoCanonicalMeters(commit: true);

            int partCount = _allParts.Length;
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

            var metersSnapshot = new double[partCount];
            Array.Copy(_canonicalMeters, metersSnapshot, partCount);

            if (hit)
            {
                EngineStats.TicksHit += phraseTicksTotal;
                HitNote(phrase);
            }
            else
            {
                var ticksHit = (uint)Math.Round(PhraseTicksHit);
                EngineStats.TicksHit += ticksHit;
                EngineStats.TicksMissed += phraseTicksTotal - ticksHit;
                MissNote(phrase, bestMeter);
            }

            OnPhraseHit?.Invoke(bestMeter / EngineParameters.PhraseHitPercent, hit, isLastPhrase);
            OnPartyVocalsPhrase?.Invoke(grade, metersSnapshot, isLastPhrase);
        }

        private void ResetPhraseState()
        {
            PhraseTicksHit = 0;
            PhraseTicksTotal = null;
            _coordinatorPhraseTicksTotal = null;

            Array.Clear(_micPartHits, 0, _micPartHits.Length);
            Array.Clear(_lastWindowSnapshot, 0, _lastWindowSnapshot.Length);
            Array.Clear(_cumulativeAssignedTicks, 0, _cumulativeAssignedTicks.Length);
            Array.Clear(_canonicalMeters, 0, _canonicalMeters.Length);
            Array.Clear(_phraseTicksTotalPerPart, 0, _phraseTicksTotalPerPart.Length);
            Array.Clear(_harmDirectTicks, 0, _harmDirectTicks.Length);
            Array.Clear(_ambiguityBuckets, 0, _ambiguityBuckets.Length);
            Array.Clear(_bucketPerMic, 0, _bucketPerMic.Length);

            _lastMeterRefreshTime = CurrentTime;
        }

        #endregion

        #region Ambiguity Scoring

        private void AccumulateAmbiguityScoring()
        {
            int partCount = _phraseTicksTotalPerPart.Length;

            // Classify: for each mic, build the mask of HARMs it could be hitting
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

            // Direct credit: binary across mics
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

            // Ambiguity bucket credit: additive across mics with per-mic-span cap.
            // Use per-part delta (not summed) so the per-mic-span cap correctly
            // limits each HARM to what one mic actually contributed per HARM.
            for (int i = 0; i < _micCount; i++)
            {
                uint m = _micHitMaskScratch[i];
                if (PopCount(m) >= 2)
                {
                    var deltas = _subEngines[i].LastTickPartDeltas;
                    double partDelta = 0;
                    int hitCount = 0;
                    for (int j = 0; j < partCount && j < deltas.Count; j++)
                    {
                        if ((m & (1u << j)) != 0u)
                        {
                            partDelta += deltas[j];
                            hitCount++;
                        }
                    }
                    if (hitCount > 0) partDelta /= hitCount;

                    _ambiguityBuckets[(int)m] += partDelta;
                    _bucketPerMic[i, (int)m] += partDelta;
                }
            }
        }

        #endregion

        #region Allocator

        private void RunAllocatorIntoCanonicalMeters(bool commit)
        {
            int partCount = _phraseTicksTotalPerPart.Length;

            Span<double> credited = stackalloc double[partCount];
            for (int j = 0; j < partCount; j++)
            {
                uint cap = _phraseTicksTotalPerPart[j];
                credited[j] = cap == 0 ? 0 : Math.Min(_harmDirectTicks[j], cap);
            }

            Span<double> bucketsCopy = stackalloc double[_ambiguityBuckets.Length];
            for (int i = 0; i < _ambiguityBuckets.Length; i++) bucketsCopy[i] = _ambiguityBuckets[i];

            Span<double> receivedFromBucket = stackalloc double[partCount];

            foreach (int S in _bucketOrder)
            {
                if (bucketsCopy[S] <= 0) continue;

                double perMicCap = 0;
                for (int i = 0; i < _micCount; i++)
                {
                    double v = _bucketPerMic[i, S];
                    if (v > perMicCap) perMicCap = v;
                }

                receivedFromBucket.Clear();

                while (bucketsCopy[S] > 0)
                {
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

                    if (chosen < 0) break;

                    double remainingCapacity = _phraseTicksTotalPerPart[chosen] - credited[chosen];
                    double remainingPerMicCap = perMicCap - receivedFromBucket[chosen];
                    double transfer = Math.Min(
                        Math.Min(bucketsCopy[S], remainingCapacity),
                        remainingPerMicCap);
                    if (transfer <= 0) break;
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

            // Mirror best meter into PhraseTicksHit for HUD combo fill bar
            if (PhraseTicksTotal is { } total && total > 0)
            {
                double best = 0;
                for (int j = 0; j < partCount; j++)
                    if (_canonicalMeters[j] > best) best = _canonicalMeters[j];
                PhraseTicksHit = best * total;
            }
        }

        private static int[] ComputeBucketOrder(int partCount)
        {
            var masks = new List<int>();
            for (int m = 0; m < (1 << partCount); m++)
            {
                if (PopCount((uint)m) >= 2) masks.Add(m);
            }
            masks.Sort((a, b) =>
            {
                int pa = PopCount((uint)a);
                int pb = PopCount((uint)b);
                if (pa != pb) return pa - pb;
                return a - b;
            });
            return masks.ToArray();
        }

        private static int PopCount(uint m)
        {
            return (int)(((m >> 0) & 1u) + ((m >> 1) & 1u) + ((m >> 2) & 1u));
        }

        #endregion

        #region Helpers

        private uint GetTicksInPhraseForPart(VocalsPart part)
        {
            var masterPhrase = Notes[NoteIndex];
            uint masterStart = masterPhrase.Tick;
            uint masterEnd = masterPhrase.TickEnd;

            uint totalTime = 0;
            foreach (var partPhrase in part.NotePhrases)
            {
                var phraseNote = partPhrase.PhraseParentNote;
                if (phraseNote.Tick >= masterEnd || phraseNote.TickEnd <= masterStart) continue;

                foreach (var noteInPhrase in phraseNote.ChildNotes)
                {
                    if (noteInPhrase.IsPercussion) continue;
                    totalTime += phraseNote.GetTicksForNote(noteInPhrase);
                }
                break;
            }
            return totalTime;
        }

        #endregion

        #region Percussion Scoring

        private void CheckPercussionHit()
        {
            if (!HasHit)
            {
                return;
            }

            HasHit = false;

            if (NoteIndex >= Notes.Count)
            {
                return;
            }

            var phrase = Notes[NoteIndex];
            var percussion = GetNextPercussionNote(phrase, CurrentTick);
            if (percussion is not null && CurrentTime >= percussion.Time)
            {
                // Check if the current time is within the note's bounds
                if (CurrentTime <= percussion.TimeEnd)
                {
                    AddScore(percussion);
                    OnNoteHit?.Invoke(NoteIndex, percussion);
                }
            }
        }

        #endregion
    }
}
