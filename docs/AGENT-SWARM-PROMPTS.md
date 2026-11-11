# Next-agent swarm prompts

Use these as small, task-focused prompts; do not preload the whole repository. Give every agent this shared preamble, then one task prompt:

> Read `README.md` and `docs/ARCHITECTURE.md`, then inspect only the files named in your task and their direct callers/tests. First check `git status` and preserve all existing changes. Treat the latest test report as a dated snapshot, not proof of current behavior. Keep the task bounded, report exact files and tests, and do not commit unless asked.

## Coordinator (planning only)

> Split the requested work into independent tasks with explicit file ownership and acceptance tests. Identify shared-file conflicts before launching workers. Read-only discovery can run in parallel; assign only one writer at a time to `DuelEngine.cs`, `CardCatalogCsvImporter.cs`, and `CardEffectMockDuelTests.cs`, unless workers have isolated worktrees and will integrate deliberately. Return the task assignments and stop; do not edit files.

## Coverage scout (read-only)

> Inspect the effect mappings, card-attributed tests, and generated coverage report for one requested card family. Return a short table of card IDs/names, current mapped mechanic, existing tests, uncovered rules cases, and the smallest likely implementation surface. Do not edit files or claim effects work based on text/mappings alone.

## Effect implementation (one mechanic/group)

> Implement the requested mechanic for only the specified card IDs. Trace existing patterns before editing; update the ID mapping, resolver, and card-attributed EditMode tests as required. Preserve unrelated work. Do not broaden to neighboring cards. Run the targeted Unity EditMode tests, then the coverage/report test if needed; report behavior, files, and exact results.

## AI decision task

> Improve one named AI decision without changing unrelated rules or presentation. Inspect current action-selection and evaluator call paths first. Add focused tests for useful vs. wasteful activation and verify legal actions remain legal. Own `DuelEngine.cs` exclusively while implementing; coordinate rather than editing concurrently with another engine task.

## UI task

> Make the requested change only in the runtime presentation/input surface unless a core API is demonstrably missing. Prefer calling existing `DuelEngine` APIs over mutating state. Add or update a test for extracted pure layout/interaction logic when available; otherwise provide a precise manual Unity verification checklist. Do not edit the engine in parallel with a rules task.

## Final verifier (read-only)

> Review the integrated diff and current test/report results for regressions in the assigned scope. Do not edit. Check acceptance criteria, missing card-attributed tests, and whether report figures match their timestamp. Return only high-confidence issues with file/line references and concise test gaps.

## Context and coordination rules

- Keep each worker to one mechanic, one screen change, or one verification question; return findings in bullets, not copied source dumps.
- Parallelize exploration and read-only review. Do not run multiple writers against the same shared working tree or common source file.
- For a multi-card behavior batch, assign card groups only after checking whether they share the same engine/importer/test files. Serialize those edits or use isolated worktrees and explicitly reconcile them.
- Ask the coordinator to summarize outcomes and unresolved questions in a compact handoff; avoid asking every worker to reread every prior agent's context.
