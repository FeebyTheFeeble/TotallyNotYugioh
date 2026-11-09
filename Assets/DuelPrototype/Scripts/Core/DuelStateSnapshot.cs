using System;

namespace DuelPrototype
{
    [Serializable]
    public sealed class DuelStateSnapshot
    {
        public int schemaVersion = 1;
        public int turnNumber;
        public string phase;
        public string currentPlayer;
        public bool isAiVsAi;
        public string winner;
        public string temporarilyDisabledCardOwner;
        public string temporarilyDisabledCardName;
        public int temporarilyDisabledCardTurn;
        public DuelPlayerStateSnapshot human;
        public DuelPlayerStateSnapshot opponent;
        public DuelCardStateSnapshot[] cards = Array.Empty<DuelCardStateSnapshot>();
        public string[] messages = Array.Empty<string>();
    }

    [Serializable]
    public sealed class DuelPlayerStateSnapshot
    {
        public string name;
        public bool isAi;
        public int lifePoints;
        public string[] deck = Array.Empty<string>();
        public string[] hand = Array.Empty<string>();
        public string[] graveyard = Array.Empty<string>();
        public string[] banished = Array.Empty<string>();
        public string[] monsterZones = new string[5];
        public string[] spellTrapZones = new string[5];
        public string fieldSpellZone;
        public string[] extraDeck = Array.Empty<string>();
        public int normalSummonsThisTurn;
        public bool positionChangedThisTurn;
        public bool summonsLockedThisTurn;
        public bool darkMagicCurtainUsedThisTurn;
        public bool attackTheMoonUsedThisTurn;
        public bool sanganEffectUsedThisTurn;
        public bool witchEffectUsedThisTurn;
        public int battleDamageProtectedTurn;
        public int cannotAttackUntilTurn;
        public int cannotChangePositionUntilTurn;
        public int noAttackTurnsRemaining;
        public int positionChangesLockedTurnsRemaining;
    }

    [Serializable]
    public sealed class DuelCardStateSnapshot
    {
        public string instanceId;
        public string cardId;
        public bool faceUp;
        public bool attackPosition;
        public bool hasAttacked;
        public bool hasChangedPositionThisTurn;
        public bool effectUsedThisTurn;
        public bool effectUsedOncePerDuel;
        public bool stardustReturnPending;
        public bool effectsNegated;
        public bool cannotBeDestroyedByCardEffects;
        public int effectsNegatedUntilTurn;
        public int wedgeCounters;
        public int extraAttacksThisTurn;
        public int levelModifier;
        public int drillWarriorReturnTurn;
        public int defenseModifier;
        public bool drillWarriorDirectAttackThisTurn;
        public bool nitroWarriorBoostReady;
        public string nitroWarriorSecondAttackTargetId;
        public int attackBonus;
        public int permanentAttackBonus;
        public int summonedTurnNumber;
        public int spellCounters;
        public int destroyedOnFieldTurn;
        public int destroyAtEndPhaseTurn;
        public int effectDurationTurnsRemaining;
        public bool banishWhenLeavingField;
        public bool cannotBeTributedForTributeSummon;
        public int cannotBeTributedForTributeSummonUntilTurn;
        public string[] fusionMaterialIds = Array.Empty<string>();
        public string equippedMonsterId;
    }
}
