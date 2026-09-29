using System;
using System.Collections.Generic;
using YARG.Core.Chart;
using YARG.Core.Input;
using YARG.Core.Utility;

namespace YARG.Core.Engine.Drums.Engines
{
    /// <summary>Scores native Elite drum notes without converting them to four- or five-lane notes.</summary>
    public class EliteDrumsEngine : BaseEngine<EliteDrumNote, DrumsEngineParameters, DrumsStats>
    {
        public delegate void PadHitEvent(EliteDrumsAction action, bool noteWasHit, bool wereBonusPointsAwarded,
            bool wasOverhitInLane, DrumNoteType type, float velocity);

        public PadHitEvent? OnPadHit;
        public Action? OnOverhit;
        /// <summary>Raised for a visible pedal resolved by accessibility assistance, not a physical strike.</summary>
        public Action<EliteDrumNote>? OnPedalAssisted;

        private readonly bool _autoHiHatPedal;
        private readonly NativeEliteAuthoredLaneMap _authoredLanes;
        private EliteDrumsAction? _action;
        private float? _velocity;

        public EliteDrumsEngine(InstrumentDifficulty<EliteDrumNote> chart, SyncTrack syncTrack,
            DrumsEngineParameters engineParameters, bool isBot, bool isMidiDrumsInput,
            bool autoHiHatPedal = false)
            : base(chart, syncTrack, engineParameters, true, isBot)
        {
            _autoHiHatPedal = autoHiHatPedal;
            _authoredLanes = new NativeEliteAuthoredLaneMap(chart, engineParameters.HitWindow.LaneAutohitWindow);
            EngineStats.OptionalPedalAccuracyEnabled = autoHiHatPedal;
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
                    else if (_autoHiHatPedal && IsPlayablePedal(note))
                    {
                        if (!note.IsBigRockEnding) EngineStats.TotalNotes--;
                        EngineStats.OptionalPedalNotes++;
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
            _processingPhysicalInput = false;
            _faultInInputGroup = false;
            _resolvingAuthoredContinuation = false;
            _authoredLanes?.Reset();
            base.Reset(keepCurrentButtons);
        }

        protected override bool DrainSameTimestampInputs => true;

        protected override void GenerateQueuedUpdates(double nextTime)
        {
            base.GenerateQueuedUpdates(nextTime);
            if (_authoredLanes is null) return;
            foreach (var parent in Notes)
            {
                foreach (var note in parent.AllNotes)
                {
                    if (note.WasHit || note.WasMissed || note.IsInvisibleTerminator) continue;
                    var deadline = _authoredLanes.NextContinuationDeadline(note);
                    if (deadline is { } when && when > CurrentTime && when < nextTime)
                        QueueUpdateTime(when, "native authored-lane cadence");
                }
            }
        }

        private bool _faultInInputGroup;
        protected override void AfterInputTimestamp(double time)
        {
            _processingPhysicalInput = false;
            // Earlier faults suppress earlier cadence but not an already entered
            // new phrase; defer their barrier until exact-T physical priority ends.
            ResolveAuthoredCadence(time);
            if (_faultInInputGroup)
            {
                _authoredLanes.LatchBarrier(time);
                _faultInInputGroup = false;
            }
        }
        protected override void AfterFrameUpdate(double time)
        {
            ResolveAuthoredCadence(time);
            ResolvePedalsThrough(time);
        }

        private void ResolveAuthoredCadenceBefore(double time)
        {
            if (NoteIndex >= Notes.Count) return;
            // Exact-T cadence waits until all physical inputs at T are drained;
            // this first input must not steal a matching later strike in the group.
            ResolveAuthoredCadence(MathUtil.BitDecrement(time));
        }

        private void ResolveAuthoredCadence(double time, EliteDrumsAction? physicalAction = null)
        {
            if (NoteIndex >= Notes.Count) return;
            foreach (var parent in Notes)
            {
                if (parent.Time > time) break;
                foreach (var note in parent.AllNotes)
                {
                    if (note.WasHit || note.WasMissed || note.IsInvisibleTerminator ||
                        (IsBot && note.Time <= time && IsNoteInWindow(note, time)) ||
                        (_autoHiHatPedal && IsPlayablePedal(note)) ||
                        _authoredLanes.NextContinuationDeadline(note) is not { } cadence ||
                        (physicalAction is { } action && Matches(action, note) &&
                            IsNoteInWindow(note, time)) ||
                        time < cadence || !_authoredLanes.CanContinue(note, time)) continue;
                    _resolvingAuthoredContinuation = true;
                    try { HitNote(note); }
                    finally { _resolvingAuthoredContinuation = false; }
                }
            }
        }

        private static bool IsPlayablePedal(EliteDrumNote note) =>
            note.Pad == (int) EliteDrumNote.EliteDrumPad.HatPedal && !note.IsInvisibleTerminator;

        // BaseEngine drains every queued physical/replay input at a timestamp before this
        // finalizer runs. A same-time hand strike therefore cannot steal a later pedal strike.
        // The frame barrier finalizes a group after every physical input at T.
        private void ResolvePedalsThrough(double time) => ResolvePedals(time);

        private void ResolvePedals(double time)
        {
            if (!_autoHiHatPedal) return;
            foreach (var parent in Notes)
            {
                if (parent.Time > time) break;
                foreach (var note in parent.AllNotes)
                {
                    if (!IsPlayablePedal(note) || note.WasHit || note.WasMissed) continue;
                    // Do not leapfrog a still-hittable required parent. Its remaining
                    // ordinary window takes precedence over the optional deadline.
                    bool blocked = false;
                    for (int preceding = NoteIndex; preceding < Notes.Count &&
                        !ReferenceEquals(Notes[preceding], parent); preceding++)
                    {
                        foreach (var required in Notes[preceding].AllNotes)
                        {
                            if (required.WasHit || required.WasMissed || required.IsInvisibleTerminator ||
                                (_autoHiHatPedal && IsPlayablePedal(required))) continue;
                            if (IsNoteInWindow(required, out _, time) || required.Time > time)
                                blocked = true;
                        }
                    }
                    if (blocked) continue;
                    // Once earlier windows expire, journal their misses before
                    // advancing the optional source through the base lifecycle.
                    for (int preceding = NoteIndex; preceding < Notes.Count &&
                        !ReferenceEquals(Notes[preceding], parent); preceding++)
                    {
                        foreach (var required in Notes[preceding].AllNotes)
                        {
                            if (!required.WasHit && !required.WasMissed && !required.IsInvisibleTerminator &&
                                !IsPlayablePedal(required))
                                MissNote(required);
                        }
                    }
                    // A note's own time is the deadline; assisted hits are distinct from
                    // ordinary hit paths and leave score, combo and accuracy untouched.
                    // The bot has already judged the hand members in frame hit logic.
                    // Finalize preceding required members before the optional chord
                    // parent can advance the base lifecycle.
                    if (NoteIndex < Notes.Count && ReferenceEquals(parent, Notes[NoteIndex]))
                    {
                        foreach (var sibling in parent.AllNotes)
                        {
                            if (ReferenceEquals(sibling, note) || sibling.WasHit || sibling.WasMissed ||
                                IsPlayablePedal(sibling) || sibling.IsInvisibleTerminator) continue;
                            if (IsNoteInWindow(sibling, out _, time) || sibling.Time > time)
                                blocked = true;
                            else if (sibling.Time < time) MissNote(sibling);
                        }
                        if (blocked) continue;
                    }
                    note.SetHitState(true, false);
                    _authoredLanes.Refresh(note, note.Time);
                    // Assistance cannot earn a phrase on its own. If every other
                    // member of the ending chord was hit physically, however, this
                    // optional pedal must not prevent that completed chord's award.
                    if (note.IsStarPowerEnd && note.ParentOrSelf.WasFullyHit())
                    {
                        bool hasPhysicalSibling = false;
                        foreach (var member in note.ParentOrSelf.AllNotes)
                        {
                            if (member.IsStarPowerEnd && !ReferenceEquals(member, note) &&
                                member.WasHit && !IsPlayablePedal(member))
                                hasPhysicalSibling = true;
                        }
                        if (hasPhysicalSibling)
                        {
                            AwardStarPower(note);
                            EngineStats.StarPowerPhrasesHit++;
                        }
                    }
                    EngineStats.AssistedPedalNotes++;
                    if (CodaHasStarted && note.IsBigRockEnding)
                        Codas[CurrentCodaIndex].HitLane(note.Time, note.Pad);
                    OnPedalAssisted?.Invoke(note);
                    base.HitNote(note);
                }
            }
        }

        protected override void MutateStateWithInput(GameInput input)
        {
            // Settle checkpoints strictly before this input, but leave exact-T
            // continuation for the complete physical timestamp group.
            ResolveAuthoredCadenceBefore(input.Time);
            _processingPhysicalInput = true;
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
            else if (_action is { } action)
            {
                // Preserve harmless freestyle feedback after the final note (and
                // on an empty chart); there is no note left to adjudicate or overhit.
                OnPadHit?.Invoke(action, false, false, false, DrumNoteType.Neutral, _velocity.GetValueOrDefault());
            }
            _action = null;
            _velocity = null;
            // Scheduled updates run hit logic, not AfterFrameUpdate. A cadence
            // checkpoint therefore resolves here, but never while a physical
            // input is still being adjudicated at the same timestamp.
            if (!_processingPhysicalInput)
                ResolveAuthoredCadence(time);
        }

        private bool _processingPhysicalInput;
        private bool _resolvingAuthoredContinuation;

        protected override void CheckForNoteHit()
        {
            for (int i = NoteIndex; i < Notes.Count; i++)
            {
                var parent = Notes[i];
                bool first = i == NoteIndex;
                bool stop = false;
                // Resolve the requested pad before judging unrelated chord members:
                // MIDI parent order is not physical input priority.
                if (_action.HasValue)
                {
                    foreach (var candidate in parent.AllNotes)
                    {
                        if (candidate.WasHit || candidate.WasMissed || !CanNoteBeHit(candidate) ||
                            !IsNoteInWindow(candidate) ||
                            (_autoHiHatPedal && IsPlayablePedal(candidate) &&
                                candidate.Time < CurrentTime)) continue;
                        bool bonus = ApplyVelocity(candidate);
                        var hitAction = _action.Value;
                        HitNote(candidate);
                        OnPadHit?.Invoke(hitAction, true, bonus, false, candidate.Dynamics,
                            _velocity.GetValueOrDefault());
                        if (bonus)
                        {
                            int points = POINTS_PER_NOTE / 2;
                            AddScore(points);
                            EngineStats.DynamicsBonus += points;
                            if (candidate.IsAccent) EngineStats.AccentsHit++;
                            if (candidate.IsGhost) EngineStats.GhostsHit++;
                        }
                        _action = null;
                        return;
                    }
                }
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
                    if (_autoHiHatPedal && IsPlayablePedal(note) &&
                        (!_action.HasValue || !Matches(_action.Value, note) ||
                            note.Time < CurrentTime))
                    {
                        // An optional pedal does not obstruct a same-tick hand strike.
                        continue;
                    }
                    if (!IsNoteInWindow(note, out bool missed))
                    {
                        if (_autoHiHatPedal && IsPlayablePedal(note) && CurrentTime > note.Time)
                            continue;
                        if (first && missed)
                        {
                            foreach (var sibling in parent.AllNotes)
                            {
                                if (!sibling.WasHit && !sibling.WasMissed && !sibling.IsInvisibleTerminator &&
                                    !(_autoHiHatPedal && IsPlayablePedal(sibling)))
                                {
                                    MissNote(sibling);
                                }
                            }
                        }
                        stop = true;
                        break;
                    }
                    // Matching input was already adjudicated before the chord-order
                    // miss scan above. No second scoring path is permitted here.
                }
                if (stop) break;
            }
            if (_action is { } action)
            {
                if (TryAuthoredPad(action, out int pad) &&
                    _authoredLanes.ProtectStrike(pad, CurrentTime, member => Matches(action, member)))
                {
                    OnPadHit?.Invoke(action, false, false, true, DrumNoteType.Neutral, _velocity.GetValueOrDefault());
                    return;
                }
                OnPadHit?.Invoke(action, false, false, false, DrumNoteType.Neutral, _velocity.GetValueOrDefault());
                // Pedal motion is free outside an active hi-hat sustain. A charted
                // pedal gem still misses normally if no matching strike arrives.
                // Sustain enforcement is not implemented yet; do not penalize an
                // unmatched stomp/splash as an ordinary drum overhit.
                if (action is EliteDrumsAction.EliteStomp or EliteDrumsAction.EliteSplash) return;
                Overhit();
            }
        }

        private static bool TryAuthoredPad(EliteDrumsAction action, out int pad)
        {
            pad = action switch
            {
                EliteDrumsAction.Kick => (int) EliteDrumNote.EliteDrumPad.Kick,
                EliteDrumsAction.EliteStomp or EliteDrumsAction.EliteSplash => (int) EliteDrumNote.EliteDrumPad.HatPedal,
                EliteDrumsAction.EliteSnare => (int) EliteDrumNote.EliteDrumPad.Snare,
                EliteDrumsAction.EliteClosedHiHat or EliteDrumsAction.EliteOpenHiHat or
                    EliteDrumsAction.EliteSizzleHiHat => (int) EliteDrumNote.EliteDrumPad.HiHat,
                EliteDrumsAction.EliteLeftCrash => (int) EliteDrumNote.EliteDrumPad.LeftCrash,
                EliteDrumsAction.EliteTom1 => (int) EliteDrumNote.EliteDrumPad.Tom1,
                EliteDrumsAction.EliteTom2 => (int) EliteDrumNote.EliteDrumPad.Tom2,
                EliteDrumsAction.EliteTom3 => (int) EliteDrumNote.EliteDrumPad.Tom3,
                EliteDrumsAction.EliteRide => (int) EliteDrumNote.EliteDrumPad.Ride,
                EliteDrumsAction.EliteRightCrash => (int) EliteDrumNote.EliteDrumPad.RightCrash,
                _ => -1
            };
            return pad >= 0;
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
                if (note.WasHit || note.WasMissed || note.IsInvisibleTerminator ||
                    (_autoHiHatPedal && IsPlayablePedal(note))) continue;
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
            if (_processingPhysicalInput) _faultInInputGroup = true;
            else _authoredLanes.LatchBarrier(CurrentTime);
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
            if (_action.HasValue && !_resolvingAuthoredContinuation)
                _authoredLanes.Refresh(note, CurrentTime);
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
            if (_autoHiHatPedal && IsPlayablePedal(note))
                EngineStats.OptionalPedalHits++;
            else
                EngineStats.IncrementNotesHit(note, CurrentTime);
            UpdateMultiplier();
            AddScore(note);
            OnNoteHit?.Invoke(NoteIndex, note);
            base.HitNote(note);
        }

        protected override void MissNote(EliteDrumNote note)
        {
            if (note.WasHit || note.WasMissed || (_autoHiHatPedal && IsPlayablePedal(note))) return;
            // Judge against the actual engine clock: a late frame or skip must not
            // retroactively score an authored member after its continuation deadline.
            if (_authoredLanes.CanContinue(note, CurrentTime))
            {
                _resolvingAuthoredContinuation = true;
                try { HitNote(note); }
                finally { _resolvingAuthoredContinuation = false; }
                return;
            }
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
            _authoredLanes.Miss(note);
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
