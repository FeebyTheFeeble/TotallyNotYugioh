using System;
using System.Linq;

namespace DuelPrototype
{
    internal static class DuelAiEvaluator
    {
        private const double HandCardValue = 280;

        public static double Evaluate(DuelPlayer player, DuelPlayer opponent)
        {
            return (player.LifePoints - opponent.LifePoints) * 0.02 +
                   (player.Hand.Count - opponent.Hand.Count) * HandCardValue +
                   BoardValue(player, opponent) - BoardValue(opponent, player);
        }

        public static double CardValue(CardInstance card)
        {
            if (card == null)
                return 0;
            if (card.Definition.IsMonster)
                return card.CurrentAttack * 0.18 + card.Definition.Defense * 0.08 +
                       (card.Definition.IsTuner ? 35 : 0) +
                       (card.Definition.IsEffectMonster ? 30 : 0);
            return 100;
        }

        private static double BoardValue(DuelPlayer player, DuelPlayer opponent)
        {
            double value = 0;
            int strongestOpponentAttack = opponent.MonsterZones
                .Where(card => card != null && card.FaceUp && card.AttackPosition)
                .Select(card => card.CurrentAttack)
                .DefaultIfEmpty(0)
                .Max();

            foreach (CardInstance monster in player.MonsterZones.Where(card => card != null))
            {
                if (monster.FaceUp)
                {
                    value += CardValue(monster) + 100;
                    if (monster.AttackPosition && strongestOpponentAttack > monster.CurrentAttack)
                        value -= (strongestOpponentAttack - monster.CurrentAttack) * 0.4;
                }
                else
                {
                    value += monster.Definition.Defense * 0.1 + 60;
                }

                if (monster.CannotBeDestroyedByCardEffects)
                    value += 175;
            }

            value += player.SpellTrapZones.Count(card => card != null) * 85;
            if (player.FieldSpellZone != null)
                value += 100;
            return value;
        }
    }
}
