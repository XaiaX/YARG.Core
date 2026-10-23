using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace YARG.Core.Song.Cache
{
    public enum SongScanScope { Songs, Updates, Upgrades, All }
    public enum SongScanStatus { Success, SourceError, NeedsFullScan }

    public sealed class SongSourceConfiguration : IEquatable<SongSourceConfiguration>
    {
        internal static readonly StringComparer PathComparer = Path.DirectorySeparatorChar == '\\'
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        public IReadOnlyList<string> SongFolders { get; }
        public string UpdatesDtaPath { get; }
        public string UpgradesDtaPath { get; }

        public SongSourceConfiguration(IEnumerable<string> songFolders, string? updatesDtaPath = null, string? upgradesDtaPath = null)
        {
            if (songFolders == null) throw new ArgumentNullException(nameof(songFolders));
            SongFolders = Array.AsReadOnly(songFolders.Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(Normalize).Distinct(PathComparer).ToArray());
            UpdatesDtaPath = Normalize(updatesDtaPath);
            UpgradesDtaPath = Normalize(upgradesDtaPath);
        }

        private static string Normalize(string? path) => string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFullPath(path!);
        public bool Equals(SongSourceConfiguration? other) => other != null &&
            SongFolders.SequenceEqual(other.SongFolders, PathComparer) &&
            PathComparer.Equals(UpdatesDtaPath, other.UpdatesDtaPath) && PathComparer.Equals(UpgradesDtaPath, other.UpgradesDtaPath);
        public override bool Equals(object? obj) => obj is SongSourceConfiguration other && Equals(other);
        public override int GetHashCode()
        {
            int hash = 17;
            foreach (string path in SongFolders.Concat(new[] { UpdatesDtaPath, UpgradesDtaPath }))
                hash = unchecked(hash * 31 + PathComparer.GetHashCode(path));
            return hash;
        }
    }

    public sealed class SongScanResult : IDisposable
    {
        private Action? _commit;
        private Action? _cleanup;
        private bool _disposed;
        internal void SetCandidate(Action commit, Action cleanup)
        {
            _commit = commit;
            _cleanup = cleanup;
        }

        /// <summary>Revalidates and publishes a deferred cache candidate. Call before publishing library references.</summary>
        public void Commit()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SongScanResult));
            if (_commit == null) return;
            _commit();
            _commit = null;
            _cleanup = null;
        }

        /// <summary>Deletes an uncommitted candidate without changing the previously published cache.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            try { _cleanup?.Invoke(); }
            catch (Exception ex) { YARG.Core.Logging.YargLogger.LogException(ex, "Could not clean up deferred scan candidate"); }
            finally
            {
                _cleanup = null;
                _commit = null;
                _disposed = true;
            }
        }
        public SongScanStatus Status { get; }
        public SongCache? Cache { get; }
        public string Message { get; }
        public int AffectedCount { get; }
        public int UnmatchedCount { get; }
        public SongSourceConfiguration? AppliedSources { get; }
        /// <summary>
        /// Parsed top-level definitions in the applied selected Updates DTA, including unmatched and repeated nodes.
        /// Zero means an empty or cleared source. Null on failure; retain the previously published count.
        /// For deferred scans, publish this value only after Commit succeeds, alongside AppliedSources and Cache.
        /// </summary>
        public int? UpdatesDefinitionCount { get; }
        /// <summary>Like UpdatesDefinitionCount, for the applied selected Upgrades DTA.</summary>
        public int? UpgradesDefinitionCount { get; }
        internal SongScanResult(SongScanStatus status, SongCache? cache, string message,
            SongSourceConfiguration? appliedSources = null, int affectedCount = 0, int unmatchedCount = 0,
            int? updatesDefinitionCount = null, int? upgradesDefinitionCount = null)
        {
            Status = status;
            Cache = cache;
            Message = message;
            AppliedSources = appliedSources;
            AffectedCount = affectedCount;
            UnmatchedCount = unmatchedCount;
            UpdatesDefinitionCount = updatesDefinitionCount;
            UpgradesDefinitionCount = upgradesDefinitionCount;
        }
    }
}
