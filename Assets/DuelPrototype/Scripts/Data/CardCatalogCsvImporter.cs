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
        private static readonly Dictionary<string, CardEffect> ImportedEffects =
            new Dictionary<string, CardEffect>(StringComparer.Ordinal)
            {
                { "5318639", CardEffect.DestroySpellTrap },
                { "26202165", CardEffect.SanganSearch }
            };

        private static readonly HashSet<string> TunerCardIds = new HashSet<string>(StringComparer.Ordinal)
        {
            "63977008", "71971554", "67270095", "20932152", "50091196",
            "14943837", "22835145", "2009101", "92676637", "21159309",
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
                    ImportedEffects.TryGetValue(id, out CardEffect effect) ? effect : CardEffect.None,
                    0,
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
                    GetValue(record, columns, "card_sets").Trim());
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
