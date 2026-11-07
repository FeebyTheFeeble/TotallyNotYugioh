using System;
using System.Collections.Generic;
using System.Linq;

namespace DuelPrototype
{
    public enum DuelPhase
    {
        Draw,
        Standby,
        MainOne,
        Battle,
        MainTwo,
        End,
        GameOver
    }

    public sealed class CardInstance
    {
        public CardDefinition Definition { get; }
        public bool FaceUp { get; set; }
        public bool AttackPosition { get; set; }
        public bool HasAttacked { get; set; }
        public bool HasChangedPositionThisTurn { get; set; }
        public bool EffectUsedThisTurn { get; set; }
        public int AttackBonus { get; set; }
        public int PermanentAttackBonus { get; set; }
        public int SummonedTurnNumber { get; set; }
        public int SpellCounters { get; set; }
        public int DestroyedOnFieldTurn { get; set; }
        public int EffectDurationTurnsRemaining { get; set; }
        public List<CardInstance> FusionMaterials { get; } = new List<CardInstance>();
        public CardInstance EquippedMonster { get; set; }
        public int CurrentAttack => Definition.Attack + AttackBonus + PermanentAttackBonus;

        public CardInstance(CardDefinition definition, bool faceUp = true, bool attackPosition = true)
        {
            Definition = definition;
            FaceUp = faceUp;
            AttackPosition = attackPosition;
        }
    }

    public sealed class DuelPlayer
    {
        public string Name { get; }
        public bool IsAi { get; }
        public int LifePoints { get; set; } = 8000;
        public List<CardInstance> Deck { get; } = new List<CardInstance>();
        public List<CardInstance> Hand { get; } = new List<CardInstance>();
        public List<CardInstance> Graveyard { get; } = new List<CardInstance>();
        public List<CardInstance> Banished { get; } = new List<CardInstance>();
        public CardInstance[] MonsterZones { get; } = new CardInstance[5];
        public CardInstance[] SpellTrapZones { get; } = new CardInstance[5];
        public CardInstance FieldSpellZone { get; set; }
        public List<CardInstance> ExtraDeck { get; } = new List<CardInstance>();
        public int NormalSummonsThisTurn { get; set; }
        public bool PositionChangedThisTurn { get; set; }
        public bool SummonsLockedThisTurn { get; set; }
        public bool DarkMagicCurtainUsedThisTurn { get; set; }
        public bool AttackTheMoonUsedThisTurn { get; set; }
        public bool SanganEffectUsedThisTurn { get; set; }
        public int CannotAttackUntilTurn { get; set; }
        public int CannotChangePositionUntilTurn { get; set; }
        public int NoAttackTurnsRemaining { get; set; }
        public int PositionChangesLockedTurnsRemaining { get; set; }
        public int DeckCount => Deck.Count;

        public DuelPlayer(string name, bool isAi)
        {
            Name = name;
            IsAi = isAi;
        }
    }

    public sealed class DuelEngine
    {
        private enum ResponseAttempt { None, Cancelled, Waiting }

        private readonly Random random = new Random();
        private readonly List<string> messages = new List<string>();
        private readonly List<Action> pendingAiActions = new List<Action>();
        private int turnNumber;
        private int pendingAiActionIndex;
        private DuelPlayer pendingAiTurnActor;
        private CardEffect pendingResponseType;
        private Action pendingResponsePass;
        private Action pendingResponseTrap;
        private Action pendingResponseCountered;
        private string pendingResponseSource;
        private CardInstance pendingResponseSourceCard;
        private CardInstance responseEffectAwaitingDiscard;
        private int chainLinkCount;
        private DuelPlayer pendingSanganOwner;
        private CardInstance pendingSanganSource;
        private DuelPlayer temporarilyDisabledCardOwner;
        private string temporarilyDisabledCardName;
        private int temporarilyDisabledCardTurn;

        public DuelPlayer Human { get; private set; }
        public DuelPlayer Opponent { get; private set; }
        public DuelPlayer CurrentPlayer { get; private set; }
        public DuelPhase Phase { get; private set; }
        public bool IsAiVsAi { get; private set; }
        public event Action<DuelPhase> PhaseChanged;
        public event Action<CardInstance, CardInstance> AttackDeclared;
        public event Action<DuelPlayer, CardInstance> CardDrawn;
        public bool CanAttackThisTurn => turnNumber > 1;
        public string AttackRestrictionMessage => CanAttackThisTurn ? "" :
            "The first player cannot attack during the first turn of the duel.";
        public int TurnNumber => turnNumber;
        public string Winner { get; private set; }
        public IReadOnlyList<string> Messages => messages;
        public bool SearchChoiceOpen => pendingSanganOwner != null;
        public string SearchChoicePrompt => SearchChoiceOpen
            ? pendingSanganSource.Definition.DisplayName +
              " was sent from the field to the Graveyard. Choose a monster with 1500 or less ATK."
            : "";
        public IReadOnlyList<CardInstance> AvailableSearchCards => SearchChoiceOpen
            ? pendingSanganOwner.Deck.Where(card => card.Definition.IsMonster &&
                card.Definition.CanBeMainDeck &&
                card.Definition.Attack <= 1500).ToList()
            : new List<CardInstance>();
        public bool ResponseWindowOpen => pendingResponsePass != null;
        public bool ResponseDiscardChoiceOpen => responseEffectAwaitingDiscard != null;
        public string ResponsePrompt
        {
            get
            {
                if (ResponseDiscardChoiceOpen)
                    return "Dark Paladin's effect is active. Select a card from your hand to discard.";
                string source = string.IsNullOrEmpty(pendingResponseSource) ? "The opponent's card" : pendingResponseSource;
                return pendingResponseType == CardEffect.StopAttack
                    ? source + " declared an attack. Activate a Set Trap or pass."
                    : pendingResponseType == CardEffect.CounterSpell
                        ? source + " activated a Spell. Activate a Counter Trap or pass."
                        : pendingResponseType == CardEffect.CounterMonsterEffect
                            ? source + " activated a monster effect. Activate a Counter Trap or pass."
                            : source + " activated a Trap. Chain a Counter Trap or pass.";
            }
        }
        public IReadOnlyList<CardInstance> AvailableResponseTraps => ResponseDiscardChoiceOpen
            ? Human.Hand.Where(card => card != responseEffectAwaitingDiscard).ToList()
            : GetResponseCards(Human, pendingResponseType);
        public bool IsHumanActionWindow => !IsAiVsAi && CurrentPlayer == Human && Phase != DuelPhase.GameOver;
        public string PhaseLabel
        {
            get
            {
                switch (Phase)
                {
                    case DuelPhase.Draw: return "DRAW";
                    case DuelPhase.Standby: return "STANDBY";
                    case DuelPhase.MainOne: return "MAIN 1";
                    case DuelPhase.Battle: return "BATTLE";
                    case DuelPhase.MainTwo: return "MAIN 2";
                    case DuelPhase.End: return "END";
                    default: return "DUEL COMPLETE";
                }
            }
        }

        public void StartMatch(
            IReadOnlyList<CardDefinition> humanDeck,
            IReadOnlyList<CardDefinition> aiDeck,
            bool aiVsAi = false,
            string firstPlayerName = "DUELIST",
            string secondPlayerName = "CPU RIVAL")
        {
            if (humanDeck == null || humanDeck.Count < 20)
                throw new ArgumentException("A duel deck must contain at least 20 cards.", nameof(humanDeck));
            if (aiDeck == null || aiDeck.Count < 20)
                throw new ArgumentException("The opponent deck must contain at least 20 cards.", nameof(aiDeck));

            messages.Clear();
            pendingAiActions.Clear();
            pendingAiActionIndex = 0;
            pendingAiTurnActor = null;
            pendingResponseType = CardEffect.None;
            pendingResponsePass = null;
            pendingResponseTrap = null;
            pendingResponseCountered = null;
            pendingResponseSource = null;
            pendingResponseSourceCard = null;
            responseEffectAwaitingDiscard = null;
            chainLinkCount = 0;
            pendingSanganOwner = null;
            pendingSanganSource = null;
            temporarilyDisabledCardOwner = null;
            temporarilyDisabledCardName = null;
            temporarilyDisabledCardTurn = 0;
            turnNumber = 0;
            Winner = null;
            IsAiVsAi = aiVsAi;
            Human = MakePlayer(firstPlayerName, aiVsAi, humanDeck);
            Opponent = MakePlayer(secondPlayerName, true, aiDeck);
            DrawInitialHand(Human);
            DrawInitialHand(Opponent);
            BeginTurn(Human);
            Log(aiVsAi
                ? "AI vs AI test started. Step turns manually or enable autoplay."
                : "Your turn. Choose an action, or advance through the phases.");
        }

        public DuelStateSnapshot CaptureState()
        {
            if (ResponseWindowOpen || ResponseDiscardChoiceOpen || SearchChoiceOpen ||
                pendingAiActions.Count > 0)
                throw new InvalidOperationException(
                    "Duel state can only be captured when no response, search, or AI action is pending.");

            Dictionary<CardInstance, string> instanceIds = new Dictionary<CardInstance, string>();
            List<CardInstance> allCards = new List<CardInstance>();
            RegisterPlayerCards(Human, instanceIds, allCards);
            RegisterPlayerCards(Opponent, instanceIds, allCards);

            DuelStateSnapshot snapshot = new DuelStateSnapshot
            {
                schemaVersion = 1,
                turnNumber = turnNumber,
                phase = Phase.ToString(),
                currentPlayer = CurrentPlayer == Human ? "human" : "opponent",
                isAiVsAi = IsAiVsAi,
                winner = Winner,
                temporarilyDisabledCardOwner = temporarilyDisabledCardOwner == Human
                    ? "human"
                    : temporarilyDisabledCardOwner == Opponent ? "opponent" : null,
                temporarilyDisabledCardName = temporarilyDisabledCardName,
                temporarilyDisabledCardTurn = temporarilyDisabledCardTurn,
                human = CapturePlayer(Human, instanceIds),
                opponent = CapturePlayer(Opponent, instanceIds),
                cards = new DuelCardStateSnapshot[allCards.Count],
                messages = messages.ToArray()
            };

            for (int i = 0; i < allCards.Count; i++)
            {
                CardInstance card = allCards[i];
                snapshot.cards[i] = new DuelCardStateSnapshot
                {
                    instanceId = instanceIds[card],
                    cardId = card.Definition.CardId,
                    faceUp = card.FaceUp,
                    attackPosition = card.AttackPosition,
                    hasAttacked = card.HasAttacked,
                    hasChangedPositionThisTurn = card.HasChangedPositionThisTurn,
                    effectUsedThisTurn = card.EffectUsedThisTurn,
                    attackBonus = card.AttackBonus,
                    permanentAttackBonus = card.PermanentAttackBonus,
                    summonedTurnNumber = card.SummonedTurnNumber,
                    spellCounters = card.SpellCounters,
                    destroyedOnFieldTurn = card.DestroyedOnFieldTurn,
                    effectDurationTurnsRemaining = card.EffectDurationTurnsRemaining,
                    fusionMaterialIds = card.FusionMaterials.Select(material => GetInstanceId(material, instanceIds)).ToArray(),
                    equippedMonsterId = card.EquippedMonster == null
                        ? null
                        : GetInstanceId(card.EquippedMonster, instanceIds)
                };
            }

            return snapshot;
        }

        public static DuelEngine RestoreState(DuelStateSnapshot snapshot)
        {
            Dictionary<string, CardInstance> cards = snapshot.cards.ToDictionary(
                item => item.instanceId,
                item =>
                {
                    CardDefinition definition = StarterCatalog.Find(item.cardId);
                    CardInstance card = new CardInstance(definition, item.faceUp, item.attackPosition)
                    {
                        HasAttacked = item.hasAttacked,
                        HasChangedPositionThisTurn = item.hasChangedPositionThisTurn,
                        EffectUsedThisTurn = item.effectUsedThisTurn,
                        AttackBonus = item.attackBonus,
                        PermanentAttackBonus = item.permanentAttackBonus,
                        SummonedTurnNumber = item.summonedTurnNumber,
                        SpellCounters = item.spellCounters,
                        DestroyedOnFieldTurn = item.destroyedOnFieldTurn,
                        EffectDurationTurnsRemaining = item.effectDurationTurnsRemaining
                    };
                    return card;
                },
                StringComparer.Ordinal);

            foreach (DuelCardStateSnapshot cardState in snapshot.cards)
            {
                CardInstance card = cards[cardState.instanceId];
                foreach (string materialId in cardState.fusionMaterialIds)
                    card.FusionMaterials.Add(cards[materialId]);
                if (!string.IsNullOrEmpty(cardState.equippedMonsterId))
                    card.EquippedMonster = cards[cardState.equippedMonsterId];
            }

            DuelEngine engine = new DuelEngine
            {
                Human = RestorePlayer(snapshot.human, cards),
                Opponent = RestorePlayer(snapshot.opponent, cards),
                turnNumber = snapshot.turnNumber,
                Phase = (DuelPhase)Enum.Parse(typeof(DuelPhase), snapshot.phase, false),
                IsAiVsAi = snapshot.isAiVsAi,
                Winner = snapshot.winner,
                temporarilyDisabledCardName = snapshot.temporarilyDisabledCardName,
                temporarilyDisabledCardTurn = snapshot.temporarilyDisabledCardTurn
            };
            engine.CurrentPlayer = snapshot.currentPlayer == "human" ? engine.Human : engine.Opponent;
            engine.temporarilyDisabledCardOwner = snapshot.temporarilyDisabledCardOwner == "human"
                ? engine.Human
                : snapshot.temporarilyDisabledCardOwner == "opponent" ? engine.Opponent : null;
            engine.messages.AddRange(snapshot.messages);
            return engine;
        }

        private static void RegisterPlayerCards(
            DuelPlayer player,
            Dictionary<CardInstance, string> instanceIds,
            List<CardInstance> allCards)
        {
            IEnumerable<CardInstance> playerCards = player.Deck
                .Concat(player.Hand)
                .Concat(player.Graveyard)
                .Concat(player.Banished)
                .Concat(player.MonsterZones)
                .Concat(player.SpellTrapZones)
                .Concat(new[] { player.FieldSpellZone })
                .Concat(player.ExtraDeck);
            foreach (CardInstance card in playerCards)
            {
                if (card == null || instanceIds.ContainsKey(card))
                    continue;
                string instanceId = "card-" + (allCards.Count + 1);
                instanceIds.Add(card, instanceId);
                allCards.Add(card);
            }
        }

        private static DuelPlayerStateSnapshot CapturePlayer(
            DuelPlayer player,
            Dictionary<CardInstance, string> instanceIds)
        {
            return new DuelPlayerStateSnapshot
            {
                name = player.Name,
                isAi = player.IsAi,
                lifePoints = player.LifePoints,
                deck = player.Deck.Select(card => GetInstanceId(card, instanceIds)).ToArray(),
                hand = player.Hand.Select(card => GetInstanceId(card, instanceIds)).ToArray(),
                graveyard = player.Graveyard.Select(card => GetInstanceId(card, instanceIds)).ToArray(),
                banished = player.Banished.Select(card => GetInstanceId(card, instanceIds)).ToArray(),
                monsterZones = player.MonsterZones.Select(card => GetInstanceId(card, instanceIds)).ToArray(),
                spellTrapZones = player.SpellTrapZones.Select(card => GetInstanceId(card, instanceIds)).ToArray(),
                fieldSpellZone = GetInstanceId(player.FieldSpellZone, instanceIds),
                extraDeck = player.ExtraDeck.Select(card => GetInstanceId(card, instanceIds)).ToArray(),
                normalSummonsThisTurn = player.NormalSummonsThisTurn,
                positionChangedThisTurn = player.PositionChangedThisTurn,
                summonsLockedThisTurn = player.SummonsLockedThisTurn,
                darkMagicCurtainUsedThisTurn = player.DarkMagicCurtainUsedThisTurn,
                attackTheMoonUsedThisTurn = player.AttackTheMoonUsedThisTurn,
                sanganEffectUsedThisTurn = player.SanganEffectUsedThisTurn,
                cannotAttackUntilTurn = player.CannotAttackUntilTurn,
                cannotChangePositionUntilTurn = player.CannotChangePositionUntilTurn,
                noAttackTurnsRemaining = player.NoAttackTurnsRemaining,
                positionChangesLockedTurnsRemaining = player.PositionChangesLockedTurnsRemaining
            };
        }

        private static DuelPlayer RestorePlayer(
            DuelPlayerStateSnapshot snapshot,
            Dictionary<string, CardInstance> cards)
        {
            DuelPlayer player = new DuelPlayer(snapshot.name, snapshot.isAi)
            {
                LifePoints = snapshot.lifePoints,
                NormalSummonsThisTurn = snapshot.normalSummonsThisTurn,
                PositionChangedThisTurn = snapshot.positionChangedThisTurn,
                SummonsLockedThisTurn = snapshot.summonsLockedThisTurn,
                DarkMagicCurtainUsedThisTurn = snapshot.darkMagicCurtainUsedThisTurn,
                AttackTheMoonUsedThisTurn = snapshot.attackTheMoonUsedThisTurn,
                SanganEffectUsedThisTurn = snapshot.sanganEffectUsedThisTurn,
                CannotAttackUntilTurn = snapshot.cannotAttackUntilTurn,
                CannotChangePositionUntilTurn = snapshot.cannotChangePositionUntilTurn,
                NoAttackTurnsRemaining = snapshot.noAttackTurnsRemaining,
                PositionChangesLockedTurnsRemaining = snapshot.positionChangesLockedTurnsRemaining,
                FieldSpellZone = GetCard(cards, snapshot.fieldSpellZone)
            };
            player.Deck.AddRange(snapshot.deck.Select(id => cards[id]));
            player.Hand.AddRange(snapshot.hand.Select(id => cards[id]));
            player.Graveyard.AddRange(snapshot.graveyard.Select(id => cards[id]));
            player.Banished.AddRange(snapshot.banished.Select(id => cards[id]));
            player.ExtraDeck.AddRange(snapshot.extraDeck.Select(id => cards[id]));
            for (int i = 0; i < player.MonsterZones.Length; i++)
            {
                player.MonsterZones[i] = GetCard(cards, snapshot.monsterZones[i]);
                player.SpellTrapZones[i] = GetCard(cards, snapshot.spellTrapZones[i]);
            }
            return player;
        }

        private static CardInstance GetCard(Dictionary<string, CardInstance> cards, string instanceId)
        {
            return string.IsNullOrEmpty(instanceId) ? null : cards[instanceId];
        }

        private static string GetInstanceId(
            CardInstance card,
            Dictionary<CardInstance, string> instanceIds)
        {
            if (card == null)
                return null;
            if (!instanceIds.TryGetValue(card, out string instanceId))
                throw new InvalidOperationException("A card reference points outside the duel state.");
            return instanceId;
        }

        public bool TryNormalSummon(CardInstance card, IReadOnlyList<CardInstance> tributes, bool setFaceDown = false)
        {
            if (!CanActInMainPhase())
                return Reject("Normal Summons are only available during your Main Phase.");
            if (card == null || !CurrentPlayer.Hand.Contains(card) || !card.Definition.CanNormalSummon)
                return Reject("Select a Main Deck monster that can be Normal Summoned.");
            if (card.Definition.Effect == CardEffect.Berserkion ||
                card.Definition.Effect == CardEffect.Valkyrion)
                return Reject("This monster must be Special Summoned using its card-specific materials.");
            if (CurrentPlayer.NormalSummonsThisTurn >= 1)
                return Reject("You have already used your Normal Summon this turn.");
            if (CurrentPlayer.SummonsLockedThisTurn)
                return Reject("A card effect prevents you from Summoning monsters this turn.");
            if (FindEmptyMonsterZone(CurrentPlayer) < 0 && !CurrentPlayer.MonsterZones.Any(material =>
                    material != null && tributes != null && tributes.Contains(material)))
                return Reject("Your Monster Zones are full.");

            int tributeCount = card.Definition.Level >= 7 ? 2 : card.Definition.Level >= 5 ? 1 : 0;
            List<CardInstance> selectedTributes = tributes == null ? new List<CardInstance>() : tributes.Distinct().ToList();
            if (selectedTributes.Count != tributeCount ||
                selectedTributes.Any(material => !CurrentPlayer.MonsterZones.Contains(material)))
                return Reject(tributeCount == 0
                    ? "This monster needs no Tributes. Clear your field selection first."
                    : "Select exactly " + tributeCount + " of your monsters as Tribute(s).");
            if (SearchChoiceOpen)
                return Reject("Resolve Sangan's Deck search before taking another action.");

            int zone = FindEmptyMonsterZone(CurrentPlayer);
            if (zone < 0 && selectedTributes.Count == 0)
                return Reject("Your Monster Zones are full.");
            foreach (CardInstance tribute in selectedTributes)
                SendMonsterToGraveyard(CurrentPlayer, tribute);
            if (zone < 0)
                zone = FindEmptyMonsterZone(CurrentPlayer);

            CurrentPlayer.Hand.Remove(card);
            card.FaceUp = !setFaceDown;
            card.AttackPosition = !setFaceDown;
            CurrentPlayer.MonsterZones[zone] = card;
            card.SummonedTurnNumber = turnNumber;
            CurrentPlayer.NormalSummonsThisTurn++;
            Log(CurrentPlayer.Name + (setFaceDown ? " Set " : " Normal Summoned ") + card.Definition.DisplayName + ".");
            if (!setFaceDown)
                ResolveSummonTriggers(CurrentPlayer, card, true);
            return true;
        }

        public bool TrySpecialSummonBoss(CardInstance card)
        {
            if (!CanActInMainPhase())
                return Reject("Special Summons are only available during your Main Phase.");
            if (card == null || !CurrentPlayer.Hand.Contains(card) ||
                (card.Definition.Effect != CardEffect.Berserkion && card.Definition.Effect != CardEffect.Valkyrion))
                return Reject("Select Berserkion or Valkyrion in your hand.");
            if (CurrentPlayer.SummonsLockedThisTurn)
                return Reject("A card effect prevents you from Summoning monsters this turn.");
            if (FindEmptyMonsterZone(CurrentPlayer) < 0)
                return Reject("Your Monster Zones are full.");

            bool berserkion = card.Definition.Effect == CardEffect.Berserkion;
            string[] materials = berserkion
                ? new[] { "Alpha The Electromagnet Warrior", "Beta The Electromagnet Warrior", "Gamma The Electromagnet Warrior" }
                : new[] { "Alpha The Magnet Warrior", "Beta The Magnet Warrior", "Gamma the Magnet Warrior" };
            List<CardInstance> chosen = new List<CardInstance>();
            foreach (string materialName in materials)
            {
                CardInstance material = (berserkion
                        ? CurrentPlayer.Hand.Concat(CurrentPlayer.Graveyard).Concat(CurrentPlayer.MonsterZones.Where(item => item != null && item.FaceUp))
                        : CurrentPlayer.Hand.Concat(CurrentPlayer.MonsterZones.Where(item => item != null)))
                    .FirstOrDefault(item => item.Definition.DisplayName == materialName && !chosen.Contains(item));
                if (material == null)
                    return Reject(card.Definition.DisplayName + " needs all three matching Magnet Warrior materials.");
                chosen.Add(material);
            }

            foreach (CardInstance material in chosen)
            {
                CurrentPlayer.Hand.Remove(material);
                CurrentPlayer.Graveyard.Remove(material);
                if (berserkion)
                {
                    RemoveFromMonsterZones(CurrentPlayer, material);
                    CurrentPlayer.Banished.Add(material);
                }
                else if (CurrentPlayer.MonsterZones.Contains(material))
                    SendMonsterToGraveyard(CurrentPlayer, material);
                else
                    CurrentPlayer.Graveyard.Add(material);
            }
            CurrentPlayer.Hand.Remove(card);
            card.FaceUp = true;
            card.AttackPosition = true;
            card.SummonedTurnNumber = turnNumber;
            CurrentPlayer.MonsterZones[FindEmptyMonsterZone(CurrentPlayer)] = card;
            Log(CurrentPlayer.Name + " Special Summoned " + card.Definition.DisplayName + ".");
            return true;
        }

        public bool TrySetBackrow(CardInstance card)
        {
            if (!CanActInMainPhase())
                return Reject("You can only Set cards during your Main Phase.");
            if (card == null || !CurrentPlayer.Hand.Contains(card) ||
                (card.Definition.Kind != CardKind.Spell && card.Definition.Kind != CardKind.Trap))
                return Reject("Select a Spell or Trap in your hand first.");
            if (card.Definition.Kind == CardKind.Spell && card.Definition.Subtype == CardSubtype.Field)
                return Reject("Activate Field Spells directly into the dedicated Field Spell Zone.");

            int slot = FindEmptySpellTrapZone(CurrentPlayer);
            if (slot < 0)
                return Reject("Your Spell & Trap Zones are full.");
            CurrentPlayer.Hand.Remove(card);
            card.FaceUp = false;
            CurrentPlayer.SpellTrapZones[slot] = card;
            Log(CurrentPlayer.Name + " Set a card.");
            return true;
        }

        private void ResolveSummonTriggers(DuelPlayer owner, CardInstance summoned, bool normalSummon)
        {
            if (summoned.EffectUsedThisTurn)
                return;
            if (IsCardEffectActivationRestricted(owner, summoned.Definition))
            {
                Log(owner.Name + " cannot activate " + summoned.Definition.DisplayName +
                    "'s effect for the rest of this turn.");
                return;
            }

            string id = summoned.Definition.CardId;
            if (id == "sdmy-en001")
            {
                AddDeckCardToHand(owner, card => card.Definition.DisplayName == "Berserkion the Electromagna Warrior",
                    summoned.Definition.DisplayName + " searched a Magna Warrior.");
                summoned.EffectUsedThisTurn = true;
            }
            else if (id == "sdmy-en002")
            {
                AddDeckCardToHand(owner, card => card.Definition.Level <= 4 &&
                    IsLowLevelMagnetWarrior(card) && card != summoned,
                    summoned.Definition.DisplayName + " searched a low-Level Magnet Warrior.");
                summoned.EffectUsedThisTurn = true;
            }
            else if (id == "sdmy-en003")
            {
                CardInstance magnet = owner.Hand.FirstOrDefault(card => card != summoned &&
                    card.Definition.Level <= 4 && IsLowLevelMagnetWarrior(card));
                int zone = FindEmptyMonsterZone(owner);
                if (magnet != null && zone >= 0)
                {
                    owner.Hand.Remove(magnet);
                    magnet.FaceUp = true;
                    magnet.AttackPosition = true;
                    magnet.SummonedTurnNumber = turnNumber;
                    owner.MonsterZones[zone] = magnet;
                    Log(owner.Name + " Special Summoned " + magnet.Definition.DisplayName + " from hand.");
                    summoned.EffectUsedThisTurn = true;
                    ResolveSummonTriggers(owner, magnet, false);
                }
            }
            else if (id == "sdmy-en015" && normalSummon &&
                     owner.MonsterZones.Any(card => card != null && card.Definition.DisplayName == "Queen's Knight"))
            {
                int zone = FindEmptyMonsterZone(owner);
                CardInstance jack = owner.Deck.FirstOrDefault(card => card.Definition.DisplayName == "Jack's Knight");
                if (jack != null && zone >= 0)
                {
                    owner.Deck.Remove(jack);
                    owner.MonsterZones[zone] = jack;
                    jack.FaceUp = true;
                    jack.AttackPosition = true;
                    jack.SummonedTurnNumber = turnNumber;
                    summoned.EffectUsedThisTurn = true;
                    Log(owner.Name + " Special Summoned Jack's Knight from the Deck.");
                }
            }
            else if (id == "sdmy-en016")
            {
                AddDeckCardToHand(owner, card => card.Definition.DisplayName == "Gazelle the King of Mythical Beasts",
                    "Berfomet searched Gazelle.");
                summoned.EffectUsedThisTurn = true;
            }
        }

        private void AddDeckCardToHand(DuelPlayer owner, Predicate<CardInstance> predicate, string message)
        {
            CardInstance card = owner.Deck.FirstOrDefault(item => predicate(item));
            if (card == null)
                return;
            owner.Deck.Remove(card);
            owner.Hand.Add(card);
            Log(owner.Name + " " + message);
        }

        private static bool IsMagnetWarrior(CardInstance card)
        {
            return card != null && (card.Definition.DisplayName.IndexOf("Magnet Warrior", StringComparison.OrdinalIgnoreCase) >= 0 ||
                card.Definition.DisplayName.IndexOf("Electromagnet Warrior", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsLowLevelMagnetWarrior(CardInstance card)
        {
            return IsMagnetWarrior(card) && card.Definition.Level <= 4;
        }

        private static bool IsRockMonster(CardInstance card)
        {
            return card != null && (IsMagnetWarrior(card) ||
                card.Definition.DisplayName == "Giant Soldier of Stone" ||
                card.Definition.DisplayName == "Berserkion the Electromagna Warrior" ||
                card.Definition.DisplayName == "Valkyrion the Magna Warrior");
        }

        public bool TryActivate(CardInstance card, CardInstance target = null)
        {
            if (card == null)
                return Reject("Select a card to activate.");

            bool fromHand = CurrentPlayer.Hand.Contains(card);
            bool fromSetTrap = CurrentPlayer.SpellTrapZones.Contains(card) && !card.FaceUp &&
                               card.Definition.Kind == CardKind.Trap;
            bool quickPlayDuringBattle = fromHand && card.Definition.Kind == CardKind.Spell &&
                                         card.Definition.Subtype == CardSubtype.QuickPlay &&
                                         CurrentPlayer == Human &&
                                         (Phase == DuelPhase.Battle || Phase == DuelPhase.End);
            if (!CanActInMainPhase() && !quickPlayDuringBattle)
                return Reject("You can only activate cards during your Main Phase, except a Quick-Play Spell during your turn.");

            bool monsterEffect = CurrentPlayer.MonsterZones.Contains(card) && card.FaceUp &&
                                card.Definition.IsMonster && card.Definition.Effect != CardEffect.None;
            bool handSpell = fromHand && card.Definition.Kind == CardKind.Spell;
            if (!handSpell && !monsterEffect && !fromSetTrap)
            {
                return Reject("Select a Spell in your hand or a face-up monster with an effect.");
            }
            if (monsterEffect && card.EffectUsedThisTurn)
                return Reject("That monster's effect has already been used this turn.");
            if (IsCardEffectActivationRestricted(CurrentPlayer, card.Definition))
                return Reject("You cannot activate " + temporarilyDisabledCardName +
                    " or its effects for the rest of this turn.");

            if (monsterEffect && (card.Definition.Effect == CardEffect.SearchMagnet ||
                                  card.Definition.Effect == CardEffect.KnightsCombo))
                return Reject("This effect resolves when its monster is Summoned.");
            if (card.Definition.Effect == CardEffect.None)
                return Reject("This card's effect is not implemented in the prototype.");
            if (card.Definition.Effect == CardEffect.BoostStrongest &&
                target == null && !CurrentPlayer.MonsterZones.Any(monster => monster != null))
                return Reject("You need a monster on the field to receive the ATK boost.");
            if (card.Definition.Effect == CardEffect.DestroyWeakest &&
                !GetOpponent(CurrentPlayer).MonsterZones.Any(monster => monster != null))
                return Reject("The opponent has no monster to destroy.");
            if (card.Definition.Effect == CardEffect.DestroySpellTrap &&
                (target == null || !IsOnSpellTrapField(target)))
                return Reject("Select a Spell or Trap on the field to destroy.");
            if (card.Definition.Effect == CardEffect.ReviveWeakest &&
                (FindEmptyMonsterZone(CurrentPlayer) < 0 ||
                 !CurrentPlayer.Graveyard.Any(monster => monster.Definition.IsMonster)))
                return Reject("You need an open Monster Zone and a monster in your Graveyard.");
            if (card.Definition.Effect == CardEffect.DarkMagicAttack &&
                !CurrentPlayer.MonsterZones.Any(monster => monster != null &&
                    monster.Definition.DisplayName == "Dark Magician"))
                return Reject("Dark Magic Attack needs Dark Magician on your field.");
            if (card.Definition.Effect == CardEffect.DarkMagicCurtain &&
                (CurrentPlayer.DarkMagicCurtainUsedThisTurn || CurrentPlayer.LifePoints < 2 ||
                 FindEmptyMonsterZone(CurrentPlayer) < 0 ||
                 !CurrentPlayer.Deck.Any(monster => monster.Definition.DisplayName == "Dark Magician")))
                return Reject("Dark Magic Curtain needs available Life Points, an open Monster Zone, and Dark Magician in your Deck.");
            if (card.Definition.Effect == CardEffect.DarkMagicInheritance &&
                (CurrentPlayer.Graveyard.Count(item => item.Definition.Kind == CardKind.Spell) < 2 ||
                 !CurrentPlayer.Deck.Any(item => item.Definition.Kind == CardKind.Spell &&
                     item.Definition.DisplayName != "Dark Magic Inheritance")))
                return Reject("Dark Magic Inheritance needs two Spells in your Graveyard and a Spell in your Deck.");
            if (card.Definition.Effect == CardEffect.MysticBox &&
                (target == null || !GetOpponent(CurrentPlayer).MonsterZones.Contains(target) ||
                 !CurrentPlayer.MonsterZones.Any(monster => monster != null)))
                return Reject("Select an opposing monster and control a monster to exchange.");
            if (card.Definition.Effect == CardEffect.SpellShatteringArrow &&
                !GetOpponent(CurrentPlayer).SpellTrapZones.Any(item => item != null && item.FaceUp &&
                    item.Definition.Kind == CardKind.Spell) &&
                (GetOpponent(CurrentPlayer).FieldSpellZone == null ||
                 GetOpponent(CurrentPlayer).FieldSpellZone.Definition.Kind != CardKind.Spell))
                return Reject("The opponent controls no face-up Spells.");
            if (card.Definition.Effect == CardEffect.RockBombardment &&
                !CurrentPlayer.Deck.Any(IsRockMonster))
                return Reject("There is no Rock monster in your Deck to send to the Graveyard.");
            if (card.Definition.Effect == CardEffect.MagnetConversion &&
                !CurrentPlayer.Graveyard.Any(IsLowLevelMagnetWarrior))
                return Reject("There are no eligible Magnet Warriors in your Graveyard.");
            if (card.Definition.Effect == CardEffect.Berserkion &&
                (!GetOpponent(CurrentPlayer).MonsterZones.Any(item => item != null) ||
                 !CurrentPlayer.Graveyard.Any(IsLowLevelMagnetWarrior)))
                return Reject("Berserkion needs a Magnet Warrior in your Graveyard and an opposing monster.");
            if (card.Definition.Effect == CardEffect.DeFusion &&
                (target == null || !CurrentPlayer.MonsterZones.Contains(target) ||
                 target.Definition.Kind != CardKind.Fusion))
                return Reject("Select one of your Fusion Monsters to return to the Extra Deck.");
            if (handSpell &&
                (card.Definition.Subtype == CardSubtype.Continuous ||
                 card.Definition.Subtype == CardSubtype.Equip) &&
                FindEmptySpellTrapZone(CurrentPlayer) < 0)
                return Reject("Your Spell & Trap Zones are full.");

            CardEffect counterType = handSpell ? CardEffect.CounterSpell :
                monsterEffect ? CardEffect.CounterMonsterEffect : CardEffect.None;
            if (counterType != CardEffect.None)
            {
                DuelPlayer owner = CurrentPlayer;
                Action cancelActivation = () =>
                {
                    ConsumeActivatedCard(owner, card, handSpell, monsterEffect, fromSetTrap);
                    Log(card.Definition.DisplayName + "'s effect was negated.");
                };
                ResponseAttempt response = OpenResponseWindow(GetOpponent(owner), counterType,
                    card, () => ResolveActivatedEffect(owner, card, target, handSpell, monsterEffect, fromSetTrap), cancelActivation);
                if (response == ResponseAttempt.Waiting)
                    return true;
                if (response == ResponseAttempt.Cancelled)
                {
                    cancelActivation();
                    return true;
                }
            }

            return ResolveActivatedEffect(CurrentPlayer, card, target, handSpell, monsterEffect, fromSetTrap);
        }

        private bool ResolveActivatedEffect(
            DuelPlayer owner, CardInstance card, CardInstance target, bool handSpell, bool monsterEffect,
            bool fromSetTrap = false)
        {
            if (handSpell && card.Definition.Subtype == CardSubtype.Equip &&
                card.Definition.Effect == CardEffect.BoostStrongest)
            {
                CardInstance equipped = target != null && owner.MonsterZones.Contains(target)
                    ? target
                    : owner.MonsterZones.Where(monster => monster != null)
                        .OrderByDescending(monster => monster.CurrentAttack).FirstOrDefault();
                if (equipped == null)
                    return Reject("You need a monster to equip this Spell to.");
                equipped.PermanentAttackBonus += card.Definition.EffectValue;
                card.EquippedMonster = equipped;
                PlaceActiveSpell(owner, card);
            }
            else
            {
                bool fieldBoostWaitingForMonster = handSpell &&
                    card.Definition.Subtype == CardSubtype.Field &&
                    card.Definition.Effect == CardEffect.BoostStrongest &&
                    !owner.MonsterZones.Any(monster => monster != null);
                if (!fieldBoostWaitingForMonster && !ApplyEffect(owner, card.Definition, target, card))
                    return false;
                if (handSpell && (card.Definition.Subtype == CardSubtype.Continuous ||
                                  card.Definition.Subtype == CardSubtype.Field))
                    PlaceActiveSpell(owner, card);
                else
                    ConsumeActivatedCard(owner, card, handSpell, monsterEffect, fromSetTrap);
            }
            if (monsterEffect)
                card.EffectUsedThisTurn = true;
            Log(owner.Name + " activated " + card.Definition.DisplayName + ".");
            return true;
        }

        private void PlaceActiveSpell(DuelPlayer owner, CardInstance card)
        {
            owner.Hand.Remove(card);
            card.FaceUp = true;
            if (card.Definition.Subtype == CardSubtype.Field)
            {
                if (owner.FieldSpellZone != null)
                {
                    owner.FieldSpellZone.FaceUp = false;
                    owner.Graveyard.Add(owner.FieldSpellZone);
                    Log(owner.Name + "'s previous Field Spell was sent to the Graveyard.");
                }
                owner.FieldSpellZone = card;
                return;
            }
            int slot = FindEmptySpellTrapZone(owner);
            owner.SpellTrapZones[slot] = card;
        }

        private void ConsumeActivatedCard(
            DuelPlayer owner, CardInstance card, bool handSpell, bool monsterEffect, bool fromSetTrap = false)
        {
            if (handSpell)
            {
                owner.Hand.Remove(card);
                owner.Graveyard.Add(card);
            }
            if (fromSetTrap)
            {
                RemoveFromSpellTrapZones(owner, card);
                owner.Graveyard.Add(card);
            }
            if (monsterEffect)
                card.EffectUsedThisTurn = true;
        }

        public bool TrySynchroSummon(CardDefinition synchro, IReadOnlyList<CardInstance> materials)
        {
            if (!CanActInMainPhase())
                return Reject("Synchro Summons are only available during your Main Phase.");
            if (CurrentPlayer.SummonsLockedThisTurn)
                return Reject("A card effect prevents you from Special Summoning this turn.");
            if (synchro == null || synchro.Kind != CardKind.Synchro ||
                !CurrentPlayer.ExtraDeck.Any(card => card.Definition == synchro))
                return Reject("Select a Synchro Monster from your Extra Deck.");
            List<CardInstance> chosen = materials == null ? new List<CardInstance>() : materials.Distinct().ToList();
            if (!IsValidSynchroMaterial(synchro, chosen, CurrentPlayer))
                return Reject("Choose face-up monsters with one Tuner and an exact total Level of " + synchro.Level + ".");

            foreach (CardInstance material in chosen)
                SendMonsterToGraveyard(CurrentPlayer, material);
            int zone = FindEmptyMonsterZone(CurrentPlayer);
            CardInstance summoned = new CardInstance(synchro) { SummonedTurnNumber = turnNumber };
            CurrentPlayer.MonsterZones[zone] = summoned;
            Log(CurrentPlayer.Name + " Synchro Summoned " + synchro.DisplayName + "!");
            return true;
        }

        public bool TryAttack(CardInstance attacker, CardInstance target)
        {
            if (SearchChoiceOpen || (!IsAiVsAi && CurrentPlayer != Human) || Phase != DuelPhase.Battle)
                return Reject("Attacks are only available during your Battle Phase.");
            bool attackStarted = ResolveAttack(CurrentPlayer, attacker, target);
            if (attackStarted)
                AttackDeclared?.Invoke(attacker, target);
            return attackStarted;
        }

        public bool TryChooseSearchCard(CardInstance card)
        {
            if (!SearchChoiceOpen || card == null ||
                !pendingSanganOwner.Deck.Contains(card) || !card.Definition.IsMonster ||
                !card.Definition.CanBeMainDeck ||
                card.Definition.Attack > 1500)
                return Reject("Choose a monster with 1500 or less ATK from the Deck.");

            DuelPlayer owner = pendingSanganOwner;
            owner.Deck.Remove(card);
            owner.Hand.Add(card);
            temporarilyDisabledCardName = card.Definition.DisplayName;
            temporarilyDisabledCardOwner = owner;
            temporarilyDisabledCardTurn = turnNumber;
            Log(owner.Name + " added " + card.Definition.DisplayName +
                " to hand with Sangan. That card's effects cannot be activated this turn.");
            pendingSanganOwner = null;
            pendingSanganSource = null;
            ProcessPendingAiActions();
            return true;
        }

        public bool TryChangePosition(CardInstance card)
        {
            if (!CanActInMainPhase())
                return Reject("You can only change a monster's position during your Main Phase.");
            if (card == null || !CurrentPlayer.MonsterZones.Contains(card) || !card.FaceUp)
                return Reject("Select one of your face-up monsters.");
            if (turnNumber <= CurrentPlayer.CannotChangePositionUntilTurn)
                return Reject("A card effect prevents your monsters from changing position.");
            if (CurrentPlayer.PositionChangesLockedTurnsRemaining > 0)
                return Reject("A card effect prevents your monsters from changing position.");
            if (card.HasAttacked || card.HasChangedPositionThisTurn || turnNumber <= card.SummonedTurnNumber)
                return Reject("This monster attacked, was summoned this turn, or already changed position this turn.");
            bool wasInDefense = !card.AttackPosition;
            card.AttackPosition = !card.AttackPosition;
            card.HasChangedPositionThisTurn = true;
            CurrentPlayer.PositionChangedThisTurn = true;
            Log(card.Definition.DisplayName + " changed to " + (card.AttackPosition ? "Attack" : "Defense") + " Position.");
            if (wasInDefense && IsRockMonster(card) && !CurrentPlayer.AttackTheMoonUsedThisTurn &&
                CurrentPlayer.SpellTrapZones.Concat(new[] { CurrentPlayer.FieldSpellZone })
                    .Any(spell => spell != null && spell.FaceUp &&
                        spell.Definition.Effect == CardEffect.AttackTheMoon))
            {
                DuelPlayer opponent = GetOpponent(CurrentPlayer);
                CardInstance backrow = opponent.SpellTrapZones.FirstOrDefault(item => item != null);
                if (backrow != null)
                {
                    RemoveFromSpellTrapZones(opponent, backrow);
                    opponent.Graveyard.Add(backrow);
                    CurrentPlayer.AttackTheMoonUsedThisTurn = true;
                    Log("Attack the Moon! destroyed " + backrow.Definition.DisplayName + ".");
                }
            }
            return true;
        }

        public bool CanFlipSummon(CardInstance card)
        {
            return CanActInMainPhase() && card != null &&
                   CurrentPlayer.MonsterZones.Contains(card) && !card.FaceUp &&
                   turnNumber > CurrentPlayer.CannotChangePositionUntilTurn &&
                   CurrentPlayer.PositionChangesLockedTurnsRemaining <= 0 &&
                   !card.HasChangedPositionThisTurn && turnNumber > card.SummonedTurnNumber;
        }

        public bool TryFlipSummon(CardInstance card)
        {
            if (!CanActInMainPhase())
                return Reject("Flip Summons are only available during your Main Phase.");
            if (card == null || !CurrentPlayer.MonsterZones.Contains(card) || card.FaceUp)
                return Reject("Select one of your face-down Defense Position monsters.");
            if (turnNumber <= card.SummonedTurnNumber)
                return Reject("A monster cannot be Flip Summoned on the turn it was Set or Summoned.");
            if (card.HasChangedPositionThisTurn)
                return Reject("That monster has already changed position this turn.");

            card.FaceUp = true;
            card.AttackPosition = true;
            card.HasChangedPositionThisTurn = true;
            CurrentPlayer.PositionChangedThisTurn = true;
            Log(CurrentPlayer.Name + " Flip Summoned " + card.Definition.DisplayName + " in Attack Position.");
            return true;
        }

        public void AdvancePhase()
        {
            if (!IsHumanActionWindow || ResponseWindowOpen || SearchChoiceOpen)
                return;
            AdvanceToNextPhase();
        }

        public bool AdvanceToPhase(DuelPhase target)
        {
            if (!IsHumanActionWindow || ResponseWindowOpen || target == DuelPhase.GameOver)
                return false;
            if (Phase == target)
            {
                if (target == DuelPhase.End)
                {
                    RunOpponentTurn();
                    return true;
                }
                return false;
            }

            int currentIndex = GetTurnPhaseIndex(Phase);
            int targetIndex = GetTurnPhaseIndex(target);
            if (currentIndex < 0 || targetIndex <= currentIndex)
                return false;

            while (Phase != target && Phase != DuelPhase.GameOver)
                AdvanceToNextPhase();
            return true;
        }

        private void AdvanceToNextPhase()
        {
            switch (Phase)
            {
                case DuelPhase.Draw:
                    SetPhase(DuelPhase.Standby);
                    Log("Standby Phase.");
                    break;
                case DuelPhase.Standby:
                    SetPhase(DuelPhase.MainOne);
                    Log("Main Phase 1. Choose an action.");
                    break;
                case DuelPhase.MainOne:
                    SetPhase(DuelPhase.Battle);
                    Log("Battle Phase. Select one of your monsters, then a target (or Direct Attack).");
                    break;
                case DuelPhase.Battle:
                    SetPhase(DuelPhase.MainTwo);
                    Log("Main Phase 2. You may play remaining cards before ending your turn.");
                    break;
                case DuelPhase.MainTwo:
                    SetPhase(DuelPhase.End);
                    Log("End Phase. Advance again to pass the turn.");
                    break;
                case DuelPhase.End:
                    RunOpponentTurn();
                    break;
            }
        }

        private static int GetTurnPhaseIndex(DuelPhase phase)
        {
            switch (phase)
            {
                case DuelPhase.Draw: return 0;
                case DuelPhase.Standby: return 1;
                case DuelPhase.MainOne: return 2;
                case DuelPhase.Battle: return 3;
                case DuelPhase.MainTwo: return 4;
                case DuelPhase.End: return 5;
                default: return -1;
            }
        }

        public bool StepAiTurn()
        {
            if (!IsAiVsAi || Phase == DuelPhase.GameOver)
                return false;

            DuelPlayer activePlayer = CurrentPlayer;
            RunAiTurnActions(activePlayer);
            return true;
        }

        public bool TryRespondWithTrap(CardInstance trap)
        {
            if (!ResponseWindowOpen || IsAiVsAi || trap == null)
                return Reject("There is no response window for that card.");

            if (ResponseDiscardChoiceOpen)
            {
                if (!Human.Hand.Contains(trap) || trap == responseEffectAwaitingDiscard)
                    return Reject("Select a different card from your hand to discard.");

                CardInstance response = responseEffectAwaitingDiscard;
                Action negateActivation = pendingResponseTrap;
                Human.Hand.Remove(trap);
                Human.Graveyard.Add(trap);
                Log(Human.Name + " discarded " + trap.Definition.DisplayName + " for Dark Paladin.");
                response.EffectUsedThisTurn = true;
                ClearResponseWindow();
                if (negateActivation != null)
                    negateActivation();
                ProcessPendingAiActions();
                return true;
            }

            if (!GetResponseCards(Human, pendingResponseType).Contains(trap))
                return Reject("That card cannot respond to the current event.");
            if (IsDarkPaladin(trap))
            {
                if (Human.Hand.Count == 0)
                    return Reject("Dark Paladin needs a card in your hand to discard.");
                responseEffectAwaitingDiscard = trap;
                Log("Dark Paladin is waiting for you to select a card to discard.");
                return true;
            }
            if (!Human.SpellTrapZones.Contains(trap) || trap.FaceUp)
                return Reject("That card is not a Set Trap.");

            Action trapResolution = pendingResponseTrap;
            Action counteredResolution = pendingResponseCountered;
            CardInstance sourceCard = pendingResponseSourceCard;
            CardEffect responseType = pendingResponseType;
            ClearResponseWindow();
            ActivateResponseTrap(Human, trap);

            bool responseNegated = ResolveCounterTrap(Opponent, CardEffect.CounterTrap);
            if (responseNegated)
            {
                if (!IsAiVsAi && GetResponseTraps(Human, CardEffect.CounterTrap).Count > 0)
                    OpenHumanChainWindow(counteredResolution, trapResolution, counteredResolution);
                else
                    counteredResolution();
            }
            else if (responseType == CardEffect.StopAttack)
            {
                if (!ResolveAttackResponse(Human, trap, sourceCard))
                    Log(trap.Definition.DisplayName + " stopped the declared attack.");
                Log("Chain Link " + chainLinkCount + " resolved.");
            }
            else
            {
                trapResolution();
                Log("Chain Link " + chainLinkCount + " resolved.");
            }

            if (!ResponseWindowOpen)
                ProcessPendingAiActions();
            return true;
        }

        public bool PassResponseWindow()
        {
            if (!ResponseWindowOpen || IsAiVsAi)
                return Reject("There is no response window to pass.");

            responseEffectAwaitingDiscard = null;
            Action resolvePending = pendingResponsePass;
            ClearResponseWindow();
            Log("Response window passed.");
            resolvePending();
            ProcessPendingAiActions();
            return true;
        }

        public bool CanSynchroSummon(CardDefinition synchro, IReadOnlyList<CardInstance> materials, DuelPlayer owner)
        {
            return owner != null && !owner.SummonsLockedThisTurn &&
                   synchro != null && synchro.Kind == CardKind.Synchro &&
                   owner.ExtraDeck.Any(card => card.Definition == synchro) &&
                   IsValidSynchroMaterial(synchro, materials == null ? new List<CardInstance>() : materials.ToList(), owner);
        }

        public bool CanFusionSummon(CardDefinition fusion, IReadOnlyList<CardInstance> materials, DuelPlayer owner)
        {
            if (owner == null || owner.SummonsLockedThisTurn || fusion == null || fusion.Kind != CardKind.Fusion ||
                !owner.ExtraDeck.Any(card => card.Definition == fusion) ||
                !owner.Hand.Any(card => card.Definition.CardId == "sdmy-en031"))
                return false;
            List<CardInstance> chosen = materials == null ? new List<CardInstance>() : materials.Distinct().ToList();
            int openZonesAfterMaterials = owner.MonsterZones.Count(card => card == null) +
                chosen.Count(card => owner.MonsterZones.Contains(card));
            return openZonesAfterMaterials > 0 && chosen.Count > 0 && chosen.All(card => card != null &&
                (owner.Hand.Contains(card) || owner.MonsterZones.Contains(card))) &&
                HasFusionMaterials(fusion, chosen);
        }

        public bool TryFusionSummon(CardDefinition fusion, IReadOnlyList<CardInstance> materials)
        {
            if (!CanActInMainPhase())
                return Reject("Fusion Summons are only available during your Main Phase.");
            if (!CanFusionSummon(fusion, materials, CurrentPlayer))
                return Reject("Select a valid Fusion Monster, Polymerization, and its required materials.");

            List<CardInstance> chosen = materials.Distinct().ToList();
            int summonZone = FindEmptyMonsterZone(CurrentPlayer);
            if (summonZone < 0)
                summonZone = Array.FindIndex(CurrentPlayer.MonsterZones, chosen.Contains);
            if (summonZone < 0)
                return Reject("Fusion Summoning needs an open Monster Zone after using its materials.");
            CardInstance polymerization = CurrentPlayer.Hand.First(card => card.Definition.CardId == "sdmy-en031");
            CardInstance extraEntry = CurrentPlayer.ExtraDeck.First(card => card.Definition == fusion);
            foreach (CardInstance material in chosen)
            {
                CurrentPlayer.Hand.Remove(material);
                if (CurrentPlayer.MonsterZones.Contains(material))
                    SendMonsterToGraveyard(CurrentPlayer, material);
                else
                    CurrentPlayer.Graveyard.Add(material);
            }
            CurrentPlayer.Hand.Remove(polymerization);
            CurrentPlayer.Graveyard.Add(polymerization);
            CurrentPlayer.ExtraDeck.Remove(extraEntry);
            CardInstance summoned = new CardInstance(fusion) { SummonedTurnNumber = turnNumber };
            summoned.FusionMaterials.AddRange(chosen);
            CurrentPlayer.MonsterZones[summonZone] = summoned;
            Log(CurrentPlayer.Name + " Fusion Summoned " + fusion.DisplayName + ".");
            return true;
        }

        private static bool HasFusionMaterials(CardDefinition fusion, IReadOnlyList<CardInstance> materials)
        {
            List<string> required;
            switch (fusion.DisplayName)
            {
                case "Imperion Magnum the Superconductive Battlebot":
                    required = new List<string>
                    {
                        "Valkyrion the Magna Warrior",
                        "Berserkion the Electromagna Warrior"
                    };
                    break;
                case "Arcana Knight Joker":
                    required = new List<string> { "Queen's Knight", "Jack's Knight", "King's Knight" };
                    break;
                case "Dark Paladin":
                    required = new List<string> { "Dark Magician", "Buster Blader" };
                    break;
                case "Chimera the Flying Mythical Beast":
                    required = new List<string> { "Gazelle the King of Mythical Beasts", "Berfomet" };
                    break;
                case "Buster Blader, the Dragon Destroyer Swordsman":
                    CardInstance buster = materials.FirstOrDefault(card => card.Definition.DisplayName == "Buster Blader");
                    return buster != null && materials.Count(card => card != buster &&
                        card.Definition.DisplayName.IndexOf("Dragon", StringComparison.OrdinalIgnoreCase) >= 0) >= 1 &&
                        materials.Count == 2;
                default:
                    return false;
            }
            return materials.Count == required.Count &&
                   required.All(name => materials.Any(card => card.Definition.DisplayName == name));
        }

        private DuelPlayer MakePlayer(string name, bool isAi, IReadOnlyList<CardDefinition> definitions)
        {
            DuelPlayer player = new DuelPlayer(name, isAi);
            foreach (CardDefinition definition in definitions)
            {
                if (definition == null)
                    continue;
                if (definition.IsExtraDeckCard)
                    player.ExtraDeck.Add(new CardInstance(definition));
                else
                    player.Deck.Add(new CardInstance(definition));
            }
            Shuffle(player.Deck);
            return player;
        }

        private void DrawInitialHand(DuelPlayer player)
        {
            for (int i = 0; i < 5; i++)
            {
                if (player.Deck.Count == 0)
                    return;
                player.Hand.Add(player.Deck[0]);
                player.Deck.RemoveAt(0);
            }
        }

        private void BeginTurn(DuelPlayer player)
        {
            CurrentPlayer = player;
            turnNumber++;
            SetPhase(DuelPhase.Draw);
            player.NormalSummonsThisTurn = 0;
            player.PositionChangedThisTurn = false;
            player.SummonsLockedThisTurn = false;
            player.DarkMagicCurtainUsedThisTurn = false;
            player.AttackTheMoonUsedThisTurn = false;
            Human.SanganEffectUsedThisTurn = false;
            Opponent.SanganEffectUsedThisTurn = false;
            foreach (CardInstance monster in player.MonsterZones.Where(card => card != null))
            {
                monster.HasAttacked = false;
                monster.HasChangedPositionThisTurn = false;
                monster.EffectUsedThisTurn = false;
                monster.AttackBonus = 0;
            }
            IEnumerable<CardInstance> activeSpells = player.SpellTrapZones.Concat(new[] { player.FieldSpellZone })
                .Where(card => card != null && card.FaceUp && card.Definition.Kind == CardKind.Spell &&
                    (card.Definition.Subtype == CardSubtype.Continuous || card.Definition.Subtype == CardSubtype.Field));
            foreach (CardInstance spell in activeSpells)
            {
                if (player.MonsterZones.Any(monster => monster != null) &&
                    spell.Definition.Effect != CardEffect.SwordsOfRevealingLight &&
                    spell.Definition.Effect != CardEffect.SwordsOfConcealingLight)
                    ApplyEffect(player, spell.Definition, null);
            }
            DrawCard(player);
            if (Phase != DuelPhase.GameOver)
                Log(player.Name + " Turn " + turnNumber + " — Draw Phase.");
        }

        private void EndTurn(DuelPlayer endingPlayer)
        {
            if (endingPlayer.NoAttackTurnsRemaining > 0)
                endingPlayer.NoAttackTurnsRemaining--;
            if (endingPlayer.PositionChangesLockedTurnsRemaining > 0)
                endingPlayer.PositionChangesLockedTurnsRemaining--;

            foreach (DuelPlayer owner in new[] { Human, Opponent })
            {
                if (GetOpponent(owner) != endingPlayer)
                    continue;
                IEnumerable<CardInstance> activeSpells = owner.SpellTrapZones
                    .Concat(new[] { owner.FieldSpellZone })
                    .Where(card => card != null && card.FaceUp && card.EffectDurationTurnsRemaining > 0);
                foreach (CardInstance spell in activeSpells.ToList())
                {
                    spell.EffectDurationTurnsRemaining--;
                    if (spell.EffectDurationTurnsRemaining > 0)
                        continue;
                    RemoveFromSpellTrapZones(owner, spell);
                    if (owner.FieldSpellZone == spell)
                        owner.FieldSpellZone = null;
                    owner.Graveyard.Add(spell);
                    Log(spell.Definition.DisplayName + " was sent to the Graveyard after its duration ended.");
                }
            }
        }

        private void RunOpponentTurn()
        {
            EndTurn(CurrentPlayer);
            BeginTurn(Opponent);
            if (Phase == DuelPhase.GameOver)
                return;

            RunAiTurnActions(Opponent);
        }

        private void RunAiTurnActions(DuelPlayer actor)
        {
            if (Phase == DuelPhase.GameOver || ResponseWindowOpen)
                return;

            if (Phase == DuelPhase.Draw)
            {
                SetPhase(DuelPhase.Standby);
                Log(actor.Name + " entered the Standby Phase.");
            }
            if (Phase == DuelPhase.Standby)
            {
                SetPhase(DuelPhase.MainOne);
                Log(actor.Name + " entered Main Phase 1.");
            }
            pendingAiTurnActor = actor;
            pendingAiActions.Clear();
            pendingAiActionIndex = 0;
            pendingAiActions.Add(() => TryAiNormalSummon(actor));
            pendingAiActions.Add(() => TryAiBossSummon(actor));
            pendingAiActions.Add(() => TryAiFusion(actor));
            pendingAiActions.Add(() => EnqueueSpellActions(actor));
            pendingAiActions.Add(() => EnqueueMonsterEffectActions(actor));
            pendingAiActions.Add(() => TryAiSynchro(actor));
            pendingAiActions.Add(() => EnqueueTrapSetActions(actor));
            pendingAiActions.Add(() => EnqueueBattleActions(actor));
            pendingAiActions.Add(FinishAiTurn);
            ProcessPendingAiActions();
        }

        private void TryAiNormalSummon(DuelPlayer actor)
        {
            if (actor.NormalSummonsThisTurn != 0)
                return;
            DuelPlayer opponent = GetOpponent(actor);
            List<CardInstance> field = actor.MonsterZones.Where(card => card != null).ToList();
            CardInstance summon = actor.Hand
                .Where(card => card.Definition.CanNormalSummon)
                .Where(card => card.Definition.Level < 5 ||
                               field.Count >= (card.Definition.Level >= 7 ? 2 : 1))
                .OrderByDescending(card => AiSummonScore(actor, card, field, opponent))
                .FirstOrDefault();
            if (summon == null)
                return;

            int tributeCount = summon.Definition.Level >= 7 ? 2 : summon.Definition.Level >= 5 ? 1 : 0;
            List<CardInstance> tributes = field
                .OrderBy(card => AiFieldMonsterValue(card, opponent))
                .Take(tributeCount).ToList();
            TryNormalSummon(summon, tributes);
        }

        private void TryAiBossSummon(DuelPlayer actor)
        {
            foreach (CardInstance boss in actor.Hand.Where(card =>
                         card.Definition.Effect == CardEffect.Berserkion ||
                         card.Definition.Effect == CardEffect.Valkyrion).ToList())
            {
                bool berserkion = boss.Definition.Effect == CardEffect.Berserkion;
                string[] required = berserkion
                    ? new[] { "Alpha The Electromagnet Warrior", "Beta The Electromagnet Warrior", "Gamma The Electromagnet Warrior" }
                    : new[] { "Alpha The Magnet Warrior", "Beta The Magnet Warrior", "Gamma the Magnet Warrior" };
                IEnumerable<CardInstance> materials = berserkion
                    ? actor.Hand.Concat(actor.Graveyard).Concat(actor.MonsterZones.Where(card => card != null && card.FaceUp))
                    : actor.Hand.Concat(actor.MonsterZones.Where(card => card != null));
                bool hasMaterials = required.All(name => materials.Any(card =>
                    card != boss && card.Definition.DisplayName == name));
                if (hasMaterials && actor.MonsterZones.Any(card => card == null) && TrySpecialSummonBoss(boss))
                    return;
            }
        }

        private void TryAiFusion(DuelPlayer actor)
        {
            if (!actor.Hand.Any(card => card.Definition.CardId == "sdmy-en031"))
                return;

            List<CardInstance> available = actor.Hand
                .Concat(actor.MonsterZones.Where(card => card != null && card.FaceUp))
                .Where(card => card.Definition.CardId != "sdmy-en031")
                .ToList();
            foreach (CardDefinition fusion in actor.ExtraDeck
                         .Where(card => card.Definition.Kind == CardKind.Fusion)
                         .Select(card => card.Definition).Distinct()
                         .OrderByDescending(card => card.Attack))
            {
                List<CardInstance> candidates = available
                    .Where(card => IsPotentialFusionMaterial(fusion, card)).ToList();
                for (int mask = 1; mask < (1 << candidates.Count); mask++)
                {
                    List<CardInstance> materials = new List<CardInstance>();
                    for (int bit = 0; bit < candidates.Count; bit++)
                    {
                        if ((mask & (1 << bit)) != 0)
                            materials.Add(candidates[bit]);
                    }
                    if (CanFusionSummon(fusion, materials, actor))
                    {
                        TryFusionSummon(fusion, materials);
                        return;
                    }
                }
            }
        }

        private static bool IsPotentialFusionMaterial(CardDefinition fusion, CardInstance card)
        {
            switch (fusion.DisplayName)
            {
                case "Imperion Magnum the Superconductive Battlebot":
                    return card.Definition.DisplayName == "Valkyrion the Magna Warrior" ||
                           card.Definition.DisplayName == "Berserkion the Electromagna Warrior";
                case "Arcana Knight Joker":
                    return card.Definition.DisplayName == "Queen's Knight" ||
                           card.Definition.DisplayName == "Jack's Knight" ||
                           card.Definition.DisplayName == "King's Knight";
                case "Dark Paladin":
                    return card.Definition.DisplayName == "Dark Magician" ||
                           card.Definition.DisplayName == "Buster Blader";
                case "Chimera the Flying Mythical Beast":
                    return card.Definition.DisplayName == "Gazelle the King of Mythical Beasts" ||
                           card.Definition.DisplayName == "Berfomet";
                case "Buster Blader, the Dragon Destroyer Swordsman":
                    return card.Definition.DisplayName == "Buster Blader" ||
                           card.Definition.DisplayName.IndexOf("Dragon", StringComparison.OrdinalIgnoreCase) >= 0;
                default:
                    return false;
            }
        }

        private double AiSummonScore(
            DuelPlayer actor,
            CardInstance card,
            IReadOnlyList<CardInstance> field,
            DuelPlayer opponent)
        {
            double score = card.Definition.Attack + card.Definition.Defense * 0.2;
            if (card.Definition.IsTuner)
                score += 180;
            if (card.Definition.Effect != CardEffect.None)
                score += 100;
            if (card.Definition.Level >= 5)
                score -= 350;

            if (CanEnableSynchro(actor, card, field))
                score += 700;

            if (opponent.MonsterZones.Any(monster => monster != null))
                score += card.Definition.Attack > opponent.MonsterZones.Where(monster => monster != null)
                    .Max(monster => monster.AttackPosition ? monster.CurrentAttack : monster.Definition.Defense)
                    ? 120 : 0;
            return score;
        }

        private static bool CanEnableSynchro(
            DuelPlayer actor,
            CardInstance candidate,
            IReadOnlyList<CardInstance> field)
        {
            foreach (CardInstance extra in actor.ExtraDeck.Where(card => card.Definition.Kind == CardKind.Synchro))
            {
                for (int mask = 0; mask < (1 << field.Count); mask++)
                {
                    int level = candidate.Definition.Level;
                    int tunerCount = candidate.Definition.IsTuner ? 1 : 0;
                    bool hasNonTuner = !candidate.Definition.IsTuner;
                    for (int bit = 0; bit < field.Count; bit++)
                    {
                        if ((mask & (1 << bit)) == 0)
                            continue;
                        CardInstance material = field[bit];
                        level += material.Definition.Level;
                        if (material.Definition.IsTuner)
                            tunerCount++;
                        else
                            hasNonTuner = true;
                    }

                    if (level == extra.Definition.Level && tunerCount == 1 && hasNonTuner)
                        return true;
                }
            }
            return false;
        }

        private static double AiFieldMonsterValue(CardInstance card, DuelPlayer opponent)
        {
            double value = card.CurrentAttack + card.Definition.Defense * 0.15;
            if (card.Definition.IsTuner)
                value += 250;
            if (card.Definition.Effect != CardEffect.None)
                value += 150;
            if (opponent.MonsterZones.Any(monster => monster != null) &&
                card.CurrentAttack > opponent.MonsterZones.Where(monster => monster != null)
                    .Max(monster => monster.AttackPosition ? monster.CurrentAttack : monster.Definition.Defense))
                value += 200;
            return value;
        }

        private void EnqueueSpellActions(DuelPlayer actor)
        {
            InsertPendingActions(actor.Hand.Where(card => card.Definition.Kind == CardKind.Spell &&
                    AiEffectPriority(card.Definition, actor) > 0)
                .OrderByDescending(card => AiEffectPriority(card.Definition, actor))
                .Select(card => (Action)(() => TryActivateAiSpell(card, actor))).ToList());
        }

        private void TryActivateAiSpell(CardInstance card, DuelPlayer actor)
        {
            CardInstance target = null;
            if (card.Definition.Effect == CardEffect.DestroySpellTrap)
            {
                DuelPlayer opponent = GetOpponent(actor);
                target = opponent.SpellTrapZones.FirstOrDefault(item => item != null) ??
                    opponent.FieldSpellZone ??
                    actor.SpellTrapZones.FirstOrDefault(item => item != null) ??
                    actor.FieldSpellZone;
            }
            TryActivate(card, target);
        }

        private void EnqueueTrapSetActions(DuelPlayer actor)
        {
            InsertPendingActions(actor.Hand.Where(card => card.Definition.Kind == CardKind.Trap &&
                    (card.Definition.Effect == CardEffect.StopAttack ||
                     card.Definition.Effect == CardEffect.CounterSpell ||
                     card.Definition.Effect == CardEffect.CounterMonsterEffect ||
                     card.Definition.Effect == CardEffect.CounterTrap))
                .Select(card => (Action)(() => TrySetBackrow(card))).ToList());
        }

        private void EnqueueMonsterEffectActions(DuelPlayer actor)
        {
            InsertPendingActions(actor.MonsterZones.Where(card => card != null && card.FaceUp &&
                    AiEffectPriority(card.Definition, actor) > 0)
                .OrderByDescending(card => AiEffectPriority(card.Definition, actor))
                .Select(card => (Action)(() =>
                {
                    if (actor.MonsterZones.Contains(card) && !card.EffectUsedThisTurn)
                        TryActivate(card);
                })).ToList());
        }

        private int AiEffectPriority(CardDefinition definition, DuelPlayer owner)
        {
            DuelPlayer opponent = GetOpponent(owner);
            switch (definition.Effect)
            {
                case CardEffect.Berserkion:
                    return opponent.MonsterZones.Any(card => card != null) &&
                           owner.Graveyard.Any(IsLowLevelMagnetWarrior) ? 950 : 0;
                case CardEffect.AttackTheMoon:
                    return owner.MonsterZones.Any(IsRockMonster) &&
                           opponent.SpellTrapZones.Any(card => card != null) ? 350 : 0;
                case CardEffect.Valkyrion:
                    return owner.MonsterZones.Count(card => card == null) >= 3 &&
                           new[] { "Alpha The Magnet Warrior", "Beta The Magnet Warrior", "Gamma the Magnet Warrior" }
                               .All(name => owner.Graveyard.Any(card => card.Definition.DisplayName == name))
                        ? 700 : 0;
                case CardEffect.DrawTwo: return owner.Deck.Count >= 2 ? 1000 : 0;
                case CardEffect.DrawOne: return owner.Deck.Count > 0 ? 900 : 0;
                case CardEffect.DestroyWeakest: return opponent.MonsterZones.Any(card => card != null) ? 850 : 0;
                case CardEffect.DestroySpellTrap:
                    return opponent.SpellTrapZones.Any(card => card != null) || opponent.FieldSpellZone != null
                        ? 850
                        : 0;
                case CardEffect.ReviveWeakest:
                    return owner.MonsterZones.Any(card => card == null) &&
                           owner.Graveyard.Any(card => card.Definition.IsMonster) ? 800 : 0;
                case CardEffect.BurnOpponent:
                    return definition.EffectValue > 0 ? 600 + definition.EffectValue : 0;
                case CardEffect.RestoreLife:
                    return owner.LifePoints <= 8000 - definition.EffectValue ? 500 + definition.EffectValue : 0;
                case CardEffect.BoostStrongest:
                    return owner.MonsterZones.Any(card => card != null) ? 400 + definition.EffectValue : 0;
                default: return 0;
            }
        }

        private void EnqueueBattleActions(DuelPlayer actor)
        {
            SetPhase(DuelPhase.Battle);
            if (!CanAttackThisTurn)
            {
                Log("No attacks are allowed during the first turn of the duel.");
                return;
            }
            if (actor.NoAttackTurnsRemaining > 0)
            {
                Log(actor.Name + " cannot attack this turn.");
                return;
            }
            InsertPendingActions(actor.MonsterZones.Where(card => card != null && card.FaceUp && card.AttackPosition)
                .Select(attacker => (Action)(() =>
                {
                    if (!actor.MonsterZones.Contains(attacker) || attacker.HasAttacked)
                        return;
                    DuelPlayer defender = GetOpponent(actor);
                    CardInstance target = SelectAiAttackTarget(defender, attacker);
                    if (target == null && defender.MonsterZones.Any(card => card != null))
                        return;
                    ResolveAttack(actor, attacker, target);
                })).ToList());
        }

        private static CardInstance SelectAiAttackTarget(DuelPlayer defender, CardInstance attacker)
        {
            CardInstance bestTarget = null;
            int bestScore = 0;
            foreach (CardInstance target in defender.MonsterZones.Where(card => card != null))
            {
                int targetPower = target.AttackPosition ? target.CurrentAttack : target.Definition.Defense;
                int score;
                if (attacker.CurrentAttack > targetPower)
                {
                    score = 1000 + (target.AttackPosition ? attacker.CurrentAttack - targetPower : 0) +
                            target.Definition.Attack / 4;
                }
                else if (attacker.CurrentAttack == targetPower && target.AttackPosition)
                    score = target.CurrentAttack >= attacker.CurrentAttack ? 200 : 100;
                else if (attacker.CurrentAttack < targetPower && target.AttackPosition)
                    score = -100 - (targetPower - attacker.CurrentAttack);
                else
                    score = 0;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestTarget = target;
                }
            }

            if (bestTarget == null && defender.MonsterZones.All(card => card == null))
                return null;
            return bestTarget;
        }

        private void InsertPendingActions(List<Action> actions)
        {
            if (actions.Count > 0)
                pendingAiActions.InsertRange(pendingAiActionIndex, actions);
        }

        private void ProcessPendingAiActions()
        {
            while (!ResponseWindowOpen && !SearchChoiceOpen && Phase != DuelPhase.GameOver &&
                   pendingAiActionIndex < pendingAiActions.Count)
            {
                Action action = pendingAiActions[pendingAiActionIndex++];
                action();
            }
        }

        private void FinishAiTurn()
        {
            if (Phase == DuelPhase.GameOver || pendingAiTurnActor == null)
                return;
            SetPhase(DuelPhase.End);
            DuelPlayer nextPlayer = GetOpponent(pendingAiTurnActor);
            EndTurn(pendingAiTurnActor);
            pendingAiTurnActor = null;
            pendingAiActions.Clear();
            pendingAiActionIndex = 0;
            BeginTurn(nextPlayer);
        }

        private void TryAiSynchro(DuelPlayer actor)
        {
            if (actor.SummonsLockedThisTurn)
                return;
            List<CardInstance> field = actor.MonsterZones.Where(card => card != null && card.FaceUp).ToList();
            if (field.Count < 2)
                return;

            CardDefinition bestSynchro = null;
            List<CardInstance> bestMaterials = null;
            double bestScore = 0;
            foreach (CardInstance extra in actor.ExtraDeck
                         .Where(card => card.Definition.Kind == CardKind.Synchro)
                         .OrderByDescending(card => card.Definition.Attack))
            {
                for (int mask = 1; mask < (1 << field.Count); mask++)
                {
                    List<CardInstance> materials = new List<CardInstance>();
                    for (int bit = 0; bit < field.Count; bit++)
                    {
                        if ((mask & (1 << bit)) != 0)
                            materials.Add(field[bit]);
                    }
                    if (!IsValidSynchroMaterial(extra.Definition, materials, actor))
                        continue;

                    double materialsValue = materials.Sum(card => AiFieldMonsterValue(card, GetOpponent(actor)));
                    double upgradeValue = extra.Definition.Attack + extra.Definition.Defense * 0.2;
                    if (upgradeValue <= materialsValue * 0.68)
                        continue;

                    double score = upgradeValue - materialsValue * 0.68;
                    if (extra.Definition.Attack > GetOpponent(actor).MonsterZones
                            .Where(card => card != null)
                            .Select(card => card.AttackPosition ? card.CurrentAttack : card.Definition.Defense)
                            .DefaultIfEmpty(0).Max())
                        score += 350;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestSynchro = extra.Definition;
                        bestMaterials = materials;
                    }
                }
            }

            if (bestSynchro != null && bestMaterials != null)
                TrySynchroSummon(bestSynchro, bestMaterials);
        }

        private bool ResolveAttack(DuelPlayer attackerOwner, CardInstance attacker, CardInstance target)
        {
            if (!CanAttackThisTurn)
                return Reject("The first player cannot attack on the first turn of the duel.");
            if (attackerOwner.NoAttackTurnsRemaining > 0)
                return Reject("A card effect prevents you from attacking this turn.");
            if (attacker == null || !attackerOwner.MonsterZones.Contains(attacker) || !attacker.FaceUp ||
                !attacker.AttackPosition || attacker.HasAttacked)
                return Reject("Select a face-up Attack Position monster that has not attacked.");

            DuelPlayer defender = attackerOwner == Human ? Opponent : Human;
            List<CardInstance> defenders = defender.MonsterZones.Where(card => card != null).ToList();
            if (target == null)
            {
                if (defenders.Count > 0)
                    return Reject("You must attack a monster while the opponent controls one.");
                attacker.HasAttacked = true;
                ResponseAttempt response = OpenResponseWindow(defender, CardEffect.StopAttack, attacker,
                    () => ResolveAttackCore(attackerOwner, attacker, null), () => { });
                if (response != ResponseAttempt.None)
                    return true;
                return ResolveAttackCore(attackerOwner, attacker, null);
            }
            if (!defender.MonsterZones.Contains(target))
                return Reject("Select an opposing monster as the attack target.");

            Log(attackerOwner.Name + "'s " + attacker.Definition.DisplayName + " declared an attack on " +
                target.Definition.DisplayName + ".");
            attacker.HasAttacked = true;
            ResponseAttempt attackResponse = OpenResponseWindow(defender, CardEffect.StopAttack, attacker,
                () => ResolveAttackCore(attackerOwner, attacker, target), () => { });
            if (attackResponse != ResponseAttempt.None)
                return true;
            return ResolveAttackCore(attackerOwner, attacker, target);
        }

        private bool ResolveAttackCore(DuelPlayer attackerOwner, CardInstance attacker, CardInstance target)
        {
            DuelPlayer defender = GetOpponent(attackerOwner);
            List<CardInstance> defenders = defender.MonsterZones.Where(card => card != null).ToList();
            if (target == null)
            {
                if (defenders.Count > 0)
                    return Reject("You must attack a monster while the opponent controls one.");
                defender.LifePoints -= attacker.CurrentAttack;
                Log(attacker.Definition.DisplayName + " attacked directly for " + attacker.CurrentAttack + " damage.");
                CheckWinner();
                return true;
            }
            if (!defender.MonsterZones.Contains(target))
                return Reject("Select an opposing monster as the attack target.");

            if (!target.FaceUp)
            {
                target.FaceUp = true;
                Log(target.Definition.DisplayName + " was flipped face-up in Defense Position.");
            }
            int targetPower = target.AttackPosition ? target.CurrentAttack : target.Definition.Defense;
            int difference = attacker.CurrentAttack - targetPower;
            if (attacker.CurrentAttack > targetPower)
            {
                SendMonsterToGraveyard(defender, target);
                if (target.AttackPosition)
                    defender.LifePoints -= difference;
                Log(attacker.Definition.DisplayName + " destroyed " + target.Definition.DisplayName + "." +
                    (target.AttackPosition ? " Opponent takes " + difference + " damage." : ""));
            }
            else if (attacker.CurrentAttack < targetPower)
            {
                if (target.AttackPosition)
                {
                    SendMonsterToGraveyard(attackerOwner, attacker);
                    attackerOwner.LifePoints -= -difference;
                    Log(attacker.Definition.DisplayName + " was destroyed. You take " + -difference + " damage.");
                }
                else
                {
                    attackerOwner.LifePoints -= -difference;
                    Log(attacker.Definition.DisplayName + " could not break through the Defense Position monster.");
                    Log(attackerOwner.Name + " takes " + -difference + " battle damage.");
                }
            }
            else
            {
                if (target.AttackPosition)
                {
                    SendMonsterToGraveyard(attackerOwner, attacker);
                    SendMonsterToGraveyard(defender, target);
                    Log("Both battling monsters were destroyed.");
                }
                else
                {
                    Log(attacker.Definition.DisplayName + " tied with the Defense Position monster. Neither was destroyed.");
                }
            }
            CheckWinner();
            return true;
        }

        private bool ApplyEffect(
            DuelPlayer owner, CardDefinition definition, CardInstance target, CardInstance source = null)
        {
            DuelPlayer opponent = owner == Human ? Opponent : Human;
            switch (definition.Effect)
            {
                case CardEffect.None:
                    return Reject("This card has no activated effect.");
                case CardEffect.DestroySpellTrap:
                    DuelPlayer targetOwner = GetSpellTrapOwner(target);
                    if (targetOwner == null)
                        return Reject("Select a Spell or Trap on the field to destroy.");
                    if (targetOwner.FieldSpellZone == target)
                        targetOwner.FieldSpellZone = null;
                    else
                        RemoveFromSpellTrapZones(targetOwner, target);
                    targetOwner.Graveyard.Add(target);
                    Log(target.Definition.DisplayName + " was destroyed by " + definition.DisplayName + ".");
                    return true;
                case CardEffect.DrawOne:
                    DrawCard(owner);
                    return true;
                case CardEffect.DrawTwo:
                    for (int i = 0; i < 2 && Phase != DuelPhase.GameOver; i++)
                        DrawCard(owner);
                    return true;
                case CardEffect.BurnOpponent:
                    opponent.LifePoints -= definition.EffectValue;
                    CheckWinner();
                    return true;
                case CardEffect.RestoreLife:
                    owner.LifePoints += definition.EffectValue;
                    return true;
                case CardEffect.BoostStrongest:
                    CardInstance boosted = target != null && owner.MonsterZones.Contains(target)
                        ? target
                        : owner.MonsterZones.Where(card => card != null).OrderByDescending(card => card.CurrentAttack).FirstOrDefault();
                    if (boosted == null)
                        return Reject("You need a monster on the field to receive the ATK boost.");
                    boosted.AttackBonus += definition.EffectValue;
                    return true;
                case CardEffect.DestroyWeakest:
                    CardInstance weakest = opponent.MonsterZones.Where(card => card != null)
                        .OrderBy(card => card.CurrentAttack).FirstOrDefault();
                    if (weakest == null)
                        return Reject("The opponent has no monster to destroy.");
                    SendMonsterToGraveyard(opponent, weakest);
                    CheckWinner();
                    return true;
                case CardEffect.ReviveWeakest:
                    CardInstance revived = owner.Graveyard
                        .Where(card => card.Definition.IsMonster &&
                            (definition.CardId != "sdmy-en024" || IsLowLevelMagnetWarrior(card)))
                        .OrderBy(card => card.CurrentAttack)
                        .FirstOrDefault();
                    int zone = FindEmptyMonsterZone(owner);
                    if (revived == null || zone < 0)
                        return Reject("You need an open Monster Zone and a monster in your Graveyard.");
                    owner.Graveyard.Remove(revived);
                    revived.FaceUp = true;
                    revived.AttackPosition = false;
                    revived.HasAttacked = false;
                    revived.EffectUsedThisTurn = false;
                    revived.AttackBonus = 0;
                    revived.SummonedTurnNumber = turnNumber;
                    owner.MonsterZones[zone] = revived;
                    Log(owner.Name + " returned " + revived.Definition.DisplayName + " from the Graveyard in Defense Position.");
                    return true;
                case CardEffect.DarkMagicAttack:
                    foreach (CardInstance backrow in opponent.SpellTrapZones.Where(item => item != null).ToList())
                    {
                        RemoveFromSpellTrapZones(opponent, backrow);
                        opponent.Graveyard.Add(backrow);
                        Log(backrow.Definition.DisplayName + " was destroyed by Dark Magic Attack.");
                    }
                    if (opponent.FieldSpellZone != null)
                    {
                        CardInstance field = opponent.FieldSpellZone;
                        opponent.FieldSpellZone = null;
                        opponent.Graveyard.Add(field);
                        Log(field.Definition.DisplayName + " was destroyed by Dark Magic Attack.");
                    }
                    return true;
                case CardEffect.DarkMagicCurtain:
                    owner.LifePoints /= 2;
                    CardInstance darkMagician = owner.Deck.FirstOrDefault(item =>
                        item.Definition.DisplayName == "Dark Magician");
                    if (darkMagician == null || FindEmptyMonsterZone(owner) < 0)
                        return Reject("Dark Magic Curtain can no longer summon Dark Magician.");
                    owner.Deck.Remove(darkMagician);
                    owner.MonsterZones[FindEmptyMonsterZone(owner)] = darkMagician;
                    darkMagician.FaceUp = true;
                    darkMagician.AttackPosition = true;
                    darkMagician.SummonedTurnNumber = turnNumber;
                    owner.DarkMagicCurtainUsedThisTurn = true;
                    owner.SummonsLockedThisTurn = true;
                    Log(owner.Name + " paid half their Life Points to Special Summon Dark Magician.");
                    return true;
                case CardEffect.DarkMagicInheritance:
                    CardInstance support = owner.Deck.FirstOrDefault(item =>
                        item.Definition.DisplayName == "Dark Magic Attack" ||
                        item.Definition.DisplayName == "Dark Magic Curtain");
                    if (support == null)
                        return Reject("No supported Dark Magician Spell is left in your Deck.");
                    List<CardInstance> spellsToBanish = owner.Graveyard
                        .Where(item => item.Definition.Kind == CardKind.Spell).Take(2).ToList();
                    if (spellsToBanish.Count < 2)
                        return Reject("Dark Magic Inheritance needs two Spells in your Graveyard.");
                    foreach (CardInstance spell in spellsToBanish)
                    {
                        owner.Graveyard.Remove(spell);
                        owner.Banished.Add(spell);
                    }
                    owner.Deck.Remove(support);
                    owner.Hand.Add(support);
                    Log(owner.Name + " banished two Spells and searched a Dark Magician Spell.");
                    return true;
                case CardEffect.MysticBox:
                    if (target == null || !opponent.MonsterZones.Contains(target))
                        return Reject("Select an opposing monster for Mystic Box.");
                    CardInstance transferred = owner.MonsterZones.FirstOrDefault(card => card != null);
                    if (transferred == null)
                        return Reject("You need a monster to give to your opponent.");
                    SendMonsterToGraveyard(opponent, target);
                    RemoveFromMonsterZones(owner, transferred);
                    int opponentZone = FindEmptyMonsterZone(opponent);
                    if (opponentZone < 0)
                        return Reject("The opponent has no open Monster Zone for the exchanged monster.");
                    opponent.MonsterZones[opponentZone] = transferred;
                    transferred.SummonedTurnNumber = turnNumber;
                    Log("Mystic Box destroyed " + target.Definition.DisplayName + " and gave control of " +
                        transferred.Definition.DisplayName + " to " + opponent.Name + ".");
                    return true;
                case CardEffect.SpellShatteringArrow:
                    List<CardInstance> spells = opponent.SpellTrapZones
                        .Where(item => item != null && item.FaceUp && item.Definition.Kind == CardKind.Spell)
                        .ToList();
                    if (opponent.FieldSpellZone != null && opponent.FieldSpellZone.FaceUp)
                        spells.Add(opponent.FieldSpellZone);
                    foreach (CardInstance spell in spells)
                    {
                        RemoveFromSpellTrapZones(opponent, spell);
                        if (opponent.FieldSpellZone == spell)
                            opponent.FieldSpellZone = null;
                        opponent.Graveyard.Add(spell);
                    }
                    opponent.LifePoints -= 500 * spells.Count;
                    if (spells.Count > 0)
                        Log("Spell Shattering Arrow destroyed " + spells.Count + " face-up Spell(s) and dealt " +
                            (500 * spells.Count) + " damage.");
                    CheckWinner();
                    return true;
                case CardEffect.SwordsOfRevealingLight:
                    if (source == null)
                        return Reject("Swords of Revealing Light could not be placed.");
                    foreach (CardInstance monster in opponent.MonsterZones.Where(item => item != null && !item.FaceUp))
                        monster.FaceUp = true;
                    opponent.NoAttackTurnsRemaining = Math.Max(opponent.NoAttackTurnsRemaining, 3);
                    source.EffectDurationTurnsRemaining = 3;
                    return true;
                case CardEffect.SwordsOfConcealingLight:
                    foreach (CardInstance monster in opponent.MonsterZones.Where(item => item != null))
                    {
                        monster.FaceUp = false;
                        monster.AttackPosition = false;
                    }
                    opponent.PositionChangesLockedTurnsRemaining =
                        Math.Max(opponent.PositionChangesLockedTurnsRemaining, 2);
                    if (source != null)
                        source.EffectDurationTurnsRemaining = 2;
                    return true;
                case CardEffect.MagnetConversion:
                    List<CardInstance> magnetWarriors = owner.Graveyard
                        .Where(IsLowLevelMagnetWarrior).Take(3).ToList();
                    foreach (CardInstance magnet in magnetWarriors)
                    {
                        owner.Graveyard.Remove(magnet);
                        owner.Hand.Add(magnet);
                    }
                    Log(owner.Name + " returned " + magnetWarriors.Count + " Magnet Warrior(s) to hand.");
                    return true;
                case CardEffect.RockBombardment:
                    CardInstance rock = owner.Deck.FirstOrDefault(IsRockMonster);
                    if (rock == null)
                        return Reject("No Rock monster remains in your Deck.");
                    owner.Deck.Remove(rock);
                    owner.Graveyard.Add(rock);
                    opponent.LifePoints -= 500;
                    Log(owner.Name + " sent a Rock monster to the Graveyard; " + opponent.Name + " took 500 damage.");
                    CheckWinner();
                    return true;
                case CardEffect.SearchMagnet:
                case CardEffect.KnightsCombo:
                    return Reject("This effect resolves when its monster is Summoned.");
                case CardEffect.AttackTheMoon:
                    return true;
                case CardEffect.Berserkion:
                    CardInstance materialToBanish = owner.Graveyard.FirstOrDefault(IsLowLevelMagnetWarrior);
                    CardInstance targetToDestroy = opponent.MonsterZones.Where(item => item != null)
                        .OrderBy(item => item.CurrentAttack).FirstOrDefault();
                    if (materialToBanish == null || targetToDestroy == null)
                        return Reject("Berserkion needs a Magnet Warrior in your Graveyard and an opposing monster.");
                    owner.Graveyard.Remove(materialToBanish);
                    owner.Banished.Add(materialToBanish);
                    SendMonsterToGraveyard(opponent, targetToDestroy);
                    CheckWinner();
                    Log("Berserkion banished a Magnet Warrior to destroy " + targetToDestroy.Definition.DisplayName + ".");
                    return true;
                case CardEffect.Valkyrion:
                    if (owner.MonsterZones.Count(item => item == null) + 1 < 3)
                        return Reject("Valkyrion needs three open Monster Zones to split into its materials.");
                    CardInstance valkyrion = owner.MonsterZones.FirstOrDefault(item =>
                        item != null && item.Definition.Effect == CardEffect.Valkyrion);
                    if (valkyrion == null)
                        return Reject("Valkyrion is no longer on the field.");
                    string[] pieces = { "Alpha The Magnet Warrior", "Beta The Magnet Warrior", "Gamma the Magnet Warrior" };
                    List<CardInstance> piecesInGraveyard = pieces.Select(name =>
                        owner.Graveyard.FirstOrDefault(item => item.Definition.DisplayName == name)).ToList();
                    if (piecesInGraveyard.Any(item => item == null))
                        return Reject("All three Magnet Warrior materials must be in your Graveyard.");
                    RemoveFromMonsterZones(owner, valkyrion);
                    owner.Graveyard.Add(valkyrion);
                    foreach (CardInstance piece in piecesInGraveyard)
                    {
                        owner.Graveyard.Remove(piece);
                        int pieceZone = FindEmptyMonsterZone(owner);
                        owner.MonsterZones[pieceZone] = piece;
                        piece.FaceUp = true;
                        piece.AttackPosition = true;
                        piece.SummonedTurnNumber = turnNumber;
                    }
                    return true;
                case CardEffect.FusionSummon:
                    return Reject("Select a Fusion Monster and its materials, then use Fusion Summon.");
                case CardEffect.DeFusion:
                    if (target == null || !owner.MonsterZones.Contains(target) ||
                        target.Definition.Kind != CardKind.Fusion)
                        return Reject("Select one of your Fusion Monsters to return to the Extra Deck.");
                    RemoveFromMonsterZones(owner, target);
                    owner.ExtraDeck.Add(new CardInstance(target.Definition));
                    List<CardInstance> returnMaterials = target.FusionMaterials
                        .Where(owner.Graveyard.Contains).ToList();
                    if (returnMaterials.Count == target.FusionMaterials.Count &&
                        returnMaterials.Count > 0 &&
                        owner.MonsterZones.Count(item => item == null) >= returnMaterials.Count)
                    {
                        foreach (CardInstance material in returnMaterials)
                        {
                            owner.Graveyard.Remove(material);
                            int returnZone = FindEmptyMonsterZone(owner);
                            owner.MonsterZones[returnZone] = material;
                            material.FaceUp = true;
                            material.AttackPosition = true;
                            material.SummonedTurnNumber = turnNumber;
                        }
                        Log("De-Fusion returned " + returnMaterials.Count + " Fusion Material(s) from the Graveyard.");
                    }
                    return true;
                case CardEffect.StopAttack:
                    return Reject("This Trap effect only resolves when an opponent declares an attack.");
                case CardEffect.CounterSpell:
                case CardEffect.CounterMonsterEffect:
                case CardEffect.CounterTrap:
                    return Reject("This Counter Trap only resolves in response to its matching effect.");
                default:
                    return Reject("This card effect is not implemented.");
            }
        }

        private ResponseAttempt OpenResponseWindow(
            DuelPlayer defender, CardEffect eventType, CardInstance source, Action onPass, Action onCancelled)
        {
            List<CardInstance> responses = GetResponseCards(defender, eventType);
            if (responses.Count == 0)
                return ResponseAttempt.None;

            chainLinkCount = 1;
            if (defender.IsAi || IsAiVsAi)
            {
                CardInstance aiTrap = responses[0];
                if (IsDarkPaladin(aiTrap))
                {
                    CardInstance discard = defender.Hand
                        .OrderBy(card => card.Definition.Attack + card.Definition.Level * 100)
                        .FirstOrDefault();
                    if (discard != null)
                    {
                        defender.Hand.Remove(discard);
                        defender.Graveyard.Add(discard);
                        aiTrap.EffectUsedThisTurn = true;
                        Log(defender.Name + " discarded a card for Dark Paladin.");
                        onCancelled();
                        return ResponseAttempt.Cancelled;
                    }
                }
                ActivateResponseTrap(defender, aiTrap);
                if (GetResponseTraps(Human, CardEffect.CounterTrap).Count > 0)
                {
                    OpenHumanChainWindow(
                        onCancelled,
                        onPass,
                        onCancelled,
                        CardEffect.CounterTrap,
                        aiTrap.Definition.DisplayName);
                    return ResponseAttempt.Waiting;
                }
                ResolveAttackResponse(defender, aiTrap, source);
                return ResponseAttempt.Cancelled;
            }

            OpenHumanChainWindow(onPass, onCancelled, onPass, eventType,
                source == null ? null : source.Definition.DisplayName, source);
            return ResponseAttempt.Waiting;
        }

        private void OpenHumanChainWindow(
            Action onPass,
            Action onTrapResolution,
            Action onTrapCountered,
            CardEffect eventType = CardEffect.CounterTrap,
            string source = null,
            CardInstance sourceCard = null)
        {
            pendingResponseType = eventType;
            pendingResponsePass = onPass;
            pendingResponseTrap = onTrapResolution;
            pendingResponseCountered = onTrapCountered;
            pendingResponseSource = source;
            pendingResponseSourceCard = sourceCard;
            Log("Chain Link " + (chainLinkCount + 1) + ": " + ResponsePrompt);
        }

        private void ClearResponseWindow()
        {
            pendingResponsePass = null;
            pendingResponseTrap = null;
            pendingResponseCountered = null;
            pendingResponseSource = null;
            pendingResponseSourceCard = null;
            responseEffectAwaitingDiscard = null;
            pendingResponseType = CardEffect.None;
        }

        private List<CardInstance> GetResponseCards(DuelPlayer player, CardEffect eventType)
        {
            List<CardInstance> responses = GetResponseTraps(player, eventType);
            if (player != null && eventType == CardEffect.CounterSpell &&
                player.Hand.Count > 0 &&
                player.MonsterZones.Any(card => card != null && card.FaceUp &&
                    card.Definition.DisplayName == "Dark Paladin" && !card.EffectUsedThisTurn))
            {
                responses.Add(player.MonsterZones.First(card => card != null && card.FaceUp &&
                    card.Definition.DisplayName == "Dark Paladin" && !card.EffectUsedThisTurn));
            }
            return responses;
        }

        private static bool IsDarkPaladin(CardInstance card)
        {
            return card != null && card.Definition.DisplayName == "Dark Paladin";
        }

        private List<CardInstance> GetResponseTraps(DuelPlayer player, CardEffect eventType)
        {
            if (player == null || eventType == CardEffect.None)
                return new List<CardInstance>();

            return player.SpellTrapZones.Where(card => card != null && !card.FaceUp &&
                card.Definition.Kind == CardKind.Trap &&
                (eventType == CardEffect.StopAttack
                    ? card.Definition.Effect == eventType ||
                      card.Definition.DisplayName == "Mirror Force" ||
                      card.Definition.DisplayName == "Magic Cylinder"
                    : card.Definition.Effect == eventType && card.Definition.Subtype == CardSubtype.Counter)).ToList();
        }

        private void ActivateResponseTrap(DuelPlayer player, CardInstance trap)
        {
            RemoveFromSpellTrapZones(player, trap);
            trap.FaceUp = true;
            player.Graveyard.Add(trap);
            chainLinkCount++;
            Log("Chain Link " + chainLinkCount + ": " + player.Name + " activated " + trap.Definition.DisplayName + ".");
        }

        private bool ResolveCounterTrap(DuelPlayer defender, CardEffect eventType)
        {
            CardInstance trap = GetResponseTraps(defender, eventType).FirstOrDefault();
            if (trap == null)
                return false;
            ActivateResponseTrap(defender, trap);
            Log("Chain Link: " + trap.Definition.DisplayName + " negated the Trap effect.");
            return true;
        }

        private bool ResolveAttackResponse(DuelPlayer defender, CardInstance response, CardInstance attacker)
        {
            if (response == null || attacker == null)
                return false;

            DuelPlayer attackerOwner = GetOpponent(defender);
            if (response.Definition.DisplayName == "Mirror Force")
            {
                List<CardInstance> targets = attackerOwner.MonsterZones
                    .Where(card => card != null && card.FaceUp && card.AttackPosition).ToList();
                foreach (CardInstance target in targets)
                {
                    SendMonsterToGraveyard(attackerOwner, target);
                    Log(target.Definition.DisplayName + " was destroyed by Mirror Force.");
                }
                return true;
            }

            if (response.Definition.DisplayName == "Magic Cylinder")
            {
                attackerOwner.LifePoints -= attacker.CurrentAttack;
                Log("Magic Cylinder stopped the attack and dealt " + attacker.CurrentAttack + " damage.");
                CheckWinner();
                return true;
            }

            if (response.Definition.Effect == CardEffect.StopAttack)
            {
                Log(response.Definition.DisplayName + " negated " + attacker.Definition.DisplayName + "'s attack.");
                return true;
            }

            return false;
        }

        private bool IsValidSynchroMaterial(CardDefinition synchro, IReadOnlyList<CardInstance> materials, DuelPlayer owner)
        {
            if (materials == null || materials.Count < 2 ||
                materials.Any(card => card == null || !card.FaceUp || !owner.MonsterZones.Contains(card)))
                return false;
            return materials.Sum(card => card.Definition.Level) == synchro.Level &&
                   materials.Count(card => card.Definition.IsTuner) == 1 &&
                   materials.Any(card => !card.Definition.IsTuner);
        }

        private DuelPlayer GetOpponent(DuelPlayer player)
        {
            return player == Human ? Opponent : Human;
        }

        private bool IsOnSpellTrapField(CardInstance card)
        {
            return GetSpellTrapOwner(card) != null;
        }

        private DuelPlayer GetSpellTrapOwner(CardInstance card)
        {
            if (card == null)
                return null;
            if (Human.SpellTrapZones.Contains(card) || Human.FieldSpellZone == card)
                return Human;
            if (Opponent.SpellTrapZones.Contains(card) || Opponent.FieldSpellZone == card)
                return Opponent;
            return null;
        }

        private void DrawCard(DuelPlayer player)
        {
            if (player.Deck.Count == 0)
            {
                player.LifePoints = 0;
                CheckWinner();
                return;
            }
            CardInstance card = player.Deck[0];
            player.Deck.RemoveAt(0);
            player.Hand.Add(card);
            CardDrawn?.Invoke(player, card);
            if (CurrentPlayer == player || CurrentPlayer == null)
                Log(player.Name + " drew a card.");
        }

        private bool CanActInMainPhase()
        {
            return !SearchChoiceOpen && CurrentPlayer != null &&
                   (Phase == DuelPhase.MainOne || Phase == DuelPhase.MainTwo);
        }

        private bool IsCardEffectActivationRestricted(DuelPlayer owner, CardDefinition definition)
        {
            return owner == temporarilyDisabledCardOwner &&
                   turnNumber == temporarilyDisabledCardTurn &&
                   definition != null &&
                   string.Equals(definition.DisplayName, temporarilyDisabledCardName,
                       StringComparison.Ordinal);
        }

        private int FindEmptyMonsterZone(DuelPlayer player)
        {
            for (int i = 0; i < player.MonsterZones.Length; i++)
            {
                if (player.MonsterZones[i] == null)
                    return i;
            }
            return -1;
        }

        private int FindEmptySpellTrapZone(DuelPlayer player)
        {
            for (int i = 0; i < player.SpellTrapZones.Length; i++)
            {
                if (player.SpellTrapZones[i] == null)
                    return i;
            }
            return -1;
        }

        private void RemoveFromMonsterZones(DuelPlayer player, CardInstance card)
        {
            for (int i = 0; i < player.MonsterZones.Length; i++)
            {
                if (player.MonsterZones[i] == card)
                    player.MonsterZones[i] = null;
            }
            foreach (CardInstance equip in player.SpellTrapZones.Where(item =>
                         item != null && item.EquippedMonster == card).ToList())
            {
                RemoveFromSpellTrapZones(player, equip);
                player.Graveyard.Add(equip);
                Log(player.Name + "'s " + equip.Definition.DisplayName + " was sent to the Graveyard.");
            }
        }

        private void SendMonsterToGraveyard(DuelPlayer owner, CardInstance monster)
        {
            if (owner == null || monster == null || !owner.MonsterZones.Contains(monster))
                return;

            RemoveFromMonsterZones(owner, monster);
            owner.Graveyard.Add(monster);
            if (monster.Definition.Effect != CardEffect.SanganSearch || owner.SanganEffectUsedThisTurn)
                return;

            owner.SanganEffectUsedThisTurn = true;
            if (IsCardEffectActivationRestricted(owner, monster.Definition))
            {
                Log(owner.Name + " cannot activate " + monster.Definition.DisplayName +
                    "'s effect for the rest of this turn.");
                return;
            }
            List<CardInstance> eligible = owner.Deck
                .Where(card => card.Definition.IsMonster && card.Definition.CanBeMainDeck &&
                    card.Definition.Attack <= 1500)
                .ToList();
            if (eligible.Count == 0)
            {
                Log(owner.Name + "'s Sangan was sent to the Graveyard, but no monster with 1500 or less ATK remains in the Deck.");
                return;
            }

            if (owner.IsAi)
            {
                CardInstance choice = eligible.OrderByDescending(card => card.Definition.Attack).First();
                owner.Deck.Remove(choice);
                owner.Hand.Add(choice);
                temporarilyDisabledCardName = choice.Definition.DisplayName;
                temporarilyDisabledCardOwner = owner;
                temporarilyDisabledCardTurn = turnNumber;
                Log(owner.Name + " added " + choice.Definition.DisplayName +
                    " to hand with Sangan. That card's effects cannot be activated this turn.");
                return;
            }

            pendingSanganOwner = owner;
            pendingSanganSource = monster;
            Log(owner.Name + " must choose a monster with 1500 or less ATK for Sangan.");
        }

        private void RemoveFromSpellTrapZones(DuelPlayer player, CardInstance card)
        {
            bool removed = false;
            for (int i = 0; i < player.SpellTrapZones.Length; i++)
            {
                if (player.SpellTrapZones[i] == card)
                {
                    player.SpellTrapZones[i] = null;
                    removed = true;
                    if (card.EquippedMonster != null)
                    {
                        card.EquippedMonster.PermanentAttackBonus -= card.Definition.EffectValue;
                        card.EquippedMonster = null;
                    }
                }
            }
            if (!removed)
                return;
            DuelPlayer affected = GetOpponent(player);
            if (card.Definition.Effect == CardEffect.SwordsOfRevealingLight &&
                !HasActiveDurationSpell(player, CardEffect.SwordsOfRevealingLight))
                affected.NoAttackTurnsRemaining = 0;
            if (card.Definition.Effect == CardEffect.SwordsOfConcealingLight &&
                !HasActiveDurationSpell(player, CardEffect.SwordsOfConcealingLight))
                affected.PositionChangesLockedTurnsRemaining = 0;
        }

        private static bool HasActiveDurationSpell(DuelPlayer owner, CardEffect effect)
        {
            return owner.SpellTrapZones.Concat(new[] { owner.FieldSpellZone })
                .Any(card => card != null && card.FaceUp && card.Definition.Effect == effect);
        }

        private void CheckWinner()
        {
            if (Phase == DuelPhase.GameOver)
                return;
            if (Human.LifePoints <= 0)
                EndMatch(Opponent.Name);
            else if (Opponent.LifePoints <= 0)
                EndMatch(Human.Name);
        }

        private void EndMatch(string winner)
        {
            Winner = winner;
            SetPhase(DuelPhase.GameOver);
            Log(winner + " wins the duel.");
        }

        private void SetPhase(DuelPhase phase)
        {
            if (Phase == phase)
                return;
            Phase = phase;
            PhaseChanged?.Invoke(phase);
        }

        private bool Reject(string reason)
        {
            Log(reason);
            return false;
        }

        private void Log(string message)
        {
            messages.Add(message);
        }

        private void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int other = random.Next(i + 1);
                T value = list[i];
                list[i] = list[other];
                list[other] = value;
            }
        }
    }
}
