using System;
using System.Collections.Generic;
using YARG.Core.Chart;
using YARG.Core.Input;

namespace YARG.Core.Engine.Drums.Engines
{
    /// <summary>Scores native Elite drum notes without converting them to four- or five-lane notes.</summary>
    public class EliteDrumsEngine : BaseEngine<EliteDrumNote, DrumsEngineParameters, DrumsStats>
    {
        public delegate void PadHitEvent(EliteDrumsAction action, bool noteWasHit, bool wereBonusPointsAwarded,
            bool wasOverhitInLane, DrumNoteType type, float velocity);

        public PadHitEvent? OnPadHit;
        public Action? OnOverhit;

        private EliteDrumsAction? _action;
        private float? _velocity;

        public EliteDrumsEngine(InstrumentDifficulty<EliteDrumNote> chart, SyncTrack syncTrack,
            DrumsEngineParameters engineParameters, bool isBot, bool isMidiDrumsInput)
            : base(chart, syncTrack, engineParameters, true, isBot)
        {
            // Invisible pedal terminators are chart control events, not playable notes.
            foreach (var parent in Notes)
            {
                foreach (var note in parent.AllNotes)
                {
                    if (note.IsInvisibleTerminator)
                    {
                        // BaseEngine already excludes BRE parents from the denominator.
                        if (!parent.IsBigRockEnding) EngineStats.TotalNotes--;
                    }
                    else if (note.IsAccent)
                    {
                        EngineStats.TotalAccents++;
                    }
                    else if (note.IsGhost)
                    {
                        EngineStats.TotalGhosts++;
                    }
                }
            }
            EngineStats.TotalChords = EngineStats.TotalNotes;
            GetWaitCountdowns(Notes);
        }

        public override void Reset(bool keepCurrentButtons = false)
        {
            _action = null;
            _velocity = null;
            base.Reset(keepCurrentButtons);
        }

        protected override void MutateStateWithInput(GameInput input)
        {
            // Drum presses are axes; a zero axis is a release, not a hit.
            if (input.Axis > 0)
            {
                _action = input.GetAction<EliteDrumsAction>();
                _velocity = input.Axis;
            }
        }

        protected override void UpdateHitLogic(double time)
        {
            UpdateBot(time);
            if (NoteIndex < Notes.Count)
            {
                CheckForNoteHit();
            }
            _action = null;
            _velocity = null;
        }

        protected override void CheckForNoteHit()
        {
            for (int i = NoteIndex; i < Notes.Count; i++)
            {
                var parent = Notes[i];
                bool first = i == NoteIndex;
                bool stop = false;
                // Resolve control-only members before playable hits, regardless of MIDI
                // chord order. They cannot consume an input or block activation completion.
                if (CurrentTime >= parent.Time)
                {
                    foreach (var control in parent.AllNotes)
                    {
                        if (control.IsInvisibleTerminator && !control.WasHit && !control.WasMissed)
                        {
                            control.SetHitState(true, false);
                            base.HitNote(control);
                        }
                    }
                }
                foreach (var note in parent.AllNotes)
                {
                    if (note.WasHit || note.WasMissed)
                    {
                        continue;
                    }
                    if (note.IsInvisibleTerminator) continue;
                    if (!IsNoteInWindow(note, out bool missed))
                    {
                        if (first && missed)
                        {
                            foreach (var sibling in parent.AllNotes)
                            {
                                if (!sibling.WasHit && !sibling.WasMissed && !sibling.IsInvisibleTerminator)
                                {
                                    MissNote(sibling);
                                }
                            }
                        }
                        stop = true;
                        break;
                    }
                    if (_action.HasValue && CanNoteBeHit(note))
                    {
                        bool bonus = ApplyVelocity(note);
                        var hitAction = _action.Value;
                        HitNote(note);
                        OnPadHit?.Invoke(hitAction, true, bonus, false, note.Dynamics, _velocity.GetValueOrDefault());
                        if (bonus)
                        {
                            int points = POINTS_PER_NOTE / 2;
                            AddScore(points);
                            EngineStats.DynamicsBonus += points;
                            if (note.IsAccent) EngineStats.AccentsHit++;
                            if (note.IsGhost) EngineStats.GhostsHit++;
                        }
                        _action = null;
                        stop = true;
                        break;
                    }
                }
                if (stop) break;
            }
            if (_action is { } action)
            {
                OnPadHit?.Invoke(action, false, false, false, DrumNoteType.Neutral, _velocity.GetValueOrDefault());
                Overhit();
            }
        }

        private static bool Matches(EliteDrumsAction action, EliteDrumNote note)
        {
            return (EliteDrumNote.EliteDrumPad) note.Pad switch
            {
                EliteDrumNote.EliteDrumPad.Kick => action == EliteDrumsAction.Kick,
                EliteDrumNote.EliteDrumPad.HatPedal => note.HatPedalType switch
                {
                    EliteDrumNote.EliteDrumsHatPedalType.Stomp => action == EliteDrumsAction.EliteStomp,
                    EliteDrumNote.EliteDrumsHatPedalType.Splash => action == EliteDrumsAction.EliteSplash,
                    _ => false
                },
                EliteDrumNote.EliteDrumPad.Snare => action == EliteDrumsAction.EliteSnare,
                EliteDrumNote.EliteDrumPad.HiHat => note.HatState switch
                {
                    EliteDrumNote.EliteDrumsHatState.Open => action is EliteDrumsAction.EliteOpenHiHat or EliteDrumsAction.EliteSizzleHiHat,
                    EliteDrumNote.EliteDrumsHatState.Closed => action is EliteDrumsAction.EliteClosedHiHat or EliteDrumsAction.EliteSizzleHiHat,
                    _ => action is EliteDrumsAction.EliteClosedHiHat or EliteDrumsAction.EliteOpenHiHat or EliteDrumsAction.EliteSizzleHiHat
                },
                EliteDrumNote.EliteDrumPad.LeftCrash => action == EliteDrumsAction.EliteLeftCrash,
                EliteDrumNote.EliteDrumPad.Tom1 => action == EliteDrumsAction.EliteTom1,
                EliteDrumNote.EliteDrumPad.Tom2 => action == EliteDrumsAction.EliteTom2,
                EliteDrumNote.EliteDrumPad.Tom3 => action == EliteDrumsAction.EliteTom3,
                EliteDrumNote.EliteDrumPad.Ride => action == EliteDrumsAction.EliteRide,
                EliteDrumNote.EliteDrumPad.RightCrash => action == EliteDrumsAction.EliteRightCrash,
                _ => false
            };
        }

        protected override bool CanNoteBeHit(EliteDrumNote note) =>
            !note.IsInvisibleTerminator && _action is { } action && Matches(action, note);

        protected override void UpdateBot(double time)
        {
            if (!IsBot || NoteIndex >= Notes.Count || time < Notes[NoteIndex].Time) return;
            foreach (var note in Notes[NoteIndex].AllNotes)
            {
                if (note.WasHit || note.WasMissed || note.IsInvisibleTerminator) continue;
                _action = (EliteDrumNote.EliteDrumPad) note.Pad switch
                {
                    EliteDrumNote.EliteDrumPad.HatPedal => note.IsStomp ? EliteDrumsAction.EliteStomp : EliteDrumsAction.EliteSplash,
                    EliteDrumNote.EliteDrumPad.Kick => EliteDrumsAction.Kick,
                    EliteDrumNote.EliteDrumPad.Snare => EliteDrumsAction.EliteSnare,
                    EliteDrumNote.EliteDrumPad.HiHat => note.IsOpen ? EliteDrumsAction.EliteOpenHiHat : EliteDrumsAction.EliteClosedHiHat,
                    EliteDrumNote.EliteDrumPad.LeftCrash => EliteDrumsAction.EliteLeftCrash,
                    EliteDrumNote.EliteDrumPad.Tom1 => EliteDrumsAction.EliteTom1,
                    EliteDrumNote.EliteDrumPad.Tom2 => EliteDrumsAction.EliteTom2,
                    EliteDrumNote.EliteDrumPad.Tom3 => EliteDrumsAction.EliteTom3,
                    EliteDrumNote.EliteDrumPad.Ride => EliteDrumsAction.EliteRide,
                    EliteDrumNote.EliteDrumPad.RightCrash => EliteDrumsAction.EliteRightCrash,
                    _ => null
                };
                CheckForNoteHit();
            }
        }

        public void Overhit()
        {
            if (NoteIndex == 0 || NoteIndex >= Notes.Count || IsWaitCountdownActive || IsCodaActive) return;
            if (CodaHasStarted) Codas[CurrentCodaIndex].Overhit();
            if (!Notes[NoteIndex].IsStarPowerStart) StripStarPower(Notes[NoteIndex]);
            ResetCombo();
            EngineStats.RecordOverhit((int?) _action);
            UpdateMultiplier();
            OnOverhit?.Invoke();
        }

        protected override void HitNote(EliteDrumNote note)
        {
            if (note.WasHit || note.WasMissed || note.IsInvisibleTerminator) return;
            note.SetHitState(true, false);
            if (CodaHasStarted && note.IsBigRockEnding)
            {
                SkipPreviousNotes(note.ParentOrSelf);
                base.HitNote(note);
                return;
            }
            SkipPreviousNotes(note.ParentOrSelf);
            if (note.IsStarPower && EngineStats.IsStarPowerActive && EngineParameters.NoStarPowerOverlap)
                StripStarPower(note);
            if (note.IsStarPowerEnd && note.ParentOrSelf.WasFullyHit())
            {
                AwardStarPower(note);
                EngineStats.StarPowerPhrasesHit++;
            }
            if (note.IsStarPowerActivator && CanStarPowerActivate)
            {
                bool complete = true;
                foreach (var sibling in note.ParentOrSelf.AllNotes)
                {
                    if (sibling.IsStarPowerActivator && !sibling.WasHit) complete = false;
                }
                if (complete) ActivateStarPower();
            }
            IncrementCombo();
            EngineStats.IncrementNotesHit(note, CurrentTime);
            UpdateMultiplier();
            AddScore(note);
            OnNoteHit?.Invoke(NoteIndex, note);
            base.HitNote(note);
        }

        protected override void MissNote(EliteDrumNote note)
        {
            if (note.WasHit || note.WasMissed) return;
            if (note.IsInvisibleTerminator)
            {
                note.SetHitState(true, false);
                base.HitNote(note);
                return;
            }
            if (CodaHasStarted && note.IsBigRockEnding)
            {
                note.SetHitState(true, true);
                base.HitNote(note);
                return;
            }
            note.SetMissState(true, false);
            if (note.IsStarPower) StripStarPower(note);
            ResetCombo();
            UpdateMultiplier();
            OnNoteMissed?.Invoke(NoteIndex, note);
            base.MissNote(note);
        }

        private bool ApplyVelocity(EliteDrumNote note)
        {
            if (note.IsNeutral) return false;
            if (IsBot) return true;
            if (_velocity is not { } velocity) return false;
            note.HitVelocity = velocity;
            return note.IsGhost ? velocity < EngineParameters.VelocityThreshold
                : velocity > 1 - EngineParameters.VelocityThreshold;
        }

        protected override void AddScore(EliteDrumNote note)
        {
            AddScore(POINTS_PER_NOTE);
            EngineStats.NoteScore += POINTS_PER_NOTE;
        }

        protected override (int baseScore, int noteScore) CalculateChartScores()
        {
            int score = 0, noteScore = 0, combo = 0;
            foreach (var parent in Notes)
            {
                foreach (var note in parent.AllNotes)
                {
                    if (note.IsInvisibleTerminator || note.IsBigRockEnding) continue;
                    score += POINTS_PER_NOTE * Math.Min(combo / 10 + 1, BaseParameters.MaxMultiplier);
                    noteScore += POINTS_PER_NOTE;
                    combo++;
                }
            }
            return (score, noteScore);
        }

        protected override List<CodaSection> GetCodaSections()
        {
            var sections = new List<CodaSection>();
            foreach (var phrase in Chart.Phrases)
            {
                if (phrase.Type == PhraseType.BigRockEnding)
                    sections.Add(new CodaSection(1, phrase.Time, phrase.TimeEnd));
            }
            return sections;
        }

        protected override bool CanSustainHold(EliteDrumNote note) => false;
        protected override bool ProximalLaneForgivesInput(int inputNote, EliteDrumNote laneNote) => false;
    }
}
