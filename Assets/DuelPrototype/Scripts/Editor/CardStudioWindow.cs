using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DuelPrototype.Editor
{
    public sealed class CardStudioWindow : EditorWindow
    {
        private string cardId = "new-card";
        private string cardName = "New Card";
        private CardKind kind = CardKind.Monster;
        private int level = 4;
        private int attack = 1000;
        private int defense = 1000;
        private bool tuner;
        private bool effectMonster = true;
        private CardSubtype subtype = CardSubtype.Normal;
        private CardEffect effect;
        private int effectValue = 500;
        private int packWeight = 8;
        private string rulesSummary = "Describe this card's effect.";
        private string status;

        [MenuItem("Tools/Duel Prototype/Card Studio")]
        private static void Open()
        {
            GetWindow<CardStudioWindow>("Card Studio");
        }

        [MenuItem("Tools/Duel Prototype/Validate Structure Decks")]
        public static void ValidateStructureDecks()
        {
            foreach (StructureDeckPreset deck in StarterCatalog.StructureDecks)
            {
                if (deck.MainDeck.Count < 40 || deck.MainDeck.Count > 60)
                    throw new System.InvalidOperationException(deck.Name + " must contain between 40 and 60 Main Deck cards.");
                if (deck.ExtraDeck.Count > 15)
                    throw new System.InvalidOperationException(deck.Name + " exceeds the 15-card Extra Deck limit.");
                if (deck.MainDeck.GroupBy(card => card.CardId).Any(group => group.Count() > 3) ||
                    deck.ExtraDeck.GroupBy(card => card.CardId).Any(group => group.Count() > 3))
                    throw new System.InvalidOperationException(deck.Name + " exceeds the three-copy limit.");

                Debug.Log(deck.Name + ": " + deck.MainDeck.Count + " Main Deck cards, " +
                          deck.ExtraDeck.Count + " Extra Deck cards. Valid.");
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Card Studio", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Create data-driven cards for Resources/CardCatalog. Use unique, lowercase IDs.", MessageType.Info);
            cardId = EditorGUILayout.TextField("Card ID", cardId);
            cardName = EditorGUILayout.TextField("Display Name", cardName);
            kind = (CardKind)EditorGUILayout.EnumPopup("Card Kind", kind);
            if (kind == CardKind.Monster || kind == CardKind.Synchro || kind == CardKind.Fusion ||
                kind == CardKind.Xyz || kind == CardKind.Link)
            {
                level = EditorGUILayout.IntSlider("Level", level, 1, 12);
                attack = EditorGUILayout.IntField("ATK", attack);
                defense = EditorGUILayout.IntField("DEF", defense);
                if (kind == CardKind.Monster)
                {
                    effectMonster = EditorGUILayout.Toggle("Effect Monster", effectMonster);
                    tuner = EditorGUILayout.Toggle("Tuner", tuner);
                }
            }
            if (kind == CardKind.Spell || kind == CardKind.Trap)
                subtype = (CardSubtype)EditorGUILayout.EnumPopup("Subtype", subtype);
            effect = (CardEffect)EditorGUILayout.EnumPopup("Effect", effect);
            effectValue = EditorGUILayout.IntField("Effect Value", effectValue);
            packWeight = EditorGUILayout.IntSlider("Pack Weight", packWeight, 1, 10);
            rulesSummary = EditorGUILayout.TextArea(rulesSummary, GUILayout.MinHeight(52));

            if (GUILayout.Button("Create Card Asset"))
                CreateCardAsset();
            if (!string.IsNullOrEmpty(status))
                EditorGUILayout.HelpBox(status, status.StartsWith("Created") ? MessageType.Info : MessageType.Warning);
        }

        private void CreateCardAsset()
        {
            cardId = (cardId ?? "").Trim().ToLowerInvariant();
            cardName = (cardName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(cardId) || string.IsNullOrWhiteSpace(cardName))
            {
                status = "A card ID and display name are required.";
                return;
            }

            const string folder = "Assets/Resources/CardCatalog";
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder("Assets/Resources", "CardCatalog");

            List<CardDefinition> existing = AssetDatabase.FindAssets("t:CardDefinition")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<CardDefinition>)
                .Where(card => card != null)
                .ToList();
            if (existing.Any(card => card.CardId == cardId) || StarterCatalog.All.Any(card => card.CardId == cardId))
            {
                status = "A card with that ID already exists. IDs must be unique.";
                return;
            }

            CardDefinition asset = CreateInstance<CardDefinition>();
            asset.Configure(cardId, cardName, kind, level, attack, defense, tuner, effect,
                effectValue, rulesSummary, packWeight, effectMonster, subtype);
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + cardId + ".asset");
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
            status = "Created " + path + ". It will load into the catalog when the next duel starts.";
        }
    }
}
