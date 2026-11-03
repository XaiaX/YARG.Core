// pattern: Functional Core
using System;
using System.Collections.Generic;
using System.Linq;
using YARG.Core.Chart;
using YARG.Core.Input;
using YARG.Core.Logging;

namespace YARG.Core.Engine.Drums
{
    public abstract class DrumsEngine : BaseEngine<DrumNote, DrumsEngineParameters,
        DrumsStats>
    {
        public delegate void OverhitEvent();

        public delegate void PadHitEvent(DrumsAction action, bool noteWasHit, bool wereBonusPointsAwarded, bool wasOverhitInLane, DrumNoteType type, float velocity);

        public OverhitEvent? OnOverhit;
        public PadHitEvent?  OnPadHit;

        /// <summary>A second physical strike without a second note lifecycle or dynamics award.</summary>
        public Action<DrumNote, DrumsAction, float, bool>? OnFlamFollowUp;

        private readonly Dictionary<int, (DrumNote Note, double Start, double End)> _pendingFlams = new();
        private sealed class PhysicalHit
        {
            public DrumsAction Action;
            public float? Velocity;
            public double Time;
            public bool FollowUpConsumed;
            public DrumsAction FollowUpAction;
            public float FollowUpVelocity;
        }
        private readonly Dictionary<DrumNote, PhysicalHit> _elitePhysicalHits = new();
        protected bool ClassicArticulationsEnabled => EngineParameters.ClassicArticulationRuleset !=
            DrumsEngineParameters.LEGACY_CLASSIC_ARTICULATIONS;

        private bool IsScoringFlam(DrumNote note) => ClassicArticulationsEnabled &&
            (note.IsHandFlam || note.IsKickFlam && EngineParameters.ClassicArticulationRuleset >= DrumsEngineParameters.CLASSIC_ARTICULATIONS_V2);

        private static bool IsFlatHandFlam(DrumNote note) => note.IsHandFlam && note.IsFlatFlam;

        private void ArmFlamFollowUp(DrumNote note)
        {
            _pendingFlams.Remove(note.Pad);
            if (!IsScoringFlam(note) || IsBot || CodaHasStarted && note.IsBigRockEnding) return;
            var window = CalculateEliteFillOrdinaryWindow(note);
            if (CurrentTime <= note.Time + window.BackEnd)
                _pendingFlams[note.Pad] = (note, note.Time + window.FrontEnd, note.Time + window.BackEnd);
        }

        protected bool TryFlamFollowUp()
        {
            if (!ClassicArticulationsEnabled || PadHit is not { } pad || Action is not { } action ||
                !_pendingFlams.TryGetValue(pad, out var pending)) return false;
            if (CurrentTime > pending.End) { _pendingFlams.Remove(pad); return false; }
            if (CurrentTime < pending.Start) return false;
            _pendingFlams.Remove(pad);
            if (!pending.Note.WasHit && _elitePhysicalHits.TryGetValue(pending.Note, out var physical))
            {
                // V1 may adjudicate this strike before an earlier chart note commits.
                // Preserve the follow-up together with its own first input, never a later input.
                physical.FollowUpConsumed = true;
                physical.FollowUpAction = action;
                physical.FollowUpVelocity = HitVelocity.GetValueOrDefault();
            }
            else
            {
                bool bonus = !IsFlatHandFlam(pending.Note);
                if (bonus) AwardFlamUnit();
                OnFlamFollowUp?.Invoke(pending.Note, action, HitVelocity.GetValueOrDefault(), bonus);
            }
            return true;
        }

        private void AwardFlamUnit()
        {
            int points = GetPointsPerNote();
            AddScore(points);
            EngineStats.NoteScore += points;
        }

        protected void CaptureElitePhysicalHit(DrumNote note, double time)
        {
            _elitePhysicalHits[note] = new PhysicalHit { Action = Action!.Value, Velocity = HitVelocity, Time = time };
            ArmFlamFollowUp(note);
        }

        protected void HitPhysicalNote(DrumNote note, DrumsAction action, float? velocity, bool openFollowUp = true)
        {
            var previousAction = Action;
            var previousPad = PadHit;
            var previousVelocity = HitVelocity;
            Action = action;
            PadHit = note.Pad;
            HitVelocity = velocity;
            try
            {
                if (note.WasHit || note.WasMissed) return;
                bool bonus = !(CodaHasStarted && note.IsBigRockEnding) && ApplyVelocity(note);
                HitNote(note);
                if (openFollowUp) ArmFlamFollowUp(note);
                OnPadHit?.Invoke(action, true, bonus, false, note.Type, velocity.GetValueOrDefault());
                if (bonus)
                {
                    const int velocityBonus = POINTS_PER_NOTE / 2;
                    AddScore(velocityBonus);
                    EngineStats.DynamicsBonus += velocityBonus;
                    if (note.IsAccent) EngineStats.AccentsHit++;
                    else if (note.IsGhost) EngineStats.GhostsHit++;
                }
            }
            finally { Action = previousAction; PadHit = previousPad; HitVelocity = previousVelocity; }
        }

        protected bool IsKickLaneActive;
        protected double KickLaneAutohitExpireTime;

        /// <summary>
        /// The integer value for the pad that was inputted this update. <c>null</c> is none, and the value can
        /// be based off of <see cref="FourLaneDrumPad"/> or <see cref="FiveLaneDrumPad"/>.
        /// </summary>
        protected int? PadHit;

        protected float? HitVelocity;

        protected DrumsAction? Action;

        protected bool IsMidiDrumsInput;

        protected override int WildcardMask => _wildcardMask;

        // Stores the integer representation of a FourLaneKickPad or FiveLaneKickPad, depending on drum mode
        protected int Kick;

        /// <summary>Set true in source to capture live Elite authored-lane diagnostics in Unity.</summary>
        public static bool ELITE_LANE_TRACE_ENABLED = false;

        public static void TraceEliteLane(string message)
        {
            if (ELITE_LANE_TRACE_ENABLED)
            {
                YargLogger.LogInfo($"[EliteLaneTrace] {message}");
            }
        }

        private int _wildcardMask;
        private readonly EliteFillRuntimeScheduler? _eliteFillRuntime;
        private readonly EliteFillPolicyV1? _eliteFillPolicy;

        protected bool IsEliteFillV1Enabled => _eliteFillRuntime is not null;
        public bool EliteFillV1Active => IsEliteFillV1Enabled;
        protected EliteFillRuntimeScheduler? EliteFillRuntime => _eliteFillRuntime;

        protected DrumsEngine(InstrumentDifficulty<DrumNote> chart, SyncTrack syncTrack,
            DrumsEngineParameters engineParameters, bool isBot, bool isMidiDrumsInput)
            : base(chart, syncTrack, engineParameters, true, isBot)
        {
            // ConversionOrigin is deliberately the only chart metadata consulted here.
            // Native and legacy charts therefore remain byte-for-byte on the old path.
            var physicalNoteList = new List<DrumNote>();
            foreach (var parent in Notes)
            {
                foreach (var physical in parent.AllNotes)
                {
                    physicalNoteList.Add(physical);
                }
            }
            var physicalNotes = physicalNoteList.ToArray();
            if (engineParameters.EliteFillRuleset == DrumsEngineParameters.ELITE_FILL_RULESET_V1 &&
                engineParameters.EnableLanes && physicalNotes.Length > 0 &&
                physicalNotes.All(note => note.ConversionOrigin is not null))
            {
                _eliteFillPolicy = new EliteFillPolicyV1(new EliteFillPolicyParameters(
                    entryWindowSeconds: 0.050 * engineParameters.EntryGraceMultiplier));
                // The scheduler expands chart parents into physical notes itself. Passing the
                // already-flattened array would enumerate chord children twice and duplicate
                // dictionary keys for every physical chord member. The authored lane map
                // preserves per-phrase lane identity for overlapping Elite hand lanes.
                var authoredLaneMap = EliteFillAuthoredLaneMap.Build(physicalNotes, chart.EliteDrumAuthoredLanePhraseRecords);
                _eliteFillRuntime = new EliteFillRuntimeScheduler(Notes, _eliteFillPolicy,
                    CalculateEliteFillOrdinaryWindow, authoredLaneMap);
                _eliteFillRuntime.SetLaneAutohitWindow(engineParameters.HitWindow.LaneAutohitWindow);
                TraceEliteLane($"engine-gate v1=on instrument={chart.Instrument} difficulty={chart.Difficulty} " +
                    $"enableLanes={engineParameters.EnableLanes} midi={isMidiDrumsInput} bot={isBot} " +
                    $"physicalNotes={physicalNotes.Length} authoredRecords={chart.EliteDrumAuthoredLanePhraseRecords?.Count ?? 0} " +
                    $"runtimeLanes={_eliteFillRuntime.AuthoredLanes.Count} membership={_eliteFillRuntime.AuthoredMembershipCount}");
            }
            else if (ELITE_LANE_TRACE_ENABLED)
            {
                TraceEliteLane($"engine-gate v1=off instrument={chart.Instrument} difficulty={chart.Difficulty} " +
                    $"ruleset={engineParameters.EliteFillRuleset} enableLanes={engineParameters.EnableLanes} " +
                    $"physicalNotes={physicalNotes.Length} withoutOrigin={physicalNotes.Count(note => note.ConversionOrigin is null)} " +
                    $"midi={isMidiDrumsInput} bot={isBot}");
            }

            _wildcardMask = EngineParameters.Mode switch
            {
                DrumsEngineParameters.DrumMode.NonProFourLane or
                DrumsEngineParameters.DrumMode.ProFourLane => (int) FourLaneDrumPad.Wildcard,
                DrumsEngineParameters.DrumMode.FiveLane => (int) FiveLaneDrumPad.Wildcard,
                _ => -1
            };

            Kick = EngineParameters.Mode switch
            {
                DrumsEngineParameters.DrumMode.NonProFourLane or
                DrumsEngineParameters.DrumMode.ProFourLane => (int) FourLaneDrumPad.Kick,
                DrumsEngineParameters.DrumMode.FiveLane => (int) FiveLaneDrumPad.Kick,
                _ => throw new ArgumentOutOfRangeException("Unreachable.")
            };

            foreach (var note in Notes)
            {
                foreach(var all in note.AllNotes)
                {
                    if (EngineParameters.ClassicArticulationRuleset >= DrumsEngineParameters.CLASSIC_ARTICULATIONS_V2 && all.Pad == Kick)
                        all.Type = DrumNoteType.Neutral;
                    if(all.IsAccent)
                    {
                        EngineStats.TotalAccents++;
                    }
                    else if(all.IsGhost)
                    {
                        EngineStats.TotalGhosts++;
                    }
                }
            }

            GetWaitCountdowns(Notes);
            IsMidiDrumsInput = isMidiDrumsInput;
        }

        public override void Reset(bool keepCurrentButtons = false)
        {
            PadHit = null;
            HitVelocity = null;
            Action = null;

            IsKickLaneActive = false;
            KickLaneAutohitExpireTime = -1;
            _pendingFlams.Clear();
            _elitePhysicalHits.Clear();

            base.Reset(keepCurrentButtons);
            _eliteFillRuntime?.Reset();
        }

        protected override void BeforeCodaStart(double codaStartTime)
        {
            if (_eliteFillRuntime is not null)
            {
                CommitEliteFill(_eliteFillRuntime.FlushBefore(codaStartTime));
            }
        }

        protected IReadOnlyList<EliteFillRuntimeScheduler.Commit> FlushEliteFillBefore(double timestamp)
        {
            return _eliteFillRuntime?.FlushBefore(timestamp) ?? Array.Empty<EliteFillRuntimeScheduler.Commit>();
        }

        protected IReadOnlyList<EliteFillRuntimeScheduler.Commit> ExpireEliteFill(double timestamp)
        {
            return _eliteFillRuntime?.Expire(timestamp) ?? Array.Empty<EliteFillRuntimeScheduler.Commit>();
        }

        protected (double FrontEnd, double BackEnd) CalculateEliteFillOrdinaryWindow(DrumNote note)
        {
            var hitWindow = EngineParameters.HitWindow.CalculateHitWindow(GetAverageNoteDistance(note));
            return (EngineParameters.HitWindow.GetFrontEnd(hitWindow),
                EngineParameters.HitWindow.GetBackEnd(hitWindow));
        }

        protected bool TryAdjudicateEliteFill(DrumNote note, bool wasHit, double timestamp,
            out IReadOnlyList<EliteFillRuntimeScheduler.Commit> commits)
        {
            if (_eliteFillRuntime is null)
            {
                commits = Array.Empty<EliteFillRuntimeScheduler.Commit>();
                return false;
            }

            return _eliteFillRuntime.TryAdjudicate(note, wasHit, timestamp, out commits);
        }

        protected void CommitEliteFill(IReadOnlyList<EliteFillRuntimeScheduler.Commit> commits, string reason = "unspecified")
        {
            foreach (var commit in commits)
            {
                TraceEliteLane($"commit reason={reason} outcome={(commit.WasHit ? "hit" : "miss")} " +
                    $"tick={commit.Note.Tick} pad={commit.Note.Pad} comboBefore={BaseStats.Combo} time={CurrentTime:F6}");
                if (commit.WasHit)
                {
                    if (ClassicArticulationsEnabled && _elitePhysicalHits.TryGetValue(commit.Note, out var physical))
                    {
                        _elitePhysicalHits.Remove(commit.Note);
                        HitPhysicalNote(commit.Note, physical.Action, physical.Velocity, !physical.FollowUpConsumed);
                        if (physical.FollowUpConsumed && !(CodaHasStarted && commit.Note.IsBigRockEnding))
                        {
                            bool bonus = !IsFlatHandFlam(commit.Note);
                            if (bonus) AwardFlamUnit();
                            OnFlamFollowUp?.Invoke(commit.Note, physical.FollowUpAction, physical.FollowUpVelocity, bonus);
                        }
                    }
                    else if (ClassicArticulationsEnabled && IsBot)
                        HitPhysicalNote(commit.Note, ConvertPadToAction(EngineParameters.Mode, commit.Note.Pad), 1);
                    else HitNote(commit.Note);
                }
                else
                {
                    MissNote(commit.Note);
                }
            }
        }

        public virtual void Overhit()
        {
            Overhit(ignoreNativeLaneProtection: false);
        }

        /// <summary>Applies normal overhit handling and reports whether it was accepted.</summary>
        protected bool OverhitWasApplied()
        {
            return Overhit(ignoreNativeLaneProtection: false);
        }

        /// <summary>
        /// Applies normal overhit consequences while bypassing only the flattened native
        /// lane suppressions. Elite V1 uses this after its authored-lane policy has already
        /// decided that an unmatched input is a fault; other modes retain Overhit().
        /// </summary>
        protected bool OverhitIgnoringNativeLaneProtection()
        {
            return Overhit(ignoreNativeLaneProtection: true);
        }

        private bool Overhit(bool ignoreNativeLaneProtection)
        {
            // Can't overhit before first note is hit/missed
            if (NoteIndex == 0)
            {
                return false;
            }

            // Cancel overhit if past last note
            if (NoteIndex > Chart.Notes.Count - 1)
            {
                return false;
            }

            // Cancel overhit if WaitCountdown is active
            if (IsWaitCountdownActive)
            {
                YargLogger.LogFormatTrace("Overhit prevented during WaitCountdown at time: {0}, tick: {1}",
                    CurrentTime, CurrentTick);
                return false;
            }

            // Cancel overhit during coda
            if (IsCodaActive)
            {
                return false;
            }

            if (!ignoreNativeLaneProtection && PadHit != null && ActiveLaneIncludesNote((int) PadHit))
            {
                // Do not count this as an overhit if the last pad hit was part of an active lane
                return false;
            }            

            // Prevent overhit too close to a lane that accepts the overhit
            if (!ignoreNativeLaneProtection && PadHit.HasValue && IsInLaneLeniencyWindow((int)PadHit))
            {
                YargLogger.LogFormatTrace("Overhit prevented by lane end leniency at {0}", CurrentTime);
                return false;
            }

            // Fail coda in post-BRE coda section
            if (CodaHasStarted)
            {
                Codas[CurrentCodaIndex].Overhit();
            }

            if (NoteIndex < Notes.Count)
            {
                // Don't remove the phrase if the current note being overstrummed is the start of a phrase
                if (!Notes[NoteIndex].IsStarPowerStart)
                {
                    StripStarPower(Notes[NoteIndex]);
                }
            }

            var comboBeforeOverhit = BaseStats.Combo;
            ResetCombo();
            TraceEliteLane($"OnOverhit action={Action?.ToString() ?? "none"} pad={PadHit?.ToString() ?? "none"} " +
                $"combo={comboBeforeOverhit}->{BaseStats.Combo} currentTick={CurrentTick}");
            EngineStats.RecordOverhit((int?) Action);

            UpdateMultiplier();

            OnOverhit?.Invoke();
            return true;
        }

        protected override bool ActiveLaneIncludesNote(int inputNote)
        {
            if (inputNote == Kick)
            {
                return IsKickLaneActive;
            }

            return base.ActiveLaneIncludesNote(inputNote);
        }

        protected override bool IsInLaneLeniencyWindow(int inputNote)
        {
            if (inputNote == Kick)
            {
                if (IsKickLaneActive)
                {
                    return false;
                }

                if (
                    NoteIndex < Notes.Count && // There is a next note
                    Notes[NoteIndex].IsKickLaneStart && // That note is a kick lane start
                    Notes[NoteIndex].Time - CurrentTime < EngineParameters.HitWindow.LaneProximityProtectionWindow // That lane is starting soon
                )
                {
                    return true;
                }

                return (
                    NoteIndex > 0 && // There is a previous note
                    Notes[NoteIndex - 1].IsKickLaneEnd && // That note was a kick lane end
                    CurrentTime - Notes[NoteIndex - 1].Time < EngineParameters.HitWindow.LaneProximityProtectionWindow // That lane ended recently
                );
            }

            return base.IsInLaneLeniencyWindow(inputNote);
        }

        protected override void HitNote(DrumNote note)
        {
            HitNote(note, false);
        }

        protected void HitNote(DrumNote note, bool activationAutoHit)
        {
            if (note.WasHit || note.WasMissed)
            {
                YargLogger.LogFormatTrace("Tried to hit/miss note twice (Pad: {0}, Index: {1}, Hit: {2}, Missed: {3})",
                    note.Pad, NoteIndex, note.WasHit, note.WasMissed);
                return;
            }

            note.SetHitState(true, false);
            _pendingFlams.Remove(note.Pad);
            if (!activationAutoHit && PadHit is null && note.IsLane)
            {
                TraceEliteLane($"lane-hit-without-current-pad tick={note.Tick} pad={note.Pad} time={CurrentTime:F6} " +
                    $"v1={IsEliteFillV1Enabled} (see commit reason for V1 hits)");
            }

            // Cancel the rest of hit logic during BRE phrase (no scoring/combo/star power),
            // but still resolve any previous notes that were skipped. BRE gems can be hit
            // out of order while mashing; without this the skipped gems stay unresolved and
            // NoteIndex strands on an already-hit note, soft-locking the engine for the rest
            // of the song.
            // Key on CodaHasStarted, not IsCodaActive: a note is judged at its back-end time,
            // which can fall after the coda's EndTime (where IsCodaActive is already false) for a
            // finale charted exactly on the BRE-end tick. Such a note is IsBigRockEnding and is
            // hidden by the BRE lanes, so it must be suppressed through the whole coda regardless.
            if (CodaHasStarted && note.IsBigRockEnding)
            {
                SkipPreviousNotes(note.ParentOrSelf);
                base.HitNote(note);
                return;
            }

            // V1 terminals are adjudicated by the physical-note journal and committed
            // in chart order; the legacy skip machinery would incorrectly block siblings.
            bool skipped = IsEliteFillV1Enabled ? false : SkipPreviousNotes(note.ParentOrSelf);

            if (note.IsStarPower)
            {
                if (EngineStats.IsStarPowerActive && EngineParameters.NoStarPowerOverlap)
                {
                    StripStarPower(note);
                }
                // Make sure that the note is fully hit, so the last hit note awards the starpower.
                else if (note.IsStarPowerEnd && note.ParentOrSelf.WasFullyHit())
                {
                    AwardStarPower(note);
                    EngineStats.StarPowerPhrasesHit++;
                }
            }

            if (!activationAutoHit && note.IsStarPowerActivator && CanStarPowerActivate && IsActivationComplete(note))
            {
                ActivateStarPower();
            }

            if (note.IsKickLane)
            {
                if (note.IsKickLaneStart)
                {
                    YargLogger.LogFormatTrace("Starting kick lane behavior at time {0}. ", CurrentTime);
                    IsKickLaneActive = true;
                    UpdateKickLaneAutohitExpireTime();
                }
                else if (note.IsKickLaneEnd)
                {
                    YargLogger.LogFormatTrace("Lane ending at {0}", CurrentTime);
                    IsKickLaneActive = false;
                }

                YargLogger.LogFormatTrace("Kick lane note hit at {0}", CurrentTime);
            }


            var comboBeforeHit = BaseStats.Combo;
            IncrementCombo();
            TraceEliteLane($"OnNoteHit tick={note.Tick} pad={note.Pad} origin={note.ConversionOrigin?.ToString() ?? "none"} " +
                $"auto={activationAutoHit} combo={comboBeforeHit}->{BaseStats.Combo} currentTick={CurrentTick}");

            EngineStats.IncrementNotesHit(note, CurrentTime);

            UpdateMultiplier();

            AddScore(note);

            // If it's an auto hit, act as if it *wasn't* hit visually.
            // Score and such is accounted for above.
            if (!activationAutoHit)
            {
                OnNoteHit?.Invoke(NoteIndex, note);
            }

            base.HitNote(note);
        }

        // Check if all activation notes in the note chord have been hit
        private static bool IsActivationComplete(DrumNote drumNote)
        {
            foreach (var note in drumNote.ParentOrSelf.AllNotes)
            {
                if (note.IsStarPowerActivator && !note.WasHit)
                {
                    return false;
                }
            }

            return true;
        }

        protected bool ApplyVelocity(DrumNote hitNote)
        {
            // Neutral notes cannot award bonus points here
            if (hitNote.IsNeutral)
            {
                return false;
            }

            // Bots will always hit at a velocity of 1, just give them the bonus
            if (IsBot)
            {
                return true;
            }

            // Hit velocity was not recorded for this note, bonus will always be false
            if (HitVelocity is not { } lastInputVelocity)
            {
                return false;
            }

            hitNote.HitVelocity = lastInputVelocity;

            // Apply bonus points from successful ghost / accent note hits
            float awardThreshold = EngineParameters.VelocityThreshold;
            float situationalVelocityWindow = EngineParameters.SituationalVelocityWindow;

            var compareNote = hitNote.PreviousNote;

            while (compareNote != null)
            {
                if (hitNote.Time - compareNote.Time > situationalVelocityWindow)
                {
                    // This note is too far in the past to consider for comparison, stop searching
                    compareNote = null;
                    break;
                }

                if (compareNote.HitVelocity != null && compareNote.Pad == hitNote.Pad)
                {
                    // Comparison note is assigned to the same pad and has stored velocity data
                    // Stop searching and use this note for comparison
                    break;
                }

                compareNote = compareNote.PreviousNote;
            }

            if (compareNote != null)
            {
                //compare this note's velocity against the velocity recorded for the last note
                float? relativeVelocityThreshold;

                if (compareNote.Type == hitNote.Type)
                {
                    // Comparison note is the same ghost/accent type as this note
                    // If this note was awarded a velocity bonus, allow multiple consecutive hits at the previous velocity
                    relativeVelocityThreshold = compareNote.HitVelocity;
                }
                else
                {
                    // Comparison note is not of the same ghost/accent type as this note
                    // Award a velocity bonus if this note was hit with a delta value greater than the previous hit
                    relativeVelocityThreshold = compareNote.HitVelocity - awardThreshold;
                }

                awardThreshold = Math.Max(awardThreshold, relativeVelocityThreshold ?? 0);
            }

            bool awardVelocityBonus = false;

            if (hitNote.IsGhost)
            {
                awardVelocityBonus = lastInputVelocity < awardThreshold;
                YargLogger.LogFormatTrace("Ghost note was hit with a velocity of {0} at tick {1}. Bonus awarded: {2}",
                    lastInputVelocity, hitNote.Tick, awardVelocityBonus);
            }
            else if (hitNote.IsAccent)
            {
                awardVelocityBonus = lastInputVelocity > (1 - awardThreshold);
                YargLogger.LogFormatTrace("Accent note was hit with a velocity of {0} at tick {1}. Bonus awarded: {2}",
                    lastInputVelocity, hitNote.Tick, awardVelocityBonus);
            }

            return awardVelocityBonus;
        }

        protected override void GenerateQueuedUpdates(double nextTime)
        {
            base.GenerateQueuedUpdates(nextTime);

            double previousTime = CurrentTime;
            uint previousTick = CurrentTick;
            uint previousSpTick = StarPowerTickPosition;

            if (IsKickLaneActive)
            {
                if (IsTimeBetween(KickLaneAutohitExpireTime, previousTime, nextTime))
                {
                    QueueUpdateTime(KickLaneAutohitExpireTime, "Potential Lane Expiration Time");
                }
            }
        }

        protected override void MissNote(DrumNote note)
        {
            if (note.WasHit || note.WasMissed)
            {
                YargLogger.LogFormatTrace("Tried to hit/miss note twice (Pad: {0}, Index: {1}, Hit: {2}, Missed: {3})",
                    note.Pad, NoteIndex, note.WasHit, note.WasMissed);
                return;
            }

            // BRE notes can't be missed during the coda. Key on CodaHasStarted (not IsCodaActive):
            // the miss is judged at the back-end time, which for an end-tick finale falls after the
            // coda's EndTime where IsCodaActive is already false — so keying on IsCodaActive would
            // let the hidden finale be normal-missed and forfeit the BRE bonus.
            if (CodaHasStarted && note.IsBigRockEnding)
            {
                // Resolve the whole chord (parent + children), matching GuitarEngine: a BRE
                // finale is often a chord, and auto-resolving children removes any dependence on
                // each sub-note being individually missed.
                note.SetHitState(true, true);
                base.HitNote(note);
                return;
            }

            note.SetMissState(true, false);

            if (note.IsStarPower)
            {
                StripStarPower(note);
            }

            if (note.IsKickLane)
            {
                if (note.IsKickLaneStart)
                {
                    YargLogger.LogFormatTrace("Starting kick lane behavior at time {0}. ", CurrentTime);
                    IsKickLaneActive = true;
                }
                else if (note.IsKickLaneEnd)
                {
                    YargLogger.LogFormatTrace("Lane ending at {0}", CurrentTime);
                    IsKickLaneActive = false;
                }

                YargLogger.LogFormatTrace("Kick lane note missed at {0}", CurrentTime);
            }

            var comboBeforeMiss = BaseStats.Combo;
            ResetCombo();
            TraceEliteLane($"OnNoteMissed tick={note.Tick} pad={note.Pad} origin={note.ConversionOrigin?.ToString() ?? "none"} " +
                $"combo={comboBeforeMiss}->{BaseStats.Combo} currentTick={CurrentTick}");

            UpdateMultiplier();

            OnNoteMissed?.Invoke(NoteIndex, note);
            base.MissNote(note);
        }

        protected int GetPointsPerNote()
        {
            return EngineParameters.Mode == DrumsEngineParameters.DrumMode.ProFourLane
                ? POINTS_PER_PRO_NOTE
                : POINTS_PER_NOTE;
        }

        protected override void AddScore(DrumNote note)
        {
            AwardFlamUnit();
            if (IsScoringFlam(note) && (IsFlatHandFlam(note) || IsBot)) AwardFlamUnit();
        }

        protected sealed override (int baseScore, int noteScore) CalculateChartScores()
        {
            double baseScore = 0;
            double noteScore = 0;
            int combo = 0;
            int multiplier;
            foreach (var note in Notes)
            {
                // Exclude BRE notes from base score calculation since they can't be scored
                if (note.IsBigRockEnding)
                {
                    continue;
                }

                // Get the current multiplier given the current combo
                multiplier = Math.Min((combo / 10) + 1, BaseParameters.MaxMultiplier);
                int units = 0;
                foreach (var member in note.AllNotes)
                    units += IsScoringFlam(member) ? 2 : 1;
                double scoreForNote = GetPointsPerNote() * units;
                baseScore += multiplier * scoreForNote;
                noteScore += scoreForNote;
                combo += 1 + note.ChildNotes.Count;
            }

            YargLogger.LogDebug($"[Drums] Base score: {baseScore}, Max Combo: {combo}");
            return ((int) Math.Round(baseScore), (int) Math.Round(noteScore));
        }

        protected override List<CodaSection> GetCodaSections()
        {
            var codaSections = new List<CodaSection>();

            foreach (var phrase in Chart.Phrases)
            {
                if (phrase.Type == PhraseType.BigRockEnding)
                {
                    codaSections.Add(new CodaSection(1, phrase.Time, phrase.TimeEnd));
                }
            }

            return codaSections;
        }

        protected static bool IsTomInput(GameInput input)
        {
            return input.GetAction<DrumsAction>() switch
            {
                DrumsAction.RedDrum or
                    DrumsAction.YellowDrum or
                    DrumsAction.BlueDrum or
                    DrumsAction.GreenDrum => true,
                _ => false,
            };
        }

        protected static bool IsCymbalInput(GameInput input)
        {
            return input.GetAction<DrumsAction>() switch
            {
                DrumsAction.YellowCymbal or
                    DrumsAction.BlueCymbal or
                    DrumsAction.OrangeCymbal or
                    DrumsAction.GreenCymbal => true,
                _ => false,
            };
        }

        protected static bool IsKickInput(GameInput input)
        {
            return input.GetAction<DrumsAction>() == DrumsAction.Kick;
        }

        protected static bool IsNoteInput(GameInput input)
        {
            return IsTomInput(input) || IsCymbalInput(input) || IsKickInput(input);
        }

        protected static int ConvertInputToPad(DrumsEngineParameters.DrumMode mode, DrumsAction action)
        {
            return mode switch
            {
                DrumsEngineParameters.DrumMode.NonProFourLane => action switch
                {
                    DrumsAction.Kick => (int) FourLaneDrumPad.Kick,

                    DrumsAction.RedDrum    => (int) FourLaneDrumPad.RedDrum,
                    DrumsAction.YellowDrum => (int) FourLaneDrumPad.YellowDrum,
                    DrumsAction.BlueDrum   => (int) FourLaneDrumPad.BlueDrum,
                    DrumsAction.GreenDrum  => (int) FourLaneDrumPad.GreenDrum,

                    DrumsAction.YellowCymbal => (int) FourLaneDrumPad.YellowDrum,
                    DrumsAction.BlueCymbal   => (int) FourLaneDrumPad.BlueDrum,
                    DrumsAction.GreenCymbal  => (int) FourLaneDrumPad.GreenDrum,

                    DrumsAction.WildcardPad => (int) FourLaneDrumPad.Wildcard,

                    _ => -1
                },
                DrumsEngineParameters.DrumMode.ProFourLane => action switch
                {
                    DrumsAction.Kick => (int) FourLaneDrumPad.Kick,

                    DrumsAction.RedDrum    => (int) FourLaneDrumPad.RedDrum,
                    DrumsAction.YellowDrum => (int) FourLaneDrumPad.YellowDrum,
                    DrumsAction.BlueDrum   => (int) FourLaneDrumPad.BlueDrum,
                    DrumsAction.GreenDrum  => (int) FourLaneDrumPad.GreenDrum,

                    DrumsAction.YellowCymbal => (int) FourLaneDrumPad.YellowCymbal,
                    DrumsAction.BlueCymbal   => (int) FourLaneDrumPad.BlueCymbal,
                    DrumsAction.GreenCymbal  => (int) FourLaneDrumPad.GreenCymbal,

                    DrumsAction.WildcardPad => (int) FourLaneDrumPad.Wildcard,

                    _ => -1
                },
                DrumsEngineParameters.DrumMode.FiveLane => action switch
                {
                    DrumsAction.Kick => (int) FiveLaneDrumPad.Kick,

                    DrumsAction.RedDrum   => (int) FiveLaneDrumPad.Red,
                    DrumsAction.BlueDrum  => (int) FiveLaneDrumPad.Blue,
                    DrumsAction.GreenDrum => (int) FiveLaneDrumPad.Green,

                    DrumsAction.YellowCymbal => (int) FiveLaneDrumPad.Yellow,
                    DrumsAction.OrangeCymbal => (int) FiveLaneDrumPad.Orange,

                    DrumsAction.WildcardPad => (int) FiveLaneDrumPad.Wildcard,

                    _ => -1
                },
                _ => throw new Exception("Unreachable.")
            };
        }

        protected override void SubmitLaneNote(int newNote, double? inputTime = null)
        {
            if (NoteIndex >= Notes.Count)
            {
                return;
            }

            if (newNote == Kick)
            {
                if (IsKickLaneActive)
                {
                    var currentNote = Notes[NoteIndex].ParentOrSelf;

                    var containsKickLaneNote = false;
                    foreach (var note in currentNote.AllNotes)
                    {
                        if (note.IsKickLane)
                        {
                            containsKickLaneNote = true;
                            break;
                        }
                    }

                    if (!containsKickLaneNote)
                    {
                        // This is either a non-kick in the middle of the kick lane,
                        // or we are in overhit forgiveness window after the kick lane has ended
                        YargLogger.LogFormatTrace("Lane input did not extend KickLaneExpireTime at {0}", CurrentTime);
                        return;
                    }

                    UpdateKickLaneAutohitExpireTime();
                }
            }
            else
            {
                base.SubmitLaneNote(newNote, inputTime);
            }
        }

        protected static DrumsAction ConvertPadToAction(DrumsEngineParameters.DrumMode mode, int pad)
        {
            return mode switch
            {
                DrumsEngineParameters.DrumMode.NonProFourLane => pad switch
                {
                    (int) FourLaneDrumPad.Kick => DrumsAction.Kick,

                    (int) FourLaneDrumPad.RedDrum    => DrumsAction.RedDrum,
                    (int) FourLaneDrumPad.YellowDrum => DrumsAction.YellowDrum,
                    (int) FourLaneDrumPad.BlueDrum   => DrumsAction.BlueDrum,
                    (int) FourLaneDrumPad.GreenDrum  => DrumsAction.GreenDrum,

                    (int) FourLaneDrumPad.Wildcard => DrumsAction.WildcardPad,

                    _ => throw new Exception("Unreachable.")
                },
                DrumsEngineParameters.DrumMode.ProFourLane => pad switch
                {
                    (int) FourLaneDrumPad.Kick => DrumsAction.Kick,

                    (int) FourLaneDrumPad.RedDrum    => DrumsAction.RedDrum,
                    (int) FourLaneDrumPad.YellowDrum => DrumsAction.YellowDrum,
                    (int) FourLaneDrumPad.BlueDrum   => DrumsAction.BlueDrum,
                    (int) FourLaneDrumPad.GreenDrum  => DrumsAction.GreenDrum,

                    (int) FourLaneDrumPad.YellowCymbal => DrumsAction.YellowCymbal,
                    (int) FourLaneDrumPad.BlueCymbal   => DrumsAction.BlueCymbal,
                    (int) FourLaneDrumPad.GreenCymbal  => DrumsAction.GreenCymbal,

                    (int) FourLaneDrumPad.Wildcard => DrumsAction.WildcardPad,

                    _ => throw new Exception("Unreachable.")
                },
                DrumsEngineParameters.DrumMode.FiveLane => pad switch
                {
                    (int) FiveLaneDrumPad.Kick => DrumsAction.Kick,

                    (int) FiveLaneDrumPad.Red   => DrumsAction.RedDrum,
                    (int) FiveLaneDrumPad.Blue  => DrumsAction.BlueDrum,
                    (int) FiveLaneDrumPad.Green => DrumsAction.GreenDrum,

                    (int) FiveLaneDrumPad.Yellow => DrumsAction.YellowCymbal,
                    (int) FiveLaneDrumPad.Orange => DrumsAction.OrangeCymbal,

                    (int) FiveLaneDrumPad.Wildcard => DrumsAction.WildcardPad,

                    _ => throw new Exception("Unreachable.")
                },
                _ => throw new Exception("Unreachable.")
            };
        }

        protected override bool CanSustainHold(DrumNote note) => throw new InvalidOperationException();

        protected override bool ProximalLaneForgivesInput(int inputNote, DrumNote laneNote)
        {
            var (requiredLaneNote, otherNoteInTrill) = GetLaneNotes(laneNote);
            return inputNote == requiredLaneNote ||
                (otherNoteInTrill != -1 && otherNoteInTrill == inputNote) ||
                requiredLaneNote == WildcardMask;
        }

        private void UpdateKickLaneAutohitExpireTime()
        {
            if (Chart.Difficulty is Difficulty.ExpertPlus)
            {
                KickLaneAutohitExpireTime = CurrentTime + EngineParameters.HitWindow.LaneAutohitWindow;
            }
            else
            {
                // When the player has only one pedal, halve the expected input speed for kick lanes
                KickLaneAutohitExpireTime = CurrentTime + (EngineParameters.HitWindow.LaneAutohitWindow * 2);
            }
            YargLogger.LogFormatTrace("KickLaneExpireTime extended to {0}. LaneAutohitWindow {1}. Increment {2}.", KickLaneAutohitExpireTime, EngineParameters.HitWindow.LaneAutohitWindow, KickLaneAutohitExpireTime - CurrentTime);
        }
    }
}