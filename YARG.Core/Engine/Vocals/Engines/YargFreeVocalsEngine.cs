using System;
using System.Collections.Generic;
using System.Linq;
using YARG.Core.Chart;
using YARG.Core.Input;
using YARG.Core.Logging;

namespace YARG.Core.Engine.Vocals.Engines
{
    public sealed class YargFreeVocalsEngine : VocalsEngine
    {
        public int CurrentTargetHarmonyIndex { get; private set; }

        /// <summary>
        /// Live canonical meter values per HARM part. Updated at rollback cadence and phrase end.
        /// </summary>
        public IReadOnlyList<double> CanonicalMeters => _canonicalMeters;

        // Store reference to all parts for hit testing
        private readonly IReadOnlyList<VocalsPart> _allParts;
        private readonly int _botPartIndex;

        // Resolved bot part for the current tick after applying the per-phrase fallback:
        // if the assigned _botPartIndex has no active phrase, fall back to the lowest-numbered
        // part that does. Updated in UpdateBot, consumed by CheckSingingHit so the bot scores
        // against whatever line it's actually singing.
        private int _currentBotEffectivePartIndex;

        // Multi-mic state. Empty/zero-length when micCount == 1 (single-mic path uses PitchSang).
        private readonly int _micCount;
        private readonly float[] _micPitches;
        private readonly bool[] _micHasSang;
        private readonly uint[] _micLastSingTicks;
        private readonly double[,] _micPartHits;

        private const double ROLLBACK_WINDOW_SECONDS = 0.5; // 500 ms MVP default per design

        // Window state for per-window assignment and N-awesome grading
        private double _lastRollbackTime;
        private readonly double[,] _lastWindowSnapshot;
        private readonly double[] _cumulativeAssignedTicks;
        private readonly double[] _canonicalMeters;
        private readonly uint[] _phraseTicksTotalPerPart;

        public YargFreeVocalsEngine(InstrumentDifficulty<VocalNote> primaryChart, IReadOnlyList<VocalsPart> allParts,
            SyncTrack syncTrack, VocalsEngineParameters engineParameters, bool isBot, int botPartIndex = 0)
            : this(primaryChart, allParts, syncTrack, engineParameters, isBot, micCount: 1, botPartIndex)
        {
        }

        public YargFreeVocalsEngine(
            InstrumentDifficulty<VocalNote> primaryChart,
            IReadOnlyList<VocalsPart> allParts,
            SyncTrack syncTrack,
            VocalsEngineParameters engineParameters,
            bool isBot,
            int micCount,
            int botPartIndex = 0)
            : base(primaryChart, syncTrack, engineParameters, isBot)
        {
            if (micCount < 1 || micCount > 7)
                throw new ArgumentOutOfRangeException(nameof(micCount), "micCount must be between 1 and 7.");

            _allParts = allParts;
            _botPartIndex = Math.Max(0, Math.Min(botPartIndex, allParts.Count - 1));
            _currentBotEffectivePartIndex = _botPartIndex;

            _micCount = micCount;
            _micPitches = new float[micCount];
            _micHasSang = new bool[micCount];
            _micLastSingTicks = new uint[micCount];
            _micPartHits = new double[micCount, allParts.Count];

            // Window state for per-window assignment and N-awesome grading
            _lastRollbackTime = 0;
            _lastWindowSnapshot = new double[micCount, allParts.Count];
            _cumulativeAssignedTicks = new double[allParts.Count];
            _canonicalMeters = new double[allParts.Count];
            _phraseTicksTotalPerPart = new uint[allParts.Count];

            // Build countdowns from all parts for free vocals
            BuildCountdownsFromAllParts(allParts.ToList());
        }

        private void UpdateBotMultiMic(double songTime)
        {
            int partCount = _allParts.Count;
            bool anyMicSang = false;
            VocalNote? representativeNote = null;

            for (int micIdx = 0; micIdx < _micCount; micIdx++)
            {
                int targetPart = micIdx % partCount;

                // Prefer the bot mic's assigned part; if no phrase active, fall back to
                // any other part that does have one (lowest-numbered wins).
                VocalNote? phrase = FindActivePhraseInPart(targetPart);
                if (phrase is null)
                {
                    for (int j = 0; j < partCount; j++)
                    {
                        if (j == targetPart) continue;
                        var fallback = FindActivePhraseInPart(j);
                        if (fallback is not null)
                        {
                            phrase = fallback;
                            break;
                        }
                    }
                }
                if (phrase is null) continue;

                VocalNote? singNote = null;
                foreach (var childNote in phrase.ChildNotes)
                {
                    if (!childNote.IsPercussion
                        && CurrentTick >= childNote.Tick
                        && CurrentTick <= childNote.TotalTickEnd)
                    {
                        singNote = childNote;
                        break;
                    }
                }
                if (singNote is null) continue;

                _micPitches[micIdx] = singNote.PitchAtSongTime(songTime);
                _micHasSang[micIdx] = true;
                anyMicSang = true;
                representativeNote ??= singNote;
            }

            if (anyMicSang)
            {
                HasSang = true;
                // Mirror the single-mic bot path's PitchSang for any legacy single-pitch
                // consumer (HUD particles, OnSing semantics).
                PitchSang = _micPitches[0];
                OnSing?.Invoke(true);
                // Drive needle anchoring: VocalsPlayer's multi-needle update inspects
                // _lastTargetNote and IsInThreshold(_lastHitTime). Without these events,
                // every needle falls through to AnchorPitchToOctave's +12-semitone fallback.
                OnTargetNoteChanged?.Invoke(representativeNote!);
                OnHit?.Invoke(true);
            }
            else
            {
                OnHit?.Invoke(false);
            }
        }

        private VocalNote? FindActivePhraseInPart(int partIndex)
        {
            foreach (var partPhrase in _allParts[partIndex].NotePhrases)
            {
                var pn = partPhrase.PhraseParentNote;
                if (CurrentTick >= pn.Tick && CurrentTick <= pn.TotalTickEnd)
                {
                    return pn;
                }
            }
            return null;
        }

        protected override void UpdateBot(double songTime)
        {
            if (!IsBot)
            {
                return;
            }

            IsStarPowerInputActive = CanStarPowerActivate && !IsStarPowerInputActive;

            // Party Vocals bot: simulate one vocalist per HARM part (cycling if mic count
            // exceeds part count). Each simulated mic produces the perfect pitch for its
            // assigned part. Populates the multi-mic buffers so AccumulateMicPartHits and
            // the rolling-window assignment run identically to a real multi-mic Party
            // Vocals player.
            if (_micCount > 1)
            {
                UpdateBotMultiMic(songTime);
                return;
            }

            var phrase = Notes[NoteIndex];

            // Find the active phrase for the bot. Prefer the assigned _botPartIndex; if no
            // phrase covers the current tick there, fall back to the lowest-numbered part
            // that does have an active phrase. This keeps a bot audible on sections where
            // its assigned HARM line isn't charted (common when a song collapses dual leads
            // into HARM1/2/3 but leaves gaps on individual parts).
            VocalNote? botPhrase = FindActivePhraseInPart(_botPartIndex);
            int effectiveIndex = _botPartIndex;
            if (botPhrase is null)
            {
                for (int i = 0; i < _allParts.Count; i++)
                {
                    if (i == _botPartIndex) continue;
                    var fallback = FindActivePhraseInPart(i);
                    if (fallback is not null)
                    {
                        botPhrase = fallback;
                        effectiveIndex = i;
                        break;
                    }
                }
            }
            _currentBotEffectivePartIndex = effectiveIndex;

            // Search botPhrase directly instead of using GetNoteInPhraseAtSongTick, which
            // short-circuits to the base engine's CarriedVocalNote — that's populated from
            // the primary chart (HARM1 for all Free bots), so it would return a HARM1 note
            // even when botPhrase is HARM2/HARM3. Result: the needle would always sit on
            // HARM1 regardless of the bot's assigned harmony index.
            VocalNote? singNote = null;
            if (botPhrase is not null)
            {
                foreach (var childNote in botPhrase.ChildNotes)
                {
                    if (!childNote.IsPercussion
                        && CurrentTick >= childNote.Tick
                        && CurrentTick <= childNote.TotalTickEnd)
                    {
                        singNote = childNote;
                        break;
                    }
                }
            }
            if (singNote is not null)
            {
                // Bots are queued extra updates to account for in-between "inputs"
                PitchSang = singNote.PitchAtSongTime(songTime);
                HasSang = true;
                OnSing?.Invoke(true);

                // Drive the visual "on note" state for bots: VocalsPlayer's needle path
                // anchors to _lastTargetNote when _lastHitTime is recent, otherwise it
                // applies AnchorPitchToOctave which adds a 12-semitone offset when
                // _lastTargetNote is null. CheckSingingHit's per-tick gating doesn't
                // fire OnTargetNoteChanged for bots (bestPartIndex always matches the
                // initial CurrentTargetHarmonyIndex of 0), so emit here unconditionally.
                OnTargetNoteChanged?.Invoke(singNote);
                OnHit?.Invoke(true);
            }
            else
            {
                // Stop hitting to prevent the hit particles from showing up too much
                OnHit?.Invoke(false);
            }

            // Handle percussion notes
            var percussion = GetNextPercussionNote(phrase, CurrentTick);
            if (percussion is not null && songTime >= percussion.Time)
            {
                HasHit = true;
            }
        }

        protected override void MutateStateWithInput(GameInput gameInput)
        {
            var action = gameInput.GetAction<VocalsAction>();

            if (action is VocalsAction.Hit && gameInput.Button)
            {
                HasHit = true;
            }
            else if (action is VocalsAction.Pitch)
            {
                HasSang = true;
                PitchSang = gameInput.Axis;

                OnSing?.Invoke(true);
            }
            else if (action is VocalsAction.StarPower)
            {
                IsStarPowerInputActive = gameInput.Button;
            }
        }

        protected override void UpdateHitLogic(double time)
        {
            // Quit early if there are no notes left
            if (NoteIndex >= Notes.Count)
            {
                HasSang = false;
                return;
            }

            UpdateBot(time);

            var phrase = Notes[NoteIndex];
            PhraseTicksTotal ??= GetTicksInPhrase(phrase);

            // Populate per-part tick totals for the current phrase
            for (int j = 0; j < _allParts.Count; j++)
            {
                _phraseTicksTotalPerPart[j] = GetTicksInPhraseForPart(_allParts[j]);
                // If part j has no active phrase, set to 0 (assignment will skip it).
            }

            CheckForNoteHit();

            // Multi-mic per-mic-per-part hidden accumulation. Runs for both humans (real mic
            // input) and Party Vocals bots (synthetic pitches populated in UpdateBotMultiMic).
            // Single-mic bots still use the single-pitch path via UpdateBot.
            if (_micCount > 1)
            {
                AccumulateMicPartHits();

                // Per-window visual rollback cadence. Does not consume the hidden buffer.
                if (CurrentTime - _lastRollbackTime >= ROLLBACK_WINDOW_SECONDS)
                {
                    CommitWindowAssignment();
                    _lastRollbackTime = CurrentTime;
                }
            }

            // Check for the end of a phrase
            if (CurrentTick > phrase.TickEnd)
            {
                bool hasNotes = PhraseTicksTotal.Value != 0;
                bool isLastPhrase = NoteIndex == Notes.Count - 1;

                if (_micCount > 1)
                {
                    // Final window commit
                    CommitWindowAssignment();

                    // Derive grade
                    int awesomeCount = 0;
                    double awesomeThreshold = EngineParameters.PhraseHitPercent;
                    for (int j = 0; j < _canonicalMeters.Length; j++)
                    {
                        if (_phraseTicksTotalPerPart[j] == 0) continue;
                        if (_canonicalMeters[j] >= awesomeThreshold) awesomeCount++;
                    }

                    PhraseGrade grade = awesomeCount switch
                    {
                        0 => PhraseGrade.Miss,
                        1 => PhraseGrade.Awesome,
                        2 => PhraseGrade.DoubleAwesome,
                        _ => PhraseGrade.TripleAwesome,
                    };

                    // Score: sum over j of M[j] × PointsPerPhrase for parts present in this phrase.
                    int totalPoints = 0;
                    uint totalTicksHit = 0;
                    uint totalTicksMissed = 0;

                    for (int j = 0; j < _canonicalMeters.Length; j++)
                    {
                        if (_phraseTicksTotalPerPart[j] == 0) continue;
                        totalPoints += (int) Math.Round(_canonicalMeters[j] * EngineParameters.PointsPerPhrase);

                        // Track hit/miss stats for EngineStats
                        totalTicksHit += (uint) Math.Round(_canonicalMeters[j] * _phraseTicksTotalPerPart[j]);
                        totalTicksMissed += (uint) Math.Round((1.0 - _canonicalMeters[j]) * _phraseTicksTotalPerPart[j]);
                    }

                    EngineStats.TicksHit += totalTicksHit;
                    EngineStats.TicksMissed += totalTicksMissed;

                    if (totalPoints > 0) AddScore(totalPoints);

                    // Combo: continue iff at least one meter crossed threshold.
                    if (grade == PhraseGrade.Miss)
                    {
                        ResetCombo();
                    }
                    else
                    {
                        IncrementCombo();
                    }

                    OnPartyVocalsPhrase?.Invoke(grade, _canonicalMeters.ToArray(), isLastPhrase);

                    // Reset all window state for next phrase
                    Array.Clear(_micPartHits, 0, _micPartHits.Length);
                    Array.Clear(_lastWindowSnapshot, 0, _lastWindowSnapshot.Length);
                    Array.Clear(_cumulativeAssignedTicks, 0, _cumulativeAssignedTicks.Length);
                    Array.Clear(_canonicalMeters, 0, _canonicalMeters.Length);
                    Array.Clear(_phraseTicksTotalPerPart, 0, _phraseTicksTotalPerPart.Length);
                    _lastRollbackTime = CurrentTime;

                    // Reset phrase state and advance to next phrase
                    PhraseTicksHit = 0;
                    PhraseTicksTotal = null;
                    NoteIndex++;
                }
                else
                {
                    // Single-mic path: existing HitNote/MissNote/OnPhraseHit flow unchanged.
                    var percentHit = PhraseTicksHit / PhraseTicksTotal.Value;
                    if (!hasNotes)
                    {
                        percentHit = 1.0;
                    }

                    bool hit = percentHit >= EngineParameters.PhraseHitPercent;
                    if (hit)
                    {
                        EngineStats.TicksHit += PhraseTicksTotal.Value;
                        HitNote(phrase);
                    }
                    else
                    {
                        var ticksHit = (uint) Math.Round(PhraseTicksHit);

                        EngineStats.TicksHit += ticksHit;
                        EngineStats.TicksMissed += PhraseTicksTotal.Value - ticksHit;

                        MissNote(phrase, percentHit);
                    }

                    PhraseTicksHit = 0;
                    PhraseTicksTotal = null;

                    if (hasNotes)
                    {
                        OnPhraseHit?.Invoke(percentHit / EngineParameters.PhraseHitPercent, hit, isLastPhrase);
                    }
                }

                UpdateCarriedNote(phrase);
            }
        }

        protected override void CheckForNoteHit()
        {
            CheckSingingHit();
            CheckPercussionHit();
        }

        private void AccumulateMicPartHits()
        {
            var maxLeniency = 1.0 / EngineParameters.ApproximateVocalFps;

            for (int micIndex = 0; micIndex < _micCount; micIndex++)
            {
                if (!_micHasSang[micIndex])
                    continue;

                var lastTick = Math.Max(
                    SyncTrack.TimeToTick(CurrentTime - maxLeniency),
                    _micLastSingTicks[micIndex]);
                var ticksSinceLast = CurrentTick - lastTick;
                _micLastSingTicks[micIndex] = CurrentTick;
                _micHasSang[micIndex] = false;

                if (ticksSinceLast == 0)
                    continue;

                // Snapshot this mic's pitch into PitchSang for the duration of the per-part scan
                // (CanVocalNoteBeHit reads PitchSang internally). Restore afterwards so the
                // single-mic / bot path isn't disturbed if it ran first in this tick.
                var savedPitchSang = PitchSang;
                PitchSang = _micPitches[micIndex];

                for (int partIndex = 0; partIndex < _allParts.Count; partIndex++)
                {
                    foreach (var partPhrase in _allParts[partIndex].NotePhrases)
                    {
                        foreach (var note in partPhrase.PhraseParentNote.ChildNotes)
                        {
                            if (note.IsPercussion) continue;
                            if (CurrentTick < note.Tick || CurrentTick > note.TotalTickEnd) continue;

                            if (CanVocalNoteBeHit(note, out float hitPercent))
                            {
                                _micPartHits[micIndex, partIndex] += ticksSinceLast * hitPercent;
                            }
                        }
                    }
                }

                PitchSang = savedPitchSang;
            }
        }

        private void CheckSingingHit()
        {
            if (!HasSang)
            {
                return;
            }

            HasSang = false;
            var lastSingTick = LastSingTick;
            LastSingTick = CurrentTick;

            // If the last sing detected was on the same tick (or less), skip it
            // since we've already handled that tick.
            if (lastSingTick >= CurrentTick)
            {
                return;
            }

            // Find the current phrase
            if (NoteIndex >= Notes.Count)
            {
                return;
            }

            var phrase = Notes[NoteIndex];

            // Check for singing hits against all parts
            bool hitAnyNote = false;
            float bestHitPercent = 0f;
            int bestPartIndex = CurrentTargetHarmonyIndex;
            VocalNote? bestNote = null;

            // Note: The primary chart (phrase.ChildNotes) is HARM1 from _allParts[0]
            // We only need to check _allParts to avoid double-counting HARM1 notes
            // For bot mode, only check HARM1 (first part)
            // Bots score against the part they're currently singing (which may be a
            // fallback chosen in UpdateBot when the assigned HARM part has no phrase here).
            var partsToCheck = IsBot ?
                _allParts.Skip(_currentBotEffectivePartIndex).Take(1).ToList() :
                _allParts;

            // Check each part for active notes
            for (int partIndex = 0; partIndex < partsToCheck.Count; partIndex++)
            {
                var part = partsToCheck[partIndex];

                // Get notes from this part's phrases
                foreach (var partPhrase in part.NotePhrases)
                {
                    foreach (var note in partPhrase.PhraseParentNote.ChildNotes)
                    {
                        if (!note.IsPercussion &&
                            CurrentTick >= note.Tick &&
                            CurrentTick <= note.TotalTickEnd)
                        {
                            if (CanVocalNoteBeHit(note, out float hitPercent))
                            {
                                hitAnyNote = true;

                                // For free vocals, we take the best hit percent from any note
                                if (hitPercent > bestHitPercent)
                                {
                                    bestHitPercent = hitPercent;
                                    bestPartIndex = partIndex;
                                    bestNote = note;
                                }
                            }
                        }
                    }
                }
            }

            if (hitAnyNote)
            {
                // Update target harmony index only if it changed (retains last value when no match)
                // Only update when we actually hit a note to ensure index retains last value when no part matches
                if (bestPartIndex != CurrentTargetHarmonyIndex)
                {
                    CurrentTargetHarmonyIndex = bestPartIndex;
                    OnTargetNoteChanged?.Invoke(bestNote!);
                }

                // Scale the hit by chart ticks elapsed since the last sing, matching
                // YargVocalsEngine. PhraseTicksTotal is in chart ticks (hundreds to
                // thousands per phrase); previously we just added bestHitPercent
                // (a 0-1 value) once per UpdateHitLogic, so PhraseTicksHit could never
                // approach PhraseTicksTotal and every phrase graded as "messy" no matter
                // how perfectly the singer hit the notes.
                var maxLeniency = 1.0 / EngineParameters.ApproximateVocalFps;
                var lastTick = Math.Max(
                    SyncTrack.TimeToTick(CurrentTime - maxLeniency),
                    lastSingTick);
                var ticksSinceLast = CurrentTick - lastTick;
                PhraseTicksHit += ticksSinceLast * bestHitPercent;

                // Trigger hit event
                if (HasHit)
                {
                    if (IsSoloActive)
                    {
                        Solos[CurrentSoloIndex].NotesHit++;
                    }

                    // Singing (or any noise) can result in a call to CheckPercussionHit() as well, so we need to check SingToActivateStarPower here.
                    if (CanStarPowerActivate && EngineParameters.SingToActivateStarPower)
                    {
                        ActivateStarPower();
                    }
                }
            }
            else
            {
                // Singing (or any noise) can result in a call to CheckPercussionHit() as well, so we need to check SingToActivateStarPower here.
                if (HasHit && CanStarPowerActivate && EngineParameters.SingToActivateStarPower)
                {
                    ActivateStarPower();
                }
            }

            HasHit = false;
        }

        private void CheckPercussionHit()
        {
            if (!HasHit)
            {
                return;
            }

            HasHit = false;

            // Find the current phrase
            if (NoteIndex >= Notes.Count)
            {
                return;
            }

            var phrase = Notes[NoteIndex];

            // Handle percussion notes
            var percussion = GetNextPercussionNote(phrase, CurrentTick);
            if (percussion is not null && CurrentTime >= percussion.Time)
            {
                AddScore(percussion);
                OnNoteHit?.Invoke(NoteIndex, percussion);
            }
        }

        protected override bool CanVocalNoteBeHit(VocalNote note, out float hitPercent)
        {
            // If it is non-pitched, it is always hittable
            if (note.IsNonPitched)
            {
                hitPercent = 1f;
                return true;
            }

            var expectedPitch = note.PitchAtSongTime(CurrentTime);

            // Formula for calculating the distance to the expected pitch, while ignoring octaves
            float distanceToExpected = Math.Min(
                Mod(PitchSang - expectedPitch, 12f),
                Mod(expectedPitch - PitchSang, 12f));

            // If it is within the full points window, award full points
            if (distanceToExpected <= EngineParameters.PitchWindowPerfect)
            {
                hitPercent = 1f;
                return true;
            }

            // If it is outside of the total pitch window, then award no points
            if (distanceToExpected > EngineParameters.PitchWindow)
            {
                hitPercent = 0f;
                return false;
            }

            hitPercent = YargMath.InverseLerpF(
                EngineParameters.PitchWindow,
                EngineParameters.PitchWindowPerfect,
                distanceToExpected);
            return true;
        }

        protected override bool CanNoteBeHit(VocalNote note) => throw new NotImplementedException();

        /// <summary>
        /// Submit a pitch reading for a specific microphone. Multi-mic Party Vocals path.
        /// micIndex must be in [0, micCount). For single-mic profiles (micCount == 1), prefer the
        /// existing PitchSang / QueueInput path — both work, but the legacy path is what existing
        /// tests exercise.
        /// </summary>
        public void SetMicPitch(int micIndex, float pitch)
        {
            if (micIndex < 0 || micIndex >= _micCount)
                throw new ArgumentOutOfRangeException(nameof(micIndex));
            _micPitches[micIndex] = pitch;
            _micHasSang[micIndex] = true;
        }

        /// <summary>
        /// Read the last pitch submitted by a specific microphone.
        /// </summary>
        public float GetMicPitch(int micIndex) => _micPitches[micIndex];

        /// <summary>
        /// Is the given mic currently sitting on a sing note within its assigned part?
        /// Visual gate: prevents needles/trails from appearing on mics whose assigned
        /// HARM line is silent (the engine's pitch fallback covers audio/scoring but
        /// shouldn't make a silent mic look like it's hitting).
        /// </summary>
        public bool IsMicOnAssignedNote(int micIndex)
        {
            if (micIndex < 0 || micIndex >= _micCount) return false;
            int targetPart = micIndex % _allParts.Count;
            var phrase = FindActivePhraseInPart(targetPart);
            if (phrase is null) return false;
            foreach (var childNote in phrase.ChildNotes)
            {
                if (!childNote.IsPercussion
                    && CurrentTick >= childNote.Tick
                    && CurrentTick <= childNote.TotalTickEnd)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Find the assignment of mics to parts that maximizes (in priority order):
        /// 1. Number of canonical meters >= awesomeThreshold (the N-awesome count)
        /// 2. Total sum of canonical meters
        /// 3. Lexicographic tiebreak: mic[0] prefers lowest-numbered part it contributes to,
        ///    then mic[1], etc.
        /// Enumerates all (M+1)^N possibilities where M = parts.Count and N = mic count.
        /// For the supported range (N <= 7, M <= 3), worst case is 16384 enumerations — fine.
        /// </summary>
        internal static (int[] assignment, double[] meters) ComputeBestAssignment(
            double[,] micPartHits,
            uint[] phraseTicksTotal,
            double awesomeThreshold)
        {
            int micCount = micPartHits.GetLength(0);
            int partCount = micPartHits.GetLength(1);

            int choices = partCount + 1; // M parts + "unassigned"
            int totalCombos = 1;
            for (int i = 0; i < micCount; i++) totalCombos *= choices;

            int[] bestAssignment = new int[micCount];
            for (int i = 0; i < micCount; i++) bestAssignment[i] = -1;
            double[] bestMeters = new double[partCount];
            int bestN = -1;
            double bestSum = -1;

            int[] currentAssignment = new int[micCount];
            double[] currentMeters = new double[partCount];

            for (int combo = 0; combo < totalCombos; combo++)
            {
                // Decode combo into per-mic choice (base-`choices` decomposition).
                int rem = combo;
                for (int i = 0; i < micCount; i++)
                {
                    int choice = rem % choices;
                    rem /= choices;
                    currentAssignment[i] = choice == partCount ? -1 : choice;
                }

                // Compute meters under this assignment.
                for (int j = 0; j < partCount; j++) currentMeters[j] = 0;
                for (int i = 0; i < micCount; i++)
                {
                    int assignedPart = currentAssignment[i];
                    if (assignedPart < 0) continue;
                    if (phraseTicksTotal[assignedPart] == 0) continue;
                    currentMeters[assignedPart] += micPartHits[i, assignedPart] / phraseTicksTotal[assignedPart];
                }
                for (int j = 0; j < partCount; j++)
                {
                    if (currentMeters[j] > 1.0) currentMeters[j] = 1.0;
                }

                // Score this assignment.
                int n = 0;
                double sum = 0;
                for (int j = 0; j < partCount; j++)
                {
                    if (currentMeters[j] >= awesomeThreshold) n++;
                    sum += currentMeters[j];
                }

                // Compare: maximize n, then sum, then lexicographic preference.
                bool better = false;
                if (n > bestN) better = true;
                else if (n == bestN && sum > bestSum + 1e-9) better = true;
                else if (n == bestN && Math.Abs(sum - bestSum) < 1e-9)
                {
                    for (int i = 0; i < micCount; i++)
                    {
                        int curr = currentAssignment[i] < 0 ? int.MaxValue : currentAssignment[i];
                        int best = bestAssignment[i] < 0 ? int.MaxValue : bestAssignment[i];
                        if (curr < best) { better = true; break; }
                        if (curr > best) break;
                    }
                }

                if (better)
                {
                    bestN = n;
                    bestSum = sum;
                    Array.Copy(currentAssignment, bestAssignment, micCount);
                    Array.Copy(currentMeters, bestMeters, partCount);
                }
            }

            return (bestAssignment, bestMeters);
        }

        /// <summary>
        /// Get the total ticks for a specific part in the current phrase.
        /// </summary>
        private uint GetTicksInPhraseForPart(VocalsPart part)
        {
            uint totalTime = 0;
            foreach (var partPhrase in part.NotePhrases)
            {
                var phraseNote = partPhrase.PhraseParentNote;
                foreach (var noteInPhrase in phraseNote.ChildNotes)
                {
                    if (noteInPhrase.IsPercussion)
                    {
                        continue;
                    }

                    // If the note continues past the end of the current phrase, clamp it to the end of the phrase instead.
                    totalTime += phraseNote.GetTicksForNote(noteInPhrase);
                }
            }
            return totalTime;
        }

        /// <summary>
        /// Snapshot the hidden buffer, compute delta since last snapshot, run assignment on the
        /// window's delta, and accumulate assigned contributions into canonical meters.
        /// </summary>
        private void CommitWindowAssignment()
        {
            int micCount = _micPartHits.GetLength(0);
            int partCount = _micPartHits.GetLength(1);

            // Solo-only: max over mics for the single part
            if (partCount == 1 && micCount > 1)
            {
                double maxDelta = 0;
                for (int i = 0; i < micCount; i++)
                {
                    double delta = _micPartHits[i, 0] - _lastWindowSnapshot[i, 0];
                    if (delta > maxDelta) maxDelta = delta;
                }
                if (_phraseTicksTotalPerPart[0] > 0)
                {
                    _cumulativeAssignedTicks[0] += maxDelta;
                    _canonicalMeters[0] = Math.Min(1.0, _cumulativeAssignedTicks[0] / _phraseTicksTotalPerPart[0]);
                }
                Array.Copy(_micPartHits, _lastWindowSnapshot, _micPartHits.Length);
                return;
            }

            // Compute per-window delta
            double[,] windowHits = new double[micCount, partCount];
            for (int i = 0; i < micCount; i++)
                for (int j = 0; j < partCount; j++)
                    windowHits[i, j] = _micPartHits[i, j] - _lastWindowSnapshot[i, j];

            // Run assignment on the window's contributions
            var (assignment, _) = ComputeBestAssignment(
                windowHits, _phraseTicksTotalPerPart, EngineParameters.PhraseHitPercent);

            // Accumulate assigned ticks into cumulative totals
            for (int i = 0; i < micCount; i++)
            {
                int part = assignment[i];
                if (part < 0) continue;
                if (_phraseTicksTotalPerPart[part] == 0) continue;
                _cumulativeAssignedTicks[part] += windowHits[i, part];
            }

            // Recompute canonical meters from cumulative totals
            for (int j = 0; j < partCount; j++)
            {
                if (_phraseTicksTotalPerPart[j] == 0)
                {
                    _canonicalMeters[j] = 0;
                    continue;
                }
                _canonicalMeters[j] = Math.Min(1.0, _cumulativeAssignedTicks[j] / _phraseTicksTotalPerPart[j]);
            }

            // Advance snapshot to current state
            Array.Copy(_micPartHits, _lastWindowSnapshot, _micPartHits.Length);
        }

        // Positive remainder
        private static float Mod(float a, float b)
        {
            var remainder = a % b;
            if (remainder < 0)
            {
                if (b < 0)
                {
                    return remainder - b;
                }

                return remainder + b;
            }

            return remainder;
        }
    }
}