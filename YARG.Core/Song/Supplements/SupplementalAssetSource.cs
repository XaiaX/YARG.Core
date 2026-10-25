// pattern: Imperative Shell
using System;
using System.IO;
using YARG.Core.IO;

namespace YARG.Core.Song.Supplements
{
    /// <summary>Opens original supplemental bytes without materializing a package on disk.</summary>
    internal sealed class SupplementalPackageException : IOException
    {
        internal SupplementalPackageException(string message, Exception? inner = null) : base(message, inner) { }
    }

    internal static class SupplementalAssetSource
    {
        internal static bool IsPackage(string path) =>
            string.Equals(Path.GetExtension(path), ".yargup", StringComparison.OrdinalIgnoreCase);

        internal static SupplementPackage OpenPackage(string path, string? expectedManifest)
        {
            SupplementPackage package;
            try { package = SupplementPackage.Open(path); }
            catch (Exception error) when (error is IOException || error is InvalidDataException || error is UnauthorizedAccessException)
            { throw new SupplementalPackageException("Cannot open supplemental package: " + path, error); }
            if (expectedManifest != null && package.ManifestHash != expectedManifest)
            {
                package.Dispose();
                throw new SupplementalPackageException("Supplemental package changed. Scan its source again: " + path);
            }
            return package;
        }

        internal static FixedArray<byte> Load(string path, string relativePath, string? expectedManifest = null)
        {
            if (!IsPackage(path)) return FixedArray.LoadFile(Path.Combine(Path.GetDirectoryName(path)!, relativePath));
            using var package = OpenPackage(path, expectedManifest);
            return Read(package, relativePath.Replace('\\', '/'));
        }

        internal static FixedArray<byte> Read(SupplementPackage package, string relativePath)
        {
            try
            {
                if (relativePath.EndsWith(".mid", StringComparison.OrdinalIgnoreCase))
                    YARG.Core.Song.Cache.CacheHandler.ScanObservation?.Invoke("package-midi:" + relativePath);
                return ToFixedArray(package.ReadAsset(relativePath));
            }
            catch (Exception error) when (error is IOException || error is InvalidDataException) { throw new SupplementalPackageException("Cannot read supplemental package asset: " + relativePath, error); }
        }

        internal static Stream Open(SupplementPackage package, string relativePath)
        {
            try { return package.OpenAsset(relativePath); }
            catch (Exception error) when (error is IOException || error is InvalidDataException) { throw new SupplementalPackageException("Cannot read supplemental package asset: " + relativePath, error); }
        }

        internal static FixedArray<byte> ToFixedArray(byte[] bytes)
        {
            var result = FixedArray<byte>.Alloc(bytes.Length);
            bytes.AsSpan().CopyTo(result.Span);
            return result;
        }
    }
}
