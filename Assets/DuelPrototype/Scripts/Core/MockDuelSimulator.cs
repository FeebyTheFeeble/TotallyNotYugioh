using System;
using System.Collections.Generic;
using System.Linq;

namespace DuelPrototype
{
    [Flags]
    public enum MockDuelFixtures
    {
        None = 0,
        PlayerMonster = 1,
        OpponentMonster = 2,
        OpponentSetTrap = 4,
        PlayerGraveyardMonster = 8,
        AutomaticEffectSupport = 16
    }

    public sealed class MockDuelSimulator
    {
        public DuelEngine Engine { get; }
        public CardInstance FocusCard { get; }
        public MockDuelFixtures Fixtures { get; }
        public string ManualResult { get; private set; } = "PENDING";

        private MockDuelSimulator(
            DuelEngine engine,
            CardInstance focusCard,
            MockDuelFixtures fixtures)
        {
            Engine = engine;
            FocusCard = focusCard;
            Fixtures = fixtures;
        }

        public static MockDuelSimulator Start(
            IReadOnlyList<CardDefinition> playerDeck,
            IReadOnlyList<CardDefinition> opponentDeck,
            CardDefinition focusDefinition,
            MockDuelFixtures fixtures,
            CardDefinition fixtureMonster,
            CardDefinition fixtureTrap)
        {
            if (focusDefinition == null)
                throw new ArgumentNullException(nameof(focusDefinition));
            if (fixtureMonster == null || !fixtureMonster.CanNormalSummon)
                throw new ArgumentException("A Normal Summonable fixture monster is required.", nameof(fixtureMonster));
            if (fixtureTrap == null || fixtureTrap.Kind != CardKind.Trap)
                throw new ArgumentException("A Trap fixture card is required.", nameof(fixtureTrap));

            DuelEngine engine = new DuelEngine();
            engine.StartMatch(playerDeck, opponentDeck, false, "TEST DUELIST", "CPU TEST RIVAL");

            CardInstance focusCard = engine.Human.Hand.FirstOrDefault(card => card.Definition == focusDefinition);
            if (focusCard == null)
            {
                focusCard = engine.Human.Deck.FirstOrDefault(card => card.Definition == focusDefinition);
                if (focusCard != null)
                    engine.Human.Deck.Remove(focusCard);
                else
                    focusCard = new CardInstance(focusDefinition);
                engine.Human.Hand.Add(focusCard);
            }

            AddFixtureMonster(engine.Human, fixtureMonster, fixtures, MockDuelFixtures.PlayerMonster, engine.TurnNumber);
            AddFixtureMonster(engine.Opponent, fixtureMonster, fixtures, MockDuelFixtures.OpponentMonster, engine.TurnNumber);
            if ((fixtures & MockDuelFixtures.OpponentSetTrap) != 0)
            {
                engine.Opponent.SpellTrapZones[0] = new CardInstance(fixtureTrap, false, false);
            }
            if ((fixtures & MockDuelFixtures.PlayerGraveyardMonster) != 0)
                engine.Human.Graveyard.Add(new CardInstance(fixtureMonster));
            if (AddEffectSupport(engine, focusCard, engine.TurnNumber))
                fixtures |= MockDuelFixtures.AutomaticEffectSupport;

            engine.AdvanceToPhase(DuelPhase.MainOne);
            return new MockDuelSimulator(engine, focusCard, fixtures);
        }

        public void MarkPassed()
        {
            ManualResult = "MANUAL PASS";
        }

        public void MarkFailed()
        {
            ManualResult = "MANUAL FAIL";
        }

        private static void AddFixtureMonster(
            DuelPlayer player,
            CardDefinition definition,
            MockDuelFixtures fixtures,
            MockDuelFixtures fixture,
            int turnNumber)
        {
            if ((fixtures & fixture) == 0)
                return;

            int zone = Array.FindIndex(player.MonsterZones, card => card == null);
            if (zone < 0)
                throw new InvalidOperationException("No Monster Zone is available for the requested test fixture.");
            player.MonsterZones[zone] = new CardInstance(definition)
            {
                SummonedTurnNumber = turnNumber
            };
        }

        private static bool AddEffectSupport(DuelEngine engine, CardInstance focusCard, int turnNumber)
        {
            if (focusCard.Definition.Effect != CardEffect.BlackwingSpecialSummon ||
                engine.Human.MonsterZones.Any(card => card != null && card.FaceUp &&
                    card.Definition.DisplayName.StartsWith("Blackwing", StringComparison.OrdinalIgnoreCase)))
                return false;

            CardDefinition supportDefinition = StarterCatalog.All.FirstOrDefault(card =>
                card.CanNormalSummon && card.DisplayName.StartsWith("Blackwing", StringComparison.OrdinalIgnoreCase) &&
                card.CardId != focusCard.Definition.CardId);
            if (supportDefinition == null)
                throw new InvalidOperationException("No Blackwing support card is available for this test.");

            CardInstance support = engine.Human.Hand.FirstOrDefault(card =>
                card.Definition.CardId == supportDefinition.CardId) ??
                engine.Human.Deck.FirstOrDefault(card => card.Definition.CardId == supportDefinition.CardId);
            if (support != null)
            {
                engine.Human.Hand.Remove(support);
                engine.Human.Deck.Remove(support);
            }
            else
                support = new CardInstance(supportDefinition);

            int zone = Array.FindIndex(engine.Human.MonsterZones, card => card == null);
            if (zone < 0)
                throw new InvalidOperationException("No Monster Zone is available for automatic effect support.");
            support.FaceUp = true;
            support.AttackPosition = true;
            support.SummonedTurnNumber = turnNumber;
            engine.Human.MonsterZones[zone] = support;
            return true;
        }
    }
}
