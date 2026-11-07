# Duel state JSON examples

`starter-main-phase.json` and `opponent-battle-phase.json` are small loadable fixtures for schema version 1. They use catalog card ID `13039848` so the examples remain compact; replace those IDs with any IDs present in `StarterCatalog`.

Load a JSON string or file with `DuelStateJson.Deserialize(json)` or `DuelStateJson.LoadFile(path)`. Save a stable duel state with `DuelStateJson.Serialize(engine)`. The schema stores both players, all card instances and zones, card status, turn state, and the message log. Card instances use unique `instanceId` values, while `cardId` resolves the shared card definition from the local catalog.

The loader rejects unsupported schema versions, unknown catalog IDs, missing or duplicated zone references, invalid phases, and malformed player zones. Capturing is currently limited to stable points: it throws if a response window, Sangan search choice, or queued AI action is pending. For multiplayer, transmit validated snapshots between peers rather than trusting client-provided actions or state.
