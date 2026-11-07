using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace DuelPrototype
{
    public static class DuelStateJson
    {
        public static string Serialize(DuelEngine engine, bool prettyPrint = true)
        {
            if (engine == null)
                throw new ArgumentNullException(nameof(engine));
            return JsonUtility.ToJson(engine.CaptureState(), prettyPrint);
        }

        public static DuelEngine Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("Duel state JSON cannot be empty.", nameof(json));

            DuelStateSnapshot snapshot;
            try
            {
                snapshot = JsonUtility.FromJson<DuelStateSnapshot>(json);
            }
            catch (ArgumentException exception)
            {
                throw new FormatException("Duel state JSON is malformed.", exception);
            }

            Validate(snapshot);
            return DuelEngine.RestoreState(snapshot);
        }

        public static DuelEngine LoadFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A duel state file path is required.", nameof(path));
            return Deserialize(File.ReadAllText(path));
        }

        private static void Validate(DuelStateSnapshot snapshot)
        {
            if (snapshot == null)
                throw new FormatException("Duel state JSON did not contain a state object.");
            if (snapshot.schemaVersion != 1)
                throw new FormatException("Unsupported duel state schema version: " + snapshot.schemaVersion + ".");
            if (snapshot.turnNumber < 1)
                throw new FormatException("Duel state turnNumber must be at least 1.");
            if (!Enum.TryParse(snapshot.phase, false, out DuelPhase phase) ||
                !Enum.IsDefined(typeof(DuelPhase), phase))
                throw new FormatException("Duel state phase is missing or invalid.");
            if (snapshot.human == null || snapshot.opponent == null)
                throw new FormatException("Duel state must include both human and opponent players.");
            if (snapshot.cards == null)
                throw new FormatException("Duel state cards must be an array.");
            if (snapshot.messages == null)
                throw new FormatException("Duel state messages must be an array.");
            if (snapshot.currentPlayer != "human" && snapshot.currentPlayer != "opponent")
                throw new FormatException("currentPlayer must be 'human' or 'opponent'.");
            if (!string.IsNullOrEmpty(snapshot.temporarilyDisabledCardOwner) &&
                snapshot.temporarilyDisabledCardOwner != "human" &&
                snapshot.temporarilyDisabledCardOwner != "opponent")
                throw new FormatException("temporarilyDisabledCardOwner must be 'human', 'opponent', or null.");
            if (phase == DuelPhase.GameOver)
            {
                if (snapshot.winner != snapshot.human.name && snapshot.winner != snapshot.opponent.name)
                    throw new FormatException("A completed duel must name one of its players as the winner.");
            }
            else if (!string.IsNullOrEmpty(snapshot.winner))
            {
                throw new FormatException("A duel that is not over cannot have a winner.");
            }

            Dictionary<string, DuelCardStateSnapshot> cards = new Dictionary<string, DuelCardStateSnapshot>();
            foreach (DuelCardStateSnapshot card in snapshot.cards)
            {
                if (card == null || string.IsNullOrWhiteSpace(card.instanceId) ||
                    string.IsNullOrWhiteSpace(card.cardId))
                    throw new FormatException("Every card must have an instanceId and cardId.");
                if (cards.ContainsKey(card.instanceId))
                    throw new FormatException("Duplicate card instanceId: " + card.instanceId + ".");
                if (StarterCatalog.Find(card.cardId) == null)
                    throw new FormatException("Unknown cardId in duel state: " + card.cardId + ".");
                if (card.fusionMaterialIds == null)
                    throw new FormatException("fusionMaterialIds must be an array for card " + card.instanceId + ".");
                cards.Add(card.instanceId, card);
            }

            HashSet<string> locatedCards = new HashSet<string>(StringComparer.Ordinal);
            ValidatePlayer(snapshot.human, "human", cards, locatedCards);
            ValidatePlayer(snapshot.opponent, "opponent", cards, locatedCards);
            if (locatedCards.Count != cards.Count)
                throw new FormatException("Every card instance must belong to exactly one player zone.");
            foreach (DuelCardStateSnapshot card in snapshot.cards)
            {
                foreach (string materialId in card.fusionMaterialIds)
                    RequireCardReference(materialId, card.instanceId, cards);
                if (!string.IsNullOrEmpty(card.equippedMonsterId))
                    RequireCardReference(card.equippedMonsterId, card.instanceId, cards);
            }
        }

        private static void ValidatePlayer(
            DuelPlayerStateSnapshot player,
            string playerKey,
            Dictionary<string, DuelCardStateSnapshot> cards,
            HashSet<string> locatedCards)
        {
            if (string.IsNullOrWhiteSpace(player.name))
                throw new FormatException(playerKey + " player name cannot be empty.");
            if (player.lifePoints < 0)
                throw new FormatException(playerKey + " lifePoints cannot be negative.");
            if (player.deck == null || player.hand == null || player.graveyard == null ||
                player.banished == null || player.monsterZones == null || player.spellTrapZones == null ||
                player.extraDeck == null)
                throw new FormatException(playerKey + " player zones must all be arrays.");
            if (player.monsterZones.Length != 5 || player.spellTrapZones.Length != 5)
                throw new FormatException(playerKey + " must have exactly five monster and five spell/trap zones.");
            if (player.normalSummonsThisTurn < 0 || player.noAttackTurnsRemaining < 0 ||
                player.positionChangesLockedTurnsRemaining < 0)
                throw new FormatException(playerKey + " has an invalid negative turn counter.");

            IEnumerable<string> zoneReferences = player.deck
                .Concat(player.hand)
                .Concat(player.graveyard)
                .Concat(player.banished)
                .Concat(player.monsterZones)
                .Concat(player.spellTrapZones)
                .Concat(player.extraDeck)
                .Concat(new[] { player.fieldSpellZone });
            foreach (string instanceId in zoneReferences.Where(id => !string.IsNullOrEmpty(id)))
            {
                if (!cards.ContainsKey(instanceId))
                    throw new FormatException(playerKey + " references missing card instance " + instanceId + ".");
                if (!locatedCards.Add(instanceId))
                    throw new FormatException("Card instance " + instanceId + " appears in more than one zone.");
            }
        }

        private static void RequireCardReference(
            string reference,
            string sourceId,
            Dictionary<string, DuelCardStateSnapshot> cards)
        {
            if (string.IsNullOrWhiteSpace(reference) || !cards.ContainsKey(reference))
                throw new FormatException("Card " + sourceId + " references missing card instance " + reference + ".");
        }
    }
}
