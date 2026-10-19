using System.IO;
using System.Text;

namespace YARG.Core.UnitTests.Song;

internal static class SngScanFixture
{
    internal static void Write(string path, string chartName, byte[] payload)
    {
        using var output = new BinaryWriter(File.Create(path));
        output.Write(Encoding.ASCII.GetBytes("SNGPKG"));
        output.Write((uint) 1);
        output.Write(new byte[16]);
        using var metadata = new MemoryStream();
        using (var writer = new BinaryWriter(metadata, Encoding.UTF8, true))
        {
            writer.Write((ulong) 2);
            foreach (var pair in new[] { ("name", "SNG fixture"), ("song_length", "60000") })
            {
                byte[] key = Encoding.UTF8.GetBytes(pair.Item1);
                byte[] value = Encoding.UTF8.GetBytes(pair.Item2);
                writer.Write(key.Length); writer.Write(key);
                writer.Write(value.Length); writer.Write(value);
            }
        }
        output.Write(metadata.Length); output.Write(metadata.ToArray());
        byte[] name = Encoding.UTF8.GetBytes(chartName);
        byte[] audio = Encoding.UTF8.GetBytes("song.opus");
        long listingLength = 8 + 1 + name.Length + 16 + 1 + audio.Length + 16;
        long position = output.BaseStream.Position + 8 + listingLength;
        output.Write(listingLength); output.Write((ulong) 2);
        output.Write((byte) name.Length); output.Write(name); output.Write((long) payload.Length); output.Write(position);
        output.Write((byte) audio.Length); output.Write(audio); output.Write((long) 0); output.Write(position + payload.Length);
        for (int i = 0; i < payload.Length; i++) output.Write((byte) (payload[i] ^ (byte) i));
    }
}
