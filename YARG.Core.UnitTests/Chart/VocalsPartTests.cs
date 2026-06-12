using NUnit.Framework;
using YARG.Core.Chart;

namespace YARG.Core.UnitTests.Chart;

public class VocalsTrackTests
{
    [Test]
    public void CloneAsInstrumentDifficultyDoesNotShareNotesBetweenClones()
    {
        var part = CreatePartWithSungPhrase();

        var first = part.CloneAsInstrumentDifficulty();
        var second = part.CloneAsInstrumentDifficulty();

        first.Notes[0].SetHitState(true, true);
        first.Notes[0].SetMissState(true, true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(second.Notes[0], Is.Not.SameAs(first.Notes[0]));
            Assert.That(second.Notes[0].WasHit, Is.False);
            Assert.That(second.Notes[0].WasMissed, Is.False);
            Assert.That(part.NotePhrases[0].PhraseParentNote.WasHit, Is.False);
            Assert.That(part.NotePhrases[0].PhraseParentNote.WasMissed, Is.False);
        }
    }

    [Test]
    public void CloneAsInstrumentDifficultyPreservesPitchSlideChildNotes()
    {
        var part = CreatePartWithSungPhrase();

        var cloned = part.CloneAsInstrumentDifficulty();
        var phrase = cloned.Notes[0];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(phrase.ChildNotes, Has.Count.EqualTo(2));
            Assert.That(phrase.ChildNotes[1].ChildNotes, Has.Count.EqualTo(1));
            Assert.That(phrase.ChildNotes[1].ChildNotes[0].Pitch, Is.EqualTo(64));
        }
    }

    private static VocalsPart CreatePartWithSungPhrase()
    {
        var parentNote = new VocalNote(NoteFlags.None, false, 1.0, 0.4, 100, 40);
        parentNote.AddChildNote(new VocalNote(60, 0, VocalNoteType.Lyric, 1.0, 0.1, 100, 10));

        var slideNote = new VocalNote(62, 0, VocalNoteType.Lyric, 1.2, 0.1, 120, 10);
        parentNote.AddChildNote(slideNote);
        slideNote.AddChildNote(new VocalNote(64, 0, VocalNoteType.Lyric, 1.3, 0.1, 130, 10));

        var phrase = new VocalsPhrase(1.0, 0.4, 100, 40, parentNote, []);
        return new VocalsPart(false, [phrase], [], [], []);
    }
}
