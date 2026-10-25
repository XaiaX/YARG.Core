// pattern: Functional Core
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace YARG.Core.Song.Supplements
{
    /// <summary>Validated, shared package metadata. Owns no file handles or asset payloads.</summary>
    internal sealed class SupplementPackageIndex
    {
        internal SupplementSourceKind Kind { get; }
        internal string ManifestHash { get; }
        internal string DtaPath { get; }
        internal string DtaSha256 { get; }
        internal byte[] Dta { get; }
        internal IReadOnlyList<string> Definitions { get; }
        internal IReadOnlyList<SupplementAsset> Assets { get; }
        internal IReadOnlyDictionary<string, SupplementAsset> AssetMap { get; }

        private SupplementPackageIndex(PackageManifest manifest, string manifestHash, byte[] dta)
        {
            if (manifest.Format != "YARG supplementary source" || manifest.Version != 1)
                throw new InvalidDataException("Unsupported .yargup container version.");
            Kind = manifest.Kind == "updates" ? SupplementSourceKind.Updates : manifest.Kind == "upgrades"
                ? SupplementSourceKind.Upgrades : throw new InvalidDataException("Unknown package source kind.");
            ManifestHash = manifestHash;
            SupplementPaths.Validate(manifest.DtaPath);
            SupplementPaths.ValidateDigest(manifest.DtaSha256);
            if (manifest.DtaLength < 0 || manifest.DtaLength > SupplementPackage.MAX_DTA_BYTES)
                throw new InvalidDataException("Invalid DTA length.");
            if (dta.LongLength != manifest.DtaLength || SupplementPackage.Hash(dta) != manifest.DtaSha256)
                throw new InvalidDataException("Package DTA digest mismatch.");
            Dta = dta; DtaPath = manifest.DtaPath; DtaSha256 = manifest.DtaSha256;
            if (manifest.Definitions == null || manifest.Assets == null ||
                manifest.Definitions.Count > SupplementPackage.MAX_ENTRIES || manifest.Assets.Count > SupplementPackage.MAX_ENTRIES)
                throw new InvalidDataException("Invalid package index.");
            var parsed = SupplementPackageBuilder.ReadDefinitions(dta);
            if (!parsed.Select(d => d.Node).SequenceEqual(manifest.Definitions))
                throw new InvalidDataException("Package definitions do not match original DTA.");
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { DtaPath };
            var assets = new Dictionary<string, SupplementAsset>(StringComparer.OrdinalIgnoreCase);
            foreach (var record in manifest.Assets)
            {
                if (record == null) throw new InvalidDataException("Null package asset.");
                SupplementPaths.Validate(record.Path); SupplementPaths.ValidateDigest(record.Sha256);
                if (!paths.Add(record.Path)) throw new InvalidDataException("Duplicate logical asset: " + record.Path);
                if (record.Length < 0 || record.Length > SupplementPackage.MAX_ASSET_BYTES)
                    throw new InvalidDataException("Invalid asset length.");
                if (record.Summary != null && record.Summary.Length > 24 * 1024 * 1024)
                    throw new InvalidDataException("Summary too large.");
                var asset = new SupplementAsset(record);
                assets.Add(asset.Path, asset);
            }
            if (Kind == SupplementSourceKind.Upgrades)
                foreach (string node in manifest.Definitions)
                    if (!assets.ContainsKey(node + "_plus.mid")) throw new InvalidDataException("Upgrade MIDI missing: " + node);
            Definitions = manifest.Definitions.AsReadOnly();
            Assets = assets.Values.ToList().AsReadOnly();
            AssetMap = assets;
            // Do not retain deserialization records alongside the final index.
        }

        internal static SupplementPackageIndex Parse(byte[] manifestBytes, string hash, byte[] dta)
        {
            using var reader = new JsonTextReader(new StringReader(new UTF8Encoding(false, true).GetString(manifestBytes))) { MaxDepth = 32 };
            var json = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            if (reader.Read()) throw new InvalidDataException("Trailing package manifest data.");
            foreach (string required in new[] { "Format", "Version", "Kind", "DtaPath", "DtaSha256", "DtaLength", "Definitions", "Assets" })
                if (json[required] == null || json[required]!.Type == JTokenType.Null)
                    throw new InvalidDataException("Missing package manifest field: " + required);
            var manifest = json.ToObject<PackageManifest>() ?? throw new InvalidDataException("Missing manifest.");
            return new SupplementPackageIndex(manifest, hash, dta);
        }
    }
}
