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
        public bool EffectUsedOncePerDuel { get; set; }
        public bool StardustReturnPending { get; set; }
        public bool EffectsNegated { get; set; }
        public bool CannotBeDestroyedByCardEffects { get; set; }
        public int EffectsNegatedUntilTurn { get; set; }
        public int WedgeCounters { get; set; }
        public int ExtraAttacksThisTurn { get; set; }
        public int LevelModifier { get; set; }
        public int DrillWarriorReturnTurn { get; set; }
        public int DefenseModifier { get; set; }
        public bool DrillWarriorDirectAttackThisTurn { get; set; }
        public bool NitroWarriorBoostReady { get; set; }
        public CardInstance NitroWarriorSecondAttackTarget { get; set; }
        public int AttackBonus { get; set; }
        public int PermanentAttackBonus { get; set; }
        public int SummonedTurnNumber { get; set; }
        public int SpellCounters { get; set; }
        public int DestroyedOnFieldTurn { get; set; }
        public int EffectDurationTurnsRemaining { get; set; }
        public int DestroyAtEndPhaseTurn { get; set; }
        public bool BanishWhenLeavingField { get; set; }
        public bool CannotBeTributedForTributeSummon { get; set; }
        public int CannotBeTributedForTributeSummonUntilTurn { get; set; }
        public bool AncientFairySummonUsedThisTurn { get; set; }
        public bool AncientFairyFieldEffectUsedThisTurn { get; set; }
        public bool MajesticTargetEffectUsedThisTurn { get; set; }
        public bool ShootingStarExcavationUsedThisTurn { get; set; }
        public bool ShootingStarDestructionNegateUsedThisTurn { get; set; }
        public bool ShootingStarAttackNegateUsedThisTurn { get; set; }
        public bool WasSynchroSummoned { get; set; }
        public int SilverwindProtectionTurn { get; set; }
        public int ReturnAtEndPhaseTurn { get; set; }
        public CardInstance CopiedEffectTarget { get; set; }
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
        public bool WitchEffectUsedThisTurn { get; set; }
        public int BattleDamageProtectedTurn { get; set; }
        public int CannotAttackUntilTurn { get; set; }
        public int CannotChangePositionUntilTurn { get; set; }
        public int NoAttackTurnsRemaining { get; set; }
        public int PositionChangesLockedTurnsRemaining { get; set; }
        public int BattlePhaseSkippedTurn { get; set; }
        public int DeckCount => Deck.Count;

        public DuelPlayer(string name, bool isAi)
        {
            Name = name;
            IsAi = isAi;
        }
    }

    public sealed class DuelEngine
    {
        public const int MinimumMainDeckSize = 40;
        private const int AiPlannerDepth = 3;
        private const int AiPlannerBeamWidth = 3;
        private const int AiPlannerCandidatesPerNode = 18;
        private const int AiPlannerEvaluationLimit = 126;
        private const int AiPlannerActionsPerTurnLimit = 8;

        private enum ResponseAttempt { None, Cancelled, Waiting }

        private sealed class DeferredGraveyardEffect
        {
            public DuelPlayer Owner;
            public CardInstance Card;
            public bool SentFromField;
            public bool DestroyedByBattle;
        }

        private readonly Random random = new Random();
        private readonly List<string> messages = new List<string>();
        private readonly List<DeferredGraveyardEffect> deferredGraveyardEffects =
            new List<DeferredGraveyardEffect>();
        private readonly List<Action> pendingAiActions = new List<Action>();
        private int turnNumber;
        private int pendingAiActionIndex;
        private int aiPlannerActionsThisTurn;
        private DuelPlayer pendingAiTurnActor;
        private CardEffect pendingResponseType;
        private Action pendingResponsePass;
        private Action pendingResponseTrap;
        private Action pendingResponseCountered;
        private string pendingResponseSource;
        private CardInstance pendingResponseSourceCard;
        private CardInstance responseEffectAwaitingDiscard;
        private bool responseDiscardCostPaid;
        private string responseDiscardReason;
        private int chainLinkCount;
        private DuelPlayer pendingDeckChoiceOwner;
        private CardInstance pendingDeckChoiceSource;
        private Func<CardInstance, bool> pendingDeckChoiceFilter;
        private string pendingDeckChoicePrompt;
        private bool pendingDeckChoiceToGraveyard;
        private bool pendingDeckChoiceSanganRestriction;
        private bool pendingDeckChoiceSpecialSummon;
        private bool pendingDeckChoiceOptional;
        private bool pendingDeckChoiceNegatesEffects;
        private CardInstance pendingPowerToolChoiceSource;
        private List<CardInstance> pendingPowerToolChoices = new List<CardInstance>();
        private DuelPlayer pendingQuickdrawOwner;
        private CardInstance pendingQuickdrawSource;
        private DuelPlayer pendingGraveyardSummonOwner;
        private CardInstance pendingGraveyardSummonSource;
        private List<CardInstance> pendingGraveyardSummonChoices = new List<CardInstance>();
        private bool pendingGraveyardSummonAttackPosition;
        private bool pendingGraveyardSummonNegatesEffects;
        private bool pendingGraveyardSummonFromEitherGraveyard;
        private Action pendingGraveyardSummonContinuation;
        private CardInstance pendingActivationDiscardSource;
        private CardInstance pendingActivationDiscardTarget;
        private CardInstance pendingActivationDiscardOptionalCard;
        private int pendingActivationDiscardCount;
        private bool pendingActivationDiscardMonstersOnly;
        private CardInstance pendingDustTornadoResponse;
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
        public event Action<DuelPlayer, CardInstance> MonsterDestroyed;
        public bool CanAttackThisTurn => turnNumber > 1;
        public bool IsTrapActivationBlockedByJinzo(CardInstance card)
        {
            return card != null && card.Definition.Kind == CardKind.Trap &&
                   Human != null && Opponent != null &&
                   Human.MonsterZones.Concat(Opponent.MonsterZones).Any(monster =>
                       monster != null && monster.FaceUp && !monster.EffectsNegated &&
                       monster.Definition.Effect == CardEffect.JinzoTrapLock);
        }
        public string AttackRestrictionMessage => CanAttackThisTurn ? "" :
            "The first player cannot attack during the first turn of the duel.";
        public int TurnNumber => turnNumber;
        public string Winner { get; private set; }
        public IReadOnlyList<string> Messages => messages;
        public bool SearchChoiceOpen => pendingDeckChoiceOwner != null;
        public bool SearchChoiceOptional => SearchChoiceOpen && pendingDeckChoiceOptional;
        public bool PowerToolChoiceOpen => pendingPowerToolChoiceSource != null;
        public bool QuickdrawDiscardChoiceOpen => pendingQuickdrawSource != null;
        public bool GraveyardSummonChoiceOpen => pendingGraveyardSummonSource != null;
        public bool ActivationDiscardChoiceOpen => pendingActivationDiscardSource != null;
        public int ActivationDiscardCount => pendingActivationDiscardCount;
        public string ActivationDiscardChoicePrompt => ActivationDiscardChoiceOpen
            ? pendingActivationDiscardSource.Definition.DisplayName + ": choose " +
              pendingActivationDiscardCount + " card(s) to discard."
            : "";
        public IReadOnlyList<CardInstance> AvailableActivationDiscards => ActivationDiscardChoiceOpen
            ? CurrentPlayer.Hand.Where(card => card != pendingActivationDiscardSource &&
                (!pendingActivationDiscardMonstersOnly || card.Definition.IsMonster)).ToList()
            : Array.Empty<CardInstance>();
        public string GraveyardSummonChoicePrompt => GraveyardSummonChoiceOpen
            ? "Choose a monster to Special Summon " +
              (pendingGraveyardSummonFromEitherGraveyard
                  ? "from either Graveyard"
                  : "from your Graveyard") + " for " +
              pendingGraveyardSummonSource.Definition.DisplayName + ", or skip the effect."
            : "";
        public IReadOnlyList<CardInstance> AvailableGraveyardSummons => GraveyardSummonChoiceOpen
            ? pendingGraveyardSummonChoices
            : Array.Empty<CardInstance>();
        public IReadOnlyList<CardInstance> AvailableQuickdrawDiscards => QuickdrawDiscardChoiceOpen
            ? pendingQuickdrawOwner.Hand.Where(card => card != pendingQuickdrawSource &&
                card.Definition.IsMonster).ToList()
            : Array.Empty<CardInstance>();
        public IReadOnlyList<CardInstance> AvailablePowerToolCards => PowerToolChoiceOpen
            ? pendingPowerToolChoices
            : Array.Empty<CardInstance>();
        public bool DustTornadoTargetChoiceOpen => pendingDustTornadoResponse != null;
        public CardInstance PendingDustTornado => pendingDustTornadoResponse;
        public IReadOnlyList<CardInstance> AvailableDustTornadoTargets => DustTornadoTargetChoiceOpen
            ? GetOpponent(Human).SpellTrapZones.Concat(new[] { GetOpponent(Human).FieldSpellZone })
                .Where(card => card != null).ToList()
            : Array.Empty<CardInstance>();
        public IReadOnlyList<CardInstance> AvailableDustTornadoSetCards => DustTornadoTargetChoiceOpen
            ? Human.Hand.Where(card => card.Definition.Kind == CardKind.Spell ||
                card.Definition.Kind == CardKind.Trap).ToList()
            : Array.Empty<CardInstance>();
        public string SearchChoicePrompt => SearchChoiceOpen
            ? pendingDeckChoicePrompt
            : "";
        public string SearchChoiceTitle => SearchChoiceOpen
            ? pendingDeckChoiceSource.Definition.DisplayName + " — CHOOSE A CARD"
            : "";
        public string SearchChoiceActionLabel => pendingDeckChoiceSpecialSummon
            ? "SPECIAL SUMMON SELECTED CARD"
            : pendingDeckChoiceToGraveyard
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
                    return "Discard a card for " + responseDiscardReason + ".";
                string source = string.IsNullOrEmpty(pendingResponseSource) ? "The opponent's card" : pendingResponseSource;
                return pendingResponseType == CardEffect.StopAttack
                    ? source + " declared an attack. Activate a Set Trap or a Quick Effect, or pass."
                    : pendingResponseType == CardEffect.CounterSpell
                        ? source + " activated a Spell. Activate a response or pass."
                        : pendingResponseType == CardEffect.CounterMonsterEffect
                            ? source + " activated a monster effect. Activate a response or pass."
                            : pendingResponseType == CardEffect.DestroyAllMonsters
                                ? source + " was Summoned. Activate a Trap or pass."
                            : source + " activated a Trap. Chain a Counter Trap or pass.";
            }
        }
        public IReadOnlyList<CardInstance> AvailableResponseTraps => ResponseDiscardChoiceOpen
            ? Human.Hand.Where(card => card != responseEffectAwaitingDiscard).ToList()
            : GetResponseCards(Human, pendingResponseType, pendingResponseSourceCard);
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
            if (humanDeck == null || humanDeck.Count < MinimumMainDeckSize)
                throw new ArgumentException("A duel deck must contain at least 40 cards.", nameof(humanDeck));
            if (aiDeck == null || aiDeck.Count < MinimumMainDeckSize)
                throw new ArgumentException("The opponent deck must contain at least 40 cards.", nameof(aiDeck));

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
            responseDiscardCostPaid = false;
            responseDiscardReason = null;
            pendingGraveyardSummonOwner = null;
            pendingGraveyardSummonSource = null;
            pendingGraveyardSummonChoices.Clear();
            pendingGraveyardSummonContinuation = null;
            ClearActivationDiscardChoice();
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
            return CaptureState(false);
        }

        public DuelStateSnapshot CaptureStateForReplay()
        {
            return CaptureState(true);
        }

        private DuelStateSnapshot CaptureState(bool allowPendingAiActions)
        {
            if (Human == null || Opponent == null || CurrentPlayer == null)
                throw new InvalidOperationException("Start a duel before capturing its state.");
            if (ResponseWindowOpen || ResponseDiscardChoiceOpen || SearchChoiceOpen ||
                QuickdrawDiscardChoiceOpen || GraveyardSummonChoiceOpen ||
                ActivationDiscardChoiceOpen ||
                (!allowPendingAiActions && pendingAiActions.Count > 0))
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
                    effectUsedOncePerDuel = card.EffectUsedOncePerDuel,
                    stardustReturnPending = card.StardustReturnPending,
                    effectsNegated = card.EffectsNegated,
                    cannotBeDestroyedByCardEffects = card.CannotBeDestroyedByCardEffects,
                    effectsNegatedUntilTurn = card.EffectsNegatedUntilTurn,
                    wedgeCounters = card.WedgeCounters,
                    extraAttacksThisTurn = card.ExtraAttacksThisTurn,
                    levelModifier = card.LevelModifier,
                    drillWarriorReturnTurn = card.DrillWarriorReturnTurn,
                    defenseModifier = card.DefenseModifier,
                    drillWarriorDirectAttackThisTurn = card.DrillWarriorDirectAttackThisTurn,
                    nitroWarriorBoostReady = card.NitroWarriorBoostReady,
                    nitroWarriorSecondAttackTargetId = GetInstanceId(card.NitroWarriorSecondAttackTarget, instanceIds),
                    attackBonus = card.AttackBonus,
                    permanentAttackBonus = card.PermanentAttackBonus,
                    summonedTurnNumber = card.SummonedTurnNumber,
                    spellCounters = card.SpellCounters,
                    destroyedOnFieldTurn = card.DestroyedOnFieldTurn,
                    destroyAtEndPhaseTurn = card.DestroyAtEndPhaseTurn,
                    effectDurationTurnsRemaining = card.EffectDurationTurnsRemaining,
                    banishWhenLeavingField = card.BanishWhenLeavingField,
                    cannotBeTributedForTributeSummon = card.CannotBeTributedForTributeSummon,
                    cannotBeTributedForTributeSummonUntilTurn = card.CannotBeTributedForTributeSummonUntilTurn,
                    ancientFairySummonUsedThisTurn = card.AncientFairySummonUsedThisTurn,
                    ancientFairyFieldEffectUsedThisTurn = card.AncientFairyFieldEffectUsedThisTurn,
                    majesticTargetEffectUsedThisTurn = card.MajesticTargetEffectUsedThisTurn,
                    shootingStarExcavationUsedThisTurn = card.ShootingStarExcavationUsedThisTurn,
                    shootingStarDestructionNegateUsedThisTurn = card.ShootingStarDestructionNegateUsedThisTurn,
                    shootingStarAttackNegateUsedThisTurn = card.ShootingStarAttackNegateUsedThisTurn,
                    wasSynchroSummoned = card.WasSynchroSummoned,
                    silverwindProtectionTurn = card.SilverwindProtectionTurn,
                    returnAtEndPhaseTurn = card.ReturnAtEndPhaseTurn,
                    fusionMaterialIds = card.FusionMaterials.Select(material => GetInstanceId(material, instanceIds)).ToArray(),
                    equippedMonsterId = card.EquippedMonster == null
                        ? null
                        : GetInstanceId(card.EquippedMonster, instanceIds),
                    copiedEffectTargetId = GetInstanceId(card.CopiedEffectTarget, instanceIds)
                };
            }

            return snapshot;
        }

        internal static DuelEngine RestoreState(DuelStateSnapshot snapshot)
        {
            return RestoreState(snapshot, StarterCatalog.Find);
        }

        private static DuelEngine RestoreState(
            DuelStateSnapshot snapshot, Func<string, CardDefinition> resolveCardDefinition)
        {
            Dictionary<string, CardInstance> cards = snapshot.cards.ToDictionary(
                item => item.instanceId,
                item =>
                {
                    CardDefinition definition = resolveCardDefinition(item.cardId);
                    if (definition == null)
                        throw new InvalidOperationException("Cannot restore unknown card " + item.cardId + ".");
                    CardInstance card = new CardInstance(definition, item.faceUp, item.attackPosition)
                    {
                        HasAttacked = item.hasAttacked,
                        HasChangedPositionThisTurn = item.hasChangedPositionThisTurn,
                        EffectUsedThisTurn = item.effectUsedThisTurn,
                        EffectUsedOncePerDuel = item.effectUsedOncePerDuel,
                        StardustReturnPending = item.stardustReturnPending,
                        EffectsNegated = item.effectsNegated,
                        CannotBeDestroyedByCardEffects = item.cannotBeDestroyedByCardEffects,
                        EffectsNegatedUntilTurn = item.effectsNegatedUntilTurn,
                        WedgeCounters = item.wedgeCounters,
                        ExtraAttacksThisTurn = item.extraAttacksThisTurn,
                        LevelModifier = item.levelModifier,
                        DrillWarriorReturnTurn = item.drillWarriorReturnTurn,
                        DefenseModifier = item.defenseModifier,
                        DrillWarriorDirectAttackThisTurn = item.drillWarriorDirectAttackThisTurn,
                        NitroWarriorBoostReady = item.nitroWarriorBoostReady,
                        AttackBonus = item.attackBonus,
                        PermanentAttackBonus = item.permanentAttackBonus,
                        SummonedTurnNumber = item.summonedTurnNumber,
                        SpellCounters = item.spellCounters,
                        DestroyedOnFieldTurn = item.destroyedOnFieldTurn,
                        DestroyAtEndPhaseTurn = item.destroyAtEndPhaseTurn,
                        EffectDurationTurnsRemaining = item.effectDurationTurnsRemaining,
                        BanishWhenLeavingField = item.banishWhenLeavingField,
                        CannotBeTributedForTributeSummon = item.cannotBeTributedForTributeSummon,
                        CannotBeTributedForTributeSummonUntilTurn = item.cannotBeTributedForTributeSummonUntilTurn,
                        AncientFairySummonUsedThisTurn = item.ancientFairySummonUsedThisTurn,
                        AncientFairyFieldEffectUsedThisTurn = item.ancientFairyFieldEffectUsedThisTurn,
                        MajesticTargetEffectUsedThisTurn = item.majesticTargetEffectUsedThisTurn,
                        ShootingStarExcavationUsedThisTurn = item.shootingStarExcavationUsedThisTurn,
                        ShootingStarDestructionNegateUsedThisTurn = item.shootingStarDestructionNegateUsedThisTurn,
                        ShootingStarAttackNegateUsedThisTurn = item.shootingStarAttackNegateUsedThisTurn,
                        WasSynchroSummoned = item.wasSynchroSummoned,
                        SilverwindProtectionTurn = item.silverwindProtectionTurn,
                        ReturnAtEndPhaseTurn = item.returnAtEndPhaseTurn
                    };
                    return card;
                },
                StringComparer.Ordinal);

            foreach (DuelCardStateSnapshot cardState in snapshot.cards)
            {
                CardInstance card = cards[cardState.instanceId];
                card.NitroWarriorSecondAttackTarget = GetCard(cards, cardState.nitroWarriorSecondAttackTargetId);
                foreach (string materialId in cardState.fusionMaterialIds)
                    card.FusionMaterials.Add(cards[materialId]);
                if (!string.IsNullOrEmpty(cardState.equippedMonsterId))
                    card.EquippedMonster = cards[cardState.equippedMonsterId];
                card.CopiedEffectTarget = GetCard(cards, cardState.copiedEffectTargetId);
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
                witchEffectUsedThisTurn = player.WitchEffectUsedThisTurn,
                battleDamageProtectedTurn = player.BattleDamageProtectedTurn,
                cannotAttackUntilTurn = player.CannotAttackUntilTurn,
                cannotChangePositionUntilTurn = player.CannotChangePositionUntilTurn,
                noAttackTurnsRemaining = player.NoAttackTurnsRemaining,
                positionChangesLockedTurnsRemaining = player.PositionChangesLockedTurnsRemaining,
                battlePhaseSkippedTurn = player.BattlePhaseSkippedTurn
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
                WitchEffectUsedThisTurn = snapshot.witchEffectUsedThisTurn,
                BattleDamageProtectedTurn = snapshot.battleDamageProtectedTurn,
                CannotAttackUntilTurn = snapshot.cannotAttackUntilTurn,
                CannotChangePositionUntilTurn = snapshot.cannotChangePositionUntilTurn,
                NoAttackTurnsRemaining = snapshot.noAttackTurnsRemaining,
                PositionChangesLockedTurnsRemaining = snapshot.positionChangesLockedTurnsRemaining,
                BattlePhaseSkippedTurn = snapshot.battlePhaseSkippedTurn,
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
            if (CurrentPlayer.SummonsLockedThisTurn && !setFaceDown)
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
                selectedTributes.Any(material => !CurrentPlayer.MonsterZones.Contains(material) ||
                    material.CannotBeTributedForTributeSummon ||
                    turnNumber <= material.CannotBeTributedForTributeSummonUntilTurn))
                return Reject(tributeCount == 0
                    ? "This monster needs no Tributes. Clear your field selection first."
                    : "Select exactly " + tributeCount +
                      " monsters you control that can be Tributed for a Tribute Summon.");
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
                SendMonsterToGraveyardDeferred(CurrentPlayer, tribute);
            if (zone < 0)
                zone = FindEmptyMonsterZone(CurrentPlayer);

            CurrentPlayer.Hand.Remove(card);
            card.FaceUp = !setFaceDown;
            card.AttackPosition = !setFaceDown;
            CurrentPlayer.MonsterZones[zone] = card;
            card.SummonedTurnNumber = turnNumber;
            CurrentPlayer.NormalSummonsThisTurn++;
            Log(CurrentPlayer.Name + (setFaceDown ? " Set " : " Normal Summoned ") + card.Definition.DisplayName + ".");
            ResolveDeferredGraveyardEffects();
            if (!setFaceDown && selectedTributes.Count > 0)
                TributeSummoned?.Invoke(CurrentPlayer, card, selectedTributes);
            if (!setFaceDown)
            {
                ResolveSummonTriggers(CurrentPlayer, card, true);
                Action openSummonResponse = () => OpenResponseWindow(
                    GetOpponent(CurrentPlayer), CardEffect.DestroyAllMonsters, card,
                    () => { }, DestroyAllMonstersOnField);
                if (GraveyardSummonChoiceOpen)
                    pendingGraveyardSummonContinuation = openSummonResponse;
                else
                    openSummonResponse();
            }
            return true;
        }

        public bool TryChooseQuickdrawDiscard(CardInstance card)
        {
            if (!QuickdrawDiscardChoiceOpen || card == null ||
                !pendingQuickdrawOwner.Hand.Contains(card) ||
                card == pendingQuickdrawSource || !card.Definition.IsMonster)
                return Reject("Choose another Monster Card in your hand to discard for Quickdraw Synchron.");

            DuelPlayer owner = pendingQuickdrawOwner;
            CardInstance source = pendingQuickdrawSource;
            int zone = FindEmptyMonsterZone(owner);
            if (owner != CurrentPlayer || !owner.Hand.Contains(source) || zone < 0 ||
                owner.SummonsLockedThisTurn)
                return Reject("Quickdraw Synchron can no longer be Special Summoned.");

            pendingQuickdrawOwner = null;
            pendingQuickdrawSource = null;
            owner.Hand.Remove(card);
            SendCardToGraveyard(owner, card);
            PlaceSpecialSummon(owner, source, zone, true);
            return true;
        }

        public bool CancelQuickdrawDiscardChoice()
        {
            if (!QuickdrawDiscardChoiceOpen)
                return Reject("Quickdraw Synchron is not waiting for a discard choice.");
            pendingQuickdrawOwner = null;
            pendingQuickdrawSource = null;
            return true;
        }

        public bool TryChooseGraveyardSummon(CardInstance card)
        {
            bool isInEligibleGraveyard = pendingGraveyardSummonFromEitherGraveyard
                ? Human.Graveyard.Contains(card) || Opponent.Graveyard.Contains(card)
                : pendingGraveyardSummonOwner != null &&
                  pendingGraveyardSummonOwner.Graveyard.Contains(card);
            if (!GraveyardSummonChoiceOpen || card == null ||
                !pendingGraveyardSummonChoices.Contains(card) || !isInEligibleGraveyard)
                return Reject("Choose an eligible monster from your Graveyard.");

            DuelPlayer owner = pendingGraveyardSummonOwner;
            bool attackPosition = pendingGraveyardSummonAttackPosition;
            bool negateEffects = pendingGraveyardSummonNegatesEffects;
            bool fromEitherGraveyard = pendingGraveyardSummonFromEitherGraveyard;
            ClearGraveyardSummonChoice();
            bool summoned = fromEitherGraveyard
                ? SpecialSummonFromEitherGraveyard(owner, card, attackPosition)
                : SpecialSummonFromGraveyard(owner, card, attackPosition);
            if (!summoned)
                return Reject("That monster can no longer be Special Summoned.");
            if (negateEffects)
                card.EffectsNegated = true;
            CompleteGraveyardSummonChoice();
            return true;
        }

        public bool TrySkipGraveyardSummonChoice()
        {
            if (!GraveyardSummonChoiceOpen)
                return Reject("There is no Graveyard Summon choice to skip.");
            ClearGraveyardSummonChoice();
            CompleteGraveyardSummonChoice();
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
                    SendMonsterToGraveyardDeferred(CurrentPlayer, material);
                else
                    SendCardToGraveyard(CurrentPlayer, material, deferTriggers: true);
            }

            CurrentPlayer.Hand.Remove(ritualSpell);
            CurrentPlayer.Graveyard.Add(ritualSpell);
            CurrentPlayer.Hand.Remove(ritualMonster);
            ritualMonster.FaceUp = true;
            ritualMonster.AttackPosition = true;
            ritualMonster.SummonedTurnNumber = turnNumber;
            CurrentPlayer.MonsterZones[summonZone] = ritualMonster;
            Log(CurrentPlayer.Name + " Ritual Summoned " + ritualMonster.Definition.DisplayName + ".");
            ResolveDeferredGraveyardEffects();
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
                        BeginGraveyardSummonChoice(owner, summoned, item =>
                            IsBlackwing(item.Definition) && item.Definition.Level <= 4,
                            false, false);
                    break;
                case CardEffect.BlackwingMistral:
                    break;
                case CardEffect.DebrisDragonRevive:
                    if (normalSummon)
                        BeginGraveyardSummonChoice(owner, summoned,
                            item => item.Definition.IsMonster && item.Definition.Attack <= 500,
                            true, true);
                    break;
                case CardEffect.JunkSynchronRevive:
                    if (normalSummon)
                        BeginGraveyardSummonChoice(owner, summoned,
                            item => item.Definition.IsMonster && item.Definition.Level <= 2,
                            false, true);
                    break;
                case CardEffect.SynchronExplorer:
                    if (normalSummon)
                        BeginGraveyardSummonChoice(owner, summoned, item =>
                            item.Definition.DisplayName.IndexOf("Synchron",
                                StringComparison.OrdinalIgnoreCase) >= 0,
                            true, true);
                    break;
                case CardEffect.JunkWarriorBoost:
                    summoned.AttackBonus += owner.MonsterZones
                        .Where(item => item != null && item != summoned && item.Definition.Level <= 2)
                        .Sum(item => item.CurrentAttack);
                    break;
                case CardEffect.BlackRoseDragon:
                    foreach (CardInstance fieldCard in GetAllFieldCards())
                        DestroyFieldCard(fieldCard);
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

        private void BeginGraveyardSummonChoice(
            DuelPlayer owner,
            CardInstance source,
            Func<CardInstance, bool> filter,
            bool attackPosition,
            bool negateEffects)
        {
            if (owner.SummonsLockedThisTurn || FindEmptyMonsterZone(owner) < 0)
                return;
            List<CardInstance> choices = owner.Graveyard.Where(card =>
                card.Definition.IsMonster && filter(card)).ToList();
            if (choices.Count == 0)
                return;

            if (owner == Human && !IsAiVsAi)
            {
                pendingGraveyardSummonOwner = owner;
                pendingGraveyardSummonSource = source;
                pendingGraveyardSummonChoices = choices;
                pendingGraveyardSummonAttackPosition = attackPosition;
                pendingGraveyardSummonNegatesEffects = negateEffects;
                pendingGraveyardSummonFromEitherGraveyard = false;
                return;
            }

            CardInstance selected = choices
                .OrderByDescending(DuelAiEvaluator.CardValue)
                .First();
            if (SpecialSummonFromGraveyard(owner, selected, attackPosition) && negateEffects)
                selected.EffectsNegated = true;
        }

        private void ClearGraveyardSummonChoice()
        {
            pendingGraveyardSummonOwner = null;
            pendingGraveyardSummonSource = null;
            pendingGraveyardSummonChoices.Clear();
            pendingGraveyardSummonAttackPosition = false;
            pendingGraveyardSummonNegatesEffects = false;
            pendingGraveyardSummonFromEitherGraveyard = false;
        }

        private void CompleteGraveyardSummonChoice()
        {
            Action continuation = pendingGraveyardSummonContinuation;
            pendingGraveyardSummonContinuation = null;
            continuation?.Invoke();
        }

        private static int GetActivationDiscardCount(
            CardEffect effect, DuelPlayer owner, CardInstance source)
        {
            switch (effect)
            {
                case CardEffect.DarkCoreBanish:
                case CardEffect.RaigekiBreak:
                case CardEffect.DiscardOneDestroyFaceUpOpponentMonsters:
                    return 1;
                case CardEffect.DrawThreeDiscardTwo:
                    return 2;
                case CardEffect.OneForOne:
                    return owner.Hand.Any(card => card != source && card.Definition.IsMonster) ? 1 : 0;
                default:
                    return 0;
            }
        }

        private static bool IsEligibleActivationDiscard(
            DuelPlayer owner, CardInstance source, CardInstance card, bool monstersOnly)
        {
            return card != null && card != source && owner.Hand.Contains(card) &&
                   (!monstersOnly || card.Definition.IsMonster);
        }

        private void BeginActivationDiscardChoice(
            CardInstance source,
            CardInstance target,
            CardInstance optionalCard,
            int count,
            bool monstersOnly)
        {
            pendingActivationDiscardSource = source;
            pendingActivationDiscardTarget = target;
            pendingActivationDiscardOptionalCard = optionalCard;
            pendingActivationDiscardCount = count;
            pendingActivationDiscardMonstersOnly = monstersOnly;
        }

        private void ClearActivationDiscardChoice()
        {
            pendingActivationDiscardSource = null;
            pendingActivationDiscardTarget = null;
            pendingActivationDiscardOptionalCard = null;
            pendingActivationDiscardCount = 0;
            pendingActivationDiscardMonstersOnly = false;
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

        public bool TryActivateAncientFairyDragonSummon(CardInstance source, CardInstance monster)
        {
            if (!CanActInMainPhase())
                return Reject("Ancient Fairy Dragon's Special Summon effect is available during your Main Phase.");
            if (source == null || !CurrentPlayer.MonsterZones.Contains(source) || !source.FaceUp ||
                source.Definition.Effect != CardEffect.AncientFairyDragon ||
                source.AncientFairySummonUsedThisTurn)
                return Reject("Ancient Fairy Dragon's Special Summon effect is unavailable.");
            if (monster == null || !CurrentPlayer.Hand.Contains(monster) ||
                !monster.Definition.IsMonster || monster.Definition.Level > 4)
                return Reject("Select a Level 4 or lower monster in your hand.");
            if (FindEmptyMonsterZone(CurrentPlayer) < 0 || CurrentPlayer.SummonsLockedThisTurn)
                return Reject("You need an open Monster Zone and permission to Special Summon.");

            source.AncientFairySummonUsedThisTurn = true;
            CurrentPlayer.BattlePhaseSkippedTurn = turnNumber;
            PlaceSpecialSummon(CurrentPlayer, monster, FindEmptyMonsterZone(CurrentPlayer), true);
            Log(CurrentPlayer.Name + " skipped the Battle Phase because of Ancient Fairy Dragon.");
            return true;
        }

        public bool TryActivateAncientFairyDragonFieldEffect(CardInstance source)
        {
            if (!CanActInMainPhase())
                return Reject("Ancient Fairy Dragon's Field Spell effect is available during your Main Phase.");
            if (source == null || !CurrentPlayer.MonsterZones.Contains(source) || !source.FaceUp ||
                source.Definition.Effect != CardEffect.AncientFairyDragon ||
                source.AncientFairyFieldEffectUsedThisTurn)
                return Reject("Ancient Fairy Dragon's Field Spell effect is unavailable.");

            List<CardInstance> fieldSpells = new[] { Human.FieldSpellZone, Opponent.FieldSpellZone }
                .Where(card => card != null && card.Definition.Subtype == CardSubtype.Field).ToList();
            if (fieldSpells.Count == 0)
                return Reject("There are no Field Spells in the Field Zones to destroy.");

            source.AncientFairyFieldEffectUsedThisTurn = true;
            string[] destroyedNames = fieldSpells.Select(card => card.Definition.DisplayName).ToArray();
            foreach (CardInstance fieldSpell in fieldSpells)
                DestroyFieldCard(fieldSpell);
            CurrentPlayer.LifePoints += 1000;
            Log(CurrentPlayer.Name + " gained 1000 LP with Ancient Fairy Dragon.");

            if (!CurrentPlayer.Deck.Any(candidate =>
                    candidate.Definition.Subtype == CardSubtype.Field &&
                    !destroyedNames.Contains(candidate.Definition.DisplayName)))
                return true;
            return BeginDeckChoice(CurrentPlayer, source,
                candidate => candidate.Definition.Subtype == CardSubtype.Field &&
                             !destroyedNames.Contains(candidate.Definition.DisplayName),
                false, "You may add a differently named Field Spell from your Deck to your hand.",
                optional: true);
        }

        public bool TryActivateBlackwingSilverwind(CardInstance source, IReadOnlyList<CardInstance> targets)
        {
            if (!CanActInMainPhase())
                return Reject("Silverwind's Synchro effect is available during your Main Phase.");
            if (source == null || !CurrentPlayer.MonsterZones.Contains(source) || !source.FaceUp ||
                source.Definition.Effect != CardEffect.BlackwingSilverwind ||
                source.SummonedTurnNumber != turnNumber || !source.WasSynchroSummoned ||
                source.EffectUsedThisTurn)
                return Reject("Silverwind's Synchro effect is unavailable.");
            List<CardInstance> chosen = targets == null
                ? new List<CardInstance>()
                : targets.Distinct().ToList();
            if (chosen.Count > 2 || chosen.Any(card =>
                    (GetMonsterOwner(card) == null) || !card.FaceUp ||
                    card.Definition.Defense >= source.CurrentAttack))
                return Reject("Choose up to two face-up monsters with DEF lower than Silverwind's ATK.");

            source.EffectUsedThisTurn = true;
            foreach (CardInstance monster in chosen)
                DestroyMonsterByCardEffect(GetMonsterOwner(monster), monster);
            CurrentPlayer.BattlePhaseSkippedTurn = turnNumber;
            CheckWinner();
            return true;
        }

        public bool TryActivateDustTornado(
            CardInstance source, CardInstance target, CardInstance cardToSet = null)
        {
            if (source == null || source.Definition.Effect != CardEffect.DustTornado)
                return Reject("Select Dust Tornado.");
            return TryActivate(source, target, cardToSet);
        }

        public bool TryActivateCopiedMonsterEffect(
            CardInstance majesticStarDragon, CardInstance effectTarget = null)
        {
            if (!CanActInMainPhase())
                return Reject("Majestic Star Dragon's copied effect is available during your Main Phase.");
            if (majesticStarDragon == null || !CurrentPlayer.MonsterZones.Contains(majesticStarDragon) ||
                !majesticStarDragon.FaceUp ||
                majesticStarDragon.Definition.Effect != CardEffect.MajesticStarDragon ||
                majesticStarDragon.CopiedEffectTarget == null ||
                majesticStarDragon.EffectUsedThisTurn)
                return Reject("Majestic Star Dragon has no unused copied monster effect.");

            CardDefinition copiedDefinition = majesticStarDragon.CopiedEffectTarget.Definition;
            if (copiedDefinition.Effect == CardEffect.None)
                return Reject("The targeted monster has no implemented activated effect to copy.");
            if (!ApplyEffect(CurrentPlayer, copiedDefinition, effectTarget, majesticStarDragon))
                return false;
            majesticStarDragon.EffectUsedThisTurn = true;
            majesticStarDragon.CopiedEffectTarget = null;
            Log(CurrentPlayer.Name + " activated the copied " + copiedDefinition.DisplayName + " effect with Majestic Star Dragon.");
            return true;
        }

        public bool TryActivate(CardInstance card, CardInstance target = null, CardInstance optionalCard = null)
        {
            return TryActivateCore(card, target, optionalCard, null);
        }

        public bool TryActivateWithDiscards(
            CardInstance card,
            CardInstance target,
            CardInstance optionalCard,
            IReadOnlyList<CardInstance> discardChoices)
        {
            return TryActivateCore(card, target, optionalCard, discardChoices);
        }

        public bool TryChooseActivationDiscards(IReadOnlyList<CardInstance> cards)
        {
            if (!ActivationDiscardChoiceOpen || cards == null ||
                cards.Count != pendingActivationDiscardCount ||
                cards.Distinct().Count() != cards.Count ||
                cards.Any(card => !AvailableActivationDiscards.Contains(card)))
                return Reject("Choose the required number of eligible cards to discard.");

            CardInstance source = pendingActivationDiscardSource;
            CardInstance target = pendingActivationDiscardTarget;
            CardInstance optionalCard = pendingActivationDiscardOptionalCard;
            List<CardInstance> choices = cards.ToList();
            ClearActivationDiscardChoice();
            return TryActivateWithDiscards(source, target, optionalCard, choices);
        }

        private bool TryActivateCore(
            CardInstance card,
            CardInstance target,
            CardInstance optionalCard,
            IReadOnlyList<CardInstance> discardChoices)
        {
            if (card == null)
                return Reject("Select a card to activate.");

            bool fromHand = CurrentPlayer.Hand.Contains(card);
            bool fromGraveyard = CurrentPlayer.Graveyard.Contains(card);
            bool fromSetTrap = CurrentPlayer.SpellTrapZones.Contains(card) && !card.FaceUp &&
                               card.Definition.Kind == CardKind.Trap;
            bool handMonsterEffect = fromHand && CanActivateMonsterEffectFromHand(card.Definition.Effect);
            bool graveyardMonsterEffect = fromGraveyard &&
                CanActivateMonsterEffectFromGraveyard(card.Definition.Effect);
            bool battleHandEffect = fromHand && card.Definition.Effect == CardEffect.BlackwingBattleBoost &&
                                    Phase == DuelPhase.Battle;
            bool handTrapEffect = fromHand && card.Definition.CardId == "59839761" &&
                                  CurrentPlayer.MonsterZones.Count(item =>
                                      item != null && IsBlackwing(item.Definition)) == 3;
            bool quickPlayDuringBattle = fromHand && card.Definition.Kind == CardKind.Spell &&
                                         card.Definition.Subtype == CardSubtype.QuickPlay &&
                                         CurrentPlayer == Human &&
                                         (Phase == DuelPhase.Battle || Phase == DuelPhase.End);
            if (!CanActInMainPhase() && !quickPlayDuringBattle && !battleHandEffect && !handTrapEffect)
                return Reject("You can only activate cards during your Main Phase, except a Quick-Play Spell during your turn.");

            bool monsterEffect = CurrentPlayer.MonsterZones.Contains(card) && card.FaceUp &&
                                card.Definition.IsMonster && card.Definition.Effect != CardEffect.None;
            bool handSpell = fromHand && card.Definition.Kind == CardKind.Spell;
            if (!handSpell && !monsterEffect && !fromSetTrap &&
                !handMonsterEffect && !graveyardMonsterEffect && !battleHandEffect && !handTrapEffect)
            {
                return Reject("Select a Spell, an activatable monster, or a Set Trap.");
            }
            bool separateOncePerTurnEffect = card.Definition.Effect == CardEffect.MajesticStarDragon ||
                card.Definition.Effect == CardEffect.ShootingStarDragon;
            if (monsterEffect && card.EffectUsedThisTurn && !separateOncePerTurnEffect)
                return Reject("That monster's effect has already been used this turn.");
            if (monsterEffect && card.Definition.Effect == CardEffect.MajesticStarDragon &&
                card.MajesticTargetEffectUsedThisTurn)
                return Reject("Majestic Star Dragon has already negated a monster this turn.");
            if (monsterEffect && card.Definition.Effect == CardEffect.ShootingStarDragon &&
                card.ShootingStarExcavationUsedThisTurn)
                return Reject("Shooting Star Dragon's excavation effect has already been used this turn.");
            if (monsterEffect && card.EffectsNegated)
                return Reject("That monster's effects are negated.");
            if (fromSetTrap && IsTrapActivationBlockedByJinzo(card))
                return Reject("Jinzo prevents Trap Cards from being activated.");
            if (IsCardEffectActivationRestricted(CurrentPlayer, card.Definition))
                return Reject("You cannot activate " + temporarilyDisabledCardName +
                    " or its effects for the rest of this turn.");

            if (monsterEffect && (card.Definition.Effect == CardEffect.SearchMagnet ||
                                  card.Definition.Effect == CardEffect.KnightsCombo ||
                                  card.Definition.Effect == CardEffect.JinzoTrapLock ||
                                  card.Definition.Effect == CardEffect.BlackwingMistral ||
                                  card.Definition.Effect == CardEffect.BlackwingArmedWing ||
                                  card.Definition.Effect == CardEffect.MajesticDragon))
                return Reject("This effect resolves when its monster is Summoned.");
            if (card.EffectUsedOncePerDuel)
                return Reject("That monster's effect has already been used this Duel.");
            if (battleHandEffect && (target == null ||
                !CurrentPlayer.MonsterZones.Contains(target) || !IsBlackwing(target.Definition) ||
                !target.HasAttacked))
                return Reject("Select a Blackwing monster that has declared an attack this turn.");
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
            if (card.Definition.Effect == CardEffect.NoblemanOfCrossout &&
                (target == null || !IsOnMonsterField(target) || target.FaceUp))
                return Reject("Select a face-down monster on the field.");
            if ((card.Definition.Effect == CardEffect.CompulsoryEvacuationDevice ||
                 card.Definition.Effect == CardEffect.DarkCoreBanish) &&
                (target == null || !IsOnMonsterField(target)))
                return Reject("Select a monster on the field.");
            if (card.Definition.Effect == CardEffect.DarkCoreBanish &&
                (!target.FaceUp || CurrentPlayer.Hand.Count < 2))
                return Reject("Dark Core needs a face-up monster and another card in your hand to discard.");
            if (card.Definition.Effect == CardEffect.RaigekiBreak &&
                (target == null || !GetAllFieldCards().Contains(target) || target == card ||
                 CurrentPlayer.Hand.Count == 0))
                return Reject("Raigeki Break needs a card to discard and another card on the field to destroy.");
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
                (target == null || !CurrentPlayer.Graveyard.Contains(target) ||
                 !target.Definition.IsMonster || FindEmptyMonsterZone(CurrentPlayer) < 0))
                return Reject("Select a monster in your Graveyard and make sure you have an open Monster Zone.");
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
            if (card.Definition.Effect == CardEffect.DustTornado &&
                (target == null || GetSpellTrapOwner(target) != GetOpponent(CurrentPlayer)))
                return Reject("Dust Tornado must target a Spell or Trap your opponent controls.");
            if (card.Definition.Effect == CardEffect.DustTornado && optionalCard != null &&
                (!CurrentPlayer.Hand.Contains(optionalCard) ||
                 (optionalCard.Definition.Kind != CardKind.Spell &&
                  optionalCard.Definition.Kind != CardKind.Trap)))
                return Reject("Only a Spell or Trap in your hand can be Set by Dust Tornado.");
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

            int requiredDiscardCount = GetActivationDiscardCount(card.Definition.Effect, CurrentPlayer, card);
            bool monstersOnlyDiscard = card.Definition.Effect == CardEffect.OneForOne;
            if (requiredDiscardCount > 0 && CurrentPlayer == Human && !IsAiVsAi &&
                discardChoices == null)
            {
                BeginActivationDiscardChoice(card, target, optionalCard, requiredDiscardCount,
                    monstersOnlyDiscard);
                return true;
            }
            if (requiredDiscardCount > 0 && discardChoices != null &&
                (discardChoices.Count != requiredDiscardCount ||
                 discardChoices.Distinct().Count() != discardChoices.Count ||
                 discardChoices.Any(discard => !IsEligibleActivationDiscard(
                     CurrentPlayer, card, discard, monstersOnlyDiscard))))
                return Reject("Choose the required number of eligible cards to discard.");

            CardEffect counterType = handSpell ? CardEffect.CounterSpell :
                monsterEffect ? CardEffect.CounterMonsterEffect :
                fromSetTrap ? CardEffect.CounterTrap : CardEffect.None;
            if (counterType != CardEffect.None)
            {
                DuelPlayer owner = CurrentPlayer;
                Action cancelActivation = () =>
                {
                    ConsumeActivatedCard(owner, card, handSpell, monsterEffect, fromSetTrap);
                    Log(card.Definition.DisplayName + "'s effect was negated.");
                };
                ResponseAttempt response = OpenResponseWindow(GetOpponent(owner), counterType,
                    card, () => ResolveActivatedEffect(owner, card, target, handSpell, monsterEffect,
                        fromSetTrap, optionalCard, discardChoices), cancelActivation);
                if (response == ResponseAttempt.Waiting)
                    return true;
                if (response == ResponseAttempt.Cancelled)
                {
                    cancelActivation();
                    return true;
                }
            }

            return ResolveActivatedEffect(CurrentPlayer, card, target, handSpell, monsterEffect,
                fromSetTrap, optionalCard, discardChoices);
        }

        private bool ResolveActivatedEffect(
            DuelPlayer owner, CardInstance card, CardInstance target, bool handSpell, bool monsterEffect,
            bool fromSetTrap = false, CardInstance optionalCard = null,
            IReadOnlyList<CardInstance> discardChoices = null)
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
                bool effectApplied = card.Definition.Effect == CardEffect.DustTornado
                    ? ResolveDustTornado(owner, card, target, optionalCard)
                    : ApplyEffect(owner, card.Definition, target, card, discardChoices);
                if (!fieldBoostWaitingForMonster && !effectApplied)
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
            if (card.Definition.CardId == "59839761" && owner.Hand.Contains(card))
            {
                owner.Hand.Remove(card);
                owner.Graveyard.Add(card);
            }
            if (monsterEffect && card.Definition.Effect != CardEffect.MajesticStarDragon &&
                card.Definition.Effect != CardEffect.ShootingStarDragon)
                card.EffectUsedThisTurn = true;
            if (handSpell)
                NotifySpellActivated(owner);
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
            if (monsterEffect && card.Definition.Effect != CardEffect.MajesticStarDragon &&
                card.Definition.Effect != CardEffect.ShootingStarDragon)
                card.EffectUsedThisTurn = true;
        }

        public bool TrySynchroSummon(CardDefinition synchro, IReadOnlyList<CardInstance> materials)
        {
            if (!CanActInMainPhase())
                return Reject("Synchro Summons are only available during your Main Phase.");
            return ResolveSynchroSummon(CurrentPlayer, synchro, materials);
        }

        public bool TryQuickSynchroSummon(
            CardInstance formulaSynchron, CardDefinition synchro, IReadOnlyList<CardInstance> materials)
        {
            DuelPlayer owner = GetMonsterOwner(formulaSynchron);
            if (owner == null || owner == CurrentPlayer || Phase != DuelPhase.MainOne &&
                Phase != DuelPhase.MainTwo)
                return Reject("Formula Synchron's Quick Effect is available during the opponent's Main Phase.");
            if (!formulaSynchron.FaceUp || formulaSynchron.Definition.Effect != CardEffect.FormulaSynchronDraw ||
                materials == null || !materials.Contains(formulaSynchron))
                return Reject("Include your face-up Formula Synchron as Synchro Material.");
            if (synchro == null || !owner.ExtraDeck.Any(card => card.Definition == synchro))
                return Reject("Select a Synchro Monster from your Extra Deck.");
            return ResolveSynchroSummon(owner, synchro, materials);
        }

        public bool TryRespondWithFormulaSynchron(
            CardInstance formulaSynchron, CardDefinition synchro, IReadOnlyList<CardInstance> materials)
        {
            if (!ResponseWindowOpen ||
                !GetResponseCards(Human, pendingResponseType, pendingResponseSourceCard).Contains(formulaSynchron))
                return Reject("Formula Synchron cannot respond to the current chain.");
            Action resolvePending = pendingResponsePass;
            if (!TryQuickSynchroSummon(formulaSynchron, synchro, materials))
                return false;
            ClearResponseWindow();
            if (resolvePending != null)
                resolvePending();
            return true;
        }

        private CardDefinition FindQuickSynchro(
            DuelPlayer owner, CardInstance formulaSynchron, out List<CardInstance> materials)
        {
            materials = null;
            if (owner == null || formulaSynchron == null || !owner.MonsterZones.Contains(formulaSynchron))
                return null;
            List<CardInstance> field = owner.MonsterZones
                .Where(card => card != null && card.FaceUp).ToList();
            foreach (CardInstance extra in owner.ExtraDeck
                         .Where(card => card.Definition.Kind == CardKind.Synchro)
                         .OrderByDescending(card => card.Definition.Attack))
            {
                for (int mask = 1; mask < (1 << field.Count); mask++)
                {
                    List<CardInstance> chosen = new List<CardInstance>();
                    for (int bit = 0; bit < field.Count; bit++)
                    {
                        if ((mask & (1 << bit)) != 0)
                            chosen.Add(field[bit]);
                    }
                    if (chosen.Contains(formulaSynchron) &&
                        IsValidSynchroMaterial(extra.Definition, chosen, owner))
                    {
                        materials = chosen;
                        return extra.Definition;
                    }
                }
            }
            return null;
        }

        private bool ResolveMajesticStarNegate(DuelPlayer owner, CardInstance majestic)
        {
            Action negate = pendingResponseTrap;
            CardInstance source = pendingResponseSourceCard;
            ClearResponseWindow();
            RemoveFromMonsterZones(owner, majestic);
            owner.Graveyard.Add(majestic);
            if (negate != null)
                negate();
            foreach (CardInstance fieldCard in GetAllFieldCards()
                         .Where(card => GetCardController(card) == GetOpponent(owner)).ToList())
                DestroyFieldCard(fieldCard);
            CheckWinner();
            Log("Majestic Star Dragon negated the activation and destroyed all cards its opponent controlled.");
            return true;
        }

        private bool ResolveShootingStarDestructionNegate(DuelPlayer owner, CardInstance shootingStar)
        {
            if (!IsDestructionEffect(pendingResponseSourceCard) ||
                shootingStar.ShootingStarDestructionNegateUsedThisTurn)
                return Reject("Shooting Star Dragon can only negate a destruction effect once per turn.");
            Action negate = pendingResponseTrap;
            CardInstance source = pendingResponseSourceCard;
            ClearResponseWindow();
            shootingStar.ShootingStarDestructionNegateUsedThisTurn = true;
            shootingStar.EffectUsedThisTurn = true;
            if (negate != null)
                negate();
            DuelPlayer sourceOwner = GetCardController(source);
            if (sourceOwner != null)
                DestroyFieldCard(source);
            CheckWinner();
            Log("Shooting Star Dragon negated and destroyed " + source.Definition.DisplayName + "'s effect.");
            return true;
        }

        private bool ResolveShootingStarAttackNegate(DuelPlayer owner, CardInstance shootingStar)
        {
            if (shootingStar.ShootingStarAttackNegateUsedThisTurn)
                return Reject("Shooting Star Dragon's attack-negation effect has already been used this turn.");
            ClearResponseWindow();
            RemoveFromMonsterZones(owner, shootingStar);
            owner.Banished.Add(shootingStar);
            shootingStar.ShootingStarAttackNegateUsedThisTurn = true;
            shootingStar.ReturnAtEndPhaseTurn = turnNumber;
            Log("Shooting Star Dragon banished itself to negate the attack.");
            return true;
        }

        private bool ResolveSynchroSummon(
            DuelPlayer owner, CardDefinition synchro, IReadOnlyList<CardInstance> materials)
        {
            if (owner.SummonsLockedThisTurn)
                return Reject("A card effect prevents you from Special Summoning this turn.");
            if (synchro == null || synchro.Kind != CardKind.Synchro ||
                !owner.ExtraDeck.Any(card => card.Definition == synchro))
                return Reject("Select a Synchro Monster from your Extra Deck.");
            List<CardInstance> chosen = materials == null
                ? new List<CardInstance>()
                : materials.Distinct().ToList();
            if (!IsValidSynchroMaterial(synchro, chosen, owner))
                return Reject("Choose face-up monsters with one Tuner and an exact total Level of " + synchro.Level + ".");

            foreach (CardInstance material in chosen)
                SendMonsterToGraveyardDeferred(owner, material);
            int zone = FindEmptyMonsterZone(owner);
            CardInstance summoned = new CardInstance(synchro) { SummonedTurnNumber = turnNumber };
            summoned.WasSynchroSummoned = true;
            owner.MonsterZones[zone] = summoned;
            Log(owner.Name + " Synchro Summoned " + synchro.DisplayName + "!");
            ResolveSummonTriggers(owner, summoned, false);
            ResolveSynchroSummonTriggers();
            if (synchro.Effect == CardEffect.FormulaSynchronDraw)
                DrawCards(owner, 1);
            if (synchro.Effect == CardEffect.LifeStreamSetLifePoints)
                owner.LifePoints = 4000;
            if (synchro.Effect == CardEffect.StardustDragon)
            {
                CardInstance xiaolong = owner.Graveyard.FirstOrDefault(item =>
                    item.Definition.Effect == CardEffect.StardustXiaolong);
                if (xiaolong != null)
                    SpecialSummonFromGraveyard(owner, xiaolong, true);
            }
            ResolveSynchroMaterialEffects(owner, chosen, summoned);
            ResolveDeferredGraveyardEffects();
            return true;
        }

        public bool TryAttack(CardInstance attacker, CardInstance target)
        {
            if (SearchChoiceOpen || PowerToolChoiceOpen || QuickdrawDiscardChoiceOpen ||
                GraveyardSummonChoiceOpen || ActivationDiscardChoiceOpen ||
                (!IsAiVsAi && CurrentPlayer != Human) || Phase != DuelPhase.Battle)
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
            CompleteDeckChoice(owner, card, pendingDeckChoiceToGraveyard,
                pendingDeckChoiceSanganRestriction, pendingDeckChoiceSpecialSummon,
                pendingDeckChoiceNegatesEffects);
            ClearDeckChoice();
            return true;
        }

        public bool TrySkipSearchChoice()
        {
            if (!SearchChoiceOpen || !pendingDeckChoiceOptional)
                return Reject("This effect requires you to choose a card from the Deck.");
            ClearDeckChoice();
            return true;
        }

        public bool TryChoosePowerToolCards(IReadOnlyList<CardInstance> cards)
        {
            if (!PowerToolChoiceOpen || cards == null || cards.Count != 3 ||
                cards.Distinct().Count() != 3 ||
                cards.Any(card => !pendingPowerToolChoices.Contains(card) ||
                    card.Definition.Kind != CardKind.Spell ||
                    card.Definition.Subtype != CardSubtype.Equip))
                return Reject("Reveal exactly three Equip Spell Cards from your Deck.");

            DuelPlayer owner = GetMonsterOwner(pendingPowerToolChoiceSource);
            if (owner == null || pendingPowerToolChoices.Any(card => !owner.Deck.Contains(card)))
                return Reject("Power Tool Dragon's Deck choices are no longer available.");
            CardInstance selected = cards[random.Next(cards.Count)];
            foreach (CardInstance equip in cards)
                owner.Deck.Remove(equip);
            owner.Hand.Add(selected);
            owner.Deck.AddRange(cards.Where(card => card != selected));
            Shuffle(owner.Deck);
            pendingPowerToolChoiceSource = null;
            pendingPowerToolChoices.Clear();
            Log(owner.Name + " randomly added an Equip Spell revealed by Power Tool Dragon.");
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

        private bool ResolveDustTornado(
            DuelPlayer owner, CardInstance source, CardInstance target, CardInstance cardToSet)
        {
            DuelPlayer targetOwner = GetSpellTrapOwner(target);
            if (targetOwner == null || targetOwner == owner)
                return Reject("Dust Tornado must target a Spell or Trap your opponent controls.");

            if (targetOwner.FieldSpellZone == target)
                targetOwner.FieldSpellZone = null;
            else
                RemoveFromSpellTrapZones(targetOwner, target);
            targetOwner.Graveyard.Add(target);
            Log(target.Definition.DisplayName + " was destroyed by Dust Tornado.");

            if (cardToSet == null)
                return true;
            if (!owner.Hand.Contains(cardToSet) ||
                (cardToSet.Definition.Kind != CardKind.Spell && cardToSet.Definition.Kind != CardKind.Trap))
                return Reject("Only a Spell or Trap in your hand can be Set by Dust Tornado.");
            int zone = FindEmptySpellTrapZone(owner);
            if (zone < 0)
                return true;
            owner.Hand.Remove(cardToSet);
            cardToSet.FaceUp = false;
            owner.SpellTrapZones[zone] = cardToSet;
            Log(owner.Name + " Set " + cardToSet.Definition.DisplayName + " with Dust Tornado.");
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
            if (CurrentPlayer.SummonsLockedThisTurn)
                return Reject("A card effect prevents you from Summoning monsters this turn.");
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
            ResolveFlipEffect(CurrentPlayer, card);
            return true;
        }

        public void AdvancePhase()
        {
            if (!IsHumanActionWindow || ResponseWindowOpen || SearchChoiceOpen || PowerToolChoiceOpen ||
                QuickdrawDiscardChoiceOpen || GraveyardSummonChoiceOpen ||
                ActivationDiscardChoiceOpen)
                return;
            AdvanceToNextPhase();
        }

        public bool EndHumanTurn()
        {
            if (!IsHumanActionWindow || ResponseWindowOpen || SearchChoiceOpen || PowerToolChoiceOpen ||
                QuickdrawDiscardChoiceOpen || GraveyardSummonChoiceOpen ||
                ActivationDiscardChoiceOpen)
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
            if (target == DuelPhase.Battle && CurrentPlayer.BattlePhaseSkippedTurn == turnNumber)
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
                    ResolveStandbyEffects(CurrentPlayer);
                    break;
                case DuelPhase.Standby:
                    SetPhase(DuelPhase.MainOne);
                    Log("Main Phase 1. Choose an action.");
                    break;
                case DuelPhase.MainOne:
                    if (CurrentPlayer.BattlePhaseSkippedTurn == turnNumber)
                    {
                        SetPhase(DuelPhase.MainTwo);
                        Log("Battle Phase skipped. Main Phase 2.");
                    }
                    else
                    {
                        SetPhase(DuelPhase.Battle);
                        Log("Battle Phase. Select one of your monsters, then a target (or Direct Attack).");
                    }
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
            if (Phase == DuelPhase.GameOver || GraveyardSummonChoiceOpen ||
                ActivationDiscardChoiceOpen ||
                (!IsAiVsAi && pendingAiTurnActor == null))
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
                Human.Hand.Remove(trap);
                Human.Graveyard.Add(trap);
                Log(Human.Name + " discarded " + trap.Definition.DisplayName + " for " +
                    response.Definition.DisplayName + ".");
                if (IsDarkPaladin(response))
                {
                    Action negateActivation = pendingResponseTrap;
                    response.EffectUsedThisTurn = true;
                    ClearResponseWindow();
                    if (negateActivation != null)
                        negateActivation();
                    return true;
                }
                responseDiscardCostPaid = true;
                responseEffectAwaitingDiscard = null;
                responseDiscardReason = null;
                return TryRespondWithTrap(response);
            }

            if (!GetResponseCards(Human, pendingResponseType, pendingResponseSourceCard).Contains(trap))
                return Reject("That card cannot respond to the current event.");
            if (trap.Definition.Effect == CardEffect.DustTornado)
            {
                pendingDustTornadoResponse = trap;
                Log("Select an opposing Spell/Trap for Dust Tornado.");
                return true;
            }
            if (trap.Definition.Effect == CardEffect.MajesticStarDragon)
                return ResolveMajesticStarNegate(Human, trap);
            if (trap.Definition.Effect == CardEffect.ShootingStarDragon)
            {
                if (pendingResponseType == CardEffect.StopAttack)
                    return ResolveShootingStarAttackNegate(Human, trap);
                return ResolveShootingStarDestructionNegate(Human, trap);
            }
            if (trap.Definition.Effect == CardEffect.FormulaSynchronDraw)
            {
                CardDefinition synchro = FindQuickSynchro(Human, trap, out List<CardInstance> materials);
                if (synchro == null)
                    return Reject("Formula Synchron has no valid Synchro Summon available.");
                return TryRespondWithFormulaSynchron(trap, synchro, materials);
            }
            if (trap.Definition.Effect == CardEffect.StarlightRoad &&
                (pendingResponseSourceCard == null ||
                 !WouldDestroyMultipleOwnedCards(pendingResponseSourceCard, Human)))
                return Reject("Starlight Road can only respond to an effect that would destroy multiple cards you control.");
            if (trap.Definition.Effect == CardEffect.StardustDragon)
            {
                if (!IsDestructionEffect(pendingResponseSourceCard))
                    return Reject("Stardust Dragon can only respond to an effect that would destroy cards.");
                Action negate = pendingResponseTrap;
                ClearResponseWindow();
                RemoveFromMonsterZones(Human, trap);
                Human.Graveyard.Add(trap);
                trap.StardustReturnPending = true;
                if (negate != null)
                    negate();
                return true;
            }
            if (IsDarkPaladin(trap))
            {
                if (Human.Hand.Count == 0)
                    return Reject("Dark Paladin needs a card in your hand to discard.");
                responseEffectAwaitingDiscard = trap;
                responseDiscardReason = trap.Definition.DisplayName;
                Log(trap.Definition.DisplayName + " is waiting for you to select a card to discard.");
                return true;
            }
            if (!Human.SpellTrapZones.Contains(trap) || trap.FaceUp)
                return Reject("That card is not a Set Trap.");
            bool requiresDiscard = trap.Definition.Effect == CardEffect.CounterSpell ||
                                  trap.Definition.Effect == CardEffect.CounterMonsterEffect;
            if (requiresDiscard && !responseDiscardCostPaid)
            {
                if (Human.Hand.Count == 0)
                    return Reject(trap.Definition.DisplayName + " needs a card in your hand to discard.");
                responseEffectAwaitingDiscard = trap;
                responseDiscardReason = trap.Definition.DisplayName;
                Log(trap.Definition.DisplayName + " is waiting for you to select a card to discard.");
                return true;
            }

            Action trapResolution = pendingResponseTrap;
            Action counteredResolution = pendingResponseCountered;
            CardInstance sourceCard = pendingResponseSourceCard;
            CardEffect responseType = pendingResponseType;
            CardEffect responseEffect = trap.Definition.Effect;
            ClearResponseWindow();
            ActivateResponseTrap(Human, trap);

            if (trap.Definition.Effect == CardEffect.SolemnJudgment)
            {
                Human.LifePoints /= 2;
                CheckWinner();
                if (trapResolution != null)
                    trapResolution();
                return true;
            }
            if (trap.Definition.Effect == CardEffect.StarlightRoad)
            {
                if (trapResolution != null)
                    trapResolution();
                SpecialSummonStardustFromExtra(Human);
                return true;
            }

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
            else if (responseType == CardEffect.DestroyAllMonsters &&
                     responseEffect == CardEffect.BottomlessTrapHole)
            {
                ResolveSummonResponse(trap, sourceCard);
                Log("Chain Link " + chainLinkCount + " resolved.");
            }
            else if (responseType == CardEffect.CounterMonsterEffect &&
                     responseEffect == CardEffect.CounterMonsterEffect)
            {
                if (trapResolution != null)
                    trapResolution();
                DuelPlayer sourceOwner = GetMonsterOwner(sourceCard);
                if (sourceOwner != null)
                    DestroyMonsterByCardEffect(sourceOwner, sourceCard);
                CheckWinner();
                Log("Chain Link " + chainLinkCount + " resolved.");
            }
            else
            {
                if (trapResolution != null)
                    trapResolution();
                Log("Chain Link " + chainLinkCount + " resolved.");
            }

            return true;
        }

        public bool TryRespondWithDustTornado(
            CardInstance target, CardInstance cardToSet = null)
        {
            if (!ResponseWindowOpen || pendingDustTornadoResponse == null ||
                !Human.SpellTrapZones.Contains(pendingDustTornadoResponse) ||
                GetSpellTrapOwner(target) != GetOpponent(Human))
                return Reject("Choose an opposing Spell or Trap for Dust Tornado.");
            if (cardToSet != null && (!Human.Hand.Contains(cardToSet) ||
                (cardToSet.Definition.Kind != CardKind.Spell && cardToSet.Definition.Kind != CardKind.Trap)))
                return Reject("Only a Spell or Trap in your hand can be Set by Dust Tornado.");

            CardInstance dustTornado = pendingDustTornadoResponse;
            Action resolvePending = pendingResponsePass;
            ClearResponseWindow();
            ActivateResponseTrap(Human, dustTornado);
            ResolveDustTornado(Human, dustTornado, target, cardToSet);
            if (resolvePending != null)
                resolvePending();
            return true;
        }

        public bool CancelDustTornadoTargetChoice()
        {
            if (!DustTornadoTargetChoiceOpen)
                return false;
            pendingDustTornadoResponse = null;
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
                    SendMonsterToGraveyardDeferred(CurrentPlayer, material);
                else
                    SendCardToGraveyard(CurrentPlayer, material, deferTriggers: true);
            }
            CurrentPlayer.Hand.Remove(polymerization);
            CurrentPlayer.Graveyard.Add(polymerization);
            CurrentPlayer.ExtraDeck.Remove(extraEntry);
            CardInstance summoned = new CardInstance(fusion) { SummonedTurnNumber = turnNumber };
            summoned.FusionMaterials.AddRange(chosen);
            CurrentPlayer.MonsterZones[summonZone] = summoned;
            Log(CurrentPlayer.Name + " Fusion Summoned " + fusion.DisplayName + ".");
            ResolveDeferredGraveyardEffects();
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
            Human.WitchEffectUsedThisTurn = false;
            Opponent.WitchEffectUsedThisTurn = false;
            foreach (CardInstance monster in player.MonsterZones.Where(card => card != null))
            {
                monster.HasAttacked = false;
                monster.HasChangedPositionThisTurn = false;
                monster.EffectUsedThisTurn = false;
                monster.AttackBonus = 0;
                monster.ExtraAttacksThisTurn = 0;
                monster.DefenseModifier = 0;
                monster.NitroWarriorBoostReady = false;
                monster.NitroWarriorSecondAttackTarget = null;
                monster.DrillWarriorDirectAttackThisTurn = false;
                monster.AncientFairySummonUsedThisTurn = false;
                monster.AncientFairyFieldEffectUsedThisTurn = false;
                monster.MajesticTargetEffectUsedThisTurn = false;
                monster.ShootingStarExcavationUsedThisTurn = false;
                monster.ShootingStarDestructionNegateUsedThisTurn = false;
                monster.ShootingStarAttackNegateUsedThisTurn = false;
                if (monster.EffectsNegatedUntilTurn > 0 && monster.EffectsNegatedUntilTurn <= turnNumber)
                {
                    monster.EffectsNegated = false;
                    monster.EffectsNegatedUntilTurn = 0;
                }
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
                DestroyMonsterByCardEffect(endingPlayer, monster);
                Log(monster.Definition.DisplayName + " was destroyed by Limiter Removal.");
            }
            foreach (CardInstance archfiend in endingPlayer.MonsterZones
                         .Where(item => item != null &&
                                        item.Definition.Effect == CardEffect.RedDragonArchfiend)
                         .ToList())
            {
                foreach (CardInstance monster in endingPlayer.MonsterZones
                             .Where(item => item != null && item != archfiend && !item.HasAttacked)
                             .ToList())
                    DestroyMonsterByCardEffect(endingPlayer, monster);
            }
            foreach (CardInstance majesticDragon in endingPlayer.MonsterZones
                         .Where(item => item != null &&
                                        item.Definition.Effect == CardEffect.MajesticStarDragon).ToList())
            {
                CardInstance stardust = endingPlayer.Graveyard.FirstOrDefault(card =>
                    card.Definition.Effect == CardEffect.StardustDragon);
                if (stardust == null)
                    continue;
                RemoveFromMonsterZones(endingPlayer, majesticDragon);
                endingPlayer.ExtraDeck.Add(new CardInstance(majesticDragon.Definition));
                SpecialSummonFromGraveyard(endingPlayer, stardust, true);
            }
            endingPlayer.BattleDamageProtectedTurn = 0;
            foreach (CardInstance monster in Human.MonsterZones.Concat(Opponent.MonsterZones)
                         .Where(item => item != null).ToList())
            {
                monster.AttackBonus = 0;
                monster.DefenseModifier = 0;
                monster.LevelModifier = 0;
                if (monster.EffectsNegatedUntilTurn > 0 &&
                    monster.EffectsNegatedUntilTurn <= turnNumber)
                {
                    monster.EffectsNegated = false;
                    monster.EffectsNegatedUntilTurn = 0;
                }
            }
            foreach (CardInstance stardust in endingPlayer.Graveyard
                         .Where(card => card.Definition.Effect == CardEffect.StardustDragon &&
                                       card.StardustReturnPending).ToList())
            {
                if (!SpecialSummonFromGraveyard(endingPlayer, stardust, true))
                    continue;
                stardust.StardustReturnPending = false;
            }

            foreach (DuelPlayer player in new[] { Human, Opponent })
            {
                foreach (CardInstance banishedDragon in player.Banished
                             .Where(card => card.ReturnAtEndPhaseTurn == turnNumber).ToList())
                {
                    int zone = FindEmptyMonsterZone(player);
                    if (zone < 0)
                        continue;
                    banishedDragon.ReturnAtEndPhaseTurn = 0;
                    PlaceSpecialSummon(player, banishedDragon, zone, true);
                }
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
            public Func<AiSimulation, bool> Simulate { get; }
            public int Priority { get; }

            public AiActionCandidate(double score, Action execute)
            {
                Score = score;
                Execute = execute;
            }

            public AiActionCandidate(
                Action execute,
                Func<AiSimulation, bool> simulate,
                int priority)
            {
                Execute = execute;
                Simulate = simulate;
                Priority = priority;
            }
        }

        private sealed class AiSimulation
        {
            public DuelEngine Engine { get; }
            public Dictionary<CardInstance, CardInstance> Cards { get; }

            public AiSimulation(DuelEngine engine, Dictionary<CardInstance, CardInstance> cards)
            {
                Engine = engine;
                Cards = cards;
            }
        }

        private sealed class AiPlanNode
        {
            public DuelEngine Engine { get; }
            public DuelPlayer Actor { get; }
            public double Score { get; }
            public AiActionCandidate FirstAction { get; }

            public AiPlanNode(
                DuelEngine engine,
                DuelPlayer actor,
                double score,
                AiActionCandidate firstAction)
            {
                Engine = engine;
                Actor = actor;
                Score = score;
                FirstAction = firstAction;
            }
        }

        private void RunAiTurnActions(DuelPlayer actor)
        {
            if (Phase == DuelPhase.GameOver || ResponseWindowOpen)
                return;

            pendingAiTurnActor = actor;
            pendingAiActions.Clear();
            pendingAiActionIndex = 0;
            aiPlannerActionsThisTurn = 0;
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
            pendingAiActions.Add(() => RunAiMainPhasePlanner(actor));
            pendingAiActions.Add(() => EnqueueTrapSetActions(actor));
            pendingAiActions.Add(() => EnqueuePositionActions(actor));
            pendingAiActions.Add(() => EnqueueBattleActions(actor));
            pendingAiActions.Add(EnterAiEndPhase);
            pendingAiActions.Add(FinishAiTurn);
        }

        private void RunAiMainPhasePlanner(DuelPlayer actor)
        {
            if (CurrentPlayer != actor || !CanActInMainPhase() ||
                aiPlannerActionsThisTurn >= AiPlannerActionsPerTurnLimit)
                return;

            AiActionCandidate firstAction = FindBestAiMainPhaseAction(actor);
            if (firstAction == null)
                return;

            firstAction.Execute();
            aiPlannerActionsThisTurn++;
            if (CurrentPlayer == actor && CanActInMainPhase() &&
                aiPlannerActionsThisTurn < AiPlannerActionsPerTurnLimit)
                pendingAiActions.Insert(pendingAiActionIndex, () => RunAiMainPhasePlanner(actor));
        }

        private AiActionCandidate FindBestAiMainPhaseAction(DuelPlayer actor)
        {
            double baseline = DuelAiEvaluator.EvaluateForTurnPlan(actor, GetOpponent(actor));
            List<AiPlanNode> beam = new List<AiPlanNode>
            {
                new AiPlanNode(this, actor, baseline, null)
            };
            AiActionCandidate bestAction = null;
            double bestScore = baseline;
            int evaluationCount = 0;

            for (int depth = 0; depth < AiPlannerDepth && beam.Count > 0 &&
                 evaluationCount < AiPlannerEvaluationLimit; depth++)
            {
                List<AiPlanNode> nextBeam = new List<AiPlanNode>();
                foreach (AiPlanNode node in beam)
                {
                    List<AiActionCandidate> candidates = SelectAiPlannerCandidates(
                        node.Engine.GenerateAiMainPhaseCandidates(node.Actor));

                    foreach (AiActionCandidate candidate in candidates)
                    {
                        if (evaluationCount >= AiPlannerEvaluationLimit)
                            break;
                        evaluationCount++;

                        AiSimulation simulation = node.Engine.CreateAiSimulation();
                        if (!candidate.Simulate(simulation) ||
                            !ResolveAiSimulationChoices(simulation.Engine))
                            continue;

                        DuelPlayer simulatedActor = node.Actor == node.Engine.Human
                            ? simulation.Engine.Human
                            : simulation.Engine.Opponent;
                        double score = DuelAiEvaluator.EvaluateForTurnPlan(simulatedActor,
                            simulation.Engine.GetOpponent(simulatedActor));
                        AiActionCandidate firstAction = node.FirstAction ?? candidate;
                        if (score > bestScore + 0.01)
                        {
                            bestScore = score;
                            bestAction = firstAction;
                        }
                        nextBeam.Add(new AiPlanNode(
                            simulation.Engine, simulatedActor, score, firstAction));
                    }
                }

                beam = nextBeam.OrderByDescending(node => node.Score)
                    .Take(AiPlannerBeamWidth).ToList();
            }

            return bestAction;
        }

        private List<AiActionCandidate> GenerateAiMainPhaseCandidates(DuelPlayer actor)
        {
            List<AiActionCandidate> candidates = new List<AiActionCandidate>();
            List<CardInstance> field = actor.MonsterZones.Where(card => card != null).ToList();

            foreach (CardInstance card in actor.Hand.Where(item => item.Definition.CanNormalSummon))
            {
                if (actor.NormalSummonsThisTurn == 0)
                {
                    int tributeCount = card.Definition.Level >= 7 ? 2 :
                        card.Definition.Level >= 5 ? 1 : 0;
                    if (tributeCount > 0)
                        AddAiSummonCandidate(candidates, actor, card,
                            Array.Empty<CardInstance>(), false, 0);
                    foreach (List<CardInstance> tributes in GetCardCombinations(field, tributeCount))
                    {
                        AddAiSummonCandidate(candidates, actor, card, tributes, false, 2);
                        if (tributeCount == 0)
                            AddAiSummonCandidate(candidates, actor, card, tributes, true, 3);
                    }
                }
                else
                {
                    AddAiSummonCandidate(candidates, actor, card,
                        Array.Empty<CardInstance>(), false, 0);
                }
            }

            foreach (CardInstance card in actor.Hand.Where(item =>
                         item.Definition.Effect == CardEffect.Berserkion ||
                         item.Definition.Effect == CardEffect.Valkyrion))
            {
                candidates.Add(new AiActionCandidate(
                    () => TrySpecialSummonBoss(card),
                    simulation => simulation.Engine.TrySpecialSummonBoss(
                        MapAiCard(simulation, card)),
                    0));
            }

            IEnumerable<CardInstance> activatableCards = actor.Hand.Where(card =>
                    card.Definition.Kind == CardKind.Spell ||
                    card.Definition.IsEffectMonster &&
                    CanActivateMonsterEffectFromHand(card.Definition.Effect))
                .Concat(actor.MonsterZones.Where(card => card != null && card.FaceUp &&
                    card.Definition.Effect != CardEffect.None))
                .Concat(actor.SpellTrapZones.Where(card => card != null && !card.FaceUp &&
                    card.Definition.Kind == CardKind.Trap))
                .Concat(actor.Graveyard.Where(card =>
                    CanActivateMonsterEffectFromGraveyard(card.Definition.Effect)))
                .Distinct()
                .ToList();
            List<CardInstance> targets = GetAiEffectTargets(actor).ToList();
            foreach (CardInstance card in activatableCards)
            {
                if (card.Definition.Effect == CardEffect.ReviveMonsterFromGraveyard)
                {
                    foreach (CardInstance target in targets)
                    {
                        if (double.IsNegativeInfinity(EvaluateAiActivation(actor, card, target)))
                            continue;
                        AddAiActivationCandidate(candidates, card, target, 1);
                    }
                    continue;
                }

                AddAiActivationCandidate(candidates, card, null,
                    card.Definition.Effect == CardEffect.QuickdrawSynchron ? 0 : 1);
                foreach (CardInstance target in targets)
                    AddAiActivationCandidate(candidates, card, target, 1);
            }

            if (!actor.SummonsLockedThisTurn)
            {
                foreach (CardDefinition synchro in actor.ExtraDeck
                             .Where(card => card.Definition.Kind == CardKind.Synchro)
                             .Select(card => card.Definition).Distinct())
                {
                    List<CardInstance> faceUpField = field.Where(card => card.FaceUp).ToList();
                    foreach (List<CardInstance> materials in GetCardSubsets(faceUpField))
                    {
                        if (!CanSynchroSummon(synchro, materials, actor))
                            continue;
                        candidates.Add(new AiActionCandidate(
                            () => TrySynchroSummon(synchro, materials),
                            simulation => simulation.Engine.TrySynchroSummon(synchro,
                                MapAiCards(simulation, materials)),
                            0));
                    }
                }
            }

            if (actor.Hand.Any(card => card.Definition.CardId == "sdmy-en031"))
            {
                List<CardInstance> available = actor.Hand
                    .Concat(field.Where(card => card.FaceUp))
                    .Where(card => card.Definition.CardId != "sdmy-en031")
                    .ToList();
                foreach (CardDefinition fusion in actor.ExtraDeck
                             .Where(card => card.Definition.Kind == CardKind.Fusion)
                             .Select(card => card.Definition).Distinct())
                {
                    List<CardInstance> possibleMaterials = available
                        .Where(card => IsPotentialFusionMaterial(fusion, card)).ToList();
                    foreach (List<CardInstance> materials in GetCardSubsets(possibleMaterials))
                    {
                        if (!CanFusionSummon(fusion, materials, actor))
                            continue;
                        candidates.Add(new AiActionCandidate(
                            () => TryFusionSummon(fusion, materials),
                            simulation => simulation.Engine.TryFusionSummon(fusion,
                                MapAiCards(simulation, materials)),
                            0));
                    }
                }
            }

            return candidates;
        }

        private static List<AiActionCandidate> SelectAiPlannerCandidates(
            IReadOnlyList<AiActionCandidate> candidates)
        {
            ILookup<int, AiActionCandidate> byPriority = candidates.ToLookup(candidate => candidate.Priority);
            List<IEnumerator<AiActionCandidate>> groups = byPriority
                .OrderBy(group => group.Key)
                .Select(group => group.Take(AiPlannerCandidatesPerNode).GetEnumerator())
                .ToList();
            List<AiActionCandidate> selected = new List<AiActionCandidate>();
            bool addedCandidate = true;
            while (selected.Count < AiPlannerCandidatesPerNode && addedCandidate)
            {
                addedCandidate = false;
                foreach (IEnumerator<AiActionCandidate> group in groups)
                {
                    if (selected.Count >= AiPlannerCandidatesPerNode)
                        break;
                    if (!group.MoveNext())
                        continue;
                    selected.Add(group.Current);
                    addedCandidate = true;
                }
            }

            foreach (IEnumerator<AiActionCandidate> group in groups)
                group.Dispose();
            return selected;
        }

        private void AddAiSummonCandidate(
            List<AiActionCandidate> candidates,
            DuelPlayer actor,
            CardInstance card,
            IReadOnlyList<CardInstance> tributes,
            bool setFaceDown,
            int priority)
        {
            List<CardInstance> chosenTributes = tributes.ToList();
            candidates.Add(new AiActionCandidate(
                () => TryNormalSummon(card, chosenTributes, setFaceDown),
                simulation => simulation.Engine.TryNormalSummon(MapAiCard(simulation, card),
                    MapAiCards(simulation, chosenTributes), setFaceDown),
                priority));
        }

        private void AddAiActivationCandidate(
            List<AiActionCandidate> candidates,
            CardInstance card,
            CardInstance target,
            int priority)
        {
            candidates.Add(new AiActionCandidate(
                () => TryActivate(card, target),
                simulation =>
                {
                    CardInstance simulatedCard = MapAiCard(simulation, card);
                    CardInstance simulatedTarget = MapAiCard(simulation, target);
                    return simulatedCard != null &&
                           simulation.Engine.TryActivate(simulatedCard, simulatedTarget) &&
                           ResolveAiSimulationChoices(simulation.Engine);
                },
                priority));
        }

        private static CardInstance MapAiCard(AiSimulation simulation, CardInstance card)
        {
            return card != null && simulation.Cards.TryGetValue(card, out CardInstance mapped)
                ? mapped
                : null;
        }

        private static List<CardInstance> MapAiCards(
            AiSimulation simulation,
            IEnumerable<CardInstance> cards)
        {
            return cards.Select(card => MapAiCard(simulation, card)).ToList();
        }

        private static IEnumerable<List<CardInstance>> GetCardCombinations(
            IReadOnlyList<CardInstance> cards,
            int count)
        {
            if (count < 0 || count > cards.Count)
                yield break;
            int maskLimit = 1 << cards.Count;
            for (int mask = 0; mask < maskLimit; mask++)
            {
                if (CountSetBits(mask) != count)
                    continue;
                List<CardInstance> combination = new List<CardInstance>();
                for (int bit = 0; bit < cards.Count; bit++)
                {
                    if ((mask & (1 << bit)) != 0)
                        combination.Add(cards[bit]);
                }
                yield return combination;
            }
        }

        private static IEnumerable<List<CardInstance>> GetCardSubsets(
            IReadOnlyList<CardInstance> cards)
        {
            int maskLimit = 1 << cards.Count;
            for (int mask = 1; mask < maskLimit; mask++)
            {
                List<CardInstance> subset = new List<CardInstance>();
                for (int bit = 0; bit < cards.Count; bit++)
                {
                    if ((mask & (1 << bit)) != 0)
                        subset.Add(cards[bit]);
                }
                yield return subset;
            }
        }

        private static int CountSetBits(int value)
        {
            int count = 0;
            while (value != 0)
            {
                count += value & 1;
                value >>= 1;
            }
            return count;
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

        private static double AiBattlePositionScore(
            int attack,
            int defense,
            bool attackPosition,
            DuelPlayer opponent)
        {
            List<CardInstance> visibleThreats = opponent.MonsterZones
                .Where(monster => monster != null && monster.FaceUp && monster.AttackPosition).ToList();
            if (visibleThreats.Count == 0)
                return attackPosition ? 300 + attack * 0.1 : defense * 0.1;

            int strongestThreat = visibleThreats.Max(monster => monster.CurrentAttack);
            if (attackPosition)
                return attack >= strongestThreat
                    ? 300 + attack * 0.1
                    : -250 - (strongestThreat - attack) * 0.35;

            return defense >= strongestThreat
                ? 240 + defense * 0.1
                : -100;
        }

        private void EnqueueSpellActions(DuelPlayer actor)
        {
            List<AiActionCandidate> candidates = new List<AiActionCandidate>();
            foreach (CardInstance card in actor.Hand.Where(item => item.Definition.Kind == CardKind.Spell))
                AddAiActivationCandidates(actor, card, candidates);
            InsertPendingActions(candidates.OrderByDescending(candidate => candidate.Score)
                .Select(candidate => candidate.Execute).ToList());
        }

        private IEnumerable<CardInstance> GetAiEffectTargets(DuelPlayer actor)
        {
            foreach (CardInstance target in actor.MonsterZones.Concat(GetOpponent(actor).MonsterZones)
                         .Concat(actor.SpellTrapZones).Concat(GetOpponent(actor).SpellTrapZones)
                         .Concat(new[] { actor.FieldSpellZone, GetOpponent(actor).FieldSpellZone })
                         .Concat(actor.Graveyard).Concat(GetOpponent(actor).Graveyard)
                         .Where(item => item != null).Distinct())
                yield return target;
        }

        private void EnqueueTrapSetActions(DuelPlayer actor)
        {
            InsertPendingActions(actor.Hand.Where(card => card.Definition.Kind == CardKind.Trap &&
                    (card.Definition.Effect == CardEffect.StopAttack ||
                     card.Definition.Effect == CardEffect.SakuretsuArmor ||
                     card.Definition.Effect == CardEffect.DimensionalPrison ||
                     card.Definition.Effect == CardEffect.MirrorForce ||
                     card.Definition.Effect == CardEffect.CompulsoryEvacuationDevice ||
                     card.Definition.Effect == CardEffect.BottomlessTrapHole ||
                     card.Definition.Effect == CardEffect.TorrentialTribute ||
                     card.Definition.Effect == CardEffect.DestroySpellTrap ||
                     card.Definition.Effect == CardEffect.RaigekiBreak ||
                     card.Definition.Effect == CardEffect.CounterSpell ||
                     card.Definition.Effect == CardEffect.CounterMonsterEffect ||
                     card.Definition.Effect == CardEffect.CounterTrap))
                .Select(card => (Action)(() => TrySetBackrow(card))).ToList());
        }

        private void EnqueueMonsterEffectActions(DuelPlayer actor)
        {
            List<AiActionCandidate> candidates = new List<AiActionCandidate>();
            foreach (CardInstance card in actor.MonsterZones.Where(item =>
                         item != null && item.FaceUp && !item.EffectUsedThisTurn)
                         .Concat(actor.SpellTrapZones.Where(item =>
                             item != null && !item.FaceUp && item.Definition.Kind == CardKind.Trap)))
                AddAiActivationCandidates(actor, card, candidates);
            InsertPendingActions(candidates.OrderByDescending(candidate => candidate.Score)
                .Select(candidate => candidate.Execute).ToList());
        }

        private void AddAiActivationCandidates(
            DuelPlayer actor, CardInstance card, List<AiActionCandidate> candidates)
        {
            double noTargetScore = EvaluateAiActivation(actor, card, null);
            if (noTargetScore > 0)
            {
                candidates.Add(new AiActionCandidate(noTargetScore,
                    () => TryAiActivateIfStillProfitable(actor, card, null)));
                return;
            }

            foreach (CardInstance target in GetAiEffectTargets(actor))
            {
                double score = EvaluateAiActivation(actor, card, target);
                if (score <= 0)
                    continue;

                candidates.Add(new AiActionCandidate(score, () =>
                    TryAiActivateIfStillProfitable(actor, card, target)));
            }
        }

        private void TryAiActivateIfStillProfitable(DuelPlayer actor, CardInstance card, CardInstance target)
        {
            if ((actor.Hand.Contains(card) || actor.MonsterZones.Contains(card) ||
                 actor.SpellTrapZones.Contains(card)) &&
                EvaluateAiActivation(actor, card, target) > 0)
                TryActivate(card, target);
        }

        private double EvaluateAiActivation(DuelPlayer actor, CardInstance card, CardInstance target)
        {
            if (card.Definition.Effect == CardEffect.ReviveMonsterFromGraveyard)
            {
                bool createsSynchroLine = false;
                double reviveScore = EvaluateAiAction(actor, (simulation, cards) =>
                {
                    DuelPlayer simulatedActor = actor == Human
                        ? simulation.Human
                        : simulation.Opponent;
                    bool hadSynchroLine = HasLegalAiSynchroSummon(simulatedActor);
                    bool activated = simulation.TryActivate(cards[card],
                        target == null ? null : cards[target]);
                    createsSynchroLine = activated && !hadSynchroLine &&
                                          HasLegalAiSynchroSummon(simulatedActor);
                    return activated;
                });
                if (double.IsNegativeInfinity(reviveScore))
                    return reviveScore;

                if (createsSynchroLine)
                    return reviveScore + 450;

                if (reviveScore < 150 || ShouldHoldMonsterRebornForBattle(actor, target))
                    return double.NegativeInfinity;
                return reviveScore;
            }

            return EvaluateAiAction(actor, (simulation, cards) =>
            {
                bool activated = simulation.TryActivate(cards[card],
                    target == null ? null : cards[target]);
                return activated && ResolveAiSimulationChoices(simulation);
            });
        }

        private bool HasLegalAiSynchroSummon(DuelPlayer actor)
        {
            List<CardInstance> field = actor.MonsterZones
                .Where(card => card != null && card.FaceUp).ToList();
            if (field.Count < 2)
                return false;

            foreach (CardInstance extra in actor.ExtraDeck
                         .Where(card => card.Definition.Kind == CardKind.Synchro))
            {
                for (int mask = 1; mask < (1 << field.Count); mask++)
                {
                    List<CardInstance> materials = new List<CardInstance>();
                    for (int bit = 0; bit < field.Count; bit++)
                    {
                        if ((mask & (1 << bit)) != 0)
                            materials.Add(field[bit]);
                    }
                    if (IsValidSynchroMaterial(extra.Definition, materials, actor))
                        return true;
                }
            }
            return false;
        }

        private bool ShouldHoldMonsterRebornForBattle(DuelPlayer actor, CardInstance currentTarget)
        {
            if (currentTarget == null || !CanAttackThisTurn || actor.NoAttackTurnsRemaining > 0)
                return false;

            DuelPlayer opponent = GetOpponent(actor);
            foreach (CardInstance attacker in actor.MonsterZones.Where(card =>
                         card != null && card.FaceUp && card.AttackPosition && !card.HasAttacked))
            {
                CardInstance potentialTarget = SelectAiAttackTarget(opponent, attacker);
                if (potentialTarget == null || !potentialTarget.FaceUp ||
                    !potentialTarget.AttackPosition || attacker.CurrentAttack <= potentialTarget.CurrentAttack)
                    continue;

                if (DuelAiEvaluator.CardValue(potentialTarget) >
                    DuelAiEvaluator.CardValue(currentTarget) + 100)
                    return true;
            }
            return false;
        }

        private double EvaluateAiAction(
            DuelPlayer actor,
            Func<DuelEngine, Dictionary<CardInstance, CardInstance>, bool> applyAction)
        {
            AiSimulation simulation = CreateAiSimulation();
            if (simulation == null)
                return double.NegativeInfinity;

            DuelPlayer simulatedActor = actor == Human ? simulation.Engine.Human : simulation.Engine.Opponent;
            double before = DuelAiEvaluator.Evaluate(simulatedActor, simulation.Engine.GetOpponent(simulatedActor));
            if (!applyAction(simulation.Engine, simulation.Cards) ||
                !ResolveAiSimulationChoices(simulation.Engine))
                return double.NegativeInfinity;

            double after = DuelAiEvaluator.Evaluate(simulatedActor, simulation.Engine.GetOpponent(simulatedActor));
            return after - before;
        }

        private AiSimulation CreateAiSimulation()
        {
            DuelStateSnapshot snapshot = CaptureState(true);
            Dictionary<CardInstance, string> sourceIds = new Dictionary<CardInstance, string>();
            List<CardInstance> sourceCards = new List<CardInstance>();
            RegisterPlayerCards(Human, sourceIds, sourceCards);
            RegisterPlayerCards(Opponent, sourceIds, sourceCards);
            Dictionary<string, CardDefinition> definitions = sourceCards
                .GroupBy(card => card.Definition.CardId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Definition, StringComparer.Ordinal);

            DuelEngine simulatedEngine = RestoreState(snapshot, cardId =>
            {
                if (definitions.TryGetValue(cardId, out CardDefinition definition))
                    return definition;
                return StarterCatalog.Find(cardId);
            });
            Dictionary<CardInstance, string> simulatedIds = new Dictionary<CardInstance, string>();
            List<CardInstance> simulatedCards = new List<CardInstance>();
            RegisterPlayerCards(simulatedEngine.Human, simulatedIds, simulatedCards);
            RegisterPlayerCards(simulatedEngine.Opponent, simulatedIds, simulatedCards);
            Dictionary<string, CardInstance> simulatedCardsById = simulatedIds
                .ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);
            Dictionary<CardInstance, CardInstance> cardMap = sourceIds.ToDictionary(
                pair => pair.Key,
                pair => simulatedCardsById[pair.Value]);
            return new AiSimulation(simulatedEngine, cardMap);
        }

        private static bool ResolveAiSimulationChoices(DuelEngine simulation)
        {
            while (simulation.SearchChoiceOpen)
            {
                CardInstance choice = simulation.AvailableSearchCards
                    .OrderByDescending(card => DuelAiEvaluator.CardValue(card))
                    .FirstOrDefault();
                if (choice == null || !simulation.TryChooseSearchCard(choice))
                    return false;
            }
            while (simulation.GraveyardSummonChoiceOpen)
            {
                CardInstance choice = simulation.AvailableGraveyardSummons
                    .OrderByDescending(DuelAiEvaluator.CardValue)
                    .FirstOrDefault();
                if (choice == null || !simulation.TryChooseGraveyardSummon(choice))
                    return false;
            }
            while (simulation.ResponseWindowOpen)
            {
                if (!simulation.PassResponseWindow())
                    return false;
            }
            return !simulation.ResponseDiscardChoiceOpen;
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
            if (ResponseWindowOpen || SearchChoiceOpen || GraveyardSummonChoiceOpen ||
                ActivationDiscardChoiceOpen ||
                Phase == DuelPhase.GameOver ||
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

        private bool ResolveAttack(DuelPlayer attackerOwner, CardInstance attacker, CardInstance target)
        {
            if (!CanAttackThisTurn)
                return Reject("The first player cannot attack on the first turn of the duel.");
            if (attackerOwner.BattlePhaseSkippedTurn == turnNumber)
                return Reject("A card effect made you skip your Battle Phase this turn.");
            if (attackerOwner.NoAttackTurnsRemaining > 0)
                return Reject("A card effect prevents you from attacking this turn.");
            if (attacker == null || !attackerOwner.MonsterZones.Contains(attacker) || !attacker.FaceUp ||
                !attacker.AttackPosition ||
                (attacker.HasAttacked && attacker.ExtraAttacksThisTurn <= 0))
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
            bool nitroFollowUpAttack = attacker.NitroWarriorSecondAttackTarget != null;
            List<CardInstance> defenders = defender.MonsterZones.Where(card => card != null).ToList();
            if (attacker.NitroWarriorSecondAttackTarget != null &&
                target != attacker.NitroWarriorSecondAttackTarget)
                return Reject("Nitro Warrior must make its second attack on the monster it changed to Attack Position.");
            if (target == null)
            {
                if (!CanAttackDirectly(attackerOwner, attacker, defenders))
                    return Reject("You must attack a monster while the opponent controls one.");
                ApplyBattleDamage(defender, GetBattleAttack(attacker, null), attacker);
                attacker.NitroWarriorBoostReady = false;
                if (attacker.Definition.Effect == CardEffect.ShootingStarDragon &&
                    attacker.ExtraAttacksThisTurn > 0)
                {
                    attacker.ExtraAttacksThisTurn--;
                    attacker.HasAttacked = false;
                }
                Log(attacker.Definition.DisplayName + " attacked directly for " + attacker.CurrentAttack + " damage.");
                IncreaseRoadSynchronLevelAfterAttack(attackerOwner, attacker);
                CheckWinner();
                return true;
            }
            if (!defender.MonsterZones.Contains(target))
                return Reject("Select an opposing monster as the attack target.");

            if (attacker.Definition.Effect == CardEffect.TurboSynchron && target.AttackPosition)
                target.AttackPosition = false;
            if (!target.FaceUp)
            {
                target.FaceUp = true;
                Log(target.Definition.DisplayName + " was flipped face-up in Defense Position.");
                if (ResolveFlipEffect(defender, target))
                    return true;
            }
            if (!defender.MonsterZones.Contains(target) || !attackerOwner.MonsterZones.Contains(attacker))
                return true;
            int attackerAttack = GetBattleAttack(attacker, target);
            int targetPower = target.AttackPosition
                ? target.CurrentAttack
                : target.Definition.Defense + target.DefenseModifier;
            int difference = attackerAttack - targetPower;
            int attackerLifeBefore = attackerOwner.LifePoints;
            bool attackerCannotBeDestroyed = attacker.Definition.Effect == CardEffect.BlackwingArmorMaster;
            bool targetCannotBeDestroyed = target.Definition.Effect == CardEffect.BlackwingArmorMaster ||
                (IsMorphtronic(target.Definition) && defender.MonsterZones.Any(card =>
                    card != null && card != target && card.FaceUp && !card.AttackPosition &&
                    card.Definition.Effect == CardEffect.MorphtronicBoarden));
            if (attackerAttack > targetPower)
            {
                if (!targetCannotBeDestroyed && !TrySilverwindBattleProtection(defender, target))
                    SendMonsterToGraveyard(defender, target, true);
                if (attacker.Definition.Effect == CardEffect.BlackwingShura &&
                    defender.Graveyard.Contains(target))
                    ResolveBlackwingShura(attackerOwner, attacker);
                if (target.AttackPosition)
                    ApplyBattleDamage(defender, difference, attacker, target);
                else if (attacker.Definition.Effect == CardEffect.BlackwingArmedWing)
                    ApplyBattleDamage(defender, difference, attacker, target);
                Log(attacker.Definition.DisplayName + " destroyed " + target.Definition.DisplayName + "." +
                    (target.AttackPosition || attacker.Definition.Effect == CardEffect.BlackwingArmedWing
                        ? " Opponent takes " + difference + " damage."
                        : ""));
            }
            else if (attackerAttack < targetPower)
            {
                if (target.AttackPosition)
                {
                    if (!attackerCannotBeDestroyed)
                        SendMonsterToGraveyard(attackerOwner, attacker, true);
                    ApplyBattleDamage(attackerOwner, -difference, attacker, target);
                    Log(attacker.Definition.DisplayName + " was destroyed. You take " + -difference + " damage.");
                }
                else
                {
                    ApplyBattleDamage(attackerOwner, -difference, attacker, target);
                    Log(attacker.Definition.DisplayName + " could not break through the Defense Position monster. " +
                        attackerOwner.Name + " takes " + -difference + " damage.");
                }
            }
            else
            {
                if (target.AttackPosition)
                {
                    if (!attackerCannotBeDestroyed)
                        SendMonsterToGraveyard(attackerOwner, attacker, true);
                    if (!targetCannotBeDestroyed && !TrySilverwindBattleProtection(defender, target))
                        SendMonsterToGraveyard(defender, target, true);
                    Log("Both battling monsters were destroyed.");
                }
                else
                {
                    Log(attacker.Definition.DisplayName + " tied with the Defense Position monster. Neither was destroyed.");
                }
            }
            if (attacker.Definition.Effect == CardEffect.BlackwingArmorMaster &&
                target.Definition.IsMonster && IsOnMonsterField(target))
                target.WedgeCounters = Math.Max(1, target.WedgeCounters);
            if (attacker.Definition.Effect == CardEffect.GoyoGuardianRevive &&
                defender.Graveyard.Contains(target) && FindEmptyMonsterZone(attackerOwner) >= 0)
                SpecialSummonFromEitherGraveyard(attackerOwner, target, false);
            if (attacker.Definition.Effect == CardEffect.ColossalFighterRevive &&
                attackerOwner.Graveyard.Contains(attacker))
                ReviveWarriorWithColossalFighter(attackerOwner, attacker);
            if (target.Definition.Effect == CardEffect.ColossalFighterRevive &&
                defender.Graveyard.Contains(target))
                ReviveWarriorWithColossalFighter(defender, target);
            if (attacker.Definition.Effect == CardEffect.RedDragonArchfiend && !target.AttackPosition)
            {
                foreach (CardInstance defenseMonster in defender.MonsterZones
                             .Where(item => item != null && !item.AttackPosition).ToList())
                    DestroyMonsterByCardEffect(defender, defenseMonster);
            }
            if ((attacker.Definition.Effect == CardEffect.NitroWarrior &&
                 defender.Graveyard.Contains(target)) ||
                attacker.Definition.Effect == CardEffect.ShootingStarDragon)
            {
                if (attacker.ExtraAttacksThisTurn > 0)
                {
                    attacker.ExtraAttacksThisTurn--;
                    attacker.HasAttacked = false;
                }
            }
            if (attacker.Definition.Effect == CardEffect.TurboSynchron &&
                attackerOwner.LifePoints < attackerLifeBefore)
            {
                int damage = attackerLifeBefore - attackerOwner.LifePoints;
                CardInstance summon = attackerOwner.Hand.FirstOrDefault(card =>
                    card.Definition.IsMonster && card.Definition.Attack <= damage);
                int summonZone = FindEmptyMonsterZone(attackerOwner);
                if (summon != null && summonZone >= 0)
                    PlaceSpecialSummon(attackerOwner, summon, summonZone, true);
            }
            if (attacker.Definition.Effect == CardEffect.NitroWarrior)
            {
                attacker.NitroWarriorBoostReady = false;
                if (!nitroFollowUpAttack && defender.Graveyard.Contains(target))
                {
                    CardInstance followUp = defender.MonsterZones.FirstOrDefault(card =>
                        card != null && card.FaceUp && !card.AttackPosition);
                    if (followUp != null)
                    {
                        followUp.AttackPosition = true;
                        attacker.NitroWarriorSecondAttackTarget = followUp;
                        attacker.HasAttacked = false;
                    }
                }
                else if (nitroFollowUpAttack)
                {
                    attacker.NitroWarriorSecondAttackTarget = null;
                }
            }
            if (attacker.Definition.Effect == CardEffect.BlackwingMistral &&
                attackerOwner.Graveyard.Contains(attacker))
                attackerOwner.BattleDamageProtectedTurn = turnNumber;
            if (target.Definition.Effect == CardEffect.BlackwingMistral &&
                defender.Graveyard.Contains(target))
                defender.BattleDamageProtectedTurn = turnNumber;
            IncreaseRoadSynchronLevelAfterAttack(attackerOwner, attacker);
            CheckWinner();
            return true;
        }

        private bool TrySilverwindBattleProtection(DuelPlayer owner, CardInstance monster)
        {
            if (owner == null || monster == null || !IsBlackwing(monster.Definition) ||
                CurrentPlayer == owner)
                return false;
            CardInstance silverwind = owner.MonsterZones.FirstOrDefault(card =>
                card != null && card.FaceUp &&
                card.Definition.Effect == CardEffect.BlackwingSilverwind &&
                card.SilverwindProtectionTurn != turnNumber);
            if (silverwind == null)
                return false;
            silverwind.SilverwindProtectionTurn = turnNumber;
            Log(owner.Name + "'s " + monster.Definition.DisplayName +
                " was protected from battle destruction by Silverwind.");
            return true;
        }

        private static void IncreaseRoadSynchronLevelAfterAttack(DuelPlayer owner, CardInstance attacker)
        {
            if (attacker != null && owner.MonsterZones.Contains(attacker) &&
                attacker.Definition.Effect == CardEffect.RoadSynchron)
                attacker.LevelModifier++;
        }

        private int GetBattleAttack(CardInstance attacker, CardInstance target)
        {
            int attack = attacker.CurrentAttack;
            if (attacker.Definition.Effect == CardEffect.ColossalFighterRevive)
                attack += Human.Graveyard.Concat(Opponent.Graveyard).Count(card =>
                    string.Equals(card.Definition.MonsterType, "Warrior", StringComparison.OrdinalIgnoreCase)) * 100;
            if (attacker.Definition.Effect == CardEffect.BlackwingArmedWing &&
                target != null && !target.AttackPosition)
                attack += 500;
            if (attacker.Definition.Effect == CardEffect.NitroWarrior && attacker.NitroWarriorBoostReady)
                attack += 1000;
            return attack;
        }

        private void ApplyBattleDamage(
            DuelPlayer player, int amount, CardInstance attacker = null, CardInstance target = null)
        {
            if (amount <= 0 || player.BattleDamageProtectedTurn == turnNumber ||
                (attacker != null && attacker.Definition.Effect == CardEffect.BlackwingArmorMaster) ||
                (target != null && target.Definition.Effect == CardEffect.BlackwingArmorMaster))
                return;
            player.LifePoints -= amount;
        }

        private void ApplyEffectDamage(DuelPlayer player, int amount)
        {
            if (amount <= 0 || player.MonsterZones.Any(card => card != null && card.FaceUp &&
                !card.EffectsNegated &&
                card.Definition.Effect == CardEffect.LifeStreamSetLifePoints))
                return;
            player.LifePoints -= amount;
            CheckWinner();
        }

        private void ResolveStandbyEffects(DuelPlayer player)
        {
            foreach (CardInstance drillWarrior in player.Banished
                         .Where(card => card.Definition.Effect == CardEffect.DrillWarrior &&
                                        card.DrillWarriorReturnTurn <= turnNumber).ToList())
            {
                int zone = FindEmptyMonsterZone(player);
                if (zone < 0)
                    continue;
                PlaceSpecialSummon(player, drillWarrior, zone, true);
                CardInstance recovered = player.Graveyard.FirstOrDefault(card => card.Definition.IsMonster);
                if (recovered == null)
                    continue;
                player.Graveyard.Remove(recovered);
                player.Hand.Add(recovered);
            }
        }

        private bool SpecialSummonFromEitherGraveyard(DuelPlayer owner, CardInstance card, bool attackPosition)
        {
            DuelPlayer graveyardOwner = Human.Graveyard.Contains(card) ? Human :
                Opponent.Graveyard.Contains(card) ? Opponent : null;
            int zone = FindEmptyMonsterZone(owner);
            if (graveyardOwner == null || zone < 0 || owner.SummonsLockedThisTurn)
                return false;
            graveyardOwner.Graveyard.Remove(card);
            card.FaceUp = true;
            card.AttackPosition = attackPosition;
            card.SummonedTurnNumber = turnNumber;
            card.HasAttacked = false;
            card.HasChangedPositionThisTurn = false;
            owner.MonsterZones[zone] = card;
            Log(owner.Name + " Special Summoned " + card.Definition.DisplayName + ".");
            ResolveSummonTriggers(owner, card, false);
            if (graveyardOwner == owner)
                ResolveGraveyardSummonEffects(owner);
            return true;
        }

        private void ReviveWarriorWithColossalFighter(DuelPlayer owner, CardInstance source)
        {
            if (owner.SummonsLockedThisTurn || FindEmptyMonsterZone(owner) < 0)
                return;
            List<CardInstance> choices = Human.Graveyard.Concat(Opponent.Graveyard)
                .Where(card => string.Equals(card.Definition.MonsterType, "Warrior",
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (choices.Count == 0)
                return;
            if (owner == Human && !IsAiVsAi)
            {
                pendingGraveyardSummonOwner = owner;
                pendingGraveyardSummonSource = source;
                pendingGraveyardSummonChoices = choices;
                pendingGraveyardSummonAttackPosition = true;
                pendingGraveyardSummonNegatesEffects = false;
                pendingGraveyardSummonFromEitherGraveyard = true;
                return;
            }

            CardInstance selected = choices.OrderByDescending(DuelAiEvaluator.CardValue).First();
            SpecialSummonFromEitherGraveyard(owner, selected, true);
        }

        private bool ApplyAdditionalStructureDeckEffect(
            DuelPlayer owner,
            DuelPlayer opponent,
            CardDefinition definition,
            CardInstance target,
            CardInstance source,
            out bool handled)
        {
            handled = true;
            switch (definition.Effect)
            {
                case CardEffect.BlackwingBattleBoost:
                    if (source == null || !owner.Hand.Contains(source) || target == null ||
                        !owner.MonsterZones.Contains(target) || !IsBlackwing(target.Definition))
                        return Reject("Kalut must be sent from your hand to boost a Blackwing you control.");
                    owner.Hand.Remove(source);
                    owner.Graveyard.Add(source);
                    target.AttackBonus += definition.EffectValue;
                    return true;
                case CardEffect.BlackwingVayuSummon:
                    return ActivateVayu(owner, source, target);
                case CardEffect.ZephyrosRevive:
                    return ActivateZephyros(owner, source, target);
                case CardEffect.DrillWarrior:
                    return ActivateDrillWarrior(owner, source, target);
                case CardEffect.LevelEater:
                    return ActivateLevelEater(owner, source, target);
                case CardEffect.MorphtronicCelfon:
                    return ResolveMorphtronicCelfon(owner, source);
                case CardEffect.MorphtronicRemoten:
                    return ResolveMorphtronicRemoten(owner, source, target);
                case CardEffect.DestroySetOpponentBackrow:
                    if (definition.CardId == "59839761" &&
                        !owner.MonsterZones.Any(item => item != null && IsBlackwing(item.Definition)))
                        return Reject("Delta Crow needs a Blackwing monster on your field.");
                    foreach (CardInstance setCard in opponent.SpellTrapZones
                                 .Where(item => item != null && !item.FaceUp).ToList())
                    {
                        RemoveFromSpellTrapZones(opponent, setCard);
                        opponent.Graveyard.Add(setCard);
                    }
                    return true;
                case CardEffect.QuickdrawSynchron:
                    return SpecialSummonQuickdraw(owner, source);
                case CardEffect.QuillboltHedgehog:
                    return SpecialSummonQuillbolt(owner, source);
                case CardEffect.PowerToolDragon:
                    return BeginPowerToolChoice(owner, source);
                case CardEffect.BlackwingArmorMaster:
                    foreach (CardInstance monster in opponent.MonsterZones.Where(item =>
                                 item != null && item.WedgeCounters > 0).ToList())
                    {
                        monster.AttackBonus -= monster.CurrentAttack;
                        monster.DefenseModifier = -monster.Definition.Defense;
                        monster.WedgeCounters = 0;
                    }
                    return true;
                case CardEffect.MajesticStarDragon:
                    if (target == null || !opponent.MonsterZones.Contains(target) || !target.FaceUp)
                        return Reject("Select a face-up opposing monster to negate.");
                    target.EffectsNegated = true;
                    target.EffectsNegatedUntilTurn = turnNumber;
                        source.CopiedEffectTarget = target;
                        source.MajesticTargetEffectUsedThisTurn = true;
                        return true;
                    case CardEffect.ShootingStarDragon:
                        if (source == null || !owner.MonsterZones.Contains(source) || !source.FaceUp)
                            return Reject("Shooting Star Dragon must be face-up on your field.");
                    int tuners = owner.Deck.Take(5).Count(card => card.Definition.IsTuner);
                    source.ExtraAttacksThisTurn = Math.Max(0, tuners - 1);
                    source.ShootingStarExcavationUsedThisTurn = true;
                    Shuffle(owner.Deck);
                    return true;
                default:
                    handled = false;
                    return false;
            }
        }

        private bool ActivateVayu(DuelPlayer owner, CardInstance vayu, CardInstance target)
        {
            if (vayu == null || !owner.Graveyard.Contains(vayu) || target == null ||
                !owner.Graveyard.Contains(target) || target.Definition.IsTuner ||
                !IsBlackwing(target.Definition))
                return Reject("Vayu needs itself and a non-Tuner Blackwing in your Graveyard.");

            CardDefinition synchro = owner.ExtraDeck
                .Where(card => card.Definition.Kind == CardKind.Synchro &&
                               card.Definition.DisplayName.IndexOf("Blackwing", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(card => card.Definition)
                .FirstOrDefault(card => card.Level == vayu.Definition.Level + target.Definition.Level);
            int zone = FindEmptyMonsterZone(owner);
            if (synchro == null || zone < 0)
                return Reject("Vayu needs a matching Blackwing Synchro Monster and an open Monster Zone.");

            owner.Graveyard.Remove(vayu);
            owner.Graveyard.Remove(target);
            owner.Banished.Add(vayu);
            owner.Banished.Add(target);
            CardInstance summoned = new CardInstance(synchro)
            {
                EffectsNegated = true,
                SummonedTurnNumber = turnNumber
            };
            owner.ExtraDeck.RemoveAll(card => card.Definition == synchro);
            owner.MonsterZones[zone] = summoned;
            Log(owner.Name + " used Vayu to Special Summon " + synchro.DisplayName + " with its effects negated.");
            return true;
        }

        private bool ActivateZephyros(DuelPlayer owner, CardInstance zephyros, CardInstance target)
        {
            if (zephyros == null || !owner.Graveyard.Contains(zephyros) ||
                zephyros.EffectUsedOncePerDuel || target == null || !target.FaceUp ||
                GetCardController(target) != owner || FindEmptyMonsterZone(owner) < 0 ||
                owner.SummonsLockedThisTurn)
                return Reject("Zephyros needs a face-up card you control, an open Monster Zone, and an unused once-per-Duel effect.");

            ReturnFieldCardToHand(owner, target);
            owner.Graveyard.Remove(zephyros);
            int zone = FindEmptyMonsterZone(owner);
            zephyros.EffectUsedOncePerDuel = true;
            PlaceSpecialSummon(owner, zephyros, zone, true);
            ApplyEffectDamage(owner, 400);
            return true;
        }

        private void ReturnFieldCardToHand(DuelPlayer owner, CardInstance card)
        {
            if (owner.MonsterZones.Contains(card))
                RemoveFromMonsterZones(owner, card);
            else if (owner.FieldSpellZone == card)
                owner.FieldSpellZone = null;
            else
                RemoveFromSpellTrapZones(owner, card);
            owner.Graveyard.Remove(card);
            owner.Hand.Add(card);
            Log(owner.Name + " returned " + card.Definition.DisplayName + " to the hand.");
        }

        private bool ActivateDrillWarrior(DuelPlayer owner, CardInstance source, CardInstance target)
        {
            if (source == null || !owner.MonsterZones.Contains(source))
                return Reject("Drill Warrior must be on your field.");

            if (target == null)
            {
                if (source.EffectUsedThisTurn)
                    return Reject("Drill Warrior's effect has already been used this turn.");
                source.AttackBonus -= source.CurrentAttack / 2;
                source.DrillWarriorDirectAttackThisTurn = true;
                source.EffectUsedThisTurn = true;
                return true;
            }

            CardInstance discard = target;
            if (!owner.Hand.Contains(discard) || discard == source)
                return Reject("Select a card in your hand to discard for Drill Warrior.");

            owner.Hand.Remove(discard);
            SendCardToGraveyard(owner, discard);
            RemoveFromMonsterZones(owner, source);
            owner.Banished.Add(source);
            source.DrillWarriorReturnTurn = turnNumber + 2;
            source.EffectUsedThisTurn = true;
            Log(owner.Name + " banished Drill Warrior until the next Standby Phase.");
            return true;
        }

        private bool ActivateLevelEater(DuelPlayer owner, CardInstance source, CardInstance target)
        {
            if (source == null || !owner.Graveyard.Contains(source) || target == null ||
                !owner.MonsterZones.Contains(target) || target.Definition.Level + target.LevelModifier < 5 ||
                FindEmptyMonsterZone(owner) < 0 || owner.SummonsLockedThisTurn)
                return Reject("Level Eater needs a Level 5 or higher monster you control and an open Monster Zone.");

            target.LevelModifier--;
            int zone = FindEmptyMonsterZone(owner);
            PlaceSpecialSummon(owner, source, zone, true);
            return true;
        }

        private bool ResolveMorphtronicCelfon(DuelPlayer owner, CardInstance source)
        {
            if (source == null || !owner.MonsterZones.Contains(source) || !source.FaceUp)
                return Reject("Celfon must be face-up on your field to activate its effect.");

            int count = random.Next(1, 7);
            List<CardInstance> revealed = owner.Deck.Take(count).ToList();
            if (!source.AttackPosition)
            {
                Log(owner.Name + " looked at the top " + revealed.Count +
                    " card(s) of the Deck with Morphtronic Celfon and returned them in the same order.");
            }
            else
            {
                CardInstance summon = revealed.FirstOrDefault(card =>
                    IsMorphtronic(card.Definition) && card.Definition.Level <= 4);
                foreach (CardInstance card in revealed)
                    owner.Deck.Remove(card);
                if (summon != null && FindEmptyMonsterZone(owner) >= 0)
                {
                    PlaceSpecialSummon(owner, summon, FindEmptyMonsterZone(owner), true);
                }
                else
                {
                    summon = null;
                }
                owner.Deck.AddRange(revealed.Where(card => card != summon));
                Shuffle(owner.Deck);
            }
            source.EffectUsedThisTurn = true;
            Log(owner.Name + " revealed " + revealed.Count + " card(s) with Morphtronic Celfon.");
            return true;
        }

        private bool ResolveMorphtronicRemoten(DuelPlayer owner, CardInstance source, CardInstance target)
        {
            if (source == null || !owner.MonsterZones.Contains(source) || !source.FaceUp)
                return Reject("Remoten must be face-up on your field to activate its effect.");
            if (source.AttackPosition)
            {
                if (target == null || !owner.Graveyard.Contains(target) || !IsMorphtronic(target.Definition) ||
                    !owner.Deck.Any(card => IsMorphtronic(card.Definition) &&
                                           card.Definition.Level == target.Definition.Level))
                    return Reject("Select a Morphtronic in your Graveyard with a matching Deck target.");
                owner.Graveyard.Remove(target);
                owner.Banished.Add(target);
                return BeginDeckChoice(owner, source,
                    card => IsMorphtronic(card.Definition) && card.Definition.Level == target.Definition.Level,
                    false, "Choose a Morphtronic with the same Level.");
            }

            CardInstance discard = target;
            CardInstance recover = discard == null ? null : owner.Graveyard.FirstOrDefault(card =>
                IsMorphtronic(card.Definition) && card.Definition.Level == discard.Definition.Level);
            if (discard == null || !owner.Hand.Contains(discard) || !IsMorphtronic(discard.Definition) ||
                recover == null)
                return Reject("In Defense Position, Remoten needs a Morphtronic in your hand and another of the same Level in your Graveyard.");
            owner.Hand.Remove(discard);
            SendCardToGraveyard(owner, discard);
            owner.Graveyard.Remove(recover);
            owner.Hand.Add(recover);
            return true;
        }

        private bool SpecialSummonQuickdraw(DuelPlayer owner, CardInstance source)
        {
            int zone = FindEmptyMonsterZone(owner);
            bool hasDiscard = source != null && owner.Hand.Any(card =>
                card != source && card.Definition.IsMonster);
            if (source == null || !owner.Hand.Contains(source) || !hasDiscard || zone < 0)
                return Reject("Quickdraw Synchron needs another monster to discard and an open Monster Zone.");
            if (owner.SummonsLockedThisTurn)
                return Reject("A card effect prevents you from Special Summoning this turn.");

            if (owner.IsAi)
            {
                CardInstance discard = owner.Hand.First(card =>
                    card != source && card.Definition.IsMonster);
                owner.Hand.Remove(discard);
                SendCardToGraveyard(owner, discard);
                PlaceSpecialSummon(owner, source, zone, true);
                return true;
            }

            pendingQuickdrawOwner = owner;
            pendingQuickdrawSource = source;
            return true;
        }

        private bool SpecialSummonQuillbolt(DuelPlayer owner, CardInstance source)
        {
            int zone = FindEmptyMonsterZone(owner);
            if (source == null || !owner.Graveyard.Contains(source) || zone < 0 ||
                owner.SummonsLockedThisTurn ||
                !owner.MonsterZones.Any(card => card != null && card.FaceUp && card.Definition.IsTuner))
                return Reject("Quillbolt Hedgehog needs a Tuner you control and an open Monster Zone.");
            source.BanishWhenLeavingField = true;
            PlaceSpecialSummon(owner, source, zone, true);
            return true;
        }

        private bool BeginPowerToolChoice(DuelPlayer owner, CardInstance source)
        {
            if (source == null || !owner.MonsterZones.Contains(source) || !source.FaceUp)
                return Reject("Power Tool Dragon must be face-up on your field.");
            List<CardInstance> equips = owner.Deck
                .Where(card => card.Definition.Kind == CardKind.Spell &&
                               card.Definition.Subtype == CardSubtype.Equip).ToList();
            if (equips.Count < 3)
                return Reject("Power Tool Dragon needs three Equip Spells in your Deck.");
            if (owner.IsAi)
            {
                List<CardInstance> revealed = equips.Take(3).ToList();
                CardInstance selected = revealed[random.Next(revealed.Count)];
                foreach (CardInstance equip in revealed)
                    owner.Deck.Remove(equip);
                owner.Hand.Add(selected);
                owner.Deck.AddRange(revealed.Where(card => card != selected));
                Shuffle(owner.Deck);
                return true;
            }
            pendingPowerToolChoiceSource = source;
            pendingPowerToolChoices = equips;
            Log(owner.Name + " must choose three Equip Spells to reveal with Power Tool Dragon.");
            return true;
        }

        private bool ResolveFlipEffect(DuelPlayer owner, CardInstance source)
        {
            if (source == null)
                return false;

            if (source.Definition.Effect == CardEffect.DekoichiDraw)
            {
                int extraDraws = owner.MonsterZones.Count(item =>
                    item != null && item.FaceUp &&
                    item.Definition.DisplayName == "Bokoichi the Freightening Car");
                DrawCards(owner, 1 + extraDraws);
                return false;
            }

            if (source.Definition.Effect != CardEffect.CyberJar)
                return false;

            Log(owner.Name + "'s Cyber Jar destroyed all monsters and revealed cards from both Decks.");
            foreach (CardInstance monster in GetAllFieldCards()
                         .Where(item => item.Definition.IsMonster).ToList())
                DestroyFieldCard(monster);

            foreach (DuelPlayer player in new[] { Human, Opponent })
            {
                List<CardInstance> revealed = player.Deck.Take(Math.Min(5, player.Deck.Count)).ToList();
                foreach (CardInstance revealedCard in revealed)
                    player.Deck.Remove(revealedCard);

                foreach (CardInstance revealedCard in revealed)
                {
                    int zone = FindEmptyMonsterZone(player);
                    if (revealedCard.Definition.IsMonster && revealedCard.Definition.Level <= 4 && zone >= 0)
                    {
                        revealedCard.FaceUp = true;
                        revealedCard.AttackPosition = true;
                        revealedCard.SummonedTurnNumber = turnNumber;
                        player.MonsterZones[zone] = revealedCard;
                        ResolveSummonTriggers(player, revealedCard, false);
                    }
                    else
                        player.Hand.Add(revealedCard);
                }
                Shuffle(player.Deck);
            }
            CheckWinner();
            return true;
        }

        private bool ResolveNoblemanOfCrossout(CardInstance target)
        {
            DuelPlayer targetOwner = GetCardController(target);
            if (target == null || targetOwner == null || !targetOwner.MonsterZones.Contains(target) ||
                target.FaceUp)
                return Reject("Nobleman of Crossout must target a face-down monster on the field.");

            bool isFlipMonster = target.Definition.RulesSummary.StartsWith("FLIP:",
                StringComparison.OrdinalIgnoreCase);
            string cardId = target.Definition.CardId;
            DestroyFieldCard(target);
            if (targetOwner.MonsterZones.Contains(target))
                return true;
            targetOwner.Graveyard.Remove(target);
            targetOwner.Banished.Add(target);
            if (!isFlipMonster)
                return true;

            foreach (DuelPlayer player in new[] { Human, Opponent })
            {
                foreach (CardInstance copy in player.Deck.Where(card => card.Definition.CardId == cardId).ToList())
                {
                    player.Deck.Remove(copy);
                    player.Banished.Add(copy);
                }
            }
            return true;
        }

        private void NotifySpellActivated(DuelPlayer owner)
        {
            foreach (CardInstance nitroWarrior in owner.MonsterZones.Where(card =>
                         card != null && card.FaceUp &&
                         card.Definition.Effect == CardEffect.NitroWarrior &&
                         !card.EffectUsedThisTurn).ToList())
            {
                nitroWarrior.NitroWarriorBoostReady = true;
                nitroWarrior.EffectUsedThisTurn = true;
            }
        }

        private static bool IsMorphtronic(CardDefinition definition)
        {
            return definition != null && definition.DisplayName.StartsWith("Morphtronic ",
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool CanActivateMonsterEffectFromHand(CardEffect effect)
        {
            return effect == CardEffect.QuickdrawSynchron;
        }

        private static bool CanActivateMonsterEffectFromGraveyard(CardEffect effect)
        {
            return effect == CardEffect.BlackwingVayuSummon || effect == CardEffect.ZephyrosRevive ||
                   effect == CardEffect.LevelEater || effect == CardEffect.QuillboltHedgehog;
        }

        private bool ApplyEffect(
            DuelPlayer owner,
            CardDefinition definition,
            CardInstance target,
            CardInstance source = null,
            IReadOnlyList<CardInstance> discardChoices = null)
        {
            DuelPlayer opponent = owner == Human ? Opponent : Human;
            bool handled;
            bool structureEffectResult = ApplyAdditionalStructureDeckEffect(
                owner, opponent, definition, target, source, out handled);
            if (handled)
                return structureEffectResult;
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
                case CardEffect.DestroyAllSpellTraps:
                    DestroySpellTrapCards(GetAllFieldCards(), definition.DisplayName);
                    return true;
                case CardEffect.DestroyOpponentSpellTraps:
                    DestroySpellTrapCards(opponent.SpellTrapZones
                        .Concat(new[] { opponent.FieldSpellZone }), definition.DisplayName);
                    return true;
                case CardEffect.CompulsoryEvacuationDevice:
                    DuelPlayer returnedMonsterOwner = GetMonsterOwner(target);
                    if (returnedMonsterOwner == null)
                        return Reject("Select a monster on the field to return to its owner's hand.");
                    RemoveFromMonsterZones(returnedMonsterOwner, target);
                    target.FaceUp = false;
                    target.AttackPosition = true;
                    returnedMonsterOwner.Hand.Add(target);
                    Log(target.Definition.DisplayName + " was returned to its owner's hand.");
                    return true;
                case CardEffect.DarkCoreBanish:
                    if (target == null || !target.FaceUp || !IsOnMonsterField(target))
                        return Reject("Select a face-up monster on the field to banish.");
                    if (!DiscardCards(owner, source, 1, definition.DisplayName, discardChoices))
                        return Reject("Dark Core needs a card in your hand to discard.");
                    BanishMonsterByCardEffect(target);
                    Log(target.Definition.DisplayName + " was banished by Dark Core.");
                    return true;
                case CardEffect.RaigekiBreak:
                    if (target == null || target == source || !GetAllFieldCards().Contains(target))
                        return Reject("Select another card on the field to destroy.");
                    if (!DiscardCards(owner, source, 1, definition.DisplayName, discardChoices))
                        return Reject("Raigeki Break needs a card in your hand to discard.");
                    DestroyFieldCard(target);
                    Log(target.Definition.DisplayName + " was destroyed by Raigeki Break.");
                    return true;
                case CardEffect.DimensionalPrison:
                case CardEffect.MirrorForce:
                case CardEffect.BottomlessTrapHole:
                    return Reject("This Trap effect only resolves in response to its matching event.");
                case CardEffect.DrawOne:
                    DrawCards(owner, 1);
                    return true;
                case CardEffect.DrawTwo:
                    DrawCards(owner, 2);
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
                case CardEffect.NoblemanOfCrossout:
                    return ResolveNoblemanOfCrossout(target);
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
                    CardInstance discard = discardChoices != null
                        ? discardChoices.FirstOrDefault()
                        : owner.Hand.FirstOrDefault(item =>
                            item != source && item.Definition.IsMonster);
                    if (discard == null || FindEmptyMonsterZone(owner) < 0)
                        return Reject("One for One no longer has a legal cost or open Monster Zone.");
                    owner.Hand.Remove(discard);
                    SendCardToGraveyard(owner, discard);
                    return BeginDeckChoice(owner, source,
                        candidate => candidate.Definition.IsMonster &&
                                     candidate.Definition.Level == 1,
                        false, "Choose a Level 1 monster from your Deck to Special Summon.",
                        specialSummon: true);
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
                    DrawCards(owner, 2);
                    return true;
                case CardEffect.Scapegoat:
                    CardDefinition tokenDefinition = StarterCatalog.Find("73915052");
                    if (tokenDefinition == null)
                        return Reject("The Sheep Token card definition is missing.");
                    owner.SummonsLockedThisTurn = true;
                    for (int i = 0; i < 4; i++)
                    {
                        int tokenZone = FindEmptyMonsterZone(owner);
                        if (tokenZone < 0)
                            break;
                        CardInstance token = new CardInstance(tokenDefinition)
                        {
                            CannotBeTributedForTributeSummon = true
                        };
                        PlaceSpecialSummon(owner, token, tokenZone, false);
                    }
                    return true;
                case CardEffect.CallOfTheHaunted:
                    return ActivateCallOfTheHaunted(owner, source, target);
                case CardEffect.BlackWhirlwindSearch:
                    return true;
                case CardEffect.DiscardDrawSameCount:
                    int ownerDiscardCount = owner.Hand.Count - (source != null && owner.Hand.Contains(source) ? 1 : 0);
                    int opponentDiscardCount = opponent.Hand.Count;
                    DiscardCards(owner, source, ownerDiscardCount, definition.DisplayName, discardChoices);
                    DiscardCards(opponent, null, opponentDiscardCount, definition.DisplayName);
                    DrawCards(owner, ownerDiscardCount);
                    DrawCards(opponent, opponentDiscardCount);
                    return true;
                case CardEffect.DrawThreeDiscardTwo:
                    DrawCards(owner, 3);
                    DiscardCards(owner, source, 2, definition.DisplayName, discardChoices);
                    return true;
                case CardEffect.DiscardOneDestroyFaceUpOpponentMonsters:
                    if (!DiscardCards(owner, source, 1, definition.DisplayName, discardChoices))
                        return Reject("You need a card in your hand to discard.");
                    foreach (CardInstance monster in opponent.MonsterZones
                                 .Where(item => item != null && item.FaceUp).ToList())
                        DestroyMonsterByCardEffect(opponent, monster);
                    CheckWinner();
                    return true;
                case CardEffect.DestroyAllMonsters:
                    foreach (CardInstance monster in owner.MonsterZones.Concat(opponent.MonsterZones)
                                 .Where(item => item != null).ToList())
                        DestroyMonsterByCardEffect(GetMonsterOwner(monster), monster);
                    CheckWinner();
                    return true;
                case CardEffect.DestroyAllOpponentMonsters:
                    foreach (CardInstance monster in opponent.MonsterZones.Where(item => item != null).ToList())
                        DestroyMonsterByCardEffect(opponent, monster);
                    CheckWinner();
                    return true;
                case CardEffect.DestroyHighestDefenseMonster:
                    CardInstance highestDefense = opponent.MonsterZones
                        .Where(item => item != null && item.FaceUp)
                        .OrderByDescending(item => item.Definition.Defense)
                        .FirstOrDefault();
                    if (highestDefense == null)
                        return Reject("The opponent has no face-up monster to destroy.");
                    DestroyMonsterByCardEffect(opponent, highestDefense);
                    CheckWinner();
                    return true;
                case CardEffect.ReturnAllSpellTrapsToHand:
                    ReturnAllSpellTrapsToHand(owner);
                    ReturnAllSpellTrapsToHand(opponent);
                    return true;
                case CardEffect.ReviveMonsterFromGraveyard:
                    return ReviveFromGraveyard(owner, target);
                case CardEffect.BurnOpponent:
                    ApplyEffectDamage(opponent, definition.EffectValue);
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
                    DestroyMonsterByCardEffect(opponent, weakest);
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
                    DestroyMonsterByCardEffect(opponent, target);
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
                    ApplyEffectDamage(opponent, 500 * spells.Count);
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
                    ApplyEffectDamage(opponent, 500);
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
                    DestroyMonsterByCardEffect(opponent, targetToDestroy);
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
            List<CardInstance> responses = GetResponseCards(defender, eventType, source);
            if (eventType == CardEffect.DestroyAllMonsters &&
                (source == null || source.CurrentAttack < 1500))
                responses.RemoveAll(card => card.Definition.Effect == CardEffect.BottomlessTrapHole);
            if (responses.Count == 0)
                return ResponseAttempt.None;

            chainLinkCount = 1;
            if (defender.IsAi || IsAiVsAi)
            {
                CardInstance aiTrap = responses[0];
                if (aiTrap.Definition.Effect == CardEffect.DustTornado)
                {
                    CardInstance target = GetOpponent(defender).SpellTrapZones
                        .Concat(new[] { GetOpponent(defender).FieldSpellZone })
                        .FirstOrDefault(card => card != null);
                    if (target == null)
                        return ResponseAttempt.None;
                    ActivateResponseTrap(defender, aiTrap);
                    ResolveDustTornado(defender, aiTrap, target,
                        defender.Hand.FirstOrDefault(card => card.Definition.Kind == CardKind.Spell ||
                            card.Definition.Kind == CardKind.Trap));
                    if (onPass != null)
                        onPass();
                    return ResponseAttempt.Cancelled;
                }
                if (aiTrap.Definition.Effect == CardEffect.MajesticStarDragon)
                {
                    RemoveFromMonsterZones(defender, aiTrap);
                    defender.Graveyard.Add(aiTrap);
                    if (onCancelled != null)
                        onCancelled();
                    foreach (CardInstance fieldCard in GetAllFieldCards()
                                 .Where(card => GetCardController(card) == GetOpponent(defender)).ToList())
                        DestroyFieldCard(fieldCard);
                    CheckWinner();
                    Log("Majestic Star Dragon negated the activation and destroyed all cards its opponent controlled.");
                    return ResponseAttempt.Cancelled;
                }
                if (aiTrap.Definition.Effect == CardEffect.ShootingStarDragon)
                {
                    if (eventType == CardEffect.StopAttack)
                    {
                        RemoveFromMonsterZones(defender, aiTrap);
                        defender.Banished.Add(aiTrap);
                        aiTrap.ShootingStarAttackNegateUsedThisTurn = true;
                        aiTrap.ReturnAtEndPhaseTurn = turnNumber;
                        Log("Shooting Star Dragon banished itself to negate the attack.");
                    }
                    else
                    {
                        if (!IsDestructionEffect(source))
                            return ResponseAttempt.None;
                        aiTrap.ShootingStarDestructionNegateUsedThisTurn = true;
                        aiTrap.EffectUsedThisTurn = true;
                        if (onCancelled != null)
                            onCancelled();
                        DuelPlayer sourceOwner = GetCardController(source);
                        if (sourceOwner != null)
                            DestroyFieldCard(source);
                        Log("Shooting Star Dragon negated and destroyed " + source.Definition.DisplayName + "'s effect.");
                    }
                    return ResponseAttempt.Cancelled;
                }
                if (aiTrap.Definition.Effect == CardEffect.FormulaSynchronDraw)
                {
                    CardDefinition quickSynchro = FindQuickSynchro(defender, aiTrap,
                        out List<CardInstance> quickMaterials);
                    if (quickSynchro == null || !ResolveSynchroSummon(defender, quickSynchro, quickMaterials))
                        return ResponseAttempt.None;
                    if (onPass != null)
                        onPass();
                    return ResponseAttempt.Cancelled;
                }
                if (aiTrap.Definition.Effect == CardEffect.StardustDragon)
                {
                    if (!IsDestructionEffect(source))
                        return ResponseAttempt.None;
                    RemoveFromMonsterZones(defender, aiTrap);
                    defender.Graveyard.Add(aiTrap);
                    aiTrap.StardustReturnPending = true;
                    aiTrap.EffectUsedThisTurn = true;
                    return ResponseAttempt.Cancelled;
                }
                if (IsDarkPaladin(aiTrap))
                {
                    CardInstance discard = defender.Hand
                        .OrderBy(card => card.Definition.Attack + card.Definition.Level * 100)
                        .FirstOrDefault();
                    if (discard != null)
                    {
                        defender.Hand.Remove(discard);
                        SendCardToGraveyard(defender, discard);
                        aiTrap.EffectUsedThisTurn = true;
                        Log(defender.Name + " discarded a card for Dark Paladin.");
                        onCancelled();
                        return ResponseAttempt.Cancelled;
                    }
                }
                bool counterEvent = eventType == CardEffect.CounterSpell ||
                                    eventType == CardEffect.CounterMonsterEffect;
                bool matchingCounter = aiTrap.Definition.Effect == eventType &&
                                       aiTrap.Definition.Subtype == CardSubtype.Counter;
                if (counterEvent &&
                    (matchingCounter ||
                     (eventType == CardEffect.CounterSpell &&
                      (aiTrap.Definition.Effect == CardEffect.SolemnJudgment ||
                       aiTrap.Definition.Effect == CardEffect.StarlightRoad))))
                {
                    if (aiTrap.Definition.Effect == CardEffect.StarlightRoad &&
                        !WouldDestroyMultipleOwnedCards(source, defender))
                        return ResponseAttempt.None;
                    if ((aiTrap.Definition.Effect == CardEffect.CounterSpell ||
                         aiTrap.Definition.Effect == CardEffect.CounterMonsterEffect) &&
                        defender.Hand.Count == 0)
                        return ResponseAttempt.None;
                    if (aiTrap.Definition.Effect == CardEffect.CounterSpell ||
                        aiTrap.Definition.Effect == CardEffect.CounterMonsterEffect)
                    {
                        CardInstance discard = defender.Hand
                            .OrderBy(card => card.Definition.Attack + card.Definition.Level * 100)
                            .First();
                        defender.Hand.Remove(discard);
                        SendCardToGraveyard(defender, discard);
                        Log(defender.Name + " discarded a card for " + aiTrap.Definition.DisplayName + ".");
                    }
                    ActivateResponseTrap(defender, aiTrap);
                    if (aiTrap.Definition.Effect == CardEffect.SolemnJudgment)
                    {
                        defender.LifePoints /= 2;
                        CheckWinner();
                    }
                    else if (aiTrap.Definition.Effect == CardEffect.StarlightRoad)
                        SpecialSummonStardustFromExtra(defender);
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
                    onCancelled();
                    if (eventType == CardEffect.CounterMonsterEffect)
                    {
                        DuelPlayer sourceOwner = GetMonsterOwner(source);
                        if (sourceOwner != null)
                            DestroyMonsterByCardEffect(sourceOwner, source);
                    }
                    return ResponseAttempt.Cancelled;
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
                if (eventType == CardEffect.StopAttack)
                    ResolveAttackResponse(defender, aiTrap, source);
                else if (eventType == CardEffect.DestroyAllMonsters)
                    ResolveSummonResponse(aiTrap, source);
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
            responseDiscardCostPaid = false;
            responseDiscardReason = null;
            pendingResponseType = CardEffect.None;
            pendingDustTornadoResponse = null;
        }

        private List<CardInstance> GetResponseCards(
            DuelPlayer player, CardEffect eventType, CardInstance source = null)
        {
            if (player == null)
                return new List<CardInstance>();
            List<CardInstance> responses = GetResponseTraps(player, eventType);
            bool opponentActivation = eventType == CardEffect.CounterSpell ||
                eventType == CardEffect.CounterMonsterEffect || eventType == CardEffect.CounterTrap;
            if (opponentActivation)
            {
                CardInstance majestic = player.MonsterZones.FirstOrDefault(card =>
                    card != null && card.FaceUp && !card.EffectsNegated &&
                    card.Definition.Effect == CardEffect.MajesticStarDragon);
                if (majestic != null)
                    responses.Add(majestic);
                CardInstance formula = player.MonsterZones.FirstOrDefault(card =>
                    card != null && card.FaceUp && !card.EffectsNegated &&
                    card.Definition.Effect == CardEffect.FormulaSynchronDraw);
                if (formula != null && FindQuickSynchro(player, formula, out _ ) != null)
                    responses.Add(formula);
                CardInstance shootingStar = player.MonsterZones.FirstOrDefault(card =>
                    card != null && card.FaceUp && !card.EffectsNegated &&
                    card.Definition.Effect == CardEffect.ShootingStarDragon &&
                    !card.ShootingStarDestructionNegateUsedThisTurn);
                if (shootingStar != null && IsDestructionEffect(source))
                    responses.Add(shootingStar);
            }
            if (eventType == CardEffect.StopAttack)
            {
                CardInstance shootingStar = player.MonsterZones.FirstOrDefault(card =>
                    card != null && card.FaceUp && !card.EffectsNegated &&
                    card.Definition.Effect == CardEffect.ShootingStarDragon &&
                    !card.ShootingStarAttackNegateUsedThisTurn);
                if (shootingStar != null)
                    responses.Add(shootingStar);
            }
            if ((eventType == CardEffect.CounterSpell || eventType == CardEffect.CounterMonsterEffect) &&
                player.MonsterZones.Any(card => card != null && card.FaceUp &&
                                                card.Definition.Effect == CardEffect.StardustDragon &&
                                                !card.EffectUsedThisTurn))
                responses.Add(player.MonsterZones.First(card => card != null && card.FaceUp &&
                    card.Definition.Effect == CardEffect.StardustDragon && !card.EffectUsedThisTurn));
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

        private bool IsDestructionEffect(CardInstance source)
        {
            if (source == null)
                return false;
            switch (source.Definition.Effect)
            {
                case CardEffect.DestroySpellTrap:
                case CardEffect.DustTornado:
                case CardEffect.DestroyAllSpellTraps:
                case CardEffect.DestroyOpponentSpellTraps:
                case CardEffect.RaigekiBreak:
                case CardEffect.DestroyAllMonsters:
                case CardEffect.DestroyAllOpponentMonsters:
                case CardEffect.DestroyWeakest:
                case CardEffect.DestroyHighestDefenseMonster:
                case CardEffect.BlackRoseDragon:
                case CardEffect.SpellShatteringArrow:
                case CardEffect.DestroySetOpponentBackrow:
                    return true;
                default:
                    return false;
            }
        }

        private bool WouldDestroyMultipleOwnedCards(CardInstance source, DuelPlayer owner)
        {
            if (source == null || owner == null)
                return false;
            switch (source.Definition.Effect)
            {
                case CardEffect.DestroyAllMonsters:
                    return owner.MonsterZones.Count(card => card != null) >= 2;
                case CardEffect.DestroyAllOpponentMonsters:
                case CardEffect.BlackRoseDragon:
                    return owner.MonsterZones.Count(card => card != null) >= 2;
                default:
                    return false;
            }
        }

        private void SpecialSummonStardustFromExtra(DuelPlayer owner)
        {
            CardDefinition stardust = owner.ExtraDeck
                .FirstOrDefault(card => card.Definition.Effect == CardEffect.StardustDragon)?.Definition;
            int zone = FindEmptyMonsterZone(owner);
            if (stardust == null || zone < 0)
                return;
            owner.ExtraDeck.RemoveAll(card => card.Definition == stardust);
            CardInstance summoned = new CardInstance(stardust)
            {
                SummonedTurnNumber = turnNumber
            };
            owner.MonsterZones[zone] = summoned;
            Log(owner.Name + " Special Summoned Stardust Dragon with Starlight Road.");
        }

        private static bool IsDarkPaladin(CardInstance card)
        {
            return card != null && card.Definition.DisplayName == "Dark Paladin";
        }

        private List<CardInstance> GetResponseTraps(DuelPlayer player, CardEffect eventType)
        {
            if (player == null || eventType == CardEffect.None ||
                Human.MonsterZones.Concat(Opponent.MonsterZones)
                    .Any(monster => monster != null && monster.FaceUp &&
                                    !monster.EffectsNegated &&
                                    monster.Definition.Effect == CardEffect.JinzoTrapLock))
                return new List<CardInstance>();

            return player.SpellTrapZones.Where(card => card != null && !card.FaceUp &&
                card.Definition.Kind == CardKind.Trap &&
                (eventType == CardEffect.StopAttack
                    ? card.Definition.Effect == CardEffect.StopAttack ||
                      card.Definition.Effect == CardEffect.SakuretsuArmor ||
                      card.Definition.Effect == CardEffect.DimensionalPrison ||
                      card.Definition.Effect == CardEffect.MirrorForce ||
                      card.Definition.Effect == CardEffect.CompulsoryEvacuationDevice ||
                      card.Definition.Effect == CardEffect.ScrapIronScarecrow ||
                      card.Definition.Effect == CardEffect.DustTornado ||
                      card.Definition.DisplayName == "Magic Cylinder"
                    : eventType == CardEffect.DestroyAllMonsters
                        ? card.Definition.Effect == CardEffect.TorrentialTribute ||
                          card.Definition.Effect == CardEffect.BottomlessTrapHole ||
                          card.Definition.Effect == CardEffect.DustTornado
                        : card.Definition.Effect == CardEffect.SolemnJudgment ||
                          card.Definition.Effect == CardEffect.StarlightRoad ||
                          card.Definition.Effect == CardEffect.DustTornado ||
                          (card.Definition.Effect == eventType &&
                           card.Definition.Subtype == CardSubtype.Counter))).ToList();
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
            if (attacker != null && attacker.Definition.Effect == CardEffect.DrillWarrior &&
                attacker.DrillWarriorDirectAttackThisTurn)
                return true;
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
            if (response.Definition.Effect == CardEffect.MirrorForce)
            {
                List<CardInstance> targets = attackerOwner.MonsterZones
                    .Where(card => card != null && card.FaceUp && card.AttackPosition).ToList();
                foreach (CardInstance target in targets)
                {
                    DestroyMonsterByCardEffect(attackerOwner, target);
                    Log(target.Definition.DisplayName + " was destroyed by Mirror Force.");
                }
                return true;
            }

            if (response.Definition.DisplayName == "Magic Cylinder")
            {
                ApplyEffectDamage(attackerOwner, attacker.CurrentAttack);
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
                DestroyMonsterByCardEffect(attackerOwner, attacker);
                Log("Sakuretsu Armor destroyed " + attacker.Definition.DisplayName + ".");
                CheckWinner();
                return true;
            }

            if (response.Definition.Effect == CardEffect.DimensionalPrison)
            {
                BanishMonsterByCardEffect(attacker);
                Log("Dimensional Prison banished " + attacker.Definition.DisplayName + ".");
                CheckWinner();
                return true;
            }

            if (response.Definition.Effect == CardEffect.CompulsoryEvacuationDevice)
            {
                RemoveFromMonsterZones(attackerOwner, attacker);
                attacker.FaceUp = false;
                attacker.AttackPosition = true;
                attackerOwner.Hand.Add(attacker);
                Log("Compulsory Evacuation Device returned " + attacker.Definition.DisplayName +
                    " to its owner's hand.");
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

        private bool ResolveSummonResponse(CardInstance response, CardInstance summoned)
        {
            if (response == null || summoned == null)
                return false;
            if (response.Definition.Effect == CardEffect.TorrentialTribute)
            {
                DestroyAllMonstersOnField();
                CheckWinner();
                Log("Torrential Tribute destroyed all monsters on the field.");
                return true;
            }
            if (response.Definition.Effect != CardEffect.BottomlessTrapHole ||
                summoned.CurrentAttack < 1500)
                return false;

            DuelPlayer summonedOwner = GetMonsterOwner(summoned);
            if (summonedOwner != null && DestroyMonsterByCardEffect(summonedOwner, summoned))
            {
                if (summonedOwner.Graveyard.Remove(summoned))
                    summonedOwner.Banished.Add(summoned);
                Log("Bottomless Trap Hole destroyed and banished " + summoned.Definition.DisplayName + ".");
            }
            CheckWinner();
            return true;
        }

        private void ResolveSynchroSummonTriggers()
        {
            foreach (DuelPlayer player in new[] { Human, Opponent })
            {
                int librarianCount = player.MonsterZones.Count(card =>
                    card != null && card.FaceUp &&
                    card.Definition.Effect == CardEffect.TGHyperLibrarian &&
                    !card.EffectsNegated);
                DrawCards(player, librarianCount);
            }
        }

        private void DestroyAllMonstersOnField()
        {
            foreach (CardInstance monster in Human.MonsterZones.Concat(Opponent.MonsterZones)
                         .Where(item => item != null).ToList())
                DestroyMonsterByCardEffect(GetMonsterOwner(monster), monster);
        }

        private bool IsValidSynchroMaterial(CardDefinition synchro, IReadOnlyList<CardInstance> materials, DuelPlayer owner)
        {
            if (materials == null || materials.Count < 2 ||
                materials.Any(card => card == null || !card.FaceUp || !owner.MonsterZones.Contains(card) ||
                    !CanUseSynchroMaterial(synchro, card)))
                return false;
            if (materials.Sum(card => GetSynchroMaterialLevel(synchro, card)) != synchro.Level ||
                materials.Count(card => card.Definition.IsTuner) != 1 ||
                !materials.Any(card => !card.Definition.IsTuner))
                return false;
            if (synchro.Effect == CardEffect.BlackwingSilverwind &&
                (materials.Count(card => !card.Definition.IsTuner) < 2 ||
                 !materials.Any(card => card.Definition.IsTuner && IsBlackwing(card.Definition))))
                return false;
            return true;
        }

        private static bool CanUseSynchroMaterial(CardDefinition synchro, CardInstance material)
        {
            if (material.Definition.Effect == CardEffect.MajesticDragon &&
                synchro.DisplayName.IndexOf("Majestic", StringComparison.OrdinalIgnoreCase) < 0)
                return false;
            if (material.Definition.Effect == CardEffect.BlackwingVayuSummon)
                return false;
            if (material.Definition.Effect == CardEffect.QuickdrawSynchron &&
                synchro.RulesSummary.IndexOf("Synchron", StringComparison.OrdinalIgnoreCase) < 0)
                return false;
            if (material.Definition.Effect == CardEffect.DebrisDragonRevive &&
                !string.Equals(synchro.MonsterType, "Dragon", StringComparison.OrdinalIgnoreCase))
                return false;
            if (synchro.Effect == CardEffect.LifeStreamSetLifePoints &&
                !material.Definition.IsTuner &&
                material.Definition.Effect != CardEffect.PowerToolDragon)
                return false;
            return true;
        }

        public static int GetSynchroMaterialLevel(CardDefinition synchro, CardInstance material)
        {
            int level = material.Definition.Level + material.LevelModifier;
            if (material.Definition.Effect == CardEffect.RoadSynchron &&
                !string.Equals(synchro.DisplayName, "Road Warrior", StringComparison.OrdinalIgnoreCase))
                level -= 2;
            if (material.Definition.Effect == CardEffect.TuningwareDraw)
                level = 2;
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
                DestroyMonsterByCardEffect(monsterOwner, card);
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

        private void DestroySpellTrapCards(IEnumerable<CardInstance> cards, string sourceName)
        {
            foreach (CardInstance card in cards
                         .Where(item => item != null &&
                             (item.Definition.Kind == CardKind.Spell ||
                              item.Definition.Kind == CardKind.Trap))
                         .Distinct().ToList())
            {
                DestroyFieldCard(card);
                Log(card.Definition.DisplayName + " was destroyed by " + sourceName + ".");
            }
        }

        private bool DestroyMonsterByCardEffect(DuelPlayer owner, CardInstance monster)
        {
            if (monster == null || monster.CannotBeDestroyedByCardEffects)
                return false;
            if (TryReplaceMonsterDestruction(owner, monster))
                return false;
            MonsterDestroyed?.Invoke(owner, monster);
            SendMonsterToGraveyard(owner, monster);
            return true;
        }

        private bool TryReplaceMonsterDestruction(DuelPlayer owner, CardInstance monster)
        {
            if (owner == null || monster == null || !owner.MonsterZones.Contains(monster) ||
                monster.EffectsNegated || !monster.FaceUp)
                return false;

            if (monster.Definition.Effect == CardEffect.LifeStreamSetLifePoints)
            {
                CardInstance equip = owner.Graveyard.FirstOrDefault(card =>
                    card.Definition.Kind == CardKind.Spell && card.Definition.Subtype == CardSubtype.Equip);
                if (equip == null)
                    return false;
                owner.Graveyard.Remove(equip);
                owner.Banished.Add(equip);
                Log(owner.Name + " banished " + equip.Definition.DisplayName +
                    " to prevent Life Stream Dragon's destruction.");
                return true;
            }

            if (monster.Definition.Effect != CardEffect.PowerToolDragon)
                return false;
            CardInstance equipped = owner.SpellTrapZones.FirstOrDefault(card =>
                card != null && card.FaceUp && card.Definition.Kind == CardKind.Spell &&
                card.Definition.Subtype == CardSubtype.Equip && card.EquippedMonster == monster);
            if (equipped == null)
                return false;
            RemoveFromSpellTrapZones(owner, equipped);
            owner.Graveyard.Add(equipped);
            Log(owner.Name + " sent " + equipped.Definition.DisplayName +
                " to the Graveyard to prevent Power Tool Dragon's destruction.");
            return true;
        }

        private bool BanishMonsterByCardEffect(CardInstance monster)
        {
            DuelPlayer owner = GetMonsterOwner(monster);
            if (owner == null)
                return false;
            RemoveFromMonsterZones(owner, monster);
            owner.Banished.Add(monster);
            return true;
        }

        private void PlaceSpecialSummon(DuelPlayer owner, CardInstance card, int zone, bool attackPosition)
        {
            bool summonedFromGraveyard = owner.Graveyard.Contains(card);
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
            if (summonedFromGraveyard)
                ResolveGraveyardSummonEffects(owner);
        }

        private void ResolveSynchroMaterialEffects(
            DuelPlayer owner, IReadOnlyCollection<CardInstance> materials, CardInstance synchro)
        {
            DrawCards(owner, materials.Count(material =>
                material.Definition.Effect == CardEffect.TuningwareDraw));
            int clearEffectorCount = materials.Count(material =>
                material.Definition.Effect == CardEffect.ClearEffector);
            DrawCards(owner, clearEffectorCount);
            if (clearEffectorCount > 0)
                synchro.CannotBeDestroyedByCardEffects = true;

            int junkConverterCount = materials.Count(material =>
                material.Definition.Effect == CardEffect.JunkConverter);
            for (int i = 0; i < junkConverterCount; i++)
            {
                CardInstance tuner = owner.Graveyard.FirstOrDefault(card => card.Definition.IsTuner);
                if (tuner != null && SpecialSummonFromGraveyard(owner, tuner, false))
                    tuner.EffectsNegated = true;
                else
                    break;
            }

            int doppelwarriorCount = materials.Count(material =>
                material.Definition.Effect == CardEffect.Doppelwarrior);
            if (doppelwarriorCount == 0)
                return;

            CardDefinition tokenDefinition = StarterCatalog.Find("53855410");
            if (tokenDefinition == null)
            {
                Log("Doppelwarrior could not create Tokens because its Token definition is missing.");
                return;
            }

            for (int i = 0; i < doppelwarriorCount * 2; i++)
            {
                int zone = FindEmptyMonsterZone(owner);
                if (zone < 0)
                    break;
                PlaceSpecialSummon(owner, new CardInstance(tokenDefinition), zone, true);
            }
        }

        private void ResolveGraveyardSummonEffects(DuelPlayer owner)
        {
            if (owner.SummonsLockedThisTurn)
                return;

            CardInstance doppelwarrior;
            int zone;
            while ((doppelwarrior = owner.Hand.FirstOrDefault(card =>
                       card.Definition.Effect == CardEffect.Doppelwarrior)) != null &&
                   (zone = FindEmptyMonsterZone(owner)) >= 0)
                PlaceSpecialSummon(owner, doppelwarrior, zone, true);
        }

        private void ResolveBlackwingShura(DuelPlayer owner, CardInstance source)
        {
            if (owner.SummonsLockedThisTurn || FindEmptyMonsterZone(owner) < 0)
                return;

            BeginDeckChoice(owner, source,
                card => IsBlackwing(card.Definition) && card.Definition.Attack <= 1500,
                false, "Choose a Blackwing monster with 1500 or less ATK to Special Summon.",
                specialSummon: true, negateSpecialSummonedEffects: true);
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
            if (cardOwner == owner)
                ResolveGraveyardSummonEffects(owner);
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
            bool applySanganRestriction = false,
            bool specialSummon = false,
            bool optional = false,
            bool negateSpecialSummonedEffects = false)
        {
            if (specialSummon && owner.SummonsLockedThisTurn)
                return Reject("A card effect prevents you from Special Summoning this turn.");
            List<CardInstance> choices = owner.Deck.Where(filter).ToList();
            if (choices.Count == 0)
                return Reject("No cards in the Deck match this effect.");

            if (owner.IsAi)
            {
                CardInstance choice = choices
                    .OrderByDescending(card => card.Definition.Effect != CardEffect.None)
                    .ThenByDescending(card => card.Definition.Attack)
                    .First();
                CompleteDeckChoice(owner, choice, toGraveyard, applySanganRestriction,
                    specialSummon, negateSpecialSummonedEffects);
                return true;
            }

            pendingDeckChoiceOwner = owner;
            pendingDeckChoiceSource = source;
            pendingDeckChoiceFilter = filter;
            pendingDeckChoicePrompt = prompt;
            pendingDeckChoiceToGraveyard = toGraveyard;
            pendingDeckChoiceSanganRestriction = applySanganRestriction;
            pendingDeckChoiceSpecialSummon = specialSummon;
            pendingDeckChoiceOptional = optional;
            pendingDeckChoiceNegatesEffects = negateSpecialSummonedEffects;
            Log(owner.Name + " must choose a card from the Deck.");
            return true;
        }

        private void CompleteDeckChoice(
            DuelPlayer owner,
            CardInstance card,
            bool toGraveyard,
            bool applySanganRestriction,
            bool specialSummon = false,
            bool negateSpecialSummonedEffects = false)
        {
            owner.Deck.Remove(card);
            if (specialSummon)
            {
                int zone = FindEmptyMonsterZone(owner);
                if (zone < 0)
                    throw new InvalidOperationException("The selected Special Summon no longer has an open Monster Zone.");
                PlaceSpecialSummon(owner, card, zone, true);
                if (negateSpecialSummonedEffects)
                    card.EffectsNegated = true;
                return;
            }
            if (toGraveyard)
            {
                SendCardToGraveyard(owner, card);
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
                    " to hand. Its effects cannot be activated this turn.");
                return;
            }
            Log(owner.Name + " added " + card.Definition.DisplayName + " from the Deck to the hand.");
            if (pendingDeckChoiceSource != null &&
                pendingDeckChoiceSource.Definition.Effect == CardEffect.Tuning &&
                owner.Deck.Count > 0)
            {
                CardInstance milled = owner.Deck[0];
                owner.Deck.RemoveAt(0);
                SendCardToGraveyard(owner, milled);
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
            pendingDeckChoiceSpecialSummon = false;
            pendingDeckChoiceOptional = false;
            pendingDeckChoiceNegatesEffects = false;
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

        private void SendCardToGraveyard(DuelPlayer owner, CardInstance card, bool deferTriggers = false)
        {
            if (owner == null || card == null)
                return;
            owner.Graveyard.Add(card);
            if (deferTriggers)
                deferredGraveyardEffects.Add(new DeferredGraveyardEffect { Owner = owner, Card = card });
            else
                ResolveSentToGraveyardEffects(owner, card, false, false);
        }

        private void ResolveDeferredGraveyardEffects()
        {
            if (deferredGraveyardEffects.Count == 0)
                return;
            DeferredGraveyardEffect[] pending = deferredGraveyardEffects.ToArray();
            deferredGraveyardEffects.Clear();
            foreach (DeferredGraveyardEffect effect in pending)
                ResolveSentToGraveyardEffects(effect.Owner, effect.Card,
                    effect.SentFromField, effect.DestroyedByBattle);
        }

        private bool DiscardCards(
            DuelPlayer owner,
            CardInstance excludedCard,
            int count,
            string sourceName,
            IReadOnlyList<CardInstance> selectedCards = null)
        {
            List<CardInstance> candidates = owner.Hand.Where(card => card != excludedCard).ToList();
            if (candidates.Count < count)
                return false;

            List<CardInstance> discarded;
            if (selectedCards != null)
            {
                if (selectedCards.Count != count || selectedCards.Distinct().Count() != count ||
                    selectedCards.Any(card => !candidates.Contains(card)))
                    return false;
                discarded = selectedCards.ToList();
            }
            else
            {
                discarded = candidates
                    .OrderBy(card => card.Definition.Effect == CardEffect.None ? 0 : 1)
                    .ThenBy(card => card.Definition.Attack)
                    .Take(count)
                    .ToList();
            }
            foreach (CardInstance card in discarded)
            {
                owner.Hand.Remove(card);
                card.FaceUp = false;
                SendCardToGraveyard(owner, card);
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
            if (cardOwner == owner)
                ResolveGraveyardSummonEffects(owner);
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

        private void DrawCards(DuelPlayer player, int count)
        {
            for (int i = 0; i < count && Phase != DuelPhase.GameOver; i++)
                DrawCard(player);
        }

        private bool CanActInMainPhase()
        {
            return !SearchChoiceOpen && !PowerToolChoiceOpen && !QuickdrawDiscardChoiceOpen &&
                   !GraveyardSummonChoiceOpen && !ActivationDiscardChoiceOpen &&
                   CurrentPlayer != null &&
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

        private void SendMonsterToGraveyard(DuelPlayer owner, CardInstance monster, bool byBattle = false)
        {
            SendMonsterToGraveyardInternal(owner, monster, byBattle, false);
        }

        private void SendMonsterToGraveyardDeferred(DuelPlayer owner, CardInstance monster)
        {
            SendMonsterToGraveyardInternal(owner, monster, false, true);
        }

        private void SendMonsterToGraveyardInternal(
            DuelPlayer owner, CardInstance monster, bool byBattle, bool deferTriggers)
        {
            if (owner == null || monster == null || !owner.MonsterZones.Contains(monster))
                return;
            if (byBattle && TryReplaceMonsterDestruction(owner, monster))
                return;
            if (byBattle)
                MonsterDestroyed?.Invoke(owner, monster);

            if (!byBattle && monster.Definition.Effect == CardEffect.BlackwingMistral)
                owner.BattleDamageProtectedTurn = turnNumber;
            RemoveFromMonsterZones(owner, monster);
            monster.DestroyAtEndPhaseTurn = 0;
            monster.LevelModifier = 0;
            monster.WedgeCounters = 0;
            if (monster.BanishWhenLeavingField)
                owner.Banished.Add(monster);
            else if (monster.Definition.Kind != CardKind.Token)
                owner.Graveyard.Add(monster);
            if (deferTriggers)
                deferredGraveyardEffects.Add(new DeferredGraveyardEffect
                {
                    Owner = owner,
                    Card = monster,
                    SentFromField = true,
                    DestroyedByBattle = byBattle
                });
            else
                ResolveSentToGraveyardEffects(owner, monster, true, byBattle);
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

        private void ResolveSentToGraveyardEffects(
            DuelPlayer owner, CardInstance card, bool sentFromField, bool destroyedByBattle)
        {
            if (owner == null || card == null || !owner.Graveyard.Contains(card))
                return;

            if (card.Definition.Effect == CardEffect.DandylionTokens)
            {
                if (owner.SummonsLockedThisTurn)
                    return;
                CardDefinition tokenDefinition = StarterCatalog.Find("15341822");
                if (tokenDefinition == null)
                {
                    Log("Dandylion could not create Fluff Tokens because their Token definition is missing.");
                    return;
                }

                for (int i = 0; i < 2; i++)
                {
                    int zone = FindEmptyMonsterZone(owner);
                    if (zone < 0)
                        break;
                    CardInstance token = new CardInstance(tokenDefinition)
                    {
                        CannotBeTributedForTributeSummonUntilTurn = turnNumber
                    };
                    PlaceSpecialSummon(owner, token, zone, false);
                }
                return;
            }

            if (sentFromField && card.Definition.Effect == CardEffect.WitchOfTheBlackForestSearch &&
                !owner.WitchEffectUsedThisTurn)
            {
                owner.WitchEffectUsedThisTurn = true;
                BeginDeckChoice(owner, card,
                    candidate => candidate.Definition.IsMonster &&
                                candidate.Definition.CanBeMainDeck &&
                                candidate.Definition.Defense <= 1500,
                    false, "Choose a monster with 1500 or less DEF. Its effects cannot be activated this turn.",
                    true);
                return;
            }

            if (sentFromField && destroyedByBattle &&
                card.Definition.Effect == CardEffect.TroopDragonSummon)
            {
                if (owner.SummonsLockedThisTurn)
                    return;
                if (!owner.Deck.Any(candidate =>
                        candidate.Definition.CardId == card.Definition.CardId))
                {
                    Log(owner.Name + "'s Troop Dragon was destroyed by battle, but no copy remains in the Deck.");
                    return;
                }
                BeginDeckChoice(owner, card,
                    candidate => candidate.Definition.CardId == card.Definition.CardId,
                    false, "Choose a Troop Dragon to Special Summon from your Deck.",
                    specialSummon: true);
            }
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
