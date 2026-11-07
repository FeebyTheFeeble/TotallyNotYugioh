using UnityEngine;

namespace DuelPrototype
{
    public enum CardKind
    {
        Monster,
        Spell,
        Trap,
        Synchro,
        Fusion,
        Xyz,
        Link,
        Token,
        Skill
    }

    public enum CardSubtype
    {
        Normal,
        QuickPlay,
        Continuous,
        Equip,
        Field,
        Ritual,
        Counter
    }

    public enum CardEffect
    {
        None,
        DrawOne,
        DrawTwo,
        BurnOpponent,
        RestoreLife,
        BoostStrongest,
        DestroyWeakest,
        ReviveWeakest,
        StopAttack,
        CounterSpell,
        CounterMonsterEffect,
        CounterTrap,
        DarkMagicAttack,
        DarkMagicCurtain,
        DarkMagicInheritance,
        MysticBox,
        SpellShatteringArrow,
        SwordsOfRevealingLight,
        SwordsOfConcealingLight,
        FusionSummon,
        DeFusion,
        MagnetConversion,
        RockBombardment,
        SearchMagnet,
        Berserkion,
        Valkyrion,
        KnightsCombo,
        AttackTheMoon,
        DestroySpellTrap,
        SanganSearch
    }

    [CreateAssetMenu(menuName = "Duel Prototype/Card", fileName = "NewCard")]
    public sealed class CardDefinition : ScriptableObject
    {
        [SerializeField] private string cardId;
        [SerializeField] private string displayName;
        [SerializeField] private CardKind kind;
        [SerializeField] private CardSubtype subtype;
        [SerializeField, Range(0, 13)] private int level = 4;
        [SerializeField] private int attack;
        [SerializeField] private int defense;
        [SerializeField] private string attackDisplay;
        [SerializeField] private string defenseDisplay;
        [SerializeField] private string attribute;
        [SerializeField] private string monsterType;
        [SerializeField] private string frameType;
        [SerializeField] private string cardSets;
        [SerializeField] private bool tuner;
        [SerializeField] private bool effectMonster;
        [SerializeField] private CardEffect effect;
        [SerializeField] private int effectValue;
        [SerializeField, TextArea(2, 5)] private string rulesSummary;
        [SerializeField, Range(1, 10)] private int packWeight = 8;

        public string CardId => cardId;
        public string DisplayName => displayName;
        public CardKind Kind => kind;
        public CardSubtype Subtype => subtype;
        public int Level => level;
        public int Attack => attack;
        public int Defense => defense;
        public string AttackDisplay => string.IsNullOrEmpty(attackDisplay) ? attack.ToString() : attackDisplay;
        public string DefenseDisplay => string.IsNullOrEmpty(defenseDisplay) ? defense.ToString() : defenseDisplay;
        public string Attribute => attribute;
        public string MonsterType => monsterType;
        public string FrameType => frameType;
        public string CardSets => cardSets;
        public bool IsTuner => tuner;
        public bool IsEffectMonster => IsMonster && kind != CardKind.Token &&
            (effectMonster || effect != CardEffect.None);
        public CardEffect Effect => effect;
        public int EffectValue => effectValue;
        public string RulesSummary => rulesSummary;
        public int PackWeight => packWeight;
        public bool IsExtraDeckCard => kind == CardKind.Synchro || kind == CardKind.Fusion ||
            kind == CardKind.Xyz || kind == CardKind.Link;
        public bool IsMonster => kind == CardKind.Monster || kind == CardKind.Synchro ||
            kind == CardKind.Fusion || kind == CardKind.Xyz || kind == CardKind.Link ||
            kind == CardKind.Token;
        public bool CanNormalSummon => IsMonster && !IsExtraDeckCard && kind != CardKind.Token &&
            !string.Equals(frameType, "ritual", System.StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(frameType, "ritual_pendulum", System.StringComparison.OrdinalIgnoreCase);
        public bool CanBeMainDeck => !IsExtraDeckCard && kind != CardKind.Token && kind != CardKind.Skill;
        public bool CanBeExtraDeck => IsExtraDeckCard;

        public void Configure(
            string id,
            string name,
            CardKind cardKind,
            int cardLevel,
            int cardAttack,
            int cardDefense,
            bool isTuner,
            CardEffect cardEffect,
            int value,
            string summary,
            int weight = 8,
            bool isEffectMonster = false,
            CardSubtype cardSubtype = CardSubtype.Normal,
            string cardAttribute = "",
            string cardMonsterType = "",
            string cardFrameType = "",
            string cardAttackDisplay = null,
            string cardDefenseDisplay = null,
            string cardSetNames = "")
        {
            cardId = id;
            displayName = name;
            kind = cardKind;
            subtype = cardSubtype;
            level = Mathf.Clamp(cardLevel, 0, 13);
            attack = Mathf.Max(0, cardAttack);
            defense = Mathf.Max(0, cardDefense);
            attackDisplay = cardAttackDisplay;
            defenseDisplay = cardDefenseDisplay;
            attribute = cardAttribute ?? "";
            monsterType = cardMonsterType ?? "";
            frameType = cardFrameType ?? "";
            cardSets = cardSetNames ?? "";
            tuner = IsMonster && isTuner;
            effectMonster = IsMonster && (isEffectMonster || cardEffect != CardEffect.None);
            effect = cardEffect;
            effectValue = Mathf.Max(0, value);
            rulesSummary = summary;
            packWeight = Mathf.Clamp(weight, 1, 10);
        }

        private void OnValidate()
        {
            level = Mathf.Clamp(level, 0, 13);
            attack = Mathf.Max(0, attack);
            defense = Mathf.Max(0, defense);
            effectValue = Mathf.Max(0, effectValue);
            packWeight = Mathf.Clamp(packWeight, 1, 10);
            if (!IsMonster)
            {
                tuner = false;
                effectMonster = false;
            }
            if (kind != CardKind.Spell && kind != CardKind.Trap)
                subtype = CardSubtype.Normal;
        }
    }
}
