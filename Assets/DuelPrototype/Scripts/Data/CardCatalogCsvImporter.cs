using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace DuelPrototype
{
    public static class CardCatalogCsvImporter
    {
        private struct ImportedEffect
        {
            public CardEffect Effect;
            public int Value;
            public string TargetMonsterType;
            public int TargetMaxLevel;
            public int TargetMaxAttack;

            public ImportedEffect(
                CardEffect effect,
                int value = 0,
                string targetMonsterType = "",
                int targetMaxLevel = 0,
                int targetMaxAttack = 0)
            {
                Effect = effect;
                Value = value;
                TargetMonsterType = targetMonsterType;
                TargetMaxLevel = targetMaxLevel;
                TargetMaxAttack = targetMaxAttack;
            }
        }

        private static readonly Dictionary<string, ImportedEffect> ImportedEffects =
            new Dictionary<string, ImportedEffect>(StringComparer.Ordinal)
            {
                { "5318639", new ImportedEffect(CardEffect.DestroySpellTrap) },
                { "19613556", new ImportedEffect(CardEffect.DestroyAllSpellTraps) },
                { "18144507", new ImportedEffect(CardEffect.DestroyOpponentSpellTraps) },
                { "26202165", new ImportedEffect(CardEffect.SanganSearch, targetMaxAttack: 1500) },
                { "83968380", new ImportedEffect(CardEffect.DrawOne) },
                { "55144522", new ImportedEffect(CardEffect.DrawTwo) },
                { "32807846", new ImportedEffect(CardEffect.SearchDeckToHand,
                    targetMonsterType: "Warrior", targetMaxLevel: 4) },
                { "81439173", new ImportedEffect(CardEffect.SendMonsterFromDeckToGraveyard) },
                { "72892473", new ImportedEffect(CardEffect.DiscardDrawSameCount) },
                { "79571449", new ImportedEffect(CardEffect.DrawThreeDiscardTwo) },
                { "69162969", new ImportedEffect(CardEffect.DiscardOneDestroyFaceUpOpponentMonsters) },
                { "53129443", new ImportedEffect(CardEffect.DestroyAllMonsters) },
                { "12580477", new ImportedEffect(CardEffect.DestroyAllOpponentMonsters) },
                { "97169186", new ImportedEffect(CardEffect.DestroyHighestDefenseMonster) },
                { "42703248", new ImportedEffect(CardEffect.ReturnAllSpellTrapsToHand) },
                { "83764719", new ImportedEffect(CardEffect.ReviveMonsterFromGraveyard) }
                ,{ "34124316", new ImportedEffect(CardEffect.CyberJar) }
                ,{ "71044499", new ImportedEffect(CardEffect.NoblemanOfCrossout) }
                ,{ "25862681", new ImportedEffect(CardEffect.AncientFairyDragon) }
                ,{ "73580471", new ImportedEffect(CardEffect.BlackRoseDragon) }
                ,{ "91351370", new ImportedEffect(CardEffect.BlackWhirlwindSearch) }
                ,{ "22835145", new ImportedEffect(CardEffect.BlackwingRevive) }
                ,{ "49003716", new ImportedEffect(CardEffect.BlackwingSpecialSummon) }
                ,{ "2009101", new ImportedEffect(CardEffect.BlackwingHalveMonster) }
                ,{ "85215458", new ImportedEffect(CardEffect.BlackwingBattleBoost, 1400) }
                ,{ "46710683", new ImportedEffect(CardEffect.BlackwingMistral) }
                ,{ "58820853", new ImportedEffect(CardEffect.BlackwingShura) }
                ,{ "33236860", new ImportedEffect(CardEffect.BlackwingSilverwind) }
                ,{ "75498415", new ImportedEffect(CardEffect.BlackwingSiroccoBoost) }
                ,{ "72714392", new ImportedEffect(CardEffect.BlackwingVayuSummon) }
                ,{ "14785765", new ImportedEffect(CardEffect.ZephyrosRevive) }
                ,{ "76913983", new ImportedEffect(CardEffect.BlackwingArmedWing) }
                ,{ "69031175", new ImportedEffect(CardEffect.BlackwingArmorMaster) }
                ,{ "14087893", new ImportedEffect(CardEffect.BookOfMoon) }
                ,{ "97077563", new ImportedEffect(CardEffect.CallOfTheHaunted) }
                ,{ "23693634", new ImportedEffect(CardEffect.ColossalFighterRevive) }
                ,{ "70095154", new ImportedEffect(CardEffect.CyberDragonSummon) }
                ,{ "14943837", new ImportedEffect(CardEffect.DebrisDragonRevive) }
                ,{ "87621407", new ImportedEffect(CardEffect.DekoichiDraw) }
                ,{ "15341821", new ImportedEffect(CardEffect.DandylionTokens) }
                ,{ "59839761", new ImportedEffect(CardEffect.DestroySetOpponentBackrow) }
                ,{ "53855409", new ImportedEffect(CardEffect.Doppelwarrior) }
                ,{ "3429238", new ImportedEffect(CardEffect.DrillWarrior) }
                ,{ "50091196", new ImportedEffect(CardEffect.FormulaSynchronDraw) }
                ,{ "58518520", new ImportedEffect(CardEffect.ClearEffector) }
                ,{ "90953320", new ImportedEffect(CardEffect.TGHyperLibrarian) }
                ,{ "55013285", new ImportedEffect(CardEffect.TroopDragonSummon) }
                ,{ "78010363", new ImportedEffect(CardEffect.WitchOfTheBlackForestSearch) }
                ,{ "7391448", new ImportedEffect(CardEffect.GoyoGuardianRevive) }
                ,{ "53567095", new ImportedEffect(CardEffect.IcarusAttack) }
                ,{ "77585513", new ImportedEffect(CardEffect.JinzoTrapLock) }
                ,{ "11069680", new ImportedEffect(CardEffect.JunkConverter) }
                ,{ "63977008", new ImportedEffect(CardEffect.JunkSynchronRevive) }
                ,{ "60800381", new ImportedEffect(CardEffect.JunkWarriorBoost) }
                ,{ "57421866", new ImportedEffect(CardEffect.LevelEater) }
                ,{ "25165047", new ImportedEffect(CardEffect.LifeStreamSetLifePoints) }
                ,{ "23171610", new ImportedEffect(CardEffect.LimiterRemoval) }
                ,{ "63995093", new ImportedEffect(CardEffect.MachineDuplication) }
                ,{ "21159309", new ImportedEffect(CardEffect.MajesticDragon) }
                ,{ "7841112", new ImportedEffect(CardEffect.MajesticStarDragon) }
                ,{ "48381268", new ImportedEffect(CardEffect.MorphtronicBoarden) }
                ,{ "93542102", new ImportedEffect(CardEffect.MorphtronicCelfon) }
                ,{ "57108202", new ImportedEffect(CardEffect.MorphtronicRemoten) }
                ,{ "18013090", new ImportedEffect(CardEffect.NitroWarrior) }
                ,{ "2295440", new ImportedEffect(CardEffect.OneForOne) }
                ,{ "67169062", new ImportedEffect(CardEffect.PotOfAvarice) }
                ,{ "2403771", new ImportedEffect(CardEffect.PowerToolDragon) }
                ,{ "20932152", new ImportedEffect(CardEffect.QuickdrawSynchron) }
                ,{ "23571046", new ImportedEffect(CardEffect.QuillboltHedgehog) }
                ,{ "70902743", new ImportedEffect(CardEffect.RedDragonArchfiend) }
                ,{ "71971554", new ImportedEffect(CardEffect.RoadSynchron) }
                ,{ "56120475", new ImportedEffect(CardEffect.SakuretsuArmor) }
                ,{ "73915051", new ImportedEffect(CardEffect.Scapegoat) }
                ,{ "98427577", new ImportedEffect(CardEffect.ScrapIronScarecrow) }
                ,{ "24696097", new ImportedEffect(CardEffect.ShootingStarDragon) }
                ,{ "41420027", new ImportedEffect(CardEffect.SolemnJudgment) }
                ,{ "44508094", new ImportedEffect(CardEffect.StardustDragon) }
                ,{ "68543408", new ImportedEffect(CardEffect.StardustXiaolong) }
                ,{ "58120309", new ImportedEffect(CardEffect.StarlightRoad) }
                ,{ "36643046", new ImportedEffect(CardEffect.SynchronExplorer) }
                ,{ "53582587", new ImportedEffect(CardEffect.TorrentialTribute) }
                ,{ "96363153", new ImportedEffect(CardEffect.Tuning) }
                ,{ "92676637", new ImportedEffect(CardEffect.TuningwareDraw) }
                ,{ "67270095", new ImportedEffect(CardEffect.TurboSynchron) }
                ,{ "44095762", new ImportedEffect(CardEffect.MirrorForce) }
                ,{ "70342110", new ImportedEffect(CardEffect.DimensionalPrison) }
                ,{ "94192409", new ImportedEffect(CardEffect.CompulsoryEvacuationDevice) }
                ,{ "29401950", new ImportedEffect(CardEffect.BottomlessTrapHole) }
                ,{ "77414722", new ImportedEffect(CardEffect.CounterSpell) }
                ,{ "49010598", new ImportedEffect(CardEffect.CounterMonsterEffect) }
                ,{ "14315573", new ImportedEffect(CardEffect.StopAttack) }
                ,{ "60082869", new ImportedEffect(CardEffect.DestroySpellTrap) }
                ,{ "4178474", new ImportedEffect(CardEffect.RaigekiBreak) }
                ,{ "70231910", new ImportedEffect(CardEffect.DarkCoreBanish) }
            };

        private static readonly HashSet<string> TunerCardIds = new HashSet<string>(StringComparer.Ordinal)
        {
            "63977008", "71971554", "67270095", "20932152", "50091196",
            "14943837", "22835145", "2009101", "21159309",
            "40975574", "97021916", "46710683"
        };

        public static List<CardDefinition> Import(string csv)
        {
            List<string[]> records = ParseRecords(csv);
            if (records.Count < 2)
                throw new FormatException("The card catalog CSV must include a header and at least one card.");

            Dictionary<string, int> columns = records[0]
                .Select((name, index) => new { Name = name.Trim(), Index = index })
                .ToDictionary(column => column.Name, column => column.Index, StringComparer.OrdinalIgnoreCase);
            string[] requiredColumns =
            {
                "id", "name", "desc", "atk", "defe", "attribute", "type", "frametype", "level", "card_sets"
            };
            foreach (string column in requiredColumns)
            {
                if (!columns.ContainsKey(column))
                    throw new FormatException("The card catalog CSV is missing the required column '" + column + "'.");
            }

            List<CardDefinition> imported = new List<CardDefinition>(records.Count - 1);
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            for (int recordIndex = 1; recordIndex < records.Count; recordIndex++)
            {
                string[] record = records[recordIndex];
                if (record.Length != records[0].Length)
                    throw new FormatException("Card catalog record " + (recordIndex + 1) + " has " + record.Length +
                        " fields; expected " + records[0].Length + ".");

                string id = GetValue(record, columns, "id").Trim();
                string name = GetValue(record, columns, "name").Trim();
                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name))
                    throw new FormatException("Card catalog record " + (recordIndex + 1) + " is missing its ID or name.");
                if (!ids.Add(id))
                    throw new FormatException("Card catalog contains duplicate card ID '" + id + "'.");

                string frameType = GetValue(record, columns, "frametype").Trim().ToLowerInvariant();
                string cardType = GetValue(record, columns, "type").Trim();
                CardKind kind = GetKind(frameType);
                CardSubtype subtype = GetSubtype(kind, cardType);
                int attack = ParseNumber(GetValue(record, columns, "atk"));
                int defense = ParseNumber(GetValue(record, columns, "defe"));
                int level = ParseNumber(GetValue(record, columns, "level"));
                bool hasImportedEffect = ImportedEffects.TryGetValue(id, out ImportedEffect importedEffect);
                bool isEffectMonster = kind == CardKind.Monster
                    ? frameType != "normal" && frameType != "normal_pendulum"
                    : kind != CardKind.Token && kind != CardKind.Skill;

                CardDefinition definition = ScriptableObject.CreateInstance<CardDefinition>();
                definition.name = name;
                definition.Configure(
                    id,
                    name,
                    kind,
                    level,
                    attack,
                    defense,
                    TunerCardIds.Contains(id),
                    hasImportedEffect ? importedEffect.Effect : CardEffect.None,
                    hasImportedEffect ? importedEffect.Value : 0,
                    GetValue(record, columns, "desc").Trim(),
                    8,
                    isEffectMonster,
                    subtype,
                    GetValue(record, columns, "attribute").Trim(),
                    kind == CardKind.Monster || kind == CardKind.Synchro || kind == CardKind.Fusion ||
                    kind == CardKind.Xyz || kind == CardKind.Link || kind == CardKind.Token ? cardType : "",
                    frameType,
                    GetValue(record, columns, "atk").Trim(),
                    GetValue(record, columns, "defe").Trim(),
                    GetValue(record, columns, "card_sets").Trim(),
                    hasImportedEffect ? importedEffect.TargetMonsterType : "",
                    hasImportedEffect ? importedEffect.TargetMaxLevel : 0,
                    hasImportedEffect ? importedEffect.TargetMaxAttack : 0);
                imported.Add(definition);
            }

            return imported;
        }

        private static CardKind GetKind(string frameType)
        {
            if (frameType == "spell")
                return CardKind.Spell;
            if (frameType == "trap")
                return CardKind.Trap;
            if (frameType.StartsWith("synchro", StringComparison.Ordinal))
                return CardKind.Synchro;
            if (frameType.StartsWith("fusion", StringComparison.Ordinal))
                return CardKind.Fusion;
            if (frameType.StartsWith("xyz", StringComparison.Ordinal))
                return CardKind.Xyz;
            if (frameType.StartsWith("link", StringComparison.Ordinal))
                return CardKind.Link;
            if (frameType == "token")
                return CardKind.Token;
            if (frameType == "skill")
                return CardKind.Skill;
            return CardKind.Monster;
        }

        private static CardSubtype GetSubtype(CardKind kind, string cardType)
        {
            string normalizedType = cardType.Replace("-", "").Replace(" ", "").ToLowerInvariant();
            switch (normalizedType)
            {
                case "quickplay": return CardSubtype.QuickPlay;
                case "continuous": return CardSubtype.Continuous;
                case "equip": return CardSubtype.Equip;
                case "field": return CardSubtype.Field;
                case "ritual": return CardSubtype.Ritual;
                case "counter": return CardSubtype.Counter;
                default: return CardSubtype.Normal;
            }
        }

        private static int ParseNumber(string value)
        {
            return int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
                ? Math.Max(0, result)
                : 0;
        }

        private static string GetValue(string[] record, IReadOnlyDictionary<string, int> columns, string name)
        {
            return record[columns[name]];
        }

        private static List<string[]> ParseRecords(string csv)
        {
            if (csv == null)
                throw new ArgumentNullException(nameof(csv));

            List<string[]> records = new List<string[]>();
            List<string> fields = new List<string>();
            StringBuilder field = new StringBuilder();
            bool insideQuotedField = false;

            for (int index = 0; index < csv.Length; index++)
            {
                char character = csv[index];
                if (character == '"')
                {
                    if (insideQuotedField && index + 1 < csv.Length && csv[index + 1] == '"')
                    {
                        field.Append('"');
                        index++;
                    }
                    else
                    {
                        insideQuotedField = !insideQuotedField;
                    }
                }
                else if (character == ',' && !insideQuotedField)
                {
                    fields.Add(field.ToString());
                    field.Clear();
                }
                else if ((character == '\r' || character == '\n') && !insideQuotedField)
                {
                    if (character == '\r' && index + 1 < csv.Length && csv[index + 1] == '\n')
                        index++;
                    fields.Add(field.ToString());
                    field.Clear();
                    if (fields.Any(value => value.Length > 0))
                        records.Add(fields.ToArray());
                    fields.Clear();
                }
                else
                {
                    field.Append(character);
                }
            }

            if (insideQuotedField)
                throw new FormatException("The card catalog CSV ends inside a quoted field.");
            if (field.Length > 0 || fields.Count > 0)
            {
                fields.Add(field.ToString());
                if (fields.Any(value => value.Length > 0))
                    records.Add(fields.ToArray());
            }
            if (records.Count > 0 && records[0].Length > 0)
                records[0][0] = records[0][0].TrimStart('\uFEFF');

            return records;
        }
    }
}
