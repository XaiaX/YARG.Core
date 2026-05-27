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

        /// <summary>
        /// Per-difficulty raw hit ratio at which a HARM line counts as "Awesome".
        /// Display meters should be normalized against this so the bar fills to
        /// 100% when the threshold is reached.
        /// </summary>
        public double AwesomeThreshold => EngineParameters.PhraseHitPercent;

        /// <summary>
        /// Whether each HARM part has any phrase content in this chart. The Harmony
        /// track always exposes 3 placeholder parts even if the song only charts
        /// HARM1+HARM2, so the HUD uses this to hide empty-lane meters.
        /// </summary>
        public bool PartHasContent(int partIndex)
        {
            if (partIndex < 0 || partIndex >= _allParts.Count) return false;
            return _allParts[partIndex].NotePhrases.Count > 0;
        }

        public int PartCount => _allParts.Count;

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

        // Per-mic bitmask of which parts that mic landed an on-pitch hit on,
        // refreshed each AccumulateMicPartHits tick. Bit j set ⇒ mic landed on
        // part j this tick. Read by the visualization layer (PartyVocalsPlayer)
        // to pick a trail color reflecting what was actually sung, rather than
        // the slot's static assignment.
        private readonly uint[] _micCurrentlyHittingParts;

        private const double ROLLBACK_WINDOW_SECONDS = 0.5; // 500 ms MVP default per design

        // Smoke-test: mics with index >= RANDOM_BEHAVIOR_MIN_MIC_INDEX use random
        // part reassignment (or silence). Threshold matches "mics 1-3 keep their
        // current behavior, mics 4-7 randomize" since the user-facing slots are
        // 1-indexed.
        private const int RANDOM_BEHAVIOR_MIN_MIC_INDEX = 3;
        private const double RANDOM_REASSIGN_INTERVAL_SECONDS = 0.2;
        private const double RANDOM_SILENCE_CHANCE = 0.10;
        private readonly int[] _micRandomTarget;
        private readonly double[] _micRandomNextReassignTime;
        private readonly Random _micRandom;

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
            _micCurrentlyHittingParts = new uint[micCount];

            // Window state for per-window assignment and N-awesome grading
            _lastRollbackTime = 0;
            _lastWindowSnapshot = new double[micCount, allParts.Count];
            _cumulativeAssignedTicks = new double[allParts.Count];
            _canonicalMeters = new double[allParts.Count];
            _phraseTicksTotalPerPart = new uint[allParts.Count];

            // Smoke-test state for mics 4-7: each picks a random target part (or
            // -1 = silent) every RANDOM_REASSIGN_INTERVAL_SECONDS. Lets us visually
            // confirm that extra mics actually move between HARM lines instead of
            // stacking invisibly on top of the first 3. Replace once the per-bot
            // behavior dropdowns land.
            _micRandomTarget = new int[micCount];
            _micRandomNextReassignTime = new double[micCount];
            for (int i = 0; i < micCount; i++)
            {
                _micRandomTarget[i] = i % Math.Max(1, allParts.Count);
                _micRandomNextReassignTime[i] = 0;
            }
            _micRandom = new Random(1234);

            // Build countdowns from all parts for free vocals; exclude percussion so
            // percussion-only stretches show the countdown wheel instead of being
            // hidden as a continuous note stream.
            BuildCountdownsFromAllParts(allParts.ToList(), excludePercussion: true);
        }

        private int ResolveMicTargetPart(int micIdx, double songTime)
        {
            int partCount = _allParts.Count;

            if (micIdx >= RANDOM_BEHAVIOR_MIN_MIC_INDEX)
            {
                if (songTime >= _micRandomNextReassignTime[micIdx])
                {
                    double roll = _micRandom.NextDouble();
                    _micRandomTarget[micIdx] = roll < RANDOM_SILENCE_CHANCE
                        ? -1
                        : _micRandom.Next(0, partCount);
                    _micRandomNextReassignTime[micIdx] = songTime + RANDOM_REASSIGN_INTERVAL_SECONDS;
                }
                return _micRandomTarget[micIdx];
            }

            int assigned = micIdx % partCount;
            if (FindActivePhraseInPart(assigned) is not null) return assigned;
            for (int j = 0; j < partCount; j++)
            {
                if (j == assigned) continue;
                if (FindActivePhraseInPart(j) is not null) return j;
            }
            return -1;
        }

        private void UpdateBotMultiMic(double songTime)
        {
            bool anyMicSang = false;
            VocalNote? representativeNote = null;

            for (int micIdx = 0; micIdx < _micCount; micIdx++)
            {
                int targetPart = ResolveMicTargetPart(micIdx, songTime);
                if (targetPart < 0) continue;

                VocalNote? phrase = FindActivePhraseInPart(targetPart);
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

                // Mirror into mic[0] for single-mic bot free vocals so the per-part
                // canonical-meter accumulation (HARM1/2/3 % HUD) gets data.
                if (_micCount == 1)
                {
                    _micPitches[0] = PitchSang;
                    _micHasSang[0] = true;
                }

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

                // Mirror into mic[0] so single-mic free vocals also feeds the per-part
                // canonical-meter accumulation (used to show HARM1/2/3 % in the HUD).
                if (_micCount == 1)
                {
                    _micPitches[0] = gameInput.Axis;
                    _micHasSang[0] = true;
                }

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

            // Per-mic-per-part hidden accumulation. Runs for all free vocals (incl. single
            // real-mic and single-mic bot) so the HARM1/2/3 % HUD has data. Multi-mic
            // scoring still gates on _micCount > 1 at phrase-end below.
            bool anyMicHit = AccumulateMicPartHits(out VocalNote? repNote);

            // Drive the "on note" visual state for real-mic multi-mic players. Single-mic
            // real-mic uses CheckSingingHit's OnHit, and bots fire OnHit from UpdateBot/
            // UpdateBotMultiMic. Without this, multi-mic real-mic players never set
            // _lastHitTime, so VocalsPlayer's multi-needle gate hides the needles. The
            // gate also requires _lastTargetNote — drive that via OnTargetNoteChanged
            // using whichever note a mic actually landed on this tick.
            if (!IsBot && _micCount > 1)
            {
                if (anyMicHit && repNote is not null)
                {
                    OnTargetNoteChanged?.Invoke(repNote);
                }
                OnHit?.Invoke(anyMicHit);
            }

            // Per-window visual rollback cadence. Does not consume the hidden buffer.
            if (CurrentTime - _lastRollbackTime >= ROLLBACK_WINDOW_SECONDS)
            {
                CommitWindowAssignment();
                _lastRollbackTime = CurrentTime;
            }

            // Mirror the best canonical meter into PhraseTicksHit so the HUD's per-phrase
            // fill bar tracks progress for multi-mic too. CheckSingingHit is the only
            // other writer and it bails when HasSang is false (multi-mic input bypasses
            // it via SetMicPitch), so without this the fill bar stays empty all phrase.
            if (_micCount > 1 && PhraseTicksTotal is { } total && total > 0)
            {
                double bestMeter = 0;
                for (int j = 0; j < _canonicalMeters.Length; j++)
                {
                    if (_phraseTicksTotalPerPart[j] == 0) continue;
                    if (_canonicalMeters[j] > bestMeter) bestMeter = _canonicalMeters[j];
                }
                PhraseTicksHit = bestMeter * total;
            }

            // Check for the end of a phrase
            if (CurrentTick > phrase.TickEnd)
            {
                bool hasNotes = PhraseTicksTotal.Value != 0;
                bool isLastPhrase = NoteIndex == Notes.Count - 1;

                if (_micCount > 1)
                {
                    // Final window commit so the canonical meters reflect the phrase tail.
                    CommitWindowAssignment();

                    // Grade is derived from how many parts crossed the awesome threshold.
                    int awesomeCount = 0;
                    double awesomeThreshold = EngineParameters.PhraseHitPercent;
                    double bestMeter = 0;
                    for (int j = 0; j < _canonicalMeters.Length; j++)
                    {
                        if (_phraseTicksTotalPerPart[j] == 0) continue;
                        if (_canonicalMeters[j] >= awesomeThreshold) awesomeCount++;
                        if (_canonicalMeters[j] > bestMeter) bestMeter = _canonicalMeters[j];
                    }

                    PhraseGrade grade = awesomeCount switch
                    {
                        0 => PhraseGrade.Miss,
                        1 => PhraseGrade.Awesome,
                        2 => PhraseGrade.DoubleAwesome,
                        _ => PhraseGrade.TripleAwesome,
                    };

                    // Reuse the legacy phrase-end path so combo, NotesHit, score, multiplier,
                    // NoteIndex, OnPhraseHit, IsFc etc. all stay consistent with the rest of
                    // the engine. percentHit is the best-matched part's meter; bonus points
                    // for double/triple awesome go through AddScore on top.
                    bool hit = grade != PhraseGrade.Miss;
                    double percentHit = bestMeter;

                    if (hasNotes)
                    {
                        if (hit)
                        {
                            EngineStats.TicksHit += PhraseTicksTotal.Value;
                            HitNote(phrase);
                            // No score bonus for double/triple awesome — per design
                            // (`2026-05-21-party-vocals.md` §Scoring): N-awesome is
                            // display and stats only, does not multiply score.
                        }
                        else
                        {
                            var ticksHit = (uint) Math.Round(PhraseTicksHit);
                            EngineStats.TicksHit += ticksHit;
                            EngineStats.TicksMissed += PhraseTicksTotal.Value - ticksHit;
                            MissNote(phrase, percentHit);
                        }
                    }
                    else
                    {
                        // Empty phrase: count as hit, no score change (mirrors single-mic path
                        // which treats hasNotes=false as percentHit=1.0).
                        HitNote(phrase);
                    }

                    PhraseTicksHit = 0;
                    PhraseTicksTotal = null;

                    if (hasNotes)
                    {
                        OnPhraseHit?.Invoke(percentHit / EngineParameters.PhraseHitPercent, hit, isLastPhrase);
                    }
                    OnPartyVocalsPhrase?.Invoke(grade, _canonicalMeters.ToArray(), isLastPhrase);

                    // Reset all window state for next phrase
                    Array.Clear(_micPartHits, 0, _micPartHits.Length);
                    Array.Clear(_lastWindowSnapshot, 0, _lastWindowSnapshot.Length);
                    Array.Clear(_cumulativeAssignedTicks, 0, _cumulativeAssignedTicks.Length);
                    Array.Clear(_canonicalMeters, 0, _canonicalMeters.Length);
                    Array.Clear(_phraseTicksTotalPerPart, 0, _phraseTicksTotalPerPart.Length);
                    _lastRollbackTime = CurrentTime;
                }
                else
                {
                    // Final per-part commit so the HUD's HARM% reflects the phrase tail
                    // before we clear the meter state for the next phrase.
                    CommitWindowAssignment();
                    Array.Clear(_micPartHits, 0, _micPartHits.Length);
                    Array.Clear(_lastWindowSnapshot, 0, _lastWindowSnapshot.Length);
                    Array.Clear(_cumulativeAssignedTicks, 0, _cumulativeAssignedTicks.Length);
                    Array.Clear(_canonicalMeters, 0, _canonicalMeters.Length);
                    Array.Clear(_phraseTicksTotalPerPart, 0, _phraseTicksTotalPerPart.Length);
                    _lastRollbackTime = CurrentTime;

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

        private bool AccumulateMicPartHits()
        {
            return AccumulateMicPartHits(out _);
        }

        private bool AccumulateMicPartHits(out VocalNote? representativeHitNote)
        {
            var maxLeniency = 1.0 / EngineParameters.ApproximateVocalFps;
            bool anyMicHit = false;
            representativeHitNote = null;

            for (int micIndex = 0; micIndex < _micCount; micIndex++)
            {
                // Reset this mic's "currently hitting parts" bitmask each tick;
                // we'll re-populate below for any parts CanVocalNoteBeHit confirms.
                _micCurrentlyHittingParts[micIndex] = 0u;

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
                                if (hitPercent > 0f)
                                {
                                    anyMicHit = true;
                                    representativeHitNote ??= note;
                                    _micCurrentlyHittingParts[micIndex] |= 1u << partIndex;
                                }
                            }
                        }
                    }
                }

                PitchSang = savedPitchSang;
            }

            return anyMicHit;
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

                // Drive the visual "on note" state for real-mic singers. Without this,
                // VocalsPlayer's single-mic path never sees _lastHitTime set, so the
                // hitting particle trail never plays. Mirrors YargVocalsEngine.
                OnHit?.Invoke(true);

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
                OnHit?.Invoke(false);

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

            // Drive OnSing so VocalsPlayer's all-needles "is anyone singing?" gate
            // (IsInThreshold(_lastSingTime)) actually opens. Without this, party-vocals
            // pitch inputs that bypass MutateStateWithInput leave _lastSingTime null
            // and every per-mic needle stays hidden.
            OnSing?.Invoke(true);
        }

        /// <summary>
        /// Read the last pitch submitted by a specific microphone.
        /// </summary>
        public float GetMicPitch(int micIndex) => _micPitches[micIndex];

        /// <summary>
        /// Is the given mic currently sitting on a sing note within its effective part?
        /// The "effective part" is the mic's assigned part if that part has an active
        /// phrase at the current tick; otherwise the lowest-numbered part that does
        /// (mirrors UpdateBotMultiMic's fallback so the mic still has somewhere to sing
        /// during solo/lead-only sections).
        ///
        /// Returns false in two visually distinct cases:
        /// 1. Effective part has no active phrase at all (the mic is genuinely silent —
        ///    holds last position, no trail).
        /// 2. Effective part has a phrase but no sing note covering CurrentTick (gap
        ///    between notes within a phrase — also silent, holds last position).
        /// </summary>
        /// <summary>
        /// Bitmask of HARM parts that this mic actually landed an on-pitch hit
        /// on during the most recent AccumulateMicPartHits tick. Bit j set ⇒
        /// mic was on part j. Used by the visualization layer to pick a trail
        /// color reflecting what the singer actually sang, not the slot's
        /// static assignment.
        /// </summary>
        public uint GetMicHittingParts(int micIndex)
        {
            if (micIndex < 0 || micIndex >= _micCount) return 0u;
            return _micCurrentlyHittingParts[micIndex];
        }

        /// <summary>
        /// Number of HARM parts in the loaded chart (1 for Solo, 1-3 for Harmony).
        /// Used by visualization to derive a mic's assigned-part index via
        /// <c>micIndex % PartCount</c>.
        /// </summary>
        public int PartCount => _allParts.Count;

        public bool IsMicOnNote(int micIndex)
        {
            if (micIndex < 0 || micIndex >= _micCount) return false;
            int effectivePart = GetEffectivePartForMic(micIndex);
            if (effectivePart < 0) return false;
            var phrase = FindActivePhraseInPart(effectivePart);
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
        /// Which HARM part this mic is effectively singing this tick — its
        /// assigned part if that part has an active phrase, otherwise the lowest-
        /// numbered part with one. Returns -1 if no part has an active phrase
        /// (the mic is silent). Used by visualization to color the needle and
        /// particle trail by the lane being hit, not by the mic's slot index.
        /// </summary>
        public int GetEffectivePartForMic(int micIndex)
        {
            if (micIndex < 0 || micIndex >= _micCount) return -1;
            int partCount = _allParts.Count;

            if (micIndex >= RANDOM_BEHAVIOR_MIN_MIC_INDEX)
            {
                // Random-target state is refreshed in UpdateBotMultiMic; just read it.
                int target = _micRandomTarget[micIndex];
                if (target < 0 || target >= partCount) return -1;
                return FindActivePhraseInPart(target) is not null ? target : -1;
            }

            int assigned = micIndex % partCount;
            if (FindActivePhraseInPart(assigned) is not null) return assigned;
            for (int j = 0; j < partCount; j++)
            {
                if (j == assigned) continue;
                if (FindActivePhraseInPart(j) is not null) return j;
            }
            return -1;
        }

        /// <summary>
        /// Find the assignment of mics to parts that maximizes (in priority order):
        /// 1. Number of canonical meters >= awesomeThreshold (the N-awesome count)
        /// 2. Number of distinct parts that any mic is assigned to (spreads mics across
        ///    HARMs so two singers don't collapse onto the same lane before either
        ///    crosses the awesome threshold)
        /// 3. Total sum of canonical meters
        /// 4. Lexicographic tiebreak: mic[0] prefers lowest-numbered part it contributes to,
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
            int bestDistinct = -1;
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

                // Count distinct parts that any mic was assigned to, but only count parts
                // that actually received hits — assigning a silent mic to an unused part
                // shouldn't game this tiebreak.
                int distinct = 0;
                for (int j = 0; j < partCount; j++)
                {
                    if (currentMeters[j] > 0) distinct++;
                }

                // Compare: maximize n, then distinct-parts-hit, then sum, then lex.
                bool better = false;
                if (n > bestN) better = true;
                else if (n == bestN && distinct > bestDistinct) better = true;
                else if (n == bestN && distinct == bestDistinct && sum > bestSum + 1e-9) better = true;
                else if (n == bestN && distinct == bestDistinct && Math.Abs(sum - bestSum) < 1e-9)
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
                    bestDistinct = distinct;
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
            // Scope to the part-phrase that overlaps the current master phrase.
            // HARM1/2/3 are separate MIDI tracks each carrying their own phrase
            // events; well-charted songs align them by tick, but we match by
            // overlap so a slightly-off chart still produces sane meters.
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