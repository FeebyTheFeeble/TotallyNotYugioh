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

        private static readonly List<CardReportRow> CardResults = new List<CardReportRow>();
        private static readonly List<string> DuelResults = new List<string>();
        private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private DuelEngine engine;

        [OneTimeSetUp]
        public void ClearReportResults()
        {
            CardResults.Clear();
            DuelResults.Clear();
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
            CardInstance setTrap = engine.Human.Deck[0];
            engine.Human.Deck.RemoveAt(0);
            setTrap.FaceUp = false;
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
        public void Csv_catalog_cards_have_unique_ids_and_preserved_card_text()
        {
            IReadOnlyList<CardDefinition> cards = StarterCatalog.All;
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
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
                if (card.Effect != CardEffect.None)
                {
                    cardsWithMappedEffects++;
                    effectStatus = "Effect mapped to " + card.Effect + ".";
                }
                else if (needsEffectScript)
                {
                    cardsWithUnimplementedEffects++;
                    effectStatus = "NOT IMPLEMENTED: effect text is imported, but no matching duel resolver is registered.";
                    resultStatus = "EFFECT FAIL";
                }

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
            TestContext.WriteLine("Mapped card effects: " + cardsWithMappedEffects);
            TestContext.WriteLine("Effect cards still needing scripts: " + cardsWithUnimplementedEffects);
            Assert.AreEqual(0, cardsWithUnimplementedEffects,
                cardsWithUnimplementedEffects + " effect cards have no registered implementation. Missing effects are test failures.");
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

        [OneTimeTearDown]
        public void WriteReport()
        {
            if (CardResults.Count == 0)
                return;

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string reportDirectory = Path.Combine(projectRoot, "TestReports");
            Directory.CreateDirectory(reportDirectory);
            string reportPath = Path.Combine(reportDirectory, "Card-Effect-Mock-Duel-Report.md");
            int effectScripts = StarterCatalog.All.Count(card => card.Effect != CardEffect.None);
            int effectCardsPending = CardResults.Count(result =>
                result.Details.StartsWith("NOT IMPLEMENTED:", StringComparison.Ordinal));

            List<string> lines = new List<string>
            {
                "# Card Catalog and Mock-Duel Test Report",
                "",
                "Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                "",
                "The CSV supplies card identity, printed stats, category metadata, and effect text. This report validates import for every catalog entry and runs the mock-duel scenarios listed below. An effect marked **NOT IMPLEMENTED** is a test failure; importing its text does not mean the effect works in a duel.",
                "",
                "## Summary",
                "",
                "| Result | Count |",
                "|---|---:|",
                "| CSV records imported | " + StarterCatalog.ImportedCardCount + " |",
                "| Card records passing import validation | " + CardResults.Count + " |",
                "| Effects mapped to duel resolvers | " + effectScripts + " |",
                "| Effect cards without implementation (test failures) | " + effectCardsPending + " |",
                "| Mock-duel scenarios passed | " + DuelResults.Count + " |",
                "",
                "## Mock-duel scenarios",
                "",
                "| Result | Scenario | Details |",
                "|---|---|---|"
            };

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

        private void SetCurrentPlayer(DuelPlayer player)
        {
            typeof(DuelEngine).GetProperty(nameof(DuelEngine.CurrentPlayer))
                .GetSetMethod(true).Invoke(engine, new object[] { player });
        }

        private void SetPhase(DuelPhase phase)
        {
            typeof(DuelEngine).GetProperty(nameof(DuelEngine.Phase))
                .GetSetMethod(true).Invoke(engine, new object[] { phase });
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
