using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace DuelPrototype.Tests
{
    public sealed class CardEffectMockDuelTests
    {
        private sealed class CardReportRow
        {
            public string CardId;
            public string Name;
            public string Status;
            public string Details;
        }

        private sealed class TestReportRow
        {
            public string Name;
            public string Status;
            public string Details;
        }

        private static readonly List<CardReportRow> CardResults = new List<CardReportRow>();
        private static readonly List<string> DuelResults = new List<string>();
        private static readonly List<TestReportRow> TestResults = new List<TestReportRow>();
        private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private DuelEngine engine;

        [OneTimeSetUp]
        public void ClearReportResults()
        {
            CardResults.Clear();
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
                Details = testContext.Result.Message ?? ""
            });
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
            engine.Human.MonsterZones[2] = monster;
            CardInstance setTrap = new CardInstance(StarterCatalog.Find("44095762"), false);
            engine.Human.SpellTrapZones[4] = setTrap;
            engine.Human.LifePoints = 6200;
            SetPhase(DuelPhase.MainTwo);

            DuelEngine restored = DuelStateJson.Deserialize(DuelStateJson.Serialize(engine));

            Assert.AreEqual(DuelPhase.MainTwo, restored.Phase);
            Assert.AreEqual(engine.TurnNumber, restored.TurnNumber);
            Assert.AreEqual(6200, restored.Human.LifePoints);
            Assert.AreEqual(19, restored.Human.Deck.Count);
            Assert.AreEqual(monster.Definition.CardId, restored.Human.MonsterZones[2].Definition.CardId);
            Assert.AreEqual(400, restored.Human.MonsterZones[2].AttackBonus);
            Assert.IsTrue(restored.Human.MonsterZones[2].HasAttacked);
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
            HashSet<string> registeredDeckCardIds = new HashSet<string>(
                StarterCatalog.StructureDecks
                    .SelectMany(deck => deck.MainDeck.Concat(deck.ExtraDeck))
                    .Select(card => card.CardId),
                StringComparer.Ordinal);
            int cardsWithUnimplementedEffects = 0;
            int cardsWithMappedEffects = 0;

            foreach (CardDefinition card in cards)
            {
                bool valid = card != null &&
                    !string.IsNullOrWhiteSpace(card.CardId) &&
                    !string.IsNullOrWhiteSpace(card.DisplayName) &&
                    !string.IsNullOrWhiteSpace(card.FrameType) &&
                    !string.IsNullOrWhiteSpace(card.RulesSummary) &&
                    ids.Add(card.CardId);
                Assert.IsTrue(valid, "Every imported card must have unique, readable source data.");

                bool needsEffectScript = card.IsEffectMonster ||
                    card.Kind == CardKind.Spell ||
                    card.Kind == CardKind.Trap;
                string effectStatus = "No activated effect reported in this card's type.";
                string resultStatus = "DATA PASS";
                bool isRegisteredDeckCard = registeredDeckCardIds.Contains(card.CardId);
                if (isRegisteredDeckCard && card.Effect != CardEffect.None)
                {
                    cardsWithMappedEffects++;
                    effectStatus = "Effect mapped to " + card.Effect + ".";
                }
                else if (isRegisteredDeckCard && needsEffectScript)
                {
                    cardsWithUnimplementedEffects++;
                    effectStatus = "NOT IMPLEMENTED: registered-deck effect text has no effect mapping.";
                    resultStatus = "EFFECT FAIL";
                }
                else if (!isRegisteredDeckCard)
                    effectStatus = "Outside registered-deck effect coverage.";

                CardResults.Add(new CardReportRow
                {
                    CardId = card.CardId,
                    Name = card.DisplayName,
                    Status = resultStatus,
                    Details = effectStatus
                });
            }

            Assert.GreaterOrEqual(ids.Count, StarterCatalog.ImportedCardCount,
                "The catalog should contain every CSV record exactly once, in addition to any custom editor cards.");
            Assert.GreaterOrEqual(StarterCatalog.ImportedCardCount, 14000);
            TestContext.WriteLine("Imported CSV cards: " + StarterCatalog.ImportedCardCount);
            TestContext.WriteLine("Registered-deck cards with mapped effects: " + cardsWithMappedEffects);
            TestContext.WriteLine("Registered-deck effect cards without mappings: " + cardsWithUnimplementedEffects);
            Assert.AreEqual(0, cardsWithUnimplementedEffects,
                cardsWithUnimplementedEffects + " registered-deck effect cards have no effect mapping.");
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
        public void Scapegoat_special_summons_up_to_four_tokens_and_locks_summons()
        {
            CardInstance scapegoat = new CardInstance(StarterCatalog.Find("73915051"));
            engine.Human.Hand.Add(scapegoat);

            Assert.IsTrue(engine.TryActivate(scapegoat));

            Assert.AreEqual(4, engine.Human.MonsterZones.Count(card => card != null));
            Assert.IsTrue(engine.Human.SummonsLockedThisTurn);
            Assert.Contains(scapegoat, engine.Human.Graveyard);
        }

        [Test]
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

        [OneTimeTearDown]
        public void WriteReport()
        {
            if (CardResults.Count == 0)
                return;

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string reportDirectory = Path.Combine(projectRoot, "TestReports");
            Directory.CreateDirectory(reportDirectory);
            string reportPath = Path.Combine(reportDirectory, "Card-Effect-Mock-Duel-Report.md");
            HashSet<string> registeredDeckCardIds = new HashSet<string>(
                StarterCatalog.StructureDecks
                    .SelectMany(deck => deck.MainDeck.Concat(deck.ExtraDeck))
                    .Select(card => card.CardId),
                StringComparer.Ordinal);
            int effectScripts = StarterCatalog.All.Count(card =>
                registeredDeckCardIds.Contains(card.CardId) && card.Effect != CardEffect.None);
            int effectCardsPending = CardResults.Count(result =>
                result.Details.StartsWith("NOT IMPLEMENTED:", StringComparison.Ordinal));

            List<string> lines = new List<string>
            {
                "# Registered Deck and Mock-Duel Test Report",
                "",
                "Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                "",
                "The CSV supplies card identity, printed stats, category metadata, and effect text. This report validates import for every catalog entry and checks effect mappings for cards in the four registered structure decks. Mapping coverage does not replace behavior tests; importing an effect's text does not mean the effect works in a duel.",
                "",
                "## Summary",
                "",
                "| Result | Count |",
                "|---|---:|",
                "| CSV records imported | " + StarterCatalog.ImportedCardCount + " |",
                "| Card records passing import validation | " + CardResults.Count + " |",
                "| Registered-deck cards with mapped effects | " + effectScripts + " |",
                "| Registered-deck effect cards missing mappings | " + effectCardsPending + " |",
                "| Mock-duel scenarios passed | " + DuelResults.Count + " |",
                "| Card/effect report tests passed | " + TestResults.Count(result => result.Status == "PASSED") + " |",
                "| Card/effect report tests failed | " + TestResults.Count(result => result.Status == "FAILED") + " |",
                "",
                "The test results below are captured from CardEffectMockDuelTests and show which effects and mechanics have behavior coverage. The Unity EditMode suite can include additional fixtures. Effect mappings are catalog metadata and do not by themselves prove an effect is fully implemented.",
                "",
                "## EditMode test results",
                "",
                "| Result | Test | Details |",
                "|---|---|---|"
            };

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
            lines.Add("## Per-card import and effect status");
            lines.Add("");
            lines.Add("| Card ID | Card | Result | Effect test status |");
            lines.Add("|---|---|---|---|");
            foreach (CardReportRow result in CardResults.OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                string details = result.Details.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
                lines.Add("| " + result.CardId + " | " + result.Name + " | " + result.Status + " | " + details + " |");
            }

            lines.Add("");
            lines.Add("## Running the suite");
            lines.Add("");
            lines.Add("In Unity, open **Window > General > Test Runner**, choose **EditMode**, and run **DuelPrototype.EditModeTests**. The report is regenerated at `TestReports/Card-Effect-Mock-Duel-Report.md`.");
            File.WriteAllLines(reportPath, lines);
            Debug.Log("Card catalog and mock-duel report written to " + reportPath);
            CardResults.Clear();
            DuelResults.Clear();
            TestResults.Clear();
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
            int maximumDeckCopies = 3)
        {
            CardDefinition definition = ScriptableObject.CreateInstance<CardDefinition>();
            definition.Configure(id, name, kind, level, 1000, 1000, false, CardEffect.None, 0, "",
                cardSubtype: cardSubtype, cardFrameType: frameType,
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
                .Invoke(engine, new object[] { owner, card });
        }

        private void InvokeBeginTurn(DuelPlayer player)
        {
            typeof(DuelEngine).GetMethod("BeginTurn", PrivateInstance)
                .Invoke(engine, new object[] { player });
        }
    }
}
