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

        // Store reference to all parts for hit testing
        private readonly IReadOnlyList<VocalsPart> _allParts;
        private readonly int _botPartIndex;

        // Resolved bot part for the current tick after applying the per-phrase fallback:
        // if the assigned _botPartIndex has no active phrase, fall back to the lowest-numbered
        // part that does. Updated in UpdateBot, consumed by CheckSingingHit so the bot scores
        // against whatever line it's actually singing.
        private int _currentBotEffectivePartIndex;

        public YargFreeVocalsEngine(InstrumentDifficulty<VocalNote> primaryChart, IReadOnlyList<VocalsPart> allParts,
            SyncTrack syncTrack, VocalsEngineParameters engineParameters, bool isBot, int botPartIndex = 0)
            : base(primaryChart, syncTrack, engineParameters, isBot)
        {
            _allParts = allParts;
            _botPartIndex = Math.Max(0, Math.Min(botPartIndex, allParts.Count - 1));
            _currentBotEffectivePartIndex = _botPartIndex;
            // Build countdowns from all parts for free vocals
            BuildCountdownsFromAllParts(allParts.ToList());
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

            CheckForNoteHit();

            // Check for the end of a phrase
            if (CurrentTick > phrase.TickEnd)
            {
                bool hasNotes = PhraseTicksTotal.Value != 0;
                bool isLastPhrase = NoteIndex == Notes.Count - 1;

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

                UpdateCarriedNote(phrase);
            }
        }

        protected override void CheckForNoteHit()
        {
            CheckSingingHit();
            CheckPercussionHit();
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