# Compact project handoff

## What this is

Unity `6000.3.20f1` prototype for a playable single-player, Synchro-era card duel. It has a CSV-backed catalog, deck builder/shop, scripted rules and AI opponent, AI-vs-AI lab, test-duel lab, local profile, and editor card tools. It is not a full official-rules simulator.

## Read only what you need

- Begin at [`README.md`](README.md) for the project snapshot and source map.
- Read [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for ownership, data flow, state, and tests.
- Read [`docs/AGENT-SWARM-PROMPTS.md`](docs/AGENT-SWARM-PROMPTS.md) only when delegating.
- Duel-state fixtures and schema notes: [`Assets/DuelPrototype/Examples/DuelStates/README.md`](Assets/DuelPrototype/Examples/DuelStates/README.md).

## Before changing code

1. Check `git status` and inspect diffs; the working tree may contain user work. Never assume it is clean or overwrite unrelated edits.
2. Identify one mechanic or behavior at a time. Card effects need an explicit ID mapping, engine behavior, and card-attributed EditMode test; imported text is not implementation.
3. Run Unity EditMode tests when changing rules, mappings, or report logic. Verify the generated report timestamp/results before quoting counts.
4. Do not commit unless requested.

## Last recorded coverage snapshot

The report generated 2026-11-10 records 14,137 unique cards, 857 implemented (763 vanilla/no-effect and 94 tested effects), all 68 cards in the four configured structure decks covered, and 160 card/effect report tests passed with none failed. These are report-time figures, not a guarantee of current branch status or a fresh test run. See [`TestReports/Card-Effect-Mock-Duel-Report.md`](TestReports/Card-Effect-Mock-Duel-Report.md).

## Important constraints

- The engine enforces a 40-card minimum Main Deck. Check current builder/validator rules before changing deck limits.
- `DuelEngine` is the rules/state owner; runtime UI should request actions through it.
- Duel-state JSON is schema v1 and separate from the `PlayerPrefs` collection/deck/credits profile. Normal capture is limited to stable points; fixture details are documented above.
- The main shared files are `DuelEngine.cs`, `CardCatalogCsvImporter.cs`, and `CardEffectMockDuelTests.cs`. Do not assign multiple agents to edit the same file concurrently in a shared working tree.
