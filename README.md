# TotallyNotYugioh

A Unity card-duel prototype focused on a playable, Synchro-era single-player duel. It includes a searchable card catalog, deck builder, AI opponent, themed AI-versus-AI lab, pack shop, and editor card-authoring tools. It is a prototype rules engine—not a complete Yu-Gi-Oh! simulator.

## Project snapshot

- **Unity:** `6000.3.20f1`; open this folder as the Unity project.
- **Entry scene:** `Assets/Scenes/SampleScene.unity`.
- **Catalog:** `Assets/Resources/all_ygo_cards.csv`, loaded from Unity Resources. The latest checked coverage report records 14,137 unique cards.
- **Duel rules:** centralized in `DuelEngine`; only explicitly implemented mechanics behave as effects.
- **Deck validation:** duels require at least 40 Main Deck cards; the editor's current structure-deck validation allows 40–60 Main Deck cards, up to 15 Extra Deck cards, and at most three copies of a card.
- **Test snapshot:** `TestReports/Card-Effect-Mock-Duel-Report.md` was generated on 2026-11-10. It reports 857 implemented catalog cards (763 vanilla/no-effect and 94 tested effects), all 68 cards across four configured structure decks covered, and 160 card/effect report tests passing. Treat these as report-time figures, not a live test run.

## Start here

1. Open the project in Unity `6000.3.20f1` and run the sample scene.
2. Read [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the code map and state/data flows.
3. Use [Assets/DuelPrototype/DESIGN_SPEC.md](Assets/DuelPrototype/DESIGN_SPEC.md) for product intent and prototype limits.
4. Before coding, check `git status` and inspect the relevant diff; preserve existing local changes.
5. Run the Unity Test Runner's **EditMode** suite (`DuelPrototype.EditModeTests`) for changes to rules, catalog mapping, or coverage.

## Documentation

- [Architecture and data flow](docs/ARCHITECTURE.md)
- [Duel-state JSON and fixtures](Assets/DuelPrototype/Examples/DuelStates/README.md)
- [Next-agent swarm prompts](docs/AGENT-SWARM-PROMPTS.md)
- [Compact chat handoff](NEW-CHAT-HANDOFF.md)
- [Generated coverage report](TestReports/Card-Effect-Mock-Duel-Report.md)

## Source map

| Area | Primary files |
|---|---|
| Card model, import, presets | `Assets/DuelPrototype/Scripts/Data/CardDefinition.cs`, `CardCatalogCsvImporter.cs`, `StarterCatalog.cs` |
| Rules, AI decisions, state snapshots | `Assets/DuelPrototype/Scripts/Core/DuelEngine.cs`, `DuelAiEvaluator.cs`, `DuelStateSnapshot.cs`, `DuelStateJson.cs` |
| Runtime screens and input | `Assets/DuelPrototype/Scripts/Runtime/DuelPrototypeApp.cs` |
| Editor card tools | `Assets/DuelPrototype/Scripts/Editor/CardStudioWindow.cs` |
| Automated behavior and coverage tests | `Assets/DuelPrototype/Tests/EditMode/CardEffectMockDuelTests.cs` |

For the next implementation task, choose one mechanic or a small cohesive card group, test card-attributed behavior, and regenerate the report. The prompts document explains how to split work without having multiple agents edit shared core files at once.
