# Solo Boundary Tests

This document explains the solo boundary tests in `ChartReaderProcessListsTests.cs` and clarifies what is and is not being tested there.

## Background

In `.chart` format, solo phrase boundaries are **inclusive** — a note at the exact solo end tick is inside the solo. This is implemented via two cooperating mechanisms:

1. **`ChartReader.ProcessLists.cs` — `ConvertSoloEvents`:** Converts `solo`/`soloend` text events into `MoonPhrase` objects. The phrase length is `soloend.tick - solo.tick` (no `+1`).
2. **`MoonSongLoader.cs` — `IsEventInPhrase`:** Checks whether a note falls inside a phrase. For `.chart` files, `_inclusiveSoloBoundary = true`, which causes it to use `<=` on the end tick rather than `<`.

This was previously broken by "double inclusivity": `ConvertSoloEvents` added `+1` to the phrase length AND `IsEventInPhrase` used `<=`, causing solos to extend one tick further than authored. The fix removed the `+1`, making the phrase length exact and letting `_inclusiveSoloBoundary` provide the inclusive check alone.

---

## What `ChartReaderProcessListsTests` actually tests

These tests exercise the full `.chart` parse pipeline via `SongChart.FromDotChart`, which internally runs `ChartReader` → `MoonSongLoader`. The solo flag on each note is assigned in `MoonSongLoader.GetNotes` (via `GetGeneralFlags` → `IsEventInPhrase`) based on the `MoonNote`'s tick from `ChartReader`. **These tests cover:**

- `ConvertSoloEvents` correctly building `MoonPhrase` objects from `solo`/`soloend` events
- `IsEventInPhrase` with `_inclusiveSoloBoundary = true` (inclusive end)
- The zero-length phrase special case in `IsEventInPhrase`
- Back-to-back solos sharing a boundary tick (handled in `ConvertSoloEvents`)

---

## Test Descriptions

### 1. `SoloBoundaries_NoteAtSoloEndTick_HasSoloFlag`
**Tests:** Inclusive solo end boundary — the core regression test for the `+1` removal.

For a solo from tick 500 to 1000:
- Note at 400 → ❌ No Solo (before solo start)
- Note at 600 → ✅ Solo
- Note at 800 → ✅ Solo
- Note at 1000 (exact end) → ✅ Solo (inclusive boundary)
- Note at 1200 → ❌ No Solo

---

### 2. `SoloBoundaries_NoteJustAfterSoloEnd_HasNoSoloFlag`
**Tests:** The tick immediately after the inclusive end is excluded.

For a solo from tick 500 to 1000:
- Note at 1000 → ✅ Solo (inclusive end)
- Note at 1001 → ❌ No Solo (first tick outside)

Paired with test 1 to pin both sides of the boundary precisely.

---

### 3. `SoloBoundaries_ChordAtSoloEnd_AllNotesInSolo`
**Tests:** Multi-note chords at the solo boundary.

Notes written at the same tick are treated as a chord. All notes in a chord at the solo end tick should have the Solo flag:
- Chord at tick 600 (inside solo) → All 3 notes ✅ Solo
- Chord at tick 1000 (solo end) → All 5 notes ✅ Solo
- Chord at tick 1020 (after solo) → All 2 notes ❌ No Solo

---

### 4. `SoloBoundaries_BackToBackSolos_NoteAtBoundary_InSecondSolo`
**Tests:** `ConvertSoloEvents` handling of consecutive solos sharing a boundary tick.

When a `soloend` and `solo` appear at the same tick, `ConvertSoloEvents` starts the second solo at that tick rather than treating the `soloend` as closing both. A note at the shared boundary tick (1000) belongs to the second solo:
- Note at 600 → ✅ Solo (first solo)
- Note at 1000 → ✅ Solo (second solo starts here)
- Note at 1100 → ✅ Solo (second solo)

---

### 5. `SoloBoundaries_ZeroLengthSolo_NoteAtTick_HasSoloFlag`
**Tests:** `IsEventInPhrase` special case for zero-length phrases.

`ConvertSoloEvents` can produce a `MoonPhrase` with length 0 when `solo` and `soloend` appear at the same tick. `IsEventInPhrase` has an explicit guard for this: `return songObj.tick == phrase.tick`. Without it, the phrase range check `tick <= note.tick <= tick + 0` would never match anything with the inclusive end if not handled.

- Zero-length solo at tick 500
- Note at 500 → ✅ Solo
- Note at 520 → ❌ No Solo

---

## What these tests do NOT cover: chord snapping

`NoteSnapThreshold` chord snapping happens in `MoonSongLoader.AddNoteToList`, which is called **after** `createNote` (which is where solo flags are assigned). This means:

- Solo flags are computed from the original `MoonNote` tick before any snapping occurs.
- When `CopyValuesFrom` is called on a snapped note, it copies the parent's tick **and all flags** wholesale — including the solo flag the parent already received.
- The snapped note's solo flag therefore reflects the parent's boundary check, not its own original tick.

The two chord-snap tests that were previously in this file (`ChordSnappingFromOutside_SnappedNoteNotInSolo`, later split into `NoteAfterEnd_SnapsToInsideParent_HasSoloFlag` and `NoteAtStart_SnapsToOutsideParent_HasNoSoloFlag`) were removed because they were effectively testing that `CopyValuesFrom` copies flags — not that solo boundary logic handles snapping correctly. They belonged in a `MoonSongLoader` integration test, not here.

**Where chord-snap + solo boundary tests belong:** A `MoonSongLoaderTests` file, calling `MoonSongLoader` directly (not via `SongChart.FromDotChart`) with a pre-built `MoonSong` containing known `MoonNote` ticks and `MoonPhrase` objects, with `NoteSnapThreshold` set. This would let you verify that notes snapped across a solo boundary carry the correct flag from the parent without the `.chart` parse pipeline in the way.
