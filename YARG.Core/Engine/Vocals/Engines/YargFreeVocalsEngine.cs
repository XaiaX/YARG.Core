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

        public YargFreeVocalsEngine(InstrumentDifficulty<VocalNote> primaryChart, IReadOnlyList<VocalsPart> allParts,
            SyncTrack syncTrack, VocalsEngineParameters engineParameters, bool isBot)
            : base(primaryChart, syncTrack, engineParameters, isBot)
        {
            _allParts = allParts;
            // Build countdowns from all parts for free vocals
            BuildCountdownsFromAllParts(allParts.ToList());
        }

        protected override void UpdateBot(double songTime)
        {
            if (!IsBot)
            {
                return;
            }

            IsStarPowerInputActive = CanStarPowerActivate && !IsStarPowerInputActive;

            var phrase = Notes[NoteIndex];

            // Handle singing notes - only hit HARM1 for bot mode
            var singNote = GetNoteInPhraseAtSongTick(phrase, CurrentTick);
            if (singNote is not null)
            {
                // Bots are queued extra updates to account for in-between "inputs"
                PitchSang = singNote.PitchAtSongTime(songTime);
                HasSang = true;
                OnSing?.Invoke(true);
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

            // Get all active notes from all parts
            var allActiveNotes = new List<VocalNote>();

            // Check current phrase notes
            foreach (var childNote in phrase.ChildNotes)
            {
                if (!childNote.IsPercussion &&
                    CurrentTick >= childNote.Tick &&
                    CurrentTick <= childNote.TotalTickEnd)
                {
                    allActiveNotes.Add(childNote);
                }
            }

            // Check carried note as well
            if (CarriedVocalNote != null &&
                CurrentTick >= CarriedVocalNote.Tick &&
                CurrentTick <= CarriedVocalNote.TotalTickEnd)
            {
                allActiveNotes.Add(CarriedVocalNote);
            }

            // For bot mode, only check HARM1 (first part)
            var partsToCheck = IsBot ?
                _allParts.Take(1).ToList() :
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
                // Update target harmony index if it changed
                if (bestPartIndex != CurrentTargetHarmonyIndex && bestNote != null)
                {
                    CurrentTargetHarmonyIndex = bestPartIndex;
                    OnTargetNoteChanged?.Invoke(bestNote);
                }

                // Add the hit percent to the phrase ticks hit
                PhraseTicksHit += bestHitPercent;

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