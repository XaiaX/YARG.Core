using System;
using System.Collections.Generic;
using System.Linq;
using YARG.Core.Chart;
using YARG.Core.Input;
using YARG.Core.Logging;

namespace YARG.Core.Engine.Vocals.Engines
{
    public class YargFreeVocalsEngine : VocalsEngine
    {
        public int CurrentTargetHarmonyIndex { get; private set; }

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

        /// <summary>
        /// Per-tick deltas credited to each part for the current tick. This property
        /// is updated in UpdateHitLogic after per-part credit is committed, allowing
        /// external systems to read the exact credit assigned for this tick.
        /// </summary>
        public IReadOnlyList<double> LastTickPartDeltas => _lastTickPartDeltas;

        // Store reference to all parts for hit testing
        protected readonly IReadOnlyList<VocalsPart> _allParts;
        private readonly int _botPartIndex;

        // Resolved bot part for the current tick after applying the per-phrase fallback:
        // if the assigned _botPartIndex has no active phrase, fall back to the lowest-numbered
        // part that does. Updated in UpdateBot, consumed by CheckSingingHit so the bot scores
        // against whatever line it's actually singing.
        private int _currentBotEffectivePartIndex;

        
        
        
        // Per-part delta for the current tick. Updated in UpdateHitLogic after
        // per-part credit is committed, for external consumption (e.g., coordinator).
        private readonly double[] _lastTickPartDeltas;

        // Per-part hit accumulator for single-mic free vocals
        private readonly double[] _singleMicPartHits;
        // Bitmask of parts that the single mic is hitting this tick
        private uint _singleMicHittingParts;

        public YargFreeVocalsEngine(
            InstrumentDifficulty<VocalNote> primaryChart,
            IReadOnlyList<VocalsPart> allParts,
            SyncTrack syncTrack,
            VocalsEngineParameters engineParameters,
            bool isBot,
            int botPartIndex = 0)
            : base(primaryChart, syncTrack, engineParameters, isBot)
        {
            _allParts = allParts;
            _botPartIndex = Math.Max(0, Math.Min(botPartIndex, allParts.Count - 1));
            _currentBotEffectivePartIndex = _botPartIndex;

            // Initialize fields for single-mic per-part accumulation
            _lastTickPartDeltas = new double[allParts.Count];
            _singleMicPartHits = new double[allParts.Count];
            _singleMicHittingParts = 0u;

            // Build countdowns from all parts for free vocals; exclude percussion so
            // percussion-only stretches show the countdown wheel instead of being
            // hidden as a continuous note stream.
            BuildCountdownsFromAllParts(allParts.ToList(), excludePercussion: true);
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

            // Populate per-part tick totals for the current phrase (local for single-mic)
            var phraseTicksTotalPerPart = new uint[_allParts.Count];
            for (int j = 0; j < _allParts.Count; j++)
            {
                phraseTicksTotalPerPart[j] = GetTicksInPhraseForPart(_allParts[j]);
                // If part j has no active phrase, set to 0 (assignment will skip it).
            }

            CheckForNoteHit();

            // Per-part hit accumulation for single-mic free vocals to feed the HUD's HARM1/2/3 %
            bool anyMicHit = AccumulateMicPartHits(out VocalNote? repNote);

            // For single-mic, CheckSingingHit already handles the visual state through OnHit

            // For single-mic free vocals, PhraseTicksHit is updated directly in CheckSingingHit

            // Check for the end of a phrase
            if (CurrentTick > phrase.TickEnd)
            {
                bool hasNotes = PhraseTicksTotal.Value != 0;
                bool isLastPhrase = NoteIndex == Notes.Count - 1;

                // For single-mic free vocals, reset per-phrase state and run the standard phrase-end flow
                // Note: _singleMicPartHits is maintained for single-mic HUD display
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

                // Update LastTickPartDeltas for external consumption (coordinator)
                UpdateLastTickPartDeltas();

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

            // Reset the "currently hitting parts" bitmask for single-mic
            _singleMicHittingParts = 0u;

            if (!HasSang)
                return false;

            var lastTick = Math.Max(
                SyncTrack.TimeToTick(CurrentTime - maxLeniency),
                LastSingTick);
            var ticksSinceLast = CurrentTick - lastTick;
            LastSingTick = CurrentTick;

            if (ticksSinceLast == 0)
                return false;

            // Accumulate hits for all parts to feed the HUD's HARM1/2/3 %
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
                            _singleMicPartHits[partIndex] += ticksSinceLast * hitPercent;
                            if (hitPercent > 0f)
                            {
                                anyMicHit = true;
                                representativeHitNote ??= note;
                                _singleMicHittingParts |= 1u << partIndex;
                            }
                        }
                    }
                }
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
            // For bot mode, check the part the bot is currently singing (fallback chosen in UpdateBot)
            // For singer mode, check all parts

            // Check each part for active notes
            for (int partIndex = 0; partIndex < _allParts.Count; partIndex++)
            {
                var part = _allParts[partIndex];

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

        /// <summary>
        /// Update LastTickPartDeltas from _singleMicPartHits for external consumption
        /// (used by coordinator to read per-tick credit).
        /// </summary>
        private void UpdateLastTickPartDeltas()
        {
            // For single-mic, copy _singleMicPartHits to _lastTickPartDeltas
            for (int j = 0; j < _singleMicPartHits.Length; j++)
            {
                _lastTickPartDeltas[j] = _singleMicPartHits[j];
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
        /// Get the current pitch for the single mic. Used by the coordinator to read
        /// the sub-engine's current pitch state for visual feedback.
        /// </summary>
        public float GetCurrentPitch() => PitchSang;

        /// <summary>
        /// Get the bitmask of parts that the single mic is hitting this tick.
        /// Used by the coordinator for visual feedback.
        /// </summary>
        public uint GetMicHittingParts() => _singleMicHittingParts;

        /// <summary>
        /// Submit a pitch reading for the single mic. Used by the coordinator under
        /// composition to push per-mic pitch into each sub-engine.
        /// </summary>
        public void SetMicPitch(float pitch)
        {
            PitchSang = pitch;
            HasSang = true;
            OnSing?.Invoke(true);
        }

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