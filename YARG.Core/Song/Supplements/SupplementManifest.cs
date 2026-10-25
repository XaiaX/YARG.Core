// pattern: Functional Core
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace YARG.Core.Song.Supplements
{
    public enum SupplementSourceKind { Updates, Upgrades }

    public sealed class SupplementAsset
    {
        public string Path { get; }
        public string Sha256 { get; }
        public long Length { get; }
        private readonly Lazy<MidiScanSummary?> _summary;
        public MidiScanSummary? Summary => _summary.Value;
        internal string Blob { get; }
        internal SupplementAsset(AssetRecord record)
        {
            Path = record.Path; Sha256 = record.Sha256; Length = record.Length;
            Blob = "blobs/sha256/" + Sha256;
            string? encoded = record.Summary;
            _summary = new Lazy<MidiScanSummary?>(() =>
            {
                if (encoded == null) return null;
                try { return MidiScanSummary.Deserialize(Convert.FromBase64String(encoded)); }
                catch (FormatException) { return null; }
            });
        }
    }

    internal sealed class PackageManifest
    {
        public string Format { get; set; } = "YARG supplementary source";
        public int Version { get; set; } = 1;
        public string Kind { get; set; } = "";
        public string DtaPath { get; set; } = "";
        public string DtaSha256 { get; set; } = "";
        public long DtaLength { get; set; }
        public List<string> Definitions { get; set; } = new();
        public List<AssetRecord> Assets { get; set; } = new();
    }

    internal sealed class AssetRecord
    {
        public string Path { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public long Length { get; set; }
        public string? Summary { get; set; }
    }

    internal static class SupplementPaths
    {
        internal static void Validate(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Length > 1024 || path.Contains('\\') || path.Contains(':') ||
                path.StartsWith("/", StringComparison.Ordinal) || path.Any(c => c < 32 || "<>\"|?*".Contains(c)))
                throw new InvalidDataException("Unsafe package path: " + path);
            foreach (string part in path.Split('/'))
            {
                string stem = part.Split('.')[0].ToUpperInvariant();
                if (part.Length == 0 || part == "." || part == ".." || part.EndsWith(".", StringComparison.Ordinal) ||
                    part.EndsWith(" ", StringComparison.Ordinal) || stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL" ||
                    (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && stem[3] >= '1' && stem[3] <= '9'))
                    throw new InvalidDataException("Unsafe package path: " + path);
            }
        }
        internal static void ValidateDigest(string hash)
        {
            if (hash == null || hash.Length != 64 || hash.Any(c => !(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f')))
                throw new InvalidDataException("Invalid package SHA-256.");
        }
    }
}
