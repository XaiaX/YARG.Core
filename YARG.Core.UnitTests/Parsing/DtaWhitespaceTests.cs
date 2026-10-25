using System.Text;
using NUnit.Framework;
using YARG.Core.IO;

namespace YARG.Core.UnitTests.Parsing;

public sealed class DtaWhitespaceTests
{
    [TestCase("\n")]
    [TestCase("\r\n")]
    [TestCase("\r")]
    public void CommentsInsideAndBetweenNodesPreserveFollowingEntries(string newline)
    {
        string source = "; leading" + newline +
            "(first ; nested comment" + newline + " (name \"Semi;colon\")) ; between" + newline +
            "(second (name \"Two\")) ; trailing comment";
        byte[] bytes = Encoding.UTF8.GetBytes(source);
        using var data = FixedArray<byte>.Alloc(bytes.Length);
        bytes.AsSpan().CopyTo(data.Span);
        var text = YARGDTAReader.Create(data);
        var names = new List<string>();
        while (YARGDTAReader.StartNode(ref text))
        {
            names.Add(YARGDTAReader.GetNameOfNode(ref text, true));
            YARGDTAReader.EndNode(ref text);
        }
        Assert.That(names, Is.EqualTo(new[] { "first", "second" }));
        Assert.That(text.IsAtEnd(), Is.True);
    }
}
