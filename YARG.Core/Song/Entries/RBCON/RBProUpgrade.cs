// pattern: Imperative Shell
using System;
using System.IO;
using YARG.Core.IO;
using YARG.Core.Song.Supplements;

namespace YARG.Core.Song
{
    internal abstract class RBProUpgrade
    {
        public const string UPGRADES_DTA = "upgrades.dta";
        public const string UPGRADES_MIDI_EXT = "_plus.mid";

        public abstract DateTime LastWriteTime { get; }
        public abstract FixedArray<byte>? LoadUpgradeMidi();

        protected readonly AbridgedFileInfo _root;
        protected RBProUpgrade(in AbridgedFileInfo root)
        {
            _root = root;
        }
    }

    [Serializable]
    internal sealed class PackedRBProUpgrade : RBProUpgrade
    {
        public const string UPGRADES_DIRECTORY = "songs_upgrades/";

        private readonly CONFileListing? _listing;

        public override DateTime LastWriteTime => _root.LastWriteTime;

        public PackedRBProUpgrade(CONFileListing? listing, in AbridgedFileInfo root)
            : base(in root)
        {
            _listing = listing;
        }

        public override FixedArray<byte>? LoadUpgradeMidi()
        {
            return _listing != null && _root.IsStillValid()
                ? CONFileStream.LoadFile(_root.FullName, _listing)
                : null;
        }
    }

    [Serializable]
    internal sealed class UnpackedRBProUpgrade : RBProUpgrade
    {
        private readonly string _name;
        private readonly DateTime _lastWritetime;
        private readonly string _dtaPath;
        internal string DtaPath => _dtaPath;
        internal string RelativePath => _name + UPGRADES_MIDI_EXT;
        internal string? ExpectedPackageManifest { get; set; }

        public override DateTime LastWriteTime => _lastWritetime;

        public UnpackedRBProUpgrade(string name, in DateTime lastWriteTime, in AbridgedFileInfo root, string? dtaPath = null)
            : base(root)
        {
            _name = name;
            _lastWritetime = lastWriteTime;
            _dtaPath = dtaPath ?? Path.Combine(root.FullName, UPGRADES_DTA);
        }

        public override FixedArray<byte>? LoadUpgradeMidi()
        {
            var data = default(FixedArray<byte>);
            if (SupplementalAssetSource.IsPackage(_dtaPath))
                return SupplementalAssetSource.Load(_dtaPath, RelativePath, ExpectedPackageManifest);
            if (AbridgedFileInfo.Validate(_dtaPath, in _root.LastWriteTime))
            {
                string file = Path.Combine(_root.FullName, _name + UPGRADES_MIDI_EXT);
                if (AbridgedFileInfo.Validate(file, in _lastWritetime))
                {
                    data = FixedArray.LoadFile(file);
                }
            }
            return data;
        }
    }
}
