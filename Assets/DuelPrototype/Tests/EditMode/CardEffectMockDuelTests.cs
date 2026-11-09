using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace DuelPrototype.Tests
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class CardBehaviorTestAttribute : Attribute
    {
        public string CardId { get; }

        public CardBehaviorTestAttribute(string cardId)
        {
            CardId = cardId;
        }
    }

    public sealed class CardEffectMockDuelTests
    {
        private sealed class CardReportRow
        {
            public string CardId;
            public string Name;
            public string Status;
            public string Details;
            public int BehaviorTestCount;
        }

        private sealed class TestReportRow
        {
            public string Name;
            public string Status;
            public string Details;
            public string[] CardIds;
        }

        private static readonly List<string> DuelResults = new List<string>();
        private static readonly List<TestReportRow> TestResults = new List<TestReportRow>();
        private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private DuelEngine engine;

        [OneTimeSetUp]
        public void ClearReportResults()
        {
            DuelResults.Clear();
            TestResults.Clear();
        }

        [TearDown]
        public void RecordTestResult()
        {
            TestContext testContext = TestContext.CurrentContext;
            TestResults.Add(new TestReportRow
            {
                Name = testContext.Test.Name,
                Status = testContext.Result.Outcome.Status.ToString().ToUpperInvariant(),
                Details = testContext.Result.Message ?? "",
                CardIds = GetCardBehaviorTestIds(testContext.Test.MethodName)
            });
        }

        private string[] GetCardBehaviorTestIds(string methodName)
        {
            MethodInfo method = GetType().GetMethod(methodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return method == null
                ? Array.Empty<string>()
                : method.GetCustomAttributes(typeof(CardBehaviorTestAttribute), true)
                    .Cast<CardBehaviorTestAttribute>()
                    .Select(attribute => attribute.CardId)
                    .ToArray();
        }

        [SetUp]
        public void SetUp()
        {
            engine = new DuelEngine();
            engine.StartMatch(StarterCatalog.CreateStarterDeck(), StarterCatalog.CreateAiDeck());
            SetCurrentPlayer(engine.Human);
            SetPhase(DuelPhase.MainOne);
            ResetPlayer(engine.Human);
            ResetPlayer(engine.Opponent);
            engine.Human.LifePoints = 8000;
            engine.Opponent.LifePoints = 8000;
        }

        [Test]
        public void Phase_change_event_reports_the_started_phase()
        {
            List<DuelPhase> startedPhases = new List<DuelPhase>();
            engine.PhaseChanged += startedPhases.Add;

            engine.AdvancePhase();

            CollectionAssert.AreEqual(new[] { DuelPhase.Battle }, startedPhases);
        }

        [Test]
        public void Hand_visual_positions_match_the_rendered_player_and_opponent_hands()
        {
            const float screenAspect = 16f / 9f;
            Vector2 opponentFirst = DuelHandLayout.GetCardCenter(0, 5, screenAspect, true);
            Vector2 opponentLast = DuelHandLayout.GetCardCenter(4, 5, screenAspect, true);
            Vector2 playerFirst = DuelHandLayout.GetCardCenter(0, 5, screenAspect, false);
            float opponentWidth = DuelHandLayout.GetCardWidth(5, screenAspect, true);
            float playerWidth = DuelHandLayout.GetCardWidth(5, screenAspect, false);
            float opponentHeight = opponentWidth * screenAspect * 7f / 5f;
            float playerHeight = playerWidth * screenAspect * 7f / 5f;

            Assert.AreEqual(DuelHandLayout.OpponentHandBottom + opponentHeight * 0.5f,
                opponentFirst.y, 0.0001f);
            Assert.AreEqual(DuelHandLayout.HumanHandBottom + playerHeight * 0.5f,
                playerFirst.y, 0.0001f);
            Assert.AreEqual(opponentFirst.x + opponentLast.x, 1f, 0.0001f);
        }

        [Test]
        public void Direct_attack_animation_targets_the_opposite_players_side()
        {
            Assert.AreEqual(new Vector2(0.5f, 0.24f),
                DuelAttackLayout.GetDirectAttackTarget(true));
            Assert.AreEqual(new Vector2(0.5f, 0.76f),
                DuelAttackLayout.GetDirectAttackTarget(false));
        }

        [Test]
        public void Starting_player_gets_five_initial_cards_and_one_opening_draw()
        {
            DuelEngine openingEngine = new DuelEngine();
            DuelPlayer drawingPlayer = null;
            CardInstance drawnCard = null;
            openingEngine.CardDrawn += (owner, card) =>
            {
                drawingPlayer = owner;
                drawnCard = card;
            };

            openingEngine.StartMatch(
                StarterCatalog.CreateStarterDeck(),
                StarterCatalog.CreateAiDeck());

            Assert.AreSame(openingEngine.Human, drawingPlayer);
            Assert.AreSame(drawnCard, openingEngine.Human.Hand[5]);
            Assert.AreEqual(6, openingEngine.Human.Hand.Count);
            Assert.AreEqual(5, openingEngine.Opponent.Hand.Count);
        }

        [Test]
        public void Structure_decks_contain_at_least_40_main_deck_cards()
        {
            foreach (StructureDeckPreset deck in StarterCatalog.StructureDecks)
                Assert.GreaterOrEqual(deck.MainDeck.Count, DuelEngine.MinimumMainDeckSize, deck.Name);
        }

        [Test]
        public void Duel_engine_rejects_either_deck_when_it_has_fewer_than_40_cards()
        {
            List<CardDefinition> undersizedDeck = StarterCatalog.CreateStarterDeck()
                .Take(DuelEngine.MinimumMainDeckSize - 1).ToList();
            List<CardDefinition> validDeck = StarterCatalog.CreateAiDeck();

            Assert.Throws<ArgumentException>(() =>
                new DuelEngine().StartMatch(undersizedDeck, validDeck));
            Assert.Throws<ArgumentException>(() =>
                new DuelEngine().StartMatch(validDeck, undersizedDeck));
        }

        [Test]
        public void Opponent_can_be_selected_to_start_the_duel()
        {
            DuelEngine openingEngine = new DuelEngine();
            DuelPlayer drawingPlayer = null;
            openingEngine.CardDrawn += (owner, _) => drawingPlayer = owner;

            openingEngine.StartMatch(
                StarterCatalog.CreateStarterDeck(),
                StarterCatalog.CreateAiDeck(),
                humanGoesFirst: false);

            Assert.AreSame(openingEngine.Opponent, openingEngine.CurrentPlayer);
            Assert.AreSame(openingEngine.Opponent, drawingPlayer);
            Assert.AreEqual(5, openingEngine.Human.Hand.Count);
            Assert.AreEqual(6, openingEngine.Opponent.Hand.Count);
            Assert.IsTrue(openingEngine.HasPendingAiActions);
        }

        [Test]
        public void Human_attack_emits_one_attack_event()
        {
            CardDefinition monsterDefinition = StarterCatalog.All.First(card =>
                card.IsMonster && card.Attack > 0 && card.Effect == CardEffect.None);
            CardInstance attacker = new CardInstance(monsterDefinition);
            CardInstance target = new CardInstance(monsterDefinition);
            engine.Human.MonsterZones[0] = attacker;
            engine.Opponent.MonsterZones[0] = target;
            SetTurnNumber(engine, 2);
            SetPhase(DuelPhase.Battle);
            int eventCount = 0;
            engine.AttackDeclared += (declaredAttacker, declaredTarget) =>
            {
                Assert.AreSame(attacker, declaredAttacker);
                Assert.AreSame(target, declaredTarget);
                eventCount++;
            };

            Assert.IsTrue(engine.TryAttack(attacker, target));
            Assert.AreEqual(1, eventCount);
        }

        [Test]
        public void Ai_attack_emits_the_same_attack_event_as_a_human_attack()
        {
            DuelEngine aiEngine = new DuelEngine();
            aiEngine.StartMatch(StarterCatalog.CreateStarterDeck(), StarterCatalog.CreateAiDeck(), true);
            CardDefinition monsterDefinition = StarterCatalog.All.First(card =>
                card.IsMonster && card.Attack > 0 && card.Effect == CardEffect.None);
            CardInstance attacker = new CardInstance(monsterDefinition);
            aiEngine.Opponent.Hand.Clear();
            aiEngine.Human.Hand.Clear();
            aiEngine.Opponent.MonsterZones[0] = attacker;
            SetEngineProperty(aiEngine, nameof(DuelEngine.CurrentPlayer), aiEngine.Opponent);
            SetEngineProperty(aiEngine, nameof(DuelEngine.Phase), DuelPhase.MainOne);
            SetTurnNumber(aiEngine, 2);
            int eventCount = 0;
            aiEngine.AttackDeclared += (declaredAttacker, target) =>
            {
                Assert.AreSame(attacker, declaredAttacker);
                Assert.IsNull(target);
                eventCount++;
            };

            for (int action = 0; action < 32 && eventCount == 0; action++)
                Assert.IsTrue(aiEngine.StepAiTurn());
            Assert.AreEqual(1, eventCount);
        }

        [Test]
        public void Ai_turn_steps_advance_only_one_phase_command_at_a_time()
        {
            DuelEngine aiEngine = new DuelEngine();
            aiEngine.StartMatch(StarterCatalog.CreateStarterDeck(), StarterCatalog.CreateAiDeck(), true);
            List<DuelPhase> startedPhases = new List<DuelPhase>();
            aiEngine.PhaseChanged += startedPhases.Add;

            Assert.IsTrue(aiEngine.StepAiTurn());

            Assert.AreEqual(DuelPhase.Standby, aiEngine.Phase);
            CollectionAssert.AreEqual(new[] { DuelPhase.Standby }, startedPhases);
            Assert.IsTrue(aiEngine.HasPendingAiActions);

            Assert.IsTrue(aiEngine.StepAiTurn());

            Assert.AreEqual(DuelPhase.MainOne, aiEngine.Phase);
            CollectionAssert.AreEqual(new[] { DuelPhase.Standby, DuelPhase.MainOne }, startedPhases);
        }

        [Test]
        public void Normal_duel_advances_the_opponents_queued_turn_actions()
        {
            SetPhase(DuelPhase.End);
            engine.AdvancePhase();

            Assert.AreSame(engine.Opponent, engine.CurrentPlayer);
            Assert.AreEqual(DuelPhase.Draw, engine.Phase);
            Assert.IsTrue(engine.HasPendingAiActions);
            Assert.IsTrue(engine.StepAiTurn());
            Assert.AreEqual(DuelPhase.Standby, engine.Phase);
        }

        [Test]
        public void Human_can_end_the_turn_from_an_earlier_phase()
        {
            SetPhase(DuelPhase.MainOne);

            Assert.IsTrue(engine.EndHumanTurn());

            Assert.AreSame(engine.Opponent, engine.CurrentPlayer);
            Assert.AreEqual(DuelPhase.Draw, engine.Phase);
            Assert.IsTrue(engine.HasPendingAiActions);
        }

        [Test]
        public void Card_draw_event_reports_the_player_and_drawn_card()
        {
            CardInstance expected = engine.Human.Deck[0];
            DuelPlayer drawnBy = null;
            CardInstance drawnCard = null;
            engine.CardDrawn += (owner, card) =>
            {
                drawnBy = owner;
                drawnCard = card;
            };

            typeof(DuelEngine).GetMethod("DrawCard", PrivateInstance)
                .Invoke(engine, new object[] { engine.Human });

            Assert.AreSame(engine.Human, drawnBy);
            Assert.AreSame(expected, drawnCard);
        }

        [Test]
        public void Duel_state_json_round_trip_preserves_zones_and_card_state()
        {
            CardInstance monster = engine.Human.Deck[0];
            engine.Human.Deck.RemoveAt(0);
            monster.AttackBonus = 400;
            monster.SummonedTurnNumber = 1;
            monster.HasAttacked = true;
            monster.EffectUsedOncePerDuel = true;
            monster.StardustReturnPending = true;
            monster.EffectsNegated = true;
            monster.CannotBeDestroyedByCardEffects = true;
            monster.EffectsNegatedUntilTurn = 2;
            monster.WedgeCounters = 3;
            monster.ExtraAttacksThisTurn = 2;
            monster.LevelModifier = -1;
            monster.DrillWarriorReturnTurn = 3;
            monster.DefenseModifier = -500;
            monster.DrillWarriorDirectAttackThisTurn = true;
            monster.NitroWarriorBoostReady = true;
            monster.DestroyAtEndPhaseTurn = 2;
            monster.BanishWhenLeavingField = true;
            CardInstance nitroTarget = engine.Human.Deck[1];
            engine.Human.Deck.Remove(nitroTarget);
            monster.NitroWarriorSecondAttackTarget = nitroTarget;
            engine.Human.MonsterZones[2] = monster;
            engine.Opponent.MonsterZones[0] = nitroTarget;
            engine.Human.BattleDamageProtectedTurn = 2;
            CardInstance setTrap = new CardInstance(StarterCatalog.Find("44095762"), false);
            engine.Human.SpellTrapZones[4] = setTrap;
            engine.Human.LifePoints = 6200;
            SetPhase(DuelPhase.MainTwo);

            DuelEngine restored = DuelStateJson.Deserialize(DuelStateJson.Serialize(engine));

            Assert.AreEqual(DuelPhase.MainTwo, restored.Phase);
            Assert.AreEqual(engine.TurnNumber, restored.TurnNumber);
            Assert.AreEqual(6200, restored.Human.LifePoints);
            Assert.AreEqual(18, restored.Human.Deck.Count);
            Assert.AreEqual(monster.Definition.CardId, restored.Human.MonsterZones[2].Definition.CardId);
            Assert.AreEqual(400, restored.Human.MonsterZones[2].AttackBonus);
            Assert.IsTrue(restored.Human.MonsterZones[2].HasAttacked);
            Assert.IsTrue(restored.Human.MonsterZones[2].EffectUsedOncePerDuel);
            Assert.IsTrue(restored.Human.MonsterZones[2].StardustReturnPending);
            Assert.IsTrue(restored.Human.MonsterZones[2].EffectsNegated);
            Assert.IsTrue(restored.Human.MonsterZones[2].CannotBeDestroyedByCardEffects);
            Assert.AreEqual(2, restored.Human.MonsterZones[2].EffectsNegatedUntilTurn);
            Assert.AreEqual(3, restored.Human.MonsterZones[2].WedgeCounters);
            Assert.AreEqual(2, restored.Human.MonsterZones[2].ExtraAttacksThisTurn);
            Assert.AreEqual(-1, restored.Human.MonsterZones[2].LevelModifier);
            Assert.AreEqual(3, restored.Human.MonsterZones[2].DrillWarriorReturnTurn);
            Assert.AreEqual(-500, restored.Human.MonsterZones[2].DefenseModifier);
            Assert.IsTrue(restored.Human.MonsterZones[2].DrillWarriorDirectAttackThisTurn);
            Assert.IsTrue(restored.Human.MonsterZones[2].NitroWarriorBoostReady);
            Assert.AreSame(restored.Opponent.MonsterZones[0],
                restored.Human.MonsterZones[2].NitroWarriorSecondAttackTarget);
            Assert.AreEqual(2, restored.Human.MonsterZones[2].DestroyAtEndPhaseTurn);
            Assert.IsTrue(restored.Human.MonsterZones[2].BanishWhenLeavingField);
            Assert.AreEqual(2, restored.Human.BattleDamageProtectedTurn);
            Assert.IsFalse(restored.Human.SpellTrapZones[4].FaceUp);
        }

        [TestCase("starter-main-phase.json", DuelPhase.MainOne)]
        [TestCase("opponent-battle-phase.json", DuelPhase.Battle)]
        public void Example_duel_state_json_files_load(string fileName, DuelPhase expectedPhase)
        {
            string path = Path.Combine(Application.dataPath, "DuelPrototype", "Examples", "DuelStates", fileName);

            DuelEngine loaded = DuelStateJson.LoadFile(path);

            Assert.AreEqual(expectedPhase, loaded.Phase);
            Assert.IsNotNull(loaded.Human);
            Assert.IsNotNull(loaded.Opponent);
        }

        [Test]
        public void Duel_state_json_rejects_unsupported_schema_versions()
        {
            DuelStateSnapshot snapshot = engine.CaptureState();
            snapshot.schemaVersion = 2;

            Assert.Throws<FormatException>(() =>
                DuelStateJson.Deserialize(JsonUtility.ToJson(snapshot)));
        }

        [Test]
        public void Duel_state_json_rejects_cards_referenced_by_multiple_zones()
        {
            DuelStateSnapshot snapshot = engine.CaptureState();
            snapshot.human.hand = new[] { snapshot.human.deck[0] };

            Assert.Throws<FormatException>(() =>
                DuelStateJson.Deserialize(JsonUtility.ToJson(snapshot)));
        }

        [Test]
        public void Csv_catalog_cards_have_unique_ids_and_preserved_card_text()
        {
            IReadOnlyList<CardDefinition> cards = StarterCatalog.All;
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);

            foreach (CardDefinition card in cards)
            {
                bool valid = card != null &&
                    !string.IsNullOrWhiteSpace(card.CardId) &&
                    !string.IsNullOrWhiteSpace(card.DisplayName) &&
                    !string.IsNullOrWhiteSpace(card.FrameType) &&
                    !string.IsNullOrWhiteSpace(card.RulesSummary) &&
                    ids.Add(card.CardId);
                Assert.IsTrue(valid, "Every imported card must have unique, readable source data.");
            }

            Assert.GreaterOrEqual(ids.Count, StarterCatalog.ImportedCardCount,
                "The catalog should contain every CSV record exactly once, in addition to any custom editor cards.");
            Assert.GreaterOrEqual(StarterCatalog.ImportedCardCount, 14000);
            TestContext.WriteLine("Imported CSV cards: " + StarterCatalog.ImportedCardCount);
        }

        [Test]
        public void Cards_default_to_three_deck_copies_and_can_configure_a_lower_limit()
        {
            Assert.IsTrue(StarterCatalog.All.All(card => card.MaxDeckCopies == 3));
            CardDefinition limitedCard = CreateTestCard(
                "limited-copy-test", "Limited Copy Test", CardKind.Monster, 4, maximumDeckCopies: 1);
            try
            {
                Assert.AreEqual(1, limitedCard.MaxDeckCopies);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(limitedCard);
            }
        }

        [Test]
        public void Csv_parser_preserves_escaped_quotes_commas_and_multiline_effect_text()
        {
            const string csv = "id,name,desc,atk,defe,attribute,type,frametype,level,card_sets\r\n" +
                "1,\"A, Test Card\",\"Text with \"\"quoted text\"\"\r\nand another line.\",100,200,LIGHT,Warrior,effect,4,Test Set\r\n";

            List<CardDefinition> imported = CardCatalogCsvImporter.Import(csv);
            try
            {
                Assert.AreEqual(1, imported.Count);
                Assert.AreEqual("A, Test Card", imported[0].DisplayName);
                Assert.AreEqual("Text with \"quoted text\"\r\nand another line.", imported[0].RulesSummary);
                Assert.AreEqual("Test Set", imported[0].CardSets);
            }
            finally
            {
                foreach (CardDefinition card in imported)
                    UnityEngine.Object.DestroyImmediate(card);
            }
        }

        [Test]
        public void Mock_duel_normal_summons_a_CSV_monster()
        {
            CardDefinition definition = StarterCatalog.Find("13039848");
            Assert.IsNotNull(definition);
            Assert.IsTrue(definition.CanNormalSummon);

            CardInstance card = new CardInstance(definition);
            engine.Human.Hand.Add(card);
            Assert.IsTrue(engine.TryNormalSummon(card, Array.Empty<CardInstance>()));
            Assert.Contains(card, engine.Human.MonsterZones);
            DuelResults.Add("PASS | Normal Summon | " + definition.DisplayName + " was summoned from the imported catalog.");
        }

        [Test]
        public void Mock_duel_normal_summon_uses_the_dropped_monster_zone()
        {
            CardInstance card = new CardInstance(StarterCatalog.Find("13039848"));
            engine.Human.Hand.Add(card);

            Assert.IsTrue(engine.TryNormalSummon(card, Array.Empty<CardInstance>(),
                preferredMonsterZone: 3));

            Assert.AreSame(card, engine.Human.MonsterZones[3]);
            Assert.IsNull(engine.Human.MonsterZones[0]);
        }

        [Test]
        public void Mock_duel_sets_a_monster_face_down_in_defense_position()
        {
            CardInstance card = new CardInstance(StarterCatalog.Find("13039848"));
            engine.Human.Hand.Add(card);

            Assert.IsTrue(engine.TryNormalSummon(card, Array.Empty<CardInstance>(), true));

            Assert.Contains(card, engine.Human.MonsterZones);
            Assert.IsFalse(card.FaceUp);
            Assert.IsFalse(card.AttackPosition);
            Assert.AreEqual(1, engine.Human.NormalSummonsThisTurn);
        }

        [Test]
        public void Mock_duel_tribute_summons_a_level_five_monster()
        {
            CardDefinition definition = CreateTestCard("tribute-test", "Tribute Test", CardKind.Monster, 5);
            CardInstance tribute = new CardInstance(StarterCatalog.Find("13039848"));
            CardInstance card = new CardInstance(definition);
            engine.Human.MonsterZones[0] = tribute;
            engine.Human.Hand.Add(card);

            try
            {
                Assert.IsTrue(engine.TryNormalSummon(card, new[] { tribute }));

                Assert.IsNull(engine.Human.MonsterZones[0]);
                Assert.Contains(tribute, engine.Human.Graveyard);
                Assert.Contains(card, engine.Human.MonsterZones);
                Assert.AreEqual(1, engine.Human.NormalSummonsThisTurn);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        [Test]
        public void Mock_duel_tribute_summon_can_replace_a_tribute_in_the_dropped_zone()
        {
            CardDefinition definition = CreateTestCard("tribute-drop-test", "Tribute Drop Test", CardKind.Monster, 5);
            CardInstance tribute = new CardInstance(StarterCatalog.Find("13039848"));
            CardInstance card = new CardInstance(definition);
            engine.Human.MonsterZones[2] = tribute;
            engine.Human.Hand.Add(card);

            try
            {
                Assert.IsTrue(engine.TryNormalSummon(card, new[] { tribute },
                    preferredMonsterZone: 2));

                Assert.AreSame(card, engine.Human.MonsterZones[2]);
                Assert.Contains(tribute, engine.Human.Graveyard);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        [Test]
        public void Mock_duel_ritual_summons_with_a_ritual_spell_and_enough_material_levels()
        {
            CardDefinition ritualDefinition = CreateTestCard(
                "ritual-test", "Ritual Test", CardKind.Monster, 6, "ritual");
            CardDefinition ritualSpellDefinition = CreateTestCard(
                "ritual-spell-test", "Ritual Spell Test", CardKind.Spell, 0,
                cardSubtype: CardSubtype.Ritual);
            CardInstance ritualMonster = new CardInstance(ritualDefinition);
            CardInstance ritualSpell = new CardInstance(ritualSpellDefinition);
            CardInstance handMaterial = new CardInstance(StarterCatalog.Find("13039848"));
            CardInstance fieldMaterial = new CardInstance(StarterCatalog.Find("13039848"));
            engine.Human.Hand.AddRange(new[] { ritualMonster, ritualSpell, handMaterial });
            engine.Human.MonsterZones[0] = fieldMaterial;

            try
            {
                Assert.IsFalse(engine.TryRitualSummon(ritualMonster, new[] { handMaterial }));
                Assert.IsTrue(engine.Human.Hand.Contains(ritualMonster));
                Assert.IsTrue(engine.Human.Hand.Contains(ritualSpell));

                Assert.IsTrue(engine.TryRitualSummon(ritualMonster, new[] { handMaterial, fieldMaterial }));

                Assert.Contains(ritualMonster, engine.Human.MonsterZones);
                Assert.Contains(ritualSpell, engine.Human.Graveyard);
                Assert.Contains(handMaterial, engine.Human.Graveyard);
                Assert.Contains(fieldMaterial, engine.Human.Graveyard);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(ritualDefinition);
                UnityEngine.Object.DestroyImmediate(ritualSpellDefinition);
            }
        }

        [Test]
        public void Mock_duel_synchro_summons_using_imported_tuner_metadata()
        {
            CardDefinition tuner = StarterCatalog.Find("63977008");
            CardDefinition nonTuner = StarterCatalog.Find("23571046");
            CardDefinition synchro = StarterCatalog.Find("60800381");
            Assert.IsNotNull(tuner);
            Assert.IsNotNull(nonTuner);
            Assert.IsNotNull(synchro);
            Assert.IsTrue(tuner.IsTuner);
            Assert.AreEqual(tuner.Level + nonTuner.Level, synchro.Level);

            CardInstance tunerInstance = new CardInstance(tuner);
            CardInstance nonTunerInstance = new CardInstance(nonTuner);
            engine.Human.MonsterZones[0] = tunerInstance;
            engine.Human.MonsterZones[1] = nonTunerInstance;
            engine.Human.ExtraDeck.Clear();
            engine.Human.ExtraDeck.Add(new CardInstance(synchro));

            Assert.IsTrue(engine.TrySynchroSummon(synchro, new[] { tunerInstance, nonTunerInstance }));
            Assert.IsTrue(engine.Human.MonsterZones.Any(card => card != null && card.Definition == synchro));
            DuelResults.Add("PASS | Synchro Summon | Junk Synchron + Quillbolt Hedgehog summoned Junk Warrior.");
        }

        [Test]
        [CardBehaviorTest("5318639")]
        public void Mock_duel_resolves_mystical_space_typhoon_against_a_set_card()
        {
            CardDefinition definition = StarterCatalog.Find("5318639");
            Assert.IsNotNull(definition);
            Assert.AreEqual(CardEffect.DestroySpellTrap, definition.Effect);

            CardInstance card = new CardInstance(definition);
            engine.Human.Hand.Add(card);
            CardDefinition mirrorForce = StarterCatalog.All.First(item => item.DisplayName == "Mirror Force");
            CardInstance target = new CardInstance(mirrorForce, false);
            CardInstance otherSetCard = new CardInstance(mirrorForce, false);
            engine.Opponent.SpellTrapZones[0] = target;
            engine.Opponent.SpellTrapZones[1] = otherSetCard;

            Assert.IsTrue(engine.TryActivate(card, target));
            Assert.IsFalse(engine.Opponent.SpellTrapZones.Contains(target));
            Assert.Contains(target, engine.Opponent.Graveyard);
            Assert.IsTrue(engine.Opponent.SpellTrapZones.Contains(otherSetCard));
            Assert.Contains(card, engine.Human.Graveyard);
            DuelResults.Add("PASS | Mystical Space Typhoon | Destroyed exactly the selected Set card and sent both cards to their owners' Graveyards.");
        }

        [Test]
        public void Tribute_summon_reports_the_summoned_card_and_materials()
        {
            CardDefinition tributeDefinition = CreateTestCard(
                "tribute-summon-test", "Tribute Summon Test", CardKind.Monster, 5);
            try
            {
                CardInstance summon = new CardInstance(tributeDefinition);
                CardInstance tribute = new CardInstance(StarterCatalog.Find("63977008"));
                CardInstance eventCard = null;
                IReadOnlyList<CardInstance> eventTributes = null;
                engine.TributeSummoned += (owner, card, materials) =>
                {
                    Assert.AreSame(engine.Human, owner);
                    eventCard = card;
                    eventTributes = materials;
                };
                engine.Human.Hand.Add(summon);
                engine.Human.MonsterZones[0] = tribute;

                Assert.IsTrue(engine.TryNormalSummon(summon, new[] { tribute }));

                Assert.AreSame(summon, eventCard);
                CollectionAssert.AreEqual(new[] { tribute }, eventTributes);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tributeDefinition);
            }
        }

        [Test]
        [CardBehaviorTest("5318639")]
        public void Mock_duel_does_not_consume_mystical_space_typhoon_for_an_invalid_target()
        {
            CardDefinition definition = StarterCatalog.Find("5318639");
            CardInstance card = new CardInstance(definition);
            CardInstance notOnField = new CardInstance(
                StarterCatalog.All.First(item => item.DisplayName == "Mirror Force"), false);
            engine.Human.Hand.Add(card);

            Assert.IsFalse(engine.TryActivate(card, notOnField));
            Assert.IsTrue(engine.Human.Hand.Contains(card));
            Assert.IsFalse(engine.Human.Graveyard.Contains(card));
            Assert.That(engine.Messages.Last(), Does.Contain("Select a Spell or Trap on the field"));
            DuelResults.Add("PASS | Mystical Space Typhoon target validation | Rejected an off-field target without consuming the Quick-Play Spell.");
        }

        [Test]
        [CardBehaviorTest("14087893")]
        public void Book_of_Moon_sets_a_face_up_monster_to_face_down_defense()
        {
            CardInstance bookOfMoon = new CardInstance(StarterCatalog.Find("14087893"));
            CardInstance target = new CardInstance(StarterCatalog.Find("63977008"));
            engine.Human.Hand.Add(bookOfMoon);
            engine.Opponent.MonsterZones[0] = target;

            Assert.IsTrue(engine.TryActivate(bookOfMoon, target));

            Assert.IsFalse(target.FaceUp);
            Assert.IsFalse(target.AttackPosition);
            Assert.Contains(bookOfMoon, engine.Human.Graveyard);
        }

        [Test]
        [CardBehaviorTest("2009101")]
        public void Blackwing_Gale_halves_a_face_up_opposing_monsters_attack()
        {
            CardInstance gale = new CardInstance(StarterCatalog.Find("2009101"));
            CardInstance target = new CardInstance(StarterCatalog.Find("70095154"));
            engine.Human.MonsterZones[0] = gale;
            engine.Opponent.MonsterZones[0] = target;

            Assert.IsTrue(engine.TryActivate(gale, target));

            Assert.AreEqual(1050, target.CurrentAttack);
            Assert.IsTrue(gale.EffectUsedThisTurn);
        }

        [Test]
        [CardBehaviorTest("75498415")]
        public void Blackwing_Sirocco_boosts_a_selected_Blackwing_by_other_Blackwings_attack()
        {
            CardInstance sirocco = new CardInstance(StarterCatalog.Find("75498415"));
            CardInstance bora = new CardInstance(StarterCatalog.Find("49003716"));
            engine.Human.MonsterZones[0] = sirocco;
            engine.Human.MonsterZones[1] = bora;

            Assert.IsTrue(engine.TryActivate(sirocco, sirocco));

            Assert.AreEqual(3700, sirocco.CurrentAttack);
            Assert.IsTrue(sirocco.EffectUsedThisTurn);
            Assert.IsTrue(bora.HasAttacked);
        }

        [Test]
        [CardBehaviorTest("48381268")]
        public void Morphtronic_Boarden_allows_Morphtronic_direct_attacks_from_defense_position()
        {
            CardInstance boarden = new CardInstance(StarterCatalog.Find("48381268"));
            CardInstance celfon = new CardInstance(StarterCatalog.Find("93542102"));
            boarden.AttackPosition = false;
            engine.Human.MonsterZones[0] = boarden;
            engine.Human.MonsterZones[1] = celfon;
            engine.Opponent.MonsterZones[0] = new CardInstance(StarterCatalog.Find("63977008"));
            SetTurnNumber(engine, 2);
            SetPhase(DuelPhase.Battle);

            Assert.IsTrue(engine.TryAttack(celfon, null));

            Assert.AreEqual(7900, engine.Opponent.LifePoints);
        }

        [Test]
        [CardBehaviorTest("49003716")]
        public void Blackwing_Bora_can_special_summon_while_another_Blackwing_is_controlled()
        {
            CardInstance bora = new CardInstance(StarterCatalog.Find("49003716"));
            engine.Human.Hand.Add(bora);
            engine.Human.MonsterZones[0] = new CardInstance(StarterCatalog.Find("58820853"));

            Assert.IsTrue(engine.TryNormalSummon(bora, null));

            Assert.Contains(bora, engine.Human.MonsterZones);
            Assert.AreEqual(0, engine.Human.NormalSummonsThisTurn);
        }

        [Test]
        [CardBehaviorTest("58820853")]
        public void Blackwing_Shura_summons_a_low_attack_Blackwing_after_battle()
        {
            CardInstance shura = new CardInstance(StarterCatalog.Find("58820853"));
            CardInstance defender = new CardInstance(StarterCatalog.Find("13039848"));
            CardInstance gale = new CardInstance(StarterCatalog.Find("2009101"));
            engine.Human.MonsterZones[0] = shura;
            engine.Opponent.MonsterZones[0] = defender;
            engine.Human.Deck.Clear();
            engine.Human.Deck.Add(gale);
            SetTurnNumber(engine, 2);
            SetPhase(DuelPhase.Battle);

            Assert.IsTrue(engine.TryAttack(shura, defender));

            Assert.Contains(defender, engine.Opponent.Graveyard);
            Assert.Contains(gale, engine.Human.MonsterZones);
            Assert.IsTrue(gale.EffectsNegated);
            Assert.IsFalse(engine.Human.Deck.Contains(gale));
        }

        [Test]
        [CardBehaviorTest("70095154")]
        public void Cyber_Dragon_can_special_summon_if_the_opponent_controls_more_monsters()
        {
            CardInstance cyberDragon = new CardInstance(StarterCatalog.Find("70095154"));
            engine.Human.Hand.Add(cyberDragon);
            engine.Opponent.MonsterZones[0] = new CardInstance(StarterCatalog.Find("63977008"));

            Assert.IsTrue(engine.TryNormalSummon(cyberDragon, null));

            Assert.Contains(cyberDragon, engine.Human.MonsterZones);
            Assert.AreEqual(0, engine.Human.NormalSummonsThisTurn);
        }

        [Test]
        [CardBehaviorTest("87621407")]
        public void Dekoichi_draws_when_flip_summoned()
        {
            CardInstance dekoichi = new CardInstance(StarterCatalog.Find("87621407"), false);
            engine.Human.MonsterZones[0] = dekoichi;
            engine.Human.Deck.Clear();
            engine.Human.Deck.AddRange(new[]
            {
                new CardInstance(StarterCatalog.Find("63977008")),
                new CardInstance(StarterCatalog.Find("23571046"))
            });
            SetTurnNumber(engine, 2);

            Assert.IsTrue(engine.TryFlipSummon(dekoichi));

            Assert.AreEqual(1, engine.Human.Hand.Count);
            Assert.AreEqual(1, engine.Human.Deck.Count);
            Assert.IsTrue(dekoichi.FaceUp);
        }

        [Test]
        [CardBehaviorTest("50091196")]
        [CardBehaviorTest("92676637")]
        public void Formula_Synchron_and_Tuningware_draw_through_the_shared_draw_action()
        {
            CardInstance tuner = new CardInstance(StarterCatalog.Find("67270095"));
            CardInstance tuningware = new CardInstance(StarterCatalog.Find("92676637"));
            CardDefinition formulaSynchron = StarterCatalog.Find("50091196");
            engine.Human.MonsterZones[0] = tuner;
            engine.Human.MonsterZones[1] = tuningware;
            engine.Human.ExtraDeck.Clear();
            engine.Human.ExtraDeck.Add(new CardInstance(formulaSynchron));
            engine.Human.Deck.Clear();
            engine.Human.Deck.AddRange(new[]
            {
                new CardInstance(StarterCatalog.Find("63977008")),
                new CardInstance(StarterCatalog.Find("23571046"))
            });

            Assert.IsTrue(engine.TrySynchroSummon(formulaSynchron, new[] { tuner, tuningware }));

            Assert.AreEqual(2, engine.Human.Hand.Count);
            Assert.IsEmpty(engine.Human.Deck);
        }

        [Test]
        [CardBehaviorTest("58518520")]
        public void Clear_Effector_draws_when_used_as_Synchro_material_and_protects_the_Synchro_from_effect_destruction()
        {
            CardInstance tuner = new CardInstance(StarterCatalog.Find("63977008"));
            CardInstance clearEffector = new CardInstance(StarterCatalog.Find("58518520"));
            CardDefinition junkWarrior = StarterCatalog.Find("60800381");
            CardInstance darkHole = new CardInstance(StarterCatalog.Find("53129443"));
            CardInstance opposingMonster = new CardInstance(StarterCatalog.Find("49003716"));
            engine.Human.MonsterZones[0] = tuner;
            engine.Human.MonsterZones[1] = clearEffector;
            engine.Human.ExtraDeck.Clear();
            engine.Human.ExtraDeck.Add(new CardInstance(junkWarrior));
            engine.Human.Deck.Clear();
            engine.Human.Deck.Add(new CardInstance(StarterCatalog.Find("23571046")));
            engine.Human.Hand.Add(darkHole);
            engine.Opponent.MonsterZones[0] = opposingMonster;

            Assert.IsTrue(engine.TrySynchroSummon(junkWarrior, new[] { tuner, clearEffector }));

            CardInstance summonedSynchro = engine.Human.MonsterZones
                .First(card => card != null && card.Definition == junkWarrior);
            Assert.IsTrue(summonedSynchro.CannotBeDestroyedByCardEffects);
            Assert.Contains(clearEffector, engine.Human.Graveyard);
            Assert.AreEqual(2, engine.Human.Hand.Count);

            Assert.IsTrue(engine.TryActivate(darkHole));

            Assert.AreEqual(1, engine.Human.Hand.Count);
            Assert.Contains(summonedSynchro, engine.Human.MonsterZones);
            Assert.Contains(opposingMonster, engine.Opponent.Graveyard);
        }

        [Test]
        [CardBehaviorTest("90953320")]
        public void TG_Hyper_Librarian_draws_for_each_face_up_copy_when_either_player_Synchro_Summons()
        {
            CardInstance humanLibrarian = new CardInstance(StarterCatalog.Find("90953320"));
            CardInstance opponentLibrarian = new CardInstance(StarterCatalog.Find("90953320"));
            CardInstance tuner = new CardInstance(StarterCatalog.Find("67270095"));
            CardInstance nonTuner = new CardInstance(StarterCatalog.Find("49003716"));
            CardDefinition junkWarrior = StarterCatalog.Find("60800381");
            engine.Human.MonsterZones[0] = humanLibrarian;
            engine.Opponent.MonsterZones[0] = opponentLibrarian;
            engine.Human.MonsterZones[1] = tuner;
            engine.Human.MonsterZones[2] = nonTuner;
            engine.Human.ExtraDeck.Clear();
            engine.Human.ExtraDeck.Add(new CardInstance(junkWarrior));
            engine.Human.Deck.Clear();
            engine.Human.Deck.Add(new CardInstance(StarterCatalog.Find("23571046")));
            engine.Opponent.Deck.Clear();
            engine.Opponent.Deck.Add(new CardInstance(StarterCatalog.Find("63977008")));

            Assert.IsTrue(engine.TrySynchroSummon(junkWarrior, new[] { tuner, nonTuner }));

            Assert.AreEqual(1, engine.Human.Hand.Count);
            Assert.AreEqual(0, engine.Human.Deck.Count);
            Assert.AreEqual(1, engine.Opponent.Hand.Count);
            Assert.AreEqual(0, engine.Opponent.Deck.Count);
        }

        [Test]
        [CardBehaviorTest("11069680")]
        public void Junk_Converter_revives_a_Tuner_when_used_as_Synchro_material()
        {
            CardInstance junkSynchron = new CardInstance(StarterCatalog.Find("63977008"));
            CardInstance junkConverter = new CardInstance(StarterCatalog.Find("11069680"));
            CardDefinition junkWarrior = StarterCatalog.Find("60800381");
            engine.Human.MonsterZones[0] = junkSynchron;
            engine.Human.MonsterZones[1] = junkConverter;
            engine.Human.ExtraDeck.Clear();
            engine.Human.ExtraDeck.Add(new CardInstance(junkWarrior));

            Assert.IsTrue(engine.TrySynchroSummon(junkWarrior, new[] { junkSynchron, junkConverter }));

            Assert.Contains(junkSynchron, engine.Human.MonsterZones);
            Assert.IsFalse(junkSynchron.AttackPosition);
            Assert.IsTrue(junkSynchron.EffectsNegated);
            Assert.Contains(junkConverter, engine.Human.Graveyard);
        }

        [Test]
        [CardBehaviorTest("53855409")]
        public void Doppelwarrior_creates_two_tokens_when_used_as_Synchro_material()
        {
            CardInstance junkSynchron = new CardInstance(StarterCatalog.Find("63977008"));
            CardInstance doppelwarrior = new CardInstance(StarterCatalog.Find("53855409"));
            CardDefinition junkWarrior = StarterCatalog.Find("60800381");
            engine.Human.MonsterZones[0] = junkSynchron;
            engine.Human.MonsterZones[1] = doppelwarrior;
            engine.Human.ExtraDeck.Clear();
            engine.Human.ExtraDeck.Add(new CardInstance(junkWarrior));

            Assert.IsTrue(engine.TrySynchroSummon(junkWarrior, new[] { junkSynchron, doppelwarrior }));

            Assert.AreEqual(2, engine.Human.MonsterZones.Count(card =>
                card != null && card.Definition.CardId == "53855410"));
        }

        [Test]
        [CardBehaviorTest("53855409")]
        public void Doppelwarrior_special_summons_from_hand_after_a_monster_is_revived()
        {
            CardInstance monster = new CardInstance(StarterCatalog.Find("63977008"));
            CardInstance doppelwarrior = new CardInstance(StarterCatalog.Find("53855409"));
            CardInstance monsterReborn = new CardInstance(StarterCatalog.Find("83764719"));
            engine.Human.Graveyard.Add(monster);
            engine.Human.Hand.AddRange(new[] { doppelwarrior, monsterReborn });

            Assert.IsTrue(engine.TryActivate(monsterReborn, monster));

            Assert.Contains(monster, engine.Human.MonsterZones);
            Assert.Contains(doppelwarrior, engine.Human.MonsterZones);
            Assert.Contains(monsterReborn, engine.Human.Graveyard);
        }

        [Test]
        [CardBehaviorTest("73915051")]
        public void Scapegoat_special_summons_up_to_four_tokens_and_locks_summons()
        {
            CardInstance scapegoat = new CardInstance(StarterCatalog.Find("73915051"));
            engine.Human.Hand.Add(scapegoat);

            Assert.IsTrue(engine.TryActivate(scapegoat));

            Assert.AreEqual(4, engine.Human.MonsterZones.Count(card => card != null));
            Assert.IsTrue(engine.Human.SummonsLockedThisTurn);
            Assert.Contains(scapegoat, engine.Human.Graveyard);
            Assert.IsTrue(engine.Human.MonsterZones.Where(card => card != null)
                .All(card => card.Definition.CardId == "73915052" &&
                             card.CannotBeTributedForTributeSummon));
        }

        [Test]
        [CardBehaviorTest("73915051")]
        public void Scapegoat_tokens_cannot_be_Tributed_for_a_Tribute_Summon()
        {
            CardInstance scapegoat = new CardInstance(StarterCatalog.Find("73915051"));
            CardInstance tributeSummon = new CardInstance(CreateTestCard(
                "tribute-forbidden-token-target", "Tribute Summon Test", CardKind.Monster, 5));
            engine.Human.Hand.AddRange(new[] { scapegoat, tributeSummon });
            Assert.IsTrue(engine.TryActivate(scapegoat));
            CardInstance sheepToken = engine.Human.MonsterZones.First(card => card != null);
            engine.Human.SummonsLockedThisTurn = false;
            SetTurnNumber(engine, 3);

            Assert.IsFalse(engine.TryNormalSummon(tributeSummon, new[] { sheepToken }));

            Assert.Contains(sheepToken, engine.Human.MonsterZones);
            Assert.Contains(tributeSummon, engine.Human.Hand);
            Assert.IsFalse(engine.Human.Graveyard.Contains(sheepToken));
        }

        [Test]
        [CardBehaviorTest("73915052")]
        public void Ordinary_tokens_can_be_Tributed_for_a_Tribute_Summon()
        {
            CardDefinition genericToken = StarterCatalog.Find("73915052");
            CardInstance token = new CardInstance(genericToken);
            CardInstance tributeSummon = new CardInstance(CreateTestCard(
                "tribute-token-target", "Tribute Summon Test", CardKind.Monster, 5));
            engine.Human.MonsterZones[0] = token;
            engine.Human.Hand.Add(tributeSummon);

            Assert.IsTrue(engine.TryNormalSummon(tributeSummon, new[] { token }));

            Assert.IsFalse(engine.Human.MonsterZones.Contains(token));
            Assert.IsFalse(engine.Human.Graveyard.Contains(token));
            Assert.Contains(tributeSummon, engine.Human.MonsterZones);
        }

        [Test]
        [CardBehaviorTest("15341821")]
        public void Dandylion_creates_two_tokens_that_can_be_Tributed_after_their_summon_turn()
        {
            CardInstance dandylion = new CardInstance(StarterCatalog.Find("15341821"));
            CardInstance tributeSummon = new CardInstance(StarterCatalog.Find("20932152"));
            CardInstance darkHole = new CardInstance(StarterCatalog.Find("53129443"));
            engine.Human.MonsterZones[0] = dandylion;
            engine.Human.Hand.AddRange(new[] { tributeSummon, darkHole });
            SetTurnNumber(engine, 2);

            Assert.IsTrue(engine.TryActivate(darkHole));

            CardInstance[] fluffTokens = engine.Human.MonsterZones
                .Where(card => card != null && card.Definition.CardId == "15341822").ToArray();
            Assert.AreEqual(2, fluffTokens.Length);
            Assert.IsTrue(fluffTokens.All(card =>
                card.CannotBeTributedForTributeSummonUntilTurn == 2));
            engine = DuelStateJson.Deserialize(DuelStateJson.Serialize(engine));
            fluffTokens = engine.Human.MonsterZones
                .Where(card => card != null && card.Definition.CardId == "15341822").ToArray();
            tributeSummon = engine.Human.Hand.First(card =>
                card.Definition.CardId == "20932152");
            Assert.IsFalse(engine.TryNormalSummon(tributeSummon, new[] { fluffTokens[0] }));

            SetTurnNumber(engine, 3);
            Assert.IsTrue(engine.TryNormalSummon(tributeSummon, new[] { fluffTokens[0] }));

            Assert.Contains(tributeSummon, engine.Human.MonsterZones);
            Assert.IsFalse(engine.Human.MonsterZones.Contains(fluffTokens[0]));
            Assert.IsFalse(engine.Human.Graveyard.Contains(fluffTokens[0]));
        }

        [Test]
        [CardBehaviorTest("55013285")]
        public void Troop_Dragon_summons_a_copy_from_the_Deck_when_destroyed_by_battle()
        {
            CardInstance attacker = new CardInstance(StarterCatalog.Find("13039848"));
            CardInstance troopDragon = new CardInstance(StarterCatalog.Find("55013285"));
            CardInstance copy = new CardInstance(StarterCatalog.Find("55013285"));
            engine.Human.MonsterZones[0] = attacker;
            engine.Opponent.MonsterZones[0] = troopDragon;
            engine.Opponent.Deck.Clear();
            engine.Opponent.Deck.Add(copy);
            SetTurnNumber(engine, 2);
            SetPhase(DuelPhase.Battle);

            Assert.IsTrue(engine.TryAttack(attacker, troopDragon));

            Assert.Contains(troopDragon, engine.Opponent.Graveyard);
            Assert.Contains(copy, engine.Opponent.MonsterZones);
            Assert.IsEmpty(engine.Opponent.Deck);
        }

        [Test]
        [CardBehaviorTest("78010363")]
        public void Witch_of_the_Black_Forest_searches_a_monster_with_1500_or_less_DEF_when_sent_from_the_field()
        {
            CardInstance witch = new CardInstance(StarterCatalog.Find("78010363"));
            CardInstance darkHole = new CardInstance(StarterCatalog.Find("53129443"));
            CardInstance quickdraw = new CardInstance(StarterCatalog.Find("20932152"));
            engine.Human.MonsterZones[0] = witch;
            engine.Human.Hand.AddRange(new[]
            {
                darkHole, new CardInstance(StarterCatalog.Find("13039848"))
            });
            engine.Human.Deck.Clear();
            engine.Human.Deck.Add(quickdraw);

            Assert.IsTrue(engine.TryActivate(darkHole));
            Assert.IsTrue(engine.SearchChoiceOpen);
            Assert.IsTrue(engine.TryChooseSearchCard(quickdraw));

            Assert.Contains(witch, engine.Human.Graveyard);
            Assert.Contains(quickdraw, engine.Human.Hand);
            Assert.IsFalse(engine.TryActivate(quickdraw));
        }

        [Test]
        [CardBehaviorTest("73915051")]
        public void Scapegoat_allows_a_Normal_Set_but_prevents_other_summons_that_turn()
        {
            CardInstance scapegoat = new CardInstance(StarterCatalog.Find("73915051"));
            CardInstance monster = new CardInstance(StarterCatalog.Find("13039848"));
            engine.Human.Hand.AddRange(new[] { scapegoat, monster });
            Assert.IsTrue(engine.TryActivate(scapegoat));

            Assert.IsFalse(engine.TryNormalSummon(monster, Array.Empty<CardInstance>()));
            Assert.IsTrue(engine.TryNormalSummon(monster, Array.Empty<CardInstance>(), true));

            Assert.IsFalse(monster.FaceUp);
            Assert.Contains(monster, engine.Human.MonsterZones);
        }

        [Test]
        [CardBehaviorTest("63995093")]
        public void Machine_Duplication_special_summons_up_to_two_matching_low_attack_machines()
        {
            CardDefinition celfonDefinition = StarterCatalog.Find("93542102");
            CardInstance celfon = new CardInstance(celfonDefinition);
            CardInstance firstCopy = new CardInstance(celfonDefinition);
            CardInstance secondCopy = new CardInstance(celfonDefinition);
            CardInstance machineDuplication = new CardInstance(StarterCatalog.Find("63995093"));
            engine.Human.MonsterZones[0] = celfon;
            engine.Human.Deck.Clear();
            engine.Human.Deck.AddRange(new[] { firstCopy, secondCopy });
            engine.Human.Hand.Add(machineDuplication);

            Assert.IsTrue(engine.TryActivate(machineDuplication, celfon));

            Assert.AreEqual(3, engine.Human.MonsterZones.Count(card =>
                card != null && card.Definition == celfonDefinition));
            Assert.Contains(machineDuplication, engine.Human.Graveyard);
            Assert.IsEmpty(engine.Human.Deck);
        }

        [Test]
        [CardBehaviorTest("53582587")]
        public void Torrential_Tribute_responds_to_a_normal_summon()
        {
            CardInstance torrentialTribute = new CardInstance(StarterCatalog.Find("53582587"), false);
            CardInstance summoned = new CardInstance(StarterCatalog.Find("63977008"));
            engine.Opponent.SpellTrapZones[0] = torrentialTribute;
            engine.Human.Hand.Add(summoned);

            Assert.IsTrue(engine.TryNormalSummon(summoned, null));

            Assert.IsFalse(engine.Human.MonsterZones.Any(card => card != null));
            Assert.IsFalse(engine.Opponent.MonsterZones.Any(card => card != null));
            Assert.Contains(summoned, engine.Human.Graveyard);
            Assert.Contains(torrentialTribute, engine.Opponent.Graveyard);
        }

        [Test]
        public void Ancient_Fairy_Dragon_is_not_mapped_to_Life_Stream_Dragon_effect()
        {
            CardDefinition ancientFairyDragon = StarterCatalog.Find("25862681");
            CardDefinition lifeStreamDragon = StarterCatalog.Find("25165047");

            Assert.IsNotNull(ancientFairyDragon);
            Assert.IsNotNull(lifeStreamDragon);
            Assert.AreEqual(CardEffect.AncientFairyDragon, ancientFairyDragon.Effect);
            Assert.AreEqual(CardEffect.LifeStreamSetLifePoints, lifeStreamDragon.Effect);
        }

        [Test]
        [CardBehaviorTest("25862681")]
        public void Ancient_Fairy_Dragon_draws_after_a_synchro_summon()
        {
            CardDefinition tunerDefinition = StarterCatalog.Find("63977008");
            CardDefinition nonTunerDefinition = StarterCatalog.Find("49003716");
            CardDefinition synchroDefinition = StarterCatalog.Find("25862681");
            CardInstance tuner = new CardInstance(tunerDefinition);
            CardInstance nonTuner = new CardInstance(nonTunerDefinition);
            engine.Human.MonsterZones[0] = tuner;
            engine.Human.MonsterZones[1] = nonTuner;
            engine.Human.ExtraDeck.Clear();
            engine.Human.ExtraDeck.Add(new CardInstance(synchroDefinition));
            int handCount = engine.Human.Hand.Count;

            Assert.IsTrue(engine.TrySynchroSummon(synchroDefinition, new[] { tuner, nonTuner }));

            Assert.AreEqual(handCount + 1, engine.Human.Hand.Count);
            Assert.IsTrue(engine.Human.MonsterZones.Any(card =>
                card != null && card.Definition == synchroDefinition));
        }

        [Test]
        [CardBehaviorTest("21159309")]
        public void Majestic_Dragon_can_only_be_used_for_a_Majestic_Synchro_Summon()
        {
            CardInstance majesticDragon = new CardInstance(StarterCatalog.Find("21159309"));
            CardInstance material = new CardInstance(
                CreateTestCard("majestic-material", "Level 4 Material", CardKind.Monster, 4));
            CardDefinition ordinarySynchro = CreateTestCard(
                "ordinary-synchro", "Ordinary Synchro", CardKind.Synchro, 5);
            CardDefinition majesticSynchro = CreateTestCard(
                "majestic-synchro", "Majestic Test Synchro", CardKind.Synchro, 5);
            engine.Human.MonsterZones[0] = majesticDragon;
            engine.Human.MonsterZones[1] = material;
            engine.Human.ExtraDeck.Add(new CardInstance(ordinarySynchro));
            engine.Human.ExtraDeck.Add(new CardInstance(majesticSynchro));

            Assert.IsFalse(engine.CanSynchroSummon(
                ordinarySynchro, new[] { majesticDragon, material }, engine.Human));
            Assert.IsTrue(engine.CanSynchroSummon(
                majesticSynchro, new[] { majesticDragon, material }, engine.Human));
        }

        [Test]
        public void Road_Synchron_reduces_its_level_for_other_Synchro_Summons()
        {
            CardInstance roadSynchron = new CardInstance(StarterCatalog.Find("71971554"));
            CardInstance material = new CardInstance(
                CreateTestCard("road-material", "Level 4 Material", CardKind.Monster, 4));
            CardDefinition synchro = CreateTestCard(
                "road-synchro", "Level 6 Synchro", CardKind.Synchro, 6);
            engine.Human.MonsterZones[0] = roadSynchron;
            engine.Human.MonsterZones[1] = material;
            engine.Human.ExtraDeck.Add(new CardInstance(synchro));

            Assert.IsTrue(engine.TrySynchroSummon(synchro, new[] { roadSynchron, material }));
            Assert.IsTrue(engine.Human.MonsterZones.Any(card => card != null && card.Definition == synchro));
        }

        [Test]
        [CardBehaviorTest("32807846")]
        public void Reinforcement_of_the_Army_searches_only_a_Warrior_of_level_four_or_less()
        {
            CardInstance reinforcement = new CardInstance(StarterCatalog.Find("32807846"));
            CardInstance eligible = new CardInstance(StarterCatalog.All.First(card =>
                card.IsMonster && card.CanBeMainDeck && card.MonsterType == "Warrior" && card.Level <= 4));
            CardInstance ineligible = new CardInstance(StarterCatalog.All.First(card =>
                card.IsMonster && card.CanBeMainDeck && card.MonsterType != "Warrior" && card.Level <= 4));
            engine.Human.Deck.Clear();
            engine.Human.Deck.AddRange(new[] { ineligible, eligible });
            engine.Human.Hand.Add(reinforcement);

            Assert.IsTrue(engine.TryActivate(reinforcement));
            CollectionAssert.AreEqual(new[] { eligible }, engine.AvailableSearchCards);
            Assert.IsTrue(engine.TryChooseSearchCard(eligible));

            Assert.Contains(eligible, engine.Human.Hand);
            Assert.Contains(ineligible, engine.Human.Deck);
            Assert.Contains(reinforcement, engine.Human.Graveyard);
        }

        [Test]
        [CardBehaviorTest("81439173")]
        public void Foolish_Burial_sends_a_selected_monster_from_the_Deck_to_the_Graveyard()
        {
            CardInstance foolishBurial = new CardInstance(StarterCatalog.Find("81439173"));
            CardInstance monster = new CardInstance(StarterCatalog.Find("13039848"));
            engine.Human.Deck.Clear();
            engine.Human.Deck.Add(monster);
            engine.Human.Hand.Add(foolishBurial);

            Assert.IsTrue(engine.TryActivate(foolishBurial));
            Assert.AreEqual("SEND SELECTED CARD TO GRAVEYARD", engine.SearchChoiceActionLabel);
            Assert.IsTrue(engine.TryChooseSearchCard(monster));

            Assert.Contains(monster, engine.Human.Graveyard);
            Assert.IsFalse(engine.Human.Deck.Contains(monster));
            Assert.Contains(foolishBurial, engine.Human.Graveyard);
        }

        [Test]
        [CardBehaviorTest("55144522")]
        public void Pot_of_Greed_draws_two_cards_from_the_Deck()
        {
            CardInstance potOfGreed = new CardInstance(StarterCatalog.Find("55144522"));
            engine.Human.Deck.Clear();
            engine.Human.Deck.AddRange(Enumerable.Range(0, 2)
                .Select(_ => new CardInstance(StarterCatalog.Find("13039848"))));
            engine.Human.Hand.Add(potOfGreed);

            Assert.IsTrue(engine.TryActivate(potOfGreed));

            Assert.AreEqual(2, engine.Human.Hand.Count);
            Assert.Contains(potOfGreed, engine.Human.Graveyard);
            Assert.AreEqual(0, engine.Human.Deck.Count);
        }

        [Test]
        [CardBehaviorTest("83968380")]
        public void Jar_of_Greed_uses_the_shared_draw_action_when_its_Set_Trap_is_activated()
        {
            CardInstance jarOfGreed = new CardInstance(StarterCatalog.Find("83968380"), false);
            CardInstance drawnCard = new CardInstance(StarterCatalog.Find("13039848"));
            engine.Human.SpellTrapZones[0] = jarOfGreed;
            engine.Human.Deck.Clear();
            engine.Human.Deck.Add(drawnCard);

            Assert.IsTrue(engine.TryActivate(jarOfGreed));

            Assert.Contains(drawnCard, engine.Human.Hand);
            Assert.Contains(jarOfGreed, engine.Human.Graveyard);
            Assert.IsNull(engine.Human.SpellTrapZones[0]);
            Assert.IsEmpty(engine.Human.Deck);
        }

        [Test]
        [CardBehaviorTest("72892473")]
        public void Card_Destruction_discards_the_remaining_hand_then_draws_the_same_count()
        {
            CardInstance cardDestruction = new CardInstance(StarterCatalog.Find("72892473"));
            CardInstance firstDiscard = new CardInstance(StarterCatalog.Find("13039848"));
            CardInstance secondDiscard = new CardInstance(StarterCatalog.Find("13039848"));
            engine.Human.Deck.Clear();
            engine.Human.Deck.AddRange(Enumerable.Range(0, 2)
                .Select(_ => new CardInstance(StarterCatalog.Find("13039848"))));
            CardInstance opponentFirstDiscard = new CardInstance(StarterCatalog.Find("13039848"));
            CardInstance opponentSecondDiscard = new CardInstance(StarterCatalog.Find("13039848"));
            engine.Opponent.Hand.AddRange(new[] { opponentFirstDiscard, opponentSecondDiscard });
            engine.Opponent.Deck.Clear();
            engine.Opponent.Deck.AddRange(Enumerable.Range(0, 2)
                .Select(_ => new CardInstance(StarterCatalog.Find("13039848"))));
            engine.Human.Hand.AddRange(new[] { cardDestruction, firstDiscard, secondDiscard });

            Assert.IsTrue(engine.TryActivate(cardDestruction));

            Assert.AreEqual(2, engine.Human.Hand.Count);
            Assert.Contains(firstDiscard, engine.Human.Graveyard);
            Assert.Contains(secondDiscard, engine.Human.Graveyard);
            Assert.Contains(cardDestruction, engine.Human.Graveyard);
            Assert.AreEqual(0, engine.Human.Deck.Count);
            Assert.AreEqual(2, engine.Opponent.Hand.Count);
            Assert.Contains(opponentFirstDiscard, engine.Opponent.Graveyard);
            Assert.Contains(opponentSecondDiscard, engine.Opponent.Graveyard);
            Assert.AreEqual(0, engine.Opponent.Deck.Count);
        }

        [Test]
        [CardBehaviorTest("79571449")]
        public void Graceful_Charity_draws_three_then_discards_two_other_cards()
        {
            CardInstance gracefulCharity = new CardInstance(StarterCatalog.Find("79571449"));
            CardInstance firstDiscard = new CardInstance(StarterCatalog.Find("13039848"));
            CardInstance secondDiscard = new CardInstance(StarterCatalog.Find("13039848"));
            engine.Human.Deck.Clear();
            engine.Human.Deck.AddRange(Enumerable.Range(0, 3)
                .Select(_ => new CardInstance(StarterCatalog.Find("13039848"))));
            engine.Human.Hand.AddRange(new[] { gracefulCharity, firstDiscard, secondDiscard });

            Assert.IsTrue(engine.TryActivate(gracefulCharity));

            Assert.AreEqual(3, engine.Human.Hand.Count);
            Assert.Contains(firstDiscard, engine.Human.Graveyard);
            Assert.Contains(secondDiscard, engine.Human.Graveyard);
            Assert.Contains(gracefulCharity, engine.Human.Graveyard);
            Assert.AreEqual(0, engine.Human.Deck.Count);
        }

        [Test]
        [CardBehaviorTest("19613556")]
        public void Heavy_Storm_destroys_Spells_and_Traps_on_both_sides()
        {
            CardInstance heavyStorm = new CardInstance(StarterCatalog.Find("19613556"));
            CardInstance ownTrap = new CardInstance(StarterCatalog.Find("83968380"), false);
            CardInstance ownSpell = new CardInstance(StarterCatalog.Find("55144522"));
            CardInstance opponentTrap = new CardInstance(StarterCatalog.Find("83968380"));
            CardInstance opponentSpell = new CardInstance(StarterCatalog.Find("55144522"), false);
            CardInstance ownFieldSpell = new CardInstance(StarterCatalog.Find("50913601"));
            CardInstance opponentFieldSpell = new CardInstance(StarterCatalog.Find("86318356"));
            engine.Human.Hand.Add(heavyStorm);
            engine.Human.SpellTrapZones[0] = ownTrap;
            engine.Human.SpellTrapZones[1] = ownSpell;
            engine.Human.FieldSpellZone = ownFieldSpell;
            engine.Opponent.SpellTrapZones[0] = opponentTrap;
            engine.Opponent.SpellTrapZones[1] = opponentSpell;
            engine.Opponent.FieldSpellZone = opponentFieldSpell;

            Assert.IsTrue(engine.TryActivate(heavyStorm));

            Assert.IsEmpty(engine.Human.SpellTrapZones.Where(card => card != null));
            Assert.IsEmpty(engine.Opponent.SpellTrapZones.Where(card => card != null));
            Assert.IsNull(engine.Human.FieldSpellZone);
            Assert.IsNull(engine.Opponent.FieldSpellZone);
            Assert.Contains(ownTrap, engine.Human.Graveyard);
            Assert.Contains(ownSpell, engine.Human.Graveyard);
            Assert.Contains(ownFieldSpell, engine.Human.Graveyard);
            Assert.Contains(opponentTrap, engine.Opponent.Graveyard);
            Assert.Contains(opponentSpell, engine.Opponent.Graveyard);
            Assert.Contains(opponentFieldSpell, engine.Opponent.Graveyard);
            Assert.Contains(heavyStorm, engine.Human.Graveyard);
        }

        [Test]
        [CardBehaviorTest("18144507")]
        public void Harpies_Feather_Duster_destroys_only_the_opponents_Spells_and_Traps()
        {
            CardInstance featherDuster = new CardInstance(StarterCatalog.Find("18144507"));
            CardInstance ownTrap = new CardInstance(StarterCatalog.Find("83968380"), false);
            CardInstance opponentTrap = new CardInstance(StarterCatalog.Find("83968380"), false);
            CardInstance opponentSpell = new CardInstance(StarterCatalog.Find("55144522"));
            CardInstance ownFieldSpell = new CardInstance(StarterCatalog.Find("50913601"));
            CardInstance opponentFieldSpell = new CardInstance(StarterCatalog.Find("86318356"));
            engine.Human.Hand.Add(featherDuster);
            engine.Human.SpellTrapZones[0] = ownTrap;
            engine.Human.FieldSpellZone = ownFieldSpell;
            engine.Opponent.SpellTrapZones[0] = opponentTrap;
            engine.Opponent.SpellTrapZones[1] = opponentSpell;
            engine.Opponent.FieldSpellZone = opponentFieldSpell;

            Assert.IsTrue(engine.TryActivate(featherDuster));

            Assert.AreSame(ownTrap, engine.Human.SpellTrapZones[0]);
            Assert.AreSame(ownFieldSpell, engine.Human.FieldSpellZone);
            Assert.IsEmpty(engine.Opponent.SpellTrapZones.Where(card => card != null));
            Assert.IsNull(engine.Opponent.FieldSpellZone);
            Assert.IsTrue(engine.Human.Graveyard.Contains(featherDuster));
            Assert.IsTrue(engine.Opponent.Graveyard.Contains(opponentTrap));
            Assert.IsTrue(engine.Opponent.Graveyard.Contains(opponentSpell));
            Assert.IsTrue(engine.Opponent.Graveyard.Contains(opponentFieldSpell));
            Assert.IsFalse(engine.Human.Graveyard.Contains(ownTrap));
            Assert.IsFalse(engine.Human.Graveyard.Contains(ownFieldSpell));
        }

        [Test]
        [CardBehaviorTest("18144507")]
        public void Ai_uses_Harpies_Feather_Duster_when_destroying_four_cards_is_profitable()
        {
            DuelEngine aiEngine = new DuelEngine();
            aiEngine.StartMatch(StarterCatalog.CreateStarterDeck(),
                StarterCatalog.CreateAiDeck(), humanGoesFirst: false);
            CardInstance featherDuster = new CardInstance(StarterCatalog.Find("18144507"));
            aiEngine.Opponent.Hand.Clear();
            aiEngine.Opponent.Hand.Add(featherDuster);
            aiEngine.Opponent.Deck.Clear();
            aiEngine.Human.Hand.Clear();
            for (int zone = 0; zone < aiEngine.Human.SpellTrapZones.Length - 1; zone++)
                aiEngine.Human.SpellTrapZones[zone] =
                    new CardInstance(StarterCatalog.Find("83968380"), false);

            for (int action = 0; action < 32 &&
                 !aiEngine.Opponent.Graveyard.Contains(featherDuster); action++)
                Assert.IsTrue(aiEngine.StepAiTurn());

            Assert.Contains(featherDuster, aiEngine.Opponent.Graveyard);
            Assert.IsEmpty(aiEngine.Human.SpellTrapZones.Where(card => card != null));
        }

        [Test]
        [CardBehaviorTest("69162969")]
        public void Lightning_Vortex_discards_one_card_and_destroys_only_face_up_opponent_monsters()
        {
            CardInstance lightningVortex = new CardInstance(StarterCatalog.Find("69162969"));
            CardInstance discard = new CardInstance(StarterCatalog.Find("13039848"));
            CardInstance faceUpMonster = new CardInstance(StarterCatalog.Find("13039848"));
            CardInstance setMonster = new CardInstance(StarterCatalog.Find("13039848"), false);
            engine.Human.Hand.AddRange(new[] { lightningVortex, discard });
            engine.Opponent.MonsterZones[0] = faceUpMonster;
            engine.Opponent.MonsterZones[1] = setMonster;

            Assert.IsTrue(engine.TryActivate(lightningVortex));

            Assert.Contains(discard, engine.Human.Graveyard);
            Assert.Contains(lightningVortex, engine.Human.Graveyard);
            Assert.Contains(faceUpMonster, engine.Opponent.Graveyard);
            Assert.AreSame(setMonster, engine.Opponent.MonsterZones[1]);
        }

        [Test]
        [CardBehaviorTest("53129443")]
        public void Dark_Hole_destroys_monsters_on_both_sides_of_the_field()
        {
            CardInstance darkHole = new CardInstance(StarterCatalog.Find("53129443"));
            CardInstance friendlyMonster = new CardInstance(StarterCatalog.Find("13039848"));
            CardInstance opponentMonster = new CardInstance(StarterCatalog.Find("13039848"));
            CardInstance setMonster = new CardInstance(StarterCatalog.Find("13039848"), false);
            engine.Human.Hand.Add(darkHole);
            engine.Human.MonsterZones[0] = friendlyMonster;
            engine.Opponent.MonsterZones[0] = opponentMonster;
            engine.Opponent.MonsterZones[1] = setMonster;

            Assert.IsTrue(engine.TryActivate(darkHole));

            Assert.Contains(friendlyMonster, engine.Human.Graveyard);
            Assert.Contains(opponentMonster, engine.Opponent.Graveyard);
            Assert.Contains(setMonster, engine.Opponent.Graveyard);
            Assert.Contains(darkHole, engine.Human.Graveyard);
        }

        [Test]
        [CardBehaviorTest("12580477")]
        public void Raigeki_destroys_all_opponent_monsters_only()
        {
            CardInstance raigeki = new CardInstance(StarterCatalog.Find("12580477"));
            CardInstance friendlyMonster = new CardInstance(StarterCatalog.Find("13039848"));
            CardInstance opponentMonster = new CardInstance(StarterCatalog.Find("13039848"));
            CardInstance setMonster = new CardInstance(StarterCatalog.Find("13039848"), false);
            engine.Human.Hand.Add(raigeki);
            engine.Human.MonsterZones[0] = friendlyMonster;
            engine.Opponent.MonsterZones[0] = opponentMonster;
            engine.Opponent.MonsterZones[1] = setMonster;

            Assert.IsTrue(engine.TryActivate(raigeki));

            Assert.AreSame(friendlyMonster, engine.Human.MonsterZones[0]);
            Assert.Contains(opponentMonster, engine.Opponent.Graveyard);
            Assert.Contains(setMonster, engine.Opponent.Graveyard);
        }

        [Test]
        [CardBehaviorTest("97169186")]
        public void Smashing_Ground_destroys_the_opponent_face_up_monster_with_highest_Defense()
        {
            CardInstance smashingGround = new CardInstance(StarterCatalog.Find("97169186"));
            CardInstance lowerDefense = new CardInstance(StarterCatalog.All
                .Where(card => card.IsMonster && card.CanBeMainDeck)
                .OrderBy(card => card.Defense)
                .First());
            CardInstance highestDefense = new CardInstance(StarterCatalog.All
                .Where(card => card.IsMonster && card.CanBeMainDeck)
                .OrderByDescending(card => card.Defense)
                .First());
            engine.Human.Hand.Add(smashingGround);
            engine.Opponent.MonsterZones[0] = lowerDefense;
            engine.Opponent.MonsterZones[1] = highestDefense;

            Assert.IsTrue(engine.TryActivate(smashingGround));

            Assert.Contains(highestDefense, engine.Opponent.Graveyard);
            Assert.AreSame(lowerDefense, engine.Opponent.MonsterZones[0]);
        }

        [Test]
        [CardBehaviorTest("42703248")]
        public void Giant_Trunade_returns_Spell_and_Trap_cards_to_their_owners_hands()
        {
            CardInstance giantTrunade = new CardInstance(StarterCatalog.Find("42703248"));
            CardInstance playerTrap = new CardInstance(StarterCatalog.All.First(card => card.Kind == CardKind.Trap), false);
            CardInstance opponentSpell = new CardInstance(StarterCatalog.All.First(card => card.Kind == CardKind.Spell), false);
            CardInstance fieldSpell = new CardInstance(StarterCatalog.All.First(card =>
                card.Kind == CardKind.Spell && card.Subtype == CardSubtype.Field), true);
            engine.Human.Hand.Add(giantTrunade);
            engine.Human.SpellTrapZones[0] = playerTrap;
            engine.Opponent.SpellTrapZones[0] = opponentSpell;
            engine.Opponent.FieldSpellZone = fieldSpell;

            Assert.IsTrue(engine.TryActivate(giantTrunade));

            Assert.Contains(playerTrap, engine.Human.Hand);
            Assert.Contains(opponentSpell, engine.Opponent.Hand);
            Assert.Contains(fieldSpell, engine.Opponent.Hand);
            Assert.IsNull(engine.Opponent.FieldSpellZone);
        }

        [Test]
        [CardBehaviorTest("83764719")]
        public void Monster_Reborn_special_summons_a_target_from_either_Graveyard()
        {
            CardInstance monsterReborn = new CardInstance(StarterCatalog.Find("83764719"));
            CardInstance opponentMonster = new CardInstance(StarterCatalog.Find("13039848"));
            engine.Human.Hand.Add(monsterReborn);
            engine.Opponent.Graveyard.Add(opponentMonster);

            Assert.IsTrue(engine.TryActivate(monsterReborn, opponentMonster));

            Assert.Contains(opponentMonster, engine.Human.MonsterZones);
            Assert.IsFalse(engine.Opponent.Graveyard.Contains(opponentMonster));
            Assert.Contains(monsterReborn, engine.Human.Graveyard);
        }

        [Test]
        [CardBehaviorTest("26202165")]
        public void Mock_duel_Sangan_searches_a_legal_monster_and_applies_its_activation_restriction()
        {
            CardDefinition sanganDefinition = StarterCatalog.Find("26202165");
            Assert.IsNotNull(sanganDefinition);
            Assert.AreEqual(CardEffect.SanganSearch, sanganDefinition.Effect);

            CardInstance searchedSangan = new CardInstance(sanganDefinition);
            CardInstance invalidMonster = new CardInstance(StarterCatalog.All.First(card =>
                card.IsMonster && card.CanBeMainDeck && card.Attack > 1500));
            engine.Human.Deck.Clear();
            engine.Human.Deck.Add(searchedSangan);
            engine.Human.Deck.Add(invalidMonster);
            CardInstance fieldSangan = new CardInstance(sanganDefinition);
            engine.Human.MonsterZones[0] = fieldSangan;
            CardDefinition tributeSummonDefinition = StarterCatalog.All.First(card =>
                card.CanNormalSummon && card.Level >= 5);
            CardInstance tributeSummon = new CardInstance(tributeSummonDefinition);
            engine.Human.Hand.Add(tributeSummon);

            Assert.IsTrue(engine.TryNormalSummon(tributeSummon, new[] { fieldSangan }));

            Assert.Contains(fieldSangan, engine.Human.Graveyard);
            Assert.Contains(tributeSummon, engine.Human.MonsterZones);
            Assert.IsTrue(engine.SearchChoiceOpen);
            Assert.AreEqual(1, engine.AvailableSearchCards.Count);
            Assert.AreSame(searchedSangan, engine.AvailableSearchCards[0]);
            Assert.IsFalse(engine.TryChooseSearchCard(invalidMonster));
            Assert.IsTrue(engine.SearchChoiceOpen);
            Assert.IsTrue(engine.TryChooseSearchCard(searchedSangan));
            Assert.IsFalse(engine.SearchChoiceOpen);
            Assert.Contains(searchedSangan, engine.Human.Hand);
            Assert.IsTrue(engine.Human.Deck.Contains(invalidMonster));

            engine.Human.Hand.Remove(searchedSangan);
            engine.Human.MonsterZones[1] = searchedSangan;
            Assert.IsFalse(engine.TryActivate(searchedSangan));
            Assert.That(engine.Messages.Last(), Does.Contain("cannot activate Sangan"));
            DuelResults.Add("PASS | Sangan search | Prompted for a Deck monster at or below 1500 ATK, rejected an illegal target, and blocked that name's activation for the turn.");
        }

        [Test]
        [CardBehaviorTest("26202165")]
        public void Mock_duel_Sangan_can_only_search_once_per_turn()
        {
            CardDefinition sanganDefinition = StarterCatalog.Find("26202165");
            CardInstance firstSangan = new CardInstance(sanganDefinition);
            CardInstance searchChoice = new CardInstance(sanganDefinition);
            CardInstance secondSangan = new CardInstance(sanganDefinition);
            engine.Human.Deck.Clear();
            engine.Human.Deck.Add(searchChoice);
            engine.Human.Deck.Add(secondSangan);
            engine.Human.MonsterZones[0] = firstSangan;

            InvokeSanganSendToGraveyard(engine.Human, firstSangan);
            Assert.IsTrue(engine.SearchChoiceOpen);
            Assert.IsTrue(engine.TryChooseSearchCard(searchChoice));
            Assert.IsTrue(engine.Human.Deck.Remove(secondSangan));
            engine.Human.MonsterZones[0] = secondSangan;
            InvokeSanganSendToGraveyard(engine.Human, secondSangan);

            Assert.IsFalse(engine.SearchChoiceOpen);
            Assert.Contains(secondSangan, engine.Human.Graveyard);
            Assert.IsFalse(engine.Human.Deck.Contains(secondSangan));
            Assert.AreEqual(1, engine.Human.Hand.Count(card => card.Definition == sanganDefinition));

            InvokeBeginTurn(engine.Opponent);
            engine.Human.Graveyard.Remove(secondSangan);
            engine.Human.MonsterZones[0] = secondSangan;
            CardInstance nextTurnChoice = new CardInstance(sanganDefinition);
            engine.Human.Deck.Add(nextTurnChoice);
            InvokeSanganSendToGraveyard(engine.Human, secondSangan);
            Assert.IsTrue(engine.SearchChoiceOpen);
            Assert.IsTrue(engine.TryChooseSearchCard(nextTurnChoice));
            DuelResults.Add("PASS | Sangan once per turn | Blocked a second search during the same turn and reset the once-per-turn limiter on the next turn.");
        }

        [Test]
        [CardBehaviorTest("26202165")]
        public void Mock_duel_AI_Sangan_selects_a_valid_monster_automatically()
        {
            CardDefinition sanganDefinition = StarterCatalog.Find("26202165");
            CardInstance searchChoice = new CardInstance(sanganDefinition);
            CardInstance fieldSangan = new CardInstance(sanganDefinition);
            engine.Opponent.Deck.Clear();
            engine.Opponent.Deck.Add(searchChoice);
            engine.Opponent.MonsterZones[0] = fieldSangan;

            InvokeSanganSendToGraveyard(engine.Opponent, fieldSangan);

            Assert.IsFalse(engine.SearchChoiceOpen);
            Assert.Contains(searchChoice, engine.Opponent.Hand);
            Assert.IsFalse(engine.Opponent.Deck.Contains(searchChoice));
            Assert.Contains(fieldSangan, engine.Opponent.Graveyard);
            DuelResults.Add("PASS | AI Sangan search | The AI selected an eligible Deck monster automatically without stalling its turn.");
        }

        [Test]
        [CardBehaviorTest("26202165")]
        public void Mock_duel_Sangan_does_not_trigger_when_not_sent_from_the_field()
        {
            CardInstance sanganInHand = new CardInstance(StarterCatalog.Find("26202165"));
            engine.Human.Hand.Add(sanganInHand);

            InvokeSanganSendToGraveyard(engine.Human, sanganInHand);

            Assert.IsFalse(engine.SearchChoiceOpen);
            Assert.IsFalse(engine.Human.Graveyard.Contains(sanganInHand));
            Assert.IsFalse(engine.Human.SanganEffectUsedThisTurn);
            DuelResults.Add("PASS | Sangan trigger condition | Sangan did not search when it was not sent from a Monster Zone.");
        }

        [Test]
        public void Mock_duel_simulator_starts_with_selected_card_and_configured_fixtures()
        {
            CardDefinition focus = StarterCatalog.Find("5318639");
            CardDefinition monster = StarterCatalog.Find("13039848");
            CardDefinition trap = StarterCatalog.All.First(card => card.DisplayName == "Mirror Force");
            MockDuelSimulator simulator = MockDuelSimulator.Start(
                StarterCatalog.CreateStarterDeck(),
                StarterCatalog.CreateAiDeck(),
                focus,
                MockDuelFixtures.PlayerMonster | MockDuelFixtures.OpponentMonster |
                MockDuelFixtures.OpponentSetTrap | MockDuelFixtures.PlayerGraveyardMonster,
                monster,
                trap);

            Assert.AreSame(focus, simulator.FocusCard.Definition);
            Assert.Contains(simulator.FocusCard, simulator.Engine.Human.Hand);
            Assert.AreEqual(DuelPhase.MainOne, simulator.Engine.Phase);
            Assert.IsTrue(simulator.Engine.IsHumanActionWindow);
            Assert.AreEqual(1, simulator.Engine.Human.MonsterZones.Count(card => card != null));
            Assert.AreEqual(1, simulator.Engine.Opponent.MonsterZones.Count(card => card != null));
            Assert.AreEqual(trap, simulator.Engine.Opponent.SpellTrapZones[0].Definition);
            Assert.AreEqual(1, simulator.Engine.Human.Graveyard.Count(card => card.Definition == monster));

            simulator.MarkPassed();
            Assert.AreEqual("MANUAL PASS", simulator.ManualResult);
            simulator.MarkFailed();
            Assert.AreEqual("MANUAL FAIL", simulator.ManualResult);
            DuelResults.Add("PASS | Mock duel simulator | Focus card, Main Phase, configurable board fixtures, and manual result marking are available.");
        }

        [Test]
        public void Mock_duel_simulator_provides_Bora_with_another_Blackwing()
        {
            CardDefinition boraDefinition = StarterCatalog.Find("49003716");
            MockDuelSimulator simulator = MockDuelSimulator.Start(
                StarterCatalog.CreateStarterDeck(),
                StarterCatalog.CreateAiDeck(),
                boraDefinition,
                MockDuelFixtures.None,
                StarterCatalog.Find("13039848"),
                StarterCatalog.All.First(card => card.DisplayName == "Mirror Force"));

            Assert.Contains(simulator.FocusCard, simulator.Engine.Human.Hand);
            CardInstance support = simulator.Engine.Human.MonsterZones.FirstOrDefault(card =>
                card != null && card.FaceUp &&
                card.Definition.DisplayName.StartsWith("Blackwing", StringComparison.OrdinalIgnoreCase));
            Assert.IsNotNull(support);
            Assert.IsTrue((simulator.Fixtures & MockDuelFixtures.AutomaticEffectSupport) != 0);
            Assert.IsTrue(simulator.Engine.TryNormalSummon(simulator.FocusCard, null));
            Assert.Contains(simulator.FocusCard, simulator.Engine.Human.MonsterZones);
            DuelResults.Add("PASS | Bora automatic support | The Unit Tests setup placed another Blackwing on the field, allowing Bora to Special Summon.");
        }

        [Test]
        [CardBehaviorTest("73580471")]
        public void Black_Rose_Dragon_destroys_all_field_cards_when_Synchro_Summoned()
        {
            CardInstance tuner = new CardInstance(StarterCatalog.Find("63977008"));
            CardInstance nonTuner = new CardInstance(StarterCatalog.Find("49003716"));
            CardInstance levelOne = new CardInstance(StarterCatalog.Find("68543408"));
            CardInstance opponentMonster = new CardInstance(StarterCatalog.Find("49003716"));
            CardInstance fieldSpell = new CardInstance(CreateTestCard(
                "black-rose-field", "Test Field Spell", CardKind.Spell, 0,
                cardSubtype: CardSubtype.Field));
            CardDefinition blackRose = StarterCatalog.Find("73580471");
            engine.Human.MonsterZones[0] = tuner;
            engine.Human.MonsterZones[1] = nonTuner;
            engine.Human.MonsterZones[2] = levelOne;
            engine.Opponent.MonsterZones[0] = opponentMonster;
            engine.Human.FieldSpellZone = fieldSpell;
            engine.Human.ExtraDeck.Clear();
            engine.Human.ExtraDeck.Add(new CardInstance(blackRose));

            Assert.IsTrue(engine.TrySynchroSummon(blackRose, new[] { tuner, nonTuner }));

            Assert.IsEmpty(engine.Human.MonsterZones.Where(card => card != null));
            Assert.IsEmpty(engine.Opponent.MonsterZones.Where(card => card != null));
            Assert.IsNull(engine.Human.FieldSpellZone);
            Assert.Contains(fieldSpell, engine.Human.Graveyard);
            Assert.Contains(levelOne, engine.Human.Graveyard);
            Assert.Contains(tuner, engine.Human.Graveyard);
        }

        [Test]
        [CardBehaviorTest("91351370")]
        public void Black_Whirlwind_searches_a_lower_ATK_Blackwing_after_a_Normal_Summon()
        {
            CardInstance controlledBlackwing = new CardInstance(StarterCatalog.Find("49003716"));
            CardInstance whirlwind = new CardInstance(StarterCatalog.Find("91351370"));
            CardInstance normalSummon = new CardInstance(StarterCatalog.Find("58820853"));
            CardInstance lowerAttackBlackwing = new CardInstance(StarterCatalog.Find("85215458"));
            engine.Human.MonsterZones[0] = controlledBlackwing;
            engine.Human.Hand.AddRange(new[] { whirlwind, normalSummon });
            engine.Human.Deck.Clear();
            engine.Human.Deck.Add(lowerAttackBlackwing);

            Assert.IsTrue(engine.TryActivate(whirlwind));
            Assert.IsTrue(engine.TryNormalSummon(normalSummon, Array.Empty<CardInstance>()));

            Assert.Contains(whirlwind, engine.Human.SpellTrapZones);
            Assert.Contains(lowerAttackBlackwing, engine.Human.Hand);
            Assert.IsEmpty(engine.Human.Deck);
        }

        [Test]
        [CardBehaviorTest("22835145")]
        public void Blackwing_Blizzard_revives_a_low_Level_Blackwing_in_Defense_Position()
        {
            CardInstance blizzard = new CardInstance(StarterCatalog.Find("22835145"));
            CardInstance graveyardBlackwing = new CardInstance(StarterCatalog.Find("85215458"));
            engine.Human.Graveyard.Add(graveyardBlackwing);
            engine.Human.Hand.Add(blizzard);

            Assert.IsTrue(engine.TryNormalSummon(blizzard, Array.Empty<CardInstance>()));

            Assert.Contains(graveyardBlackwing, engine.Human.MonsterZones);
            Assert.IsFalse(graveyardBlackwing.AttackPosition);
            Assert.Contains(blizzard, engine.Human.MonsterZones);
        }

        [Test]
        [CardBehaviorTest("85215458")]
        public void Blackwing_Kalut_boosts_a_Blackwing_that_has_declared_an_attack()
        {
            CardInstance kalut = new CardInstance(StarterCatalog.Find("85215458"));
            CardInstance blackwing = new CardInstance(StarterCatalog.Find("49003716"))
            {
                HasAttacked = true
            };
            engine.Human.Hand.Add(kalut);
            engine.Human.MonsterZones[0] = blackwing;
            SetTurnNumber(engine, 2);
            SetPhase(DuelPhase.Battle);

            Assert.IsTrue(engine.TryActivate(kalut, blackwing));

            Assert.AreEqual(3100, blackwing.CurrentAttack);
            Assert.Contains(kalut, engine.Human.Graveyard);
        }

        [Test]
        [CardBehaviorTest("46710683")]
        public void Blackwing_Mistral_prevents_the_next_battle_damage_after_it_is_destroyed()
        {
            CardInstance firstAttacker = new CardInstance(StarterCatalog.Find("49003716"));
            CardInstance secondAttacker = new CardInstance(StarterCatalog.Find("75498415"));
            CardInstance mistral = new CardInstance(StarterCatalog.Find("46710683"));
            CardInstance secondTarget = new CardInstance(StarterCatalog.Find("22835145"));
            engine.Human.MonsterZones[0] = firstAttacker;
            engine.Human.MonsterZones[1] = secondAttacker;
            engine.Opponent.MonsterZones[0] = mistral;
            engine.Opponent.MonsterZones[1] = secondTarget;
            SetTurnNumber(engine, 2);
            SetPhase(DuelPhase.Battle);

            Assert.IsTrue(engine.TryAttack(firstAttacker, mistral));
            int lifeAfterFirstBattle = engine.Opponent.LifePoints;
            Assert.IsTrue(engine.TryAttack(secondAttacker, secondTarget));

            Assert.AreEqual(lifeAfterFirstBattle, engine.Opponent.LifePoints);
            Assert.AreEqual(engine.TurnNumber, engine.Opponent.BattleDamageProtectedTurn);
        }

        [Test]
        [CardBehaviorTest("33236860")]
        public void Blackwing_Silverwind_destroys_up_to_two_lower_DEF_face_up_monsters()
        {
            CardDefinition tunerDefinition = CreateTestCard(
                "silverwind-tuner", "Blackwing Test Tuner", CardKind.Monster, 2, isTuner: true);
            CardDefinition firstMaterialDefinition = CreateTestCard(
                "silverwind-nontuner-1", "Level 3 Material 1", CardKind.Monster, 3);
            CardDefinition secondMaterialDefinition = CreateTestCard(
                "silverwind-nontuner-2", "Level 3 Material 2", CardKind.Monster, 3);
            CardInstance tuner = new CardInstance(tunerDefinition);
            CardInstance firstMaterial = new CardInstance(firstMaterialDefinition);
            CardInstance secondMaterial = new CardInstance(secondMaterialDefinition);
            CardInstance firstTarget = new CardInstance(StarterCatalog.Find("93542102"));
            CardInstance secondTarget = new CardInstance(StarterCatalog.Find("93542102"));
            CardDefinition silverwind = StarterCatalog.Find("33236860");
            engine.Human.MonsterZones[0] = tuner;
            engine.Human.MonsterZones[1] = firstMaterial;
            engine.Human.MonsterZones[2] = secondMaterial;
            engine.Human.MonsterZones[3] = firstTarget;
            engine.Opponent.MonsterZones[0] = secondTarget;
            engine.Human.ExtraDeck.Clear();
            engine.Human.ExtraDeck.Add(new CardInstance(silverwind));

            try
            {
                Assert.IsTrue(engine.TrySynchroSummon(
                    silverwind, new[] { tuner, firstMaterial, secondMaterial }));
                Assert.Contains(firstTarget, engine.Human.Graveyard);
                Assert.Contains(secondTarget, engine.Opponent.Graveyard);
                Assert.AreEqual(1, engine.Human.NoAttackTurnsRemaining);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tunerDefinition);
                UnityEngine.Object.DestroyImmediate(firstMaterialDefinition);
                UnityEngine.Object.DestroyImmediate(secondMaterialDefinition);
            }
        }

        [Test]
        [CardBehaviorTest("72714392")]
        public void Blackwing_Vayu_banishes_itself_and_a_Blackwing_to_summon_a_negated_Synchro()
        {
            CardInstance vayu = new CardInstance(StarterCatalog.Find("72714392"));
            CardInstance armorMaster = new CardInstance(StarterCatalog.Find("69031175"));
            CardDefinition silverwind = StarterCatalog.Find("33236860");
            engine.Human.Graveyard.AddRange(new[] { vayu, armorMaster });
            engine.Human.ExtraDeck.Clear();
            engine.Human.ExtraDeck.Add(new CardInstance(silverwind));

            Assert.IsTrue(engine.TryActivate(vayu, armorMaster));

            CardInstance summoned = engine.Human.MonsterZones.First(card =>
                card != null && card.Definition == silverwind);
            Assert.IsTrue(summoned.EffectsNegated);
            Assert.Contains(vayu, engine.Human.Banished);
            Assert.Contains(armorMaster, engine.Human.Banished);
        }

        [Test]
        [CardBehaviorTest("14785765")]
        public void Blackwing_Zephyros_returns_a_face_up_card_and_special_summons_once_per_Duel()
        {
            CardInstance zephyros = new CardInstance(StarterCatalog.Find("14785765"));
            CardInstance cardToReturn = new CardInstance(StarterCatalog.Find("49003716"));
            engine.Human.Graveyard.Add(zephyros);
            engine.Human.MonsterZones[0] = cardToReturn;

            Assert.IsTrue(engine.TryActivate(zephyros, cardToReturn));

            Assert.Contains(zephyros, engine.Human.MonsterZones);
            Assert.Contains(cardToReturn, engine.Human.Hand);
            Assert.AreEqual(7600, engine.Human.LifePoints);
            Assert.IsTrue(zephyros.EffectUsedOncePerDuel);
            Assert.IsFalse(engine.TryActivate(zephyros, zephyros));
        }

        [Test]
        [CardBehaviorTest("76913983")]
        public void Blackwing_Armed_Wing_gains_ATK_and_pierces_Defense_Position()
        {
            CardInstance attacker = new CardInstance(StarterCatalog.Find("76913983"));
            CardInstance defender = new CardInstance(CreateTestCard(
                "armed-wing-target", "Defense Monster", CardKind.Monster, 4,
                attack: 1000, defense: 2300), true, false);
            engine.Human.MonsterZones[0] = attacker;
            engine.Opponent.MonsterZones[0] = defender;
            SetTurnNumber(engine, 2);
            SetPhase(DuelPhase.Battle);

            Assert.IsTrue(engine.TryAttack(attacker, defender));

            Assert.AreEqual(7500, engine.Opponent.LifePoints);
            Assert.Contains(defender, engine.Opponent.Graveyard);
        }

        [Test]
        [CardBehaviorTest("69031175")]
        public void Blackwing_Armor_Master_survives_battle_and_applies_Wedge_Counters()
        {
            CardInstance armorMaster = new CardInstance(StarterCatalog.Find("69031175"));
            CardInstance target = new CardInstance(CreateTestCard(
                "armor-master-target", "High ATK Monster", CardKind.Monster, 4,
                attack: 2800, defense: 3000));
            engine.Human.MonsterZones[0] = armorMaster;
            engine.Opponent.MonsterZones[0] = target;
            SetTurnNumber(engine, 2);
            SetPhase(DuelPhase.Battle);

            Assert.IsTrue(engine.TryAttack(armorMaster, target));

            Assert.Contains(armorMaster, engine.Human.MonsterZones);
            Assert.AreEqual(1, target.WedgeCounters);
            Assert.AreEqual(8000, engine.Human.LifePoints);
            SetPhase(DuelPhase.MainTwo);
            Assert.IsTrue(engine.TryActivate(armorMaster));
            Assert.AreEqual(0, target.WedgeCounters);
            Assert.AreEqual(0, target.CurrentAttack);
            Assert.AreEqual(0, target.Definition.Defense + target.DefenseModifier);
        }

        [Test]
        [CardBehaviorTest("97077563")]
        public void Call_of_the_Haunted_revives_a_monster_in_Attack_Position_and_remains_equipped()
        {
            CardInstance haunted = new CardInstance(StarterCatalog.Find("97077563"), false);
            CardInstance monster = new CardInstance(StarterCatalog.Find("63977008"));
            engine.Human.SpellTrapZones[0] = haunted;
            engine.Human.Graveyard.Add(monster);

            Assert.IsTrue(engine.TryActivate(haunted, monster));

            Assert.AreSame(monster, engine.Human.MonsterZones[0]);
            Assert.IsTrue(monster.AttackPosition);
            Assert.IsTrue(haunted.FaceUp);
            Assert.AreSame(monster, haunted.EquippedMonster);
        }

        [Test]
        [CardBehaviorTest("23693634")]
        public void Colossal_Fighter_gains_ATK_for_Warriors_in_both_Graveyards()
        {
            CardInstance fighter = new CardInstance(StarterCatalog.Find("23693634"));
            CardInstance warriorOne = new CardInstance(CreateTestCard(
                "colossal-warrior-1", "Warrior One", CardKind.Monster, 4, monsterType: "Warrior"));
            CardInstance warriorTwo = new CardInstance(CreateTestCard(
                "colossal-warrior-2", "Warrior Two", CardKind.Monster, 4, monsterType: "Warrior"));
            CardInstance target = new CardInstance(CreateTestCard(
                "colossal-target", "Target", CardKind.Monster, 4, attack: 2950));
            engine.Human.MonsterZones[0] = fighter;
            engine.Opponent.MonsterZones[0] = target;
            engine.Human.Graveyard.Add(warriorOne);
            engine.Opponent.Graveyard.Add(warriorTwo);
            SetTurnNumber(engine, 2);
            SetPhase(DuelPhase.Battle);

            Assert.IsTrue(engine.TryAttack(fighter, target));

            Assert.Contains(target, engine.Opponent.Graveyard);
            Assert.AreEqual(7950, engine.Opponent.LifePoints);
        }

        [Test]
        [CardBehaviorTest("23693634")]
        public void Colossal_Fighter_revives_a_Warrior_when_destroyed_by_battle()
        {
            CardInstance opponentAttacker = new CardInstance(CreateTestCard(
                "colossal-killer", "Strong Monster", CardKind.Monster, 4, attack: 3500));
            CardInstance fighter = new CardInstance(StarterCatalog.Find("23693634"));
            CardInstance warrior = new CardInstance(CreateTestCard(
                "colossal-revive-target", "Warrior to Revive", CardKind.Monster, 4,
                attack: 1200, monsterType: "Warrior"));
            engine.Human.MonsterZones[0] = fighter;
            engine.Human.Graveyard.Add(warrior);
            engine.Opponent.MonsterZones[0] = opponentAttacker;
            SetTurnNumber(engine, 2);
            SetPhase(DuelPhase.Battle);

            Assert.IsTrue(engine.TryAttack(fighter, opponentAttacker));

            Assert.Contains(fighter, engine.Human.Graveyard);
            Assert.Contains(warrior, engine.Human.MonsterZones);
        }

        [Test]
        [CardBehaviorTest("14943837")]
        public void Debris_Dragon_revives_and_negates_a_low_ATK_monster_on_Normal_Summon()
        {
            CardInstance debris = new CardInstance(StarterCatalog.Find("14943837"));
            CardInstance target = new CardInstance(StarterCatalog.Find("93542102"));
            engine.Human.Graveyard.Add(target);
            engine.Human.Hand.Add(debris);

            Assert.IsTrue(engine.TryNormalSummon(debris, Array.Empty<CardInstance>()));

            Assert.Contains(target, engine.Human.MonsterZones);
            Assert.IsTrue(target.EffectsNegated);
            Assert.IsTrue(target.AttackPosition);
        }

        [Test]
        [CardBehaviorTest("59839761")]
        public void Delta_Crow_destroys_all_Set_opposing_Spells_and_Traps_with_a_Blackwing()
        {
            CardInstance blackwing = new CardInstance(StarterCatalog.Find("49003716"));
            CardInstance deltaCrow = new CardInstance(StarterCatalog.Find("59839761"), false);
            CardInstance firstSet = new CardInstance(StarterCatalog.Find("44095762"), false);
            CardInstance secondSet = new CardInstance(StarterCatalog.Find("56120475"), false);
            engine.Human.MonsterZones[0] = blackwing;
            engine.Human.SpellTrapZones[0] = deltaCrow;
            engine.Opponent.SpellTrapZones[0] = firstSet;
            engine.Opponent.SpellTrapZones[1] = secondSet;

            Assert.IsTrue(engine.TryActivate(deltaCrow));

            Assert.Contains(firstSet, engine.Opponent.Graveyard);
            Assert.Contains(secondSet, engine.Opponent.Graveyard);
            Assert.Contains(deltaCrow, engine.Human.Graveyard);
        }

        [Test]
        [CardBehaviorTest("3429238")]
        public void Drill_Warrior_halves_its_ATK_and_can_attack_directly()
        {
            CardInstance drill = new CardInstance(StarterCatalog.Find("3429238"));
            CardInstance opposingMonster = new CardInstance(StarterCatalog.Find("49003716"));
            engine.Human.MonsterZones[0] = drill;
            engine.Opponent.MonsterZones[0] = opposingMonster;
            SetTurnNumber(engine, 2);

            Assert.IsTrue(engine.TryActivate(drill));
            SetPhase(DuelPhase.Battle);
            Assert.AreEqual(1200, drill.CurrentAttack);
            Assert.IsTrue(engine.TryAttack(drill, null));

            Assert.AreEqual(6800, engine.Opponent.LifePoints);
        }

        [Test]
        [CardBehaviorTest("3429238")]
        public void Drill_Warrior_returns_on_the_next_Standby_Phase_and_recovers_a_monster()
        {
            CardInstance drill = new CardInstance(StarterCatalog.Find("3429238"));
            CardInstance discard = new CardInstance(StarterCatalog.Find("49003716"));
            CardInstance recovered = new CardInstance(StarterCatalog.Find("93542102"));
            engine.Human.MonsterZones[0] = drill;
            engine.Human.Hand.Add(discard);
            engine.Human.Graveyard.Add(recovered);
            SetTurnNumber(engine, 2);

            Assert.IsTrue(engine.TryActivate(drill, discard));
            SetTurnNumber(engine, 4);
            SetPhase(DuelPhase.Draw);
            engine.AdvancePhase();

            Assert.Contains(drill, engine.Human.MonsterZones);
            Assert.Contains(recovered, engine.Human.Hand);
            Assert.IsFalse(engine.Human.Banished.Contains(drill));
        }

        [Test]
        [CardBehaviorTest("7391448")]
        public void Goyo_Guardian_takes_control_of_a_monster_it_destroys_by_battle()
        {
            CardInstance goyo = new CardInstance(StarterCatalog.Find("7391448"));
            CardInstance target = new CardInstance(StarterCatalog.Find("93542102"));
            engine.Human.MonsterZones[0] = goyo;
            engine.Opponent.MonsterZones[0] = target;
            SetTurnNumber(engine, 2);
            SetPhase(DuelPhase.Battle);

            Assert.IsTrue(engine.TryAttack(goyo, target));

            Assert.Contains(target, engine.Human.MonsterZones);
            Assert.IsFalse(engine.Opponent.Graveyard.Contains(target));
            Assert.IsFalse(target.AttackPosition);
        }

        [Test]
        [CardBehaviorTest("53567095")]
        public void Icarus_Attack_tributes_a_Winged_Beast_and_destroys_two_field_cards()
        {
            CardInstance icarus = new CardInstance(StarterCatalog.Find("53567095"), false);
            CardInstance wingedBeast = new CardInstance(StarterCatalog.Find("49003716"));
            CardInstance opponentMonster = new CardInstance(StarterCatalog.Find("93542102"));
            CardInstance opponentSet = new CardInstance(StarterCatalog.Find("56120475"), false);
            engine.Human.SpellTrapZones[0] = icarus;
            engine.Human.MonsterZones[0] = wingedBeast;
            engine.Opponent.MonsterZones[0] = opponentMonster;
            engine.Opponent.SpellTrapZones[0] = opponentSet;

            Assert.IsTrue(engine.TryActivate(icarus));

            Assert.Contains(wingedBeast, engine.Human.Graveyard);
            Assert.Contains(opponentMonster, engine.Opponent.Graveyard);
            Assert.Contains(opponentSet, engine.Opponent.Graveyard);
        }

        [Test]
        [CardBehaviorTest("77585513")]
        public void Jinzo_prevents_Set_Trap_Cards_from_being_activated()
        {
            CardInstance jinzo = new CardInstance(StarterCatalog.Find("77585513"));
            CardInstance setTrap = new CardInstance(StarterCatalog.Find("44095762"), false);
            engine.Human.MonsterZones[0] = jinzo;
            engine.Human.SpellTrapZones[0] = setTrap;

            Assert.IsTrue(engine.IsTrapActivationBlockedByJinzo(setTrap));
            Assert.IsFalse(engine.TryActivate(setTrap));

            Assert.IsFalse(setTrap.FaceUp);
            Assert.AreSame(setTrap, engine.Human.SpellTrapZones[0]);
        }

        [Test]
        [CardBehaviorTest("34124316")]
        public void Cyber_Jar_destroys_monsters_and_processes_up_to_five_cards_from_each_deck()
        {
            CardInstance jar = new CardInstance(StarterCatalog.Find("34124316"), false);
            CardInstance opponentMonster = new CardInstance(StarterCatalog.Find("93542102"));
            CardDefinition lowMonster = CreateTestCard(
                "cyber-jar-low", "Level Four Monster", CardKind.Monster, 4);
            CardDefinition highMonster = CreateTestCard(
                "cyber-jar-high", "Level Five Monster", CardKind.Monster, 5);
            CardDefinition spell = CreateTestCard(
                "cyber-jar-spell", "Test Spell", CardKind.Spell, 0);
            CardInstance humanLow = new CardInstance(lowMonster);
            CardInstance humanHigh = new CardInstance(highMonster);
            CardInstance humanSpell = new CardInstance(spell);
            CardInstance humanExtraMonster = new CardInstance(lowMonster);
            CardInstance humanExtraSpell = new CardInstance(spell);
            CardInstance humanDeckRemainder = new CardInstance(highMonster);
            CardInstance opponentLow = new CardInstance(lowMonster);
            CardInstance opponentSpell = new CardInstance(spell);
            CardInstance opponentExtraMonster = new CardInstance(lowMonster);
            CardInstance opponentExtraSpell = new CardInstance(spell);
            CardInstance opponentExtraHigh = new CardInstance(highMonster);
            CardInstance opponentDeckRemainder = new CardInstance(highMonster);
            engine.Human.MonsterZones[0] = jar;
            engine.Opponent.MonsterZones[0] = opponentMonster;
            engine.Human.Deck.Clear();
            engine.Human.Deck.AddRange(new[]
            {
                humanLow, humanHigh, humanSpell, humanExtraMonster, humanExtraSpell, humanDeckRemainder
            });
            engine.Opponent.Deck.Clear();
            engine.Opponent.Deck.AddRange(new[]
            {
                opponentLow, opponentSpell, opponentExtraMonster, opponentExtraSpell, opponentExtraHigh,
                opponentDeckRemainder
            });
            SetTurnNumber(engine, 2);

            Assert.IsTrue(engine.TryFlipSummon(jar));

            Assert.Contains(jar, engine.Human.Graveyard);
            Assert.Contains(opponentMonster, engine.Opponent.Graveyard);
            CollectionAssert.Contains(engine.Human.MonsterZones, humanLow);
            CollectionAssert.Contains(engine.Opponent.MonsterZones, opponentLow);
            Assert.Contains(humanHigh, engine.Human.Hand);
            Assert.Contains(humanSpell, engine.Human.Hand);
            Assert.Contains(opponentSpell, engine.Opponent.Hand);
            Assert.Contains(humanExtraMonster, engine.Human.MonsterZones);
            Assert.Contains(humanExtraSpell, engine.Human.Hand);
            Assert.Contains(opponentExtraMonster, engine.Opponent.MonsterZones);
            Assert.Contains(opponentExtraSpell, engine.Opponent.Hand);
            Assert.Contains(opponentExtraHigh, engine.Opponent.Hand);
            CollectionAssert.AreEqual(new[] { humanDeckRemainder }, engine.Human.Deck);
            CollectionAssert.AreEqual(new[] { opponentDeckRemainder }, engine.Opponent.Deck);
        }

        [Test]
        [CardBehaviorTest("34124316")]
        public void Cyber_Jar_flip_effect_resolves_during_battle_before_damage()
        {
            CardInstance attacker = new CardInstance(StarterCatalog.Find("13039848"));
            CardInstance jar = new CardInstance(StarterCatalog.Find("34124316"), false);
            CardInstance humanReveal = new CardInstance(StarterCatalog.Find("93542102"));
            CardInstance opponentReveal = new CardInstance(StarterCatalog.Find("93542102"));
            engine.Human.MonsterZones[0] = attacker;
            engine.Opponent.MonsterZones[0] = jar;
            engine.Human.Deck.Clear();
            engine.Human.Deck.Add(humanReveal);
            engine.Opponent.Deck.Clear();
            engine.Opponent.Deck.Add(opponentReveal);
            SetTurnNumber(engine, 2);
            SetPhase(DuelPhase.Battle);

            Assert.IsTrue(engine.TryAttack(attacker, jar));

            Assert.Contains(attacker, engine.Human.Graveyard);
            Assert.Contains(jar, engine.Opponent.Graveyard);
            Assert.Contains(humanReveal, engine.Human.MonsterZones);
            Assert.Contains(opponentReveal, engine.Opponent.MonsterZones);
            Assert.AreEqual(8000, engine.Human.LifePoints);
            Assert.AreEqual(8000, engine.Opponent.LifePoints);
        }

        [Test]
        [CardBehaviorTest("71044499")]
        public void Nobleman_of_Crossout_banishes_target_and_matching_Flip_monsters_in_both_decks()
        {
            CardDefinition flipMonster = ScriptableObject.CreateInstance<CardDefinition>();
            flipMonster.Configure("nobleman-flip", "Test Flip Monster", CardKind.Monster, 2,
                500, 500, false, CardEffect.None, 0, "FLIP: Test effect.");
            CardInstance setMonster = new CardInstance(flipMonster, false);
            CardInstance humanCopy = new CardInstance(flipMonster);
            CardInstance opponentCopy = new CardInstance(flipMonster);
            CardInstance nobleman = new CardInstance(StarterCatalog.Find("71044499"));
            engine.Opponent.MonsterZones[0] = setMonster;
            engine.Human.Deck.Clear();
            engine.Human.Deck.Add(humanCopy);
            engine.Opponent.Deck.Clear();
            engine.Opponent.Deck.Add(opponentCopy);
            engine.Human.Hand.Add(nobleman);

            Assert.IsTrue(engine.TryActivate(nobleman, setMonster));

            Assert.Contains(setMonster, engine.Opponent.Banished);
            Assert.Contains(humanCopy, engine.Human.Banished);
            Assert.Contains(opponentCopy, engine.Opponent.Banished);
            Assert.IsEmpty(engine.Human.Deck);
            Assert.IsEmpty(engine.Opponent.Deck);
        }

        [Test]
        [CardBehaviorTest("77585513")]
        public void Jinzo_does_not_block_Trap_activation_when_its_effect_is_negated()
        {
            CardInstance jinzo = new CardInstance(StarterCatalog.Find("77585513"))
            {
                EffectsNegated = true
            };
            CardInstance setTrap = new CardInstance(StarterCatalog.Find("44095762"), false);
            engine.Opponent.MonsterZones[0] = jinzo;

            Assert.IsFalse(engine.IsTrapActivationBlockedByJinzo(setTrap));
        }

        [Test]
        [CardBehaviorTest("63977008")]
        public void Junk_Synchron_revives_a_Level_two_or_lower_monster_on_Normal_Summon()
        {
            CardInstance junkSynchron = new CardInstance(StarterCatalog.Find("63977008"));
            CardInstance target = new CardInstance(StarterCatalog.Find("53855409"));
            engine.Human.Graveyard.Add(target);
            engine.Human.Hand.Add(junkSynchron);

            Assert.IsTrue(engine.TryNormalSummon(junkSynchron, Array.Empty<CardInstance>()));

            Assert.Contains(target, engine.Human.MonsterZones);
            Assert.IsFalse(target.AttackPosition);
            Assert.IsTrue(target.EffectsNegated);
        }

        [Test]
        [CardBehaviorTest("60800381")]
        public void Junk_Warrior_gains_ATK_from_other_low_Level_monsters_after_Synchro_Summoning()
        {
            CardInstance tuner = new CardInstance(StarterCatalog.Find("63977008"));
            CardInstance material = new CardInstance(StarterCatalog.Find("53855409"));
            CardInstance lowLevelMonster = new CardInstance(StarterCatalog.Find("93542102"));
            CardDefinition junkWarrior = StarterCatalog.Find("60800381");
            engine.Human.MonsterZones[0] = tuner;
            engine.Human.MonsterZones[1] = material;
            engine.Human.MonsterZones[2] = lowLevelMonster;
            engine.Human.ExtraDeck.Clear();
            engine.Human.ExtraDeck.Add(new CardInstance(junkWarrior));

            Assert.IsTrue(engine.TrySynchroSummon(junkWarrior, new[] { tuner, material }));

            Assert.AreEqual(2400, engine.Human.MonsterZones
                .First(card => card != null && card.Definition == junkWarrior).CurrentAttack);
        }

        [Test]
        [CardBehaviorTest("57421866")]
        public void Level_Eater_reduces_a_high_Level_monster_and_special_summons_itself()
        {
            CardInstance eater = new CardInstance(StarterCatalog.Find("57421866"));
            CardInstance highLevelMonster = new CardInstance(StarterCatalog.Find("70902743"));
            engine.Human.Graveyard.Add(eater);
            engine.Human.MonsterZones[0] = highLevelMonster;

            Assert.IsTrue(engine.TryActivate(eater, highLevelMonster));

            Assert.Contains(eater, engine.Human.MonsterZones);
            Assert.AreEqual(-1, highLevelMonster.LevelModifier);
        }

        [Test]
        [CardBehaviorTest("25165047")]
        [CardBehaviorTest("14785765")]
        public void Life_Stream_Dragon_sets_Life_Points_and_prevents_Zephyros_effect_damage()
        {
            CardDefinition tuner = CreateTestCard(
                "life-stream-tuner", "Level 1 Tuner", CardKind.Monster, 1, isTuner: true);
            CardDefinition material = StarterCatalog.Find("2403771");
            CardDefinition lifeStream = StarterCatalog.Find("25165047");
            CardInstance tunerInstance = new CardInstance(tuner);
            CardInstance materialInstance = new CardInstance(material);
            CardInstance zephyros = new CardInstance(StarterCatalog.Find("14785765"));
            CardInstance fieldSpell = new CardInstance(CreateTestCard(
                "life-stream-protection-target", "Test Field Spell", CardKind.Spell, 0,
                cardSubtype: CardSubtype.Field));
            engine.Human.MonsterZones[0] = tunerInstance;
            engine.Human.MonsterZones[1] = materialInstance;
            engine.Human.Graveyard.Add(zephyros);
            engine.Human.FieldSpellZone = fieldSpell;
            engine.Human.LifePoints = 1200;
            engine.Human.ExtraDeck.Clear();
            engine.Human.ExtraDeck.Add(new CardInstance(lifeStream));

            Assert.IsTrue(engine.TrySynchroSummon(lifeStream, new[] { tunerInstance, materialInstance }));
            Assert.AreEqual(4000, engine.Human.LifePoints);
            Assert.IsTrue(engine.Human.MonsterZones.Any(card =>
                card != null && card.Definition == lifeStream));
            Assert.IsTrue(engine.TryActivate(zephyros, fieldSpell));
            Assert.Contains(fieldSpell, engine.Human.Hand);
            Assert.AreEqual(4000, engine.Human.LifePoints);
            UnityEngine.Object.DestroyImmediate(tuner);
        }

        [Test]
        [CardBehaviorTest("23171610")]
        public void Limiter_Removal_doubles_Machine_ATK_then_destroys_the_boosted_monster()
        {
            CardInstance limiter = new CardInstance(StarterCatalog.Find("23171610"));
            CardInstance machine = new CardInstance(StarterCatalog.Find("93542102"));
            engine.Human.Hand.Add(limiter);
            engine.Human.MonsterZones[0] = machine;

            Assert.IsTrue(engine.TryActivate(limiter));
            Assert.AreEqual(machine.Definition.Attack * 2, machine.CurrentAttack);
            typeof(DuelEngine).GetMethod("EndTurn", PrivateInstance)
                .Invoke(engine, new object[] { engine.Human });

            Assert.IsNull(engine.Human.MonsterZones[0]);
            Assert.Contains(machine, engine.Human.Graveyard);
        }

        [Test]
        [CardBehaviorTest("7841112")]
        public void Majestic_Star_Dragon_negates_an_opposing_monster_and_returns_to_the_Extra_Deck()
        {
            CardInstance majesticStar = new CardInstance(StarterCatalog.Find("7841112"));
            CardInstance stardust = new CardInstance(StarterCatalog.Find("44508094"));
            CardInstance opposingMonster = new CardInstance(StarterCatalog.Find("49003716"));
            engine.Human.MonsterZones[0] = majesticStar;
            engine.Human.Graveyard.Add(stardust);
            engine.Opponent.MonsterZones[0] = opposingMonster;

            Assert.IsTrue(engine.TryActivate(majesticStar, opposingMonster));
            Assert.IsTrue(opposingMonster.EffectsNegated);
            typeof(DuelEngine).GetMethod("EndTurn", PrivateInstance)
                .Invoke(engine, new object[] { engine.Human });

            Assert.IsTrue(engine.Human.ExtraDeck.Any(card => card.Definition == majesticStar.Definition));
            Assert.Contains(stardust, engine.Human.MonsterZones);
        }

        [Test]
        [CardBehaviorTest("44095762")]
        public void Mirror_Force_destroys_all_attacking_monsters_when_the_opponent_declares_an_attack()
        {
            CardInstance mirrorForce = new CardInstance(StarterCatalog.Find("44095762"), false);
            CardInstance attacker = new CardInstance(StarterCatalog.Find("49003716"));
            CardInstance secondAttacker = new CardInstance(StarterCatalog.Find("75498415"));
            CardInstance target = new CardInstance(StarterCatalog.Find("93542102"));
            engine.Opponent.SpellTrapZones[0] = mirrorForce;
            engine.Human.MonsterZones[0] = attacker;
            engine.Human.MonsterZones[1] = secondAttacker;
            engine.Opponent.MonsterZones[0] = target;
            SetTurnNumber(engine, 2);
            SetPhase(DuelPhase.Battle);

            Assert.IsTrue(engine.TryAttack(attacker, target));

            Assert.Contains(attacker, engine.Human.Graveyard);
            Assert.Contains(secondAttacker, engine.Human.Graveyard);
            Assert.Contains(mirrorForce, engine.Opponent.Graveyard);
        }

        [Test]
        [CardBehaviorTest("93542102")]
        public void Morphtronic_Celfon_summons_a_low_Level_Morphtronic_from_the_revealed_cards()
        {
            CardInstance celfon = new CardInstance(StarterCatalog.Find("93542102"));
            engine.Human.MonsterZones[0] = celfon;
            engine.Human.Deck.Clear();
            CardDefinition morphtronic = StarterCatalog.Find("57108202");
            engine.Human.Deck.AddRange(Enumerable.Range(0, 6)
                .Select(_ => new CardInstance(morphtronic)));

            Assert.IsTrue(engine.TryActivate(celfon));

            Assert.AreEqual(1, engine.Human.MonsterZones.Count(card =>
                card != null && card.Definition.Effect == CardEffect.MorphtronicRemoten));
            Assert.AreEqual(5, engine.Human.Deck.Count);
        }

        [Test]
        [CardBehaviorTest("57108202")]
        public void Morphtronic_Remoten_banishes_a_Graveyard_target_and_searches_its_Level()
        {
            CardInstance remoten = new CardInstance(StarterCatalog.Find("57108202"));
            CardInstance target = new CardInstance(StarterCatalog.Find("93542102"));
            CardInstance searchTarget = new CardInstance(StarterCatalog.Find("93542102"));
            engine.Human.MonsterZones[0] = remoten;
            engine.Human.Graveyard.Add(target);
            engine.Human.Deck.Clear();
            engine.Human.Deck.Add(searchTarget);

            Assert.IsTrue(engine.TryActivate(remoten, target));
            Assert.IsTrue(engine.SearchChoiceOpen);
            Assert.IsTrue(engine.TryChooseSearchCard(searchTarget));

            Assert.Contains(target, engine.Human.Banished);
            Assert.Contains(searchTarget, engine.Human.Hand);
        }

        [Test]
        [CardBehaviorTest("57108202")]
        public void Morphtronic_Remoten_in_Defense_Position_recovers_a_same_Level_Graveyard_card()
        {
            CardInstance remoten = new CardInstance(StarterCatalog.Find("57108202"), true, false);
            CardInstance discard = new CardInstance(StarterCatalog.Find("93542102"));
            CardInstance recover = new CardInstance(StarterCatalog.Find("93542102"));
            engine.Human.MonsterZones[0] = remoten;
            engine.Human.Hand.Add(discard);
            engine.Human.Graveyard.Add(recover);

            Assert.IsTrue(engine.TryActivate(remoten, discard));

            Assert.Contains(discard, engine.Human.Graveyard);
            Assert.Contains(recover, engine.Human.Hand);
        }

        [Test]
        [CardBehaviorTest("18013090")]
        public void Nitro_Warrior_gains_ATK_after_a_Spell_and_can_make_its_follow_up_attack()
        {
            CardInstance nitro = new CardInstance(StarterCatalog.Find("18013090"));
            CardInstance spell = new CardInstance(StarterCatalog.Find("14087893"));
            CardInstance firstTarget = new CardInstance(CreateTestCard(
                "nitro-first-target", "Strong Target", CardKind.Monster, 4, attack: 3500));
            CardInstance secondTarget = new CardInstance(StarterCatalog.Find("93542102"), true, false);
            engine.Human.MonsterZones[0] = nitro;
            engine.Human.Hand.Add(spell);
            engine.Opponent.MonsterZones[0] = firstTarget;
            engine.Opponent.MonsterZones[1] = secondTarget;
            SetTurnNumber(engine, 2);

            Assert.IsTrue(engine.TryActivate(spell, firstTarget));
            Assert.IsTrue(nitro.NitroWarriorBoostReady);
            SetPhase(DuelPhase.Battle);
            Assert.IsTrue(engine.TryAttack(nitro, firstTarget));

            Assert.IsTrue(secondTarget.AttackPosition);
            Assert.IsFalse(nitro.HasAttacked);
            Assert.IsTrue(engine.TryAttack(nitro, secondTarget));
            Assert.IsTrue(engine.Opponent.Graveyard.Contains(secondTarget));
        }

        [Test]
        [CardBehaviorTest("2295440")]
        public void One_for_One_discards_a_monster_and_summons_a_Level_one_monster_from_the_Deck()
        {
            CardInstance oneForOne = new CardInstance(StarterCatalog.Find("2295440"));
            CardInstance discard = new CardInstance(StarterCatalog.Find("93542102"));
            CardInstance levelOne = new CardInstance(StarterCatalog.Find("93542102"));
            engine.Human.Hand.AddRange(new[] { oneForOne, discard });
            engine.Human.Deck.Clear();
            engine.Human.Deck.Add(levelOne);

            Assert.IsTrue(engine.TryActivate(oneForOne));

            Assert.Contains(discard, engine.Human.Graveyard);
            Assert.Contains(levelOne, engine.Human.MonsterZones);
            Assert.IsEmpty(engine.Human.Deck);
        }

        [Test]
        [CardBehaviorTest("67169062")]
        public void Pot_of_Avarice_returns_five_Graveyard_monsters_and_draws_two_cards()
        {
            CardInstance pot = new CardInstance(StarterCatalog.Find("67169062"));
            List<CardInstance> monsters = Enumerable.Range(0, 5)
                .Select(_ => new CardInstance(StarterCatalog.Find("93542102"))).ToList();
            engine.Human.Graveyard.AddRange(monsters);
            engine.Human.Deck.Clear();
            engine.Human.Deck.AddRange(Enumerable.Range(0, 2)
                .Select(_ => new CardInstance(StarterCatalog.Find("13039848"))));
            engine.Human.Hand.Add(pot);

            Assert.IsTrue(engine.TryActivate(pot));

            Assert.AreEqual(2, engine.Human.Hand.Count);
            Assert.AreEqual(1, engine.Human.Graveyard.Count);
            Assert.Contains(pot, engine.Human.Graveyard);
            Assert.AreEqual(5, engine.Human.Deck.Count);
        }

        [Test]
        [CardBehaviorTest("2403771")]
        public void Power_Tool_Dragon_adds_one_of_three_Equip_Spells_and_shuffles_the_others_back()
        {
            CardInstance powerTool = new CardInstance(StarterCatalog.Find("2403771"));
            CardDefinition equipDefinition = CreateTestCard(
                "power-tool-equip", "Test Equip Spell", CardKind.Spell, 0,
                cardSubtype: CardSubtype.Equip);
            List<CardInstance> equips = Enumerable.Range(0, 3)
                .Select(_ => new CardInstance(equipDefinition)).ToList();
            engine.Human.MonsterZones[0] = powerTool;
            engine.Human.Deck.Clear();
            engine.Human.Deck.AddRange(equips);

            try
            {
                Assert.IsTrue(engine.TryActivate(powerTool));

                Assert.AreEqual(1, engine.Human.Hand.Count(card =>
                    card.Definition.Subtype == CardSubtype.Equip));
                Assert.AreEqual(2, engine.Human.Deck.Count);
                Assert.IsTrue(equips.Any(card => engine.Human.Hand.Contains(card)));
                Assert.AreEqual(2, equips.Count(card => engine.Human.Deck.Contains(card)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(equipDefinition);
            }
        }

        [Test]
        [CardBehaviorTest("20932152")]
        public void Quickdraw_Synchron_discards_a_monster_to_special_summon_itself_from_the_hand()
        {
            CardInstance quickdraw = new CardInstance(StarterCatalog.Find("20932152"));
            CardInstance discard = new CardInstance(StarterCatalog.Find("93542102"));
            engine.Human.Hand.AddRange(new[] { quickdraw, discard });

            Assert.IsTrue(engine.TryActivate(quickdraw));

            Assert.Contains(quickdraw, engine.Human.MonsterZones);
            Assert.Contains(discard, engine.Human.Graveyard);
        }

        [Test]
        [CardBehaviorTest("23571046")]
        public void Quillbolt_Hedgehog_revives_while_a_Tuner_is_controlled_and_is_banished_when_it_leaves()
        {
            CardInstance quillbolt = new CardInstance(StarterCatalog.Find("23571046"));
            CardInstance tuner = new CardInstance(StarterCatalog.Find("63977008"));
            engine.Human.Graveyard.Add(quillbolt);
            engine.Human.MonsterZones[0] = tuner;

            Assert.IsTrue(engine.TryActivate(quillbolt));
            InvokeSanganSendToGraveyard(engine.Human, quillbolt);

            Assert.Contains(quillbolt, engine.Human.Banished);
            Assert.IsFalse(engine.Human.Graveyard.Contains(quillbolt));
        }

        [Test]
        [CardBehaviorTest("70902743")]
        public void Red_Dragon_Archfiend_destroys_all_Defense_Position_monsters_it_attacks()
        {
            CardInstance archfiend = new CardInstance(StarterCatalog.Find("70902743"));
            CardInstance target = new CardInstance(StarterCatalog.Find("93542102"), true, false);
            CardInstance secondDefenseMonster = new CardInstance(StarterCatalog.Find("57108202"), true, false);
            engine.Human.MonsterZones[0] = archfiend;
            engine.Opponent.MonsterZones[0] = target;
            engine.Opponent.MonsterZones[1] = secondDefenseMonster;
            SetTurnNumber(engine, 2);
            SetPhase(DuelPhase.Battle);

            Assert.IsTrue(engine.TryAttack(archfiend, target));

            Assert.Contains(target, engine.Opponent.Graveyard);
            Assert.Contains(secondDefenseMonster, engine.Opponent.Graveyard);
        }

        [Test]
        [CardBehaviorTest("71971554")]
        public void Road_Synchron_reduces_its_Synchro_Material_Level_by_two_for_other_Synchros()
        {
            CardInstance roadSynchron = new CardInstance(StarterCatalog.Find("71971554"));
            CardDefinition levelSix = CreateTestCard(
                "road-material", "Level 6 Material", CardKind.Monster, 6);
            CardInstance material = new CardInstance(levelSix);
            CardDefinition stardust = StarterCatalog.Find("44508094");
            engine.Human.MonsterZones[0] = roadSynchron;
            engine.Human.MonsterZones[1] = material;
            engine.Human.ExtraDeck.Clear();
            engine.Human.ExtraDeck.Add(new CardInstance(stardust));

            try
            {
                Assert.IsTrue(engine.TrySynchroSummon(stardust, new[] { roadSynchron, material }));
                Assert.IsTrue(engine.Human.MonsterZones.Any(card =>
                    card != null && card.Definition == stardust));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(levelSix);
            }
        }

        [Test]
        [CardBehaviorTest("56120475")]
        public void Sakuretsu_Armor_destroys_the_attacking_monster()
        {
            CardInstance sakuretsu = new CardInstance(StarterCatalog.Find("56120475"), false);
            CardInstance attacker = new CardInstance(StarterCatalog.Find("49003716"));
            CardInstance target = new CardInstance(StarterCatalog.Find("93542102"));
            engine.Opponent.SpellTrapZones[0] = sakuretsu;
            engine.Human.MonsterZones[0] = attacker;
            engine.Opponent.MonsterZones[0] = target;
            SetTurnNumber(engine, 2);
            SetPhase(DuelPhase.Battle);

            Assert.IsTrue(engine.TryAttack(attacker, target));

            Assert.Contains(attacker, engine.Human.Graveyard);
            Assert.Contains(sakuretsu, engine.Opponent.Graveyard);
        }

        [Test]
        [CardBehaviorTest("70342110")]
        public void Dimensional_Prison_banishes_the_attacking_monster()
        {
            CardInstance prison = new CardInstance(StarterCatalog.Find("70342110"), false);
            CardInstance attacker = new CardInstance(StarterCatalog.Find("49003716"));
            CardInstance target = new CardInstance(StarterCatalog.Find("93542102"));
            engine.Opponent.SpellTrapZones[0] = prison;
            engine.Human.MonsterZones[0] = attacker;
            engine.Opponent.MonsterZones[0] = target;
            SetTurnNumber(engine, 2);
            SetPhase(DuelPhase.Battle);

            Assert.IsTrue(engine.TryAttack(attacker, target));

            Assert.Contains(attacker, engine.Human.Banished);
            Assert.IsFalse(engine.Human.Graveyard.Contains(attacker));
            Assert.Contains(prison, engine.Opponent.Graveyard);
        }

        [Test]
        [CardBehaviorTest("44095762")]
        public void Mirror_Force_destroys_all_opposing_attack_position_monsters()
        {
            CardInstance mirrorForce = new CardInstance(StarterCatalog.Find("44095762"), false);
            CardInstance attacker = new CardInstance(StarterCatalog.Find("49003716"));
            CardInstance secondAttacker = new CardInstance(StarterCatalog.Find("93542102"));
            CardInstance target = new CardInstance(StarterCatalog.Find("57108202"));
            engine.Opponent.SpellTrapZones[0] = mirrorForce;
            engine.Human.MonsterZones[0] = attacker;
            engine.Human.MonsterZones[1] = secondAttacker;
            engine.Opponent.MonsterZones[0] = target;
            SetTurnNumber(engine, 2);
            SetPhase(DuelPhase.Battle);

            Assert.IsTrue(engine.TryAttack(attacker, target));

            Assert.Contains(attacker, engine.Human.Graveyard);
            Assert.Contains(secondAttacker, engine.Human.Graveyard);
            Assert.Contains(target, engine.Opponent.MonsterZones);
            Assert.AreEqual(CardEffect.MirrorForce, mirrorForce.Definition.Effect);
        }

        [Test]
        [CardBehaviorTest("94192409")]
        public void Compulsory_Evacuation_Device_returns_the_target_to_its_owners_hand()
        {
            CardInstance device = new CardInstance(StarterCatalog.Find("94192409"), false);
            CardInstance target = new CardInstance(StarterCatalog.Find("93542102"));
            engine.Human.SpellTrapZones[0] = device;
            engine.Opponent.MonsterZones[0] = target;

            Assert.IsTrue(engine.TryActivate(device, target));

            Assert.Contains(target, engine.Opponent.Hand);
            Assert.IsFalse(engine.Opponent.MonsterZones.Contains(target));
            Assert.Contains(device, engine.Human.Graveyard);
        }

        [Test]
        [CardBehaviorTest("14315573")]
        public void Negate_Attack_stops_battle_damage()
        {
            CardInstance negateAttack = new CardInstance(StarterCatalog.Find("14315573"), false);
            CardInstance attacker = new CardInstance(StarterCatalog.Find("49003716"));
            CardInstance target = new CardInstance(StarterCatalog.Find("93542102"));
            engine.Opponent.SpellTrapZones[0] = negateAttack;
            engine.Human.MonsterZones[0] = attacker;
            engine.Opponent.MonsterZones[0] = target;
            SetTurnNumber(engine, 2);
            SetPhase(DuelPhase.Battle);

            Assert.IsTrue(engine.TryAttack(attacker, target));

            Assert.Contains(attacker, engine.Human.MonsterZones);
            Assert.Contains(target, engine.Opponent.MonsterZones);
            Assert.Contains(negateAttack, engine.Opponent.Graveyard);
            Assert.AreEqual(8000, engine.Opponent.LifePoints);
        }

        [Test]
        [CardBehaviorTest("77414722")]
        public void Magic_Jammer_discards_a_card_to_negate_a_Spell_activation()
        {
            CardInstance jammer = new CardInstance(StarterCatalog.Find("77414722"), false);
            CardInstance darkHole = new CardInstance(StarterCatalog.Find("53129443"));
            CardInstance humanMonster = new CardInstance(StarterCatalog.Find("93542102"));
            CardInstance opponentMonster = new CardInstance(StarterCatalog.Find("57108202"));
            CardInstance discard = new CardInstance(StarterCatalog.Find("67270095"));
            engine.Opponent.SpellTrapZones[0] = jammer;
            engine.Opponent.Hand.Add(discard);
            engine.Human.Hand.Add(darkHole);
            engine.Human.MonsterZones[0] = humanMonster;
            engine.Opponent.MonsterZones[0] = opponentMonster;

            Assert.IsTrue(engine.TryActivate(darkHole));

            Assert.Contains(jammer, engine.Opponent.Graveyard);
            Assert.Contains(discard, engine.Opponent.Graveyard);
            Assert.Contains(darkHole, engine.Human.Graveyard);
            Assert.Contains(humanMonster, engine.Human.MonsterZones);
            Assert.Contains(opponentMonster, engine.Opponent.MonsterZones);
        }

        [Test]
        [CardBehaviorTest("49010598")]
        public void Divine_Wrath_discards_to_negate_and_destroy_a_monster_effect_source()
        {
            CardInstance divineWrath = new CardInstance(StarterCatalog.Find("49010598"), false);
            CardInstance gale = new CardInstance(StarterCatalog.Find("2009101"));
            CardInstance target = new CardInstance(StarterCatalog.Find("70095154"));
            CardInstance discard = new CardInstance(StarterCatalog.Find("67270095"));
            engine.Opponent.SpellTrapZones[0] = divineWrath;
            engine.Opponent.Hand.Add(discard);
            engine.Human.MonsterZones[0] = gale;
            engine.Opponent.MonsterZones[0] = target;

            Assert.IsTrue(engine.TryActivate(gale, target));

            Assert.Contains(divineWrath, engine.Opponent.Graveyard);
            Assert.Contains(discard, engine.Opponent.Graveyard);
            Assert.Contains(gale, engine.Human.Graveyard);
            Assert.AreEqual(2100, target.CurrentAttack);
        }

        [Test]
        [CardBehaviorTest("29401950")]
        public void Bottomless_Trap_Hole_destroys_and_banishes_a_1500_ATK_summon()
        {
            CardInstance bottomless = new CardInstance(StarterCatalog.Find("29401950"), false);
            CardInstance summoned = new CardInstance(StarterCatalog.Find("49003716"));
            engine.Opponent.SpellTrapZones[0] = bottomless;
            engine.Human.Hand.Add(summoned);

            Assert.IsTrue(engine.TryNormalSummon(summoned, Array.Empty<CardInstance>()));

            Assert.Contains(bottomless, engine.Opponent.Graveyard);
            Assert.Contains(summoned, engine.Human.Banished);
            Assert.IsFalse(engine.Human.Graveyard.Contains(summoned));
        }

        [Test]
        [CardBehaviorTest("60082869")]
        public void Dust_Tornado_destroys_only_the_selected_opposing_Spell_or_Trap()
        {
            CardInstance dustTornado = new CardInstance(StarterCatalog.Find("60082869"), false);
            CardDefinition mirrorForce = StarterCatalog.Find("44095762");
            CardInstance target = new CardInstance(mirrorForce, false);
            CardInstance otherSetCard = new CardInstance(mirrorForce, false);
            CardInstance ownSetCard = new CardInstance(mirrorForce, false);
            engine.Human.SpellTrapZones[0] = dustTornado;
            engine.Human.SpellTrapZones[1] = ownSetCard;
            engine.Opponent.SpellTrapZones[0] = target;
            engine.Opponent.SpellTrapZones[1] = otherSetCard;

            Assert.IsFalse(engine.TryActivate(dustTornado, ownSetCard));
            Assert.IsTrue(engine.Human.SpellTrapZones.Contains(dustTornado));
            Assert.IsTrue(engine.TryActivate(dustTornado, target));

            Assert.Contains(target, engine.Opponent.Graveyard);
            Assert.IsTrue(engine.Opponent.SpellTrapZones.Contains(otherSetCard));
            Assert.Contains(dustTornado, engine.Human.Graveyard);
        }

        [Test]
        [CardBehaviorTest("4178474")]
        public void Raigeki_Break_discards_a_card_and_destroys_a_selected_field_card()
        {
            CardInstance raigekiBreak = new CardInstance(StarterCatalog.Find("4178474"), false);
            CardInstance target = new CardInstance(StarterCatalog.Find("93542102"));
            CardInstance discard = new CardInstance(StarterCatalog.Find("67270095"));
            engine.Human.SpellTrapZones[0] = raigekiBreak;
            engine.Human.Hand.Add(discard);
            engine.Opponent.MonsterZones[0] = target;

            Assert.IsTrue(engine.TryActivate(raigekiBreak, target));

            Assert.Contains(discard, engine.Human.Graveyard);
            Assert.Contains(target, engine.Opponent.Graveyard);
            Assert.Contains(raigekiBreak, engine.Human.Graveyard);
        }

        [Test]
        [CardBehaviorTest("70231910")]
        public void Dark_Core_discards_a_card_to_banish_a_face_up_monster()
        {
            CardInstance darkCore = new CardInstance(StarterCatalog.Find("70231910"));
            CardInstance target = new CardInstance(StarterCatalog.Find("93542102"));
            CardInstance discard = new CardInstance(StarterCatalog.Find("67270095"));
            engine.Human.Hand.Add(darkCore);
            engine.Human.Hand.Add(discard);
            engine.Opponent.MonsterZones[0] = target;

            Assert.IsTrue(engine.TryActivate(darkCore, target));

            Assert.Contains(discard, engine.Human.Graveyard);
            Assert.Contains(darkCore, engine.Human.Graveyard);
            Assert.Contains(target, engine.Opponent.Banished);
            Assert.IsFalse(engine.Opponent.Graveyard.Contains(target));
        }

        [Test]
        [CardBehaviorTest("98427577")]
        public void Scrap_Iron_Scarecrow_negates_an_attack_and_sets_itself_again()
        {
            CardInstance scarecrow = new CardInstance(StarterCatalog.Find("98427577"), false);
            CardInstance attacker = new CardInstance(StarterCatalog.Find("49003716"));
            CardInstance target = new CardInstance(StarterCatalog.Find("93542102"));
            engine.Opponent.SpellTrapZones[0] = scarecrow;
            engine.Human.MonsterZones[0] = attacker;
            engine.Opponent.MonsterZones[0] = target;
            SetTurnNumber(engine, 2);
            SetPhase(DuelPhase.Battle);

            Assert.IsTrue(engine.TryAttack(attacker, target));

            Assert.IsFalse(scarecrow.FaceUp);
            Assert.AreSame(scarecrow, engine.Opponent.SpellTrapZones[0]);
            Assert.Contains(attacker, engine.Human.MonsterZones);
        }

        [Test]
        [CardBehaviorTest("24696097")]
        public void Shooting_Star_Dragon_excavates_Tuners_to_gain_additional_attacks()
        {
            CardInstance shootingStar = new CardInstance(StarterCatalog.Find("24696097"));
            CardInstance firstTarget = new CardInstance(StarterCatalog.Find("93542102"));
            CardInstance secondTarget = new CardInstance(StarterCatalog.Find("57108202"));
            engine.Human.MonsterZones[0] = shootingStar;
            engine.Human.Deck.Clear();
            engine.Human.Deck.AddRange(new[]
            {
                new CardInstance(StarterCatalog.Find("63977008")),
                new CardInstance(StarterCatalog.Find("67270095")),
                new CardInstance(StarterCatalog.Find("13039848")),
                new CardInstance(StarterCatalog.Find("13039848")),
                new CardInstance(StarterCatalog.Find("13039848"))
            });
            engine.Opponent.MonsterZones[0] = firstTarget;
            engine.Opponent.MonsterZones[1] = secondTarget;
            Assert.IsTrue(engine.TryActivate(shootingStar));
            Assert.AreEqual(1, shootingStar.ExtraAttacksThisTurn);
            SetTurnNumber(engine, 2);
            SetPhase(DuelPhase.Battle);
            Assert.IsTrue(engine.TryAttack(shootingStar, firstTarget));
            Assert.IsFalse(shootingStar.HasAttacked);
            Assert.IsTrue(engine.TryAttack(shootingStar, secondTarget));

            Assert.Contains(firstTarget, engine.Opponent.Graveyard);
            Assert.Contains(secondTarget, engine.Opponent.Graveyard);
        }

        [Test]
        [CardBehaviorTest("41420027")]
        public void Solemn_Judgment_pays_half_Life_Points_and_negates_a_Spell_activation()
        {
            CardInstance solemn = new CardInstance(StarterCatalog.Find("41420027"), false);
            CardInstance darkHole = new CardInstance(StarterCatalog.Find("53129443"));
            CardInstance humanMonster = new CardInstance(StarterCatalog.Find("93542102"));
            CardInstance opponentMonster = new CardInstance(StarterCatalog.Find("93542102"));
            engine.Opponent.SpellTrapZones[0] = solemn;
            engine.Human.Hand.Add(darkHole);
            engine.Human.MonsterZones[0] = humanMonster;
            engine.Opponent.MonsterZones[0] = opponentMonster;

            Assert.IsTrue(engine.TryActivate(darkHole));

            Assert.AreEqual(4000, engine.Opponent.LifePoints);
            Assert.Contains(humanMonster, engine.Human.MonsterZones);
            Assert.Contains(opponentMonster, engine.Opponent.MonsterZones);
            Assert.Contains(darkHole, engine.Human.Graveyard);
        }

        [Test]
        [CardBehaviorTest("44508094")]
        public void Stardust_Dragon_negates_destruction_and_returns_in_the_End_Phase()
        {
            CardInstance stardust = new CardInstance(StarterCatalog.Find("44508094"));
            CardInstance darkHole = new CardInstance(StarterCatalog.Find("53129443"));
            CardInstance humanMonster = new CardInstance(StarterCatalog.Find("93542102"));
            engine.Opponent.MonsterZones[0] = stardust;
            engine.Human.MonsterZones[0] = humanMonster;
            engine.Human.Hand.Add(darkHole);

            Assert.IsTrue(engine.TryActivate(darkHole));

            Assert.Contains(humanMonster, engine.Human.MonsterZones);
            Assert.Contains(stardust, engine.Opponent.Graveyard);
            typeof(DuelEngine).GetMethod("EndTurn", PrivateInstance)
                .Invoke(engine, new object[] { engine.Opponent });

            Assert.Contains(stardust, engine.Opponent.MonsterZones);
        }

        [Test]
        [CardBehaviorTest("68543408")]
        public void Stardust_Xiaolong_revives_when_Stardust_Dragon_is_Synchro_Summoned()
        {
            CardInstance tuner = new CardInstance(StarterCatalog.Find("63977008"));
            CardInstance nonTuner = new CardInstance(StarterCatalog.Find("49003716"));
            CardInstance levelOne = new CardInstance(StarterCatalog.Find("68543408"));
            CardInstance graveyardXiaolong = new CardInstance(StarterCatalog.Find("68543408"));
            CardDefinition stardust = StarterCatalog.Find("44508094");
            engine.Human.MonsterZones[0] = tuner;
            engine.Human.MonsterZones[1] = nonTuner;
            engine.Human.MonsterZones[2] = levelOne;
            engine.Human.Graveyard.Add(graveyardXiaolong);
            engine.Human.ExtraDeck.Clear();
            engine.Human.ExtraDeck.Add(new CardInstance(stardust));

            Assert.IsTrue(engine.TrySynchroSummon(stardust, new[] { tuner, nonTuner, levelOne }));

            Assert.Contains(graveyardXiaolong, engine.Human.MonsterZones);
            Assert.IsTrue(graveyardXiaolong.AttackPosition);
        }

        [Test]
        [CardBehaviorTest("58120309")]
        public void Starlight_Road_negates_mass_destruction_and_summons_Stardust_Dragon()
        {
            CardInstance starlight = new CardInstance(StarterCatalog.Find("58120309"), false);
            CardInstance darkHole = new CardInstance(StarterCatalog.Find("53129443"));
            CardInstance opponentMonsterOne = new CardInstance(StarterCatalog.Find("93542102"));
            CardInstance opponentMonsterTwo = new CardInstance(StarterCatalog.Find("57108202"));
            CardDefinition stardust = StarterCatalog.Find("44508094");
            engine.Opponent.SpellTrapZones[0] = starlight;
            engine.Opponent.MonsterZones[0] = opponentMonsterOne;
            engine.Opponent.MonsterZones[1] = opponentMonsterTwo;
            engine.Opponent.ExtraDeck.Clear();
            engine.Opponent.ExtraDeck.Add(new CardInstance(stardust));
            engine.Human.Hand.Add(darkHole);

            Assert.IsTrue(engine.TryActivate(darkHole));

            Assert.Contains(opponentMonsterOne, engine.Opponent.MonsterZones);
            Assert.Contains(opponentMonsterTwo, engine.Opponent.MonsterZones);
            Assert.IsTrue(engine.Opponent.MonsterZones.Any(card =>
                card != null && card.Definition == stardust));
        }

        [Test]
        [CardBehaviorTest("58120309")]
        public void Starlight_Road_does_not_consume_its_Set_Trap_for_a_single_card_destruction()
        {
            CardInstance starlight = new CardInstance(StarterCatalog.Find("58120309"), false);
            CardInstance singleTargetDestruction = new CardInstance(StarterCatalog.Find("5318639"));
            CardInstance opponentSpell = new CardInstance(StarterCatalog.Find("91351370"));
            engine.Opponent.SpellTrapZones[0] = starlight;
            engine.Opponent.SpellTrapZones[1] = opponentSpell;
            engine.Human.Hand.Add(singleTargetDestruction);

            Assert.IsTrue(engine.TryActivate(singleTargetDestruction, opponentSpell));

            Assert.IsFalse(starlight.FaceUp);
            Assert.AreSame(starlight, engine.Opponent.SpellTrapZones[0]);
            Assert.Contains(opponentSpell, engine.Opponent.Graveyard);
        }

        [Test]
        [CardBehaviorTest("36643046")]
        public void Synchron_Explorer_revives_a_Synchron_with_its_effects_negated()
        {
            CardInstance explorer = new CardInstance(StarterCatalog.Find("36643046"));
            CardInstance junkSynchron = new CardInstance(StarterCatalog.Find("63977008"));
            engine.Human.Graveyard.Add(junkSynchron);
            engine.Human.Hand.Add(explorer);

            Assert.IsTrue(engine.TryNormalSummon(explorer, Array.Empty<CardInstance>()));

            Assert.Contains(junkSynchron, engine.Human.MonsterZones);
            Assert.IsTrue(junkSynchron.EffectsNegated);
            Assert.IsTrue(junkSynchron.AttackPosition);
        }

        [Test]
        [CardBehaviorTest("96363153")]
        public void Tuning_searches_a_Synchron_Tuner_then_sends_the_new_top_card_to_the_Graveyard()
        {
            CardInstance tuning = new CardInstance(StarterCatalog.Find("96363153"));
            CardInstance synchron = new CardInstance(StarterCatalog.Find("63977008"));
            CardInstance milled = new CardInstance(StarterCatalog.Find("13039848"));
            engine.Human.Hand.Add(tuning);
            engine.Human.Deck.Clear();
            engine.Human.Deck.AddRange(new[] { synchron, milled });

            Assert.IsTrue(engine.TryActivate(tuning));
            Assert.IsTrue(engine.TryChooseSearchCard(synchron));

            Assert.Contains(synchron, engine.Human.Hand);
            Assert.Contains(milled, engine.Human.Graveyard);
            Assert.IsEmpty(engine.Human.Deck);
        }

        [Test]
        [CardBehaviorTest("67270095")]
        public void Turbo_Synchron_summons_a_low_ATK_monster_after_battle_damage()
        {
            CardInstance turbo = new CardInstance(StarterCatalog.Find("67270095"));
            CardInstance strongTarget = new CardInstance(CreateTestCard(
                "turbo-target", "Strong Attacker", CardKind.Monster, 4, attack: 2500));
            CardInstance summon = new CardInstance(StarterCatalog.Find("93542102"));
            engine.Human.MonsterZones[0] = turbo;
            engine.Human.Hand.Add(summon);
            engine.Opponent.MonsterZones[0] = strongTarget;
            SetTurnNumber(engine, 2);
            SetPhase(DuelPhase.Battle);

            Assert.IsTrue(engine.TryAttack(turbo, strongTarget));

            Assert.Contains(summon, engine.Human.MonsterZones);
            Assert.AreEqual(7100, engine.Human.LifePoints);
        }

        [OneTimeTearDown]
        public void WriteReport()
        {
            if (StarterCatalog.ImportedCardCount == 0)
                return;

            HashSet<string> structureCardIds = new HashSet<string>(StarterCatalog.StructureDecks
                .SelectMany(deck => deck.MainDeck.Concat(deck.ExtraDeck))
                .Select(card => card.CardId), StringComparer.Ordinal);
            Dictionary<string, CardReportRow> cardResults = StarterCatalog.StructureDecks
                .SelectMany(deck => deck.MainDeck.Concat(deck.ExtraDeck))
                .GroupBy(card => card.CardId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => CreateCardReportRow(group.First()), StringComparer.Ordinal);
            Dictionary<string, CardReportRow> importedEffectResults = StarterCatalog.All
                .Where(card => card.Effect != CardEffect.None)
                .GroupBy(card => card.CardId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => CreateCardReportRow(group.First()), StringComparer.Ordinal);

            foreach (TestReportRow test in TestResults.Where(result =>
                         result.CardIds != null && result.CardIds.Length > 0))
            {
                foreach (string cardId in test.CardIds.Distinct(StringComparer.Ordinal))
                {
                    RecordCardBehaviorTest(cardResults, cardId, test);
                    RecordCardBehaviorTest(importedEffectResults, cardId, test);
                }
            }

            int cardsWithPassingBehavior = cardResults.Values.Count(result => result.Status == "PASS");
            int cardsFailingBehavior = cardResults.Count - cardsWithPassingBehavior;
            List<CardReportRow> otherEffectResults = importedEffectResults.Values
                .Where(result => !structureCardIds.Contains(result.CardId))
                .OrderBy(result => result.Name, StringComparer.Ordinal)
                .ToList();
            int otherEffectCardsWithPassingBehavior =
                otherEffectResults.Count(result => result.Status == "PASS");
            int otherEffectCardsFailingBehavior =
                otherEffectResults.Count - otherEffectCardsWithPassingBehavior;
            int otherCardBehaviorTests = TestResults.Count(result =>
                result.CardIds != null && result.CardIds.Any(id => !structureCardIds.Contains(id)));
            int registeredDeckBehaviorTests = TestResults.Count(result =>
                result.CardIds != null && result.CardIds.Length > 0);
            int passingBehaviorTests = TestResults.Count(result =>
                result.Status == "PASSED" && result.CardIds != null && result.CardIds.Length > 0);

            bool catalogValidationPassed = TestResults.Any(result =>
                result.Name == nameof(Csv_catalog_cards_have_unique_ids_and_preserved_card_text) &&
                result.Status == "PASSED");
            int importedCardsWithValidData = catalogValidationPassed
                ? StarterCatalog.ImportedCardCount
                : 0;
            List<CardDefinition> catalogCards = StarterCatalog.All
                .GroupBy(card => card.CardId, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList();
            HashSet<string> implementedCardIds = new HashSet<string>(catalogCards
                .Where(IsNoEffectCard)
                .Select(card => card.CardId), StringComparer.Ordinal);
            implementedCardIds.UnionWith(importedEffectResults.Values
                .Where(result => result.Status == "PASS")
                .Select(result => result.CardId));
            List<CardDefinition> implementedCards = catalogCards
                .Where(card => implementedCardIds.Contains(card.CardId))
                .ToList();
            List<CardDefinition> unimplementedCards = catalogCards
                .Where(card => !implementedCardIds.Contains(card.CardId))
                .ToList();
            List<KeyValuePair<string, int>> implementedBreakdown = implementedCards
                .GroupBy(GetImplementedMechanicCategory)
                .Select(group => new KeyValuePair<string, int>(group.Key, group.Count()))
                .OrderByDescending(group => group.Value)
                .ThenBy(group => group.Key, StringComparer.Ordinal)
                .ToList();
            List<KeyValuePair<string, int>> unimplementedBreakdown = unimplementedCards
                .GroupBy(GetUnimplementedMechanicCategory)
                .Select(group => new KeyValuePair<string, int>(group.Key, group.Count()))
                .OrderByDescending(group => group.Value)
                .ThenBy(group => group.Key, StringComparer.Ordinal)
                .ToList();
            string reportRoot = Environment.GetEnvironmentVariable("TOTALLY_NOT_YUGIOH_REPORT_ROOT");
            if (string.IsNullOrWhiteSpace(reportRoot))
                reportRoot = Directory.GetParent(Application.dataPath).FullName;
            string reportDirectory = Path.Combine(reportRoot, "TestReports");
            Directory.CreateDirectory(reportDirectory);
            string reportPath = Path.Combine(reportDirectory, "Card-Effect-Mock-Duel-Report.md");

            List<string> lines = new List<string>
            {
                "# Registered Card Behavior Test Report",
                "",
                "Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                "",
                "A card is considered working only when it has an explicitly associated card-specific behavior test and all associated tests pass. Imported text and effect mappings do not count as behavior coverage. Cards with imported effect mappings but without a passing behavior test are reported as FAIL.",
                "",
                "## Full-catalog implementation coverage",
                "",
                "Every unique catalog card is counted once. Vanilla normal monsters and Token definitions with no mapped effect count as implemented no-effect cards; effect cards count as implemented only when their mapped mechanic has passing card-attributed behavior tests. Everything else, including unmapped cards with text effects, remains unimplemented.",
                "",
                "| Coverage | Cards |",
                "|---|---:|",
                "| Total unique catalog cards | " + catalogCards.Count + " |",
                "| Implemented (including no-effect cards) | " + implementedCards.Count + " |",
                "| Unimplemented | " + unimplementedCards.Count + " |",
                "| Implemented no-effect / vanilla cards | " + catalogCards.Count(IsNoEffectCard) + " |",
                "| Implemented cards with tested effects | " + importedEffectResults.Values.Count(result => result.Status == "PASS") + " |",
                "",
                "| Implemented card category | Cards |",
                "|---|---:|"
            };

            foreach (KeyValuePair<string, int> category in implementedBreakdown)
                lines.Add("| " + category.Key + " | " + category.Value + " |");

            lines.Add("");
            lines.Add("| Unimplemented card category | Cards |");
            lines.Add("|---|---:|");
            foreach (KeyValuePair<string, int> category in unimplementedBreakdown)
                lines.Add("| " + category.Key + " | " + category.Value + " |");

            lines.Add("");
            lines.Add("## Summary");
            lines.Add("");
            lines.Add("| Result | Count |");
            lines.Add("|---|---:|");
            lines.Add("| CSV records imported | " + StarterCatalog.ImportedCardCount + " |");
            lines.Add("| Imported cards passing identity/data validation | " + importedCardsWithValidData + " |");
            lines.Add("| Unique cards in the four structure decks | " + cardResults.Count + " |");
            lines.Add("| Structure-deck cards with passing behavior tests | " + cardsWithPassingBehavior + " |");
            lines.Add("| Structure-deck cards failing behavior coverage | " + cardsFailingBehavior + " |");
            lines.Add("| Structure-deck-specific behavior tests | " + registeredDeckBehaviorTests + " |");
            lines.Add("| Passing structure-deck-specific behavior tests | " + passingBehaviorTests + " |");
            lines.Add("| Cards with imported effect mappings | " + importedEffectResults.Count + " |");
            lines.Add("| Mapped cards with passing behavior tests | " +
                importedEffectResults.Values.Count(result => result.Status == "PASS") + " |");
            lines.Add("| Mapped cards failing behavior coverage | " +
                importedEffectResults.Values.Count(result => result.Status != "PASS") + " |");
            lines.Add("| Non-structure mapped cards with passing behavior tests | " +
                otherEffectCardsWithPassingBehavior + " |");
            lines.Add("| Non-structure mapped cards failing behavior coverage | " +
                otherEffectCardsFailingBehavior + " |");
            lines.Add("| Non-structure card-specific behavior tests | " + otherCardBehaviorTests + " |");
            lines.Add("| Mock-duel scenarios passed | " + DuelResults.Count + " |");
            lines.Add("| Card/effect report tests passed | " +
                TestResults.Count(result => result.Status == "PASSED") + " |");
            lines.Add("| Card/effect report tests failed | " +
                TestResults.Count(result => result.Status == "FAILED") + " |");
            lines.Add("");
            lines.Add("The structure-deck table tracks all 68 cards in the four configured decks. The additional-card table tracks every catalog card with an imported effect mapping outside those decks. Other CSV cards with no imported effect mapping are not yet considered implemented or covered.");
            lines.Add("");
            lines.Add("## EditMode test results");
            lines.Add("");
            lines.Add("| Result | Test | Details |");
            lines.Add("|---|---|---|");

            foreach (TestReportRow result in TestResults.OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                string details = result.Details.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
                lines.Add("| " + result.Status + " | " +
                    result.Name.Replace("|", "\\|") + " | " + details + " |");
            }

            lines.Add("");
            lines.Add("## Mock-duel scenarios");
            lines.Add("");
            lines.Add("| Result | Scenario | Details |");
            lines.Add("|---|---|---|");
            foreach (string result in DuelResults)
            {
                string[] columns = result.Split(new[] { " | " }, 3, StringSplitOptions.None);
                lines.Add("| " + string.Join(" | ", columns) + " |");
            }

            lines.Add("");
            lines.Add("## Per-card behavior status");
            lines.Add("");
            lines.Add("| Card ID | Card | Result | Card-specific behavior tests |");
            lines.Add("|---|---|---|---|");
            foreach (CardReportRow result in cardResults.Values.OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                string details = result.Details.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
                lines.Add("| " + result.CardId + " | " + result.Name + " | " + result.Status + " | " + details + " |");
            }

            lines.Add("");
            lines.Add("## Additional imported-effect card status");
            lines.Add("");
            lines.Add("| Card ID | Card | Result | Card-specific behavior tests |");
            lines.Add("|---|---|---|---|");
            foreach (CardReportRow result in otherEffectResults)
            {
                string details = result.Details.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
                lines.Add("| " + result.CardId + " | " + result.Name + " | " + result.Status + " | " + details + " |");
            }

            lines.Add("");
            lines.Add("## Running the suite");
            lines.Add("");
            lines.Add("In Unity, open **Window > General > Test Runner**, choose **EditMode**, and run **DuelPrototype.EditModeTests**. The report is regenerated at `TestReports/Card-Effect-Mock-Duel-Report.md`.");
            File.WriteAllLines(reportPath, lines);
            Debug.Log("Registered-deck card behavior report written to " + reportPath);
            DuelResults.Clear();
            TestResults.Clear();
        }

        private static bool IsNoEffectCard(CardDefinition card)
        {
            return card != null && card.Effect == CardEffect.None &&
                   (card.Kind == CardKind.Token ||
                    string.Equals(card.FrameType, "normal", StringComparison.OrdinalIgnoreCase));
        }

        private static string GetImplementedMechanicCategory(CardDefinition card)
        {
            if (IsNoEffectCard(card))
                return "Normal / Token cards with no effect";

            switch (card.Effect)
            {
                case CardEffect.DrawOne:
                case CardEffect.DrawTwo:
                case CardEffect.DiscardDrawSameCount:
                case CardEffect.DrawThreeDiscardTwo:
                case CardEffect.DekoichiDraw:
                case CardEffect.FormulaSynchronDraw:
                case CardEffect.TGHyperLibrarian:
                case CardEffect.TuningwareDraw:
                case CardEffect.PotOfAvarice:
                    return "Draw cards / card advantage";
                case CardEffect.SearchDeckToHand:
                case CardEffect.SanganSearch:
                case CardEffect.BlackWhirlwindSearch:
                case CardEffect.WitchOfTheBlackForestSearch:
                    return "Search the Deck and add to hand";
                case CardEffect.DandylionTokens:
                case CardEffect.Scapegoat:
                case CardEffect.Doppelwarrior:
                case CardEffect.TroopDragonSummon:
                    return "Create Tokens / summon from Deck";
                case CardEffect.DestroyWeakest:
                case CardEffect.DestroySpellTrap:
                case CardEffect.DestroyAllSpellTraps:
                case CardEffect.DestroyOpponentSpellTraps:
                case CardEffect.CompulsoryEvacuationDevice:
                case CardEffect.BottomlessTrapHole:
                case CardEffect.RaigekiBreak:
                case CardEffect.DarkCoreBanish:
                case CardEffect.NoblemanOfCrossout:
                case CardEffect.BookOfMoon:
                    return "Remove or disable a targeted card";
                case CardEffect.DestroyAllMonsters:
                case CardEffect.DestroyAllOpponentMonsters:
                case CardEffect.DestroyHighestDefenseMonster:
                case CardEffect.DiscardOneDestroyFaceUpOpponentMonsters:
                case CardEffect.MirrorForce:
                case CardEffect.IcarusAttack:
                case CardEffect.BlackRoseDragon:
                case CardEffect.TorrentialTribute:
                    return "Destroy multiple cards";
                case CardEffect.DimensionalPrison:
                    return "Banish an attacking monster";
                case CardEffect.ReviveWeakest:
                case CardEffect.ReviveMonsterFromGraveyard:
                case CardEffect.BlackwingRevive:
                case CardEffect.BlackwingSpecialSummon:
                case CardEffect.BlackwingDeckSummon:
                case CardEffect.BlackwingVayuSummon:
                case CardEffect.ColossalFighterRevive:
                case CardEffect.CyberDragonSummon:
                case CardEffect.DebrisDragonRevive:
                case CardEffect.GoyoGuardianRevive:
                case CardEffect.JunkSynchronRevive:
                case CardEffect.LevelEater:
                case CardEffect.MachineDuplication:
                case CardEffect.OneForOne:
                case CardEffect.QuickdrawSynchron:
                case CardEffect.QuillboltHedgehog:
                case CardEffect.ZephyrosRevive:
                case CardEffect.CyberJar:
                    return "Special Summon / revive monsters";
                case CardEffect.StopAttack:
                case CardEffect.CounterSpell:
                case CardEffect.CounterMonsterEffect:
                case CardEffect.CounterTrap:
                case CardEffect.SolemnJudgment:
                case CardEffect.StardustDragon:
                case CardEffect.StarlightRoad:
                case CardEffect.JinzoTrapLock:
                case CardEffect.SakuretsuArmor:
                case CardEffect.ScrapIronScarecrow:
                    return "Negate effects / stop attacks";
                case CardEffect.BoostStrongest:
                case CardEffect.BlackwingBattleBoost:
                case CardEffect.BlackwingHalveMonster:
                case CardEffect.BlackwingSiroccoBoost:
                case CardEffect.BlackwingMistral:
                case CardEffect.BlackwingArmedWing:
                case CardEffect.BlackwingArmorMaster:
                case CardEffect.LimiterRemoval:
                case CardEffect.NitroWarrior:
                case CardEffect.TurboSynchron:
                case CardEffect.MorphtronicBoarden:
                case CardEffect.MorphtronicCelfon:
                case CardEffect.MorphtronicRemoten:
                case CardEffect.JunkWarriorBoost:
                    return "Modify stats / battle behavior";
                case CardEffect.ReturnAllSpellTrapsToHand:
                case CardEffect.DeFusion:
                case CardEffect.MysticBox:
                    return "Return or move cards";
                case CardEffect.BurnOpponent:
                case CardEffect.RestoreLife:
                case CardEffect.LifeStreamSetLifePoints:
                    return "Change Life Points";
                default:
                    return "Other tested effects";
            }
        }

        private static string GetUnimplementedMechanicCategory(CardDefinition card)
        {
            string name = card.DisplayName ?? "";
            string description = card.RulesSummary ?? "";
            string searchableText = name + " " + description;
            if (ContainsAny(searchableText, "Exodia", "Destiny Board", "FINAL",
                    "win the Duel", "win the match", "win the game"))
                return "Alternate-win-condition effects (Exodia, FINAL, etc.)";

            if (ContainsAny(description, "destroy all", "destroy every", "destroy each",
                    "destroy as many", "destroy all other"))
                return "Mass-destruction effects";

            return "Other unimplemented cards";
        }

        private static bool ContainsAny(string text, params string[] phrases)
        {
            return phrases.Any(phrase => text.IndexOf(phrase, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static CardReportRow CreateCardReportRow(CardDefinition card)
        {
            return new CardReportRow
            {
                CardId = card.CardId,
                Name = card.DisplayName,
                Status = "FAIL",
                Details = "No card-specific behavior test is registered."
            };
        }

        private static void RecordCardBehaviorTest(
            Dictionary<string, CardReportRow> results, string cardId, TestReportRow test)
        {
            if (!results.TryGetValue(cardId, out CardReportRow result))
                return;

            result.BehaviorTestCount++;
            if (result.BehaviorTestCount == 1)
                result.Details = "";
            result.Details += (result.Details.Length == 0 ? "" : "; ") +
                test.Name + ": " + test.Status;
            if (test.Status != "PASSED")
                result.Status = "FAIL";
            else if (result.BehaviorTestCount == 1)
                result.Status = "PASS";
        }

        private static void ResetPlayer(DuelPlayer player)
        {
            player.Hand.Clear();
            player.Graveyard.Clear();
            player.Banished.Clear();
            player.Deck.Clear();
            player.ExtraDeck.Clear();
            player.NormalSummonsThisTurn = 0;
            for (int i = 0; i < player.MonsterZones.Length; i++)
                player.MonsterZones[i] = null;
            for (int i = 0; i < player.SpellTrapZones.Length; i++)
                player.SpellTrapZones[i] = null;
            player.FieldSpellZone = null;
            CardDefinition filler = StarterCatalog.Find("13039848");
            player.Deck.AddRange(Enumerable.Range(0, 20).Select(_ => new CardInstance(filler)));
        }

        private static CardDefinition CreateTestCard(
            string id,
            string name,
            CardKind kind,
            int level,
            string frameType = "",
            CardSubtype cardSubtype = CardSubtype.Normal,
            int maximumDeckCopies = 3,
            int attack = 1000,
            int defense = 1000,
            bool isTuner = false,
            string monsterType = "")
        {
            CardDefinition definition = ScriptableObject.CreateInstance<CardDefinition>();
            definition.Configure(id, name, kind, level, attack, defense, isTuner, CardEffect.None, 0, "",
                cardSubtype: cardSubtype, cardFrameType: frameType,
                cardMonsterType: monsterType,
                maximumDeckCopies: maximumDeckCopies);
            return definition;
        }

        private void SetCurrentPlayer(DuelPlayer player)
        {
            SetEngineProperty(engine, nameof(DuelEngine.CurrentPlayer), player);
        }

        private void SetPhase(DuelPhase phase)
        {
            SetEngineProperty(engine, nameof(DuelEngine.Phase), phase);
        }

        private static void SetEngineProperty(DuelEngine target, string propertyName, object value)
        {
            typeof(DuelEngine).GetProperty(propertyName)
                .GetSetMethod(true).Invoke(target, new[] { value });
        }

        private static void SetTurnNumber(DuelEngine target, int turnNumber)
        {
            typeof(DuelEngine).GetField("turnNumber", PrivateInstance).SetValue(target, turnNumber);
        }

        private void InvokeSanganSendToGraveyard(DuelPlayer owner, CardInstance card)
        {
            typeof(DuelEngine).GetMethod("SendMonsterToGraveyard", PrivateInstance)
                .Invoke(engine, new object[] { owner, card, false });
        }

        private void InvokeBeginTurn(DuelPlayer player)
        {
            typeof(DuelEngine).GetMethod("BeginTurn", PrivateInstance)
                .Invoke(engine, new object[] { player });
        }
    }
}
