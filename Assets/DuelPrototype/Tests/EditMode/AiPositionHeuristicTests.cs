using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace DuelPrototype.Tests
{
    public sealed class AiPositionHeuristicTests
    {
        private readonly List<CardDefinition> definitions = new List<CardDefinition>();

        [TearDown]
        public void TearDown()
        {
            foreach (CardDefinition definition in definitions)
                UnityEngine.Object.DestroyImmediate(definition);
            definitions.Clear();
        }

        [Test]
        public void Ai_sets_a_high_defense_monster_when_a_visible_battle_threat_favors_defense()
        {
            DuelEngine engine = CreateAiEngine();
            CardDefinition summonDefinition = CreateMonster("ai-defensive-summon", 900, 2200);
            CardDefinition threatDefinition = CreateMonster("ai-visible-threat", 1800, 900);
            CardInstance summon = new CardInstance(summonDefinition);
            engine.Opponent.Hand.Clear();
            engine.Opponent.Hand.Add(summon);
            engine.Human.MonsterZones[0] = new CardInstance(threatDefinition);

            Assert.IsTrue(engine.StepAiTurn());

            Assert.Contains(summon, engine.Opponent.MonsterZones);
            Assert.IsFalse(summon.FaceUp);
            Assert.IsFalse(summon.AttackPosition);
        }

        [Test]
        public void Ai_keeps_an_unthreatened_high_attack_summon_face_up_in_attack_position()
        {
            DuelEngine engine = CreateAiEngine();
            CardInstance summon = new CardInstance(CreateMonster("ai-aggressive-summon", 2000, 500));
            engine.Opponent.Hand.Clear();
            engine.Opponent.Hand.Add(summon);

            Assert.IsTrue(engine.StepAiTurn());

            Assert.Contains(summon, engine.Opponent.MonsterZones);
            Assert.IsTrue(summon.FaceUp);
            Assert.IsTrue(summon.AttackPosition);
        }

        [Test]
        public void Ai_changes_a_face_up_monster_to_defense_when_it_improves_expected_battle_outcome()
        {
            DuelEngine engine = CreateAiEngine();
            CardInstance monster = new CardInstance(CreateMonster("ai-position-change", 1000, 2300));
            monster.SummonedTurnNumber = 1;
            engine.Opponent.Hand.Clear();
            engine.Opponent.NormalSummonsThisTurn = 1;
            engine.Opponent.MonsterZones[0] = monster;
            engine.Human.MonsterZones[0] = new CardInstance(CreateMonster("ai-position-threat", 2000, 1000));

            for (int step = 0; step < 24 && monster.AttackPosition; step++)
                Assert.IsTrue(engine.StepAiTurn());

            Assert.IsTrue(monster.FaceUp);
            Assert.IsFalse(monster.AttackPosition);
            Assert.IsTrue(monster.HasChangedPositionThisTurn);
        }

        private DuelEngine CreateAiEngine()
        {
            DuelEngine engine = new DuelEngine();
            engine.StartMatch(StarterCatalog.CreateStarterDeck(), StarterCatalog.CreateAiDeck(), aiVsAi: true);
            SetEngineProperty(engine, nameof(DuelEngine.CurrentPlayer), engine.Opponent);
            SetEngineProperty(engine, nameof(DuelEngine.Phase), DuelPhase.MainOne);
            typeof(DuelEngine).GetField("turnNumber", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(engine, 2);
            return engine;
        }

        private CardDefinition CreateMonster(string id, int attack, int defense)
        {
            CardDefinition definition = ScriptableObject.CreateInstance<CardDefinition>();
            definition.Configure(id, id, CardKind.Monster, 4, attack, defense, false,
                CardEffect.None, 0, "AI position heuristic fixture.");
            definitions.Add(definition);
            return definition;
        }

        private static void SetEngineProperty(DuelEngine engine, string propertyName, object value)
        {
            typeof(DuelEngine).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)
                .GetSetMethod(true).Invoke(engine, new[] { value });
        }
    }
}
