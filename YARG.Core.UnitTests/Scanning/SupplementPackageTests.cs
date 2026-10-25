// pattern: Imperative Shell
using System.IO.Compression;
using Melanchall.DryWetMidi.Core;
using NUnit.Framework;
using YARG.Core.Song;
using YARG.Core.Song.Supplements;
using YARG.Core.IO;
using YARG.Core.Chart;

namespace YARG.Core.UnitTests.Scanning;

public class SupplementPackageTests
{
    private string _root = null!;
    [SetUp] public void SetUp() { _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_root); }
    [TearDown] public void TearDown() { Directory.Delete(_root, true); }
    private string Write(string path, byte[] data) { path = Path.Combine(_root, path); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, data); return path; }
    private string Dta(string text) => Write("songs_updates.dta", System.Text.Encoding.UTF8.GetBytes(text));
    private static byte[] Midi()
    {
        var file = new MidiFile(new TrackChunk(new SequenceTrackNameEvent("SYNC")), new TrackChunk(new SequenceTrackNameEvent("PART DRUMS"), new NoteOnEvent((Melanchall.DryWetMidi.Common.SevenBitNumber) 96, (Melanchall.DryWetMidi.Common.SevenBitNumber) 100), new NoteOffEvent((Melanchall.DryWetMidi.Common.SevenBitNumber) 96, (Melanchall.DryWetMidi.Common.SevenBitNumber) 0) { DeltaTime = 120 }));
        using var stream = new MemoryStream(); file.Write(stream); return stream.ToArray();
    }
    [Test] public void RoundTripPreservesDefinitionsAssetsAndSummary()
    {
        var dta = Dta("(song (name \"First\"))\n(song (name \"Second\"))\n(metadata (name \"Only\"))");
        var midi = Midi(); Write("song/song_update.mid", midi);
        Write("alternate/gen/alternate_keep.png_xbox", new byte[] { 1, 2, 3 });
        var output = Path.Combine(_root, "test.yargup");
        SupplementPackageBuilder.Pack(dta, SupplementSourceKind.Updates, output);
        using var package = SupplementPackage.Open(output);
        Assert.That(package.Definitions, Is.EqualTo(new[] { "song", "song", "metadata" }));
        Assert.That(package.DtaBytes, Is.EqualTo(File.ReadAllBytes(dta)));
        Assert.That(package.ReadAsset("song/song_update.mid"), Is.EqualTo(midi));
        Assert.That(package.TryGetAsset("song/song_update.mid", out var asset), Is.True);
        Assert.That(asset.Summary, Is.Not.Null);
        using var bytes = FixedArray<byte>.Alloc(midi.Length); midi.CopyTo(bytes.Span);
        var expected = AvailableParts.Default; var actual = AvailableParts.Default;
        var d1 = DrumsType.Unknown; var d2 = DrumsType.Unknown;
        Assert.That(asset.Summary!.Scan(ref actual, ref d1), Is.EqualTo(MidiScanSummary.Compile(bytes).Scan(ref expected, ref d2)));
        Assert.That(actual, Is.EqualTo(expected));
        package.Verify();
        var extract = Path.Combine(_root, "extract"); package.Extract(extract);
        Assert.That(File.ReadAllBytes(Path.Combine(extract, "song/song_update.mid")), Is.EqualTo(midi));
        Assert.That(File.ReadAllBytes(Path.Combine(extract, "alternate/gen/alternate_keep.png_xbox")), Is.EqualTo(new byte[] { 1, 2, 3 }));
        Assert.Throws<IOException>(() => package.Extract(extract));
        Assert.Throws<IOException>(() => SupplementPackageBuilder.Pack(dta, SupplementSourceKind.Updates, output));
    }
    [Test] public void UpgradesRequireMidiWhileMetadataOnlyUpdatesDoNot()
    {
        var dta = Dta("(song (name \"Only\"))"); var output = Path.Combine(_root, "test.yargup");
        Assert.Throws<FileNotFoundException>(() => SupplementPackageBuilder.Pack(dta, SupplementSourceKind.Upgrades, output));
        Assert.That(File.Exists(output), Is.False);
        SupplementPackageBuilder.Pack(dta, SupplementSourceKind.Updates, output);
        using var package = SupplementPackage.Open(output); Assert.That(package.Assets, Is.Empty);
    }
    [Test] public void UnsupportedMidiRetainsRawBytesWithoutSummary()
    {
        var dta = Dta("(song (name \"Only\"))"); var original = Midi().Concat(new byte[] { 0 }).ToArray();
        Write("song/song_update.mid", original); var output = Path.Combine(_root, "test.yargup");
        SupplementPackageBuilder.Pack(dta, SupplementSourceKind.Updates, output);
        using var package = SupplementPackage.Open(output); Assert.That(package.Assets.Single().Summary, Is.Null);
        Assert.That(package.ReadAsset("song/song_update.mid"), Is.EqualTo(original));
    }
    [Test] public void PackingIsDeterministicAndDeduplicatesBlobs()
    {
        var dta = Dta("(a (name a))(b (name b))"); var bytes = Midi(); Write("a/a_update.mid", bytes); Write("b/b_update.mid", bytes);
        var one = Path.Combine(_root,"one.yargup"); var two = Path.Combine(_root,"two.yargup");
        SupplementPackageBuilder.Pack(dta, SupplementSourceKind.Updates, one); SupplementPackageBuilder.Pack(dta, SupplementSourceKind.Updates, two);
        Assert.That(File.ReadAllBytes(one), Is.EqualTo(File.ReadAllBytes(two)));
        using var zip = ZipFile.OpenRead(one); Assert.That(zip.Entries.Count(e => e.FullName.StartsWith("blobs/")), Is.EqualTo(1));
    }
    [Test] public void AssetStreamRemainsReadableAfterPackageDispose()
    {
        var dta = Dta("(song (name song))"); byte[] original = Midi(); Write("song/song_update.mid", original);
        string output = Path.Combine(_root, "stream.yargup"); SupplementPackageBuilder.Pack(dta, SupplementSourceKind.Updates, output);
        var package = SupplementPackage.Open(output); using var stream = package.OpenAsset("song/song_update.mid"); package.Dispose();
        using var result = new MemoryStream(); stream.CopyTo(result); Assert.That(result.ToArray(), Is.EqualTo(original));
    }
    [Test] public void CorruptUnrequestedBlobDoesNotPreventIndexReadsButRequestedReadFails()
    {
        var dta = Dta("(song (name song))"); byte[] original = Midi(); Write("song/song_update.mid", original);
        string output = Path.Combine(_root, "corrupt.yargup"); SupplementPackageBuilder.Pack(dta, SupplementSourceKind.Updates, output);
        using (var zip = ZipFile.Open(output, ZipArchiveMode.Update))
        {
            var entry = zip.Entries.Single(e => e.FullName.StartsWith("blobs/")); string name = entry.FullName; entry.Delete();
            using var writer = zip.CreateEntry(name).Open(); writer.Write(new byte[original.Length]);
        }
        using var package = SupplementPackage.Open(output);
        Assert.That(package.Definitions, Is.EqualTo(new[] { "song" }));
        Assert.Throws<InvalidDataException>(() => package.ReadAsset("song/song_update.mid"));
        Assert.Throws<InvalidDataException>(() => package.OpenAsset("song/song_update.mid"));
    }
    [TestCase("../escape")]
    [TestCase("C:/escape")]
    [TestCase("a/CON")]
    public void UnsafeManifestAssetPathsAreRejected(string path)
    {
        var dta = Dta("(song (name song))"); Write("song/song_update.mid", Midi());
        string output = Path.Combine(_root, "unsafe.yargup"); SupplementPackageBuilder.Pack(dta, SupplementSourceKind.Updates, output);
        RewriteManifest(output, json => json["Assets"]![0]!["Path"] = path);
        Assert.Throws<InvalidDataException>(() => SupplementPackage.Open(output));
    }
    [Test] public void FutureSummaryFallsBackWhileFutureContainerFails()
    {
        var dta = Dta("(song (name song))"); Write("song/song_update.mid", Midi());
        string output = Path.Combine(_root, "future.yargup"); SupplementPackageBuilder.Pack(dta, SupplementSourceKind.Updates, output);
        RewriteManifest(output, json => json["Assets"]![0]!["Summary"] = Convert.ToBase64String(new byte[] { 99, 0, 0, 0 }));
        using (var package = SupplementPackage.Open(output)) { Assert.That(package.Assets[0].Summary, Is.Null); package.Verify(); }
        RewriteManifest(output, json => json["Version"] = 99);
        Assert.Throws<InvalidDataException>(() => SupplementPackage.Open(output));
    }
    private static void RewriteManifest(string path, Action<Newtonsoft.Json.Linq.JObject> mutate)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Update); var entry = zip.GetEntry("manifest.json")!;
        Newtonsoft.Json.Linq.JObject json;
        using (var reader = new StreamReader(entry.Open())) json = Newtonsoft.Json.Linq.JObject.Parse(reader.ReadToEnd());
        mutate(json); entry.Delete(); using var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open()); writer.Write(json.ToString());
    }

    [Test] public void SummarySerializationPreservesScannerResultsAcrossGeneratedTracks()
    {
        var random = new Random(20261024);
        string[] names = { "PART DRUMS", "PART GUITAR", "PART BASS", "PART VOCALS", "HARM1", "HARM2", "PART ELITE_DRUMS", "UNKNOWN" };
        for (int trial = 0; trial < 80; trial++)
        {
            var tracks = new List<TrackChunk> { new(new SequenceTrackNameEvent("SYNC")) };
            for (int t = 0; t < random.Next(1, 8); t++)
            {
                var track = new TrackChunk(new SequenceTrackNameEvent(names[random.Next(names.Length)]));
                for (int n = 0; n < random.Next(0, 30); n++)
                {
                    var note = (Melanchall.DryWetMidi.Common.SevenBitNumber)random.Next(36, 110);
                    track.Events.Add(new NoteOnEvent(note, (Melanchall.DryWetMidi.Common.SevenBitNumber)100));
                    track.Events.Add(new NoteOffEvent(note, (Melanchall.DryWetMidi.Common.SevenBitNumber)0) { DeltaTime = random.Next(1, 200) });
                }
                tracks.Add(track);
            }
            var file = new MidiFile(tracks); using var stream = new MemoryStream(); file.Write(stream); stream.Position = 0;
            using var bytes = FixedArray.ReadRemainder(stream);
            var original = MidiScanSummary.Compile(bytes); var restored = MidiScanSummary.Deserialize(original.Serialize());
            Assert.That(restored, Is.Not.Null);
            for (int context = 0; context < 8; context++)
            {
                var expected = AvailableParts.Default; var actual = AvailableParts.Default;
                var d1 = (DrumsType)context; var d2 = d1;
                Assert.That(restored!.Scan(ref actual, ref d2), Is.EqualTo(original.Scan(ref expected, ref d1)));
                Assert.That(actual, Is.EqualTo(expected)); Assert.That(d2, Is.EqualTo(d1));
            }
        }
    }
    [Test] public void InvalidSummaryPayloadsAlwaysFallBack()
    {
        var random = new Random(14);
        for (int size = 0; size < 256; size++)
        {
            var bytes = new byte[size]; random.NextBytes(bytes);
            Assert.That(MidiScanSummary.Deserialize(bytes), Is.Null);
        }
    }
    [Test] public void DuplicateArchiveEntriesAndAmbiguousLogicalNamesAreRejected()
    {
        var dta = Dta("(song (name song))"); Write("song/song_update.mid", Midi());
        string output = Path.Combine(_root, "duplicate.yargup"); SupplementPackageBuilder.Pack(dta, SupplementSourceKind.Updates, output);
        using (var zip = ZipFile.Open(output, ZipArchiveMode.Update)) zip.CreateEntry("MANIFEST.JSON");
        Assert.Throws<InvalidDataException>(() => SupplementPackage.Open(output));
    }

    [Test] public void VerifiedAssetStreamsSupportAudioSeekingWithoutPackageLifetime()
    {
        var dta = Dta("(song (name song))"); byte[] original = Enumerable.Range(0, 200000).Select(i => (byte)(i % 251)).ToArray();
        Write("song/song_update.mogg", original); string output = Path.Combine(_root, "seek.yargup");
        SupplementPackageBuilder.Pack(dta, SupplementSourceKind.Updates, output);
        var package = SupplementPackage.Open(output); using var stream = package.OpenAsset("song/song_update.mogg"); package.Dispose();
        Assert.That(stream.CanSeek, Is.True);
        Assert.That(stream.Seek(150000, SeekOrigin.Begin), Is.EqualTo(150000)); Assert.That(stream.ReadByte(), Is.EqualTo(original[150000]));
        stream.Position = 10; Assert.That(stream.ReadByte(), Is.EqualTo(original[10]));
        stream.Seek(-1, SeekOrigin.End); Assert.That(stream.ReadByte(), Is.EqualTo(original[^1])); Assert.That(stream.ReadByte(), Is.EqualTo(-1));
    }

    [Test] public void DtaLeadingCommentsAndWhitespacePreserveDefinitions()
    {
        var dta = Dta("; original comment\n(song (name \"Metadata only\"))\n");
        string output = Path.Combine(_root, "comments.yargup"); SupplementPackageBuilder.Pack(dta, SupplementSourceKind.Updates, output);
        using var package = SupplementPackage.Open(output); Assert.That(package.Definitions, Is.EqualTo(new[] { "song" }));
    }

    [Test] public void MissingContainerVersionIsNotTreatedAsCurrentVersion()
    {
        var dta = Dta("(song (name song))"); string output = Path.Combine(_root, "version.yargup");
        SupplementPackageBuilder.Pack(dta, SupplementSourceKind.Updates, output);
        RewriteManifest(output, json => json.Remove("Version"));
        Assert.Throws<InvalidDataException>(() => SupplementPackage.Open(output));
    }
    [TestCase("(song (name \"unterminated)")]
    [TestCase("(song (name \"closed string\")")]
    public void MalformedDtaReportsInvalidDataAndDoesNotPublish(string text)
    {
        var dta = Dta(text); string output = Path.Combine(_root, "invalid.yargup");
        Assert.Throws<InvalidDataException>(() => SupplementPackageBuilder.Pack(dta, SupplementSourceKind.Updates, output));
        Assert.That(File.Exists(output), Is.False);
    }

    [Test] public void PackageLogicalLookupUsesPortableCaseInsensitivePaths()
    {
        var dta = Dta("(song (name song))"); Write("song/gen/song_keep.PNG_XBOX", new byte[] { 4, 5, 6 });
        string output = Path.Combine(_root, "case.yargup"); SupplementPackageBuilder.Pack(dta, SupplementSourceKind.Updates, output);
        using var package = SupplementPackage.Open(output);
        Assert.That(package.ContainsAsset("song/gen/song_keep.png_xbox"), Is.True);
        Assert.That(package.ReadAsset("SONG/GEN/SONG_KEEP.PNG_XBOX"), Is.EqualTo(new byte[] { 4, 5, 6 }));
        Assert.That(package.Assets.Single().Path, Is.EqualTo("song/gen/song_keep.PNG_XBOX"));
    }

}
