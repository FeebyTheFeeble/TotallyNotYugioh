using System;
using System.Collections.Generic;
using System.Linq;
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
        public void Ai_sets_an_outmatched_monster_even_when_its_defense_is_too_low_to_survive()
        {
            DuelEngine engine = CreateAiEngine();
            CardInstance summon = new CardInstance(CreateMonster("ai-outmatched-summon", 900, 500));
            engine.Opponent.Hand.Clear();
            engine.Opponent.Hand.Add(summon);
            engine.Human.MonsterZones[0] = new CardInstance(CreateMonster("ai-strong-attacker", 1800, 900));

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

        [Test]
        public void Ai_values_clear_effector_draw_and_synchro_protection_in_its_summon_choice()
        {
            DuelEngine engine = CreateAiEngine();
            CardInstance tuner = new CardInstance(StarterCatalog.Find("63977008"));
            CardInstance clearEffector = new CardInstance(StarterCatalog.Find("58518520"));
            CardDefinition synchro = StarterCatalog.Find("60800381");
            engine.Opponent.Hand.Clear();
            engine.Opponent.Deck.Clear();
            engine.Opponent.Deck.Add(new CardInstance(StarterCatalog.Find("23571046")));
            engine.Opponent.MonsterZones[0] = tuner;
            engine.Opponent.MonsterZones[1] = clearEffector;
            engine.Opponent.ExtraDeck.Clear();
            engine.Opponent.ExtraDeck.Add(new CardInstance(synchro));

            for (int step = 0; step < 8 && !engine.Opponent.MonsterZones.Any(card =>
                     card != null && card.Definition == synchro); step++)
                Assert.IsTrue(engine.StepAiTurn());

            CardInstance summoned = Array.Find(engine.Opponent.MonsterZones,
                card => card != null && card.Definition == synchro);
            Assert.IsNotNull(summoned);
            Assert.IsTrue(summoned.CannotBeDestroyedByCardEffects);
            Assert.AreEqual(1, engine.Opponent.Hand.Count);
        }

        [Test]
        public void Ai_values_TG_Hyper_Librarian_draws_when_selecting_a_Synchro_Summon()
        {
            DuelEngine engine = CreateAiEngine();
            CardDefinition librarian = StarterCatalog.Find("90953320");
            CardInstance tuner = new CardInstance(StarterCatalog.Find("67270095"));
            CardInstance nonTuner = new CardInstance(StarterCatalog.Find("49003716"));
            CardDefinition synchro = StarterCatalog.Find("60800381");
            engine.Opponent.Hand.Clear();
            engine.Opponent.Deck.Clear();
            engine.Opponent.Deck.Add(new CardInstance(StarterCatalog.Find("23571046")));
            engine.Opponent.MonsterZones[0] = new CardInstance(librarian);
            engine.Opponent.MonsterZones[1] = tuner;
            engine.Opponent.MonsterZones[2] = nonTuner;
            engine.Opponent.ExtraDeck.Clear();
            engine.Opponent.ExtraDeck.Add(new CardInstance(synchro));

            for (int step = 0; step < 8 && !engine.Opponent.MonsterZones.Any(card =>
                     card != null && card.Definition == synchro); step++)
                Assert.IsTrue(engine.StepAiTurn());

            Assert.IsTrue(engine.Opponent.MonsterZones.Any(card =>
                card != null && card.Definition == synchro));
            Assert.AreEqual(1, engine.Opponent.Hand.Count);
        }

        [Test]
        public void Ai_activates_a_Set_draw_Trap_when_its_simulated_effect_is_profitable()
        {
            DuelEngine engine = CreateAiEngine();
            CardInstance jarOfGreed = new CardInstance(StarterCatalog.Find("83968380"), false);
            engine.Opponent.Hand.Clear();
            engine.Opponent.Deck.Clear();
            engine.Opponent.Deck.Add(new CardInstance(StarterCatalog.Find("23571046")));
            engine.Opponent.SpellTrapZones[0] = jarOfGreed;

            for (int step = 0; step < 8 && !engine.Opponent.Graveyard.Contains(jarOfGreed); step++)
                Assert.IsTrue(engine.StepAiTurn());

            Assert.Contains(jarOfGreed, engine.Opponent.Graveyard);
            Assert.AreEqual(1, engine.Opponent.Hand.Count);
            Assert.IsEmpty(engine.Opponent.Deck);
        }

        [Test]
        public void Ai_uses_Monster_Reborn_on_a_valuable_monster_in_its_Graveyard()
        {
            DuelEngine engine = CreateAiEngine();
            CardInstance monsterReborn = new CardInstance(StarterCatalog.Find("83764719"));
            CardInstance monster = new CardInstance(StarterCatalog.Find("13039848"));
            engine.Opponent.Hand.Clear();
            engine.Opponent.Hand.Add(monsterReborn);
            engine.Opponent.Graveyard.Add(monster);

            for (int step = 0; step < 8 && !engine.Opponent.MonsterZones.Contains(monster); step++)
                Assert.IsTrue(engine.StepAiTurn());

            Assert.Contains(monster, engine.Opponent.MonsterZones);
            Assert.Contains(monsterReborn, engine.Opponent.Graveyard);
            Assert.IsFalse(engine.Opponent.Graveyard.Contains(monster));
        }

        [Test]
        public void Ai_holds_Monster_Reborn_when_a_better_opposing_monster_is_likely_to_be_destroyed_in_battle()
        {
            DuelEngine engine = CreateAiEngine();
            CardInstance monsterReborn = new CardInstance(StarterCatalog.Find("83764719"));
            CardInstance weakGraveyardTarget = new CardInstance(StarterCatalog.Find("13039848"));
            CardInstance battleTarget = new CardInstance(StarterCatalog.Find("44508094"));
            CardInstance attacker = new CardInstance(StarterCatalog.Find("70902743"));
            engine.Opponent.Hand.Clear();
            engine.Opponent.Hand.Add(monsterReborn);
            engine.Opponent.NormalSummonsThisTurn = 1;
            engine.Opponent.Graveyard.Add(weakGraveyardTarget);
            engine.Opponent.MonsterZones[0] = attacker;
            engine.Human.MonsterZones[0] = battleTarget;

            for (int step = 0; step < 12 && !engine.Human.Graveyard.Contains(battleTarget); step++)
                Assert.IsTrue(engine.StepAiTurn());

            Assert.Contains(battleTarget, engine.Human.Graveyard);
            Assert.Contains(monsterReborn, engine.Opponent.Hand);
            Assert.Contains(weakGraveyardTarget, engine.Opponent.Graveyard);
            Assert.IsFalse(engine.Opponent.MonsterZones.Contains(weakGraveyardTarget));
        }

        [Test]
        public void Ai_holds_Monster_Reborn_instead_of_spending_it_on_a_low_value_graveyard_target()
        {
            DuelEngine engine = CreateAiEngine();
            CardInstance monsterReborn = new CardInstance(StarterCatalog.Find("83764719"));
            CardInstance weakTarget = new CardInstance(CreateMonster("ai-weak-reborn-target", 1300, 0));
            engine.Opponent.Hand.Clear();
            engine.Opponent.Hand.Add(monsterReborn);
            engine.Opponent.Graveyard.Add(weakTarget);

            for (int step = 0; step < 8; step++)
                Assert.IsTrue(engine.StepAiTurn());

            Assert.Contains(monsterReborn, engine.Opponent.Hand);
            Assert.Contains(weakTarget, engine.Opponent.Graveyard);
            Assert.IsFalse(engine.Opponent.MonsterZones.Contains(weakTarget));
        }

        [Test]
        public void Ai_spends_Monster_Reborn_to_complete_a_legal_Synchro_line()
        {
            DuelEngine engine = CreateAiEngine();
            CardInstance monsterReborn = new CardInstance(StarterCatalog.Find("83764719"));
            CardInstance junkSynchron = new CardInstance(StarterCatalog.Find("63977008"));
            CardInstance nonTuner = new CardInstance(StarterCatalog.Find("23571046"));
            CardInstance boostMonster = new CardInstance(StarterCatalog.Find("23571046"));
            CardDefinition junkWarrior = StarterCatalog.Find("60800381");
            engine.Opponent.Hand.Clear();
            engine.Opponent.Hand.Add(monsterReborn);
            engine.Opponent.Graveyard.Add(junkSynchron);
            engine.Opponent.MonsterZones[0] = nonTuner;
            engine.Opponent.MonsterZones[1] = boostMonster;
            engine.Opponent.ExtraDeck.Clear();
            engine.Opponent.ExtraDeck.Add(new CardInstance(junkWarrior));

            for (int step = 0; step < 12 && !engine.Opponent.MonsterZones.Any(card =>
                     card != null && card.Definition == junkWarrior); step++)
                Assert.IsTrue(engine.StepAiTurn());

            Assert.IsTrue(engine.Opponent.MonsterZones.Any(card =>
                card != null && card.Definition == junkWarrior));
            Assert.Contains(junkSynchron, engine.Opponent.Graveyard);
            Assert.Contains(nonTuner, engine.Opponent.Graveyard);
            Assert.Contains(monsterReborn, engine.Opponent.Graveyard);
            Assert.Contains(boostMonster, engine.Opponent.MonsterZones);
        }

        [Test]
        public void Attacking_a_stronger_defense_monster_causes_damage_to_the_attacking_player()
        {
            DuelEngine engine = CreateAiEngine();
            SetEngineProperty(engine, nameof(DuelEngine.Phase), DuelPhase.Battle);
            CardInstance attacker = new CardInstance(CreateMonster("ai-test-attacker", 1000, 500));
            CardInstance defender = new CardInstance(CreateMonster("ai-test-defender", 1500, 1500))
            {
                AttackPosition = false
            };
            engine.Opponent.MonsterZones[0] = attacker;
            engine.Human.MonsterZones[0] = defender;

            Assert.IsTrue(engine.TryAttack(attacker, defender));

            Assert.AreEqual(7500, engine.Opponent.LifePoints);
            Assert.AreEqual(8000, engine.Human.LifePoints);
            CollectionAssert.Contains(engine.Opponent.MonsterZones, attacker);
            CollectionAssert.Contains(engine.Human.MonsterZones, defender);
        }

        [Test]
        public void Destroying_a_defense_monster_does_not_cause_battle_damage()
        {
            DuelEngine engine = CreateAiEngine();
            SetEngineProperty(engine, nameof(DuelEngine.Phase), DuelPhase.Battle);
            CardInstance attacker = new CardInstance(CreateMonster("ai-test-strong-attacker", 2000, 500));
            CardInstance defender = new CardInstance(CreateMonster("ai-test-weak-defender", 1000, 500))
            {
                AttackPosition = false
            };
            engine.Opponent.MonsterZones[0] = attacker;
            engine.Human.MonsterZones[0] = defender;

            Assert.IsTrue(engine.TryAttack(attacker, defender));

            Assert.AreEqual(8000, engine.Opponent.LifePoints);
            Assert.AreEqual(8000, engine.Human.LifePoints);
            CollectionAssert.Contains(engine.Opponent.MonsterZones, attacker);
            CollectionAssert.DoesNotContain(engine.Human.MonsterZones, defender);
            Assert.Contains(defender, engine.Human.Graveyard);
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
