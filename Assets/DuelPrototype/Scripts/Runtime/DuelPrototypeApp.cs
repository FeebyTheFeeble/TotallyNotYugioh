using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace DuelPrototype
{
    public sealed class WheelScrollRect : ScrollRect
    {
        public override void OnScroll(PointerEventData eventData)
        {
            Vector2 originalScrollDelta = eventData.scrollDelta;
            Vector2 normalizedScrollDelta = originalScrollDelta;
            if (Mathf.Max(Mathf.Abs(normalizedScrollDelta.x), Mathf.Abs(normalizedScrollDelta.y)) > 10f)
                normalizedScrollDelta /= 120f;
            if (horizontal && !vertical && normalizedScrollDelta.x == 0f)
                normalizedScrollDelta = new Vector2(normalizedScrollDelta.y, 0f);

            eventData.scrollDelta = normalizedScrollDelta;
            try
            {
                base.OnScroll(eventData);
            }
            finally
            {
                eventData.scrollDelta = originalScrollDelta;
            }
        }
    }

    public sealed class DuelPrototypeApp : MonoBehaviour
    {
        private enum ScreenMode { Duel, Deck, Cards, Shop, AiLab, TestLab }
        private enum TestLabFilter { None, Kind, Type, Attribute, FrameType, Level, CardSet }
        private enum CardVisualZone { Deck, Hand, Graveyard, Banished, Monster, SpellTrap, FieldSpell, ExtraDeck }
        private enum CardLibraryFilter
        {
            AllCards,
            NormalMonsters,
            EffectMonsters,
            Tuners,
            Synchro,
            Fusion,
            Xyz,
            Link,
            AllSpells,
            NormalSpells,
            QuickPlaySpells,
            ContinuousSpells,
            EquipSpells,
            FieldSpells,
            RitualSpells,
            AllTraps,
            NormalTraps,
            ContinuousTraps,
            CounterTraps
        }

        private sealed class CardVisualState
        {
            public DuelPlayer Owner;
            public CardVisualZone Zone;
            public int Index;
            public int ContainerCount;
            public bool FaceUp;
            public bool EffectUsedThisTurn;
        }

        private sealed class CardMovementAnimation
        {
            public CardInstance Card;
            public Vector2 From;
            public Vector2 To;
            public Vector2 StartSize;
            public Vector2 EndSize;
            public bool ShowPopup;
            public bool StartsFaceUp;
            public bool EndsFaceUp;
        }

        private sealed class AttackAnimation
        {
            public CardInstance Attacker;
            public Vector2 From;
            public Vector2 To;
        }

        [Serializable]
        private sealed class CardCount
        {
            public string id;
            public int count;
        }

        [Serializable]
        private sealed class PlayerProfile
        {
            public int credits;
            public int catalogVersion;
            public List<CardCount> collection = new List<CardCount>();
            public List<string> deck = new List<string>();
        }

        private const string ProfileKey = "DuelPrototype.Profile.v1";
        private const float BoardCardWidth = 0.06f;
        private const float BoardCardStepX = 0.075f;
        private const float OpponentDeckX = 0.70f;
        private const float HandCardHeight = 0.112f;
        private const float HandCardGap = 0.008f;
        private const float HandMaxWidth = 0.68f;
        private static DuelPrototypeApp instance;

        private readonly Color backgroundColor = new Color(0.035f, 0.055f, 0.085f);
        private readonly Color panelColor = new Color(0.075f, 0.105f, 0.15f);
        private readonly Color accentColor = new Color(0.15f, 0.76f, 0.72f);
        private readonly Color mutedColor = new Color(0.42f, 0.5f, 0.6f);
        private readonly Dictionary<ScreenMode, Button> headerTabButtons = new Dictionary<ScreenMode, Button>();
        private readonly Dictionary<ScreenMode, Image> headerTabBorders = new Dictionary<ScreenMode, Image>();

        private PlayerProfile profile;
        private DuelEngine engine;
        private MockDuelSimulator mockDuelSimulator;
        private Canvas canvas;
        private CanvasGroup canvasInputGroup;
        private Text creditsText;
        private RectTransform contentRoot;
        private RectTransform cardPreviewRoot;
        private Image cardPreviewBackground;
        private Image cardPreviewArtImage;
        private Text cardPreviewTitle;
        private Text cardPreviewBody;
        private RectTransform cardLibraryPreviewRoot;
        private Image cardLibraryPreviewBackground;
        private Text cardLibraryPreviewTitle;
        private Text cardLibraryPreviewType;
        private Text cardLibraryPreviewBody;
        private Image cardLibraryArtImage;
        private Sprite cardBackCircleSprite;
        private ScreenMode screen = ScreenMode.Duel;
        private CardInstance selectedHandCard;
        private CardInstance selectedMonsterCard;
        private CardInstance selectedAttacker;
        private CardInstance selectedTarget;
        private DuelPlayer graveyardOwner;
        private CardInstance selectedGraveyardCard;
        private DuelPlayer deckOwner;
        private CardInstance selectedDeckCard;
        private float deckScrollPosition = 1f;
        private float graveyardScrollPosition = 1f;
        private float sanganScrollPosition = 1f;
        private RectTransform deckPreviewRoot;
        private RectTransform graveyardPreviewRoot;
        private RectTransform sanganPreviewRoot;
        private CardDefinition selectedSynchro;
        private readonly HashSet<CardInstance> selectedMaterials = new HashSet<CardInstance>();
        private List<CardDefinition> lastPack = new List<CardDefinition>();
        private string notice = "";
        private Font font;
        private bool matchRewardGranted;
        private int firstAiDeckIndex;
        private int secondAiDeckIndex = 1;
        private int deckPage;
        private string deckSearchQuery = "";
        private int deckSortMode;
        private CardLibraryFilter cardLibraryFilter;
        private bool cardFilterOptionsOpen;
        private int deckLibraryPage;
        private int cardLibraryPage;
        private CardDefinition selectedTestCard;
        private string testCardSearchQuery = "";
        private int testCardPage;
        private TestLabFilter openTestFilter;
        private string testKindFilter = "All";
        private string testTypeFilter = "All";
        private string testAttributeFilter = "All";
        private string testFrameTypeFilter = "All";
        private string testLevelFilter = "All";
        private string testCardSetFilter = "All";
        private string testSetSearchQuery = "";
        private readonly List<RectTransform> contextActionButtons = new List<RectTransform>();
        private readonly Queue<CardMovementAnimation> cardAnimationQueue = new Queue<CardMovementAnimation>();
        private readonly Queue<AttackAnimation> attackAnimationQueue = new Queue<AttackAnimation>();
        private readonly Queue<string> phaseTickerQueue = new Queue<string>();
        private readonly Dictionary<CardInstance, CardVisualState> pendingDrawnCardStates =
            new Dictionary<CardInstance, CardVisualState>();
        private Dictionary<CardInstance, CardVisualState> lastCardVisualStates;
        private DuelEngine observedEngine;
        private Coroutine cardAnimationRoutine;
        private Coroutine attackAnimationRoutine;
        private Coroutine phaseTickerRoutine;
        private CardInstance selectedSanganSearchCard;
        private MockDuelFixtures testFixtures = MockDuelFixtures.OpponentMonster |
            MockDuelFixtures.OpponentSetTrap;
        private const int LibraryPageSize = 80;
        private bool extraDeckOpen;
        private bool packRevealOpen;
        private bool aiHandsOpen;
        private bool aiAutoplay;
        private float nextAiStepTime;
        private readonly float[] aiAutoplayPaces = { 5f, 10f, 15f };
        private int aiAutoplayPaceIndex = 1;
        private float BoardCardHeight => GetPortraitHeight(BoardCardWidth);
        private float MonsterCardStepX
        {
            get
            {
                float screenAspect = Screen.width > 0 && Screen.height > 0
                    ? (float)Screen.width / Screen.height
                    : 16f / 9f;
                return Mathf.Max(BoardCardStepX, BoardCardHeight / screenAspect + 0.006f);
            }
        }
        private float BoardCardStartX => (1f - (BoardCardStepX * 4f + BoardCardWidth)) * 0.5f;
        private float MonsterCardStartX => (1f - (MonsterCardStepX * 4f + BoardCardWidth)) * 0.5f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateWhenSceneLoads()
        {
            if (instance != null)
                return;
            GameObject app = new GameObject("Duel Prototype");
            instance = app.AddComponent<DuelPrototypeApp>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            DontDestroyOnLoad(gameObject);
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            LoadProfile();
            BuildCanvas();
            StartDuel();
            Render();
        }

        private void Update()
        {
            if (screen != ScreenMode.AiLab || !aiAutoplay || engine == null || engine.Phase == DuelPhase.GameOver ||
                Time.unscaledTime < nextAiStepTime)
                return;

            engine.StepAiTurn();
            if (engine.Phase == DuelPhase.GameOver)
                aiAutoplay = false;
            nextAiStepTime = Time.unscaledTime + aiAutoplayPaces[aiAutoplayPaceIndex];
            Render();
        }

        private void LoadProfile()
        {
            string json = PlayerPrefs.GetString(ProfileKey, "");
            profile = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<PlayerProfile>(json);
            if (profile == null || profile.collection == null || profile.deck == null)
            {
                profile = new PlayerProfile { credits = 500, catalogVersion = 3 };
                AddPresetToProfile(StarterCatalog.StructureDecks[0]);
                SaveProfile();
            }
            else if (profile.catalogVersion < 3)
            {
                profile.deck.RemoveAll(id => StarterCatalog.Find(id) == null);
                profile.collection.RemoveAll(item => item == null || StarterCatalog.Find(item.id) == null);
                int mainCount = profile.deck.Select(StarterCatalog.Find)
                    .Count(card => card != null && card.CanBeMainDeck);
                if (mainCount < 20)
                {
                    profile.deck.Clear();
                    AddPresetToProfile(StarterCatalog.StructureDecks[0]);
                }
                profile.catalogVersion = 3;
                SaveProfile();
            }
        }

        private void AddPresetToProfile(StructureDeckPreset preset)
        {
            foreach (CardDefinition card in preset.MainDeck.Concat(preset.ExtraDeck))
            {
                profile.deck.Add(card.CardId);
                AddToCollection(card.CardId, 1);
            }
        }

        private void SaveProfile()
        {
            PlayerPrefs.SetString(ProfileKey, JsonUtility.ToJson(profile));
            PlayerPrefs.Save();
        }

        private void StartDuel()
        {
            matchRewardGranted = false;
            notice = "";
            graveyardOwner = null;
            selectedGraveyardCard = null;
            deckOwner = null;
            extraDeckOpen = false;
            List<CardDefinition> deck = profile.deck
                .Select(StarterCatalog.Find)
                .Where(card => card != null)
                .ToList();
            int mainCount = deck.Count(card => card.CanBeMainDeck);
            if (mainCount < 20)
            {
                notice = "Your Main Deck needs at least 20 cards. Restore it in Deck Builder to start a duel.";
                engine = null;
                return;
            }

            engine = new DuelEngine();
            engine.StartMatch(deck, StarterCatalog.CreateAiDeck());
            ClearSelection();
        }

        private void StartAiMatch()
        {
            graveyardOwner = null;
            selectedGraveyardCard = null;
            deckOwner = null;
            extraDeckOpen = false;
            aiHandsOpen = false;
            IReadOnlyList<StructureDeckPreset> decks = StarterCatalog.StructureDecks;
            StructureDeckPreset first = decks[firstAiDeckIndex];
            StructureDeckPreset second = decks[secondAiDeckIndex];
            engine = new DuelEngine();
            engine.StartMatch(first.MainDeck.Concat(first.ExtraDeck).ToList(),
                second.MainDeck.Concat(second.ExtraDeck).ToList(), true, first.Name, second.Name);
            aiAutoplay = false;
            nextAiStepTime = Time.unscaledTime;
            ClearSelection();
            screen = ScreenMode.AiLab;
            Render();
        }

        private void BuildCanvas()
        {
            GameObject canvasObject = new GameObject("Duel Prototype UI", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasInputGroup = canvasObject.AddComponent<CanvasGroup>();
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.matchWidthOrHeight = 0.5f;

            if (EventSystem.current == null)
            {
                GameObject eventObject = new GameObject("Event System", typeof(EventSystem), typeof(InputSystemUIInputModule));
                eventObject.transform.SetParent(transform, false);
                eventObject.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }

            RectTransform root = canvasObject.GetComponent<RectTransform>();
            AddPanel(root, 0, 0, 1, 1, backgroundColor);
            BuildHeader(root);
            contentRoot = CreateRect("Content", root);
            Stretch(contentRoot);
            BuildCardPreview(root);
        }

        private void BuildCardPreview(RectTransform canvasRoot)
        {
            cardPreviewRoot = CreateRect("Card Preview", canvasRoot);
            cardPreviewRoot.anchorMin = new Vector2(0.5f, 0.5f);
            cardPreviewRoot.anchorMax = new Vector2(0.5f, 0.5f);
            cardPreviewRoot.pivot = new Vector2(0, 1);
            cardPreviewRoot.sizeDelta = new Vector2(340, 476);
            cardPreviewBackground = cardPreviewRoot.gameObject.AddComponent<Image>();
            cardPreviewBackground.color = new Color(0.035f, 0.055f, 0.085f, 1f);
            cardPreviewBackground.raycastTarget = false;

            RectTransform border = CreateRect("Accent", cardPreviewRoot);
            SetAnchors(border, 0, 0.975f, 1, 1);
            Image accent = border.gameObject.AddComponent<Image>();
            accent.color = accentColor;
            accent.raycastTarget = false;

            RectTransform titleRect = CreateRect("Title", cardPreviewRoot);
            SetAnchors(titleRect, 0.06f, 0.9f, 0.94f, 0.97f);
            cardPreviewTitle = titleRect.gameObject.AddComponent<Text>();
            cardPreviewTitle.font = font;
            cardPreviewTitle.fontSize = 18;
            cardPreviewTitle.fontStyle = FontStyle.Bold;
            cardPreviewTitle.color = Color.white;
            cardPreviewTitle.alignment = TextAnchor.MiddleCenter;
            cardPreviewTitle.raycastTarget = false;

            RectTransform bodyRect = CreateRect("Card Text", cardPreviewRoot);
            SetAnchors(bodyRect, 0.07f, 0.045f, 0.93f, 0.36f);
            cardPreviewBody = bodyRect.gameObject.AddComponent<Text>();
            cardPreviewBody.font = font;
            cardPreviewBody.fontSize = 14;
            cardPreviewBody.color = Color.white;
            cardPreviewBody.alignment = TextAnchor.UpperLeft;
            cardPreviewBody.horizontalOverflow = HorizontalWrapMode.Wrap;
            cardPreviewBody.verticalOverflow = VerticalWrapMode.Truncate;
            cardPreviewBody.raycastTarget = false;

            cardPreviewArtImage = AddCardArtPlaceholder(cardPreviewRoot, null, 0.06f, 0.39f, 0.94f, 0.88f, true);
            AspectRatioFitter artAspect = cardPreviewArtImage.GetComponent<AspectRatioFitter>();
            if (artAspect != null)
                artAspect.enabled = false;
            cardPreviewRoot.gameObject.SetActive(false);
        }

        private void BuildHeader(RectTransform root)
        {
            RectTransform header = AddPanel(root, 0, 0.9f, 1, 1, new Color(0.055f, 0.08f, 0.12f));
            AddText(header, "SYNCHRO / DUEL LAB", 0.025f, 0.22f, 0.32f, 0.78f, 27, accentColor, TextAnchor.MiddleLeft, true);
            AddText(header, "A tactical card-dueling prototype", 0.025f, 0, 0.38f, 0.28f, 13, mutedColor, TextAnchor.MiddleLeft);
            AddHeaderTab(header, ScreenMode.Duel, "DUEL", 0.34f, 0.42f, new Color(0.15f, 0.22f, 0.31f));
            AddHeaderTab(header, ScreenMode.Deck, "DECK", 0.425f, 0.50f, new Color(0.15f, 0.22f, 0.31f));
            AddHeaderTab(header, ScreenMode.Cards, "CARDS", 0.505f, 0.58f, new Color(0.15f, 0.22f, 0.31f));
            AddHeaderTab(header, ScreenMode.Shop, "SHOP", 0.585f, 0.66f, new Color(0.15f, 0.22f, 0.31f));
            AddHeaderTab(header, ScreenMode.AiLab, "AI LAB", 0.665f, 0.755f, new Color(0.36f, 0.23f, 0.5f));
            AddHeaderTab(header, ScreenMode.TestLab, "UNIT TESTS", 0.76f, 0.89f, new Color(0.12f, 0.42f, 0.4f));
            RectTransform creditsRect = CreateRect("Credits", header);
            SetAnchors(creditsRect, 0.91f, 0.2f, 0.985f, 0.8f);
            creditsText = creditsRect.gameObject.AddComponent<Text>();
            creditsText.font = font;
            creditsText.fontSize = 16;
            creditsText.color = new Color(1f, 0.8f, 0.32f);
            creditsText.alignment = TextAnchor.MiddleRight;
            creditsText.fontStyle = FontStyle.Bold;
            UpdateCreditsText();
        }

        private void AddHeaderTab(RectTransform header, ScreenMode mode, string label, float xMin, float xMax, Color baseColor)
        {
            RectTransform border = AddPanel(header, xMin - 0.004f, 0.15f, xMax + 0.004f, 0.85f,
                new Color(1f, 0.82f, 0.28f));
            headerTabBorders[mode] = border.GetComponent<Image>();
            RectTransform button = AddButton(header, label, xMin, 0.2f, xMax, 0.8f, baseColor,
                () => SwitchScreen(mode), 13);
            headerTabButtons[mode] = button.GetComponent<Button>();
        }

        private void UpdateHeaderTabSelection()
        {
            foreach (KeyValuePair<ScreenMode, Button> entry in headerTabButtons)
            {
                bool selected = entry.Key == screen;
                entry.Value.targetGraphic.color = selected
                    ? Color.Lerp(accentColor, Color.white, 0.08f)
                    : entry.Key == ScreenMode.AiLab
                        ? new Color(0.36f, 0.23f, 0.5f)
                        : entry.Key == ScreenMode.TestLab
                            ? new Color(0.12f, 0.42f, 0.4f)
                        : new Color(0.15f, 0.22f, 0.31f);
                headerTabBorders[entry.Key].enabled = selected;
            }
        }

        private void SwitchScreen(ScreenMode mode)
        {
            bool restartStandardDuel = mode == ScreenMode.Duel && engine != null &&
                (engine.IsAiVsAi || (mockDuelSimulator != null && engine == mockDuelSimulator.Engine));
            if (screen == ScreenMode.TestLab && mode != ScreenMode.TestLab && mockDuelSimulator != null)
            {
                mockDuelSimulator = null;
                engine = null;
            }
            deckOwner = null;
            selectedDeckCard = null;
            graveyardOwner = null;
            selectedGraveyardCard = null;
            extraDeckOpen = false;
            if (screen == ScreenMode.AiLab && mode != ScreenMode.AiLab && aiAutoplay)
            {
                aiAutoplay = false;
                notice = "Autoplay paused when you left the AI Lab.";
            }
            if (restartStandardDuel)
            {
                mockDuelSimulator = null;
                StartDuel();
            }
            screen = mode;
            Render();
        }

        private void Render()
        {
            if (contentRoot == null)
                return;
            BindEngineEvents();
            AdvanceAutomaticPhases();
            TrackCardVisualChanges();
            HideCardPreview();
            AwardMatchIfNeeded();
            UpdateCreditsText();
            UpdateHeaderTabSelection();
            deckPreviewRoot = null;
            graveyardPreviewRoot = null;
            sanganPreviewRoot = null;
            for (int i = contentRoot.childCount - 1; i >= 0; i--)
                Destroy(contentRoot.GetChild(i).gameObject);

            if (screen == ScreenMode.Duel)
                DrawDuelScreen();
            else if (screen == ScreenMode.Deck)
                DrawDeckScreen();
            else if (screen == ScreenMode.Cards)
                DrawCardLibraryScreen();
            else if (screen == ScreenMode.Shop)
                DrawShopScreen();
            else if (screen == ScreenMode.TestLab && mockDuelSimulator == null)
                DrawTestLabSetupScreen();
            else if (screen == ScreenMode.TestLab)
                DrawDuelScreen();
            else
                DrawAiLabScreen();

            if (packRevealOpen && screen == ScreenMode.Shop)
                DrawPackRevealOverlay();
            if (aiHandsOpen && screen == ScreenMode.AiLab && engine != null && engine.IsAiVsAi)
                DrawAiHandsOverlay();
            bool canInspectZones = screen == ScreenMode.Duel || screen == ScreenMode.AiLab ||
                                   (screen == ScreenMode.TestLab && mockDuelSimulator != null);
            if (graveyardOwner != null && canInspectZones && engine != null)
                DrawGraveyardOverlay();
            if (deckOwner != null && canInspectZones && engine != null)
                DrawDeckOverlay();
            if (extraDeckOpen && screen == ScreenMode.Duel && engine != null)
                DrawExtraDeckOverlay();
            if (screen == ScreenMode.Duel && engine != null && engine.Phase == DuelPhase.GameOver)
                DrawDuelResultsOverlay();
            if (engine != null && engine.SearchChoiceOpen &&
                (screen == ScreenMode.Duel || screen == ScreenMode.TestLab))
                DrawSanganSearchOverlay();
        }

        private void BindEngineEvents()
        {
            if (observedEngine == engine)
                return;

            if (observedEngine != null)
            {
                observedEngine.PhaseChanged -= OnPhaseStarted;
                observedEngine.AttackDeclared -= OnAttackDeclared;
                observedEngine.CardDrawn -= OnCardDrawn;
            }

            observedEngine = engine;
            lastCardVisualStates = null;
            pendingDrawnCardStates.Clear();
            if (observedEngine == null)
                return;

            observedEngine.PhaseChanged += OnPhaseStarted;
            observedEngine.AttackDeclared += OnAttackDeclared;
            observedEngine.CardDrawn += OnCardDrawn;
            OnPhaseStarted(observedEngine.Phase);
        }

        private void AdvanceAutomaticPhases()
        {
            bool duelIsVisible = screen == ScreenMode.Duel ||
                (screen == ScreenMode.TestLab && mockDuelSimulator != null);
            if (engine == null || !duelIsVisible)
                return;

            for (int advances = 0; advances < 16; advances++)
            {
                if (!engine.IsHumanActionWindow || engine.ResponseWindowOpen || engine.SearchChoiceOpen)
                    return;

                if (engine.Phase == DuelPhase.Draw || engine.Phase == DuelPhase.Standby)
                {
                    engine.AdvancePhase();
                    continue;
                }

                if (engine.Phase == DuelPhase.End && !HasEndPhaseAction())
                {
                    engine.AdvancePhase();
                    continue;
                }

                return;
            }
        }

        private bool HasEndPhaseAction()
        {
            return engine.Human.Hand.Any(card =>
                card.Definition.Kind == CardKind.Spell &&
                card.Definition.Subtype == CardSubtype.QuickPlay &&
                card.Definition.Effect != CardEffect.None);
        }

        private void TrackCardVisualChanges()
        {
            if (engine == null)
            {
                lastCardVisualStates = null;
                return;
            }

            Dictionary<CardInstance, CardVisualState> currentStates = CaptureCardVisualStates();
            if (lastCardVisualStates != null)
            {
                foreach (KeyValuePair<CardInstance, CardVisualState> entry in currentStates)
                {
                    if (!lastCardVisualStates.TryGetValue(entry.Key, out CardVisualState previous))
                        continue;

                    CardInstance card = entry.Key;
                    CardVisualState current = entry.Value;
                    CardVisualState movementStart = previous;
                    if (pendingDrawnCardStates.TryGetValue(card, out CardVisualState drawnState))
                    {
                        if (current.Zone == CardVisualZone.Hand)
                            continue;
                        movementStart = drawnState;
                    }

                    bool zoneChanged = movementStart.Zone != current.Zone || movementStart.Owner != current.Owner;
                    bool effectStarted = !movementStart.EffectUsedThisTurn && current.EffectUsedThisTurn;
                    bool activatedFromHand = movementStart.Zone == CardVisualZone.Hand &&
                        (card.Definition.Kind == CardKind.Spell || card.Definition.Kind == CardKind.Trap) &&
                        (current.Zone == CardVisualZone.Graveyard ||
                         current.Zone == CardVisualZone.FieldSpell ||
                         (current.Zone == CardVisualZone.SpellTrap && current.FaceUp));
                    bool revealedTrap = card.Definition.Kind == CardKind.Trap &&
                        movementStart.Zone == CardVisualZone.SpellTrap && !movementStart.FaceUp &&
                        (current.FaceUp || current.Zone == CardVisualZone.Graveyard);
                    bool revealed = !CardIsVisible(movementStart) && CardIsVisible(current);
                    bool showPopup = effectStarted || activatedFromHand || revealedTrap;
                    if (!zoneChanged && !showPopup && !revealed)
                        continue;

                    QueueCardAnimation(new CardMovementAnimation
                    {
                        Card = card,
                        From = GetCardVisualPosition(movementStart),
                        To = GetCardVisualPosition(current),
                        StartSize = GetCardVisualSize(movementStart),
                        EndSize = GetCardVisualSize(current),
                        ShowPopup = showPopup,
                        StartsFaceUp = CardIsVisible(movementStart),
                        EndsFaceUp = CardIsVisible(current)
                    });
                }
            }

            pendingDrawnCardStates.Clear();
            lastCardVisualStates = currentStates;
        }

        private Dictionary<CardInstance, CardVisualState> CaptureCardVisualStates()
        {
            Dictionary<CardInstance, CardVisualState> states = new Dictionary<CardInstance, CardVisualState>();
            AddCardVisualStates(states, engine.Human, engine.Human.Deck, CardVisualZone.Deck);
            AddCardVisualStates(states, engine.Human, engine.Human.Hand, CardVisualZone.Hand);
            AddCardVisualStates(states, engine.Human, engine.Human.Graveyard, CardVisualZone.Graveyard);
            AddCardVisualStates(states, engine.Human, engine.Human.Banished, CardVisualZone.Banished);
            AddCardVisualStates(states, engine.Human, engine.Human.ExtraDeck, CardVisualZone.ExtraDeck);
            AddCardVisualStates(states, engine.Opponent, engine.Opponent.Deck, CardVisualZone.Deck);
            AddCardVisualStates(states, engine.Opponent, engine.Opponent.Hand, CardVisualZone.Hand);
            AddCardVisualStates(states, engine.Opponent, engine.Opponent.Graveyard, CardVisualZone.Graveyard);
            AddCardVisualStates(states, engine.Opponent, engine.Opponent.Banished, CardVisualZone.Banished);
            AddCardVisualStates(states, engine.Opponent, engine.Opponent.ExtraDeck, CardVisualZone.ExtraDeck);

            for (int i = 0; i < engine.Human.MonsterZones.Length; i++)
                AddCardVisualState(states, engine.Human, engine.Human.MonsterZones[i], CardVisualZone.Monster, i,
                    engine.Human.MonsterZones.Length);
            for (int i = 0; i < engine.Opponent.MonsterZones.Length; i++)
                AddCardVisualState(states, engine.Opponent, engine.Opponent.MonsterZones[i], CardVisualZone.Monster, i,
                    engine.Opponent.MonsterZones.Length);
            for (int i = 0; i < engine.Human.SpellTrapZones.Length; i++)
                AddCardVisualState(states, engine.Human, engine.Human.SpellTrapZones[i], CardVisualZone.SpellTrap, i,
                    engine.Human.SpellTrapZones.Length);
            for (int i = 0; i < engine.Opponent.SpellTrapZones.Length; i++)
                AddCardVisualState(states, engine.Opponent, engine.Opponent.SpellTrapZones[i], CardVisualZone.SpellTrap, i,
                    engine.Opponent.SpellTrapZones.Length);
            AddCardVisualState(states, engine.Human, engine.Human.FieldSpellZone, CardVisualZone.FieldSpell, 0, 1);
            AddCardVisualState(states, engine.Opponent, engine.Opponent.FieldSpellZone, CardVisualZone.FieldSpell, 0, 1);
            return states;
        }

        private static void AddCardVisualStates(
            Dictionary<CardInstance, CardVisualState> states,
            DuelPlayer owner,
            IReadOnlyList<CardInstance> cards,
            CardVisualZone zone)
        {
            for (int i = 0; i < cards.Count; i++)
                AddCardVisualState(states, owner, cards[i], zone, i, cards.Count);
        }

        private static void AddCardVisualState(
            Dictionary<CardInstance, CardVisualState> states,
            DuelPlayer owner,
            CardInstance card,
            CardVisualZone zone,
            int index,
            int count)
        {
            if (card == null)
                return;
            states[card] = new CardVisualState
            {
                Owner = owner,
                Zone = zone,
                Index = index,
                ContainerCount = count,
                FaceUp = card.FaceUp,
                EffectUsedThisTurn = card.EffectUsedThisTurn
            };
        }

        private static bool CardIsVisible(CardVisualState state)
        {
            if (state.Zone == CardVisualZone.Deck || state.Zone == CardVisualZone.ExtraDeck ||
                (state.Owner.IsAi && state.Zone == CardVisualZone.Hand))
                return false;
            return state.Zone == CardVisualZone.Graveyard || state.Zone == CardVisualZone.Banished ||
                   state.FaceUp;
        }

        private Vector2 GetCardVisualPosition(CardVisualState state)
        {
            switch (state.Zone)
            {
                case CardVisualZone.Hand:
                    float baseWidth = GetPortraitWidth(HandCardHeight);
                    float gap = Math.Min(HandCardGap, HandMaxWidth / Math.Max(1, state.ContainerCount - 1));
                    float width = Math.Min(baseWidth,
                        (HandMaxWidth - (state.ContainerCount - 1) * gap) / state.ContainerCount);
                    float start = (1f - (state.ContainerCount * width +
                        (state.ContainerCount - 1) * gap)) * 0.5f;
                    return new Vector2(start + state.Index * (width + gap) + width * 0.5f,
                        0.018f + GetPortraitHeight(width) * 0.5f);
                case CardVisualZone.Monster:
                    return new Vector2(MonsterCardStartX + state.Index * MonsterCardStepX + BoardCardWidth * 0.5f,
                        (state.Owner == engine.Opponent ? 0.59f : 0.35f) + BoardCardHeight * 0.5f);
                case CardVisualZone.SpellTrap:
                    return new Vector2(MonsterCardStartX + state.Index * MonsterCardStepX + BoardCardWidth * 0.5f,
                        (state.Owner == engine.Opponent ? 0.75f : 0.19f) + BoardCardHeight * 0.5f);
                case CardVisualZone.FieldSpell:
                    return state.Owner == engine.Opponent
                        ? new Vector2(OpponentDeckX + BoardCardWidth * 0.5f, 0.59f + BoardCardHeight * 0.5f)
                        : new Vector2(0.205f + BoardCardWidth * 0.5f, 0.35f + BoardCardHeight * 0.5f);
                case CardVisualZone.Graveyard:
                    return state.Owner == engine.Opponent
                        ? new Vector2(0.205f + BoardCardWidth * 0.5f, 0.75f + BoardCardHeight * 0.5f)
                        : new Vector2(0.74f + BoardCardWidth * 0.5f, 0.19f + BoardCardHeight * 0.5f);
                case CardVisualZone.Banished:
                    return state.Owner == engine.Opponent
                        ? new Vector2(0.205f + BoardCardWidth * 0.5f, 0.59f + BoardCardHeight * 0.5f)
                        : new Vector2(0.74f + BoardCardWidth * 0.5f, 0.35f + BoardCardHeight * 0.5f);
                case CardVisualZone.ExtraDeck:
                    return new Vector2(0.86f + 0.115f * 0.5f, 0.018f + 0.112f * 0.5f);
                default:
                    return state.Owner == engine.Opponent
                        ? new Vector2(OpponentDeckX + BoardCardWidth * 0.5f, 0.75f + BoardCardHeight * 0.5f)
                        : new Vector2(0.205f + BoardCardWidth * 0.5f, 0.19f + BoardCardHeight * 0.5f);
            }
        }

        private Vector2 GetCardVisualSize(CardVisualState state)
        {
            Rect canvasRect = ((RectTransform)canvas.transform).rect;
            if (state.Zone == CardVisualZone.Hand)
            {
                float baseWidth = GetPortraitWidth(HandCardHeight);
                float gap = Math.Min(HandCardGap, HandMaxWidth / Math.Max(1, state.ContainerCount - 1));
                float width = Math.Min(baseWidth,
                    (HandMaxWidth - (state.ContainerCount - 1) * gap) / state.ContainerCount);
                return new Vector2(width * canvasRect.width, GetPortraitHeight(width) * canvasRect.height);
            }
            return new Vector2(BoardCardWidth * canvasRect.width, BoardCardHeight * canvasRect.height);
        }

        private void QueueCardAnimation(CardMovementAnimation animation)
        {
            if (canvas == null)
                return;
            cardAnimationQueue.Enqueue(animation);
            if (cardAnimationRoutine == null)
                cardAnimationRoutine = StartCoroutine(PlayCardAnimations());
        }

        private void OnCardDrawn(DuelPlayer owner, CardInstance card)
        {
            if (owner == null || card == null)
                return;

            CardVisualState deckState = lastCardVisualStates != null &&
                lastCardVisualStates.TryGetValue(card, out CardVisualState previous) &&
                previous.Zone == CardVisualZone.Deck
                ? previous
                : new CardVisualState
                {
                    Owner = owner,
                    Zone = CardVisualZone.Deck,
                    Index = 0,
                    ContainerCount = owner.Deck.Count + 1,
                    FaceUp = false
                };
            CardVisualState handState = new CardVisualState
            {
                Owner = owner,
                Zone = CardVisualZone.Hand,
                Index = owner.Hand.IndexOf(card),
                ContainerCount = owner.Hand.Count,
                FaceUp = card.FaceUp,
                EffectUsedThisTurn = card.EffectUsedThisTurn
            };
            pendingDrawnCardStates[card] = handState;
            QueueCardAnimation(new CardMovementAnimation
            {
                Card = card,
                From = GetCardVisualPosition(deckState),
                To = GetCardVisualPosition(handState),
                StartSize = GetCardVisualSize(deckState),
                EndSize = GetCardVisualSize(handState),
                StartsFaceUp = false,
                EndsFaceUp = CardIsVisible(handState)
            });
        }

        private IEnumerator PlayCardAnimations()
        {
            while (cardAnimationQueue.Count > 0)
            {
                CardMovementAnimation animation = cardAnimationQueue.Dequeue();
                RectTransform visual = CreateAnimatedCard(animation.Card.Definition,
                    animation.StartsFaceUp, animation.From, animation.StartSize);
                if (animation.ShowPopup)
                {
                    yield return TweenCardVisual(visual, animation.From, new Vector2(0.5f, 0.5f),
                        animation.StartSize, new Vector2(190f, 266f), 0.3f);
                    if (!animation.StartsFaceUp)
                        yield return FlipAnimatedCard(visual, true);
                    yield return new WaitForSecondsRealtime(0.8f);
                    yield return TweenCardVisual(visual, new Vector2(0.5f, 0.5f), animation.To,
                        new Vector2(190f, 266f), animation.EndSize, 0.3f);
                }
                else
                {
                    yield return TweenCardVisual(visual, animation.From, animation.To,
                        animation.StartSize, animation.EndSize, 0.42f);
                    if (animation.StartsFaceUp != animation.EndsFaceUp)
                        yield return FlipAnimatedCard(visual, animation.EndsFaceUp);
                }

                yield return TweenCardVisual(visual, animation.To, animation.To,
                    animation.EndSize, animation.EndSize * 1.08f, 0.07f);
                yield return TweenCardVisual(visual, animation.To, animation.To,
                    animation.EndSize * 1.08f, animation.EndSize, 0.09f);
                Destroy(visual.gameObject);
            }

            cardAnimationRoutine = null;
        }

        private RectTransform CreateAnimatedCard(
            CardDefinition definition,
            bool faceUp,
            Vector2 normalizedPosition,
            Vector2 size)
        {
            RectTransform root = CreateRect("Animated Card", (RectTransform)canvas.transform);
            root.anchorMin = new Vector2(0.5f, 0.5f);
            root.anchorMax = new Vector2(0.5f, 0.5f);
            root.sizeDelta = size;
            SetAnimatedCardPosition(root, normalizedPosition);
            Image frame = root.gameObject.AddComponent<Image>();
            frame.color = CardFrameColor(definition);
            frame.raycastTarget = false;

            RectTransform back = CreateRect("Back", root);
            Stretch(back);
            AddCardArtPlaceholder(back, null, 0f, 0f, 1f, 1f, false);

            RectTransform front = CreateRect("Front", root);
            Stretch(front);
            AddCardArtPlaceholder(front, definition, 0.06f, 0.2f, 0.94f, 0.82f, true);
            AddText(front, definition.DisplayName, 0.04f, 0.82f, 0.96f, 0.98f,
                11, Color.white, TextAnchor.MiddleCenter, true);
            AddText(front, CardTypeLabel(definition), 0.04f, 0.02f, 0.96f, 0.2f,
                9, Color.white, TextAnchor.MiddleCenter, true);
            SetAnimatedCardFace(root, faceUp);
            root.SetAsLastSibling();
            return root;
        }

        private static void SetAnimatedCardPosition(RectTransform card, Vector2 normalizedPosition)
        {
            RectTransform canvasRect = card.parent as RectTransform;
            card.anchoredPosition = new Vector2(
                (normalizedPosition.x - 0.5f) * canvasRect.rect.width,
                (normalizedPosition.y - 0.5f) * canvasRect.rect.height);
        }

        private static void SetAnimatedCardFace(RectTransform card, bool faceUp)
        {
            card.Find("Front").gameObject.SetActive(faceUp);
            card.Find("Back").gameObject.SetActive(!faceUp);
        }

        private IEnumerator FlipAnimatedCard(RectTransform card, bool faceUp)
        {
            Vector2 size = card.sizeDelta;
            yield return TweenCardVisual(card, GetAnimatedCardPosition(card), GetAnimatedCardPosition(card),
                size, new Vector2(0f, size.y), 0.1f);
            SetAnimatedCardFace(card, faceUp);
            yield return TweenCardVisual(card, GetAnimatedCardPosition(card), GetAnimatedCardPosition(card),
                new Vector2(0f, size.y), size, 0.1f);
        }

        private static Vector2 GetAnimatedCardPosition(RectTransform card)
        {
            RectTransform canvasRect = card.parent as RectTransform;
            return new Vector2(card.anchoredPosition.x / canvasRect.rect.width + 0.5f,
                card.anchoredPosition.y / canvasRect.rect.height + 0.5f);
        }

        private IEnumerator TweenCardVisual(
            RectTransform card,
            Vector2 from,
            Vector2 to,
            Vector2 startSize,
            Vector2 endSize,
            float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                float progress = Mathf.Clamp01(elapsed / duration);
                float eased = progress * progress * (3f - 2f * progress);
                SetAnimatedCardPosition(card, Vector2.Lerp(from, to, eased));
                card.sizeDelta = Vector2.Lerp(startSize, endSize, eased);
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            SetAnimatedCardPosition(card, to);
            card.sizeDelta = endSize;
        }

        private void OnPhaseStarted(DuelPhase phase)
        {
            if (phase == DuelPhase.GameOver || engine == null || canvas == null)
                return;
            phaseTickerQueue.Enqueue(GetPhaseTickerLabel(phase));
            if (phaseTickerRoutine == null)
                phaseTickerRoutine = StartCoroutine(PlayPhaseTicker());
        }

        private static string GetPhaseTickerLabel(DuelPhase phase)
        {
            switch (phase)
            {
                case DuelPhase.Draw: return "DRAW PHASE";
                case DuelPhase.Standby: return "STANDBY PHASE";
                case DuelPhase.MainOne: return "MAIN PHASE I";
                case DuelPhase.Battle: return "BATTLE PHASE";
                case DuelPhase.MainTwo: return "MAIN PHASE II";
                case DuelPhase.End: return "END PHASE";
                default: return "";
            }
        }

        private IEnumerator PlayPhaseTicker()
        {
            RectTransform canvasRect = (RectTransform)canvas.transform;
            RectTransform inputBlocker = CreateRect("Phase Ticker Input Blocker", canvasRect);
            Stretch(inputBlocker);
            Image blockerImage = inputBlocker.gameObject.AddComponent<Image>();
            blockerImage.color = new Color(0f, 0f, 0f, 0f);
            blockerImage.raycastTarget = true;
            canvasInputGroup.interactable = false;
            canvasInputGroup.blocksRaycasts = true;

            while (phaseTickerQueue.Count > 0)
            {
                RectTransform ticker = CreateRect("Phase Ticker", canvasRect);
                ticker.anchorMin = new Vector2(0f, 0.505f);
                ticker.anchorMax = new Vector2(1f, 0.59f);
                ticker.sizeDelta = Vector2.zero;
                ticker.anchoredPosition = new Vector2(-canvasRect.rect.width, 0f);
                Image background = ticker.gameObject.AddComponent<Image>();
                background.color = new Color(0.035f, 0.055f, 0.085f, 1f);
                background.raycastTarget = false;
                AddPhaseTickerEdge(ticker, true);
                AddPhaseTickerEdge(ticker, false);
                ticker.SetAsLastSibling();

                yield return SlidePhaseTickerBackground(ticker, canvasRect.rect.width, 0.3f);

                RectTransform labelRect = CreateRect("Phase Ticker Text", ticker);
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.sizeDelta = Vector2.zero;
                Text label = labelRect.gameObject.AddComponent<Text>();
                label.font = font;
                label.text = phaseTickerQueue.Dequeue();
                label.fontSize = 21;
                label.fontStyle = FontStyle.Bold;
                label.color = Color.white;
                label.alignment = TextAnchor.MiddleCenter;
                label.raycastTarget = false;
                CanvasGroup labelGroup = labelRect.gameObject.AddComponent<CanvasGroup>();
                labelGroup.alpha = 0f;
                labelRect.localScale = Vector3.one * 0.65f;
                yield return PlopPhaseTickerText(labelRect, labelGroup, 0.22f);
                yield return new WaitForSecondsRealtime(1f);
                Destroy(ticker.gameObject);
            }

            Destroy(inputBlocker.gameObject);
            canvasInputGroup.interactable = true;
            phaseTickerRoutine = null;
        }

        private void AddPhaseTickerEdge(RectTransform ticker, bool top)
        {
            RectTransform edge = CreateRect(top ? "Top Accent Band" : "Bottom Accent Band", ticker);
            float edgeAnchor = top ? 1f : 0f;
            edge.anchorMin = new Vector2(0f, edgeAnchor);
            edge.anchorMax = new Vector2(1f, edgeAnchor);
            edge.sizeDelta = new Vector2(0f, 3f);
            edge.anchoredPosition = new Vector2(0f, top ? -1.5f : 1.5f);
            Image image = edge.gameObject.AddComponent<Image>();
            image.color = new Color(accentColor.r, accentColor.g, accentColor.b, 0.78f);
            image.raycastTarget = false;
        }

        private IEnumerator SlidePhaseTickerBackground(RectTransform ticker, float width, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                float progress = Mathf.Clamp01(elapsed / duration);
                float eased = progress * progress * (3f - 2f * progress);
                ticker.anchoredPosition = new Vector2(Mathf.Lerp(-width, 0f, eased), 0f);
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            ticker.anchoredPosition = Vector2.zero;
        }

        private IEnumerator PlopPhaseTickerText(RectTransform label, CanvasGroup canvasGroup, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                float progress = Mathf.Clamp01(elapsed / duration);
                float eased = 1f - Mathf.Pow(1f - progress, 3f);
                float scale = Mathf.Lerp(0.65f, 1.08f, eased);
                label.localScale = Vector3.one * scale;
                canvasGroup.alpha = progress;
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            label.localScale = Vector3.one;
            canvasGroup.alpha = 1f;
        }

        private void OnAttackDeclared(CardInstance attacker, CardInstance target)
        {
            if (engine == null || canvas == null || attacker == null)
                return;

            Vector2 from = lastCardVisualStates != null &&
                lastCardVisualStates.TryGetValue(attacker, out CardVisualState attackerState)
                ? GetCardVisualPosition(attackerState)
                : new Vector2(0.5f, 0.35f);
            Vector2 to = target == null
                ? new Vector2(0.5f, 0.83f)
                : lastCardVisualStates != null &&
                  lastCardVisualStates.TryGetValue(target, out CardVisualState targetState)
                    ? GetCardVisualPosition(targetState)
                    : new Vector2(0.5f, 0.59f);

            attackAnimationQueue.Enqueue(new AttackAnimation
            {
                Attacker = attacker,
                From = from,
                To = to
            });
            if (attackAnimationRoutine == null)
                attackAnimationRoutine = StartCoroutine(PlayAttackAnimations());
        }

        private IEnumerator PlayAttackAnimations()
        {
            RectTransform canvasRect = (RectTransform)canvas.transform;
            while (attackAnimationQueue.Count > 0)
            {
                AttackAnimation animation = attackAnimationQueue.Dequeue();
                List<Image> dots = new List<Image>();
                for (int i = 1; i <= 8; i++)
                {
                    float progress = i / 9f;
                    RectTransform dot = CreateRect("Attack Trail Dot", canvasRect);
                    dot.anchorMin = new Vector2(0.5f, 0.5f);
                    dot.anchorMax = new Vector2(0.5f, 0.5f);
                    dot.sizeDelta = new Vector2(7f, 7f);
                    SetAnimatedCardPosition(dot, Vector2.Lerp(animation.From, animation.To, progress));
                    Image dotImage = dot.gameObject.AddComponent<Image>();
                    dotImage.color = new Color(1f, 0.78f, 0.35f, 0.9f);
                    dotImage.raycastTarget = false;
                    dots.Add(dotImage);
                }

                RectTransform card = CreateAnimatedCard(animation.Attacker.Definition, true,
                    animation.From, new Vector2(96f, 134f));
                Vector2 lunge = Vector2.Lerp(animation.From, animation.To, 0.32f);
                yield return TweenCardVisual(card, animation.From, lunge,
                    card.sizeDelta, card.sizeDelta * 1.08f, 0.16f);
                yield return TweenCardVisual(card, lunge, animation.From,
                    card.sizeDelta, new Vector2(96f, 134f), 0.2f);
                for (int i = 0; i < dots.Count; i++)
                    Destroy(dots[i].gameObject);
                Destroy(card.gameObject);
            }

            attackAnimationRoutine = null;
        }

        private void DrawDuelScreen()
        {
            if (engine == null)
            {
                AddText(contentRoot, "BUILD YOUR DECK", 0.15f, 0.5f, 0.85f, 0.7f, 32, Color.white, TextAnchor.MiddleCenter, true);
                AddText(contentRoot, notice, 0.15f, 0.35f, 0.85f, 0.5f, 20, mutedColor, TextAnchor.MiddleCenter);
                AddButton(contentRoot, "OPEN DECK BUILDER", 0.35f, 0.2f, 0.65f, 0.31f, accentColor, () => SwitchScreen(ScreenMode.Deck), 18);
                return;
            }

            bool humanTurn = engine.CurrentPlayer == engine.Human && engine.Phase != DuelPhase.GameOver;
            DrawPlayerPortrait("AI", 0.025f, 0.804f, 0.085f, 0.9f,
                new Color(0.42f, 0.22f, 0.28f), !humanTurn && engine.Phase != DuelPhase.GameOver);
            AddText(contentRoot, engine.Opponent.Name + "\n" + engine.Opponent.LifePoints + " LP",
                0.095f, 0.805f, 0.195f, 0.9f, 17, Color.white, TextAnchor.MiddleLeft, true);
            if (!humanTurn && engine.Phase != DuelPhase.GameOver)
                DrawCurrentTurnLabel(0.095f, 0.75f, 0.195f, 0.79f);
            DrawGraveyardButton(engine.Opponent, 0.205f, 0.75f);
            DrawDeckButton(engine.Opponent, OpponentDeckX, 0.75f);
            DrawBanishedIndicator(engine.Opponent, 0.205f, 0.59f);
            DrawSpellTrapRow(engine.Opponent.SpellTrapZones, 0.75f);
            DrawFieldSpellSlot(engine.Opponent.FieldSpellZone, true);
            DrawZoneRow(engine.Opponent.MonsterZones, 0.59f, true);

            string prompt = engine.Phase == DuelPhase.GameOver
                ? engine.Winner + " wins. Start a new duel whenever you're ready."
                : PhasePrompt();
            if (screen == ScreenMode.TestLab && mockDuelSimulator != null)
            {
                CardDefinition focus = mockDuelSimulator.FocusCard.Definition;
                string effectStatus = focus.Effect != CardEffect.None
                    ? engine.PhaseLabel
                    : RequiresEffectResolver(focus) ? "UNIMPLEMENTED" : "NO EFFECT";
                prompt = "UNIT TEST  •  " + mockDuelSimulator.FocusCard.Definition.DisplayName + "  •  " +
                    mockDuelSimulator.ManualResult + "  •  " + effectStatus;
            }
            DrawPhaseTrack();
            AddText(contentRoot, "TURN " + engine.TurnNumber + "  •  " + prompt,
                0.025f, 0.565f, 0.975f, 0.588f, 12, accentColor, TextAnchor.MiddleCenter, true);

            DrawZoneRow(engine.Human.MonsterZones, 0.35f, false);
            DrawSpellTrapRow(engine.Human.SpellTrapZones, 0.19f);
            DrawFieldSpellSlot(engine.Human.FieldSpellZone, false);
            DrawPlayerPortrait("YOU", 0.025f, 0.2f, 0.085f, 0.296f,
                new Color(0.12f, 0.36f, 0.4f), humanTurn);
            AddText(contentRoot, engine.Human.Name + "\n" + engine.Human.LifePoints + " LP",
                0.095f, 0.2f, 0.195f, 0.296f, 17, Color.white, TextAnchor.MiddleLeft, true);
            if (humanTurn)
                DrawCurrentTurnLabel(0.095f, 0.3f, 0.195f, 0.34f);
            DrawDeckButton(engine.Human, 0.205f, 0.19f);
            DrawGraveyardButton(engine.Human, 0.74f, 0.19f);
            DrawBanishedIndicator(engine.Human, 0.74f, 0.35f);

            DrawActionBar();
            DrawExtraDeck();
            DrawHand();
            DrawDuelLog();



            if (engine.ResponseWindowOpen)
                DrawResponseWindow();
        }

        private void DrawPhaseTrack()
        {
            DuelPhase[] phases =
            {
                DuelPhase.Draw, DuelPhase.Standby, DuelPhase.MainOne,
                DuelPhase.Battle, DuelPhase.MainTwo, DuelPhase.End
            };
            string[] labels = { "DRAW", "STANDBY", "MAIN 1", "BATTLE", "MAIN 2", "END" };
            float startX = 0.025f;
            const float width = 0.15f;
            const float gap = 0.01f;
            int activeIndex = GetPhaseIndex(engine.Phase);
            for (int i = 0; i < phases.Length; i++)
            {
                DuelPhase phase = phases[i];
                float xMin = startX + i * (width + gap);
                float xMax = xMin + width;
                bool active = phase == engine.Phase;
                Color phaseColor = GetPhaseColor(phase);
                Color color = active
                    ? Color.Lerp(phaseColor, Color.white, 0.18f)
                    : i < activeIndex
                        ? Color.Lerp(phaseColor, new Color(0.12f, 0.22f, 0.25f), 0.58f)
                        : Color.Lerp(phaseColor, new Color(0.07f, 0.1f, 0.14f), 0.48f);
                if (active)
                    AddPanel(contentRoot, xMin - 0.004f, 0.511f, xMax + 0.004f, 0.564f,
                        new Color(1f, 0.82f, 0.28f));
                AddButton(contentRoot, labels[i], xMin, 0.515f, xMax, 0.56f, color,
                    () =>
                    {
                        if (engine.AdvanceToPhase(phase))
                        {
                            ClearSelection();
                            Render();
                        }
                    }, 12);
            }
        }

        private static Color GetPhaseColor(DuelPhase phase)
        {
            switch (phase)
            {
                case DuelPhase.Draw: return new Color(0.12f, 0.42f, 0.78f);
                case DuelPhase.Standby: return new Color(0.08f, 0.58f, 0.62f);
                case DuelPhase.MainOne: return new Color(0.18f, 0.58f, 0.32f);
                case DuelPhase.Battle: return new Color(0.82f, 0.28f, 0.16f);
                case DuelPhase.MainTwo: return new Color(0.5f, 0.58f, 0.2f);
                case DuelPhase.End: return new Color(0.42f, 0.32f, 0.64f);
                default: return new Color(0.14f, 0.2f, 0.28f);
            }
        }

        private static int GetPhaseIndex(DuelPhase phase)
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

        private void DrawResponseWindow()
        {
            AddPanel(contentRoot, 0, 0.09f, 1, 0.9f, new Color(0.01f, 0.02f, 0.035f, 0.82f));
            AddPanel(contentRoot, 0.2f, 0.19f, 0.8f, 0.81f, new Color(0.07f, 0.1f, 0.15f));
            AddText(contentRoot, "CHAIN RESPONSE", 0.24f, 0.72f, 0.76f, 0.79f, 22,
                accentColor, TextAnchor.MiddleCenter, true);
            AddText(contentRoot, engine.ResponsePrompt, 0.25f, 0.65f, 0.75f, 0.72f, 16,
                Color.white, TextAnchor.MiddleCenter);

            IReadOnlyList<CardInstance> responses = engine.AvailableResponseTraps;
            for (int i = 0; i < responses.Count; i++)
            {
                CardInstance trap = responses[i];
                float top = 0.62f - i * 0.075f;
                string action = engine.ResponseDiscardChoiceOpen
                    ? "DISCARD " + trap.Definition.DisplayName
                    : "ACTIVATE " + trap.Definition.DisplayName;
                AddButton(contentRoot, action,
                    0.28f, top - 0.06f, 0.72f, top, CardFrameColor(trap.Definition),
                    () =>
                    {
                        engine.TryRespondWithTrap(trap);
                        Render();
                    }, 14, trap.Definition);
            }

            AddButton(contentRoot, "PASS — RESOLVE THE CHAIN", 0.31f, 0.22f, 0.69f, 0.29f,
                new Color(0.2f, 0.34f, 0.46f), () =>
                {
                    engine.PassResponseWindow();
                    Render();
                }, 13);
        }

        private void DrawSanganSearchOverlay()
        {
            IReadOnlyList<CardInstance> choices = engine.AvailableSearchCards;
            AddPanel(contentRoot, 0, 0.09f, 1, 0.9f, new Color(0.01f, 0.02f, 0.035f, 0.9f));
            AddPanel(contentRoot, 0.06f, 0.12f, 0.94f, 0.88f, panelColor);
            AddText(contentRoot, "SANGAN — CHOOSE A MONSTER",
                0.2f, 0.75f, 0.8f, 0.81f, 22, accentColor, TextAnchor.MiddleCenter, true);
            AddText(contentRoot, engine.SearchChoicePrompt + " Select a card, review it, then confirm.",
                0.2f, 0.69f, 0.8f, 0.75f, 14, Color.white, TextAnchor.MiddleCenter);

            RectTransform viewport = AddPanel(contentRoot, 0.2f, 0.24f, 0.57f, 0.67f, backgroundColor);
            viewport.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            ScrollRect scroll = viewport.gameObject.AddComponent<WheelScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            ConfigureMouseWheelScrolling(scroll);

            const float rowHeight = 58f;
            RectTransform list = CreateRect("Sangan Search Candidates", viewport);
            list.anchorMin = new Vector2(0, 1);
            list.anchorMax = new Vector2(1, 1);
            list.pivot = new Vector2(0.5f, 1);
            list.sizeDelta = new Vector2(0, choices.Count * rowHeight);
            scroll.content = list;
            scroll.viewport = viewport;
            scroll.verticalScrollbar = CreateVerticalScrollbar(0.575f, 0.24f, 0.595f, 0.67f);
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            scroll.verticalNormalizedPosition = sanganScrollPosition;
            scroll.onValueChanged.AddListener(_ =>
                sanganScrollPosition = scroll.verticalNormalizedPosition);

            if (selectedSanganSearchCard == null || !choices.Contains(selectedSanganSearchCard))
                selectedSanganSearchCard = choices.FirstOrDefault();

            for (int i = 0; i < choices.Count; i++)
            {
                CardInstance choice = choices[i];
                string label = choice.Definition.DisplayName + "  •  ATK " +
                    choice.Definition.Attack + " / DEF " + choice.Definition.Defense;
                Color rowColor = choice == selectedSanganSearchCard
                    ? Color.Lerp(CardFrameColor(choice.Definition), accentColor, 0.38f)
                    : CardFrameColor(choice.Definition);
                AddScrollListButton(list, label, i, rowHeight, rowColor,
                    () =>
                    {
                        sanganScrollPosition = scroll.verticalNormalizedPosition;
                        selectedSanganSearchCard = choice;
                        Render();
                    }, 12);
                AttachInlineCardPreviewHover(list.GetChild(list.childCount - 1).GetComponent<RectTransform>(),
                    choice.Definition,
                    () => selectedSanganSearchCard == null ? null : selectedSanganSearchCard.Definition,
                    definition => UpdatePortraitCardPreview(sanganPreviewRoot, definition));
            }

            sanganPreviewRoot = CreatePortraitCardPreview(
                selectedSanganSearchCard == null ? null : selectedSanganSearchCard.Definition,
                0.77f, 0.24f, 0.43f);

            AddButton(contentRoot, "ADD SELECTED CARD TO HAND", 0.57f, 0.17f, 0.94f, 0.23f,
                selectedSanganSearchCard == null ? mutedColor : accentColor,
                () =>
                {
                    if (selectedSanganSearchCard != null &&
                        engine.TryChooseSearchCard(selectedSanganSearchCard))
                    {
                        selectedSanganSearchCard = null;
                        Render();
                    }
                }, 13);
        }

        private void ConfigureMouseWheelScrolling(ScrollRect scroll)
        {
            scroll.scrollSensitivity = 36f;
        }

        private void DrawZoneRow(CardInstance[] zones, float y, bool opponent)
        {
            for (int i = 0; i < zones.Length; i++)
            {
                float xMin = MonsterCardStartX + i * MonsterCardStepX;
                float xMax = xMin + BoardCardWidth;
                CardInstance card = zones[i];
                string label = card == null ? "MONSTER\nSLOT" : CardLabel(card, opponent);
                Color color = card == null ? new Color(0.09f, 0.13f, 0.18f) :
                    (selectedMaterials.Contains(card) || card == selectedMonsterCard || card == selectedAttacker
                        ? new Color(0.11f, 0.45f, 0.43f)
                        : CardZoneColor(card));
                RectTransform slot = AddButton(contentRoot, label, xMin, y, xMax, y + BoardCardHeight, color,
                    () => OnZoneClicked(card, opponent), 11,
                    card != null && (!opponent || card.FaceUp) ? card.Definition : null);
                AddCardArtPlaceholder(slot, card != null && (!opponent || card.FaceUp) ? card.Definition : null,
                    0f, 0f, 1f, 1f, card == null || card.FaceUp);
                RotateDefenseMonster(slot, card);
                FitMonsterSlotStats(slot, card);
            }
        }

        private void DrawSpellTrapRow(CardInstance[] zones, float y)
        {
            for (int i = 0; i < zones.Length; i++)
            {
                float xMin = MonsterCardStartX + i * MonsterCardStepX;
                CardInstance card = zones[i];
                string label = card == null ? "SPELL/TRAP\nSLOT" :
                    card.FaceUp ? CardLabel(card, true) : "SET CARD";
                Color color = card == null ? new Color(0.09f, 0.13f, 0.18f) :
                    card == selectedTarget ? new Color(0.11f, 0.45f, 0.43f) : CardZoneColor(card);
                RectTransform slot = AddButton(contentRoot, label, xMin, y, xMin + BoardCardWidth, y + BoardCardHeight, color,
                    () => OnSpellTrapZoneClicked(card), 10,
                    card != null && card.FaceUp ? card.Definition : null);
                AddCardArtPlaceholder(slot, card != null && card.FaceUp ? card.Definition : null,
                    0f, 0f, 1f, 1f, card == null || card.FaceUp);
                AddSpellTrapTargetHover(slot, card);
            }
        }

        private static void FitMonsterSlotStats(RectTransform slot, CardInstance card)
        {
            if (card == null || !card.FaceUp || !card.Definition.IsMonster)
                return;

            Transform labelTransform = slot.Find("Label");
            if (labelTransform == null)
                return;

            Text label = labelTransform.GetComponent<Text>();
            if (label == null)
                return;

            SetAnchors(label.rectTransform, 0.02f, 0.02f, 0.98f, 0.58f);
            label.fontSize = 10;
        }

        private static void RotateDefenseMonster(RectTransform slot, CardInstance card)
        {
            if (card != null && !card.AttackPosition)
                slot.localRotation = Quaternion.Euler(0, 0, 90);
        }

        private void DrawFieldSpellSlot(CardInstance card, bool opponent)
        {
            float xMin = opponent ? OpponentDeckX : 0.205f;
            float yMin = opponent ? 0.59f : 0.35f;
            AddText(contentRoot, "FIELD", xMin, opponent ? 0.73f : 0.17f, xMin + BoardCardWidth,
                opponent ? 0.75f : 0.19f, 9, mutedColor, TextAnchor.MiddleCenter, true);
            RectTransform slot = AddButton(contentRoot, card == null ? "FIELD\nSLOT" : CardLabel(card, opponent),
                xMin, yMin, xMin + BoardCardWidth, yMin + BoardCardHeight,
                card == null ? new Color(0.09f, 0.13f, 0.18f) :
                    card == selectedTarget ? new Color(0.11f, 0.45f, 0.43f) : CardZoneColor(card),
                () => OnSpellTrapZoneClicked(card), 9,
                card != null && (!opponent || card.FaceUp) ? card.Definition : null);
            AddCardArtPlaceholder(slot, card != null && (!opponent || card.FaceUp) ? card.Definition : null,
                0f, 0f, 1f, 1f, card == null || card.FaceUp);
            AddSpellTrapTargetHover(slot, card);
        }

        private void AddSpellTrapTargetHover(RectTransform slot, CardInstance card)
        {
            if (card == null || !IsSelectingSpellTrapTarget())
                return;

            Outline outline = slot.gameObject.AddComponent<Outline>();
            outline.effectColor = card == selectedTarget
                ? new Color(1f, 0.82f, 0.28f)
                : new Color(0.18f, 0.92f, 0.78f);
            outline.effectDistance = new Vector2(2f, 2f);
            outline.enabled = card == selectedTarget;

            EventTrigger trigger = slot.GetComponent<EventTrigger>();
            if (trigger == null)
                trigger = slot.gameObject.AddComponent<EventTrigger>();
            EventTrigger.Entry enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ =>
            {
                outline.enabled = true;
                outline.effectColor = card == selectedTarget
                    ? new Color(1f, 0.82f, 0.28f)
                    : new Color(0.18f, 0.92f, 0.78f);
            });
            trigger.triggers.Add(enter);
            EventTrigger.Entry exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => outline.enabled = card == selectedTarget);
            trigger.triggers.Add(exit);
        }

        private bool IsSelectingSpellTrapTarget()
        {
            return engine != null && selectedHandCard != null &&
                selectedHandCard.Definition.Effect == CardEffect.DestroySpellTrap &&
                engine.Human.Hand.Contains(selectedHandCard) &&
                (engine.Phase == DuelPhase.MainOne || engine.Phase == DuelPhase.MainTwo ||
                 engine.Phase == DuelPhase.Battle || engine.Phase == DuelPhase.End);
        }

        private void OpenGraveyard(DuelPlayer owner)
        {
            deckOwner = null;
            graveyardOwner = owner;
            selectedGraveyardCard = owner.Graveyard.LastOrDefault();
            graveyardScrollPosition = 1f;
            Render();
        }

        private void OpenDeck(DuelPlayer owner)
        {
            graveyardOwner = null;
            selectedGraveyardCard = null;
            deckOwner = owner;
            selectedDeckCard = owner.Deck.FirstOrDefault();
            deckScrollPosition = 1f;
            Render();
        }

        private void DrawDeckButton(DuelPlayer owner, float xMin, float yMin)
        {
            RectTransform button = AddButton(contentRoot, "DECK\n" + owner.DeckCount,
                xMin, yMin, xMin + BoardCardWidth, yMin + BoardCardHeight,
                new Color(0.12f, 0.2f, 0.32f), () => OpenDeck(owner), 9);
            AddCardArtPlaceholder(button, null, 0.06f, 0.06f, 0.94f, 0.94f, false);
        }

        private void DrawGraveyardButton(DuelPlayer owner, float xMin, float yMin)
        {
            AddButton(contentRoot, "GRAVE\n" + owner.Graveyard.Count,
                xMin, yMin, xMin + BoardCardWidth, yMin + BoardCardHeight,
                new Color(0.28f, 0.15f, 0.29f), () => OpenGraveyard(owner), 9);
        }

        private void DrawBanishedIndicator(DuelPlayer owner, float xMin, float yMin)
        {
            AddPanel(contentRoot, xMin, yMin, xMin + BoardCardWidth, yMin + BoardCardHeight,
                new Color(0.24f, 0.28f, 0.38f));
            AddText(contentRoot, "BANISHED\n" + owner.Banished.Count,
                xMin, yMin, xMin + BoardCardWidth, yMin + BoardCardHeight,
                8, Color.white, TextAnchor.MiddleCenter);
        }

        private void DrawDeckOverlay()
        {
            AddPanel(contentRoot, 0, 0.09f, 1, 0.9f, new Color(0.01f, 0.02f, 0.035f, 0.88f));
            AddPanel(contentRoot, 0.08f, 0.12f, 0.92f, 0.88f, panelColor);
            AddText(contentRoot, deckOwner.Name + " DECK  •  " + deckOwner.Deck.Count +
                " cards remaining  •  chance of each card on the next draw",
                0.12f, 0.81f, 0.77f, 0.86f, 20, Color.white, TextAnchor.MiddleLeft, true);
            AddButton(contentRoot, "CLOSE", 0.82f, 0.81f, 0.88f, 0.86f, new Color(0.36f, 0.22f, 0.24f),
                () =>
                {
                    deckOwner = null;
                    Render();
                }, 12);

            var remainingCards = deckOwner.Deck
                .GroupBy(card => card.Definition.CardId)
                .Select(group => new
                {
                    Card = group.First(),
                    Copies = group.Count()
                })
                .OrderBy(item => item.Card.Definition.DisplayName)
                .ToList();
            RectTransform viewport = AddPanel(contentRoot, 0.12f, 0.17f, 0.49f, 0.79f, backgroundColor);
            viewport.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            ScrollRect scroll = viewport.gameObject.AddComponent<WheelScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            ConfigureMouseWheelScrolling(scroll);

            RectTransform list = CreateRect("Remaining Deck Cards", viewport);
            list.anchorMin = new Vector2(0, 1);
            list.anchorMax = new Vector2(1, 1);
            list.pivot = new Vector2(0.5f, 1);
            const float rowPixelHeight = 58f;
            list.sizeDelta = new Vector2(0, remainingCards.Count * rowPixelHeight);
            scroll.content = list;
            scroll.viewport = viewport;
            scroll.verticalScrollbar = CreateVerticalScrollbar(0.495f, 0.17f, 0.515f, 0.79f);
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            scroll.verticalNormalizedPosition = deckScrollPosition;
            scroll.onValueChanged.AddListener(_ => deckScrollPosition = scroll.verticalNormalizedPosition);

            CardInstance selected = selectedDeckCard != null && deckOwner.Deck.Contains(selectedDeckCard)
                ? selectedDeckCard : deckOwner.Deck.FirstOrDefault();
            deckPreviewRoot = CreatePortraitCardPreview(selected == null ? null : selected.Definition,
                0.715f, 0.18f, 0.61f);

            if (remainingCards.Count == 0)
            {
                AddText(contentRoot, "No cards remain in this Deck.", 0.16f, 0.43f, 0.45f, 0.53f,
                    18, mutedColor, TextAnchor.MiddleCenter);
                return;
            }

            for (int i = 0; i < remainingCards.Count; i++)
            {
                var item = remainingCards[i];
                float chance = 100f * item.Copies / deckOwner.Deck.Count;
                string label = item.Card.Definition.DisplayName + "  •  " + CardTypeLabel(item.Card.Definition) +
                    "  •  " + item.Copies + " remaining  •  " + chance.ToString("0.0") + "% draw chance";
                AddScrollListButton(list, label, i, rowPixelHeight,
                    CardFrameColor(item.Card.Definition), () =>
                    {
                        deckScrollPosition = scroll.verticalNormalizedPosition;
                        selectedDeckCard = item.Card;
                        Render();
                    }, 12);
                AttachInlineCardPreviewHover(list.GetChild(list.childCount - 1).GetComponent<RectTransform>(),
                    item.Card.Definition, () =>
                    {
                        CardInstance previewCard = deckOwner == null
                            ? null
                            : selectedDeckCard != null && deckOwner.Deck.Contains(selectedDeckCard)
                                ? selectedDeckCard : deckOwner.Deck.FirstOrDefault();
                        return previewCard == null ? null : previewCard.Definition;
                    },
                    definition => UpdatePortraitCardPreview(deckPreviewRoot, definition));
            }
        }

        private Scrollbar CreateVerticalScrollbar(float xMin, float yMin, float xMax, float yMax)
        {
            RectTransform track = CreateRect("Deck Scrollbar", contentRoot);
            SetAnchors(track, xMin, yMin, xMax, yMax);
            Image trackImage = track.gameObject.AddComponent<Image>();
            trackImage.color = new Color(0.035f, 0.055f, 0.085f);

            RectTransform handle = CreateRect("Handle", track);
            handle.anchorMin = Vector2.zero;
            handle.anchorMax = Vector2.one;
            handle.offsetMin = new Vector2(2f, 2f);
            handle.offsetMax = new Vector2(-2f, -2f);
            Image handleImage = handle.gameObject.AddComponent<Image>();
            handleImage.color = accentColor;

            Scrollbar scrollbar = track.gameObject.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect = handle;
            scrollbar.targetGraphic = handleImage;
            return scrollbar;
        }

        private void DrawGraveyardOverlay()
        {
            AddPanel(contentRoot, 0, 0.09f, 1, 0.9f, new Color(0.01f, 0.02f, 0.035f, 0.88f));
            AddPanel(contentRoot, 0.08f, 0.12f, 0.92f, 0.88f, panelColor);
            AddText(contentRoot, graveyardOwner.Name + " GRAVEYARD  •  " + graveyardOwner.Graveyard.Count + " cards",
                0.12f, 0.81f, 0.72f, 0.86f, 22, Color.white, TextAnchor.MiddleLeft, true);
            AddButton(contentRoot, "CLOSE", 0.82f, 0.81f, 0.88f, 0.86f, new Color(0.36f, 0.22f, 0.24f),
                () =>
                {
                    graveyardOwner = null;
                    selectedGraveyardCard = null;
                    Render();
                }, 12);

            RectTransform viewport = AddPanel(contentRoot, 0.12f, 0.17f, 0.45f, 0.79f, backgroundColor);
            viewport.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            ScrollRect scroll = viewport.gameObject.AddComponent<WheelScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            ConfigureMouseWheelScrolling(scroll);

            RectTransform list = CreateRect("Graveyard Card List", viewport);
            list.anchorMin = new Vector2(0, 1);
            list.anchorMax = new Vector2(1, 1);
            list.pivot = new Vector2(0.5f, 1);
            list.sizeDelta = new Vector2(0, graveyardOwner.Graveyard.Count * 58f);
            scroll.content = list;
            scroll.viewport = viewport;
            scroll.verticalNormalizedPosition = graveyardScrollPosition;
            scroll.onValueChanged.AddListener(_ =>
                graveyardScrollPosition = scroll.verticalNormalizedPosition);
            scroll.verticalScrollbar = CreateVerticalScrollbar(0.455f, 0.17f, 0.475f, 0.79f);
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            for (int i = 0; i < graveyardOwner.Graveyard.Count; i++)
            {
                CardInstance card = graveyardOwner.Graveyard[graveyardOwner.Graveyard.Count - 1 - i];
                RectTransform row = AddScrollListButton(list,
                    card.Definition.DisplayName + "  •  " + CardTypeLabel(card.Definition),
                    i, 58f, CardFrameColor(card.Definition),
                    () =>
                    {
                        graveyardScrollPosition = scroll.verticalNormalizedPosition;
                        selectedGraveyardCard = card;
                        Render();
                    }, 12);
                AttachInlineCardPreviewHover(row, card.Definition,
                    () =>
                    {
                        if (graveyardOwner == null)
                            return null;
                        CardInstance previewCard = selectedGraveyardCard != null &&
                            graveyardOwner.Graveyard.Contains(selectedGraveyardCard)
                                ? selectedGraveyardCard : graveyardOwner.Graveyard.LastOrDefault();
                        return previewCard == null ? null : previewCard.Definition;
                    },
                    definition => UpdatePortraitCardPreview(graveyardPreviewRoot, definition));
            }

            CardInstance selected = selectedGraveyardCard != null && graveyardOwner.Graveyard.Contains(selectedGraveyardCard)
                ? selectedGraveyardCard : graveyardOwner.Graveyard.LastOrDefault();
            graveyardPreviewRoot = CreatePortraitCardPreview(selected == null ? null : selected.Definition,
                0.685f, 0.18f, 0.61f);
            if (selected == null)
            {
                return;
            }
        }

        private RectTransform CreatePortraitCardPreview(CardDefinition definition, float centerX, float bottom, float height)
        {
            float width = GetPortraitWidth(height);
            float left = centerX - width * 0.5f;
            RectTransform preview = CreateRect("Inline Card Preview", contentRoot);
            SetAnchors(preview, left, bottom, left + width, bottom + height);
            UpdatePortraitCardPreview(preview, definition);
            return preview;
        }

        private void UpdatePortraitCardPreview(RectTransform preview, CardDefinition definition)
        {
            if (preview == null)
                return;

            for (int i = preview.childCount - 1; i >= 0; i--)
                Destroy(preview.GetChild(i).gameObject);
            if (definition == null)
            {
                AddText(preview, "No card selected.", 0.05f, 0.4f, 0.95f, 0.6f,
                    16, mutedColor, TextAnchor.MiddleCenter);
                return;
            }

            AddPanel(preview, 0, 0, 1, 1, CardFrameColor(definition));
            AddPanel(preview, 0.018f, 0.012f, 0.982f, 0.988f, new Color(0.055f, 0.075f, 0.105f));
            AddText(preview, definition.DisplayName, 0.055f, 0.89f, 0.945f, 0.97f, 15,
                Color.white, TextAnchor.MiddleCenter, true);
            AddText(preview, CardTypeLabel(definition), 0.055f, 0.84f, 0.945f, 0.89f, 11,
                mutedColor, TextAnchor.MiddleCenter);

            AddPanel(preview, 0.09f, 0.39f, 0.91f, 0.82f,
                Color.Lerp(backgroundColor, CardFrameColor(definition), 0.52f));

            string details = definition.IsMonster
                ? CardMonsterDetails(definition) + "\n\n"
                : "";
            details += "EFFECT\n" + definition.RulesSummary;
            AddText(preview, details, 0.07f, 0.045f, 0.93f, 0.36f,
                12, Color.white, TextAnchor.MiddleCenter);
        }

        private void AttachInlineCardPreviewHover(
            RectTransform row,
            CardDefinition definition,
            Func<CardDefinition> selectedDefinition,
            Action<CardDefinition> updatePreview)
        {
            EventTrigger trigger = row.GetComponent<EventTrigger>();
            if (trigger == null)
                trigger = row.gameObject.AddComponent<EventTrigger>();

            EventTrigger.Entry enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => updatePreview(definition));
            trigger.triggers.Add(enter);

            EventTrigger.Entry exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => updatePreview(selectedDefinition()));
            trigger.triggers.Add(exit);
        }

        private string CardLabel(CardInstance card, bool hidden)
        {
            if (!card.FaceUp)
                return "FACE-DOWN\nDEFENSE";

            if (!card.Definition.IsMonster)
                return card.Definition.DisplayName + "\nFACE-UP";

            string position = card.AttackPosition
                ? "FACE-UP ATK"
                : "FACE-UP DEF";
            return card.Definition.DisplayName + "\nATK " + card.CurrentAttack + " / DEF " +
                   card.Definition.Defense + "\n" + position;
        }

        private void OnZoneClicked(CardInstance card, bool opponent)
        {
            if (card == null)
                return;
            if (opponent)
            {
                if (engine.Phase == DuelPhase.Battle && selectedAttacker != null)
                {
                    engine.TryAttack(selectedAttacker, card);
                    selectedAttacker = null;
                    selectedTarget = null;
                }
                else
                    selectedTarget = card;
            }
            else if (engine.Phase == DuelPhase.Battle)
            {
                selectedAttacker = card;
                selectedMonsterCard = card;
            }
            else
            {
                selectedMonsterCard = card;
                if (!selectedMaterials.Add(card))
                    selectedMaterials.Remove(card);
            }
            Render();
        }

        private void OnSpellTrapZoneClicked(CardInstance card)
        {
            if (IsSelectingSpellTrapTarget())
            {
                if (card == null)
                {
                    selectedTarget = null;
                    notice = "That Spell/Trap Zone is empty. Choose an occupied Spell/Trap or Field Spell.";
                    Render();
                    return;
                }

                selectedTarget = card;
                notice = "";
                Render();
                return;
            }
            if (card == null)
                return;
            selectedTarget = card;
            Render();
        }

        private void DrawActionBar()
        {
            AddPanel(contentRoot, 0.02f, 0.135f, 0.98f, 0.185f, panelColor);
            contextActionButtons.Clear();
            if (engine.Phase == DuelPhase.GameOver)
                return;

            float actionX = 0.03f;
            if (engine.Phase == DuelPhase.MainOne || engine.Phase == DuelPhase.MainTwo)
            {
                if (selectedHandCard != null && selectedHandCard.Definition.IsMonster)
                {
                    if (!selectedHandCard.Definition.CanNormalSummon)
                    {
                        AddContextAction(ref actionX, "SPECIAL SUMMON REQUIRED",
                            new Color(0.2f, 0.25f, 0.31f), () => { });
                    }
                    else if (selectedHandCard.Definition.Effect == CardEffect.Berserkion ||
                        selectedHandCard.Definition.Effect == CardEffect.Valkyrion)
                    {
                        AddContextAction(ref actionX, "SPECIAL SUMMON", accentColor, () => Act(() =>
                            engine.TrySpecialSummonBoss(selectedHandCard)));
                    }
                    else
                    {
                        int tributeCount = selectedHandCard.Definition.Level >= 7 ? 2 : selectedHandCard.Definition.Level >= 5 ? 1 : 0;
                        string summonLabel = tributeCount == 0 ? "NORMAL SUMMON" : "TRIBUTE SUMMON (" + tributeCount + ")";
                        AddContextAction(ref actionX, summonLabel, accentColor, () => Act(() =>
                            engine.TryNormalSummon(selectedHandCard,
                                selectedMaterials.Where(card => engine.Human.MonsterZones.Contains(card)).ToList())));
                        AddContextAction(ref actionX, "SET MONSTER", new Color(0.2f, 0.34f, 0.46f), () => Act(() =>
                            engine.TryNormalSummon(selectedHandCard,
                                selectedMaterials.Where(card => engine.Human.MonsterZones.Contains(card)).ToList(), true)));
                    }
                }
                else if (selectedHandCard != null &&
                         (selectedHandCard.Definition.Kind == CardKind.Spell || selectedHandCard.Definition.Kind == CardKind.Trap))
                {
                    if (selectedHandCard.Definition.Kind == CardKind.Spell)
                    {
                        if (selectedHandCard.Definition.Effect == CardEffect.None)
                            AddContextAction(ref actionX, "EFFECT NOT IMPLEMENTED",
                                new Color(0.2f, 0.25f, 0.31f), () => { });
                        else if (selectedHandCard.Definition.Effect == CardEffect.FusionSummon)
                        {
                            if (selectedSynchro != null && selectedSynchro.Kind == CardKind.Fusion &&
                                engine.CanFusionSummon(selectedSynchro, selectedMaterials.ToList(), engine.Human))
                                AddContextAction(ref actionX, "FUSION SUMMON", accentColor, () => Act(() =>
                                    engine.TryFusionSummon(selectedSynchro, selectedMaterials.ToList())));
                            else
                                AddContextAction(ref actionX, "SELECT FUSION + MATERIALS",
                                    new Color(0.2f, 0.34f, 0.46f), () => { });
                        }
                        else
                        {
                            CardInstance effectTarget = selectedHandCard.Definition.Effect == CardEffect.MysticBox ||
                                selectedHandCard.Definition.Effect == CardEffect.DestroySpellTrap
                                ? selectedTarget
                                : selectedMonsterCard;
                            string actionLabel = selectedHandCard.Definition.Effect == CardEffect.DestroySpellTrap &&
                                selectedTarget != null
                                ? "DESTROY SELECTED"
                                : "ACTIVATE SPELL";
                            AddContextAction(ref actionX, actionLabel, accentColor, () => Act(() =>
                                engine.TryActivate(selectedHandCard, effectTarget)));
                        }
                    }
                    if (selectedHandCard.Definition.Subtype != CardSubtype.Field)
                        AddContextAction(ref actionX, "SET BACKROW", new Color(0.2f, 0.34f, 0.46f), () => Act(() =>
                            engine.TrySetBackrow(selectedHandCard)));
                }

                if (selectedHandCard == null && selectedMonsterCard != null && selectedMonsterCard.FaceUp)
                {
                    if (selectedMonsterCard.Definition.Effect != CardEffect.None)
                        AddContextAction(ref actionX, "ACTIVATE EFFECT", new Color(0.2f, 0.34f, 0.46f), () => Act(() =>
                            engine.TryActivate(selectedMonsterCard, selectedMonsterCard)));
                    AddContextAction(ref actionX, "CHANGE POSITION", new Color(0.2f, 0.34f, 0.46f), () => Act(() =>
                        engine.TryChangePosition(selectedMonsterCard)));
                }

                if (selectedMonsterCard != null && !selectedMonsterCard.FaceUp &&
                    engine.CanFlipSummon(selectedMonsterCard))
                    AddContextAction(ref actionX, "FLIP SUMMON", accentColor, () => Act(() =>
                        engine.TryFlipSummon(selectedMonsterCard)));

                if (selectedSynchro != null && engine.CanSynchroSummon(selectedSynchro, selectedMaterials.ToList(), engine.Human))
                    AddContextAction(ref actionX, "SYNCHRO SUMMON", new Color(0.44f, 0.24f, 0.56f), () => Act(() =>
                        engine.TrySynchroSummon(selectedSynchro, selectedMaterials.ToList())));
            }
            else if (engine.Phase == DuelPhase.Battle)
            {
                if (selectedHandCard != null && selectedHandCard.Definition.Kind == CardKind.Spell &&
                    selectedHandCard.Definition.Subtype == CardSubtype.QuickPlay)
                {
                    if (selectedHandCard.Definition.Effect == CardEffect.None)
                        AddContextAction(ref actionX, "EFFECT NOT IMPLEMENTED",
                            new Color(0.2f, 0.25f, 0.31f), () => { });
                    else
                    {
                        string quickPlayLabel = selectedHandCard.Definition.Effect == CardEffect.DestroySpellTrap &&
                            selectedTarget != null
                            ? "DESTROY SELECTED"
                            : "ACTIVATE QUICK-PLAY";
                        AddContextAction(ref actionX, quickPlayLabel, accentColor, () => Act(() =>
                            engine.TryActivate(selectedHandCard,
                                selectedHandCard.Definition.Effect == CardEffect.DestroySpellTrap
                                    ? selectedTarget
                                    : selectedMonsterCard)));
                    }
                }
                if (!engine.CanAttackThisTurn)
                    AddContextAction(ref actionX, "NO ATTACKS ON TURN 1", new Color(0.2f, 0.25f, 0.31f), () => { });
                else if (selectedAttacker != null && !engine.Opponent.MonsterZones.Any(card => card != null))
                    AddContextAction(ref actionX, "DIRECT ATTACK", new Color(0.58f, 0.22f, 0.22f), () => Act(() =>
                        engine.TryAttack(selectedAttacker, null)));
                else if (selectedAttacker != null)
                    AddContextAction(ref actionX, "SELECT AN OPPONENT MONSTER", new Color(0.2f, 0.34f, 0.46f), () => { });
                else
                    AddContextAction(ref actionX, "SELECT AN ATTACKER", new Color(0.2f, 0.34f, 0.46f), () => { });
            }
            else if (engine.Phase == DuelPhase.End && selectedHandCard != null &&
                     selectedHandCard.Definition.Kind == CardKind.Spell &&
                     selectedHandCard.Definition.Subtype == CardSubtype.QuickPlay)
            {
                if (selectedHandCard.Definition.Effect == CardEffect.None)
                    AddContextAction(ref actionX, "EFFECT NOT IMPLEMENTED",
                        new Color(0.2f, 0.25f, 0.31f), () => { });
                else
                {
                    string endPhaseQuickPlayLabel = selectedHandCard.Definition.Effect == CardEffect.DestroySpellTrap &&
                        selectedTarget != null
                        ? "DESTROY SELECTED"
                        : "ACTIVATE QUICK-PLAY";
                    AddContextAction(ref actionX, endPhaseQuickPlayLabel, accentColor, () => Act(() =>
                        engine.TryActivate(selectedHandCard,
                            selectedHandCard.Definition.Effect == CardEffect.DestroySpellTrap
                                ? selectedTarget
                                : selectedMonsterCard)));
                }
            }

            CenterContextActions();
        }

        private void AddContextAction(ref float x, string label, Color color, Action action)
        {
            const float width = 0.145f;
            contextActionButtons.Add(AddButton(contentRoot, label, x, 0.135f, x + width, 0.185f,
                color, action, 11));
            x += width + 0.012f;
        }

        private void CenterContextActions()
        {
            if (contextActionButtons.Count == 0)
                return;

            RectTransform first = contextActionButtons[0];
            RectTransform last = contextActionButtons[contextActionButtons.Count - 1];
            float totalWidth = last.anchorMax.x - first.anchorMin.x;
            float offset = (1f - totalWidth) * 0.5f - first.anchorMin.x;
            foreach (RectTransform button in contextActionButtons)
            {
                Vector2 min = button.anchorMin;
                Vector2 max = button.anchorMax;
                min.x += offset;
                max.x += offset;
                button.anchorMin = min;
                button.anchorMax = max;
            }
        }

        private void DrawExtraDeck()
        {
            if (engine.Phase == DuelPhase.GameOver)
                return;
            AddButton(contentRoot, "EXTRA DECK\n" + engine.Human.ExtraDeck.Count + " cards",
                0.86f, 0.018f, 0.975f, 0.13f, new Color(0.25f, 0.2f, 0.38f),
                () =>
                {
                    extraDeckOpen = true;
                    Render();
                }, 12);
        }

        private void DrawExtraDeckOverlay()
        {
            AddPanel(contentRoot, 0, 0.09f, 1, 0.9f, new Color(0.01f, 0.02f, 0.035f, 0.88f));
            AddPanel(contentRoot, 0.08f, 0.12f, 0.92f, 0.88f, panelColor);
            AddText(contentRoot, "EXTRA DECK  •  " + engine.Human.ExtraDeck.Count + " cards",
                0.12f, 0.82f, 0.72f, 0.87f, 22, Color.white, TextAnchor.MiddleLeft, true);
            AddButton(contentRoot, "CLOSE", 0.82f, 0.82f, 0.88f, 0.87f,
                new Color(0.36f, 0.22f, 0.24f), () =>
                {
                    extraDeckOpen = false;
                    Render();
                }, 12);

            List<CardInstance> extra = engine.Human.ExtraDeck;
            if (extra.Count == 0)
            {
                AddText(contentRoot, "Your Extra Deck is empty.", 0.2f, 0.42f, 0.8f, 0.55f,
                    18, mutedColor, TextAnchor.MiddleCenter);
                return;
            }

            const int columns = 5;
            const float cardWidth = 0.078f;
            const float columnGap = 0.018f;
            const float rowGap = 0.018f;
            float cardHeight = GetPortraitHeight(cardWidth);
            int rows = (extra.Count + columns - 1) / columns;
            float gridWidth = columns * cardWidth + (columns - 1) * columnGap;
            float gridHeight = rows * cardHeight + (rows - 1) * rowGap;
            float startX = 0.5f - gridWidth * 0.5f;
            float startY = 0.78f - (0.66f - gridHeight) * 0.5f;

            for (int i = 0; i < extra.Count; i++)
            {
                CardDefinition definition = extra[i].Definition;
                int column = i % columns;
                int row = i / columns;
                float x = startX + column * (cardWidth + columnGap);
                float top = startY - row * (cardHeight + rowGap);
                RectTransform cardButton = AddButton(contentRoot,
                    definition.DisplayName + "\nLV " + definition.Level,
                    x, top - cardHeight, x + cardWidth, top,
                    selectedSynchro == definition
                        ? Color.Lerp(CardFrameColor(definition), accentColor, 0.5f)
                        : CardFrameColor(definition),
                    () =>
                    {
                        selectedSynchro = definition;
                        selectedMaterials.Clear();
                        extraDeckOpen = false;
                        Render();
                    }, 10, definition);
                AddCardArtPlaceholder(cardButton, definition, 0f, 0f, 1f, 1f, true);
                Text label = cardButton.Find("Label").GetComponent<Text>();
                SetAnchors(label.rectTransform, 0.02f, 0.02f, 0.98f, 0.28f);
                label.alignment = TextAnchor.MiddleCenter;
            }
        }

        private void DrawHand()
        {
            if (engine.Phase == DuelPhase.GameOver)
                return;
            int count = engine.Human.Hand.Count;
            if (count == 0)
            {
                AddText(contentRoot, "Your hand is empty.", 0.03f, 0.04f, 0.52f, 0.1f, 14, mutedColor, TextAnchor.MiddleCenter);
                return;
            }
            float baseWidth = GetPortraitWidth(HandCardHeight);
            float gap = Math.Min(HandCardGap, HandMaxWidth / Math.Max(1, count - 1));
            float width = Math.Min(baseWidth, (HandMaxWidth - (count - 1) * gap) / count);
            float height = GetPortraitHeight(width);
            float totalWidth = count * width + (count - 1) * gap;
            float start = (1f - totalWidth) * 0.5f;
            for (int i = 0; i < count; i++)
            {
                CardInstance card = engine.Human.Hand[i];
                float x = start + i * (width + gap);
                RectTransform cardButton = AddButton(contentRoot, CardLabel(card, false), x, 0.018f, x + width, 0.018f + height,
                    selectedHandCard == card || selectedMaterials.Contains(card)
                        ? Color.Lerp(CardFrameColor(card.Definition), accentColor, 0.5f)
                        : CardFrameColor(card.Definition),
                    () =>
                    {
                        if (selectedSynchro != null && selectedSynchro.Kind == CardKind.Fusion &&
                            selectedHandCard != null && selectedHandCard.Definition.Effect == CardEffect.FusionSummon &&
                            card != selectedHandCard)
                        {
                            if (!selectedMaterials.Add(card))
                                selectedMaterials.Remove(card);
                            selectedMonsterCard = null;
                            Render();
                            return;
                        }
                        if (selectedHandCard == card)
                        {
                            selectedHandCard = null;
                            selectedMaterials.Clear();
                            Render();
                            return;
                        }
                        selectedHandCard = card;
                        selectedTarget = null;
                        notice = "";
                        selectedMonsterCard = null;
                        if (card.Definition.Effect != CardEffect.FusionSummon)
                        {
                            selectedSynchro = null;
                            selectedMaterials.Clear();
                        }
                        Render();
                    }, 10, card.Definition);
                AddCardArtPlaceholder(cardButton, card.Definition, 0f, 0f, 1f, 1f, true);
                Text label = cardButton.Find("Label").GetComponent<Text>();
                SetAnchors(label.rectTransform, 0.02f, 0.02f, 0.98f, 0.28f);
                label.alignment = TextAnchor.MiddleCenter;
            }
        }

        private void DrawDuelLog()
        {
            if (screen == ScreenMode.TestLab && mockDuelSimulator != null)
            {
                string logLine = engine.Messages.LastOrDefault() ?? "No duel actions logged yet.";
                AddText(contentRoot, logLine, 0.025f, 0.005f, 0.47f, 0.035f,
                    10, mutedColor, TextAnchor.MiddleLeft);
                AddText(contentRoot, mockDuelSimulator.ManualResult, 0.48f, 0.005f, 0.62f, 0.035f,
                    9, mockDuelSimulator.ManualResult == "PENDING" ? new Color(1f, 0.78f, 0.35f) :
                        mockDuelSimulator.ManualResult == "MANUAL PASS" ? accentColor : new Color(1f, 0.38f, 0.34f),
                    TextAnchor.MiddleCenter, true);
                AddButton(contentRoot, "PASS", 0.63f, 0.004f, 0.735f, 0.036f,
                    new Color(0.12f, 0.42f, 0.32f), () =>
                    {
                        mockDuelSimulator.MarkPassed();
                        notice = "";
                        Render();
                    }, 9);
                AddButton(contentRoot, "FAIL", 0.745f, 0.004f, 0.85f, 0.036f,
                    new Color(0.48f, 0.2f, 0.22f), () =>
                    {
                        mockDuelSimulator.MarkFailed();
                        notice = "";
                        Render();
                    }, 9);
                AddButton(contentRoot, "NEW TEST", 0.86f, 0.004f, 0.975f, 0.036f,
                    new Color(0.2f, 0.34f, 0.46f), ReturnToMockDuelSetup, 9);
                return;
            }
            if (!string.IsNullOrEmpty(notice))
            {
                AddText(contentRoot, notice, 0.025f, 0.005f, 0.98f, 0.035f, 12, new Color(1f, 0.78f, 0.35f), TextAnchor.MiddleLeft);
                return;
            }
            string log = string.Join("\n", engine.Messages.Reverse().Take(3).Reverse());
            AddText(contentRoot, log, 0.025f, 0.005f, 0.98f, 0.035f, 11, mutedColor, TextAnchor.MiddleLeft);
        }

        private void DrawDuelResultsOverlay()
        {
            AddPanel(contentRoot, 0, 0, 1, 1, new Color(0.01f, 0.02f, 0.035f, 0.86f));
            AddPanel(contentRoot, 0.1f, 0.07f, 0.9f, 0.91f, panelColor);

            bool won = engine.Winner == engine.Human.Name;
            string result = won ? "VICTORY" : "DEFEAT";
            Color resultColor = won ? accentColor : new Color(1f, 0.38f, 0.34f);
            AddText(contentRoot, result, 0.16f, 0.82f, 0.84f, 0.89f, 30,
                resultColor, TextAnchor.MiddleCenter, true);
            AddText(contentRoot, engine.Winner + " wins  •  Turn " + engine.TurnNumber,
                0.16f, 0.77f, 0.84f, 0.82f, 17, Color.white, TextAnchor.MiddleCenter);
            AddText(contentRoot,
                engine.Human.Name + "  " + engine.Human.LifePoints + " LP     |     " +
                engine.Opponent.Name + "  " + engine.Opponent.LifePoints + " LP",
                0.16f, 0.72f, 0.84f, 0.77f, 15, mutedColor, TextAnchor.MiddleCenter);
            AddText(contentRoot, "DUEL LOG  •  " + engine.Messages.Count + " events",
                0.19f, 0.68f, 0.79f, 0.72f, 14, accentColor, TextAnchor.MiddleLeft, true);

            RectTransform viewport = AddPanel(contentRoot, 0.19f, 0.23f, 0.79f, 0.67f, backgroundColor);
            viewport.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            ScrollRect scroll = viewport.gameObject.AddComponent<WheelScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            ConfigureMouseWheelScrolling(scroll);

            const float rowHeight = 38f;
            RectTransform list = CreateRect("Duel Results Log", viewport);
            list.anchorMin = new Vector2(0, 1);
            list.anchorMax = new Vector2(1, 1);
            list.pivot = new Vector2(0.5f, 1);
            list.sizeDelta = new Vector2(0, engine.Messages.Count * rowHeight);
            scroll.content = list;
            scroll.viewport = viewport;

            for (int i = 0; i < engine.Messages.Count; i++)
            {
                Color rowColor = i % 2 == 0
                    ? new Color(0.1f, 0.14f, 0.19f)
                    : new Color(0.075f, 0.105f, 0.15f);
                AddScrollListButton(list, (i + 1).ToString("00") + "   " + engine.Messages[i],
                    i, rowHeight, rowColor, () => { }, 12);
            }

            scroll.verticalScrollbar = CreateVerticalScrollbar(0.8f, 0.23f, 0.815f, 0.67f);
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            scroll.verticalNormalizedPosition = 0;

            AddButton(contentRoot, "REMATCH", 0.34f, 0.105f, 0.66f, 0.19f, accentColor, () =>
            {
                StartDuel();
                Render();
            }, 20);
        }

        private string PhasePrompt()
        {
            if (!engine.IsHumanActionWindow)
                return "Opponent is resolving its turn.";
            switch (engine.Phase)
            {
                case DuelPhase.Draw:
                    return "Draw Phase. Review your hand, then select Standby or a later phase.";
                case DuelPhase.Standby:
                    return "Standby Phase. Select Main 1 when ready.";
                case DuelPhase.MainOne:
                    if (selectedHandCard != null && selectedHandCard.Definition.IsMonster)
                    {
                        if (!selectedHandCard.Definition.CanNormalSummon)
                            return "This card cannot be Normal Summoned; it needs its listed Special Summon procedure.";
                        int tributes = selectedHandCard.Definition.Level >= 7 ? 2 :
                            selectedHandCard.Definition.Level >= 5 ? 1 : 0;
                        if (tributes > 0)
                            return "Select exactly " + tributes + " monster(s) as Tribute, then choose Summon or Set.";
                    }
                    if (selectedHandCard != null &&
                        selectedHandCard.Definition.Effect == CardEffect.MysticBox &&
                        selectedTarget == null)
                        return "Select an opposing monster for Mystic Box, then activate the Spell.";
                    if (selectedHandCard != null &&
                        selectedHandCard.Definition.Effect == CardEffect.DestroySpellTrap)
                        return selectedTarget == null
                            ? "Choose Mystical Space Typhoon, then hover and click an occupied Spell/Trap or Field Spell to target it."
                            : "Target selected: " + selectedTarget.Definition.DisplayName +
                              ". Choose DESTROY SELECTED to resolve Mystical Space Typhoon.";
                    if (selectedHandCard != null &&
                        selectedHandCard.Definition.Effect == CardEffect.FusionSummon &&
                        selectedSynchro == null)
                        return "Open the Extra Deck and select a Fusion Monster.";
                    if (selectedSynchro != null)
                        return selectedSynchro.Kind == CardKind.Fusion
                            ? "Select its required Fusion Materials from your hand or field, then Fusion Summon."
                            : "Select face-up Tuner and non-Tuner materials totaling Level " + selectedSynchro.Level + ".";
                    return "Select a hand card or field monster, then choose an action.";
                case DuelPhase.Battle:
                    if (selectedHandCard != null &&
                        selectedHandCard.Definition.Effect == CardEffect.DestroySpellTrap)
                        return selectedTarget == null
                            ? "Hover and click an occupied Spell/Trap or Field Spell, then activate the Quick-Play Spell."
                            : "Target selected: " + selectedTarget.Definition.DisplayName +
                              ". Choose DESTROY SELECTED to resolve the Quick-Play Spell.";
                    if (!engine.CanAttackThisTurn)
                        return engine.AttackRestrictionMessage + " Advance to Main Phase 2 when ready.";
                    return selectedAttacker == null
                        ? "Select an Attack Position monster, then choose an opposing target."
                        : "Attacker selected. Choose an opposing monster, or press ATTACK DIRECT.";
                case DuelPhase.MainTwo:
                    return "Play more cards, or advance to the End Phase.";
                case DuelPhase.End:
                    if (selectedHandCard != null &&
                        selectedHandCard.Definition.Effect == CardEffect.DestroySpellTrap)
                        return selectedTarget == null
                            ? "Hover and click an occupied Spell/Trap or Field Spell, then activate the Quick-Play Spell."
                            : "Target selected: " + selectedTarget.Definition.DisplayName +
                              ". Choose DESTROY SELECTED to resolve the Quick-Play Spell.";
                    return HasEndPhaseAction()
                        ? "Activate a Quick-Play Spell if needed, or pass the turn."
                        : "No End Phase effects are available. The next turn begins automatically.";
                default:
                    return "";
            }
        }

        private void Act(Func<bool> action)
        {
            bool succeeded = action();
            if (succeeded)
            {
                selectedHandCard = null;
                selectedMonsterCard = null;
                selectedAttacker = null;
                selectedTarget = null;
                selectedMaterials.Clear();
                selectedSynchro = null;
            }
            Render();
        }

        private void ClearSelection()
        {
            selectedHandCard = null;
            selectedMonsterCard = null;
            selectedAttacker = null;
            selectedTarget = null;
            selectedSynchro = null;
            selectedMaterials.Clear();
        }

        private void DrawDeckScreen()
        {
            int mainCount = profile.deck.Select(StarterCatalog.Find)
                .Count(card => card != null && card.CanBeMainDeck);
            int extraCount = profile.deck.Select(StarterCatalog.Find)
                .Count(card => card != null && card.CanBeExtraDeck);
            bool valid = mainCount >= 20 && mainCount <= 40 && extraCount <= 15;
            AddText(contentRoot, "DECK BUILDER", 0.035f, 0.84f, 0.38f, 0.89f,
                24, Color.white, TextAnchor.MiddleLeft, true);
            AddText(contentRoot, "MAIN " + mainCount + "/40     EXTRA " + extraCount + "/15",
                0.44f, 0.845f, 0.7f, 0.89f, 13,
                valid ? accentColor : new Color(1f, 0.65f, 0.32f), TextAnchor.MiddleRight, true);
            AddText(contentRoot, valid ? "READY TO DUEL" : "MAIN DECK NEEDS 20–40 CARDS",
                0.71f, 0.845f, 0.965f, 0.89f, 12,
                valid ? accentColor : new Color(1f, 0.65f, 0.32f), TextAnchor.MiddleRight, true);

            AddPanel(contentRoot, 0.035f, 0.12f, 0.405f, 0.82f, panelColor);
            AddText(contentRoot, "YOUR DECK", 0.055f, 0.775f, 0.27f, 0.81f,
                17, Color.white, TextAnchor.MiddleLeft, true);
            AddText(contentRoot, mainCount + " main  •  " + extraCount + " extra",
                0.27f, 0.775f, 0.385f, 0.81f, 11, mutedColor, TextAnchor.MiddleRight);

            RectTransform deckViewport = AddPanel(contentRoot, 0.05f, 0.15f, 0.39f, 0.76f, backgroundColor);
            deckViewport.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            ScrollRect deckScroll = deckViewport.gameObject.AddComponent<WheelScrollRect>();
            deckScroll.horizontal = false;
            deckScroll.vertical = true;
            deckScroll.movementType = ScrollRect.MovementType.Clamped;
            ConfigureMouseWheelScrolling(deckScroll);

            List<CardDefinition> deckCards = profile.deck
                .Select(StarterCatalog.Find)
                .Where(card => card != null)
                .GroupBy(card => card.CardId)
                .Select(group => group.First())
                .OrderBy(card => card.IsExtraDeckCard)
                .ThenBy(card => card.DisplayName)
                .ToList();
            const float deckRowHeight = 48f;
            RectTransform deckList = CreateRect("Deck Card List", deckViewport);
            deckList.anchorMin = new Vector2(0, 1);
            deckList.anchorMax = new Vector2(1, 1);
            deckList.pivot = new Vector2(0.5f, 1);
            deckList.sizeDelta = new Vector2(0, deckCards.Count * deckRowHeight);
            deckScroll.content = deckList;
            deckScroll.viewport = deckViewport;
            for (int i = 0; i < deckCards.Count; i++)
                AddDeckListRow(deckList, deckCards[i], i, deckRowHeight);
            if (deckCards.Count == 0)
                AddText(contentRoot, "Your deck is empty.", 0.07f, 0.43f, 0.37f, 0.5f,
                    16, mutedColor, TextAnchor.MiddleCenter);

            AddText(contentRoot, "CARD LIBRARY", 0.435f, 0.775f, 0.68f, 0.81f,
                17, Color.white, TextAnchor.MiddleLeft, true);
            AddText(contentRoot, "Hover a card to preview it. Use + to add a copy.",
                0.435f, 0.745f, 0.965f, 0.78f, 11, mutedColor, TextAnchor.MiddleLeft);
            AddDeckSearchField(0.435f, 0.695f, 0.685f, 0.74f);
            AddButton(contentRoot, CardLibraryFilterLabel(cardLibraryFilter) + "  ▼",
                0.695f, 0.695f, 0.855f, 0.74f, CardFilterColor(cardLibraryFilter),
                () => { cardFilterOptionsOpen = !cardFilterOptionsOpen; Render(); }, 11);
            AddButton(contentRoot, "SORT: " + DeckSortLabel(),
                0.865f, 0.695f, 0.965f, 0.74f, new Color(0.2f, 0.34f, 0.46f),
                () => { deckSortMode = (deckSortMode + 1) % 4; Render(); }, 10);

            List<CardDefinition> libraryCards = StarterCatalog.All
                .Where(card => card != null && !string.IsNullOrWhiteSpace(card.CardId))
                .GroupBy(card => card.CardId)
                .Select(group => group.Last())
                .Where(MatchesCardLibraryFilter)
                .Where(MatchesDeckSearch)
                .ToList();
            SortDeckLibrary(libraryCards);
            int deckPageCount = Mathf.Max(1, Mathf.CeilToInt(libraryCards.Count / (float)LibraryPageSize));
            deckLibraryPage = Mathf.Clamp(deckLibraryPage, 0, deckPageCount - 1);
            AddText(contentRoot, libraryCards.Count + " cards  •  " +
                (deckLibraryPage + 1) + "/" + deckPageCount + " pages",
                0.77f, 0.75f, 0.965f, 0.78f, 11, mutedColor, TextAnchor.MiddleRight);

            RectTransform libraryViewport = AddPanel(contentRoot, 0.435f, 0.18f, 0.965f, 0.68f, backgroundColor);
            libraryViewport.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            ScrollRect libraryScroll = libraryViewport.gameObject.AddComponent<WheelScrollRect>();
            libraryScroll.horizontal = false;
            libraryScroll.vertical = true;
            libraryScroll.movementType = ScrollRect.MovementType.Clamped;
            ConfigureMouseWheelScrolling(libraryScroll);
            const float libraryRowHeight = 54f;
            List<CardDefinition> visibleLibraryCards = libraryCards
                .Skip(deckLibraryPage * LibraryPageSize)
                .Take(LibraryPageSize)
                .ToList();
            RectTransform libraryList = CreateRect("Card Library List", libraryViewport);
            libraryList.anchorMin = new Vector2(0, 1);
            libraryList.anchorMax = new Vector2(1, 1);
            libraryList.pivot = new Vector2(0.5f, 1);
            libraryList.sizeDelta = new Vector2(0, visibleLibraryCards.Count * libraryRowHeight);
            libraryScroll.content = libraryList;
            libraryScroll.viewport = libraryViewport;
            for (int i = 0; i < visibleLibraryCards.Count; i++)
                AddDeckLibraryRow(libraryList, visibleLibraryCards[i], i, libraryRowHeight);
            if (libraryCards.Count == 0)
                AddText(contentRoot, "No cards match these filters.", 0.46f, 0.4f, 0.94f, 0.48f,
                    15, mutedColor, TextAnchor.MiddleCenter);

            AddButton(contentRoot, "<", 0.435f, 0.125f, 0.49f, 0.165f,
                new Color(0.2f, 0.34f, 0.46f), () =>
                {
                    deckLibraryPage--;
                    Render();
                }, 12);
            AddText(contentRoot, "PAGE " + (deckLibraryPage + 1) + " / " + deckPageCount,
                0.5f, 0.125f, 0.9f, 0.165f, 11, mutedColor, TextAnchor.MiddleCenter);
            AddButton(contentRoot, ">", 0.91f, 0.125f, 0.965f, 0.165f,
                new Color(0.2f, 0.34f, 0.46f), () =>
                {
                    deckLibraryPage++;
                    Render();
                }, 12);

            if (!string.IsNullOrEmpty(notice))
                AddText(contentRoot, notice, 0.435f, 0.085f, 0.965f, 0.115f,
                    12, new Color(1f, 0.78f, 0.35f), TextAnchor.MiddleLeft);
            if (cardFilterOptionsOpen)
                DrawDeckFilterOptions();
        }

        private void AddDeckListRow(RectTransform parent, CardDefinition card, int index, float rowHeight)
        {
            int count = profile.deck.Count(id => id == card.CardId);
            float top = -index * rowHeight;
            RectTransform row = AddPanel(parent, 0, 0, 1, 1,
                index % 2 == 0 ? new Color(0.1f, 0.14f, 0.19f) : new Color(0.075f, 0.105f, 0.15f));
            row.anchorMin = new Vector2(0, 1);
            row.anchorMax = new Vector2(1, 1);
            row.pivot = new Vector2(0.5f, 1);
            row.sizeDelta = new Vector2(-8f, rowHeight - 3f);
            row.anchoredPosition = new Vector2(0, top);
            AttachCardHover(row, card);
            string kind = card.IsExtraDeckCard ? "EXTRA" : CardTypeLabel(card);
            AddText(row, card.DisplayName + "\n" + kind + "  •  " + count + " copy/copies",
                0.025f, 0.08f, 0.78f, 0.92f, 12, Color.white, TextAnchor.MiddleLeft, true);
            AddButton(row, "−", 0.82f, 0.16f, 0.97f, 0.84f,
                new Color(0.36f, 0.22f, 0.24f), () => ChangeDeck(card, -1), 15);
        }

        private void AddDeckLibraryRow(RectTransform parent, CardDefinition card, int index, float rowHeight)
        {
            int owned = GetOwned(card.CardId);
            int inDeck = profile.deck.Count(id => id == card.CardId);
            float top = -index * rowHeight;
            RectTransform row = AddPanel(parent, 0, 0, 1, 1,
                index % 2 == 0 ? new Color(0.1f, 0.14f, 0.19f) : new Color(0.075f, 0.105f, 0.15f));
            row.anchorMin = new Vector2(0, 1);
            row.anchorMax = new Vector2(1, 1);
            row.pivot = new Vector2(0.5f, 1);
            row.sizeDelta = new Vector2(-8f, rowHeight - 3f);
            row.anchoredPosition = new Vector2(0, top);
            AttachCardHover(row, card);
            string details = card.IsMonster
                ? CardTypeLabel(card) + "  •  " + card.Level + "  •  " +
                  card.AttackDisplay + "/" + card.DefenseDisplay
                : CardTypeLabel(card);
            AddText(row, card.DisplayName + "\n" + details + "  •  OWNED " + owned + "  •  IN DECK " + inDeck,
                0.025f, 0.08f, 0.82f, 0.92f, 11, Color.white, TextAnchor.MiddleLeft, true);
            AddButton(row, "+", 0.86f, 0.16f, 0.97f, 0.84f,
                new Color(0.14f, 0.38f, 0.34f), () => ChangeDeck(card, 1), 15);
        }

        private void AddDeckSearchField(float xMin, float yMin, float xMax, float yMax)
        {
            RectTransform root = AddPanel(contentRoot, xMin, yMin, xMax, yMax, new Color(0.1f, 0.14f, 0.19f));
            InputField input = root.gameObject.AddComponent<InputField>();
            input.lineType = InputField.LineType.SingleLine;
            input.targetGraphic = root.GetComponent<Image>();

            RectTransform textRect = CreateRect("Search Text", root);
            SetAnchors(textRect, 0.04f, 0.05f, 0.96f, 0.95f);
            Text text = textRect.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = 12;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;
            text.raycastTarget = false;
            input.textComponent = text;

            RectTransform placeholderRect = CreateRect("Search Placeholder", root);
            SetAnchors(placeholderRect, 0.04f, 0.05f, 0.96f, 0.95f);
            Text placeholder = placeholderRect.gameObject.AddComponent<Text>();
            placeholder.font = font;
            placeholder.fontSize = 12;
            placeholder.color = mutedColor;
            placeholder.text = "Search card names...";
            placeholder.alignment = TextAnchor.MiddleLeft;
            placeholder.raycastTarget = false;
            input.placeholder = placeholder;
            input.text = deckSearchQuery;
            input.onEndEdit.AddListener(value =>
            {
                if (deckSearchQuery == value)
                    return;
                deckSearchQuery = value;
                Render();
            });
        }

        private void DrawDeckFilterOptions()
        {
            CardLibraryFilter[] filters = (CardLibraryFilter[])Enum.GetValues(typeof(CardLibraryFilter));
            const float rowHeight = 0.034f;
            float top = 0.69f;
            float bottom = top - filters.Length * rowHeight - 0.008f;
            AddPanel(contentRoot, 0.695f, bottom, 0.855f, 0.695f, new Color(0.035f, 0.055f, 0.085f, 0.99f));
            for (int i = 0; i < filters.Length; i++)
            {
                CardLibraryFilter filter = filters[i];
                float rowTop = top - i * rowHeight;
                AddButton(contentRoot, CardLibraryFilterLabel(filter),
                    0.699f, rowTop - rowHeight + 0.001f, 0.851f, rowTop - 0.001f,
                    filter == cardLibraryFilter ? accentColor : CardFilterColor(filter),
                    () =>
                    {
                        cardLibraryFilter = filter;
                        cardFilterOptionsOpen = false;
                        Render();
                    }, 10);
            }
        }

        private bool MatchesDeckSearch(CardDefinition card)
        {
            return string.IsNullOrWhiteSpace(deckSearchQuery) ||
                   card.DisplayName.IndexOf(deckSearchQuery.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string DeckSortLabel()
        {
            switch (deckSortMode)
            {
                case 1: return "ATK";
                case 2: return "LEVEL";
                case 3: return "TYPE";
                default: return "NAME";
            }
        }

        private void SortDeckLibrary(List<CardDefinition> cards)
        {
            switch (deckSortMode)
            {
                case 1:
                    cards.Sort((left, right) =>
                    {
                        int result = right.Attack.CompareTo(left.Attack);
                        return result != 0 ? result : string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase);
                    });
                    break;
                case 2:
                    cards.Sort((left, right) =>
                    {
                        int result = right.Level.CompareTo(left.Level);
                        return result != 0 ? result : string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase);
                    });
                    break;
                case 3:
                    cards.Sort((left, right) =>
                    {
                        int result = left.Kind.CompareTo(right.Kind);
                        return result != 0 ? result : string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase);
                    });
                    break;
                default:
                    cards.Sort((left, right) => string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase));
                    break;
            }
        }

        private void DrawCardLibraryScreen()
        {
            List<CardDefinition> cards = StarterCatalog.All
                .Where(card => card != null && !string.IsNullOrWhiteSpace(card.CardId))
                .GroupBy(card => card.CardId)
                .Select(group => group.Last())
                .Where(MatchesCardLibraryFilter)
                .OrderBy(card => card.Kind)
                .ThenBy(card => card.DisplayName)
                .ToList();
            int cardPageCount = Mathf.Max(1, Mathf.CeilToInt(cards.Count / (float)LibraryPageSize));
            cardLibraryPage = Mathf.Clamp(cardLibraryPage, 0, cardPageCount - 1);

            AddText(contentRoot, "CARD LIBRARY", 0.025f, 0.84f, 0.34f, 0.89f, 24, Color.white, TextAnchor.MiddleLeft, true);
            AddText(contentRoot, cards.Count + " cards  •  " +
                (cardLibraryPage + 1) + "/" + cardPageCount + " pages",
                0.35f, 0.84f, 0.49f, 0.89f, 13, mutedColor, TextAnchor.MiddleCenter);
            AddButton(contentRoot, CardLibraryFilterLabel(cardLibraryFilter) + "  ▼",
                0.51f, 0.84f, 0.975f, 0.89f, new Color(0.16f, 0.24f, 0.33f),
                () => { cardFilterOptionsOpen = !cardFilterOptionsOpen; Render(); }, 13);
            RectTransform viewport = AddPanel(contentRoot, 0.025f, 0.12f, 0.455f, 0.82f, panelColor);
            Mask mask = viewport.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = true;
            ScrollRect scroll = viewport.gameObject.AddComponent<WheelScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            ConfigureMouseWheelScrolling(scroll);

            List<CardDefinition> visibleCards = cards
                .Skip(cardLibraryPage * LibraryPageSize)
                .Take(LibraryPageSize)
                .ToList();
            RectTransform listContent = CreateRect("Card Name List", viewport);
            listContent.anchorMin = new Vector2(0, 1);
            listContent.anchorMax = new Vector2(1, 1);
            listContent.pivot = new Vector2(0.5f, 1);
            listContent.anchoredPosition = Vector2.zero;
            const float rowHeight = 42f;
            listContent.sizeDelta = new Vector2(0, visibleCards.Count * rowHeight);
            scroll.content = listContent;
            scroll.viewport = viewport;

            for (int i = 0; i < visibleCards.Count; i++)
                AddCardLibraryRow(listContent, visibleCards[i], i, rowHeight);

            const float previewHeight = 0.7f;
            float previewWidth = GetPortraitWidth(previewHeight);
            float previewX = 0.48f + (0.975f - 0.48f - previewWidth) * 0.5f;
            RectTransform preview = AddPanel(contentRoot, previewX, 0.12f, previewX + previewWidth,
                0.12f + previewHeight, panelColor);
            cardLibraryPreviewRoot = preview;
            cardLibraryPreviewBackground = preview.GetComponent<Image>();
            cardLibraryArtImage = AddCardArtPlaceholder(preview, visibleCards.Count > 0 ? visibleCards[0] : null,
                0.25f, 0.29f, 0.75f, 0.79f, visibleCards.Count > 0);
            RectTransform title = CreateRect("Preview Name", preview);
            SetAnchors(title, 0.06f, 0.88f, 0.94f, 0.98f);
            cardLibraryPreviewTitle = title.gameObject.AddComponent<Text>();
            ConfigurePreviewText(cardLibraryPreviewTitle, 17, FontStyle.Bold, TextAnchor.MiddleCenter);

            RectTransform type = CreateRect("Preview Type", preview);
            SetAnchors(type, 0.06f, 0.8f, 0.94f, 0.87f);
            cardLibraryPreviewType = type.gameObject.AddComponent<Text>();
            ConfigurePreviewText(cardLibraryPreviewType, 12, FontStyle.Bold, TextAnchor.MiddleCenter);

            RectTransform details = CreateRect("Preview Stats And Effect", preview);
            SetAnchors(details, 0.07f, 0.04f, 0.93f, 0.27f);
            cardLibraryPreviewBody = details.gameObject.AddComponent<Text>();
            ConfigurePreviewText(cardLibraryPreviewBody, 12, FontStyle.Normal, TextAnchor.UpperLeft);
            cardLibraryPreviewBody.horizontalOverflow = HorizontalWrapMode.Wrap;
            cardLibraryPreviewBody.verticalOverflow = VerticalWrapMode.Truncate;

            AddCardLibraryPreview(visibleCards.Count > 0 ? visibleCards[0] : null);

            AddButton(contentRoot, "<", 0.31f, 0.075f, 0.37f, 0.11f,
                new Color(0.2f, 0.34f, 0.46f), () =>
                {
                    cardLibraryPage--;
                    Render();
                }, 12);
            AddText(contentRoot, "PAGE " + (cardLibraryPage + 1) + " / " + cardPageCount,
                0.375f, 0.075f, 0.625f, 0.11f, 11, mutedColor, TextAnchor.MiddleCenter);
            AddButton(contentRoot, ">", 0.63f, 0.075f, 0.69f, 0.11f,
                new Color(0.2f, 0.34f, 0.46f), () =>
                {
                    cardLibraryPage++;
                    Render();
                }, 12);

            if (cardFilterOptionsOpen)
                DrawCardLibraryFilterOptions();
        }

        private bool MatchesCardLibraryFilter(CardDefinition card)
        {
            switch (cardLibraryFilter)
            {
                case CardLibraryFilter.NormalMonsters: return card.Kind == CardKind.Monster && !card.IsEffectMonster;
                case CardLibraryFilter.EffectMonsters: return card.Kind == CardKind.Monster && card.IsEffectMonster;
                case CardLibraryFilter.Tuners: return card.IsMonster && card.IsTuner;
                case CardLibraryFilter.Synchro: return card.Kind == CardKind.Synchro;
                case CardLibraryFilter.Fusion: return card.Kind == CardKind.Fusion;
                case CardLibraryFilter.Xyz: return card.Kind == CardKind.Xyz;
                case CardLibraryFilter.Link: return card.Kind == CardKind.Link;
                case CardLibraryFilter.AllSpells: return card.Kind == CardKind.Spell;
                case CardLibraryFilter.NormalSpells: return card.Kind == CardKind.Spell && card.Subtype == CardSubtype.Normal;
                case CardLibraryFilter.QuickPlaySpells: return card.Kind == CardKind.Spell && card.Subtype == CardSubtype.QuickPlay;
                case CardLibraryFilter.ContinuousSpells: return card.Kind == CardKind.Spell && card.Subtype == CardSubtype.Continuous;
                case CardLibraryFilter.EquipSpells: return card.Kind == CardKind.Spell && card.Subtype == CardSubtype.Equip;
                case CardLibraryFilter.FieldSpells: return card.Kind == CardKind.Spell && card.Subtype == CardSubtype.Field;
                case CardLibraryFilter.RitualSpells: return card.Kind == CardKind.Spell && card.Subtype == CardSubtype.Ritual;
                case CardLibraryFilter.AllTraps: return card.Kind == CardKind.Trap;
                case CardLibraryFilter.NormalTraps: return card.Kind == CardKind.Trap && card.Subtype == CardSubtype.Normal;
                case CardLibraryFilter.ContinuousTraps: return card.Kind == CardKind.Trap && card.Subtype == CardSubtype.Continuous;
                case CardLibraryFilter.CounterTraps: return card.Kind == CardKind.Trap && card.Subtype == CardSubtype.Counter;
                default: return true;
            }
        }

        private string CardLibraryFilterLabel(CardLibraryFilter filter)
        {
            switch (filter)
            {
                case CardLibraryFilter.NormalMonsters: return "Normal Monsters";
                case CardLibraryFilter.EffectMonsters: return "Effect Monsters";
                case CardLibraryFilter.Tuners: return "Tuners";
                case CardLibraryFilter.Synchro: return "Synchro Monsters";
                case CardLibraryFilter.Fusion: return "Fusion Monsters";
                case CardLibraryFilter.Xyz: return "Xyz Monsters";
                case CardLibraryFilter.Link: return "Link Monsters";
                case CardLibraryFilter.AllSpells: return "All Spell Cards";
                case CardLibraryFilter.NormalSpells: return "Normal Spells";
                case CardLibraryFilter.QuickPlaySpells: return "Quick-Play Spells";
                case CardLibraryFilter.ContinuousSpells: return "Continuous Spells";
                case CardLibraryFilter.EquipSpells: return "Equip Spells";
                case CardLibraryFilter.FieldSpells: return "Field Spells";
                case CardLibraryFilter.RitualSpells: return "Ritual Spells";
                case CardLibraryFilter.AllTraps: return "All Trap Cards";
                case CardLibraryFilter.NormalTraps: return "Normal Traps";
                case CardLibraryFilter.ContinuousTraps: return "Continuous Traps";
                case CardLibraryFilter.CounterTraps: return "Counter Traps";
                default: return "All Card Types";
            }
        }

        private void DrawCardLibraryFilterOptions()
        {
            CardLibraryFilter[] filters = (CardLibraryFilter[])Enum.GetValues(typeof(CardLibraryFilter));
            const float rowHeight = 0.039f;
            float top = 0.865f;
            float bottom = top - filters.Length * rowHeight - 0.01f;
            AddPanel(contentRoot, 0.025f, bottom, 0.455f, 0.87f, new Color(0.035f, 0.055f, 0.085f, 0.99f));
            for (int i = 0; i < filters.Length; i++)
            {
                CardLibraryFilter filter = filters[i];
                float rowTop = top - i * rowHeight;
                AddButton(contentRoot, CardLibraryFilterLabel(filter), 0.035f, rowTop - rowHeight + 0.002f,
                    0.445f, rowTop - 0.002f, filter == cardLibraryFilter ? accentColor : CardFilterColor(filter),
                    () =>
                    {
                        cardLibraryFilter = filter;
                        cardFilterOptionsOpen = false;
                        Render();
                    }, 12);
            }
        }

        private Color CardFilterColor(CardLibraryFilter filter)
        {
            switch (filter)
            {
                case CardLibraryFilter.NormalMonsters: return CardFrameColor(StarterCatalog.All.FirstOrDefault(card => card.Kind == CardKind.Monster && !card.IsEffectMonster));
                case CardLibraryFilter.EffectMonsters:
                case CardLibraryFilter.Tuners: return new Color(0.63f, 0.30f, 0.13f);
                case CardLibraryFilter.Synchro: return new Color(0.48f, 0.53f, 0.59f);
                case CardLibraryFilter.Fusion: return new Color(0.38f, 0.25f, 0.58f);
                case CardLibraryFilter.Xyz: return new Color(0.16f, 0.16f, 0.21f);
                case CardLibraryFilter.Link: return new Color(0.12f, 0.32f, 0.48f);
                case CardLibraryFilter.AllSpells:
                case CardLibraryFilter.NormalSpells:
                case CardLibraryFilter.QuickPlaySpells:
                case CardLibraryFilter.ContinuousSpells:
                case CardLibraryFilter.EquipSpells:
                case CardLibraryFilter.FieldSpells:
                case CardLibraryFilter.RitualSpells: return new Color(0.12f, 0.43f, 0.28f);
                case CardLibraryFilter.AllTraps:
                case CardLibraryFilter.NormalTraps:
                case CardLibraryFilter.ContinuousTraps:
                case CardLibraryFilter.CounterTraps: return new Color(0.48f, 0.19f, 0.38f);
                default: return new Color(0.17f, 0.23f, 0.31f);
            }
        }

        private void AddCardLibraryRow(RectTransform parent, CardDefinition card, int index, float rowHeight)
        {
            RectTransform row = CreateRect(card.DisplayName, parent);
            row.anchorMin = new Vector2(0, 1);
            row.anchorMax = new Vector2(1, 1);
            row.pivot = new Vector2(0.5f, 1);
            row.sizeDelta = new Vector2(-12, rowHeight - 4);
            row.anchoredPosition = new Vector2(0, -index * rowHeight);

            Image image = row.gameObject.AddComponent<Image>();
            image.color = CardFrameColor(card);
            Button button = row.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => AddCardLibraryPreview(card));
            ColorBlock colors = button.colors;
            colors.highlightedColor = Color.Lerp(image.color, Color.white, 0.18f);
            colors.pressedColor = Color.Lerp(image.color, Color.black, 0.18f);
            button.colors = colors;

            RectTransform label = CreateRect("Name", row);
            Stretch(label);
            label.offsetMin = new Vector2(12, 0);
            label.offsetMax = new Vector2(-8, 0);
            Text text = label.gameObject.AddComponent<Text>();
            text.font = font;
            text.text = card.DisplayName;
            text.fontSize = 14;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;
            text.fontStyle = FontStyle.Bold;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;

            EventTrigger trigger = row.gameObject.AddComponent<EventTrigger>();
            EventTrigger.Entry enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => AddCardLibraryPreview(card));
            trigger.triggers.Add(enter);
        }

        private void AddCardLibraryPreview(CardDefinition card)
        {
            if (card == null || cardLibraryPreviewRoot == null)
                return;

            Color typeColor = CardFrameColor(card);
            cardLibraryPreviewBackground.color = Color.Lerp(panelColor, typeColor, 0.24f);
            cardLibraryPreviewTitle.text = card.DisplayName;
            cardLibraryPreviewTitle.color = typeColor;
            cardLibraryPreviewType.text = CardTypeLabel(card);
            cardLibraryPreviewType.color = Color.white;
            cardLibraryArtImage.color = Color.Lerp(backgroundColor, typeColor, 0.45f);
            cardLibraryPreviewBody.text = (card.IsMonster
                ? CardMonsterDetails(card) + "\n\n"
                : "") + "EFFECT\n" + card.RulesSummary;
        }

        private void ConfigurePreviewText(Text text, int size, FontStyle style, TextAnchor alignment)
        {
            text.font = font;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = Color.white;
            text.alignment = alignment;
            text.raycastTarget = false;
        }

        private void ChangeDeck(CardDefinition card, int delta)
        {
            notice = "";
            int count = profile.deck.Count(id => id == card.CardId);
            int mainCount = profile.deck.Select(StarterCatalog.Find).Count(item => item != null && item.CanBeMainDeck);
            int extraCount = profile.deck.Select(StarterCatalog.Find).Count(item => item != null && item.CanBeExtraDeck);
            if (delta > 0)
            {
                if (count >= 3)
                    notice = "Copy limit reached (3 per card).";
                else if (count >= GetOwned(card.CardId))
                    notice = "You do not own another copy. Buy a pack in the Shop.";
                else if (!card.CanBeMainDeck && !card.CanBeExtraDeck)
                    notice = "This card type cannot be added to a Duel Deck.";
                else if (card.CanBeExtraDeck && extraCount >= 15)
                    notice = "Your Extra Deck is full (15 cards maximum).";
                else if (card.CanBeMainDeck && mainCount >= 40)
                    notice = "Your Main Deck is full (40 cards maximum).";
                else
                    profile.deck.Add(card.CardId);
            }
            else if (count == 0)
                notice = "That card is not in your deck.";
            else if (card.CanBeMainDeck && mainCount <= 20)
                notice = "Keep at least 20 cards in your Main Deck.";
            else
            {
                profile.deck.Remove(card.CardId);
                if (engine != null)
                    notice = "Deck changed. Start a new duel to use the updated list.";
            }
            SaveProfile();
            Render();
        }

        private void DrawShopScreen()
        {
            AddText(contentRoot, "CARD MARKET", 0.04f, 0.84f, 0.55f, 0.94f, 30, Color.white, TextAnchor.MiddleLeft, true);
            AddText(contentRoot, "A pack contains five random cards. Each card is added to your collection and can be used in Deck Builder.",
                0.04f, 0.75f, 0.72f, 0.82f, 16, mutedColor, TextAnchor.MiddleLeft);

            AddPanel(contentRoot, 0.12f, 0.21f, 0.88f, 0.74f, panelColor);
            float packWidth = 0.19f;
            float packHeight = GetPortraitHeight(packWidth);
            float packX = 0.19f;
            float packY = 0.24f;
            RectTransform pack = AddPanel(contentRoot, packX, packY, packX + packWidth, packY + packHeight,
                new Color(0.25f, 0.17f, 0.42f));
            AddCardArtPlaceholder(pack, null, 0.035f, 0.035f, 0.965f, 0.965f, false);
            AddText(pack, "SYNCHRO\nBOOSTER", 0.08f, 0.32f, 0.92f, 0.7f, 17,
                new Color(1f, 0.8f, 0.32f), TextAnchor.MiddleCenter, true);

            AddText(contentRoot, "BOOSTER PACK", 0.44f, 0.57f, 0.78f, 0.66f, 25,
                new Color(1f, 0.8f, 0.32f), TextAnchor.MiddleLeft, true);
            AddText(contentRoot, "Five random cards from the current catalog.", 0.44f, 0.48f,
                0.8f, 0.56f, 16, mutedColor, TextAnchor.MiddleLeft);
            AddText(contentRoot, "100 CR", 0.44f, 0.38f, 0.65f, 0.46f, 22,
                Color.white, TextAnchor.MiddleLeft, true);
            AddText(contentRoot, "CREDITS  " + profile.credits, 0.65f, 0.38f, 0.83f, 0.46f, 17,
                new Color(1f, 0.8f, 0.32f), TextAnchor.MiddleRight, true);
            AddButton(contentRoot, "BUY PACK", 0.44f, 0.27f, 0.65f, 0.35f, accentColor, BuyPack, 16);
            AddButton(contentRoot, "DEV: +1000 CR", 0.67f, 0.27f, 0.85f, 0.35f,
                new Color(0.38f, 0.24f, 0.52f), GrantDeveloperCredits, 14);
            AddText(contentRoot, string.IsNullOrEmpty(notice) ? "Credits are earned by winning duels." : notice,
                0.04f, 0.07f, 0.96f, 0.13f, 14, mutedColor, TextAnchor.MiddleLeft);
        }

        private void DrawPackRevealOverlay()
        {
            AddPanel(contentRoot, 0, 0.09f, 1, 0.9f, new Color(0.01f, 0.02f, 0.035f, 0.9f));
            AddPanel(contentRoot, 0.06f, 0.14f, 0.94f, 0.86f, panelColor);
            AddText(contentRoot, "PACK OPENED", 0.12f, 0.76f, 0.68f, 0.83f, 26,
                new Color(1f, 0.8f, 0.32f), TextAnchor.MiddleLeft, true);
            AddText(contentRoot, "Five cards added to your collection. Hover a card to inspect it.",
                0.12f, 0.71f, 0.76f, 0.76f, 14, mutedColor, TextAnchor.MiddleLeft);
            AddButton(contentRoot, "CLOSE", 0.81f, 0.76f, 0.89f, 0.83f,
                new Color(0.36f, 0.22f, 0.24f), () =>
                {
                    packRevealOpen = false;
                    Render();
                }, 12);

            const float cardWidth = 0.13f;
            const float gap = 0.035f;
            float cardHeight = GetPortraitHeight(cardWidth);
            float totalWidth = lastPack.Count * cardWidth + Math.Max(0, lastPack.Count - 1) * gap;
            float startX = (1f - totalWidth) * 0.5f;
            float y = 0.34f;
            for (int i = 0; i < lastPack.Count; i++)
            {
                CardDefinition definition = lastPack[i];
                float x = startX + i * (cardWidth + gap);
                RectTransform card = AddButton(contentRoot, definition.DisplayName,
                    x, y, x + cardWidth, y + cardHeight, CardFrameColor(definition),
                    () => { }, 10, definition);
                AddCardArtPlaceholder(card, definition, 0.02f, 0.02f, 0.98f, 0.98f, true);
                RectTransform label = card.Find("Label") as RectTransform;
                if (label != null)
                    SetAnchors(label, 0.02f, 0.02f, 0.98f, 0.3f);
            }
        }

        private void DrawAiLabScreen()
        {
            IReadOnlyList<StructureDeckPreset> decks = StarterCatalog.StructureDecks;
            StructureDeckPreset first = decks[firstAiDeckIndex];
            StructureDeckPreset second = decks[secondAiDeckIndex];
            if (engine == null || !engine.IsAiVsAi)
            {
                AddText(contentRoot, "AI VS AI LAB", 0.025f, 0.89f, 0.5f, 0.98f, 27, Color.white, TextAnchor.MiddleLeft, true);
                AddText(contentRoot, "Choose two decks, then start a match on the full duel board.",
                    0.025f, 0.83f, 0.72f, 0.89f, 14, mutedColor, TextAnchor.MiddleLeft);
                DrawDeckPicker(first, 0.025f, true);
                DrawDeckPicker(second, 0.525f, false);
                AddButton(contentRoot, "START MATCH", 0.4f, 0.49f, 0.6f, 0.57f,
                    accentColor, StartAiMatch, 15);
                AddPanel(contentRoot, 0.025f, 0.13f, 0.975f, 0.45f, panelColor);
                AddText(contentRoot, "The AI uses the same monster, Spell/Trap, Field, Graveyard, and Deck zones as a normal duel.",
                    0.06f, 0.24f, 0.94f, 0.39f, 20, Color.white, TextAnchor.MiddleCenter);
                AddText(contentRoot, string.IsNullOrEmpty(notice)
                        ? "Step advances one complete turn. Autoplay pauses between turns so you can follow the match."
                        : notice,
                    0.06f, 0.16f, 0.94f, 0.23f, 14, mutedColor, TextAnchor.MiddleCenter);
                return;
            }

            bool running = engine.Phase != DuelPhase.GameOver;
            bool firstPlayerTurn = engine.CurrentPlayer == engine.Human && engine.Phase != DuelPhase.GameOver;
            DrawPlayerPortrait("P2", 0.025f, 0.804f, 0.085f, 0.9f,
                new Color(0.42f, 0.22f, 0.28f), !firstPlayerTurn && running);
            AddText(contentRoot, engine.Opponent.Name + "\n" + engine.Opponent.LifePoints +
                " LP   HAND " + engine.Opponent.Hand.Count, 0.095f, 0.805f, 0.195f, 0.9f,
                16, Color.white, TextAnchor.MiddleLeft, true);
            if (!firstPlayerTurn && running)
                DrawCurrentTurnLabel(0.095f, 0.75f, 0.195f, 0.79f);
            DrawGraveyardButton(engine.Opponent, 0.205f, 0.75f);
            DrawDeckButton(engine.Opponent, OpponentDeckX, 0.75f);
            DrawBanishedIndicator(engine.Opponent, 0.205f, 0.59f);
            DrawSpellTrapRow(engine.Opponent.SpellTrapZones, 0.75f);
            DrawFieldSpellSlot(engine.Opponent.FieldSpellZone, true);
            DrawReadOnlyZones(engine.Opponent.MonsterZones, 0.59f);

            AddPanel(contentRoot, 0.025f, 0.55f, 0.975f, 0.59f, new Color(0.08f, 0.14f, 0.18f));
            string status = engine.Phase == DuelPhase.GameOver
                ? engine.Winner + " wins"
                : "TURN " + engine.TurnNumber + "  •  " + engine.CurrentPlayer.Name + "  •  " + engine.PhaseLabel;
            AddText(contentRoot, status, 0.035f, 0.55f, 0.965f, 0.59f, 14,
                accentColor, TextAnchor.MiddleCenter, true);
            AddButton(contentRoot, "STEP 1 TURN", 0.025f, 0.505f, 0.18f, 0.545f,
                new Color(0.2f, 0.34f, 0.46f), () =>
                {
                    if (running)
                    {
                        engine.StepAiTurn();
                        Render();
                    }
                }, 11);
            AddButton(contentRoot, "PACE " + aiAutoplayPaces[aiAutoplayPaceIndex].ToString("0") + "s",
                0.19f, 0.505f, 0.34f, 0.545f, new Color(0.2f, 0.34f, 0.46f), () =>
                {
                    aiAutoplayPaceIndex = (aiAutoplayPaceIndex + 1) % aiAutoplayPaces.Length;
                    if (aiAutoplay)
                        nextAiStepTime = Time.unscaledTime + aiAutoplayPaces[aiAutoplayPaceIndex];
                    Render();
                }, 11);
            AddButton(contentRoot, aiAutoplay ? "PAUSE AUTO" : "AUTO PLAY",
                0.35f, 0.505f, 0.52f, 0.545f, new Color(0.44f, 0.24f, 0.56f), () =>
                {
                    if (running)
                    {
                        aiAutoplay = !aiAutoplay;
                        nextAiStepTime = Time.unscaledTime + aiAutoplayPaces[aiAutoplayPaceIndex];
                    }
                    Render();
                }, 11);
            AddButton(contentRoot, "HANDS", 0.53f, 0.505f, 0.68f, 0.545f,
                new Color(0.15f, 0.42f, 0.4f), () =>
                {
                    aiHandsOpen = true;
                    Render();
                }, 11);
            AddButton(contentRoot, "SELECT DECKS", 0.69f, 0.505f, 0.83f, 0.545f,
                new Color(0.2f, 0.34f, 0.46f), () =>
                {
                    aiAutoplay = false;
                    engine = null;
                    aiHandsOpen = false;
                    Render();
                }, 11);
            AddButton(contentRoot, "RESET MATCH", 0.84f, 0.505f, 0.975f, 0.545f,
                new Color(0.36f, 0.22f, 0.24f), StartAiMatch, 11);

            DrawReadOnlyZones(engine.Human.MonsterZones, 0.35f);
            DrawSpellTrapRow(engine.Human.SpellTrapZones, 0.19f);
            DrawFieldSpellSlot(engine.Human.FieldSpellZone, false);
            DrawPlayerPortrait("P1", 0.025f, 0.2f, 0.085f, 0.296f,
                new Color(0.12f, 0.36f, 0.4f), firstPlayerTurn);
            AddText(contentRoot, engine.Human.Name + "\n" + engine.Human.LifePoints +
                " LP   HAND " + engine.Human.Hand.Count, 0.095f, 0.2f, 0.195f, 0.296f,
                17, Color.white, TextAnchor.MiddleLeft, true);
            if (firstPlayerTurn)
                DrawCurrentTurnLabel(0.095f, 0.3f, 0.195f, 0.34f);
            DrawDeckButton(engine.Human, 0.205f, 0.19f);
            DrawBanishedIndicator(engine.Human, 0.74f, 0.35f);
            DrawGraveyardButton(engine.Human, 0.74f, 0.19f);
            AddText(contentRoot, string.Join("\n", engine.Messages.Reverse().Take(3).Reverse()),
                0.025f, 0.005f, 0.975f, 0.035f, 10, mutedColor, TextAnchor.MiddleLeft);
        }

        private void DrawTestLabSetupScreen()
        {
            List<CardDefinition> candidates = StarterCatalog.All
                .Where(card => card != null && !string.IsNullOrWhiteSpace(card.CardId) &&
                    MatchesTestCardFilters(card, TestLabFilter.None))
                .OrderBy(card => card.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            const int pageSize = 10;
            int pageCount = Mathf.Max(1, Mathf.CeilToInt(candidates.Count / (float)pageSize));
            testCardPage = Mathf.Clamp(testCardPage, 0, pageCount - 1);
            List<CardDefinition> page = candidates.Skip(testCardPage * pageSize).Take(pageSize).ToList();
            if (selectedTestCard == null && page.Count > 0)
                selectedTestCard = page[0];
            if (selectedTestCard != null && candidates.All(card => card.CardId != selectedTestCard.CardId))
                selectedTestCard = page.FirstOrDefault();

            AddText(contentRoot, "UNIT TESTS", 0.025f, 0.89f, 0.58f, 0.96f,
                26, Color.white, TextAnchor.MiddleLeft, true);
            AddText(contentRoot, "Search the catalog, filter by its data, and prepare a reproducible duel state.",
                0.025f, 0.84f, 0.72f, 0.89f, 13, mutedColor, TextAnchor.MiddleLeft);
            AddTestSearchField(0.025f, 0.795f, 0.475f, 0.835f);
            DrawTestFilterButton(TestLabFilter.Kind, 0.025f, 0.745f, 0.17f, 0.79f);
            DrawTestFilterButton(TestLabFilter.Type, 0.18f, 0.745f, 0.325f, 0.79f);
            DrawTestFilterButton(TestLabFilter.Attribute, 0.335f, 0.745f, 0.475f, 0.79f);
            DrawTestFilterButton(TestLabFilter.FrameType, 0.025f, 0.695f, 0.17f, 0.74f);
            DrawTestFilterButton(TestLabFilter.Level, 0.18f, 0.695f, 0.325f, 0.74f);
            DrawTestFilterButton(TestLabFilter.CardSet, 0.335f, 0.695f, 0.475f, 0.74f);
            AddText(contentRoot, candidates.Count + " matching cards  •  page " + (testCardPage + 1) +
                " of " + pageCount, 0.025f, 0.66f, 0.475f, 0.69f, 10, mutedColor, TextAnchor.MiddleLeft);

            AddPanel(contentRoot, 0.025f, 0.17f, 0.475f, 0.65f, panelColor);
            const float rowHeight = 0.045f;
            for (int i = 0; i < page.Count; i++)
            {
                CardDefinition card = page[i];
                float top = 0.645f - i * rowHeight;
                bool selected = selectedTestCard == card;
                AddButton(contentRoot, card.DisplayName + "   •   " + CardTypeLabel(card) +
                    "   •   " + TestEffectStatusLabel(card),
                    0.035f, top - rowHeight + 0.002f, 0.465f, top - 0.002f,
                    selected ? Color.Lerp(CardFrameColor(card), accentColor, 0.42f) : CardFrameColor(card),
                    () =>
                    {
                        selectedTestCard = card;
                        Render();
                    }, 10, card);
            }
            if (page.Count == 0)
                AddText(contentRoot, "No cards match these filters.", 0.06f, 0.43f, 0.44f, 0.5f,
                    15, mutedColor, TextAnchor.MiddleCenter);

            AddButton(contentRoot, "<", 0.15f, 0.115f, 0.22f, 0.155f,
                new Color(0.2f, 0.34f, 0.46f), () =>
                {
                    testCardPage = Mathf.Max(0, testCardPage - 1);
                    Render();
                }, 12);
            AddText(contentRoot, "PAGE " + (testCardPage + 1) + " / " + pageCount,
                0.225f, 0.115f, 0.355f, 0.155f, 11, mutedColor, TextAnchor.MiddleCenter);
            AddButton(contentRoot, ">", 0.36f, 0.115f, 0.43f, 0.155f,
                new Color(0.2f, 0.34f, 0.46f), () =>
                {
                    testCardPage = Mathf.Min(pageCount - 1, testCardPage + 1);
                    Render();
                }, 12);

            AddPanel(contentRoot, 0.5f, 0.49f, 0.975f, 0.83f, panelColor);
            if (selectedTestCard != null)
            {
                AddText(contentRoot, selectedTestCard.DisplayName, 0.525f, 0.76f, 0.95f, 0.815f,
                    18, CardFrameColor(selectedTestCard), TextAnchor.MiddleLeft, true);
                AddText(contentRoot, CardTypeLabel(selectedTestCard) + "  •  " +
                    TestEffectStatusLabel(selectedTestCard),
                    0.525f, 0.71f, 0.95f, 0.755f, 11,
                    selectedTestCard.Effect == CardEffect.None && RequiresEffectResolver(selectedTestCard)
                        ? new Color(1f, 0.62f, 0.35f) : accentColor,
                    TextAnchor.MiddleLeft, true);
                string description = selectedTestCard.IsMonster
                    ? CardMonsterDetails(selectedTestCard) + "\n\n"
                    : "";
                description += selectedTestCard.RulesSummary;
                AddText(contentRoot, description, 0.525f, 0.515f, 0.95f, 0.7f,
                    11, Color.white, TextAnchor.UpperLeft);
            }
            else
                AddText(contentRoot, "Search for a card to inspect its rules text.", 0.525f, 0.58f,
                    0.95f, 0.7f, 13, mutedColor, TextAnchor.MiddleLeft);

            AddPanel(contentRoot, 0.5f, 0.22f, 0.975f, 0.46f, panelColor);
            AddText(contentRoot, "OPTIONAL TEST FIXTURES", 0.525f, 0.41f, 0.95f, 0.455f,
                13, Color.white, TextAnchor.MiddleLeft, true);
            DrawTestFixtureButton("PLAYER MONSTER", MockDuelFixtures.PlayerMonster, 0.525f, 0.35f);
            DrawTestFixtureButton("OPPONENT MONSTER", MockDuelFixtures.OpponentMonster, 0.755f, 0.35f);
            DrawTestFixtureButton("OPPONENT SET TRAP", MockDuelFixtures.OpponentSetTrap, 0.525f, 0.29f);
            DrawTestFixtureButton("PLAYER GY MONSTER", MockDuelFixtures.PlayerGraveyardMonster, 0.755f, 0.29f);
            AddText(contentRoot, "The focused card starts in your hand; the CPU opponent still takes normal turns.",
                0.525f, 0.235f, 0.95f, 0.28f, 10, mutedColor, TextAnchor.MiddleLeft);

            string startLabel = selectedTestCard == null ? "SELECT A CARD" : "START UNIT TEST";
            AddButton(contentRoot, startLabel, 0.62f, 0.115f, 0.86f, 0.18f,
                selectedTestCard == null ? new Color(0.2f, 0.25f, 0.31f) : accentColor,
                StartMockDuel, 14, selectedTestCard);
            AddText(contentRoot, "Decks: " + StarterCatalog.StructureDecks[firstAiDeckIndex].Name + " vs " +
                StarterCatalog.StructureDecks[secondAiDeckIndex].Name +
                " (choose both in the AI LAB)", 0.5f, 0.075f, 0.975f, 0.11f, 10,
                mutedColor, TextAnchor.MiddleCenter);

            if (openTestFilter != TestLabFilter.None)
                DrawTestFilterOptions();
        }

        private void DrawTestFilterButton(TestLabFilter filter, float xMin, float yMin, float xMax, float yMax)
        {
            string selectedValue = GetSelectedTestFilter(filter);
            string label = TestLabFilterLabel(filter) + ": " +
                (selectedValue == "All" ? "ALL" : selectedValue.ToUpperInvariant());
            if (label.Length > 22)
                label = label.Substring(0, 19) + "...";

            AddButton(contentRoot, label, xMin, yMin, xMax, yMax,
                openTestFilter == filter ? accentColor : new Color(0.16f, 0.24f, 0.33f),
                () =>
                {
                    openTestFilter = openTestFilter == filter ? TestLabFilter.None : filter;
                    Render();
                }, 8);
        }

        private void DrawTestFilterOptions()
        {
            AddPanel(contentRoot, 0.025f, 0.17f, 0.475f, 0.65f, new Color(0.035f, 0.055f, 0.085f, 0.99f));
            AddText(contentRoot, "FILTER BY " + TestLabFilterLabel(openTestFilter),
                0.04f, 0.61f, 0.34f, 0.645f, 12, Color.white, TextAnchor.MiddleLeft, true);
            AddButton(contentRoot, "ALL", 0.37f, 0.61f, 0.46f, 0.645f,
                accentColor, () => SelectTestFilterValue("All"), 9);

            float viewportTop = 0.605f;
            if (openTestFilter == TestLabFilter.CardSet)
            {
                AddTestSetSearchField(0.04f, 0.565f, 0.46f, 0.605f);
                viewportTop = 0.53f;
            }

            List<string> options = GetTestFilterOptions(openTestFilter);
            if (openTestFilter == TestLabFilter.CardSet &&
                !string.IsNullOrWhiteSpace(testSetSearchQuery))
            {
                options = options.Where(option => option.IndexOf(
                    testSetSearchQuery.Trim(), StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            }

            const int maxOptions = 80;
            bool truncated = options.Count > maxOptions;
            options = options.Take(maxOptions).ToList();
            if (truncated)
                AddText(contentRoot, "Showing 80 values; narrow with the search field.",
                    0.04f, 0.535f, 0.46f, 0.56f, 9, mutedColor, TextAnchor.MiddleLeft);

            RectTransform viewport = AddPanel(contentRoot, 0.04f, 0.2f, 0.46f, viewportTop, backgroundColor);
            viewport.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            ScrollRect scroll = viewport.gameObject.AddComponent<WheelScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            ConfigureMouseWheelScrolling(scroll);

            const float rowHeight = 36f;
            RectTransform list = CreateRect("Test Filter Options", viewport);
            list.anchorMin = new Vector2(0, 1);
            list.anchorMax = new Vector2(1, 1);
            list.pivot = new Vector2(0.5f, 1);
            list.sizeDelta = new Vector2(0, options.Count * rowHeight);
            scroll.content = list;
            scroll.viewport = viewport;
            scroll.verticalScrollbar = CreateVerticalScrollbar(0.465f, 0.2f, 0.48f, viewportTop);
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            for (int i = 0; i < options.Count; i++)
            {
                string option = options[i];
                AddScrollListButton(list, option, i, rowHeight, new Color(0.16f, 0.24f, 0.33f),
                    () => SelectTestFilterValue(option), 10);
            }

            if (options.Count == 0)
                AddText(contentRoot, "No matching values.", 0.08f, 0.36f, 0.42f, 0.43f,
                    12, mutedColor, TextAnchor.MiddleCenter);
        }

        private void AddTestSetSearchField(float xMin, float yMin, float xMax, float yMax)
        {
            RectTransform root = AddPanel(contentRoot, xMin, yMin, xMax, yMax,
                new Color(0.1f, 0.14f, 0.19f));
            InputField input = root.gameObject.AddComponent<InputField>();
            input.lineType = InputField.LineType.SingleLine;
            input.targetGraphic = root.GetComponent<Image>();

            RectTransform textRect = CreateRect("Set Filter Search Text", root);
            SetAnchors(textRect, 0.04f, 0.05f, 0.96f, 0.95f);
            Text text = textRect.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = 11;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;
            text.raycastTarget = false;
            input.textComponent = text;

            RectTransform placeholderRect = CreateRect("Set Filter Search Placeholder", root);
            SetAnchors(placeholderRect, 0.04f, 0.05f, 0.96f, 0.95f);
            Text placeholder = placeholderRect.gameObject.AddComponent<Text>();
            placeholder.font = font;
            placeholder.fontSize = 11;
            placeholder.color = mutedColor;
            placeholder.text = "Search card sets...";
            placeholder.alignment = TextAnchor.MiddleLeft;
            placeholder.raycastTarget = false;
            input.placeholder = placeholder;
            input.text = testSetSearchQuery;
            input.onEndEdit.AddListener(value =>
            {
                if (testSetSearchQuery == value)
                    return;
                testSetSearchQuery = value;
                StartCoroutine(RefreshTestSetupAfterInput());
            });
        }

        private void SelectTestFilterValue(string value)
        {
            switch (openTestFilter)
            {
                case TestLabFilter.Kind: testKindFilter = value; break;
                case TestLabFilter.Type: testTypeFilter = value; break;
                case TestLabFilter.Attribute: testAttributeFilter = value; break;
                case TestLabFilter.FrameType: testFrameTypeFilter = value; break;
                case TestLabFilter.Level: testLevelFilter = value; break;
                case TestLabFilter.CardSet: testCardSetFilter = value; break;
            }
            openTestFilter = TestLabFilter.None;
            testCardPage = 0;
            Render();
        }

        private string GetSelectedTestFilter(TestLabFilter filter)
        {
            switch (filter)
            {
                case TestLabFilter.Kind: return testKindFilter;
                case TestLabFilter.Type: return testTypeFilter;
                case TestLabFilter.Attribute: return testAttributeFilter;
                case TestLabFilter.FrameType: return testFrameTypeFilter;
                case TestLabFilter.Level: return testLevelFilter;
                case TestLabFilter.CardSet: return testCardSetFilter;
                default: return "All";
            }
        }

        private static string TestLabFilterLabel(TestLabFilter filter)
        {
            switch (filter)
            {
                case TestLabFilter.Kind: return "KIND";
                case TestLabFilter.Type: return "CARD TYPE";
                case TestLabFilter.Attribute: return "ATTRIBUTE";
                case TestLabFilter.FrameType: return "FRAME TYPE";
                case TestLabFilter.Level: return "LEVEL";
                case TestLabFilter.CardSet: return "CARD SET";
                default: return "FILTER";
            }
        }

        private static bool RequiresEffectResolver(CardDefinition definition)
        {
            return definition.IsEffectMonster ||
                   definition.Kind == CardKind.Spell ||
                   definition.Kind == CardKind.Trap;
        }

        private static string TestEffectStatusLabel(CardDefinition definition)
        {
            if (definition.Effect != CardEffect.None)
                return "RESOLVER: " + definition.Effect;
            return RequiresEffectResolver(definition) ? "NOT IMPLEMENTED" : "NO ACTIVATED EFFECT";
        }

        private List<string> GetTestFilterOptions(TestLabFilter filter)
        {
            IEnumerable<string> values = StarterCatalog.All
                .Where(card => card != null && MatchesTestCardFilters(card, filter))
                .SelectMany(card => GetTestFilterValues(card, filter))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase);
            return values.ToList();
        }

        private bool MatchesTestCardFilters(CardDefinition card, TestLabFilter excludedFilter)
        {
            if (!string.IsNullOrWhiteSpace(testCardSearchQuery) &&
                card.DisplayName.IndexOf(testCardSearchQuery.Trim(), StringComparison.OrdinalIgnoreCase) < 0)
                return false;
            return FilterMatches(TestLabFilter.Kind, testKindFilter, excludedFilter, card) &&
                   FilterMatches(TestLabFilter.Type, testTypeFilter, excludedFilter, card) &&
                   FilterMatches(TestLabFilter.Attribute, testAttributeFilter, excludedFilter, card) &&
                   FilterMatches(TestLabFilter.FrameType, testFrameTypeFilter, excludedFilter, card) &&
                   FilterMatches(TestLabFilter.Level, testLevelFilter, excludedFilter, card) &&
                   FilterMatches(TestLabFilter.CardSet, testCardSetFilter, excludedFilter, card);
        }

        private static bool FilterMatches(
            TestLabFilter filter,
            string selectedValue,
            TestLabFilter excludedFilter,
            CardDefinition card)
        {
            if (filter == excludedFilter || selectedValue == "All")
                return true;
            return GetTestFilterValues(card, filter).Contains(selectedValue, StringComparer.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> GetTestFilterValues(CardDefinition card, TestLabFilter filter)
        {
            switch (filter)
            {
                case TestLabFilter.Kind:
                    return new[] { card.Kind.ToString() };
                case TestLabFilter.Type:
                    string cardType = card.IsMonster
                        ? string.IsNullOrWhiteSpace(card.MonsterType) ? "(No type)" : card.MonsterType
                        : card.Subtype.ToString();
                    return new[] { cardType };
                case TestLabFilter.Attribute:
                    return new[] { string.IsNullOrWhiteSpace(card.Attribute) ? "(No attribute)" : card.Attribute };
                case TestLabFilter.FrameType:
                    return new[] { string.IsNullOrWhiteSpace(card.FrameType) ? "(No frame type)" : card.FrameType };
                case TestLabFilter.Level:
                    return new[] { card.IsMonster ? card.Level.ToString() : "(No level)" };
                case TestLabFilter.CardSet:
                    return string.IsNullOrWhiteSpace(card.CardSets)
                        ? new[] { "(No card set listed)" }
                        : card.CardSets.Split('|')
                            .Select(value => value.Trim())
                            .Where(value => value.Length > 0);
                default:
                    return Array.Empty<string>();
            }
        }

        private void DrawTestFixtureButton(string label, MockDuelFixtures fixture, float x, float y)
        {
            bool enabled = (testFixtures & fixture) != 0;
            AddButton(contentRoot, (enabled ? "✓  " : "○  ") + label, x, y, x + 0.215f, y + 0.048f,
                enabled ? new Color(0.12f, 0.42f, 0.4f) : new Color(0.16f, 0.22f, 0.3f),
                () =>
                {
                    testFixtures ^= fixture;
                    Render();
                }, 10);
        }

        private void AddTestSearchField(float xMin, float yMin, float xMax, float yMax)
        {
            RectTransform root = AddPanel(contentRoot, xMin, yMin, xMax, yMax, new Color(0.1f, 0.14f, 0.19f));
            InputField input = root.gameObject.AddComponent<InputField>();
            input.lineType = InputField.LineType.SingleLine;
            input.targetGraphic = root.GetComponent<Image>();

            RectTransform textRect = CreateRect("Test Search Text", root);
            SetAnchors(textRect, 0.04f, 0.05f, 0.96f, 0.95f);
            Text text = textRect.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = 12;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;
            text.raycastTarget = false;
            input.textComponent = text;

            RectTransform placeholderRect = CreateRect("Test Search Placeholder", root);
            SetAnchors(placeholderRect, 0.04f, 0.05f, 0.96f, 0.95f);
            Text placeholder = placeholderRect.gameObject.AddComponent<Text>();
            placeholder.font = font;
            placeholder.fontSize = 12;
            placeholder.color = mutedColor;
            placeholder.text = "Search card names...";
            placeholder.alignment = TextAnchor.MiddleLeft;
            placeholder.raycastTarget = false;
            input.placeholder = placeholder;
            input.text = testCardSearchQuery;
            input.onEndEdit.AddListener(value =>
            {
                if (testCardSearchQuery == value)
                    return;
                testCardSearchQuery = value;
                testCardPage = 0;
                StartCoroutine(RefreshTestSetupAfterInput());
            });
        }

        private IEnumerator RefreshTestSetupAfterInput()
        {
            yield return null;
            if (screen == ScreenMode.TestLab && mockDuelSimulator == null)
                Render();
        }

        private void StartMockDuel()
        {
            if (selectedTestCard == null)
                return;
            IReadOnlyList<StructureDeckPreset> decks = StarterCatalog.StructureDecks;
            CardDefinition fixtureMonster = StarterCatalog.Find("13039848");
            CardDefinition fixtureTrap = StarterCatalog.All.FirstOrDefault(card => card.DisplayName == "Mirror Force");
            if (fixtureMonster == null || fixtureTrap == null)
                throw new InvalidOperationException("The mock duel requires its standard monster and Trap fixture cards.");

            StructureDeckPreset playerDeck = decks[firstAiDeckIndex];
            StructureDeckPreset opponentDeck = decks[secondAiDeckIndex];
            mockDuelSimulator = MockDuelSimulator.Start(
                playerDeck.MainDeck.Concat(playerDeck.ExtraDeck).ToList(),
                opponentDeck.MainDeck.Concat(opponentDeck.ExtraDeck).ToList(),
                selectedTestCard,
                testFixtures,
                fixtureMonster,
                fixtureTrap);
            engine = mockDuelSimulator.Engine;
            ClearSelection();
            notice = selectedTestCard.Effect == CardEffect.None && RequiresEffectResolver(selectedTestCard)
                ? "UNIMPLEMENTED EFFECT: the engine should reject activation. Verify the rejection, then mark the result."
                : selectedTestCard.Effect == CardEffect.None
                    ? "Manual test pending: use " + selectedTestCard.DisplayName +
                      " normally and inspect the resulting board/log."
                    : "Manual test pending: activate " + selectedTestCard.DisplayName +
                      " and inspect the resulting board/log.";
            Render();
        }

        private void ReturnToMockDuelSetup()
        {
            mockDuelSimulator = null;
            engine = null;
            notice = "";
            ClearSelection();
            Render();
        }

        private void DrawAiHandsOverlay()
        {
            AddPanel(contentRoot, 0, 0.09f, 1, 0.9f, new Color(0.01f, 0.02f, 0.035f, 0.9f));
            AddPanel(contentRoot, 0.08f, 0.13f, 0.92f, 0.87f, panelColor);
            AddText(contentRoot, "AI HANDS", 0.12f, 0.8f, 0.7f, 0.85f,
                24, Color.white, TextAnchor.MiddleLeft, true);
            AddButton(contentRoot, "CLOSE", 0.82f, 0.8f, 0.88f, 0.85f,
                new Color(0.36f, 0.22f, 0.24f), () =>
                {
                    aiHandsOpen = false;
                    Render();
                }, 12);

            AddText(contentRoot, engine.Opponent.Name + "  •  " + engine.Opponent.Hand.Count +
                " cards  •  face-down", 0.14f, 0.69f, 0.86f, 0.75f,
                15, Color.white, TextAnchor.MiddleLeft, true);
            DrawAiHandRow(engine.Opponent, true, 0.43f, 0.68f);

            AddText(contentRoot, engine.Human.Name + "  •  " + engine.Human.Hand.Count +
                " cards  •  face-up", 0.14f, 0.35f, 0.86f, 0.41f,
                15, Color.white, TextAnchor.MiddleLeft, true);
            DrawAiHandRow(engine.Human, false, 0.17f, 0.34f);
        }

        private void DrawAiHandRow(DuelPlayer owner, bool hideCards, float yMin, float yMax)
        {
            RectTransform viewport = AddPanel(contentRoot, 0.14f, yMin, 0.86f, yMax, backgroundColor);
            viewport.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            ScrollRect scroll = viewport.gameObject.AddComponent<WheelScrollRect>();
            scroll.horizontal = true;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            ConfigureMouseWheelScrolling(scroll);

            const float cardWidthPixels = 92f;
            const float cardGapPixels = 12f;
            float cardHeightPixels = cardWidthPixels * 7f / 5f;
            float contentWidth = Math.Max(Screen.width * 0.72f,
                owner.Hand.Count * (cardWidthPixels + cardGapPixels) + cardGapPixels);
            float contentHeight = cardHeightPixels + 12f;
            RectTransform list = CreateRect(owner.Name + " Hand", viewport);
            list.anchorMin = new Vector2(0, 1);
            list.anchorMax = new Vector2(0, 1);
            list.pivot = new Vector2(0, 1);
            list.sizeDelta = new Vector2(contentWidth, contentHeight);
            scroll.content = list;
            scroll.viewport = viewport;

            for (int i = 0; i < owner.Hand.Count; i++)
            {
                CardInstance card = owner.Hand[i];
                float xMinPixels = cardGapPixels + i * (cardWidthPixels + cardGapPixels);
                float xMin = xMinPixels / contentWidth;
                float xMax = (xMinPixels + cardWidthPixels) / contentWidth;
                float yMinNormalized = 1f - (cardHeightPixels + 6f) / contentHeight;
                RectTransform cardRect = AddButton(list,
                    hideCards ? "" : card.Definition.DisplayName,
                    xMin, yMinNormalized, xMax, 1f,
                    hideCards ? new Color(0.08f, 0.16f, 0.3f) : CardFrameColor(card.Definition),
                    () => { }, 9, hideCards ? null : card.Definition);
                AddCardArtPlaceholder(cardRect, hideCards ? null : card.Definition,
                    0.03f, 0.03f, 0.97f, 0.97f, !hideCards);
                if (hideCards)
                {
                    AddPanel(cardRect, 0.17f, 0.17f, 0.83f, 0.83f, new Color(0.12f, 0.21f, 0.36f));
                    AddPanel(cardRect, 0.25f, 0.25f, 0.75f, 0.75f, new Color(0.08f, 0.16f, 0.3f));
                }
                else
                {
                    Text label = cardRect.Find("Label").GetComponent<Text>();
                    SetAnchors(label.rectTransform, 0.02f, 0.02f, 0.98f, 0.3f);
                    label.fontSize = 8;
                }
            }
        }

        private void DrawDeckPicker(StructureDeckPreset deck, float x, bool first)
        {
            AddPanel(contentRoot, x, 0.67f, x + 0.45f, 0.81f, panelColor);
            int mainCount = deck.MainDeck.Count;
            int extraCount = deck.ExtraDeck.Count;
            AddText(contentRoot, (first ? "SEAT A" : "SEAT B") + "  •  " + deck.Name + "\n" + deck.Theme +
                "\n" + mainCount + " Main / " + extraCount + " Extra cards",
                x + 0.012f, 0.68f, x + 0.34f, 0.8f, 14, Color.white, TextAnchor.MiddleLeft, true);
            AddButton(contentRoot, "◀", x + 0.355f, 0.73f, x + 0.395f, 0.78f, new Color(0.2f, 0.34f, 0.46f),
                () => CycleAiDeck(first, -1), 14);
            AddButton(contentRoot, "▶", x + 0.4f, 0.73f, x + 0.44f, 0.78f, new Color(0.2f, 0.34f, 0.46f),
                () => CycleAiDeck(first, 1), 14);
        }

        private void CycleAiDeck(bool first, int direction)
        {
            int count = StarterCatalog.StructureDecks.Count;
            if (first)
                firstAiDeckIndex = (firstAiDeckIndex + direction + count) % count;
            else
                secondAiDeckIndex = (secondAiDeckIndex + direction + count) % count;
            notice = "";
            Render();
        }

        private void DrawReadOnlyZones(CardInstance[] zones, float y)
        {
            for (int i = 0; i < zones.Length; i++)
            {
                float x = MonsterCardStartX + i * MonsterCardStepX;
                CardInstance card = zones[i];
                string label = card == null ? "MONSTER\nSLOT" : CardLabel(card, true);
                Color zoneColor = card == null ? new Color(0.09f, 0.13f, 0.18f) : CardZoneColor(card);
                RectTransform slot = AddButton(contentRoot, label, x, y, x + BoardCardWidth, y + BoardCardHeight,
                    zoneColor, () => { }, 10,
                    card != null && card.FaceUp ? card.Definition : null);
                AddCardArtPlaceholder(slot, card != null && card.FaceUp ? card.Definition : null,
                    0f, 0f, 1f, 1f, card == null || card.FaceUp);
                RotateDefenseMonster(slot, card);
                FitMonsterSlotStats(slot, card);
            }
        }

        private void DrawPlayerPortrait(
            string label,
            float xMin,
            float yMin,
            float xMax,
            float yMax,
            Color color,
            bool currentTurn = false)
        {
            if (currentTurn)
            {
                AddPanel(contentRoot, xMin, yMin, xMax, yMax, new Color(1f, 0.82f, 0.28f));
                AddPanel(contentRoot, xMin + 0.004f, yMin + 0.004f, xMax - 0.004f, yMax - 0.004f,
                    Color.Lerp(color, accentColor, 0.3f));
            }
            else
                AddPanel(contentRoot, xMin, yMin, xMax, yMax, color);
            AddText(contentRoot, label, xMin + 0.002f, yMin + 0.032f, xMax - 0.002f, yMax - 0.004f,
                16, Color.white, TextAnchor.MiddleCenter, true);
            AddText(contentRoot, "PORTRAIT", xMin + 0.001f, yMin + 0.001f, xMax - 0.001f, yMin + 0.032f,
                9, new Color(0.85f, 0.88f, 0.91f), TextAnchor.MiddleCenter, true);
        }

        private void DrawCurrentTurnLabel(float xMin, float yMin, float xMax, float yMax)
        {
            AddText(contentRoot, "CURRENT TURN", xMin, yMin, xMax, yMax,
                11, new Color(1f, 0.82f, 0.28f), TextAnchor.MiddleLeft, true);
        }

        private RectTransform AddScrollListButton(
            RectTransform list,
            string label,
            int index,
            float rowHeight,
            Color color,
            Action callback,
            int fontSize,
            CardDefinition hoverCard = null)
        {
            RectTransform button = AddButton(list, label, 0, 0, 1, 1, color, callback, fontSize, hoverCard);
            button.anchorMin = new Vector2(0, 1);
            button.anchorMax = new Vector2(1, 1);
            button.pivot = new Vector2(0.5f, 1);
            button.sizeDelta = new Vector2(0, rowHeight - 2f);
            button.anchoredPosition = new Vector2(0, -index * rowHeight);
            return button;
        }

        private void BuyPack()
        {
            notice = "";
            if (profile.credits < 100)
            {
                notice = "Not enough credits. Win a duel to earn more.";
                Render();
                return;
            }

            profile.credits -= 100;
            lastPack = new List<CardDefinition>();
            for (int i = 0; i < 5; i++)
            {
                CardDefinition card = RollPackCard();
                lastPack.Add(card);
                AddToCollection(card.CardId, 1);
            }
            packRevealOpen = true;
            SaveProfile();
            Render();
        }

        private void GrantDeveloperCredits()
        {
            profile.credits = checked(profile.credits + 1000);
            notice = "Developer grant: +1000 credits.";
            SaveProfile();
            Render();
        }

        private CardDefinition RollPackCard()
        {
            IReadOnlyList<CardDefinition> cards = StarterCatalog.All
                .Where(card => card.CanBeMainDeck || card.CanBeExtraDeck)
                .ToList();
            if (cards.Count == 0)
                throw new InvalidOperationException("The card catalog contains no cards that can be used in a Duel Deck.");
            int totalWeight = cards.Sum(card => card.PackWeight);
            int roll = UnityEngine.Random.Range(0, totalWeight);
            foreach (CardDefinition card in cards)
            {
                roll -= card.PackWeight;
                if (roll < 0)
                    return card;
            }
            return cards[cards.Count - 1];
        }

        private int GetOwned(string id)
        {
            CardCount entry = profile.collection.FirstOrDefault(item => item.id == id);
            return entry == null ? 0 : entry.count;
        }

        private void AddToCollection(string id, int amount)
        {
            CardCount entry = profile.collection.FirstOrDefault(item => item.id == id);
            if (entry == null)
            {
                entry = new CardCount { id = id, count = 0 };
                profile.collection.Add(entry);
            }
            entry.count += amount;
        }

        private void AwardMatchIfNeeded()
        {
            if (engine != null && !engine.IsAiVsAi && engine.Phase == DuelPhase.GameOver && engine.Winner == engine.Human.Name &&
                !matchRewardGranted)
            {
                profile.credits += 100;
                matchRewardGranted = true;
                notice = "Victory reward: +100 credits.";
                SaveProfile();
            }
        }

        private void UpdateCreditsText()
        {
            if (creditsText != null)
                creditsText.text = profile.credits + " CR";
        }

        private void AttachCardHover(RectTransform rect, CardDefinition definition)
        {
            if (rect == null || definition == null)
                return;
            EventTrigger trigger = rect.gameObject.AddComponent<EventTrigger>();
            EventTrigger.Entry enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(data => ShowCardPreview(definition, data as PointerEventData));
            trigger.triggers.Add(enter);
            EventTrigger.Entry exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => HideCardPreview());
            trigger.triggers.Add(exit);
        }

        private void ShowCardPreview(CardDefinition definition, PointerEventData eventData)
        {
            if (definition == null || cardPreviewRoot == null || eventData == null)
                return;

            string kind = CardTypeLabel(definition);
            cardPreviewTitle.text = definition.DisplayName;
            cardPreviewBody.text = (definition.IsMonster
                ? kind + "  •  " + CardMonsterDetails(definition) + "\n\n"
                : kind + "\n\n") + definition.RulesSummary;
            cardPreviewTitle.color = CardFrameColor(definition);
            cardPreviewBackground.color = Color.Lerp(backgroundColor, CardFrameColor(definition), 0.24f);
            cardPreviewArtImage.color = Color.Lerp(backgroundColor, CardFrameColor(definition), 0.45f);

            RectTransform canvasRect = canvas.transform as RectTransform;
            if (canvasRect != null)
            {
                Rect canvasBounds = canvasRect.rect;
                float previewHeight = Mathf.Min(520f, canvasBounds.height * 0.62f);
                float previewWidth = previewHeight * 5f / 7f;
                cardPreviewRoot.sizeDelta = new Vector2(previewWidth, previewHeight);

                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        canvasRect, eventData.position, eventData.pressEventCamera, out Vector2 localPoint))
                {
                    Rect targetBounds = GetHoverTargetBounds(eventData, canvasRect, localPoint);
                    Vector2 position = FindCardPreviewPosition(targetBounds, canvasBounds, previewWidth, previewHeight,
                        localPoint.x >= canvasBounds.center.x);
                    cardPreviewRoot.localPosition = new Vector3(position.x, position.y, 0);
                }
            }

            cardPreviewRoot.gameObject.SetActive(true);
            cardPreviewRoot.SetAsLastSibling();
        }

        private static Rect GetHoverTargetBounds(
            PointerEventData eventData,
            RectTransform canvasRect,
            Vector2 pointerPosition)
        {
            RectTransform target = eventData.pointerEnter != null
                ? eventData.pointerEnter.GetComponent<RectTransform>()
                : null;
            if (target == null)
                return new Rect(pointerPosition.x, pointerPosition.y, 0, 0);

            Vector3[] corners = new Vector3[4];
            target.GetWorldCorners(corners);
            Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (int i = 0; i < corners.Length; i++)
            {
                Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(eventData.pressEventCamera, corners[i]);
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        canvasRect, screenPoint, eventData.pressEventCamera, out Vector2 localPoint))
                    continue;

                min = Vector2.Min(min, localPoint);
                max = Vector2.Max(max, localPoint);
            }

            return float.IsInfinity(min.x)
                ? new Rect(pointerPosition.x, pointerPosition.y, 0, 0)
                : Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private static Vector2 FindCardPreviewPosition(
            Rect target,
            Rect canvas,
            float width,
            float height,
            bool preferLeft)
        {
            const float gap = 18f;
            Rect[] candidates = preferLeft
                ? new[]
                {
                    Rect.MinMaxRect(target.xMin - gap - width, target.yMax - height,
                        target.xMin - gap, target.yMax),
                    Rect.MinMaxRect(target.xMax + gap, target.yMax - height,
                        target.xMax + gap + width, target.yMax)
                }
                : new[]
                {
                    Rect.MinMaxRect(target.xMax + gap, target.yMax - height,
                        target.xMax + gap + width, target.yMax),
                    Rect.MinMaxRect(target.xMin - gap - width, target.yMax - height,
                        target.xMin - gap, target.yMax)
                };

            for (int i = 0; i < candidates.Length; i++)
            {
                if (IsInsideCanvas(candidates[i], canvas) && !candidates[i].Overlaps(target))
                    return new Vector2(candidates[i].xMin, candidates[i].yMax);
            }

            float centeredX = Mathf.Clamp(target.center.x - width * 0.5f,
                canvas.xMin, canvas.xMax - width);
            Rect below = Rect.MinMaxRect(centeredX, target.yMin - gap - height,
                centeredX + width, target.yMin - gap);
            if (IsInsideCanvas(below, canvas) && !below.Overlaps(target))
                return new Vector2(below.xMin, below.yMax);

            Rect above = Rect.MinMaxRect(centeredX, target.yMax + gap,
                centeredX + width, target.yMax + gap + height);
            if (IsInsideCanvas(above, canvas) && !above.Overlaps(target))
                return new Vector2(above.xMin, above.yMax);

            float fallbackX = Mathf.Clamp(target.center.x - width * 0.5f,
                canvas.xMin, canvas.xMax - width);
            float fallbackY = Mathf.Clamp(target.center.y + height * 0.5f,
                canvas.yMin + height, canvas.yMax);
            return new Vector2(fallbackX, fallbackY);
        }

        private static bool IsInsideCanvas(Rect candidate, Rect canvas)
        {
            return candidate.xMin >= canvas.xMin && candidate.xMax <= canvas.xMax &&
                   candidate.yMin >= canvas.yMin && candidate.yMax <= canvas.yMax;
        }

        private void HideCardPreview()
        {
            if (cardPreviewRoot != null)
                cardPreviewRoot.gameObject.SetActive(false);
        }

        private Color CardZoneColor(CardInstance card)
        {
            if (card == null)
                return new Color(0.09f, 0.13f, 0.18f);
            if (!card.FaceUp)
                return new Color(0.38f, 0.29f, 0.12f);

            Color position = card.AttackPosition
                ? new Color(0.36f, 0.16f, 0.19f)
                : new Color(0.13f, 0.25f, 0.39f);
            return Color.Lerp(CardFrameColor(card.Definition), position, 0.22f);
        }

        private static Color CardFrameColor(CardDefinition definition)
        {
            if (definition == null)
                return new Color(0.12f, 0.17f, 0.23f);

            switch (definition.Kind)
            {
                case CardKind.Spell: return new Color(0.12f, 0.43f, 0.28f);
                case CardKind.Trap: return new Color(0.48f, 0.19f, 0.38f);
                case CardKind.Synchro: return new Color(0.48f, 0.53f, 0.59f);
                case CardKind.Fusion: return new Color(0.38f, 0.25f, 0.58f);
                case CardKind.Xyz: return new Color(0.16f, 0.16f, 0.21f);
                case CardKind.Link: return new Color(0.12f, 0.32f, 0.48f);
                case CardKind.Token: return new Color(0.26f, 0.34f, 0.36f);
                default:
                    return definition.IsEffectMonster
                        ? new Color(0.63f, 0.30f, 0.13f)
                        : new Color(0.55f, 0.41f, 0.14f);
            }
        }

        private static string CardTypeLabel(CardDefinition definition)
        {
            if (definition.Kind == CardKind.Monster)
                return definition.IsEffectMonster ? "EFFECT MONSTER" : "NORMAL MONSTER";
            if (definition.Kind == CardKind.Spell)
                return definition.Subtype.ToString().ToUpperInvariant() + " SPELL";
            if (definition.Kind == CardKind.Trap)
                return definition.Subtype.ToString().ToUpperInvariant() + " TRAP";
            return definition.Kind.ToString().ToUpperInvariant();
        }

        private static string CardMonsterDetails(CardDefinition definition)
        {
            string levelLabel = definition.Kind == CardKind.Xyz
                ? "RANK " + definition.Level
                : definition.Kind == CardKind.Link
                    ? "LINK-" + definition.Level
                    : "LEVEL " + definition.Level;
            string tuner = definition.IsTuner ? "  •  TUNER" : "";
            string attribute = string.IsNullOrEmpty(definition.Attribute) ? "" : "  •  " + definition.Attribute;
            string race = string.IsNullOrEmpty(definition.MonsterType) ? "" : " " + definition.MonsterType;
            string stats = definition.Kind == CardKind.Link
                ? "\nATK " + definition.AttackDisplay
                : "\nATK " + definition.AttackDisplay + "   /   DEF " + definition.DefenseDisplay;
            return levelLabel + tuner + attribute + race + stats;
        }

        private RectTransform AddPanel(RectTransform parent, float xMin, float yMin, float xMax, float yMax, Color color)
        {
            RectTransform rect = CreateRect("Panel", parent);
            SetAnchors(rect, xMin, yMin, xMax, yMax);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            return rect;
        }

        private void AddText(
            RectTransform parent,
            string value,
            float xMin,
            float yMin,
            float xMax,
            float yMax,
            int size,
            Color color,
            TextAnchor alignment,
            bool bold = false)
        {
            RectTransform rect = CreateRect("Text", parent);
            SetAnchors(rect, xMin, yMin, xMax, yMax);
            Text text = rect.gameObject.AddComponent<Text>();
            text.font = font;
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
        }

        private RectTransform AddButton(
            RectTransform parent,
            string label,
            float xMin,
            float yMin,
            float xMax,
            float yMax,
            Color color,
            Action callback,
            int fontSize,
            CardDefinition hoverCard = null)
        {
            RectTransform rect = CreateRect("Button", parent);
            SetAnchors(rect, xMin, yMin, xMax, yMax);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => callback());
            ColorBlock colors = button.colors;
            colors.highlightedColor = Color.Lerp(color, Color.white, 0.15f);
            colors.pressedColor = Color.Lerp(color, Color.black, 0.15f);
            button.colors = colors;

            RectTransform labelRect = CreateRect("Label", rect);
            Stretch(labelRect);
            Text text = labelRect.gameObject.AddComponent<Text>();
            text.font = font;
            text.text = label;
            text.fontSize = fontSize;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.fontStyle = FontStyle.Bold;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;

            AttachCardHover(rect, hoverCard);
            return rect;
        }

        private static float GetPortraitHeight(float normalizedWidth)
        {
            float screenAspect = Screen.width > 0 && Screen.height > 0
                ? (float)Screen.width / Screen.height
                : 16f / 9f;
            return normalizedWidth * screenAspect * 7f / 5f;
        }

        private static float GetPortraitWidth(float normalizedHeight)
        {
            float screenAspect = Screen.width > 0 && Screen.height > 0
                ? (float)Screen.width / Screen.height
                : 16f / 9f;
            return normalizedHeight * 5f / 7f / screenAspect;
        }

        private Image AddCardArtPlaceholder(
            RectTransform parent,
            CardDefinition definition,
            float xMin,
            float yMin,
            float xMax,
            float yMax,
            bool faceUp)
        {
            RectTransform artRect = CreateRect("Card Art Placeholder", parent);
            SetAnchors(artRect, xMin, yMin, xMax, yMax);
            Image image = artRect.gameObject.AddComponent<Image>();
            image.color = faceUp
                ? Color.Lerp(backgroundColor, definition == null
                    ? new Color(0.15f, 0.19f, 0.24f)
                    : CardFrameColor(definition), 0.45f)
                : new Color(0.075f, 0.13f, 0.34f);
            image.raycastTarget = false;
            artRect.SetAsFirstSibling();
            AspectRatioFitter fitter = artRect.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = 5f / 7f;
            if (!faceUp)
                AddCardBackDecoration(artRect);

            Transform buttonLabel = parent.Find("Label");
            if (buttonLabel != null)
                SetAnchors((RectTransform)buttonLabel, 0.02f, 0.02f, 0.98f, 0.28f);
            return image;
        }

        private void AddCardBackDecoration(RectTransform cardBack)
        {
            AddCardBackFrame(cardBack, 0.035f, 0.02f, 0.965f, 0.98f, new Color(0.25f, 0.53f, 0.76f));
            AddCardBackFrame(cardBack, 0.055f, 0.035f, 0.945f, 0.965f, new Color(0.075f, 0.13f, 0.34f));

            AddCardBackCircle(cardBack, 0.276f, 0.48f, 0.724f, 0.8f, new Color(0.34f, 0.77f, 0.92f, 0.8f));
            AddCardBackCircle(cardBack, 0.374f, 0.55f, 0.626f, 0.73f, new Color(0.95f, 0.72f, 0.3f, 0.82f));
            AddCardBackCircle(cardBack, 0.458f, 0.61f, 0.542f, 0.67f, new Color(0.34f, 0.77f, 0.92f, 0.92f));
        }

        private void AddCardBackFrame(
            RectTransform parent,
            float xMin,
            float yMin,
            float xMax,
            float yMax,
            Color color)
        {
            RectTransform frame = AddPanel(parent, xMin, yMin, xMax, yMax, color);
            frame.GetComponent<Image>().raycastTarget = false;
        }

        private void AddCardBackCircle(
            RectTransform parent,
            float xMin,
            float yMin,
            float xMax,
            float yMax,
            Color color)
        {
            RectTransform circle = CreateRect("Card Back Circle", parent);
            SetAnchors(circle, xMin, yMin, xMax, yMax);
            Image image = circle.gameObject.AddComponent<Image>();
            image.sprite = GetCardBackCircleSprite();
            image.color = color;
            image.raycastTarget = false;
        }

        private Sprite GetCardBackCircleSprite()
        {
            if (cardBackCircleSprite != null)
                return cardBackCircleSprite;

            const int size = 96;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = "Card Back Circle Pattern";
            texture.wrapMode = TextureWrapMode.Clamp;
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f;
                    float dy = (y + 0.5f) / size * 2f - 1f;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    float ring = Mathf.Max(
                        1f - Mathf.Abs(distance - 0.86f) / 0.045f,
                        1f - Mathf.Abs(distance - 0.68f) / 0.035f);
                    ring = Mathf.Max(ring, 1f - Mathf.Abs(distance - 0.18f) / 0.045f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(ring));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            cardBackCircleSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            cardBackCircleSprite.name = "Card Back Circle Pattern";
            return cardBackCircleSprite;
        }

        private void OnDestroy()
        {
            if (observedEngine != null)
            {
                observedEngine.PhaseChanged -= OnPhaseStarted;
                observedEngine.AttackDeclared -= OnAttackDeclared;
                observedEngine.CardDrawn -= OnCardDrawn;
                observedEngine = null;
            }
            if (cardBackCircleSprite == null)
                return;
            Texture2D texture = cardBackCircleSprite.texture;
            Destroy(cardBackCircleSprite);
            Destroy(texture);
            cardBackCircleSprite = null;
        }

        private static RectTransform CreateRect(string name, RectTransform parent)
        {
            GameObject child = new GameObject(name, typeof(RectTransform));
            RectTransform rect = child.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static void SetAnchors(RectTransform rect, float xMin, float yMin, float xMax, float yMax)
        {
            rect.anchorMin = new Vector2(xMin, yMin);
            rect.anchorMax = new Vector2(xMax, yMax);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void Stretch(RectTransform rect)
        {
            SetAnchors(rect, 0, 0, 1, 1);
        }
    }

}
