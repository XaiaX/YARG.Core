using System;
using YARG.Core.IO;

namespace YARG.Core.Song.Cache
{
    // Carries effective entries through a detached rewrite without opening their base files.
    internal sealed class CachedCONEntryGroup : CONEntryGroup
    {
        private readonly CONEntryType _type;
        protected override CONEntryType Tag => _type;
        internal CachedCONEntryGroup(AbridgedFileInfo root, string playlist, CONEntryType type) : base(root, playlist) { _type = type; }
        public override ScanExpected<RBCONEntry> CreateEntry(in RBScanParameters parameters) => throw new InvalidOperationException();
        public override void DeserializeEntry(ref FixedArrayStream stream, string name, int index, CacheReadStrings strings) => throw new InvalidOperationException();
    }
}
