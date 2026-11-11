# Architecture

## Runtime shape

```text
CSV / CardDefinition assets
            |
            v
 CardCatalogCsvImporter ----> StarterCatalog ----> DuelPrototypeApp
                                      |                  |
                                      v                  | user intent
                                  CardDefinition          v
                                      ^              DuelEngine
                                      |                  |
                               DuelAiEvaluator <---------+
                                                         |
                                      phase/card events + state
                                                         v
                                                generated Unity UI
```

`DuelPrototypeApp` builds the interface in code in the sample scene. `DuelEngine` is the authoritative mutable duel state: UI actions call its validation/action methods rather than editing game zones directly. The engine raises events for phase changes, draws, attacks, summons, and destruction; the app renders those changes and handles user choices.

## Responsibilities

| Component | Responsibility |
|---|---|
| `Data/CardDefinition.cs` | Card enums and ScriptableObject model: identity, type, stats, text, effect ID/value, deck limits, and catalog metadata. |
| `Data/CardCatalogCsvImporter.cs` | Parses the catalog CSV and maps selected card IDs to explicit `CardEffect` mechanics. Imported text alone does not implement an effect. |
| `Data/StarterCatalog.cs` | Loads `Resources/all_ygo_cards.csv`, then additional `Resources/CardCatalog` assets; provides lookup, starter data, and four themed deck presets. |
| `Core/DuelEngine.cs` | Player/card instances and zones, phases, legal actions, summons, effect resolution, response windows, battle, turn flow, AI action execution, and winner detection. |
| `Core/DuelAiEvaluator.cs` | Board/material valuation used by AI choices. It is a prototype heuristic, not a general strategy solver. |
| `Core/DuelStateSnapshot.cs`, `DuelStateJson.cs` | Versioned snapshot DTOs plus JSON serialization, file loading, structural validation, and engine restoration. |
| `Core/MockDuelSimulator.cs` | Creates focused in-game test-duel fixtures around a selected card. |
| `Runtime/DuelPrototypeApp.cs` | Programmatic UI, input and rendering, screen navigation, replay/test-lab interactions, and local player profile. The profile uses `PlayerPrefs`; it is separate from duel-state snapshots. |
| `Editor/CardStudioWindow.cs` | Unity Editor card-asset authoring and structure-deck validation menu commands. |
| `Tests/EditMode/CardEffectMockDuelTests.cs` | EditMode rule/catalog tests, card-attributed effect coverage, and generation of the Markdown report. |

## Data and effect flow

1. Unity loads `all_ygo_cards.csv` as a `TextAsset`; the importer creates card definitions and preserves catalog text/metadata.
2. Curated card-ID mappings assign a `CardEffect`. A mapped ID is only a declaration of intended behavior; the rule resolver and passing card-attributed tests are needed for coverage.
3. `StarterCatalog` adds authored assets with new IDs and constructs the named deck presets.
4. The UI or AI requests a move through `DuelEngine`. The engine validates preconditions and mutates card/player state; effects and event handlers expose resulting prompts or presentation changes.
5. EditMode tests exercise mechanics and catalog identity. The report generator counts only the coverage its tests establish; do not infer coverage from imported text or mappings alone.

When adding an effect, follow the existing ID → enum → resolver → behavior-test path. Test legal and illegal activation, targets/costs, zone transitions, turn/use limits, and AI behavior where applicable. Keep card-specific tests associated with every newly covered card ID.

## Persistence and state snapshots

- Player collection, deck, and credits are stored separately by the runtime app in `PlayerPrefs`.
- Duel snapshot JSON uses schema version 1 and card instance IDs distinct from catalog card IDs. The loader validates card references, zones, and schema before restoring.
- Normal state capture is for stable points and rejects unresolved choice/response states. Replay capture permits queued AI actions. See the [fixture and loader notes](../Assets/DuelPrototype/Examples/DuelStates/README.md).
- Snapshots are local prototype save/replay data, not a trusted multiplayer protocol.

## Test and report workflow

Use Unity **Window > General > Test Runner > EditMode** and run `DuelPrototype.EditModeTests`. The test assembly is editor-only and references the runtime assembly. The suite's teardown writes `TestReports/Card-Effect-Mock-Duel-Report.md`; check the report timestamp and results before citing its numbers. The in-game Test Lab is useful for manual play, but automated EditMode assertions are the regression gate.

## Boundaries

The rules core is broader than a single demo mechanic, but this is not complete official rules coverage. Only curated effects are implemented and covered. Catalog presence, printed effect text, or deck-builder visibility does not imply a functioning effect. Chain timing, activation conditions, and card-specific edge cases may be simplified or absent; inspect the resolver and its tests before assuming support. Deck and format limits are prototype rules and should be checked in code before changing.

For product intent and screen-level design, see [DESIGN_SPEC.md](../Assets/DuelPrototype/DESIGN_SPEC.md). Treat it as a design reference; for current behavior, prefer code and recent test results.
