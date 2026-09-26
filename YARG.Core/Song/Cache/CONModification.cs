using System;
using System.Collections.Generic;
using YARG.Core.IO;

namespace YARG.Core.Song
{
    internal sealed class CONUpdateLayer
    {
        public AbridgedFileInfo Root { get; }
        public DTAEntry Dta { get; }
        public DateTime? Midi { get; }

        public CONUpdateLayer(AbridgedFileInfo root, DTAEntry dta, DateTime? midi)
        {
            Root = root;
            Dta = dta;
            Midi = midi;
        }
    }

    internal class CONModification
    {
        public bool Processed = false;
        public DTAEntry UpdateDTA = DTAEntry.Empty;
        public DTAEntry UpgradeDTA = DTAEntry.Empty;
        public AbridgedFileInfo? UpdateDirectoryAndDtaLastWrite;
        public DateTime? UpdateMidiLastWrite;
        public RBProUpgrade? Upgrade;
        public readonly List<CONUpdateLayer> UpdateLayers = new();
    }

    internal class QuickCONMods
    {
        public AbridgedFileInfo? UpdateDirectoryAndDtaLastWrite;
        public DateTime? UpdateMidi;
        public readonly List<CONUpdateLayer> UpdateLayers = new();
        public RBProUpgrade? Upgrade;
    }
}
