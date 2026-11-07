using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DuelPrototype
{
    public sealed class StructureDeckPreset
    {
        public string Name { get; }
        public string Theme { get; }
        public IReadOnlyList<CardDefinition> MainDeck { get; }
        public IReadOnlyList<CardDefinition> ExtraDeck { get; }

        public StructureDeckPreset(string name, string theme, IEnumerable<CardDefinition> mainDeck, IEnumerable<CardDefinition> extraDeck)
        {
            Name = name;
            Theme = theme;
            MainDeck = mainDeck.ToList();
            ExtraDeck = extraDeck.ToList();
        }
    }

    public static class StarterCatalog
    {
        private static readonly List<CardDefinition> cards = new List<CardDefinition>();
        private static bool initialized;
        private static List<StructureDeckPreset> structureDecks;
        private static int importedCardCount;

        public static IReadOnlyList<CardDefinition> All
        {
            get
            {
                EnsureInitialized();
                return cards;
            }
        }

        public static CardDefinition Find(string id)
        {
            EnsureInitialized();
            return cards.FirstOrDefault(card => card.CardId == id);
        }

        public static int ImportedCardCount
        {
            get
            {
                EnsureInitialized();
                return importedCardCount;
            }
        }

        public static List<CardDefinition> CreateStarterDeck()
        {
            EnsureInitialized();
            return structureDecks[0].MainDeck.ToList();
        }

        public static List<CardDefinition> CreateAiDeck()
        {
            EnsureInitialized();
            return structureDecks[1].MainDeck.ToList();
        }

        public static IReadOnlyList<StructureDeckPreset> StructureDecks
        {
            get
            {
                EnsureInitialized();
                if (structureDecks == null)
                    BuildStructureDecks();
                return structureDecks;
            }
        }

        private static void EnsureInitialized()
        {
            if (initialized)
                return;

            TextAsset catalogFile = Resources.Load<TextAsset>("all_ygo_cards");
            if (catalogFile == null)
                throw new InvalidOperationException("The card catalog resource 'Resources/all_ygo_cards.csv' could not be loaded.");

            List<CardDefinition> imported = CardCatalogCsvImporter.Import(catalogFile.text);
            cards.AddRange(imported);
            importedCardCount = imported.Count;
            foreach (CardDefinition customCard in Resources.LoadAll<CardDefinition>("CardCatalog"))
            {
                if (customCard == null || string.IsNullOrWhiteSpace(customCard.CardId) ||
                    cards.Any(card => card.CardId == customCard.CardId))
                    continue;

                cards.Add(customCard);
            }

            initialized = true;
            BuildStructureDecks();
        }

        private static void BuildStructureDecks()
        {
            structureDecks = new List<StructureDeckPreset>
            {
                CreatePreset(
                    "Junk Synchron",
                    "Build Level 5-10 Synchro lines with Junk and Synchron monsters.",
                    new[]
                    {
                        "Junk Synchron", "Quillbolt Hedgehog", "Doppelwarrior", "Junk Converter",
                        "Synchron Explorer", "Level Eater", "Road Synchron", "Turbo Synchron",
                        "Quickdraw Synchron", "Sangan", "Debris Dragon", "Tuningware",
                        "Tuning", "Reinforcement of the Army", "Monster Reborn", "Foolish Burial",
                        "Mystical Space Typhoon", "Mirror Force", "Starlight Road", "Scrap-Iron Scarecrow"
                    },
                    new[]
                    {
                        "Junk Warrior", "Stardust Dragon", "Nitro Warrior", "Drill Warrior",
                        "Formula Synchron", "Shooting Star Dragon", "Colossal Fighter", "Black Rose Dragon"
                    }),
                CreatePreset(
                    "Blackwing",
                    "Swarm with Blackwing monsters and climb into Winged Beast Synchro Monsters.",
                    new[]
                    {
                        "Blackwing - Blizzard the Far North", "Blackwing - Bora the Spear",
                        "Blackwing - Gale the Whirlwind", "Blackwing - Kalut the Moon Shadow",
                        "Blackwing - Shura the Blue Flame", "Blackwing - Sirocco the Dawn",
                        "Blackwing - Vayu the Emblem of Honor", "Blackwing - Mistral the Silver Shield",
                        "Blackwing - Zephyros the Elite", "Sangan", "Dekoichi the Battlechanted Locomotive",
                        "Jinzo", "Black Whirlwind", "Mystical Space Typhoon", "Dark Hole",
                        "Book of Moon", "Monster Reborn", "Icarus Attack", "Delta Crow - Anti Reverse",
                        "Torrential Tribute"
                    },
                    new[]
                    {
                        "Blackwing Armed Wing", "Blackwing Armor Master",
                        "Blackwing - Silverwind the Ascendant", "Black Rose Dragon",
                        "Red Dragon Archfiend", "Stardust Dragon", "Colossal Fighter", "Goyo Guardian"
                    }),
                CreatePreset(
                    "Stardust Synchron",
                    "Protect the field with Stardust Dragon and extend into powerful Synchro Summons.",
                    new[]
                    {
                        "Junk Synchron", "Doppelwarrior", "Junk Converter", "Synchron Explorer",
                        "Quickdraw Synchron", "Road Synchron", "Turbo Synchron", "Stardust Xiaolong",
                        "Majestic Dragon", "Debris Dragon", "Tuningware", "Cyber Dragon",
                        "Tuning", "One for One", "Foolish Burial", "Monster Reborn",
                        "Scapegoat", "Starlight Road", "Scrap-Iron Scarecrow", "Solemn Judgment"
                    },
                    new[]
                    {
                        "Stardust Dragon", "Shooting Star Dragon", "Majestic Star Dragon",
                        "Junk Warrior", "Formula Synchron", "Drill Warrior", "Power Tool Dragon",
                        "Life Stream Dragon"
                    }),
                CreatePreset(
                    "Morphtronic Power Tool",
                    "Use Morphtronic monsters and Equip Spells to make Power Tool Dragon.",
                    new[]
                    {
                        "Morphtronic Celfon", "Morphtronic Remoten", "Morphtronic Boarden",
                        "Dekoichi the Battlechanted Locomotive", "Cyber Dragon", "Jinzo",
                        "Tuningware", "Mystical Space Typhoon", "Machine Duplication",
                        "Limiter Removal", "Monster Reborn", "Dark Hole", "Book of Moon",
                        "Mirror Force", "Sakuretsu Armor", "Call of the Haunted", "Torrential Tribute",
                        "Pot of Avarice", "One for One", "Sangan"
                    },
                    new[]
                    {
                        "Power Tool Dragon", "Life Stream Dragon", "Ancient Fairy Dragon",
                        "Stardust Dragon", "Junk Warrior", "Colossal Fighter", "Black Rose Dragon",
                        "Red Dragon Archfiend"
                    })
            };
        }

        private static StructureDeckPreset CreatePreset(
            string name,
            string theme,
            IReadOnlyList<string> mainNames,
            IReadOnlyList<string> extraNames)
        {
            if (mainNames.Count != 20)
                throw new InvalidOperationException("Structure deck '" + name + "' must define 20 unique Main Deck cards.");
            if (mainNames.Distinct(StringComparer.Ordinal).Count() != mainNames.Count)
                throw new InvalidOperationException("Structure deck '" + name + "' contains duplicate Main Deck entries.");

            List<CardDefinition> main = mainNames.Select(RequireCard).ToList();
            if (main.Any(card => !card.CanBeMainDeck))
                throw new InvalidOperationException("Structure deck '" + name + "' contains a card that cannot be in the Main Deck.");
            main = main.SelectMany(card => Enumerable.Repeat(card, 2)).ToList();

            List<CardDefinition> extra = extraNames.Select(RequireCard).ToList();
            if (extra.Count > 15 || extra.Any(card => !card.CanBeExtraDeck))
                throw new InvalidOperationException("Structure deck '" + name + "' has an invalid Extra Deck.");
            if (main.GroupBy(card => card.CardId).Any(group => group.Count() > 3) ||
                extra.GroupBy(card => card.CardId).Any(group => group.Count() > 3))
                throw new InvalidOperationException("Structure deck '" + name + "' exceeds the three-copy limit.");

            return new StructureDeckPreset(name, theme, main, extra);
        }

        private static CardDefinition RequireCard(string name)
        {
            CardDefinition definition = cards.FirstOrDefault(card =>
                string.Equals(card.DisplayName, name, StringComparison.Ordinal));
            if (definition == null)
                throw new InvalidOperationException("Required card '" + name + "' is missing from the imported card catalog.");
            return definition;
        }
    }
}
