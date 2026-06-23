using Newtonsoft.Json;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Game;

namespace YARG.Core.UnitTests.Game;

public class YargProfileModifierTests
{
    private static YargProfile CreateVocalsProfile(string name)
    {
        return new YargProfile
        {
            Name = name,
            GameMode = GameMode.Vocals,
        };
    }

    private static YargProfile JsonRoundTrip(YargProfile profile)
    {
        var json = JsonConvert.SerializeObject(profile);
        return JsonConvert.DeserializeObject<YargProfile>(json)!;
    }

    [Test]
    public void ApplySessionModifiersChangesEffectiveModifiers()
    {
        var selector = CreateVocalsProfile("selector");
        selector.AddSingleModifier(Modifier.NoVocalPercussion);

        var other = CreateVocalsProfile("other");
        other.ApplySessionModifiers(selector);

        Assert.That(other.IsModifierActive(Modifier.NoVocalPercussion), Is.True);
    }

    [Test]
    public void ApplySessionModifiersDoesNotPersist()
    {
        var selector = CreateVocalsProfile("selector");

        var other = CreateVocalsProfile("other");
        other.AddSingleModifier(Modifier.UnpitchedOnly);
        other.ApplySessionModifiers(selector);

        using (Assert.EnterMultipleScope())
        {
            // Effective modifiers follow the selector for this session...
            Assert.That(other.IsModifierActive(Modifier.UnpitchedOnly), Is.False);
            // ...but the saved selection survives serialization.
            var reloaded = JsonRoundTrip(other);
            Assert.That(reloaded.IsModifierActive(Modifier.UnpitchedOnly), Is.True);
        }
    }

    [Test]
    public void RestoreSavedModifiersDiscardsSessionModifiers()
    {
        var selector = CreateVocalsProfile("selector");
        selector.AddSingleModifier(Modifier.NoVocalPercussion);

        var other = CreateVocalsProfile("other");
        other.AddSingleModifier(Modifier.UnpitchedOnly);
        other.ApplySessionModifiers(selector);

        // Mid-session, "other" runs with the selector's modifiers.
        Assert.That(other.IsModifierActive(Modifier.NoVocalPercussion), Is.True);
        Assert.That(other.IsModifierActive(Modifier.UnpitchedOnly), Is.False);

        other.RestoreSavedModifiers();

        using (Assert.EnterMultipleScope())
        {
            // Back to its own saved selection, with no trace of the session value.
            Assert.That(other.IsModifierActive(Modifier.UnpitchedOnly), Is.True);
            Assert.That(other.IsModifierActive(Modifier.NoVocalPercussion), Is.False);
        }
    }

    [Test]
    public void RestoreSavedModifiersIsIdempotentForUntouchedProfile()
    {
        var profile = CreateVocalsProfile("player");
        profile.AddSingleModifier(Modifier.NoVocalPercussion);

        profile.RestoreSavedModifiers();

        Assert.That(profile.IsModifierActive(Modifier.NoVocalPercussion), Is.True);
    }

    [Test]
    public void CopyModifiersStillPersists()
    {
        var selector = CreateVocalsProfile("selector");

        var other = CreateVocalsProfile("other");
        other.AddSingleModifier(Modifier.UnpitchedOnly);
        other.CopyModifiers(selector);

        var reloaded = JsonRoundTrip(other);
        Assert.That(reloaded.IsModifierActive(Modifier.UnpitchedOnly), Is.False);
    }

    [Test]
    public void ExplicitModifierEditsPersist()
    {
        var profile = CreateVocalsProfile("player");
        profile.AddSingleModifier(Modifier.NoVocalPercussion);
        profile.AddSingleModifier(Modifier.UnpitchedOnly);
        profile.RemoveModifiers(Modifier.NoVocalPercussion);

        var reloaded = JsonRoundTrip(profile);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reloaded.IsModifierActive(Modifier.UnpitchedOnly), Is.True);
            Assert.That(reloaded.IsModifierActive(Modifier.NoVocalPercussion), Is.False);
        }
    }

    [Test]
    public void DeserializationSeedsEffectiveModifiers()
    {
        var profile = CreateVocalsProfile("player");
        profile.AddSingleModifier(Modifier.NoVocalPercussion);

        var reloaded = JsonRoundTrip(profile);

        Assert.That(reloaded.IsModifierActive(Modifier.NoVocalPercussion), Is.True);
    }

    [Test]
    public void LegacyProfileJsonLoadsModifiers()
    {
        // Profiles written before the saved/effective split carry the modifiers
        // under the same "CurrentModifiers" property name.
        var profile = CreateVocalsProfile("player");
        var json = JsonConvert.SerializeObject(profile);
        Assert.That(json, Does.Contain("\"CurrentModifiers\""));

        var withModifier = json.Replace("\"CurrentModifiers\":0",
            $"\"CurrentModifiers\":{(ulong) Modifier.UnpitchedOnly}");
        Assert.That(withModifier, Is.Not.EqualTo(json), "test setup: expected to inject a modifier value");

        var reloaded = JsonConvert.DeserializeObject<YargProfile>(withModifier)!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reloaded.IsModifierActive(Modifier.UnpitchedOnly), Is.True);
            // And it round-trips back out under the same name.
            var rewritten = JsonRoundTrip(reloaded);
            Assert.That(rewritten.IsModifierActive(Modifier.UnpitchedOnly), Is.True);
        }
    }

    #region Per-part unpitching (ApplyVocalModifiers partIndex)

    private static VocalsPart CreatePartWithPitchedNote(int harmonyPart = 0)
    {
        var phraseParent = new VocalNote(NoteFlags.None, false, 0, 1.0, 0, 480);
        phraseParent.AddChildNote(
            new VocalNote(60f, harmonyPart, VocalNoteType.Lyric, 0, 0.5, 0, 240));
        var phrase = new VocalsPhrase(0, 1.0, 0, 480, phraseParent, new());
        return new VocalsPart(harmonyPart > 0, new() { phrase }, new(), new(), new());
    }

    private static VocalsPart CreatePartWithPercussion()
    {
        var phraseParent = new VocalNote(NoteFlags.None, false, 0, 2.0, 0, 960);
        phraseParent.AddChildNote(
            new VocalNote(60f, 0, VocalNoteType.Lyric, 0, 0.5, 0, 240));
        phraseParent.AddChildNote(
            new VocalNote(-1f, 0, VocalNoteType.Percussion, 0.1, 0.1, 100, 20));
        var phrase = new VocalsPhrase(0, 2.0, 0, 960, phraseParent, new());
        return new VocalsPart(false, new() { phrase }, new(), new(), new());
    }

    private static bool AllLyricNotesUnpitched(VocalsPart part)
    {
        foreach (var phrase in part.NotePhrases)
        {
            foreach (var note in phrase.PhraseParentNote.ChildNotes)
            {
                if (note.Type == VocalNoteType.Percussion) continue;
                if (!note.IsNonPitched) return false;
            }
        }
        return true;
    }

    private static bool HasPercussion(VocalsPart part)
    {
        foreach (var phrase in part.NotePhrases)
        {
            foreach (var note in phrase.PhraseParentNote.ChildNotes)
            {
                if (note.Type == VocalNoteType.Percussion) return true;
            }
        }
        return false;
    }

    [Test]
    public void ApplyVocalModifiers_UnpitchedOnly_ConvertsPart0()
    {
        var profile = CreateVocalsProfile("player");
        profile.AddSingleModifier(Modifier.UnpitchedOnly);
        var part = CreatePartWithPitchedNote();

        profile.ApplyVocalModifiers(part, 0);

        Assert.That(AllLyricNotesUnpitched(part), Is.True);
    }

    [Test]
    public void ApplyVocalModifiers_UnpitchedOnly_DoesNotConvertPart1()
    {
        var profile = CreateVocalsProfile("player");
        profile.AddSingleModifier(Modifier.UnpitchedOnly);
        var part = CreatePartWithPitchedNote(1);

        profile.ApplyVocalModifiers(part, 1);

        Assert.That(AllLyricNotesUnpitched(part), Is.False);
    }

    [Test]
    public void ApplyVocalModifiers_UnpitchedHarm2_ConvertsPart1()
    {
        var profile = CreateVocalsProfile("player");
        profile.AddSingleModifier(Modifier.UnpitchedHarm2);
        var part = CreatePartWithPitchedNote(1);

        profile.ApplyVocalModifiers(part, 1);

        Assert.That(AllLyricNotesUnpitched(part), Is.True);
    }

    [Test]
    public void ApplyVocalModifiers_UnpitchedHarm2_DoesNotConvertPart0()
    {
        var profile = CreateVocalsProfile("player");
        profile.AddSingleModifier(Modifier.UnpitchedHarm2);
        var part = CreatePartWithPitchedNote();

        profile.ApplyVocalModifiers(part, 0);

        Assert.That(AllLyricNotesUnpitched(part), Is.False);
    }

    [Test]
    public void ApplyVocalModifiers_UnpitchedHarm3_ConvertsPart2()
    {
        var profile = CreateVocalsProfile("player");
        profile.AddSingleModifier(Modifier.UnpitchedHarm3);
        var part = CreatePartWithPitchedNote(2);

        profile.ApplyVocalModifiers(part, 2);

        Assert.That(AllLyricNotesUnpitched(part), Is.True);
    }

    [Test]
    public void ApplyVocalModifiers_AllThreeToggles_ConvertMatchingParts()
    {
        var profile = CreateVocalsProfile("player");
        profile.AddSingleModifier(Modifier.UnpitchedOnly);
        profile.AddSingleModifier(Modifier.UnpitchedHarm2);
        profile.AddSingleModifier(Modifier.UnpitchedHarm3);

        var part0 = CreatePartWithPitchedNote(0);
        var part1 = CreatePartWithPitchedNote(1);
        var part2 = CreatePartWithPitchedNote(2);

        profile.ApplyVocalModifiers(part0, 0);
        profile.ApplyVocalModifiers(part1, 1);
        profile.ApplyVocalModifiers(part2, 2);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(AllLyricNotesUnpitched(part0), Is.True);
            Assert.That(AllLyricNotesUnpitched(part1), Is.True);
            Assert.That(AllLyricNotesUnpitched(part2), Is.True);
        }
    }

    [Test]
    public void ApplyVocalModifiers_MixedToggles_OnlyMatchingPartsConverted()
    {
        // Part 1 off, Parts 2+3 on
        var profile = CreateVocalsProfile("player");
        profile.AddSingleModifier(Modifier.UnpitchedHarm2);
        profile.AddSingleModifier(Modifier.UnpitchedHarm3);

        var part0 = CreatePartWithPitchedNote(0);
        var part1 = CreatePartWithPitchedNote(1);
        var part2 = CreatePartWithPitchedNote(2);

        profile.ApplyVocalModifiers(part0, 0);
        profile.ApplyVocalModifiers(part1, 1);
        profile.ApplyVocalModifiers(part2, 2);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(AllLyricNotesUnpitched(part0), Is.False);
            Assert.That(AllLyricNotesUnpitched(part1), Is.True);
            Assert.That(AllLyricNotesUnpitched(part2), Is.True);
        }
    }

    [Test]
    public void ApplyVocalModifiers_NoVocalPercussion_RemovesRegardlessOfPartIndex()
    {
        var profile = CreateVocalsProfile("player");
        profile.AddSingleModifier(Modifier.NoVocalPercussion);

        var part1 = CreatePartWithPercussion();

        // Percussion removal must work for every part index, not just 0.
        profile.ApplyVocalModifiers(part1, 1);

        Assert.That(HasPercussion(part1), Is.False);
    }

    #endregion
}
