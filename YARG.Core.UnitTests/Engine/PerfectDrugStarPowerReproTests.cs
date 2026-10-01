using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using MoonscraperChartEditor.Song.IO;
using YARG.Core.Chart;
using YARG.Core.Engine.Drums;
using YARG.Core.Engine.Drums.Engines;
using YARG.Core.Game;

namespace YARG.Core.UnitTests.Engine;

[TestFixture]
[Explicit("Local repro for Perfect Drug upgrade; requires YARG_PERFECT_DRUG_MIDI path")]
public sealed class PerfectDrugStarPowerReproTests
{
    [Test]
    public void EnumerateStarPowerPhrasesAndRunNativeBotWithAutoPedal()
    {
        string path = Environment.GetEnvironmentVariable("YARG_PERFECT_DRUG_MIDI") ??
            "/Users/xaiax/Local/song_edits/Updates2/songs_updates/perfectdrug/perfectdrug_update.mid";
        if (!File.Exists(path)) Assert.Ignore($"Perfect Drug MIDI not found: {path}");

        var song = MidReader.ReadMidi(path);
        var loader = new MoonSongLoader(song, ParseSettings.Default);
        var track = loader.LoadEliteDrumsTrack(Instrument.EliteDrums);
        var syncTrack = loader.LoadSyncTrack();
        var expert = track.GetDifficulty(Difficulty.Expert);
        var phraseNotes = expert.Notes.SelectMany(parent => parent.ChildNotes.Prepend(parent)).ToArray();
        var starPhrases = expert.Phrases.Where(phrase => phrase.Type == PhraseType.StarPower).OrderBy(phrase => phrase.Tick).ToArray();
        TestContext.Progress.WriteLine($"Parsed Expert note-chords={expert.Notes.Count}, physical-notes={phraseNotes.Length}, star-phrases={starPhrases.Length}");
        for (int index = 0; index < starPhrases.Length; index++)
        {
            var phrase = starPhrases[index];
            var notes = phraseNotes.Where(note => note.Tick >= phrase.Tick && note.Tick <= phrase.Tick + phrase.TickLength)
                .OrderBy(note => note.Time).ToArray();
            TestContext.Progress.WriteLine($"phrase={index + 1} {phrase.Time:F3}-{phrase.TimeEnd:F3}s ticks={phrase.Tick}-{phrase.Tick + phrase.TickLength} notes={notes.Length}; " +
                string.Join("; ", notes.Select(note => $"{note.Time:F3}s/t{note.Tick}/pad={note.Pad}/pedal={note.HatPedalType}/hat={note.HatState}/sp={note.IsStarPower}/start={note.IsStarPowerStart}/parent={note.Parent?.Time:F3}")));
        }

        Assert.That(starPhrases, Has.Length.EqualTo(18), "Expected chart content is 18 SP phrases; inspect parsed phrase boundaries above.");
        var parameters = EnginePreset.Default.Drums.Create([1f, 2f, 3f, 4f, 5f, 6f],
            [1f, 2f, 3f, 4f, 5f, 6f], DrumsEngineParameters.DrumMode.Elite);
        var engine = new EliteDrumsEngine(expert, syncTrack, parameters, isBot: true,
            isMidiDrumsInput: true, autoHiHatPedal: true);
        engine.OnStarPowerPhraseHit += note => TestContext.Progress.WriteLine($"AWARD t={note.Time:F3} tick={note.Tick} pad={note.Pad} parent={note.Parent?.Pad} full={note.ParentOrSelf.WasFullyHit()} flags={note.Flags}");
        engine.OnStarPowerPhraseMissed += note => TestContext.Progress.WriteLine($"STRIP t={note.Time:F3} tick={note.Tick} pad={note.Pad} flags={note.Flags}");
        TestContext.Progress.WriteLine($"NoStarPowerOverlap={parameters.NoStarPowerOverlap}");
        double end = phraseNotes.Max(note => note.Time) + 20;
        for (double time = 0; time <= end; time += 0.01)
            engine.Update(time);

        for (int index = 0; index < starPhrases.Length; index++)
        {
            var phrase = starPhrases[index];
            var notes = phraseNotes.Where(note => note.Tick >= phrase.Tick && note.Tick <= phrase.Tick + phrase.TickLength)
                .OrderBy(note => note.Time).ToArray();
            TestContext.Progress.WriteLine($"result phrase={index + 1} hit={notes.Count(note => note.WasHit)}/{notes.Length} missed={notes.Count(note => note.WasMissed)} " +
                string.Join("; ", notes.Where(note => !note.WasHit).Select(note => $"MISS {note.Time:F3}s/t{note.Tick}/pad={note.Pad}/pedal={note.HatPedalType}/hat={note.HatState}/parent={note.Parent?.Time:F3}")));
        }
        TestContext.Progress.WriteLine($"phrases-awarded={engine.EngineStats.StarPowerPhrasesHit}; notes-hit={engine.EngineStats.NotesHit}; optional-pedal-hits={engine.EngineStats.OptionalPedalHits}; assisted-pedal={engine.EngineStats.AssistedPedalNotes}");
        for (int index = 0; index < starPhrases.Length; index++)
        {
            var phrase = starPhrases[index];
            var notes = phraseNotes.Where(note => note.Tick >= phrase.Tick && note.Tick <= phrase.Tick + phrase.TickLength)
                .OrderBy(note => note.Time).ToArray();
            TestContext.Progress.WriteLine($"flags phrase={index + 1} start={notes.Count(note => note.IsStarPowerStart)} end={notes.Count(note => note.IsStarPowerEnd)} " +
                string.Join("; ", notes.Where(note => note.IsStarPowerStart || note.IsStarPowerEnd).Select(note => $"{note.Time:F3}/t{note.Tick}/pad={note.Pad}/start={note.IsStarPowerStart}/end={note.IsStarPowerEnd}/hit={note.WasHit}/parent={note.Parent?.WasHit}/{note.Parent?.WasFullyHit()}")));
        }
        Assert.That(engine.EngineStats.StarPowerPhrasesHit, Is.EqualTo(18));
    }
}
