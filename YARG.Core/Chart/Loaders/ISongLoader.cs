using System.Collections.Generic;

namespace YARG.Core.Chart
{
    /// <summary>
    /// Interface used for loading chart files.
    /// </summary>
    internal interface ISongLoader
    {
        List<TextEvent> LoadGlobalEvents();
        List<Section> LoadSections();
        SyncTrack LoadSyncTrack();
        VenueTrack LoadVenueTrack();
        LyricsTrack LoadLyrics();

        InstrumentTrack<GuitarNote> LoadGuitarTrack(Instrument instrument);
        InstrumentTrack<ProGuitarNote> LoadProGuitarTrack(Instrument instrument);
        InstrumentTrack<ProKeysNote> LoadProKeysTrack(Instrument instrument);
        InstrumentTrack<DrumNote> LoadDrumsTrack(Instrument instrument, InstrumentTrack<EliteDrumNote>? eliteDrumsFallback);
        InstrumentTrack<EliteDrumNote> LoadEliteDrumsTrack(Instrument instrument);

        /// <summary>
        /// Builds the forced Elite Drums downchart tracks requested by the parse settings
        /// (see <see cref="ParseSettings.EliteDrumsDownchartOutputs"/>). Unlike the
        /// <see cref="LoadDrumsTrack"/> fallback, these are generated even when a native
        /// chart exists, so both variants can coexist on one <see cref="SongChart"/>.
        /// </summary>
        IReadOnlyDictionary<Instrument, InstrumentTrack<DrumNote>> LoadEliteDrumsDownchartTracks(
            InstrumentTrack<EliteDrumNote> eliteDrumsTrack);
        VocalsTrack LoadVocalsTrack(Instrument instrument);
    }
}