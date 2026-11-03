// pattern: Functional Core
using System;
using System.Collections.Generic;
using System.Linq;
using MoonscraperChartEditor.Song.IO;

namespace YARG.Core.Chart
{
    // pattern: Functional Core
    public class EliteDrumNote : Note<EliteDrumNote>
    {
        private DrumNoteFlags _drumFlags;
        public DrumNoteFlags DrumFlags;
        public int Pad { get; }
        /// <summary>Immutable source pad before player-local cymbal projection.</summary>
        public int OriginalPad { get; }
        /// <summary>The source was moved to a separate target because both merged members occur.</summary>
        public bool IsCymbalExpanded { get; }
        /// <summary>Default display pad selected by the recorded projection policy.</summary>
        public int ColorPad { get; }
        public int GetColorPad(bool keepOriginalCymbalColors = false) =>
            keepOriginalCymbalColors ? OriginalPad : ColorPad;
        private int _padMask;
        public DrumNoteType Dynamics { get; set; }
        public bool IsNeutral => Dynamics == DrumNoteType.Neutral;
        public bool IsAccent => Dynamics == DrumNoteType.Accent;
        public bool IsGhost => Dynamics == DrumNoteType.Ghost;
        public bool IsInvisibleTerminator => HatPedalType == EliteDrumsHatPedalType.InvisibleTerminator;
        public bool IsStomp => HatPedalType == EliteDrumsHatPedalType.Stomp;
        public bool IsSplash => HatPedalType == EliteDrumsHatPedalType.Splash;
        public bool IsDoubleKick;
        public EliteDrumsHatState HatState { get; set; }
        public EliteDrumsHatPedalType HatPedalType { get; set; }
        public bool IsOpen => HatState == EliteDrumsHatState.Open;
        public bool IsClosed => HatState == EliteDrumsHatState.Closed;
        public bool IsIndifferent => HatState == EliteDrumsHatState.Indifferent;
        public bool IsFlam { get; set; }
        /// <summary>Effective hand flam; kick, pedal and wildcard notes never use hand-flam scoring.</summary>
        public bool IsHandFlam => (IsFlam || IsFlatFlam) && (EliteDrumPad) Pad is
            EliteDrumPad.Snare or EliteDrumPad.HiHat or EliteDrumPad.LeftCrash or
            EliteDrumPad.Tom1 or EliteDrumPad.Tom2 or EliteDrumPad.Tom3 or
            EliteDrumPad.Ride or EliteDrumPad.RightCrash;
        /// <summary>Kick flams never use flat-flam scoring.</summary>
        public bool IsKickFlam => Pad == (int)EliteDrumPad.Kick && (IsFlam || IsFlatFlam);
        public bool IsScoringFlam => IsHandFlam || IsKickFlam;
        /// <summary>Authored flam intent for generated downcharts; native roll lanes may suppress IsFlam.</summary>
        public bool IsAuthoredFlam { get; }
        public bool IsFlatFlam { get; set; }
        public EliteDrumsChannelFlag ChannelFlag { get; set; }
        public float? HitVelocity;
        /// <summary>Immutable authored source identity, retained through cloning and modifiers.</summary>
        public EliteDrumSourceDefinition? SourceDefinition { get; }
        /// <summary>All authored identities represented by this physical note, including collapsed roll duplicates.</summary>
        public IReadOnlyList<EliteDrumSourceDefinition> SourceDefinitions { get; }
        public bool IsStarPowerActivator => (DrumFlags & DrumNoteFlags.StarPowerActivator) != 0;

        public EliteDrumNote (EliteDrumPad pad, DrumNoteType dynamics, EliteDrumsHatState hatState, EliteDrumsHatPedalType hatPedalType, bool isFlam, DrumNoteFlags drumFlags,
            NoteFlags flags, EliteDrumsChannelFlag channelFlag, double time, uint tick, bool isDoubleKick,
            EliteDrumSourceDefinition? sourceDefinition = null, bool isFlatFlam = false, bool? isAuthoredFlam = null, int? originalPad = null, bool isCymbalExpanded = false, int? colorPad = null,
            IEnumerable<EliteDrumSourceDefinition>? sourceDefinitions = null)
            : this((int)pad, dynamics, hatState, hatPedalType, isFlam, drumFlags, flags, channelFlag, time, tick, isDoubleKick, sourceDefinition, isFlatFlam, isAuthoredFlam, originalPad, isCymbalExpanded, colorPad, sourceDefinitions)
        {
        }

        public EliteDrumNote(int pad, DrumNoteType dynamics, EliteDrumsHatState hatState, EliteDrumsHatPedalType hatPedalType, bool isFlam, DrumNoteFlags drumFlags,
            NoteFlags flags, EliteDrumsChannelFlag channelFlag, double time, uint tick, bool isDoubleKick,
            EliteDrumSourceDefinition? sourceDefinition = null, bool isFlatFlam = false, bool? isAuthoredFlam = null, int? originalPad = null, bool isCymbalExpanded = false, int? colorPad = null,
            IEnumerable<EliteDrumSourceDefinition>? sourceDefinitions = null)
            : base(flags, time, 0, tick, 0)
        {
            Pad = pad;
            OriginalPad = originalPad ?? pad;
            IsCymbalExpanded = isCymbalExpanded;
            ColorPad = colorPad ?? (isCymbalExpanded ? pad : OriginalPad);
            Dynamics = dynamics;
            HatState = hatState;
            HatPedalType = hatPedalType;
            IsFlam = isFlam;
            IsAuthoredFlam = isAuthoredFlam ?? isFlam;
            IsFlatFlam = isFlatFlam;
            DrumFlags = _drumFlags = drumFlags;
            ChannelFlag = channelFlag;
            _padMask = 1 << pad;
            IsDoubleKick = isDoubleKick;
            SourceDefinition = sourceDefinition;
            SourceDefinitions = Array.AsReadOnly((sourceDefinition == null ? Enumerable.Empty<EliteDrumSourceDefinition>() :
                new[] { sourceDefinition }).Concat(sourceDefinitions ?? Enumerable.Empty<EliteDrumSourceDefinition>()).Distinct().ToArray());
        }

        public EliteDrumNote(EliteDrumNote other) : base(other)
        {
            Pad = other.Pad;
            OriginalPad = other.OriginalPad;
            IsCymbalExpanded = other.IsCymbalExpanded;
            ColorPad = other.ColorPad;
            Dynamics = other.Dynamics;
            IsDoubleKick = other.IsDoubleKick;
            HatState = other.HatState;
            HatPedalType = other.HatPedalType;
            IsFlam = other.IsFlam;
            IsAuthoredFlam = other.IsAuthoredFlam;
            IsFlatFlam = other.IsFlatFlam;
            ChannelFlag = other.ChannelFlag;
            SourceDefinition = other.SourceDefinition;
            SourceDefinitions = other.SourceDefinitions;

            DrumFlags = _drumFlags = other._drumFlags;

            _padMask = 1 << other.Pad;
        }

        internal EliteDrumNote(EliteDrumNote other, IEnumerable<EliteDrumSourceDefinition> sourceDefinitions) : this(other)
        {
            SourceDefinitions = Array.AsReadOnly(SourceDefinitions.Concat(sourceDefinitions).Distinct().ToArray());
        }

        public override void AddChildNote(EliteDrumNote note)
        {
            // A projected destination can coincide with another authored target. Keep both
            // source obligations; one input still resolves only one member in the engine.
            if ((_padMask & (1 << note.Pad)) != 0)
            {
                foreach (var member in AllNotes)
                    if (member.Pad == note.Pad && member.OriginalPad == note.OriginalPad) return;
            }

            _padMask |= 1 << note.Pad;

            base.AddChildNote(note);
        }

        public override void ResetNoteState()
        {
            base.ResetNoteState();
            DrumFlags = _drumFlags;
            HitVelocity = null;
        }

        public void ActivateFlag(DrumNoteFlags drumNoteFlag)
        {
            _drumFlags |= drumNoteFlag;
            DrumFlags |= drumNoteFlag;
        }

        public void ClearFlag(DrumNoteFlags drumNoteFlag)
        {
            _drumFlags &= ~drumNoteFlag;
            DrumFlags &= ~drumNoteFlag;
        }

        protected override void CopyFlags(EliteDrumNote other)
        {
            _drumFlags = other._drumFlags;
            DrumFlags = other.DrumFlags;

            Dynamics = other.Dynamics;
        }

        protected override EliteDrumNote CloneNote()
        {
            return new(this);
        }

        public enum EliteDrumPad
        {
            HatPedal,
            Kick,

            Snare,
            HiHat,
            LeftCrash,
            Tom1,
            Tom2,
            Tom3,
            Ride,
            RightCrash,
            Wildcard,
        }

        public enum EliteDrumsHatState {
            Open,
            Closed,
            Indifferent
        }

        public enum EliteDrumsHatPedalType
        {
            Stomp,
            Splash,
            InvisibleTerminator
        }

        public enum EliteDrumsChannelFlag
        {
            None = 0,
            Red = MidIOHelper.ELITE_DRUMS_CHANNEL_FLAG_RED,
            Yellow = MidIOHelper.ELITE_DRUMS_CHANNEL_FLAG_YELLOW,
            Blue = MidIOHelper.ELITE_DRUMS_CHANNEL_FLAG_BLUE,
            Green = MidIOHelper.ELITE_DRUMS_CHANNEL_FLAG_GREEN,
        }
    }
}
