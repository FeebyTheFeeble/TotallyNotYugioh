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
        public bool EffectsNegated { get; set; }
        public int AttackBonus { get; set; }
        public int PermanentAttackBonus { get; set; }
        public int SummonedTurnNumber { get; set; }
        public int SpellCounters { get; set; }
        public int DestroyedOnFieldTurn { get; set; }
        public int EffectDurationTurnsRemaining { get; set; }
        public int DestroyAtEndPhaseTurn { get; set; }
        public bool BanishWhenLeavingField { get; set; }
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
        private DuelPlayer pendingDeckChoiceOwner;
        private CardInstance pendingDeckChoiceSource;
        private Func<CardInstance, bool> pendingDeckChoiceFilter;
        private string pendingDeckChoicePrompt;
        private bool pendingDeckChoiceToGraveyard;
        private bool pendingDeckChoiceSanganRestriction;
        private DuelPlayer temporarilyDisabledCardOwner;
        private string temporarilyDisabledCardName;
        private int temporarilyDisabledCardTurn;

        public DuelPlayer Human { get; private set; }
        public DuelPlayer Opponent { get; private set; }
        public DuelPlayer CurrentPlayer { get; private set; }
        public DuelPhase Phase { get; private set; }
        public bool IsAiVsAi { get; private set; }
        public bool HasPendingAiActions => pendingAiActionIndex < pendingAiActions.Count;
        public event Action<DuelPhase> PhaseChanged;
        public event Action<CardInstance, CardInstance> AttackDeclared;
        public event Action<DuelPlayer, CardInstance> CardDrawn;
        public event Action<DuelPlayer, CardInstance, IReadOnlyList<CardInstance>> TributeSummoned;
        public bool CanAttackThisTurn => turnNumber > 1;
        public string AttackRestrictionMessage => CanAttackThisTurn ? "" :
            "The first player cannot attack during the first turn of the duel.";
        public int TurnNumber => turnNumber;
        public string Winner { get; private set; }
        public IReadOnlyList<string> Messages => messages;
        public bool SearchChoiceOpen => pendingDeckChoiceOwner != null;
        public string SearchChoicePrompt => SearchChoiceOpen
            ? pendingDeckChoicePrompt
            : "";
        public string SearchChoiceTitle => SearchChoiceOpen
            ? pendingDeckChoiceSource.Definition.DisplayName + " — CHOOSE A CARD"
            : "";
        public string SearchChoiceActionLabel => pendingDeckChoiceToGraveyard
            ? "SEND SELECTED CARD TO GRAVEYARD"
            : "ADD SELECTED CARD TO HAND";
        public IReadOnlyList<CardInstance> AvailableSearchCards => SearchChoiceOpen
            ? pendingDeckChoiceOwner.Deck.Where(pendingDeckChoiceFilter).ToList()
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
            string secondPlayerName = "CPU RIVAL",
            bool humanGoesFirst = true)
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
            ClearDeckChoice();
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
            DuelPlayer firstPlayer = humanGoesFirst ? Human : Opponent;
            BeginTurn(firstPlayer);
            if (firstPlayer == Opponent)
                RunAiTurnActions(Opponent);
            Log(aiVsAi
                ? "AI vs AI test started. Step turns manually or enable autoplay."
                : firstPlayer == Human
                    ? "Your turn. Choose an action, or advance through the phases."
                    : Opponent.Name + " goes first.");
        }

        public DuelStateSnapshot CaptureState()
        {
            if (Human == null || Opponent == null || CurrentPlayer == null)
                throw new InvalidOperationException("Start a duel before capturing its state.");
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

        internal static DuelEngine RestoreState(DuelStateSnapshot snapshot)
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

        public bool TryNormalSummon(
            CardInstance card,
            IReadOnlyList<CardInstance> tributes,
            bool setFaceDown = false,
            int preferredMonsterZone = -1)
        {
            if (!CanActInMainPhase())
                return Reject("Normal Summons are only available during your Main Phase.");
            if (card == null || !CurrentPlayer.Hand.Contains(card) || !card.Definition.CanNormalSummon)
                return Reject("Select a Main Deck monster that can be Normal Summoned.");
            if (card.Definition.Effect == CardEffect.Berserkion ||
                card.Definition.Effect == CardEffect.Valkyrion)
                return Reject("This monster must be Special Summoned using its card-specific materials.");
            if (CurrentPlayer.SummonsLockedThisTurn)
                return Reject("A card effect prevents you from Summoning monsters this turn.");
            bool siroccoNoTribute = card.Definition.Effect == CardEffect.BlackwingSiroccoBoost &&
                !CurrentPlayer.MonsterZones.Any(monster => monster != null) &&
                GetOpponent(CurrentPlayer).MonsterZones.Any(monster => monster != null);
            if (!setFaceDown && (tributes == null || tributes.Count == 0))
            {
                bool blackwingSpecialSummon = card.Definition.Effect == CardEffect.BlackwingSpecialSummon &&
                    CurrentPlayer.MonsterZones.Any(monster => monster != null && monster.FaceUp &&
                        IsBlackwing(monster.Definition));
                bool cyberDragonSpecialSummon = card.Definition.Effect == CardEffect.CyberDragonSummon &&
                    GetOpponent(CurrentPlayer).MonsterZones.Count(monster => monster != null) >
                    CurrentPlayer.MonsterZones.Count(monster => monster != null);
                if (blackwingSpecialSummon || cyberDragonSpecialSummon)
                {
                    int specialSummonZone = preferredMonsterZone >= 0
                        ? preferredMonsterZone
                        : FindEmptyMonsterZone(CurrentPlayer);
                    if (specialSummonZone < 0 ||
                        (specialSummonZone >= CurrentPlayer.MonsterZones.Length) ||
                        (CurrentPlayer.MonsterZones[specialSummonZone] != null))
                        return Reject("Your Monster Zones are full or the selected zone is occupied.");
                    PlaceSpecialSummon(CurrentPlayer, card, specialSummonZone, true);
                    OpenResponseWindow(GetOpponent(CurrentPlayer), CardEffect.DestroyAllMonsters, card,
                        () => { }, DestroyAllMonstersOnField);
                    return true;
                }
            }
            if (CurrentPlayer.NormalSummonsThisTurn >= 1)
                return Reject("You have already used your Normal Summon this turn.");
            if (FindEmptyMonsterZone(CurrentPlayer) < 0 && !CurrentPlayer.MonsterZones.Any(material =>
                    material != null && tributes != null && tributes.Contains(material)))
                return Reject("Your Monster Zones are full.");

            int tributeCount = siroccoNoTribute ? 0 :
                card.Definition.Level >= 7 ? 2 : card.Definition.Level >= 5 ? 1 : 0;
            List<CardInstance> selectedTributes = tributes == null ? new List<CardInstance>() : tributes.Distinct().ToList();
            if (selectedTributes.Count != tributeCount ||
                selectedTributes.Any(material => !CurrentPlayer.MonsterZones.Contains(material)))
                return Reject(tributeCount == 0
                    ? "This monster needs no Tributes. Clear your field selection first."
                    : "Select exactly " + tributeCount + " of your monsters as Tribute(s).");
            if (SearchChoiceOpen)
                return Reject("Resolve Sangan's Deck search before taking another action.");

            int zone = preferredMonsterZone;
            if (zone >= 0 && (zone >= CurrentPlayer.MonsterZones.Length ||
                              (CurrentPlayer.MonsterZones[zone] != null &&
                               !selectedTributes.Contains(CurrentPlayer.MonsterZones[zone]))))
                return Reject("That Monster Zone is occupied.");
            if (zone < 0)
                zone = FindEmptyMonsterZone(CurrentPlayer);
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
            if (!setFaceDown && selectedTributes.Count > 0)
                TributeSummoned?.Invoke(CurrentPlayer, card, selectedTributes);
            if (!setFaceDown)
            {
                ResolveSummonTriggers(CurrentPlayer, card, true);
                OpenResponseWindow(GetOpponent(CurrentPlayer), CardEffect.DestroyAllMonsters, card,
                    () => { }, DestroyAllMonstersOnField);
            }
            return true;
        }

        public bool TrySpecialSummonBoss(CardInstance card, int preferredMonsterZone = -1)
        {
            if (!CanActInMainPhase())
                return Reject("Special Summons are only available during your Main Phase.");
            if (card == null || !CurrentPlayer.Hand.Contains(card) ||
                (card.Definition.Effect != CardEffect.Berserkion && card.Definition.Effect != CardEffect.Valkyrion))
                return Reject("Select Berserkion or Valkyrion in your hand.");
            if (CurrentPlayer.SummonsLockedThisTurn)
                return Reject("A card effect prevents you from Summoning monsters this turn.");
            int summonZone = preferredMonsterZone;
            if (summonZone >= 0 &&
                (summonZone >= CurrentPlayer.MonsterZones.Length || CurrentPlayer.MonsterZones[summonZone] != null))
                return Reject("That Monster Zone is occupied.");
            if (summonZone < 0)
                summonZone = FindEmptyMonsterZone(CurrentPlayer);
            if (summonZone < 0)
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
            CurrentPlayer.MonsterZones[summonZone] = card;
            Log(CurrentPlayer.Name + " Special Summoned " + card.Definition.DisplayName + ".");
            return true;
        }

        public bool TryRitualSummon(CardInstance ritualMonster, IReadOnlyList<CardInstance> materials)
        {
            if (!CanActInMainPhase())
                return Reject("Ritual Summons are only available during your Main Phase.");
            if (CurrentPlayer.SummonsLockedThisTurn)
                return Reject("A card effect prevents you from Special Summoning this turn.");
            if (ritualMonster == null || !CurrentPlayer.Hand.Contains(ritualMonster) ||
                !ritualMonster.Definition.IsRitualMonster)
                return Reject("Select a Ritual Monster from your hand.");

            CardInstance ritualSpell = CurrentPlayer.Hand.FirstOrDefault(card =>
                card.Definition.Kind == CardKind.Spell && card.Definition.Subtype == CardSubtype.Ritual);
            if (ritualSpell == null)
                return Reject("You need a Ritual Spell in your hand.");

            List<CardInstance> chosen = materials == null
                ? new List<CardInstance>()
                : materials.Distinct().ToList();
            if (chosen.Count == 0 || chosen.Any(card => card == null || card == ritualMonster ||
                    (!CurrentPlayer.Hand.Contains(card) && !CurrentPlayer.MonsterZones.Contains(card)) ||
                    !card.Definition.IsMonster))
                return Reject("Select Ritual Materials from your hand or Monster Zones.");
            if (chosen.Sum(card => card.Definition.Level) < ritualMonster.Definition.Level)
                return Reject("Your Ritual Materials must have total Levels at least equal to the Ritual Monster.");

            int summonZone = FindEmptyMonsterZone(CurrentPlayer);
            if (summonZone < 0)
                summonZone = Array.FindIndex(CurrentPlayer.MonsterZones, chosen.Contains);
            if (summonZone < 0)
                return Reject("Ritual Summoning needs an open Monster Zone after using its materials.");

            foreach (CardInstance material in chosen)
            {
                CurrentPlayer.Hand.Remove(material);
                if (CurrentPlayer.MonsterZones.Contains(material))
                    SendMonsterToGraveyard(CurrentPlayer, material);
                else
                    CurrentPlayer.Graveyard.Add(material);
            }

            CurrentPlayer.Hand.Remove(ritualSpell);
            CurrentPlayer.Graveyard.Add(ritualSpell);
            CurrentPlayer.Hand.Remove(ritualMonster);
            ritualMonster.FaceUp = true;
            ritualMonster.AttackPosition = true;
            ritualMonster.SummonedTurnNumber = turnNumber;
            CurrentPlayer.MonsterZones[summonZone] = ritualMonster;
            Log(CurrentPlayer.Name + " Ritual Summoned " + ritualMonster.Definition.DisplayName + ".");
            ResolveSummonTriggers(CurrentPlayer, ritualMonster, false);
            return true;
        }

        public bool TrySetBackrow(CardInstance card, int preferredSpellTrapZone = -1)
        {
            if (!CanActInMainPhase())
                return Reject("You can only Set cards during your Main Phase.");
            if (card == null || !CurrentPlayer.Hand.Contains(card) ||
                (card.Definition.Kind != CardKind.Spell && card.Definition.Kind != CardKind.Trap))
                return Reject("Select a Spell or Trap in your hand first.");
            if (card.Definition.Kind == CardKind.Spell && card.Definition.Subtype == CardSubtype.Field)
                return Reject("Activate Field Spells directly into the dedicated Field Spell Zone.");

            int slot = preferredSpellTrapZone;
            if (slot >= 0 && (slot >= CurrentPlayer.SpellTrapZones.Length ||
                              CurrentPlayer.SpellTrapZones[slot] != null))
                return Reject("That Spell & Trap Zone is occupied.");
            if (slot < 0)
                slot = FindEmptySpellTrapZone(CurrentPlayer);
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

            switch (summoned.Definition.Effect)
            {
                case CardEffect.BlackwingRevive:
                    if (normalSummon)
                        SpecialSummonFromGraveyard(owner, owner.Graveyard.FirstOrDefault(item =>
                            IsBlackwing(item.Definition) && item.Definition.Level <= 4), false);
                    break;
                case CardEffect.DebrisDragonRevive:
                    if (normalSummon)
                    {
                        CardInstance debrisTarget = owner.Graveyard.FirstOrDefault(item =>
                            item.Definition.IsMonster && item.Definition.Attack <= 500);
                        if (SpecialSummonFromGraveyard(owner, debrisTarget, true) && debrisTarget != null)
                            debrisTarget.EffectsNegated = true;
                    }
                    break;
                case CardEffect.JunkSynchronRevive:
                    if (normalSummon)
                        SpecialSummonFromGraveyard(owner, owner.Graveyard.FirstOrDefault(item =>
                            item.Definition.IsMonster && item.Definition.Level <= 2), false);
                    break;
                case CardEffect.SynchronExplorer:
                    if (normalSummon)
                        SpecialSummonFromGraveyard(owner, owner.Graveyard.FirstOrDefault(item =>
                            item.Definition.DisplayName.IndexOf("Synchron", StringComparison.OrdinalIgnoreCase) >= 0),
                            true);
                    break;
                case CardEffect.FormulaSynchronDraw:
                    DrawCard(owner);
                    break;
                case CardEffect.AncientFairyDragon:
                    DrawCard(owner);
                    break;
                case CardEffect.JunkWarriorBoost:
                    summoned.AttackBonus += owner.MonsterZones
                        .Where(item => item != null && item != summoned && item.Definition.Level <= 2)
                        .Sum(item => item.CurrentAttack);
                    break;
                case CardEffect.LifeStreamSetLifePoints:
                    owner.LifePoints = 4000;
                    break;
                case CardEffect.BlackRoseDragon:
                    foreach (CardInstance fieldCard in GetAllFieldCards())
                        DestroyFieldCard(fieldCard);
                    CheckWinner();
                    break;
                case CardEffect.BlackwingSilverwind:
                    foreach (CardInstance monster in Human.MonsterZones.Concat(Opponent.MonsterZones)
                                 .Where(item => item != null && item != summoned && item.FaceUp &&
                                                item.Definition.Defense < summoned.CurrentAttack)
                                 .Take(2).ToList())
                        DestroyFieldCard(monster);
                    CheckWinner();
                    break;
            }

            if (normalSummon && IsBlackwing(summoned.Definition))
            {
                CardInstance blackWhirlwind = owner.SpellTrapZones.FirstOrDefault(item =>
                    item != null && item.FaceUp &&
                    item.Definition.Effect == CardEffect.BlackWhirlwindSearch);
                CardInstance searched = blackWhirlwind == null ? null : owner.Deck
                    .Where(item => IsBlackwing(item.Definition) &&
                                   item.Definition.Attack < summoned.Definition.Attack)
                    .OrderByDescending(item => item.Definition.Attack)
                    .FirstOrDefault();
                if (searched != null)
                {
                    owner.Deck.Remove(searched);
                    owner.Hand.Add(searched);
                    Log(owner.Name + " searched " + searched.Definition.DisplayName +
                        " with Black Whirlwind.");
                }
            }
        }

        private bool SpecialSummonFromGraveyard(DuelPlayer owner, CardInstance card, bool attackPosition)
        {
            int zone = FindEmptyMonsterZone(owner);
            if (card == null || zone < 0 || !owner.Graveyard.Contains(card) ||
                owner.SummonsLockedThisTurn)
                return false;
            PlaceSpecialSummon(owner, card, zone, attackPosition);
            return true;
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
            if (monsterEffect && card.EffectsNegated)
                return Reject("That monster's effects are negated.");
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
            if (card.Definition.Effect == CardEffect.SearchDeckToHand &&
                !CurrentPlayer.Deck.Any(candidate => MatchesDeckEffectFilter(card.Definition, candidate)))
                return Reject("There are no cards in your Deck that match this search.");
            if (card.Definition.Effect == CardEffect.SendMonsterFromDeckToGraveyard &&
                !CurrentPlayer.Deck.Any(candidate => MatchesDeckEffectFilter(card.Definition, candidate)))
                return Reject("There are no eligible monsters in your Deck to send to the Graveyard.");
            if (card.Definition.Effect == CardEffect.DiscardOneDestroyFaceUpOpponentMonsters &&
                (CurrentPlayer.Hand.Count < 2 ||
                 !GetOpponent(CurrentPlayer).MonsterZones.Any(monster => monster != null && monster.FaceUp)))
                return Reject("Lightning Vortex needs another card to discard and a face-up opposing monster.");
            if (card.Definition.Effect == CardEffect.DrawThreeDiscardTwo &&
                (CurrentPlayer.Hand.Count < 3 || CurrentPlayer.Deck.Count < 3))
                return Reject("Graceful Charity needs two cards to discard and three cards in your Deck.");
            if (card.Definition.Effect == CardEffect.DestroyAllMonsters &&
                !CurrentPlayer.MonsterZones.Concat(GetOpponent(CurrentPlayer).MonsterZones)
                    .Any(monster => monster != null))
                return Reject("There are no monsters on the field to destroy.");
            if (card.Definition.Effect == CardEffect.DestroyAllOpponentMonsters &&
                !GetOpponent(CurrentPlayer).MonsterZones.Any(monster => monster != null))
                return Reject("The opponent has no monster to destroy.");
            if (card.Definition.Effect == CardEffect.DestroyHighestDefenseMonster &&
                !GetOpponent(CurrentPlayer).MonsterZones.Any(monster => monster != null && monster.FaceUp))
                return Reject("The opponent has no face-up monster to destroy.");
            if (card.Definition.Effect == CardEffect.ReviveMonsterFromGraveyard &&
                (target == null ||
                 !(CurrentPlayer.Graveyard.Contains(target) || GetOpponent(CurrentPlayer).Graveyard.Contains(target)) ||
                 FindEmptyMonsterZone(CurrentPlayer) < 0))
                return Reject("Select a monster in either Graveyard and make sure you have an open Monster Zone.");
            if (card.Definition.Effect == CardEffect.BookOfMoon &&
                (target == null || !IsOnMonsterField(target) || !target.FaceUp))
                return Reject("Select a face-up monster on the field.");
            if (card.Definition.Effect == CardEffect.BlackwingHalveMonster &&
                (target == null || !GetOpponent(CurrentPlayer).MonsterZones.Contains(target) || !target.FaceUp))
                return Reject("Select a face-up opposing monster.");
            if (card.Definition.Effect == CardEffect.BlackwingSiroccoBoost &&
                (Phase != DuelPhase.MainOne || target == null || !CurrentPlayer.MonsterZones.Contains(target) ||
                 !IsBlackwing(target.Definition)))
                return Reject("During Main Phase 1, select a Blackwing monster you control.");
            if (card.Definition.Effect == CardEffect.DestroySetOpponentBackrow &&
                !GetOpponent(CurrentPlayer).SpellTrapZones.Any(item => item != null && !item.FaceUp))
                return Reject("The opponent has no Set Spell or Trap to destroy.");
            if (card.Definition.Effect == CardEffect.MachineDuplication &&
                (target == null || !CurrentPlayer.MonsterZones.Contains(target) || !target.FaceUp ||
                 target.Definition.MonsterType != "Machine" || target.CurrentAttack > 500 ||
                 !CurrentPlayer.Deck.Any(item => item.Definition.CardId == target.Definition.CardId)))
                return Reject("Select a face-up Machine with 500 or less ATK and a matching copy in your Deck.");
            if (card.Definition.Effect == CardEffect.CallOfTheHaunted &&
                (target == null ||
                 !(CurrentPlayer.Graveyard.Contains(target) || GetOpponent(CurrentPlayer).Graveyard.Contains(target)) ||
                 !target.Definition.IsMonster || FindEmptyMonsterZone(CurrentPlayer) < 0))
                return Reject("Select a monster in either Graveyard and make sure you have an open Monster Zone.");
            if (card.Definition.Effect == CardEffect.IcarusAttack &&
                (!CurrentPlayer.MonsterZones.Any(IsWingedBeast) || CountFieldCards() < 3))
                return Reject("Icarus Attack needs a Winged Beast to Tribute and two other cards on the field.");
            if (card.Definition.Effect == CardEffect.PotOfAvarice &&
                (CurrentPlayer.Graveyard.Count(item => item.Definition.IsMonster) < 5 ||
                 CurrentPlayer.Deck.Count < 2))
                return Reject("Pot of Avarice needs five monsters in your Graveyard and two cards in your Deck.");
            if (card.Definition.Effect == CardEffect.OneForOne &&
                (!CurrentPlayer.Hand.Any(item => item != card && item.Definition.IsMonster) ||
                 !CurrentPlayer.Deck.Any(item => item.Definition.IsMonster && item.Definition.Level == 1) ||
                 FindEmptyMonsterZone(CurrentPlayer) < 0))
                return Reject("One for One needs a monster to send from your hand, a Level 1 monster in your Deck, and an open Monster Zone.");
            if (card.Definition.Effect == CardEffect.Scapegoat &&
                (CurrentPlayer.SummonsLockedThisTurn ||
                 CurrentPlayer.MonsterZones.Count(item => item == null) == 0 ||
                 !StarterCatalog.All.Any(item => item.Kind == CardKind.Token)))
                return Reject("Scapegoat needs an open Monster Zone and an available Token definition.");
            if (card.Definition.Effect == CardEffect.LimiterRemoval &&
                !CurrentPlayer.MonsterZones.Any(item => item != null && item.Definition.MonsterType == "Machine"))
                return Reject("You need a Machine monster on your field.");
            if (card.Definition.Effect == CardEffect.Tuning &&
                !CurrentPlayer.Deck.Any(item => IsSynchronTuner(item.Definition)))
                return Reject("There is no Synchron Tuner left in your Deck.");
            if (card.Definition.Effect == CardEffect.BlackWhirlwindSearch &&
                (card.Definition.Subtype != CardSubtype.Continuous || !CurrentPlayer.MonsterZones
                    .Any(item => item != null && IsBlackwing(item.Definition))))
                return Reject("Black Whirlwind needs a Blackwing monster on your field.");
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
                if (fromSetTrap && card.Definition.Effect == CardEffect.CallOfTheHaunted)
                {
                    card.FaceUp = true;
                }
                else
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
            ResolveSummonTriggers(CurrentPlayer, summoned, false);
            return true;
        }

        public bool TryAttack(CardInstance attacker, CardInstance target)
        {
            if (SearchChoiceOpen || (!IsAiVsAi && CurrentPlayer != Human) || Phase != DuelPhase.Battle)
                return Reject("Attacks are only available during your Battle Phase.");
            return ResolveAttack(CurrentPlayer, attacker, target);
        }

        public bool TryChooseSearchCard(CardInstance card)
        {
            if (!SearchChoiceOpen || card == null ||
                !pendingDeckChoiceOwner.Deck.Contains(card) ||
                !pendingDeckChoiceFilter(card))
                return Reject("Choose a card that matches the effect's Deck search requirements.");

            DuelPlayer owner = pendingDeckChoiceOwner;
            CompleteDeckChoice(owner, card, pendingDeckChoiceToGraveyard, pendingDeckChoiceSanganRestriction);
            ClearDeckChoice();
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
            if (card.Definition.Effect == CardEffect.DekoichiDraw)
            {
                DrawCard(CurrentPlayer);
                int extraDraws = CurrentPlayer.MonsterZones.Count(item =>
                    item != null && item.FaceUp && item.Definition.DisplayName == "Bokoichi the Freightening Car");
                for (int i = 0; i < extraDraws && Phase != DuelPhase.GameOver; i++)
                    DrawCard(CurrentPlayer);
            }
            return true;
        }

        public void AdvancePhase()
        {
            if (!IsHumanActionWindow || ResponseWindowOpen || SearchChoiceOpen)
                return;
            AdvanceToNextPhase();
        }

        public bool EndHumanTurn()
        {
            if (!IsHumanActionWindow || ResponseWindowOpen || SearchChoiceOpen)
                return false;

            if (Phase != DuelPhase.End && !AdvanceToPhase(DuelPhase.End))
                return false;

            AdvancePhase();
            return CurrentPlayer != Human || Phase == DuelPhase.GameOver;
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
            if (Phase == DuelPhase.GameOver || (!IsAiVsAi && pendingAiTurnActor == null))
                return false;

            if (!HasPendingAiActions)
                RunAiTurnActions(CurrentPlayer);
            ProcessPendingAiActions();
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
            foreach (CardInstance monster in endingPlayer.MonsterZones
                         .Where(item => item != null && item.DestroyAtEndPhaseTurn == turnNumber).ToList())
            {
                SendMonsterToGraveyard(endingPlayer, monster);
                Log(monster.Definition.DisplayName + " was destroyed by Limiter Removal.");
            }

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

        // Candidate callbacks keep AI choice separate from rule validation. A candidate may
        // execute any validated DuelEngine action, including TryActivate for supported effects.
        private sealed class AiActionCandidate
        {
            public double Score { get; }
            public Action Execute { get; }

            public AiActionCandidate(double score, Action execute)
            {
                Score = score;
                Execute = execute;
            }
        }

        private void RunAiTurnActions(DuelPlayer actor)
        {
            if (Phase == DuelPhase.GameOver || ResponseWindowOpen)
                return;

            pendingAiTurnActor = actor;
            pendingAiActions.Clear();
            pendingAiActionIndex = 0;
            if (Phase == DuelPhase.Draw)
            {
                pendingAiActions.Add(() =>
                {
                    SetPhase(DuelPhase.Standby);
                    Log(actor.Name + " entered the Standby Phase.");
                });
            }
            if (Phase == DuelPhase.Draw || Phase == DuelPhase.Standby)
            {
                pendingAiActions.Add(() =>
                {
                    SetPhase(DuelPhase.MainOne);
                    Log(actor.Name + " entered Main Phase 1.");
                });
            }
            pendingAiActions.Add(() => TryAiNormalSummon(actor));
            pendingAiActions.Add(() => TryAiBossSummon(actor));
            pendingAiActions.Add(() => TryAiFusion(actor));
            pendingAiActions.Add(() => EnqueueSpellActions(actor));
            pendingAiActions.Add(() => EnqueueMonsterEffectActions(actor));
            pendingAiActions.Add(() => TryAiSynchro(actor));
            pendingAiActions.Add(() => EnqueueTrapSetActions(actor));
            pendingAiActions.Add(() => EnqueuePositionActions(actor));
            pendingAiActions.Add(() => EnqueueBattleActions(actor));
            pendingAiActions.Add(EnterAiEndPhase);
            pendingAiActions.Add(FinishAiTurn);
        }

        private void TryAiNormalSummon(DuelPlayer actor)
        {
            if (actor.NormalSummonsThisTurn != 0)
                return;

            DuelPlayer opponent = GetOpponent(actor);
            List<CardInstance> field = actor.MonsterZones.Where(card => card != null).ToList();
            List<AiActionCandidate> candidates = new List<AiActionCandidate>();
            foreach (CardInstance summon in actor.Hand
                         .Where(card => card.Definition.CanNormalSummon)
                         .Where(card => card.Definition.Level < 5 ||
                                        field.Count >= (card.Definition.Level >= 7 ? 2 : 1)))
            {
                int tributeCount = summon.Definition.Level >= 7 ? 2 : summon.Definition.Level >= 5 ? 1 : 0;
                List<CardInstance> tributes = field
                    .OrderBy(card => AiFieldMonsterValue(card, opponent))
                    .Take(tributeCount).ToList();
                foreach (bool setFaceDown in new[] { false, true })
                {
                    double score = AiSummonScore(actor, summon, field, opponent, setFaceDown);
                    candidates.Add(new AiActionCandidate(score,
                        () => TryNormalSummon(summon, tributes, setFaceDown)));
                }
            }

            AiActionCandidate best = candidates.OrderByDescending(candidate => candidate.Score).FirstOrDefault();
            if (best != null)
                best.Execute();
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
            DuelPlayer opponent,
            bool setFaceDown)
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

            score += AiBattlePositionScore(card.Definition.Attack, card.Definition.Defense,
                !setFaceDown, opponent);
            // Setting hides effects as well as stats; prefer a face-up summon when it has an effect.
            if (setFaceDown && card.Definition.IsEffectMonster)
                score -= 220;
            return score;
        }

        private static double AiBattlePositionScore(
            int attack,
            int defense,
            bool attackPosition,
            DuelPlayer opponent)
        {
            List<CardInstance> visibleThreats = opponent.MonsterZones
                .Where(monster => monster != null && monster.FaceUp).ToList();
            if (visibleThreats.Count == 0)
                return attackPosition ? 300 + attack * 0.1 : defense * 0.1;

            int strongestThreat = visibleThreats.Max(monster => monster.CurrentAttack);
            int power = attackPosition ? attack : defense;
            bool canSurviveOrWin = power >= strongestThreat;
            double positionScore = (attackPosition ? attack : defense) * 0.1;
            positionScore += canSurviveOrWin
                ? (attackPosition ? 300 : 240)
                : -250 - (strongestThreat - power) * 0.35;
            return positionScore;
        }

        private static bool CanEnableSynchro(
            DuelPlayer actor,
            CardInstance candidate,
            IReadOnlyList<CardInstance> field)
        {
            foreach (CardInstance extra in actor.ExtraDeck.Where(card => card.Definition.Kind == CardKind.Synchro))
            {
                if (!CanUseSynchroMaterial(extra.Definition, candidate))
                    continue;
                for (int mask = 0; mask < (1 << field.Count); mask++)
                {
                    int level = GetSynchroMaterialLevel(extra.Definition, candidate);
                    int tunerCount = candidate.Definition.IsTuner ? 1 : 0;
                    bool hasNonTuner = !candidate.Definition.IsTuner;
                    for (int bit = 0; bit < field.Count; bit++)
                    {
                        if ((mask & (1 << bit)) == 0)
                            continue;
                        CardInstance material = field[bit];
                        if (!CanUseSynchroMaterial(extra.Definition, material))
                        {
                            level = -1;
                            break;
                        }
                        level += GetSynchroMaterialLevel(extra.Definition, material);
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
            List<AiActionCandidate> candidates = actor.Hand
                .Where(card => card.Definition.Kind == CardKind.Spell &&
                               AiEffectPriority(card.Definition, actor) > 0)
                .Select(card => new AiActionCandidate(AiEffectPriority(card.Definition, actor),
                    () => TryActivateAiSpell(card, actor)))
                .OrderByDescending(candidate => candidate.Score).ToList();
            InsertPendingActions(candidates.Select(candidate => candidate.Execute).ToList());
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
            List<AiActionCandidate> candidates = actor.MonsterZones
                .Where(card => card != null && card.FaceUp && AiEffectPriority(card.Definition, actor) > 0)
                .Select(card => new AiActionCandidate(AiEffectPriority(card.Definition, actor), () =>
                {
                    if (actor.MonsterZones.Contains(card) && !card.EffectUsedThisTurn)
                        TryActivate(card);
                }))
                .OrderByDescending(candidate => candidate.Score).ToList();
            InsertPendingActions(candidates.Select(candidate => candidate.Execute).ToList());
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

        private void EnqueuePositionActions(DuelPlayer actor)
        {
            if (CurrentPlayer != actor || !CanActInMainPhase() ||
                turnNumber <= actor.CannotChangePositionUntilTurn ||
                actor.PositionChangesLockedTurnsRemaining > 0)
                return;

            List<AiActionCandidate> candidates = actor.MonsterZones
                .Where(card => card != null && card.FaceUp && !card.HasAttacked &&
                               !card.HasChangedPositionThisTurn && turnNumber > card.SummonedTurnNumber)
                .Select(card => new AiActionCandidate(
                    AiBattlePositionScore(card.CurrentAttack, card.Definition.Defense,
                        !card.AttackPosition, GetOpponent(actor)) -
                    AiBattlePositionScore(card.CurrentAttack, card.Definition.Defense,
                        card.AttackPosition, GetOpponent(actor)),
                    () => TryChangePosition(card)))
                .Where(candidate => candidate.Score >= 100)
                .OrderByDescending(candidate => candidate.Score).ToList();
            InsertPendingActions(candidates.Select(candidate => candidate.Execute).ToList());
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
            if (ResponseWindowOpen || SearchChoiceOpen || Phase == DuelPhase.GameOver ||
                pendingAiActionIndex >= pendingAiActions.Count)
                return;

            Action action = pendingAiActions[pendingAiActionIndex++];
            action();
        }

        private void EnterAiEndPhase()
        {
            if (Phase == DuelPhase.GameOver || pendingAiTurnActor == null)
                return;
            SetPhase(DuelPhase.End);
            Log(pendingAiTurnActor.Name + " entered the End Phase.");
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
                if (!CanAttackDirectly(attackerOwner, attacker, defenders))
                    return Reject("You must attack a monster while the opponent controls one.");
                Log(attackerOwner.Name + "'s " + attacker.Definition.DisplayName + " declared a direct attack.");
                AttackDeclared?.Invoke(attacker, null);
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
            AttackDeclared?.Invoke(attacker, target);
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
                if (!CanAttackDirectly(attackerOwner, attacker, defenders))
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
                case CardEffect.SearchDeckToHand:
                    return BeginDeckChoice(owner, source,
                        candidate => MatchesDeckEffectFilter(definition, candidate),
                        false, definition.DisplayName + ": choose a matching card from your Deck.");
                case CardEffect.Tuning:
                    return BeginDeckChoice(owner, source,
                        candidate => IsSynchronTuner(candidate.Definition),
                        false, "Choose a Synchron Tuner from your Deck. Then send the top card of your Deck to the Graveyard.");
                case CardEffect.SendMonsterFromDeckToGraveyard:
                    return BeginDeckChoice(owner, source,
                        candidate => MatchesDeckEffectFilter(definition, candidate),
                        true, definition.DisplayName + ": choose a monster to send from your Deck to the Graveyard.");
                case CardEffect.BlackwingHalveMonster:
                    if (target == null || !opponent.MonsterZones.Contains(target) || !target.FaceUp)
                        return Reject("Select a face-up opposing monster.");
                    target.AttackBonus -= target.CurrentAttack / 2;
                    return true;
                case CardEffect.BlackwingSiroccoBoost:
                    if (target == null || !owner.MonsterZones.Contains(target) ||
                        !IsBlackwing(target.Definition))
                        return Reject("Select a Blackwing monster you control.");
                    target.AttackBonus += owner.MonsterZones
                        .Where(item => item != null && item != target && item.FaceUp &&
                            IsBlackwing(item.Definition))
                        .Sum(item => item.CurrentAttack);
                    foreach (CardInstance otherMonster in owner.MonsterZones.Where(item =>
                                 item != null && item != target))
                        otherMonster.HasAttacked = true;
                    return true;
                case CardEffect.BookOfMoon:
                    if (target == null || !IsOnMonsterField(target) || !target.FaceUp)
                        return Reject("Select a face-up monster on the field.");
                    target.FaceUp = false;
                    target.AttackPosition = false;
                    Log(target.Definition.DisplayName + " was changed to face-down Defense Position.");
                    return true;
                case CardEffect.DestroySetOpponentBackrow:
                    foreach (CardInstance setCard in opponent.SpellTrapZones
                                 .Where(item => item != null && !item.FaceUp).ToList())
                    {
                        RemoveFromSpellTrapZones(opponent, setCard);
                        opponent.Graveyard.Add(setCard);
                    }
                    return true;
                case CardEffect.IcarusAttack:
                    CardInstance tribute = owner.MonsterZones.FirstOrDefault(IsWingedBeast);
                    List<CardInstance> fieldTargets = GetAllFieldCards()
                        .Where(item => item != tribute)
                        .OrderBy(item => GetCardController(item) == owner ? 1 : 0)
                        .Take(2)
                        .ToList();
                    if (tribute == null || fieldTargets.Count < 2)
                        return Reject("Icarus Attack needs a Winged Beast to Tribute and two other cards on the field.");
                    SendMonsterToGraveyard(owner, tribute);
                    foreach (CardInstance fieldTarget in fieldTargets)
                        DestroyFieldCard(fieldTarget);
                    return true;
                case CardEffect.LimiterRemoval:
                    foreach (CardInstance machine in owner.MonsterZones.Where(item =>
                                 item != null && item.Definition.MonsterType == "Machine"))
                    {
                        machine.AttackBonus += machine.CurrentAttack;
                        machine.DestroyAtEndPhaseTurn = turnNumber;
                    }
                    return true;
                case CardEffect.MachineDuplication:
                    if (target == null || !owner.MonsterZones.Contains(target) ||
                        target.Definition.MonsterType != "Machine" || target.CurrentAttack > 500)
                        return Reject("Select your face-up Machine with 500 or less ATK.");
                    for (int count = 0; count < 2; count++)
                    {
                        int duplicationZone = FindEmptyMonsterZone(owner);
                        CardInstance copy = owner.Deck.FirstOrDefault(item =>
                            item.Definition.CardId == target.Definition.CardId);
                        if (duplicationZone < 0 || copy == null)
                            break;
                        owner.Deck.Remove(copy);
                        PlaceSpecialSummon(owner, copy, duplicationZone, true);
                    }
                    return true;
                case CardEffect.OneForOne:
                    CardInstance discard = owner.Hand.FirstOrDefault(item =>
                        item != source && item.Definition.IsMonster);
                    CardInstance levelOne = owner.Deck.FirstOrDefault(item =>
                        item.Definition.IsMonster && item.Definition.Level == 1);
                    int oneForOneZone = FindEmptyMonsterZone(owner);
                    if (discard == null || levelOne == null || oneForOneZone < 0)
                        return Reject("One for One no longer has a legal cost, target, or open Monster Zone.");
                    owner.Hand.Remove(discard);
                    owner.Graveyard.Add(discard);
                    owner.Deck.Remove(levelOne);
                    PlaceSpecialSummon(owner, levelOne, oneForOneZone, true);
                    return true;
                case CardEffect.PotOfAvarice:
                    List<CardInstance> returnedMonsters = owner.Graveyard
                        .Where(item => item.Definition.IsMonster).Take(5).ToList();
                    if (returnedMonsters.Count < 5)
                        return Reject("Pot of Avarice no longer has five monsters to return.");
                    foreach (CardInstance monster in returnedMonsters)
                    {
                        owner.Graveyard.Remove(monster);
                        owner.Deck.Add(monster);
                    }
                    Shuffle(owner.Deck);
                    DrawCard(owner);
                    DrawCard(owner);
                    return true;
                case CardEffect.Scapegoat:
                    CardDefinition tokenDefinition = StarterCatalog.All.FirstOrDefault(item => item.Kind == CardKind.Token);
                    if (tokenDefinition == null)
                        return Reject("No Token card definition is available for Scapegoat.");
                    owner.SummonsLockedThisTurn = true;
                    for (int i = 0; i < 4; i++)
                    {
                        int tokenZone = FindEmptyMonsterZone(owner);
                        if (tokenZone < 0)
                            break;
                        PlaceSpecialSummon(owner, new CardInstance(tokenDefinition), tokenZone, false);
                    }
                    return true;
                case CardEffect.CallOfTheHaunted:
                    return ActivateCallOfTheHaunted(owner, source, target);
                case CardEffect.BlackWhirlwindSearch:
                    return true;
                case CardEffect.DiscardDrawSameCount:
                    int ownerDiscardCount = owner.Hand.Count - (source != null && owner.Hand.Contains(source) ? 1 : 0);
                    int opponentDiscardCount = opponent.Hand.Count;
                    DiscardCards(owner, source, ownerDiscardCount, definition.DisplayName);
                    DiscardCards(opponent, null, opponentDiscardCount, definition.DisplayName);
                    for (int i = 0; i < ownerDiscardCount && Phase != DuelPhase.GameOver; i++)
                        DrawCard(owner);
                    for (int i = 0; i < opponentDiscardCount && Phase != DuelPhase.GameOver; i++)
                        DrawCard(opponent);
                    return true;
                case CardEffect.DrawThreeDiscardTwo:
                    for (int i = 0; i < 3 && owner.Deck.Count > 0 && Phase != DuelPhase.GameOver; i++)
                        DrawCard(owner);
                    DiscardCards(owner, source, 2, definition.DisplayName);
                    return true;
                case CardEffect.DiscardOneDestroyFaceUpOpponentMonsters:
                    if (!DiscardCards(owner, source, 1, definition.DisplayName))
                        return Reject("You need a card in your hand to discard.");
                    foreach (CardInstance monster in opponent.MonsterZones
                                 .Where(item => item != null && item.FaceUp).ToList())
                        SendMonsterToGraveyard(opponent, monster);
                    CheckWinner();
                    return true;
                case CardEffect.DestroyAllMonsters:
                    foreach (CardInstance monster in owner.MonsterZones.Concat(opponent.MonsterZones)
                                 .Where(item => item != null).ToList())
                        SendMonsterToGraveyard(GetMonsterOwner(monster), monster);
                    CheckWinner();
                    return true;
                case CardEffect.DestroyAllOpponentMonsters:
                    foreach (CardInstance monster in opponent.MonsterZones.Where(item => item != null).ToList())
                        SendMonsterToGraveyard(opponent, monster);
                    CheckWinner();
                    return true;
                case CardEffect.DestroyHighestDefenseMonster:
                    CardInstance highestDefense = opponent.MonsterZones
                        .Where(item => item != null && item.FaceUp)
                        .OrderByDescending(item => item.Definition.Defense)
                        .FirstOrDefault();
                    if (highestDefense == null)
                        return Reject("The opponent has no face-up monster to destroy.");
                    SendMonsterToGraveyard(opponent, highestDefense);
                    CheckWinner();
                    return true;
                case CardEffect.ReturnAllSpellTrapsToHand:
                    ReturnAllSpellTrapsToHand(owner);
                    ReturnAllSpellTrapsToHand(opponent);
                    return true;
                case CardEffect.ReviveMonsterFromGraveyard:
                    return ReviveFromGraveyard(owner, target);
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
                    int reviveZone = FindEmptyMonsterZone(owner);
                    if (revived == null || reviveZone < 0)
                        return Reject("You need an open Monster Zone and a monster in your Graveyard.");
                    owner.Graveyard.Remove(revived);
                    revived.FaceUp = true;
                    revived.AttackPosition = false;
                    revived.HasAttacked = false;
                    revived.EffectUsedThisTurn = false;
                    revived.AttackBonus = 0;
                    revived.SummonedTurnNumber = turnNumber;
                    owner.MonsterZones[reviveZone] = revived;
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
                    ? card.Definition.Effect == CardEffect.StopAttack ||
                      card.Definition.Effect == CardEffect.SakuretsuArmor ||
                      card.Definition.Effect == CardEffect.ScrapIronScarecrow ||
                      card.Definition.DisplayName == "Magic Cylinder"
                    : eventType == CardEffect.DestroyAllMonsters
                        ? card.Definition.Effect == CardEffect.TorrentialTribute
                        : card.Definition.Effect == eventType &&
                          card.Definition.Subtype == CardSubtype.Counter)).ToList();
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

        private static bool CanAttackDirectly(
            DuelPlayer attackerOwner, CardInstance attacker, IReadOnlyCollection<CardInstance> defenders)
        {
            if (defenders.Count == 0)
                return true;
            bool boardenEffect = attackerOwner.MonsterZones.Any(card =>
                card != null && card.FaceUp && !card.AttackPosition &&
                card.Definition.Effect == CardEffect.MorphtronicBoarden);
            return boardenEffect && attacker.Definition.DisplayName.StartsWith("Morphtronic",
                StringComparison.OrdinalIgnoreCase);
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

            if (response.Definition.Effect == CardEffect.TorrentialTribute)
            {
                DestroyAllMonstersOnField();
                CheckWinner();
                Log("Torrential Tribute destroyed all monsters on the field.");
                return true;
            }

            if (response.Definition.Effect == CardEffect.SakuretsuArmor)
            {
                SendMonsterToGraveyard(attackerOwner, attacker);
                Log("Sakuretsu Armor destroyed " + attacker.Definition.DisplayName + ".");
                CheckWinner();
                return true;
            }

            if (response.Definition.Effect == CardEffect.ScrapIronScarecrow)
            {
                Log(response.Definition.DisplayName + " negated " + attacker.Definition.DisplayName + "'s attack.");
                int zone = FindEmptySpellTrapZone(defender);
                if (zone >= 0)
                {
                    defender.Graveyard.Remove(response);
                    response.FaceUp = false;
                    defender.SpellTrapZones[zone] = response;
                }
                return true;
            }

            if (response.Definition.Effect == CardEffect.StopAttack)
            {
                Log(response.Definition.DisplayName + " negated " + attacker.Definition.DisplayName + "'s attack.");
                return true;
            }

            return false;
        }

        private void DestroyAllMonstersOnField()
        {
            foreach (CardInstance monster in Human.MonsterZones.Concat(Opponent.MonsterZones)
                         .Where(item => item != null).ToList())
                SendMonsterToGraveyard(GetMonsterOwner(monster), monster);
        }

        private bool IsValidSynchroMaterial(CardDefinition synchro, IReadOnlyList<CardInstance> materials, DuelPlayer owner)
        {
            if (materials == null || materials.Count < 2 ||
                materials.Any(card => card == null || !card.FaceUp || !owner.MonsterZones.Contains(card) ||
                    !CanUseSynchroMaterial(synchro, card)))
                return false;
            return materials.Sum(card => GetSynchroMaterialLevel(synchro, card)) == synchro.Level &&
                   materials.Count(card => card.Definition.IsTuner) == 1 &&
                   materials.Any(card => !card.Definition.IsTuner);
        }

        private static bool CanUseSynchroMaterial(CardDefinition synchro, CardInstance material)
        {
            return material.Definition.Effect != CardEffect.MajesticDragon ||
                synchro.DisplayName.IndexOf("Majestic", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int GetSynchroMaterialLevel(CardDefinition synchro, CardInstance material)
        {
            int level = material.Definition.Level;
            if (material.Definition.Effect == CardEffect.RoadSynchron &&
                !string.Equals(synchro.DisplayName, "Road Warrior", StringComparison.OrdinalIgnoreCase))
                level -= 2;
            return level;
        }

        private DuelPlayer GetOpponent(DuelPlayer player)
        {
            return player == Human ? Opponent : Human;
        }

        private bool IsOnMonsterField(CardInstance card)
        {
            return card != null && (Human.MonsterZones.Contains(card) || Opponent.MonsterZones.Contains(card));
        }

        private static bool IsWingedBeast(CardInstance card)
        {
            return card != null && string.Equals(card.Definition.MonsterType, "Winged Beast",
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsBlackwing(CardDefinition definition)
        {
            return definition != null && definition.DisplayName.StartsWith("Blackwing",
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSynchronTuner(CardDefinition definition)
        {
            return definition != null && definition.IsTuner &&
                   definition.DisplayName.IndexOf("Synchron", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private List<CardInstance> GetAllFieldCards()
        {
            return Human.MonsterZones.Concat(Opponent.MonsterZones)
                .Concat(Human.SpellTrapZones).Concat(Opponent.SpellTrapZones)
                .Concat(new[] { Human.FieldSpellZone, Opponent.FieldSpellZone })
                .Where(card => card != null).ToList();
        }

        private DuelPlayer GetCardController(CardInstance card)
        {
            DuelPlayer monsterOwner = GetMonsterOwner(card);
            return monsterOwner ?? GetSpellTrapOwner(card);
        }

        private int CountFieldCards()
        {
            return GetAllFieldCards().Count;
        }

        private void DestroyFieldCard(CardInstance card)
        {
            DuelPlayer monsterOwner = GetMonsterOwner(card);
            if (monsterOwner != null)
            {
                SendMonsterToGraveyard(monsterOwner, card);
                return;
            }

            DuelPlayer spellTrapOwner = GetSpellTrapOwner(card);
            if (spellTrapOwner == null)
                return;
            if (spellTrapOwner.FieldSpellZone == card)
                spellTrapOwner.FieldSpellZone = null;
            else
                RemoveFromSpellTrapZones(spellTrapOwner, card);
            spellTrapOwner.Graveyard.Add(card);
        }

        private void PlaceSpecialSummon(DuelPlayer owner, CardInstance card, int zone, bool attackPosition)
        {
            owner.Hand.Remove(card);
            owner.Graveyard.Remove(card);
            owner.Deck.Remove(card);
            owner.Banished.Remove(card);
            card.FaceUp = true;
            card.AttackPosition = attackPosition;
            card.SummonedTurnNumber = turnNumber;
            card.HasAttacked = false;
            card.HasChangedPositionThisTurn = false;
            owner.MonsterZones[zone] = card;
            Log(owner.Name + " Special Summoned " + card.Definition.DisplayName + ".");
            ResolveSummonTriggers(owner, card, false);
        }

        private bool ActivateCallOfTheHaunted(DuelPlayer owner, CardInstance source, CardInstance target)
        {
            DuelPlayer cardOwner = Human.Graveyard.Contains(target) ? Human :
                Opponent.Graveyard.Contains(target) ? Opponent : null;
            if (source == null || !owner.SpellTrapZones.Contains(source) || cardOwner == null ||
                !target.Definition.IsMonster || FindEmptyMonsterZone(owner) < 0)
                return Reject("Call of the Haunted needs a Set Trap, a monster in either Graveyard, and an open Monster Zone.");

            int zone = FindEmptyMonsterZone(owner);
            cardOwner.Graveyard.Remove(target);
            target.FaceUp = true;
            target.AttackPosition = true;
            target.SummonedTurnNumber = turnNumber;
            owner.MonsterZones[zone] = target;
            source.EquippedMonster = target;
            source.FaceUp = true;
            Log(owner.Name + " Special Summoned " + target.Definition.DisplayName + " with Call of the Haunted.");
            ResolveSummonTriggers(owner, target, false);
            return true;
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

        private DuelPlayer GetMonsterOwner(CardInstance card)
        {
            if (card == null)
                return null;
            if (Human.MonsterZones.Contains(card))
                return Human;
            if (Opponent.MonsterZones.Contains(card))
                return Opponent;
            return null;
        }

        private bool BeginDeckChoice(
            DuelPlayer owner,
            CardInstance source,
            Func<CardInstance, bool> filter,
            bool toGraveyard,
            string prompt,
            bool applySanganRestriction = false)
        {
            List<CardInstance> choices = owner.Deck.Where(filter).ToList();
            if (choices.Count == 0)
                return Reject("No cards in the Deck match this effect.");

            if (owner.IsAi)
            {
                CardInstance choice = choices
                    .OrderByDescending(card => card.Definition.Effect != CardEffect.None)
                    .ThenByDescending(card => card.Definition.Attack)
                    .First();
                CompleteDeckChoice(owner, choice, toGraveyard, applySanganRestriction);
                return true;
            }

            pendingDeckChoiceOwner = owner;
            pendingDeckChoiceSource = source;
            pendingDeckChoiceFilter = filter;
            pendingDeckChoicePrompt = prompt;
            pendingDeckChoiceToGraveyard = toGraveyard;
            pendingDeckChoiceSanganRestriction = applySanganRestriction;
            Log(owner.Name + " must choose a card from the Deck.");
            return true;
        }

        private void CompleteDeckChoice(
            DuelPlayer owner,
            CardInstance card,
            bool toGraveyard,
            bool applySanganRestriction)
        {
            owner.Deck.Remove(card);
            if (toGraveyard)
            {
                owner.Graveyard.Add(card);
                Log(owner.Name + " sent " + card.Definition.DisplayName + " from the Deck to the Graveyard.");
                return;
            }

            owner.Hand.Add(card);
            if (applySanganRestriction)
            {
                temporarilyDisabledCardName = card.Definition.DisplayName;
                temporarilyDisabledCardOwner = owner;
                temporarilyDisabledCardTurn = turnNumber;
                Log(owner.Name + " added " + card.Definition.DisplayName +
                    " to hand with Sangan. That card's effects cannot be activated this turn.");
                return;
            }
            Log(owner.Name + " added " + card.Definition.DisplayName + " from the Deck to the hand.");
            if (pendingDeckChoiceSource != null &&
                pendingDeckChoiceSource.Definition.Effect == CardEffect.Tuning &&
                owner.Deck.Count > 0)
            {
                CardInstance milled = owner.Deck[0];
                owner.Deck.RemoveAt(0);
                owner.Graveyard.Add(milled);
                Log(owner.Name + " sent " + milled.Definition.DisplayName +
                    " from the top of the Deck to the Graveyard.");
            }
        }

        private void ClearDeckChoice()
        {
            pendingDeckChoiceOwner = null;
            pendingDeckChoiceSource = null;
            pendingDeckChoiceFilter = null;
            pendingDeckChoicePrompt = "";
            pendingDeckChoiceToGraveyard = false;
            pendingDeckChoiceSanganRestriction = false;
        }

        private static bool MatchesDeckEffectFilter(CardDefinition effect, CardInstance candidate)
        {
            if (effect == null || candidate == null || !candidate.Definition.CanBeMainDeck)
                return false;
            CardDefinition definition = candidate.Definition;
            if (effect.Effect == CardEffect.SendMonsterFromDeckToGraveyard && !definition.IsMonster)
                return false;
            if (!string.IsNullOrWhiteSpace(effect.EffectTargetMonsterType) &&
                !string.Equals(effect.EffectTargetMonsterType, definition.MonsterType,
                    StringComparison.OrdinalIgnoreCase))
                return false;
            if (effect.EffectTargetMaxLevel > 0 && definition.Level > effect.EffectTargetMaxLevel)
                return false;
            if (effect.EffectTargetMaxAttack > 0 && definition.Attack > effect.EffectTargetMaxAttack)
                return false;
            return true;
        }

        private bool DiscardCards(DuelPlayer owner, CardInstance excludedCard, int count, string sourceName)
        {
            List<CardInstance> candidates = owner.Hand.Where(card => card != excludedCard).ToList();
            if (candidates.Count < count)
                return false;

            List<CardInstance> discarded = candidates
                .OrderBy(card => card.Definition.Effect == CardEffect.None ? 0 : 1)
                .ThenBy(card => card.Definition.Attack)
                .Take(count)
                .ToList();
            foreach (CardInstance card in discarded)
            {
                owner.Hand.Remove(card);
                card.FaceUp = false;
                owner.Graveyard.Add(card);
            }
            if (discarded.Count > 0)
                Log(owner.Name + " discarded " + discarded.Count + " card(s) for " + sourceName + ".");
            return true;
        }

        private void ReturnAllSpellTrapsToHand(DuelPlayer owner)
        {
            foreach (CardInstance card in owner.SpellTrapZones.Where(item => item != null).ToList())
            {
                RemoveFromSpellTrapZones(owner, card);
                card.FaceUp = false;
                owner.Hand.Add(card);
                Log(card.Definition.DisplayName + " returned to " + owner.Name + "'s hand.");
            }

            if (owner.FieldSpellZone == null)
                return;
            CardInstance fieldSpell = owner.FieldSpellZone;
            owner.FieldSpellZone = null;
            fieldSpell.FaceUp = false;
            owner.Hand.Add(fieldSpell);
            Log(fieldSpell.Definition.DisplayName + " returned to " + owner.Name + "'s hand.");
        }

        private bool ReviveFromGraveyard(DuelPlayer owner, CardInstance target)
        {
            DuelPlayer cardOwner = Human.Graveyard.Contains(target) ? Human :
                Opponent.Graveyard.Contains(target) ? Opponent : null;
            int zone = FindEmptyMonsterZone(owner);
            if (cardOwner == null || zone < 0 || !target.Definition.IsMonster ||
                target.Definition.Kind == CardKind.Token || owner.SummonsLockedThisTurn)
                return Reject("That monster cannot be Special Summoned from the Graveyard.");

            cardOwner.Graveyard.Remove(target);
            target.FaceUp = true;
            target.AttackPosition = true;
            target.SummonedTurnNumber = turnNumber;
            owner.MonsterZones[zone] = target;
            Log(owner.Name + " Special Summoned " + target.Definition.DisplayName + " from the Graveyard.");
            ResolveSummonTriggers(owner, target, false);
            return true;
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
            monster.DestroyAtEndPhaseTurn = 0;
            if (monster.BanishWhenLeavingField)
                owner.Banished.Add(monster);
            else
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
            bool IsEligible(CardInstance card) => card.Definition.IsMonster &&
                card.Definition.CanBeMainDeck && card.Definition.Attack <= 1500;
            if (!owner.Deck.Any(IsEligible))
            {
                Log(owner.Name + "'s Sangan was sent to the Graveyard, but no monster with 1500 or less ATK remains in the Deck.");
                return;
            }

            BeginDeckChoice(owner, monster, IsEligible, false,
                "Choose a monster with 1500 or less ATK.", true);
        }

        private void RemoveFromSpellTrapZones(DuelPlayer player, CardInstance card)
        {
            CardInstance hauntedMonster = card.Definition.Effect == CardEffect.CallOfTheHaunted
                ? card.EquippedMonster
                : null;
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
            if (hauntedMonster != null)
            {
                DuelPlayer monsterOwner = GetMonsterOwner(hauntedMonster);
                if (monsterOwner != null)
                    SendMonsterToGraveyard(monsterOwner, hauntedMonster);
            }
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
