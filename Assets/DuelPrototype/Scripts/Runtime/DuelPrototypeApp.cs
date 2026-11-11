using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace DuelPrototype
{
    public sealed class WheelScrollRect : ScrollRect
    {
        public Action<int> PageBoundaryScrolled;

        public override void OnScroll(PointerEventData eventData)
        {
            Vector2 originalScrollDelta = eventData.scrollDelta;
            Vector2 normalizedScrollDelta = originalScrollDelta;
            if (Mathf.Max(Mathf.Abs(normalizedScrollDelta.x), Mathf.Abs(normalizedScrollDelta.y)) > 10f)
                normalizedScrollDelta /= 120f;
            if (horizontal && !vertical && normalizedScrollDelta.x == 0f)
                normalizedScrollDelta = new Vector2(normalizedScrollDelta.y, 0f);

            bool contentFitsViewport = vertical && content != null && viewport != null &&
                content.rect.height <= viewport.rect.height;
            eventData.scrollDelta = normalizedScrollDelta;
            try
            {
                base.OnScroll(eventData);
            }
            finally
            {
                eventData.scrollDelta = originalScrollDelta;
            }

            if (!vertical || PageBoundaryScrolled == null || normalizedScrollDelta.y == 0f)
                return;
            if (normalizedScrollDelta.y < 0f &&
                (contentFitsViewport || verticalNormalizedPosition <= 0.001f))
                PageBoundaryScrolled(1);
            else if (normalizedScrollDelta.y > 0f &&
                (contentFitsViewport || verticalNormalizedPosition >= 0.999f))
                PageBoundaryScrolled(-1);
        }
    }

    public sealed class ScrollWheelForwarder : MonoBehaviour, IScrollHandler
    {
        public WheelScrollRect ScrollTarget;

        public void OnScroll(PointerEventData eventData)
        {
            if (ScrollTarget != null)
                ScrollTarget.OnScroll(eventData);
        }
    }

    public sealed class RightClickAction : MonoBehaviour, IPointerClickHandler
    {
        public Action OnRightClick;
        public Action<PointerEventData> OnRightClickWithEventData;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Right)
            {
                OnRightClick?.Invoke();
                OnRightClickWithEventData?.Invoke(eventData);
            }
        }
    }

    public sealed class DuelCardDragHandler : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
    {
        public CardInstance Card;
        public Func<CardInstance, bool> OnDropCard;
        public Action<PointerEventData> OnBeginDragAction;
        public Action<PointerEventData> OnDragAction;
        public Action<PointerEventData> OnEndDragAction;

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (Card != null)
                OnBeginDragAction?.Invoke(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (Card != null)
                OnDragAction?.Invoke(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (Card != null)
                OnEndDragAction?.Invoke(eventData);
        }

        public void OnDrop(PointerEventData eventData)
        {
            DuelCardDragHandler draggedCard = eventData.pointerDrag == null
                ? null
                : eventData.pointerDrag.GetComponent<DuelCardDragHandler>();
            if (draggedCard != null && draggedCard.Card != null)
                OnDropCard?.Invoke(draggedCard.Card);
        }
    }

    public sealed class DuelHandCardVisual : MonoBehaviour
    {
        public CardInstance Card;
    }

    public static class DuelHandLayout
    {
        public const int MaxVisibleOpponentCards = 10;
        public const float CardHeight = 0.112f;
        public const float CardGap = 0.008f;
        public const float MaxWidth = 0.68f;
        public const float HumanHandBottom = 0.018f;
        public const float OpponentHandBottom = 0.875f;

        public static float GetCardWidth(int handCount, float screenAspect, bool opponent)
        {
            int visibleCount = GetVisibleCount(handCount, opponent);
            float baseWidth = CardHeight * 5f / 7f / screenAspect;
            float gap = GetCardGap(visibleCount);
            return Mathf.Min(baseWidth, (MaxWidth - (visibleCount - 1) * gap) / visibleCount);
        }

        public static Vector2 GetCardCenter(int index, int handCount, float screenAspect, bool opponent)
        {
            int visibleCount = GetVisibleCount(handCount, opponent);
            int visibleIndex = Mathf.Clamp(index, 0, visibleCount - 1);
            float gap = GetCardGap(visibleCount);
            float width = GetCardWidth(handCount, screenAspect, opponent);
            float totalWidth = visibleCount * width + (visibleCount - 1) * gap;
            float startX = (1f - totalWidth) * 0.5f;
            float height = width * screenAspect * 7f / 5f;
            float bottom = opponent ? OpponentHandBottom : HumanHandBottom;
            return new Vector2(startX + visibleIndex * (width + gap) + width * 0.5f,
                bottom + height * 0.5f);
        }

        private static int GetVisibleCount(int handCount, bool opponent)
        {
            return Mathf.Max(1, opponent ? Mathf.Min(handCount, MaxVisibleOpponentCards) : handCount);
        }

        private static float GetCardGap(int visibleCount)
        {
            return Mathf.Min(CardGap, MaxWidth / Mathf.Max(1, visibleCount - 1));
        }
    }

    public static class DuelAttackLayout
    {
        public static Vector2 GetDirectAttackTarget(bool attackerIsOpponent)
        {
            return new Vector2(0.5f, attackerIsOpponent ? 0.24f : 0.76f);
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
            public bool AttackPosition;
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
            public bool IsHandReflow;
            public bool IsSummon;
            public bool IsHandDeparture;
            public bool IsDraw;
            public bool IsTributeSummon;
            public bool IsTributedCard;
            public bool IsDestruction;
            public bool IsSet;
            public int BatchId;
        }

        private sealed class AttackAnimation
        {
            public CardInstance Attacker;
            public Vector2 From;
            public Vector2 To;
        }

        private sealed class PhaseTickerMessage
        {
            public string Label;
            public Color BackgroundColor;
            public Color AccentColor;
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
        private const float CoinSideButtonOffset = 90f;
        private const float CoinSideButtonSize = 140f;
        private const float BoardCardWidth = 0.052f;
        private const float BoardCardStepX = 0.075f;
        private const float OpponentDeckX = 0.73f;
        private const float OpponentSpellTrapY = 0.72f;
        private const float OpponentMonsterY = 0.56f;
        private const float HumanMonsterY = 0.39f;
        private const float HumanSpellTrapY = 0.22f;
        private static DuelPrototypeApp instance;
        private static Sprite activationBlockedIconSprite;

        private readonly Color backgroundColor = new Color(0.035f, 0.055f, 0.085f);
        private readonly Color panelColor = new Color(0.075f, 0.105f, 0.15f);
        private readonly Color accentColor = new Color(0.15f, 0.76f, 0.72f);
        private readonly Color mutedColor = new Color(0.42f, 0.5f, 0.6f);

        private PlayerProfile profile;
        private DuelEngine engine;
        private MockDuelSimulator mockDuelSimulator;
        private Canvas canvas;
        private CanvasGroup canvasInputGroup;
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
        private CardInstance selectedRitualMonster;
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
        private readonly HashSet<CardInstance> selectedPowerToolCards = new HashSet<CardInstance>();
        private List<CardDefinition> lastPack = new List<CardDefinition>();
        private string notice = "";
        private Font font;
        private bool matchRewardGranted;
        private bool forfeitConfirmOpen;
        private bool coinTossOpen;
        private bool coinFlipInProgress;
        private bool coinFlipResolved;
        private bool playerCalledHeads;
        private bool playerWonCoinFlip;
        private string coinTossResult = "";
        private Coroutine coinFlipRoutine;
        private int firstAiDeckIndex;
        private int secondAiDeckIndex = 1;
        private int deckPage;
        private string deckSearchQuery = "";
        private int deckSortMode;
        private CardLibraryFilter cardLibraryFilter;
        private bool cardFilterOptionsOpen;
        private int deckLibraryPage;
        private float deckLibraryScrollPosition = 1f;
        private int cardLibraryPage;
        private float cardLibraryScrollPosition = 1f;
        private string cardLibrarySearchQuery = "";
        private CardDefinition selectedTestCard;
        private string testCardSearchQuery = "";
        private bool showFailedUnitTests;
        private int testCardPage;
        private float testCardScrollPosition = 1f;
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
        private readonly Queue<PhaseTickerMessage> phaseTickerQueue = new Queue<PhaseTickerMessage>();
        private Action phaseTickerCompletion;
        private readonly Dictionary<CardInstance, CardVisualState> pendingDrawnCardStates =
            new Dictionary<CardInstance, CardVisualState>();
        private readonly Dictionary<CardInstance, CardVisualState> pendingHandLayoutStarts =
            new Dictionary<CardInstance, CardVisualState>();
        private readonly Dictionary<CardInstance, HashSet<CardInstance>> pendingTributeSummons =
            new Dictionary<CardInstance, HashSet<CardInstance>>();
        private readonly Dictionary<CardInstance, CardVisualState> pendingDestructionStates =
            new Dictionary<CardInstance, CardVisualState>();
        private readonly HashSet<CardInstance> animatedHandCards = new HashSet<CardInstance>();
        private readonly HashSet<CardInstance> animatingCardArrivals = new HashSet<CardInstance>();
        private Dictionary<CardInstance, CardVisualState> lastCardVisualStates;
        private int cardVisualBatchId;
        private CardInstance draggingHandCard;
        private RectTransform draggedCardVisual;
        private CanvasGroup draggedCardSourceGroup;
        private bool dragDropHandled;
        private CardInstance pendingTributeSummonCard;
        private int pendingTributeSummonZone = -1;
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
        private readonly List<DuelStateSnapshot> replayStates = new List<DuelStateSnapshot>();
        private int replayIndex = -1;
        private float nextAiStepTime;
        private readonly float[] aiAutoplayPaces = { 0f, 5f, 10f, 15f };
        private int aiAutoplayPaceIndex;
        private const float AiCommandDelay = 0.35f;
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
            Render();
        }

        private void Update()
        {
            if (screen == ScreenMode.AiLab && engine != null && engine.IsAiVsAi &&
                engine.Phase != DuelPhase.GameOver && Keyboard.current != null &&
                Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                aiAutoplay = !aiAutoplay;
                nextAiStepTime = Time.unscaledTime + AiCommandDelay;
                Render();
            }

            if (engine == null || engine.Phase == DuelPhase.GameOver)
                return;

            if ((screen == ScreenMode.Duel ||
                 (screen == ScreenMode.TestLab && mockDuelSimulator != null)) &&
                engine.IsHumanActionWindow && !engine.ResponseWindowOpen && !engine.SearchChoiceOpen &&
                !engine.QuickdrawDiscardChoiceOpen &&
                IsAutomaticHumanPhase() &&
                !IsPresentationInProgress() && Time.unscaledTime >= nextAiStepTime)
            {
                engine.AdvancePhase();
                RecordReplayState();
                nextAiStepTime = Time.unscaledTime + AiCommandDelay;
                Render();
                return;
            }

            bool playAiTurn = screen == ScreenMode.Duel &&
                engine.CurrentPlayer != engine.Human && engine.HasPendingAiActions;
            bool autoplayAiLab = screen == ScreenMode.AiLab && aiAutoplay && engine.IsAiVsAi;
            if ((!playAiTurn && !autoplayAiLab) || engine.ResponseWindowOpen || engine.SearchChoiceOpen ||
                engine.QuickdrawDiscardChoiceOpen ||
                IsPresentationInProgress() || Time.unscaledTime < nextAiStepTime)
                return;

            int previousTurn = engine.TurnNumber;
            if (!engine.StepAiTurn())
                return;
            RecordReplayState();

            if (engine.Phase == DuelPhase.GameOver)
                aiAutoplay = false;
            float delay = AiCommandDelay;
            if (screen == ScreenMode.AiLab && engine.TurnNumber != previousTurn)
                delay += aiAutoplayPaces[aiAutoplayPaceIndex];
            nextAiStepTime = Time.unscaledTime + delay;
            Render();
            RefreshInputState();
        }

        private void LoadProfile()
        {
            string json = PlayerPrefs.GetString(ProfileKey, "");
            profile = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<PlayerProfile>(json);
            if (profile == null || profile.collection == null || profile.deck == null)
            {
                profile = new PlayerProfile { credits = 500, catalogVersion = 5 };
                AddPresetToProfile(StarterCatalog.StructureDecks[0]);
                SaveProfile();
            }
            else if (profile.catalogVersion < 5)
            {
                profile.deck.RemoveAll(id => StarterCatalog.Find(id) == null);
                profile.collection.RemoveAll(item => item == null || StarterCatalog.Find(item.id) == null);
                ClampDeckToCopyLimits();
                int mainCount = profile.deck.Select(StarterCatalog.Find)
                    .Count(card => card != null && card.CanBeMainDeck);
                if (mainCount < DuelEngine.MinimumMainDeckSize)
                {
                    profile.deck.Clear();
                    AddPresetToProfile(StarterCatalog.StructureDecks[0]);
                }
                profile.catalogVersion = 5;
                SaveProfile();
            }
        }

        private void ClampDeckToCopyLimits()
        {
            Dictionary<string, int> copiesByCard = new Dictionary<string, int>(StringComparer.Ordinal);
            profile.deck.RemoveAll(id =>
            {
                CardDefinition definition = StarterCatalog.Find(id);
                if (definition == null)
                    return true;
                copiesByCard.TryGetValue(id, out int copies);
                if (copies >= definition.MaxDeckCopies)
                    return true;
                copiesByCard[id] = copies + 1;
                return false;
            });
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

        private void StartDuel(bool humanGoesFirst)
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
            if (mainCount < DuelEngine.MinimumMainDeckSize || mainCount > 40 ||
                !HasValidDeckCopyCounts(deck))
            {
                notice = mainCount < DuelEngine.MinimumMainDeckSize
                    ? "Your Main Deck needs at least 40 cards. Restore it in Deck Builder to start a duel."
                    : mainCount > 40
                        ? "Your Main Deck cannot exceed 40 cards. Correct it in Deck Builder before starting a duel."
                        : "Your deck exceeds a card's copy limit. Correct it in Deck Builder before starting a duel.";
                engine = null;
                return;
            }

            engine = new DuelEngine();
            BindEngineEvents(false);
            engine.StartMatch(deck, StarterCatalog.CreateAiDeck(), humanGoesFirst: humanGoesFirst);
            ResetReplayHistory();
            RecordReplayState();
            OnPhaseStarted(engine.Phase);
            ClearSelection();
        }

        private void StartDuelFromMenu()
        {
            List<CardDefinition> deck = profile.deck
                .Select(StarterCatalog.Find)
                .Where(card => card != null)
                .ToList();
            int mainCount = deck.Count(card => card.CanBeMainDeck);
            if (mainCount < DuelEngine.MinimumMainDeckSize || mainCount > 40 ||
                !HasValidDeckCopyCounts(deck))
            {
                notice = mainCount < DuelEngine.MinimumMainDeckSize
                    ? "Your Main Deck needs at least 40 cards. Restore it in Deck Builder to start a duel."
                    : mainCount > 40
                        ? "Your Main Deck cannot exceed 40 cards. Correct it in Deck Builder before starting a duel."
                        : "Your deck exceeds a card's copy limit. Correct it in Deck Builder before starting a duel.";
                engine = null;
                Render();
                return;
            }

            engine = null;
            coinTossOpen = true;
            coinFlipInProgress = false;
            coinFlipResolved = false;
            coinTossResult = "";
            notice = "";
            ClearSelection();
            Render();
        }

        private void BeginDuelAfterCoinToss(bool humanGoesFirst)
        {
            coinTossOpen = false;
            coinFlipInProgress = false;
            coinFlipResolved = false;
            coinFlipRoutine = null;
            StartDuel(humanGoesFirst);
            Render();
        }

        private void CallCoinSide(bool callHeads)
        {
            if (!coinTossOpen || coinFlipInProgress || coinFlipResolved)
                return;

            playerCalledHeads = callHeads;
            coinFlipInProgress = true;
            Render();
            coinFlipRoutine = StartCoroutine(AnimateCoinFlip());
        }

        private IEnumerator AnimateCoinFlip()
        {
            RectTransform selector = CreateRect("Coin Toss Selector", (RectTransform)canvas.transform);
            selector.anchorMin = new Vector2(0.5f, 0.5f);
            selector.anchorMax = new Vector2(0.5f, 0.5f);
            selector.anchoredPosition = Vector2.zero;
            selector.sizeDelta = new Vector2(360f, 160f);
            selector.SetAsLastSibling();

            Image[] sideImages = new Image[2];
            Text[] sideLabels = new Text[2];
            string[] sideNames = { "HEADS", "TAILS" };
            Color highlightedColor = new Color(0.9f, 0.66f, 0.2f);
            Color idleColor = new Color(0.22f, 0.28f, 0.36f);
            int selectedSide = UnityEngine.Random.Range(0, 2);

            for (int side = 0; side < sideNames.Length; side++)
            {
                RectTransform square = CreateRect(sideNames[side] + " Square", selector);
                square.anchorMin = new Vector2(0.5f, 0.5f);
                square.anchorMax = new Vector2(0.5f, 0.5f);
                square.anchoredPosition = new Vector2(
                    side == 0 ? -CoinSideButtonOffset : CoinSideButtonOffset, 0f);
                square.sizeDelta = new Vector2(CoinSideButtonSize, CoinSideButtonSize);
                sideImages[side] = square.gameObject.AddComponent<Image>();

                RectTransform labelRect = CreateRect(sideNames[side] + " Label", square);
                SetAnchors(labelRect, 0.04f, 0.04f, 0.96f, 0.96f);
                sideLabels[side] = labelRect.gameObject.AddComponent<Text>();
                sideLabels[side].font = font;
                sideLabels[side].text = sideNames[side];
                sideLabels[side].fontSize = 22;
                sideLabels[side].alignment = TextAnchor.MiddleCenter;
                sideLabels[side].fontStyle = FontStyle.Bold;
                sideLabels[side].raycastTarget = false;
            }

            bool resultHeads = UnityEngine.Random.value < 0.5f;
            int resultSide = resultHeads ? 0 : 1;
            int transitions = UnityEngine.Random.Range(18, 25);
            if ((selectedSide + transitions) % 2 != resultSide)
                transitions++;

            for (int transition = 0; transition <= transitions; transition++)
            {
                if (transition > 0)
                    selectedSide = 1 - selectedSide;

                for (int side = 0; side < sideImages.Length; side++)
                {
                    bool isSelected = side == selectedSide;
                    sideImages[side].color = isSelected ? highlightedColor : idleColor;
                    sideLabels[side].color = isSelected ? new Color(0.16f, 0.12f, 0.06f) : Color.white;
                }

                if (transition == transitions)
                    break;

                float progress = transition / (float)(transitions - 1);
                float interval = Mathf.Lerp(0.045f, 0.32f, progress * progress);
                yield return new WaitForSecondsRealtime(interval);
            }

            resultHeads = selectedSide == 0;
            yield return new WaitForSecondsRealtime(0.65f);
            Destroy(selector.gameObject);

            playerWonCoinFlip = resultHeads == playerCalledHeads;
            coinFlipResolved = true;
            if (playerWonCoinFlip)
            {
                coinTossResult = "You won the coin toss. Choose whether to go first or second.";
                coinFlipInProgress = false;
                coinFlipRoutine = null;
                Render();
                yield break;
            }

            bool aiGoesFirst = UnityEngine.Random.value < 0.5f;
            coinTossResult = "CPU RIVAL won and chose to go " + (aiGoesFirst ? "first." : "second.");
            Render();
            yield return new WaitForSecondsRealtime(1.25f);
            coinFlipInProgress = false;
            BeginDuelAfterCoinToss(!aiGoesFirst);
        }

        private void ForfeitDuel()
        {
            if (screen != ScreenMode.Duel || engine == null || engine.Phase == DuelPhase.GameOver)
                return;

            forfeitConfirmOpen = false;
            Render();
            QueuePhaseTicker("FORFEIT", () =>
            {
                engine = null;
                matchRewardGranted = false;
                notice = "";
                ClearSelection();
                graveyardOwner = null;
                selectedGraveyardCard = null;
                deckOwner = null;
                selectedDeckCard = null;
                extraDeckOpen = false;
                forfeitConfirmOpen = false;
                Render();
            });
        }

        private void OpenForfeitConfirmation()
        {
            if (screen != ScreenMode.Duel || engine == null || engine.Phase == DuelPhase.GameOver)
                return;

            forfeitConfirmOpen = true;
            Render();
        }

        private void QueuePhaseTicker(
            string label,
            Action onComplete = null,
            Color? backgroundColor = null,
            Color? edgeColor = null)
        {
            phaseTickerQueue.Enqueue(new PhaseTickerMessage
            {
                Label = label,
                BackgroundColor = backgroundColor ?? new Color(0.035f, 0.055f, 0.085f, 1f),
                AccentColor = edgeColor ?? accentColor
            });
            if (onComplete != null)
                phaseTickerCompletion = onComplete;
            TryStartNextPresentation();
        }

        private void TryStartNextPresentation()
        {
            if (cardAnimationRoutine != null || attackAnimationRoutine != null || phaseTickerRoutine != null)
            {
                RefreshInputState();
                return;
            }

            if (cardAnimationQueue.Count > 0)
                cardAnimationRoutine = StartCoroutine(PlayCardAnimations());
            else if (attackAnimationQueue.Count > 0)
                attackAnimationRoutine = StartCoroutine(PlayAttackAnimations());
            else if (phaseTickerQueue.Count > 0)
                phaseTickerRoutine = StartCoroutine(PlayPhaseTicker());

            RefreshInputState();
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
            ResetReplayHistory();
            RecordReplayState();
            aiAutoplay = true;
            nextAiStepTime = Time.unscaledTime + AiCommandDelay;
            ClearSelection();
            screen = ScreenMode.AiLab;
            Render();
        }

        private void SimulateAiLabDuel()
        {
            IReadOnlyList<StructureDeckPreset> decks = StarterCatalog.StructureDecks;
            if (decks.Count < 2)
                return;

            StructureDeckPreset first = decks[firstAiDeckIndex];
            StructureDeckPreset second = decks[secondAiDeckIndex];
            DuelEngine replayEngine = new DuelEngine();
            replayEngine.StartMatch(first.MainDeck.Concat(first.ExtraDeck).ToList(),
                second.MainDeck.Concat(second.ExtraDeck).ToList(), true, first.Name, second.Name);

            List<DuelStateSnapshot> history =
                new List<DuelStateSnapshot> { replayEngine.CaptureStateForReplay() };
            while (replayEngine.Phase != DuelPhase.GameOver)
            {
                if (!replayEngine.StepAiTurn())
                    break;
                history.Add(replayEngine.CaptureStateForReplay());
            }

            replayStates.Clear();
            replayStates.AddRange(history);
            replayIndex = Mathf.Max(0, replayStates.Count - 1);
            engine = replayEngine;
            aiAutoplay = false;
            screen = ScreenMode.AiLab;
            Render();
        }

        private void ResetReplayHistory()
        {
            replayStates.Clear();
            replayIndex = -1;
        }

        private void RecordReplayState()
        {
            if (engine == null || engine.ResponseWindowOpen ||
                engine.ResponseDiscardChoiceOpen || engine.SearchChoiceOpen)
                return;

            replayStates.Add(engine.CaptureStateForReplay());
            replayIndex = replayStates.Count - 1;
        }

        private void RestoreReplayState(int index)
        {
            if (replayStates.Count == 0)
                return;

            replayIndex = Mathf.Clamp(index, 0, replayStates.Count - 1);
            engine = DuelEngine.RestoreState(replayStates[replayIndex]);
            aiAutoplay = false;
            ClearSelection();
            Render();
        }

        private void ReplayToStart()
        {
            RestoreReplayState(0);
        }

        private void ReplayToEnd()
        {
            if (replayStates.Count > 0)
                RestoreReplayState(replayStates.Count - 1);
        }

        private void ReplayStepBack()
        {
            if (replayIndex > 0)
                RestoreReplayState(replayIndex - 1);
        }

        private void ReplayStepForward()
        {
            if (replayIndex < replayStates.Count - 1)
                RestoreReplayState(replayIndex + 1);
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
            cardPreviewRoot.gameObject.AddComponent<CanvasGroup>().blocksRaycasts = false;
        }

        private void SwitchScreen(ScreenMode mode)
        {
            forfeitConfirmOpen = false;
            bool returnToDuelStart = mode == ScreenMode.Duel && engine != null &&
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
            if (returnToDuelStart)
            {
                mockDuelSimulator = null;
                engine = null;
            }
            screen = mode;
            Render();
        }

        private void DrawBackToMainMenuButton()
        {
            string label = screen == ScreenMode.Duel && engine != null &&
                engine.Phase != DuelPhase.GameOver
                ? "FORFEIT"
                : "← MAIN MENU";
            AddButton(contentRoot, label, 0.85f, 0.925f, 0.98f, 0.98f,
                new Color(0.15f, 0.22f, 0.31f), ReturnToMainMenu, 11);
        }

        private void ReturnToMainMenu()
        {
            if (screen == ScreenMode.Duel && engine != null &&
                engine.Phase != DuelPhase.GameOver)
            {
                OpenForfeitConfirmation();
                return;
            }

            if (screen == ScreenMode.Duel)
            {
                engine = null;
                matchRewardGranted = false;
                ClearSelection();
                Render();
                return;
            }

            SwitchScreen(ScreenMode.Duel);
        }

        private void Render()
        {
            if (contentRoot == null)
                return;
            BindEngineEvents();
            TrackCardVisualChanges();
            HideCardPreview();
            AwardMatchIfNeeded();
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

            if (screen != ScreenMode.Duel || engine != null)
                DrawBackToMainMenuButton();
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
            if ((screen == ScreenMode.Duel || screen == ScreenMode.AiLab) && replayStates.Count > 0)
                DrawReplayControls();
            if (engine != null && engine.SearchChoiceOpen &&
                (screen == ScreenMode.Duel || screen == ScreenMode.TestLab))
                DrawSanganSearchOverlay();
            if (engine != null && engine.PowerToolChoiceOpen && screen == ScreenMode.Duel)
                DrawPowerToolChoiceOverlay();
            if (engine != null && engine.QuickdrawDiscardChoiceOpen && screen == ScreenMode.Duel)
                DrawQuickdrawDiscardOverlay();
            if (forfeitConfirmOpen && screen == ScreenMode.Duel && engine != null)
                DrawForfeitConfirmationOverlay();
            if (pendingTributeSummonCard != null && screen == ScreenMode.Duel && engine != null)
                DrawTributeSelectionOverlay();
            RefreshInputState();
        }

        private void BindEngineEvents(bool showCurrentPhase = true)
        {
            if (observedEngine == engine)
                return;

            if (observedEngine != null)
            {
                observedEngine.PhaseChanged -= OnPhaseStarted;
                observedEngine.AttackDeclared -= OnAttackDeclared;
                observedEngine.CardDrawn -= OnCardDrawn;
                observedEngine.TributeSummoned -= OnTributeSummoned;
                observedEngine.MonsterDestroyed -= OnMonsterDestroyed;
            }

            observedEngine = engine;
            lastCardVisualStates = null;
            pendingDrawnCardStates.Clear();
            pendingHandLayoutStarts.Clear();
            pendingTributeSummons.Clear();
            pendingDestructionStates.Clear();
            animatedHandCards.Clear();
            animatingCardArrivals.Clear();
            if (observedEngine == null)
                return;

            observedEngine.PhaseChanged += OnPhaseStarted;
            observedEngine.AttackDeclared += OnAttackDeclared;
            observedEngine.CardDrawn += OnCardDrawn;
            observedEngine.TributeSummoned += OnTributeSummoned;
            observedEngine.MonsterDestroyed += OnMonsterDestroyed;
            if (showCurrentPhase)
                OnPhaseStarted(observedEngine.Phase);
        }

        private bool IsAutomaticHumanPhase()
        {
            return engine.Phase == DuelPhase.Draw || engine.Phase == DuelPhase.Standby ||
                   (engine.Phase == DuelPhase.End && !HasEndPhaseAction());
        }

        private bool HasEndPhaseAction()
        {
            return engine.Human.Hand.Any(card =>
                card.Definition.Kind == CardKind.Spell &&
                card.Definition.Subtype == CardSubtype.QuickPlay &&
                card.Definition.Effect != CardEffect.None);
        }

        private bool IsPresentationInProgress()
        {
            return cardAnimationRoutine != null || cardAnimationQueue.Count > 0 ||
                   attackAnimationRoutine != null || attackAnimationQueue.Count > 0 ||
                   phaseTickerRoutine != null || phaseTickerQueue.Count > 0;
        }

        private void RefreshInputState()
        {
            if (canvasInputGroup == null)
                return;

            bool aiTurnInProgress = screen == ScreenMode.Duel && engine != null &&
                engine.CurrentPlayer != engine.Human && engine.HasPendingAiActions &&
                !engine.ResponseWindowOpen && !engine.SearchChoiceOpen;
            canvasInputGroup.interactable = !IsPresentationInProgress() && !aiTurnInProgress &&
                                            !coinFlipInProgress;
            canvasInputGroup.blocksRaycasts = true;
        }

        private void TrackCardVisualChanges()
        {
            if (engine == null)
            {
                lastCardVisualStates = null;
                return;
            }

            int batchId = ++cardVisualBatchId;
            Dictionary<CardInstance, CardVisualState> currentStates = CaptureCardVisualStates();
            HashSet<CardInstance> destructionCards = new HashSet<CardInstance>();
            foreach (KeyValuePair<CardInstance, CardVisualState> entry in pendingDestructionStates)
            {
                if (entry.Value.Zone != CardVisualZone.Monster)
                    continue;

                CardInstance card = entry.Key;
                bool reachedGraveyardOrBanished =
                    currentStates.TryGetValue(card, out CardVisualState destination) &&
                    (destination.Zone == CardVisualZone.Graveyard ||
                     destination.Zone == CardVisualZone.Banished);
                bool leftField = !currentStates.ContainsKey(card);
                if (!reachedGraveyardOrBanished && !leftField)
                    continue;

                destructionCards.Add(card);
                Vector2 from = GetCardVisualPosition(entry.Value);
                cardAnimationQueue.Enqueue(new CardMovementAnimation
                {
                    Card = card,
                    From = from,
                    To = reachedGraveyardOrBanished ? GetCardVisualPosition(destination) : from,
                    StartSize = GetCardVisualSize(entry.Value),
                    EndSize = reachedGraveyardOrBanished ? GetCardVisualSize(destination) :
                        GetCardVisualSize(entry.Value),
                    StartsFaceUp = CardIsVisible(entry.Value),
                    EndsFaceUp = reachedGraveyardOrBanished && CardIsVisible(destination),
                    IsDestruction = true,
                    BatchId = batchId
                });
            }

            if (lastCardVisualStates != null || pendingHandLayoutStarts.Count > 0)
            {
                foreach (KeyValuePair<CardInstance, CardVisualState> entry in currentStates)
                {
                    CardInstance card = entry.Key;
                    if (destructionCards.Contains(card))
                        continue;

                    CardVisualState previous = null;
                    bool hasPrevious = lastCardVisualStates != null &&
                        lastCardVisualStates.TryGetValue(card, out previous);
                    bool isDraw = pendingDrawnCardStates.TryGetValue(card, out CardVisualState drawnState);
                    if (!hasPrevious && !pendingHandLayoutStarts.TryGetValue(card, out previous) && !isDraw)
                        continue;

                    CardVisualState current = entry.Value;
                    CardVisualState movementStart = previous;
                    if (isDraw && current.Zone != CardVisualZone.Hand)
                    {
                        movementStart = drawnState;
                    }
                    else if (isDraw && (movementStart == null || movementStart.Zone != CardVisualZone.Deck))
                        movementStart = CreateDeckVisualState(drawnState.Owner);
                    if (movementStart == null)
                        continue;

                    bool zoneChanged = movementStart.Zone != current.Zone || movementStart.Owner != current.Owner;
                    bool handLayoutChanged = movementStart.Zone == CardVisualZone.Hand &&
                        current.Zone == CardVisualZone.Hand && movementStart.Owner == current.Owner &&
                        (movementStart.Index != current.Index ||
                         movementStart.ContainerCount != current.ContainerCount);
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
                    bool isSet = zoneChanged && current.Zone == CardVisualZone.Monster &&
                        !current.FaceUp && !current.AttackPosition &&
                        card.SummonedTurnNumber == engine.TurnNumber;
                    bool showPopup = !isSet && (effectStarted || activatedFromHand || revealedTrap);
                    if (!zoneChanged && !showPopup && !revealed && !handLayoutChanged)
                        continue;

                    if ((zoneChanged && IsBoardZone(current.Zone)) ||
                        (revealed && current.Zone == CardVisualZone.Monster))
                        animatingCardArrivals.Add(card);
                    bool isTributeSummon = pendingTributeSummons.ContainsKey(card);
                    cardAnimationQueue.Enqueue(new CardMovementAnimation
                    {
                        Card = card,
                        From = GetCardVisualPosition(movementStart),
                        To = GetCardVisualPosition(current),
                        StartSize = GetCardVisualSize(movementStart),
                        EndSize = GetCardVisualSize(current),
                        ShowPopup = showPopup,
                        StartsFaceUp = CardIsVisible(movementStart),
                        EndsFaceUp = CardIsVisible(current),
                        IsHandReflow = handLayoutChanged,
                        IsHandDeparture = zoneChanged && movementStart.Zone == CardVisualZone.Hand &&
                                          current.Zone != CardVisualZone.Hand,
                        IsDraw = isDraw && current.Zone == CardVisualZone.Hand,
                        IsTributeSummon = isTributeSummon,
                        IsTributedCard = pendingTributeSummons.Values.Any(materials =>
                            materials.Contains(card)),
                        IsSet = isSet,
                        BatchId = batchId,
                        IsSummon = zoneChanged && current.Zone == CardVisualZone.Monster &&
                                   current.FaceUp &&
                                   card.SummonedTurnNumber == engine.TurnNumber
                    });
                }
            }

            pendingDrawnCardStates.Clear();
            pendingHandLayoutStarts.Clear();
            pendingTributeSummons.Clear();
            pendingDestructionStates.Clear();
            lastCardVisualStates = currentStates;
            TryStartNextPresentation();
        }

        private CardVisualState CreateDeckVisualState(DuelPlayer owner)
        {
            return new CardVisualState
            {
                Owner = owner,
                Zone = CardVisualZone.Deck,
                Index = 0,
                ContainerCount = owner.Deck.Count + 1,
                FaceUp = false
            };
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
                AttackPosition = card.AttackPosition,
                EffectUsedThisTurn = card.EffectUsedThisTurn
            };
        }

        private bool CardIsVisible(CardVisualState state)
        {
            if (state.Zone == CardVisualZone.Deck || state.Zone == CardVisualZone.ExtraDeck ||
                (state.Owner.IsAi && state.Zone == CardVisualZone.Hand &&
                 !(screen == ScreenMode.AiLab && engine != null && engine.IsAiVsAi)))
                return false;
            if (state.Zone == CardVisualZone.Hand &&
                screen == ScreenMode.AiLab && engine != null && engine.IsAiVsAi)
                return true;
            return state.Zone == CardVisualZone.Graveyard || state.Zone == CardVisualZone.Banished ||
                   state.FaceUp;
        }

        private static bool IsBoardZone(CardVisualZone zone)
        {
            return zone == CardVisualZone.Monster ||
                   zone == CardVisualZone.SpellTrap ||
                   zone == CardVisualZone.FieldSpell;
        }

        private Vector2 GetCardVisualPosition(CardVisualState state)
        {
            switch (state.Zone)
            {
                case CardVisualZone.Hand:
                    return DuelHandLayout.GetCardCenter(state.Index, state.ContainerCount,
                        GetScreenAspectRatio(), state.Owner == engine.Opponent);
                case CardVisualZone.Monster:
                    return new Vector2(MonsterCardStartX + state.Index * MonsterCardStepX + BoardCardWidth * 0.5f,
                        GetMonsterZoneY(state.Owner) + BoardCardHeight * 0.5f);
                case CardVisualZone.SpellTrap:
                    return new Vector2(MonsterCardStartX + state.Index * MonsterCardStepX + BoardCardWidth * 0.5f,
                        GetSpellTrapZoneY(state.Owner) + BoardCardHeight * 0.5f);
                case CardVisualZone.FieldSpell:
                    return state.Owner == engine.Opponent
                        ? new Vector2(OpponentDeckX + BoardCardWidth * 0.5f,
                            GetFieldSpellZoneY(true) + BoardCardHeight * 0.5f)
                        : new Vector2(0.205f + BoardCardWidth * 0.5f,
                            GetFieldSpellZoneY(false) + BoardCardHeight * 0.5f);
                case CardVisualZone.Graveyard:
                    return state.Owner == engine.Opponent
                        ? new Vector2(0.205f + BoardCardWidth * 0.5f,
                            GetSpellTrapZoneY(state.Owner) + BoardCardHeight * 0.5f)
                        : new Vector2(0.74f + BoardCardWidth * 0.5f,
                            GetSpellTrapZoneY(state.Owner) + BoardCardHeight * 0.5f);
                case CardVisualZone.Banished:
                    return state.Owner == engine.Opponent
                        ? new Vector2(0.205f + BoardCardWidth * 0.5f,
                            GetMonsterZoneY(state.Owner) + BoardCardHeight * 0.5f)
                        : new Vector2(0.74f + BoardCardWidth * 0.5f,
                            GetMonsterZoneY(state.Owner) + BoardCardHeight * 0.5f);
                case CardVisualZone.ExtraDeck:
                    return new Vector2(0.86f + 0.115f * 0.5f, 0.018f + 0.112f * 0.5f);
                default:
                    return state.Owner == engine.Opponent
                        ? new Vector2(OpponentDeckX + BoardCardWidth * 0.5f,
                            GetSpellTrapZoneY(state.Owner) + BoardCardHeight * 0.5f)
                        : new Vector2(0.205f + BoardCardWidth * 0.5f,
                            GetSpellTrapZoneY(state.Owner) + BoardCardHeight * 0.5f);
            }
        }

        private float GetMonsterZoneY(DuelPlayer owner)
        {
            return owner == engine.Opponent ? OpponentMonsterY : HumanMonsterY;
        }

        private float GetSpellTrapZoneY(DuelPlayer owner)
        {
            return owner == engine.Opponent ? OpponentSpellTrapY : HumanSpellTrapY;
        }

        private float GetFieldSpellZoneY(bool opponent)
        {
            return opponent ? OpponentMonsterY : HumanMonsterY;
        }

        private Vector2 GetCardVisualSize(CardVisualState state)
        {
            Rect canvasRect = ((RectTransform)canvas.transform).rect;
            if (state.Zone == CardVisualZone.Hand)
            {
                bool opponent = state.Owner == engine.Opponent;
                float width = DuelHandLayout.GetCardWidth(state.ContainerCount,
                    GetScreenAspectRatio(), opponent);
                return new Vector2(width * canvasRect.width, GetPortraitHeight(width) * canvasRect.height);
            }
            return new Vector2(BoardCardWidth * canvasRect.width, BoardCardHeight * canvasRect.height);
        }

        private void OnCardDrawn(DuelPlayer owner, CardInstance card)
        {
            if (owner == null || card == null)
                return;

            if (lastCardVisualStates == null)
            {
                int previousHandCount = owner.Hand.Count - 1;
                for (int i = 0; i < previousHandCount; i++)
                {
                    CardInstance handCard = owner.Hand[i];
                    if (!pendingHandLayoutStarts.ContainsKey(handCard))
                    {
                        pendingHandLayoutStarts[handCard] = new CardVisualState
                        {
                            Owner = owner,
                            Zone = CardVisualZone.Hand,
                            Index = i,
                            ContainerCount = previousHandCount,
                            FaceUp = handCard.FaceUp,
                            AttackPosition = handCard.AttackPosition,
                            EffectUsedThisTurn = handCard.EffectUsedThisTurn
                        };
                    }
                }
            }

            CardVisualState handState = new CardVisualState
            {
                Owner = owner,
                Zone = CardVisualZone.Hand,
                Index = owner.Hand.IndexOf(card),
                ContainerCount = owner.Hand.Count,
                FaceUp = card.FaceUp,
                AttackPosition = card.AttackPosition,
                EffectUsedThisTurn = card.EffectUsedThisTurn
            };
            pendingDrawnCardStates[card] = handState;
            if (canvas != null)
                animatedHandCards.Add(card);
        }

        private void OnTributeSummoned(
            DuelPlayer owner, CardInstance summonedCard, IReadOnlyList<CardInstance> tributes)
        {
            if (owner == null || summonedCard == null || tributes == null || tributes.Count == 0)
                return;
            pendingTributeSummons[summonedCard] = new HashSet<CardInstance>(tributes);
        }

        private void OnMonsterDestroyed(DuelPlayer owner, CardInstance monster)
        {
            if (owner == null || monster == null || engine == null)
                return;

            Dictionary<CardInstance, CardVisualState> states = CaptureCardVisualStates();
            if (states.TryGetValue(monster, out CardVisualState state) &&
                state.Zone == CardVisualZone.Monster)
                pendingDestructionStates[monster] = state;
        }

        private IEnumerator PlayCardAnimations()
        {
            while (cardAnimationQueue.Count > 0)
            {
                CardMovementAnimation animation = cardAnimationQueue.Dequeue();
                if (animation.IsDestruction)
                {
                    List<CardMovementAnimation> destroyedCards = new List<CardMovementAnimation>
                    {
                        animation
                    };
                    int queuedCount = cardAnimationQueue.Count;
                    Queue<CardMovementAnimation> laterAnimations = new Queue<CardMovementAnimation>();
                    for (int i = 0; i < queuedCount; i++)
                    {
                        CardMovementAnimation queued = cardAnimationQueue.Dequeue();
                        if (queued.IsDestruction && queued.BatchId == animation.BatchId)
                            destroyedCards.Add(queued);
                        else
                            laterAnimations.Enqueue(queued);
                    }
                    while (laterAnimations.Count > 0)
                        cardAnimationQueue.Enqueue(laterAnimations.Dequeue());

                    yield return PlayDestructionAnimations(destroyedCards);
                    continue;
                }

                bool coordinatedBatch = animation.BatchId != 0 &&
                    (animation.IsDraw || animation.IsHandDeparture || animation.IsTributeSummon ||
                     cardAnimationQueue.Any(item => item.BatchId == animation.BatchId &&
                         (item.IsDraw || item.IsHandDeparture || item.IsTributeSummon)));
                if (coordinatedBatch)
                {
                    List<CardMovementAnimation> batch = new List<CardMovementAnimation> { animation };
                    int queuedCount = cardAnimationQueue.Count;
                    Queue<CardMovementAnimation> laterAnimations = new Queue<CardMovementAnimation>();
                    for (int i = 0; i < queuedCount; i++)
                    {
                        CardMovementAnimation queued = cardAnimationQueue.Dequeue();
                        if (queued.BatchId == animation.BatchId)
                            batch.Add(queued);
                        else
                            laterAnimations.Enqueue(queued);
                    }
                    while (laterAnimations.Count > 0)
                        cardAnimationQueue.Enqueue(laterAnimations.Dequeue());

                    List<CardMovementAnimation> handReflow = batch
                        .Where(item => item.IsHandReflow)
                        .ToList();
                    CardMovementAnimation tributeSummon = batch.FirstOrDefault(item =>
                        item.IsTributeSummon);
                    if (tributeSummon != null)
                    {
                        yield return PlayTributeSummonAnimation(tributeSummon, batch, handReflow);
                        foreach (CardMovementAnimation other in batch.Where(item =>
                                     item != tributeSummon && !item.IsTributedCard &&
                                     !item.IsHandReflow))
                            yield return PlaySingleCardAnimation(other);
                    }
                    else if (batch.Any(item => item.IsDraw))
                    {
                        yield return PlayDrawArrivalAnimations(
                            batch.Where(item => item.IsDraw).ToList(), handReflow);
                        foreach (CardMovementAnimation other in batch.Where(item =>
                                     !item.IsDraw && !item.IsHandReflow))
                        {
                            if (other.IsHandDeparture)
                                yield return PlayHandDepartureAnimation(other, new List<CardMovementAnimation>());
                            else
                                yield return PlaySingleCardAnimation(other);
                        }
                    }
                    else
                    {
                        CardMovementAnimation playedCard = batch.First(item => item.IsHandDeparture);
                        yield return PlayHandDepartureAnimation(playedCard, handReflow);
                        foreach (CardMovementAnimation other in batch.Where(item =>
                                     item != playedCard && !item.IsHandReflow))
                            yield return PlaySingleCardAnimation(other);
                    }
                    continue;
                }

                if (animation.IsHandReflow)
                {
                    List<CardMovementAnimation> reflowGroup = new List<CardMovementAnimation> { animation };
                    while (cardAnimationQueue.Count > 0 && cardAnimationQueue.Peek().IsHandReflow)
                        reflowGroup.Add(cardAnimationQueue.Dequeue());

                    yield return PlayHandReflowAnimation(reflowGroup, 0.34f);
                    continue;
                }

                yield return PlaySingleCardAnimation(animation);
            }

            cardAnimationRoutine = null;
            if (animatedHandCards.Count > 0)
            {
                animatedHandCards.Clear();
                Render();
            }
            if (animatingCardArrivals.Count > 0)
            {
                animatingCardArrivals.Clear();
                Render();
            }
            TryStartNextPresentation();
        }

        private IEnumerator PlayHandDepartureAnimation(
            CardMovementAnimation playedCard,
            List<CardMovementAnimation> handReflow,
            bool showSummonPulse = true)
        {
            List<RectTransform> reflowVisuals = handReflow
                .Select(item => FindHandCardVisual(item.Card))
                .ToList();
            for (int i = 0; i < handReflow.Count; i++)
                SetCardVisualRect(reflowVisuals[i], handReflow[i].From, handReflow[i].StartSize);

            if (playedCard.IsSummon && showSummonPulse)
                yield return PlaySummonPulse(playedCard.To);

            RectTransform playedVisual = CreateAnimatedCard(playedCard.Card.Definition,
                playedCard.IsSet ? false : playedCard.StartsFaceUp,
                playedCard.From, playedCard.StartSize);
            if (playedCard.ShowPopup && !playedCard.IsSet)
            {
                yield return TweenCardAndHand(playedVisual, playedCard.From,
                    new Vector2(0.5f, 0.5f), playedCard.StartSize, new Vector2(190f, 266f),
                    handReflow, reflowVisuals, 0.3f);
                if (!playedCard.StartsFaceUp)
                    yield return FlipAnimatedCard(playedVisual, true);
                yield return new WaitForSecondsRealtime(0.8f);
                yield return TweenCardVisual(playedVisual, new Vector2(0.5f, 0.5f), playedCard.To,
                    new Vector2(190f, 266f), playedCard.EndSize, 0.3f);
            }
            else if (playedCard.IsSet)
            {
                yield return TweenSetCardAndHand(playedVisual, playedCard, handReflow,
                    reflowVisuals, 0.42f);
            }
            else
            {
                yield return TweenCardAndHand(playedVisual, playedCard.From, playedCard.To,
                    playedCard.StartSize, playedCard.EndSize, handReflow, reflowVisuals, 0.42f);
            }

            SetAnimatedCardPosition(playedVisual, playedCard.To);
            playedVisual.sizeDelta = playedCard.EndSize;
            for (int i = 0; i < handReflow.Count; i++)
                SetCardVisualRect(reflowVisuals[i], handReflow[i].To, handReflow[i].EndSize);
            if (playedCard.IsSet)
                playedVisual.localRotation = Quaternion.Euler(0f, 0f, 90f);
            else if (!playedCard.ShowPopup && playedCard.StartsFaceUp != playedCard.EndsFaceUp)
                yield return FlipAnimatedCard(playedVisual, playedCard.EndsFaceUp);
            yield return TweenCardVisual(playedVisual, playedCard.To, playedCard.To,
                playedCard.EndSize, playedCard.EndSize * 1.08f, 0.07f);
            yield return TweenCardVisual(playedVisual, playedCard.To, playedCard.To,
                playedCard.EndSize * 1.08f, playedCard.EndSize, 0.09f);

            Destroy(playedVisual.gameObject);
            CompleteCardArrival(playedCard.Card);
        }

        private IEnumerator TweenSetCardAndHand(
            RectTransform card,
            CardMovementAnimation setCard,
            List<CardMovementAnimation> handReflow,
            List<RectTransform> handVisuals,
            float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                float progress = Mathf.Clamp01(elapsed / duration);
                float eased = progress * progress * (3f - 2f * progress);
                SetAnimatedCardPosition(card, Vector2.Lerp(setCard.From, setCard.To, eased));
                card.sizeDelta = Vector2.Lerp(setCard.StartSize, setCard.EndSize, eased);
                card.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(0f, 90f, eased));
                for (int i = 0; i < handReflow.Count; i++)
                {
                    CardMovementAnimation item = handReflow[i];
                    SetCardVisualRect(handVisuals[i],
                        Vector2.Lerp(item.From, item.To, eased),
                        Vector2.Lerp(item.StartSize, item.EndSize, eased));
                }
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            SetAnimatedCardPosition(card, setCard.To);
            card.sizeDelta = setCard.EndSize;
            card.localRotation = Quaternion.Euler(0f, 0f, 90f);
            for (int i = 0; i < handReflow.Count; i++)
                SetCardVisualRect(handVisuals[i], handReflow[i].To, handReflow[i].EndSize);
        }

        private IEnumerator PlayDrawArrivalAnimations(
            List<CardMovementAnimation> drawnCards,
            List<CardMovementAnimation> handReflow)
        {
            List<RectTransform> drawVisuals = drawnCards.Select(item =>
                CreateAnimatedCard(item.Card.Definition, item.StartsFaceUp, item.From, item.StartSize))
                .ToList();
            List<RectTransform> handVisuals = handReflow
                .Select(item => FindHandCardVisual(item.Card))
                .ToList();
            for (int i = 0; i < handReflow.Count; i++)
                SetCardVisualRect(handVisuals[i], handReflow[i].From, handReflow[i].StartSize);

            const float duration = 0.48f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                float progress = Mathf.Clamp01(elapsed / duration);
                float eased = progress * progress * (3f - 2f * progress);
                for (int i = 0; i < drawnCards.Count; i++)
                {
                    CardMovementAnimation item = drawnCards[i];
                    SetAnimatedCardPosition(drawVisuals[i], Vector2.Lerp(item.From, item.To, eased));
                    drawVisuals[i].sizeDelta = Vector2.Lerp(item.StartSize, item.EndSize, eased);
                }
                for (int i = 0; i < handReflow.Count; i++)
                {
                    CardMovementAnimation item = handReflow[i];
                    SetCardVisualRect(handVisuals[i],
                        Vector2.Lerp(item.From, item.To, eased),
                        Vector2.Lerp(item.StartSize, item.EndSize, eased));
                }
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            for (int i = 0; i < drawnCards.Count; i++)
            {
                CardMovementAnimation item = drawnCards[i];
                SetAnimatedCardPosition(drawVisuals[i], item.To);
                drawVisuals[i].sizeDelta = item.EndSize;
            }
            for (int i = 0; i < handReflow.Count; i++)
                SetCardVisualRect(handVisuals[i], handReflow[i].To, handReflow[i].EndSize);

            for (int i = 0; i < drawnCards.Count; i++)
            {
                CardMovementAnimation item = drawnCards[i];
                if (item.StartsFaceUp != item.EndsFaceUp)
                    yield return FlipAnimatedCard(drawVisuals[i], item.EndsFaceUp);
                yield return TweenCardVisual(drawVisuals[i], item.To, item.To,
                    item.EndSize, item.EndSize * 1.08f, 0.07f);
                yield return TweenCardVisual(drawVisuals[i], item.To, item.To,
                    item.EndSize * 1.08f, item.EndSize, 0.09f);
                Destroy(drawVisuals[i].gameObject);
            }
        }

        private IEnumerator PlayDestructionAnimations(List<CardMovementAnimation> destroyedCards)
        {
            List<RectTransform> visuals = destroyedCards.Select(item =>
                CreateAnimatedCard(item.Card.Definition, item.StartsFaceUp, item.From, item.StartSize))
                .ToList();

            yield return PlayTributeDeathPulses(destroyedCards);

            for (int i = 0; i < destroyedCards.Count; i++)
            {
                if (!destroyedCards[i].StartsFaceUp)
                    yield return FlipAnimatedCard(visuals[i], true);
            }

            yield return TweenCardGroup(visuals, destroyedCards, 0.38f);
            foreach (RectTransform visual in visuals)
                Destroy(visual.gameObject);
        }

        private IEnumerator PlayTributeSummonAnimation(
            CardMovementAnimation summonedCard,
            List<CardMovementAnimation> batch,
            List<CardMovementAnimation> handReflow)
        {
            List<RectTransform> handVisuals = handReflow
                .Select(item => FindHandCardVisual(item.Card))
                .ToList();
            for (int i = 0; i < handReflow.Count; i++)
                SetCardVisualRect(handVisuals[i], handReflow[i].From, handReflow[i].StartSize);

            List<CardMovementAnimation> tributes = batch
                .Where(item => item.IsTributedCard)
                .ToList();
            if (tributes.Count > 0)
            {
                yield return PlayTributeDeathPulses(tributes);
                List<RectTransform> tributeVisuals = tributes.Select(item =>
                    CreateAnimatedCard(item.Card.Definition, item.StartsFaceUp, item.From, item.StartSize))
                    .ToList();
                yield return TweenCardGroup(tributeVisuals, tributes, 0.28f);
                foreach (RectTransform visual in tributeVisuals)
                    Destroy(visual.gameObject);
            }

            yield return PlaySummonPulse(summonedCard.To, new Color(1f, 0.68f, 0.2f, 1f));
            if (summonedCard.IsHandDeparture)
                yield return PlayHandDepartureAnimation(summonedCard, handReflow, false);
            else
                yield return PlaySingleCardAnimation(summonedCard, false);
        }

        private IEnumerator TweenCardGroup(
            List<RectTransform> visuals,
            List<CardMovementAnimation> animations,
            float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                float progress = Mathf.Clamp01(elapsed / duration);
                float eased = progress * progress * (3f - 2f * progress);
                for (int i = 0; i < animations.Count; i++)
                {
                    CardMovementAnimation item = animations[i];
                    SetAnimatedCardPosition(visuals[i], Vector2.Lerp(item.From, item.To, eased));
                    visuals[i].sizeDelta = Vector2.Lerp(item.StartSize, item.EndSize, eased);
                }
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            for (int i = 0; i < animations.Count; i++)
            {
                SetAnimatedCardPosition(visuals[i], animations[i].To);
                visuals[i].sizeDelta = animations[i].EndSize;
            }
        }

        private IEnumerator PlayTributeDeathPulses(List<CardMovementAnimation> tributes)
        {
            List<RectTransform> rings = new List<RectTransform>();
            List<Image> images = new List<Image>();
            List<float> endScales = new List<float>();
            foreach (CardMovementAnimation tribute in tributes)
            {
                CreateTributeDeathRing(tribute.From, 150f, 1.2f,
                    new Color(1f, 0.18f, 0.12f, 0.92f), rings, images, endScales);
                CreateTributeDeathRing(tribute.From, 104f, 0.92f,
                    new Color(1f, 0.62f, 0.18f, 0.9f), rings, images, endScales);
            }

            const float duration = 0.36f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                float progress = Mathf.Clamp01(elapsed / duration);
                float eased = progress * progress * (3f - 2f * progress);
                for (int i = 0; i < rings.Count; i++)
                {
                    rings[i].localScale = Vector3.one * Mathf.Lerp(0.18f, endScales[i], eased);
                    Color color = i % 2 == 0
                        ? new Color(1f, 0.18f, 0.12f, 0.92f * (1f - progress))
                        : new Color(1f, 0.62f, 0.18f, 0.9f * (1f - progress));
                    images[i].color = color;
                }
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            foreach (RectTransform ring in rings)
                Destroy(ring.gameObject);
        }

        private void CreateTributeDeathRing(
            Vector2 position,
            float size,
            float endScale,
            Color color,
            List<RectTransform> rings,
            List<Image> images,
            List<float> endScales)
        {
            RectTransform ring = CreateRect("Tribute Death Ring", (RectTransform)canvas.transform);
            ring.anchorMin = new Vector2(0.5f, 0.5f);
            ring.anchorMax = new Vector2(0.5f, 0.5f);
            ring.sizeDelta = new Vector2(size, size);
            ring.localScale = Vector3.one * 0.18f;
            SetAnimatedCardPosition(ring, position);
            Image image = ring.gameObject.AddComponent<Image>();
            image.sprite = GetCardBackCircleSprite();
            image.color = color;
            image.raycastTarget = false;
            ring.SetAsLastSibling();
            rings.Add(ring);
            images.Add(image);
            endScales.Add(endScale);
        }

        private IEnumerator PlayHandReflowAnimation(
            List<CardMovementAnimation> handReflow,
            float duration)
        {
            List<RectTransform> visuals = handReflow.Select(item => FindHandCardVisual(item.Card)).ToList();
            for (int i = 0; i < handReflow.Count; i++)
                SetCardVisualRect(visuals[i], handReflow[i].From, handReflow[i].StartSize);

            float elapsed = 0f;
            while (elapsed < duration)
            {
                float progress = Mathf.Clamp01(elapsed / duration);
                float eased = progress * progress * (3f - 2f * progress);
                for (int i = 0; i < handReflow.Count; i++)
                {
                    CardMovementAnimation item = handReflow[i];
                    SetCardVisualRect(visuals[i],
                        Vector2.Lerp(item.From, item.To, eased),
                        Vector2.Lerp(item.StartSize, item.EndSize, eased));
                }
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            for (int i = 0; i < handReflow.Count; i++)
                SetCardVisualRect(visuals[i], handReflow[i].To, handReflow[i].EndSize);
        }

        private IEnumerator TweenCardAndHand(
            RectTransform card,
            Vector2 from,
            Vector2 to,
            Vector2 startSize,
            Vector2 endSize,
            List<CardMovementAnimation> handReflow,
            List<RectTransform> handVisuals,
            float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                float progress = Mathf.Clamp01(elapsed / duration);
                float eased = progress * progress * (3f - 2f * progress);
                SetAnimatedCardPosition(card, Vector2.Lerp(from, to, eased));
                card.sizeDelta = Vector2.Lerp(startSize, endSize, eased);
                for (int i = 0; i < handReflow.Count; i++)
                {
                    CardMovementAnimation item = handReflow[i];
                    SetCardVisualRect(handVisuals[i],
                        Vector2.Lerp(item.From, item.To, eased),
                        Vector2.Lerp(item.StartSize, item.EndSize, eased));
                }
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            SetAnimatedCardPosition(card, to);
            card.sizeDelta = endSize;
            for (int i = 0; i < handReflow.Count; i++)
                SetCardVisualRect(handVisuals[i], handReflow[i].To, handReflow[i].EndSize);
        }

        private RectTransform FindHandCardVisual(CardInstance card)
        {
            foreach (DuelHandCardVisual handVisual in contentRoot.GetComponentsInChildren<DuelHandCardVisual>(true))
                if (handVisual.Card == card)
                    return (RectTransform)handVisual.transform;
            return null;
        }

        private void SetCardVisualRect(RectTransform visual, Vector2 center, Vector2 size)
        {
            if (visual == null)
                return;
            Rect canvasRect = ((RectTransform)canvas.transform).rect;
            float width = size.x / canvasRect.width;
            float height = size.y / canvasRect.height;
            SetAnchors(visual, center.x - width * 0.5f, center.y - height * 0.5f,
                center.x + width * 0.5f, center.y + height * 0.5f);
        }

        private IEnumerator PlaySingleCardAnimation(
            CardMovementAnimation animation, bool showSummonPulse = true)
        {
            if (animation.IsSummon && showSummonPulse)
                yield return PlaySummonPulse(animation.To);

            RectTransform visual = CreateAnimatedCard(animation.Card.Definition,
                animation.IsSet ? false : animation.StartsFaceUp,
                animation.From, animation.StartSize);
            bool flipBeforeMovement = !animation.ShowPopup && !animation.IsSet &&
                                     animation.StartsFaceUp != animation.EndsFaceUp &&
                                     animation.EndsFaceUp;
            if (flipBeforeMovement)
                yield return FlipAnimatedCard(visual, true);

            if (animation.ShowPopup && !animation.IsSet)
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
                if (animation.IsSet)
                    yield return TweenSetCardAndHand(visual, animation, new List<CardMovementAnimation>(),
                        new List<RectTransform>(), 0.42f);
                else
                    yield return TweenCardVisual(visual, animation.From, animation.To,
                        animation.StartSize, animation.EndSize, 0.42f);
                if (animation.IsSet)
                    visual.localRotation = Quaternion.Euler(0f, 0f, 90f);
                else if (!flipBeforeMovement && animation.StartsFaceUp != animation.EndsFaceUp)
                    yield return FlipAnimatedCard(visual, animation.EndsFaceUp);
            }

            yield return TweenCardVisual(visual, animation.To, animation.To,
                animation.EndSize, animation.EndSize * 1.08f, 0.07f);
            yield return TweenCardVisual(visual, animation.To, animation.To,
                animation.EndSize * 1.08f, animation.EndSize, 0.09f);
            Destroy(visual.gameObject);
            CompleteCardArrival(animation.Card);
        }

        private void CompleteCardArrival(CardInstance card)
        {
            if (animatingCardArrivals.Remove(card) && contentRoot != null)
                Render();
        }

        private IEnumerator PlaySummonPulse(Vector2 position, Color? pulseColor = null)
        {
            Color color = pulseColor ?? new Color(0.3f, 0.95f, 0.82f, 0.95f);
            RectTransform canvasRect = (RectTransform)canvas.transform;
            RectTransform pulse = CreateRect("Summon Slot Pulse", canvasRect);
            pulse.anchorMin = new Vector2(0.5f, 0.5f);
            pulse.anchorMax = new Vector2(0.5f, 0.5f);
            pulse.sizeDelta = new Vector2(120f, 120f);
            SetAnimatedCardPosition(pulse, position);
            Image image = pulse.gameObject.AddComponent<Image>();
            image.sprite = GetCardBackCircleSprite();
            image.color = color;
            image.raycastTarget = false;
            pulse.SetAsLastSibling();

            const float duration = 0.34f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                float progress = Mathf.Clamp01(elapsed / duration);
                float pulseScale = 0.35f + progress * 0.95f;
                pulse.localScale = Vector3.one * pulseScale;
                image.color = new Color(color.r, color.g, color.b, color.a * (1f - progress));
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            Destroy(pulse.gameObject);
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
            if (engine == null || canvas == null)
                return;

            if (phase == DuelPhase.GameOver && screen == ScreenMode.Duel)
            {
                bool playerWon = engine.Winner == engine.Human.Name;
                Color resultColor = playerWon ? accentColor : new Color(0.78f, 0.18f, 0.22f);
                QueuePhaseTicker(playerWon ? "YOU WIN!" : "YOU LOST!",
                    backgroundColor: resultColor,
                    edgeColor: Color.Lerp(resultColor, Color.white, 0.22f));
                return;
            }

            if (phase == DuelPhase.GameOver)
                return;
            string activePlayer = engine.CurrentPlayer == engine.Human ? "PLAYER" : "OPPONENT";
            Color turnColor = GetTurnColor(engine.CurrentPlayer == engine.Human);
            QueuePhaseTicker(activePlayer + " - " + GetPhaseTickerLabel(phase),
                backgroundColor: turnColor,
                edgeColor: Color.Lerp(turnColor, Color.white, 0.18f));
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
            while (phaseTickerQueue.Count > 0)
            {
                PhaseTickerMessage message = phaseTickerQueue.Dequeue();
                RectTransform ticker = CreateRect("Phase Ticker", canvasRect);
                ticker.anchorMin = new Vector2(0f, 0.505f);
                ticker.anchorMax = new Vector2(1f, 0.59f);
                ticker.sizeDelta = Vector2.zero;
                ticker.anchoredPosition = new Vector2(-canvasRect.rect.width, 0f);
                Image background = ticker.gameObject.AddComponent<Image>();
                background.color = message.BackgroundColor;
                background.raycastTarget = false;
                AddPhaseTickerEdge(ticker, true, message.AccentColor);
                AddPhaseTickerEdge(ticker, false, message.AccentColor);
                ticker.SetAsLastSibling();

                yield return SlidePhaseTickerBackground(ticker, canvasRect.rect.width, 0.3f);

                RectTransform labelRect = CreateRect("Phase Ticker Text", ticker);
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.sizeDelta = Vector2.zero;
                Text label = labelRect.gameObject.AddComponent<Text>();
                label.font = font;
                label.text = message.Label;
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
            phaseTickerRoutine = null;
            Action onComplete = phaseTickerCompletion;
            phaseTickerCompletion = null;
            onComplete?.Invoke();
            TryStartNextPresentation();
        }

        private void AddPhaseTickerEdge(RectTransform ticker, bool top, Color edgeColor)
        {
            RectTransform edge = CreateRect(top ? "Top Accent Band" : "Bottom Accent Band", ticker);
            float edgeAnchor = top ? 1f : 0f;
            edge.anchorMin = new Vector2(0f, edgeAnchor);
            edge.anchorMax = new Vector2(1f, edgeAnchor);
            edge.sizeDelta = new Vector2(0f, 3f);
            edge.anchoredPosition = new Vector2(0f, top ? -1.5f : 1.5f);
            Image image = edge.gameObject.AddComponent<Image>();
            image.color = new Color(edgeColor.r, edgeColor.g, edgeColor.b, 0.78f);
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

            Dictionary<CardInstance, CardVisualState> currentStates = CaptureCardVisualStates();
            bool hasAttackerState = currentStates.TryGetValue(attacker, out CardVisualState attackerState);
            Vector2 from = hasAttackerState
                ? GetCardVisualPosition(attackerState)
                : new Vector2(0.5f, 0.35f);
            Vector2 to = target == null
                ? DuelAttackLayout.GetDirectAttackTarget(
                    hasAttackerState && attackerState.Owner == engine.Opponent)
                : currentStates.TryGetValue(target, out CardVisualState targetState)
                    ? GetCardVisualPosition(targetState)
                    : new Vector2(0.5f, 0.59f);

            attackAnimationQueue.Enqueue(new AttackAnimation
            {
                Attacker = attacker,
                From = from,
                To = to
            });
            TryStartNextPresentation();
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
            TryStartNextPresentation();
        }

        private void DrawDuelScreen()
        {
            if (engine == null)
            {
                AddText(contentRoot, "SYNCHRO / DUEL LAB", 0.2f, 0.62f, 0.8f, 0.72f,
                    34, accentColor, TextAnchor.MiddleCenter, true);
                AddText(contentRoot, "Ready to duel?", 0.2f, 0.54f, 0.8f, 0.6f,
                    20, Color.white, TextAnchor.MiddleCenter);
                AddButton(contentRoot, "START DUEL", 0.38f, 0.4f, 0.62f, 0.49f,
                    accentColor, StartDuelFromMenu, 20);
                AddPanel(contentRoot, 0.14f, 0.2f, 0.86f, 0.29f, panelColor);
                AddButton(contentRoot, "DECK BUILDER", 0.15f, 0.21f, 0.29f, 0.28f,
                    new Color(0.15f, 0.22f, 0.31f), () => SwitchScreen(ScreenMode.Deck), 11);
                AddButton(contentRoot, "CARD LIST", 0.295f, 0.21f, 0.43f, 0.28f,
                    new Color(0.15f, 0.22f, 0.31f), () => SwitchScreen(ScreenMode.Cards), 11);
                AddButton(contentRoot, "SHOP", 0.435f, 0.21f, 0.565f, 0.28f,
                    new Color(0.15f, 0.22f, 0.31f), () => SwitchScreen(ScreenMode.Shop), 11);
                AddButton(contentRoot, "AI LAB", 0.57f, 0.21f, 0.705f, 0.28f,
                    new Color(0.36f, 0.23f, 0.5f), () => SwitchScreen(ScreenMode.AiLab), 11);
                AddButton(contentRoot, "UNIT TESTS", 0.71f, 0.21f, 0.85f, 0.28f,
                    new Color(0.12f, 0.42f, 0.4f), () => SwitchScreen(ScreenMode.TestLab), 11);
                if (!string.IsNullOrEmpty(notice))
                    AddText(contentRoot, notice, 0.2f, 0.12f, 0.8f, 0.18f,
                        14, new Color(1f, 0.78f, 0.35f), TextAnchor.MiddleCenter);
                if (coinTossOpen)
                    DrawCoinTossOverlay();
                return;
            }

            bool humanTurn = engine.CurrentPlayer == engine.Human && engine.Phase != DuelPhase.GameOver;
            DrawPlayerPortrait("AI", 0.025f, 0.804f, 0.085f, 0.9f,
                new Color(0.42f, 0.22f, 0.28f), !humanTurn && engine.Phase != DuelPhase.GameOver);
            AddText(contentRoot, engine.Opponent.Name + "\n" + engine.Opponent.LifePoints + " LP",
                0.095f, 0.805f, 0.195f, 0.9f, 17, Color.white, TextAnchor.MiddleLeft, true);
            if (!humanTurn && engine.Phase != DuelPhase.GameOver)
                DrawCurrentTurnLabel(0.095f, 0.75f, 0.195f, 0.79f);
            DrawGraveyardButton(engine.Opponent, 0.205f, OpponentSpellTrapY);
            DrawDeckButton(engine.Opponent, OpponentDeckX, OpponentSpellTrapY);
            DrawBanishedIndicator(engine.Opponent, 0.205f, OpponentMonsterY);
            DrawSpellTrapRow(engine.Opponent.SpellTrapZones, OpponentSpellTrapY);
            DrawFieldSpellSlot(engine.Opponent.FieldSpellZone, true);
            DrawZoneRow(engine.Opponent.MonsterZones, OpponentMonsterY, true);
            DrawOpponentHandBacks();

            DrawPhaseTrack();

            DrawZoneRow(engine.Human.MonsterZones, HumanMonsterY, false);
            DrawSpellTrapRow(engine.Human.SpellTrapZones, HumanSpellTrapY);
            DrawFieldSpellSlot(engine.Human.FieldSpellZone, false);
            DrawPlayerPortrait("YOU", 0.025f, 0.2f, 0.085f, 0.296f,
                new Color(0.12f, 0.36f, 0.4f), humanTurn);
            AddText(contentRoot, engine.Human.Name + "\n" + engine.Human.LifePoints + " LP",
                0.095f, 0.2f, 0.195f, 0.296f, 17, Color.white, TextAnchor.MiddleLeft, true);
            if (humanTurn)
                DrawCurrentTurnLabel(0.095f, 0.3f, 0.195f, 0.34f);
            DrawDeckButton(engine.Human, 0.205f, HumanSpellTrapY);
            DrawGraveyardButton(engine.Human, 0.74f, HumanSpellTrapY);
            DrawBanishedIndicator(engine.Human, 0.74f, HumanMonsterY);

            DrawActionBar();
            DrawExtraDeck();
            DrawHand();
            DrawSynchroMaterialPrompt();
            DrawDuelLog();



            if (engine.ResponseWindowOpen)
                DrawResponseWindow();
        }

        private void DrawCoinTossOverlay()
        {
            AddPanel(contentRoot, 0f, 0f, 1f, 1f, new Color(0.01f, 0.02f, 0.035f, 0.92f));
            AddPanel(contentRoot, 0.2f, 0.22f, 0.8f, 0.78f, panelColor);
            AddText(contentRoot, "COIN TOSS", 0.25f, 0.67f, 0.75f, 0.73f,
                24, accentColor, TextAnchor.MiddleCenter, true);

            if (!coinFlipResolved)
            {
                AddText(contentRoot, coinFlipInProgress
                        ? "Flipping the coin..."
                        : "Choose heads or tails.",
                    0.25f, 0.59f, 0.75f, 0.65f, 16, Color.white, TextAnchor.MiddleCenter);
                if (!coinFlipInProgress)
                {
                    AddCenteredButton(contentRoot, "HEADS", -CoinSideButtonOffset,
                        CoinSideButtonSize, new Color(0.78f, 0.58f, 0.16f),
                        () => CallCoinSide(true), 22);
                    AddCenteredButton(contentRoot, "TAILS", CoinSideButtonOffset,
                        CoinSideButtonSize, new Color(0.34f, 0.4f, 0.52f),
                        () => CallCoinSide(false), 22);
                    AddButton(contentRoot, "CANCEL", 0.4f, 0.31f, 0.6f, 0.38f,
                        new Color(0.25f, 0.29f, 0.34f), () =>
                        {
                            coinTossOpen = false;
                            Render();
                        }, 11);
                }
                return;
            }

            AddText(contentRoot, coinTossResult, 0.25f, 0.57f, 0.75f, 0.65f,
                15, Color.white, TextAnchor.MiddleCenter);
            if (playerWonCoinFlip)
            {
                AddButton(contentRoot, "I'LL GO FIRST", 0.29f, 0.42f, 0.49f, 0.51f,
                    new Color(0.15f, 0.38f, 0.66f), () => BeginDuelAfterCoinToss(true), 13);
                AddButton(contentRoot, "I'LL GO SECOND", 0.51f, 0.42f, 0.71f, 0.51f,
                    new Color(0.56f, 0.2f, 0.23f), () => BeginDuelAfterCoinToss(false), 13);
            }
        }

        private void DrawOpponentHandBacks()
        {
            int cardCount = engine.Opponent.Hand.Count;
            if (cardCount == 0)
                return;

            int visibleCards = Mathf.Min(cardCount, DuelHandLayout.MaxVisibleOpponentCards);
            float cardWidth = DuelHandLayout.GetCardWidth(cardCount, GetScreenAspectRatio(), true);
            float cardHeight = GetPortraitHeight(cardWidth);
            for (int i = 0; i < visibleCards; i++)
            {
                CardInstance card = engine.Opponent.Hand[i];
                Vector2 center = DuelHandLayout.GetCardCenter(i, cardCount, GetScreenAspectRatio(), true);
                float xMin = center.x - cardWidth * 0.5f;
                RectTransform cardBack = AddPanel(contentRoot, xMin,
                    center.y - cardHeight * 0.5f, xMin + cardWidth, center.y + cardHeight * 0.5f,
                    new Color(0.08f, 0.16f, 0.3f));
                cardBack.gameObject.AddComponent<DuelHandCardVisual>().Card = card;
                if (animatedHandCards.Contains(card))
                    cardBack.gameObject.AddComponent<CanvasGroup>().alpha = 0f;
                AddCardArtPlaceholder(cardBack, null, 0.04f, 0.04f, 0.96f, 0.96f, false);
            }
        }

        private void DrawAiVisibleHand(DuelPlayer owner, bool opponent)
        {
            int count = owner.Hand.Count;
            if (count == 0)
                return;

            int visibleCount = opponent
                ? Mathf.Min(count, DuelHandLayout.MaxVisibleOpponentCards)
                : count;
            float width = DuelHandLayout.GetCardWidth(count, GetScreenAspectRatio(), opponent);
            float height = GetPortraitHeight(width);
            for (int i = 0; i < visibleCount; i++)
            {
                CardInstance card = owner.Hand[i];
                Vector2 center = DuelHandLayout.GetCardCenter(i, count,
                    GetScreenAspectRatio(), opponent);
                RectTransform cardView = AddButton(contentRoot, card.Definition.DisplayName,
                    center.x - width * 0.5f, center.y - height * 0.5f,
                    center.x + width * 0.5f, center.y + height * 0.5f,
                    CardFrameColor(card.Definition), () => { }, 8, card.Definition);
                cardView.gameObject.AddComponent<DuelHandCardVisual>().Card = card;
                if (animatedHandCards.Contains(card))
                    cardView.gameObject.AddComponent<CanvasGroup>().alpha = 0f;
                AddCardArtPlaceholder(cardView, card.Definition, 0.02f, 0.02f, 0.98f, 0.98f, true);
                Text label = cardView.Find("Label").GetComponent<Text>();
                SetAnchors(label.rectTransform, 0.02f, 0.02f, 0.98f, 0.28f);
                label.alignment = TextAnchor.MiddleCenter;
                label.fontSize = 8;
            }
        }

        private void DrawPhaseTrack()
        {
            bool humanTurn = engine.CurrentPlayer == engine.Human;
            Color turnColor = GetTurnColor(humanTurn);
            AddPanel(contentRoot, 0.025f, 0.465f, 0.195f, 0.565f,
                Color.Lerp(panelColor, turnColor, 0.3f));
            AddText(contentRoot, "TURN " + engine.TurnNumber,
                0.03f, 0.537f, 0.19f, 0.56f, 12, accentColor, TextAnchor.MiddleCenter, true);
            AddText(contentRoot, CurrentPhaseLabel(),
                0.03f, 0.517f, 0.19f, 0.535f, 8, mutedColor, TextAnchor.MiddleCenter);
            RectTransform phaseButton = AddButton(contentRoot, NextPhaseButtonLabel(),
                0.03f, 0.472f, 0.19f, 0.514f,
                Color.Lerp(turnColor, Color.white, 0.12f), () =>
                {
                    if (!engine.IsHumanActionWindow || engine.ResponseWindowOpen || engine.SearchChoiceOpen)
                        return;
                    engine.AdvancePhase();
                    ClearSelection();
                    Render();
                }, 10);
            Text phaseButtonText = phaseButton.GetComponentInChildren<Text>();
            phaseButtonText.resizeTextForBestFit = true;
            phaseButtonText.resizeTextMinSize = 7;
            phaseButtonText.resizeTextMaxSize = 10;

            RectTransform endTurnButton = AddButton(contentRoot, "END TURN",
                0.81f, 0.472f, 0.97f, 0.514f,
                new Color(0.28f, 0.3f, 0.34f), () =>
                {
                    if (!engine.EndHumanTurn())
                        return;
                    ClearSelection();
                    Render();
                }, 10);
            Text endTurnButtonText = endTurnButton.GetComponentInChildren<Text>();
            endTurnButtonText.resizeTextForBestFit = true;
            endTurnButtonText.resizeTextMinSize = 7;
            endTurnButtonText.resizeTextMaxSize = 10;
        }

        private static Color GetTurnColor(bool humanTurn)
        {
            return humanTurn
                ? new Color(0.12f, 0.34f, 0.62f)
                : new Color(0.62f, 0.16f, 0.18f);
        }

        private string CurrentPhaseLabel()
        {
            string activePlayer = engine.CurrentPlayer == engine.Human ? "PLAYER" : "OPPONENT";
            switch (engine.Phase)
            {
                case DuelPhase.Draw: return activePlayer + " - DRAW PHASE";
                case DuelPhase.Standby: return activePlayer + " - STANDBY PHASE";
                case DuelPhase.MainOne: return activePlayer + " - MAIN PHASE I";
                case DuelPhase.Battle: return activePlayer + " - BATTLE PHASE";
                case DuelPhase.MainTwo: return activePlayer + " - MAIN PHASE II";
                case DuelPhase.End: return activePlayer + " - END PHASE";
                default: return activePlayer + " - " + engine.PhaseLabel.ToUpperInvariant();
            }
        }

        private string NextPhaseButtonLabel()
        {
            switch (engine.Phase)
            {
                case DuelPhase.Draw: return "GO TO STANDBY PHASE";
                case DuelPhase.Standby: return "GO TO MAIN PHASE I";
                case DuelPhase.MainOne:
                    return engine.Human.BattlePhaseSkippedTurn == engine.TurnNumber
                        ? "SKIP BATTLE — GO TO MAIN 2"
                        : "GO TO BATTLE PHASE";
                case DuelPhase.Battle: return "GO TO MAIN PHASE II";
                case DuelPhase.MainTwo: return "GO TO END PHASE";
                case DuelPhase.End: return "GO TO DRAW PHASE";
                default: return "DUEL OVER";
            }
        }

        private void DrawResponseWindow()
        {
            if (engine.DustTornadoTargetChoiceOpen)
            {
                DrawDustTornadoResponseOverlay();
                return;
            }
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
                        if (trap.Definition.Effect == CardEffect.DustTornado)
                            selectedTarget = null;
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

        private void DrawPowerToolChoiceOverlay()
        {
                IReadOnlyList<CardInstance> choices = engine.AvailablePowerToolCards;
                AddPanel(contentRoot, 0, 0.09f, 1, 0.9f, new Color(0.01f, 0.02f, 0.035f, 0.9f));
                AddPanel(contentRoot, 0.12f, 0.15f, 0.88f, 0.85f, panelColor);
                AddText(contentRoot, "POWER TOOL DRAGON — SELECT 3 EQUIP SPELLS",
                    0.18f, 0.76f, 0.82f, 0.82f, 19, accentColor, TextAnchor.MiddleCenter, true);
                AddText(contentRoot, "Your opponent will randomly add 1 revealed card to your hand.",
                    0.18f, 0.7f, 0.82f, 0.75f, 13, Color.white, TextAnchor.MiddleCenter);

                for (int i = 0; i < choices.Count; i++)
                {
                    CardInstance choice = choices[i];
                    int column = i % 2;
                    int row = i / 2;
                    float x = column == 0 ? 0.18f : 0.51f;
                    float top = 0.65f - row * 0.065f;
                    bool selected = selectedPowerToolCards.Contains(choice);
                    AddButton(contentRoot,
                        (selected ? "✓  " : "") + choice.Definition.DisplayName,
                        x, top - 0.052f, x + 0.31f, top,
                        selected ? new Color(0.12f, 0.52f, 0.43f) : CardFrameColor(choice.Definition),
                        () =>
                        {
                            if (selectedPowerToolCards.Contains(choice))
                                selectedPowerToolCards.Remove(choice);
                            else if (selectedPowerToolCards.Count < 3)
                                selectedPowerToolCards.Add(choice);
                            Render();
                        }, 11, choice.Definition);
                }

                bool canConfirm = selectedPowerToolCards.Count == 3;
                AddButton(contentRoot, "REVEAL SELECTED (" + selectedPowerToolCards.Count + "/3)",
                    0.32f, 0.19f, 0.68f, 0.26f,
                    canConfirm ? accentColor : new Color(0.2f, 0.25f, 0.31f),
                    () =>
                    {
                        if (!canConfirm)
                            return;
                        if (engine.TryChoosePowerToolCards(selectedPowerToolCards.ToList()))
                            selectedPowerToolCards.Clear();
                        Render();
                    }, 12);
        }

        private void DrawQuickdrawDiscardOverlay()
        {
            IReadOnlyList<CardInstance> choices = engine.AvailableQuickdrawDiscards;
            AddPanel(contentRoot, 0, 0.09f, 1, 0.9f, new Color(0.01f, 0.02f, 0.035f, 0.9f));
            AddPanel(contentRoot, 0.12f, 0.15f, 0.88f, 0.85f, panelColor);
            AddText(contentRoot, "QUICKDRAW SYNCHRON — CHOOSE A MONSTER TO DISCARD",
                0.17f, 0.76f, 0.83f, 0.82f, 18, accentColor, TextAnchor.MiddleCenter, true);

            for (int i = 0; i < choices.Count; i++)
            {
                CardInstance choice = choices[i];
                int column = i % 2;
                int row = i / 2;
                float x = column == 0 ? 0.18f : 0.51f;
                float top = 0.67f - row * 0.09f;
                AddButton(contentRoot, "DISCARD " + choice.Definition.DisplayName,
                    x, top - 0.07f, x + 0.31f, top, CardFrameColor(choice.Definition),
                    () =>
                    {
                        if (engine.TryChooseQuickdrawDiscard(choice))
                            RecordReplayState();
                        Render();
                    }, 11, choice.Definition);
            }

            AddButton(contentRoot, "CANCEL",
                0.39f, 0.19f, 0.61f, 0.26f, new Color(0.25f, 0.29f, 0.34f),
                () =>
                {
                    engine.CancelQuickdrawDiscardChoice();
                    Render();
                }, 11);
        }

        private void DrawDustTornadoResponseOverlay()
        {
                AddPanel(contentRoot, 0, 0.09f, 1, 0.9f, new Color(0.01f, 0.02f, 0.035f, 0.9f));
                AddPanel(contentRoot, 0.12f, 0.15f, 0.88f, 0.85f, panelColor);
                AddText(contentRoot, "DUST TORNADO — TARGET AN OPPONENT'S SPELL/TRAP",
                    0.17f, 0.76f, 0.83f, 0.82f, 18, accentColor, TextAnchor.MiddleCenter, true);
                IReadOnlyList<CardInstance> targets = engine.AvailableDustTornadoTargets;
                for (int i = 0; i < targets.Count; i++)
                {
                    CardInstance target = targets[i];
                    float top = 0.68f - i * 0.07f;
                    AddButton(contentRoot,
                        target.FaceUp ? target.Definition.DisplayName : "SET CARD",
                        0.18f, top - 0.055f, 0.48f, top,
                        CardFrameColor(target.Definition),
                        () =>
                        {
                            selectedTarget = target;
                            Render();
                        }, 11,
                        target.FaceUp ? target.Definition : null);
                }
                bool hasTarget = selectedTarget != null && targets.Contains(selectedTarget);
                if (hasTarget)
                    AddButton(contentRoot, "DESTROY SELECTED — NO SET",
                        0.18f, 0.2f, 0.48f, 0.27f, accentColor,
                        () => ResolveDustTornadoResponse(selectedTarget, null), 11);
                IReadOnlyList<CardInstance> setCards = engine.AvailableDustTornadoSetCards;
                for (int i = 0; hasTarget && i < setCards.Count; i++)
                {
                    CardInstance card = setCards[i];
                    float top = 0.68f - i * 0.07f;
                    AddButton(contentRoot, "SET " + card.Definition.DisplayName,
                        0.52f, top - 0.055f, 0.82f, top,
                        CardFrameColor(card.Definition),
                        () => ResolveDustTornadoResponse(selectedTarget, card), 10, card.Definition);
                }
                AddButton(contentRoot, "CANCEL DUST TORNADO",
                    0.33f, 0.18f, 0.67f, 0.25f, new Color(0.32f, 0.2f, 0.23f),
                    () =>
                    {
                        selectedTarget = null;
                        engine.CancelDustTornadoTargetChoice();
                        Render();
                    }, 12);
                AddText(contentRoot, "Choose a target on the left. Choose a card on the right to Set it, or skip the Set.",
                    0.17f, 0.29f, 0.83f, 0.34f, 11, mutedColor, TextAnchor.MiddleCenter);
        }

        private void ResolveDustTornadoResponse(CardInstance target, CardInstance cardToSet)
        {
                if (target == null)
                    return;
                engine.TryRespondWithDustTornado(target, cardToSet);
                selectedTarget = null;
                Render();
        }

        private void DrawSanganSearchOverlay()
        {
            IReadOnlyList<CardInstance> choices = engine.AvailableSearchCards;
            AddPanel(contentRoot, 0, 0.09f, 1, 0.9f, new Color(0.01f, 0.02f, 0.035f, 0.9f));
            AddPanel(contentRoot, 0.06f, 0.12f, 0.94f, 0.88f, panelColor);
            AddText(contentRoot, engine.SearchChoiceTitle,
                0.2f, 0.75f, 0.8f, 0.81f, 22, accentColor, TextAnchor.MiddleCenter, true);
            AddText(contentRoot, engine.SearchChoicePrompt + " Select a card, review it, then confirm.",
                0.2f, 0.69f, 0.8f, 0.75f, 14, Color.white, TextAnchor.MiddleCenter);

            RectTransform viewport = AddPanel(contentRoot, 0.2f, 0.24f, 0.57f, 0.67f, backgroundColor);
            viewport.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            WheelScrollRect scroll = viewport.gameObject.AddComponent<WheelScrollRect>();
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

            AddButton(contentRoot, engine.SearchChoiceActionLabel, 0.57f, 0.17f, 0.94f, 0.23f,
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
            if (engine.SearchChoiceOptional)
                AddButton(contentRoot, "SKIP OPTIONAL SEARCH", 0.17f, 0.17f, 0.47f, 0.23f,
                    new Color(0.2f, 0.34f, 0.46f), () =>
                    {
                        engine.TrySkipSearchChoice();
                        selectedSanganSearchCard = null;
                        Render();
                    }, 12);
        }

        private void ConfigureMouseWheelScrolling(ScrollRect scroll)
        {
            scroll.scrollSensitivity = 36f;
        }

        private void ConfigureCardDropTarget(RectTransform target, Func<CardInstance, bool> onDrop)
        {
            DuelCardDragHandler dragHandler = target.gameObject.AddComponent<DuelCardDragHandler>();
            dragHandler.OnDropCard = onDrop;
        }

        private void BeginHandCardDrag(
            CardInstance card,
            RectTransform source,
            PointerEventData eventData)
        {
            if (engine == null || screen != ScreenMode.Duel || !engine.IsHumanActionWindow ||
                pendingTributeSummonCard != null)
                return;

            draggingHandCard = card;
            dragDropHandled = false;
            draggedCardSourceGroup = source.GetComponent<CanvasGroup>();
            if (draggedCardSourceGroup == null)
                draggedCardSourceGroup = source.gameObject.AddComponent<CanvasGroup>();
            draggedCardSourceGroup.alpha = 0f;

            int cardIndex = engine.Human.Hand.IndexOf(card);
            Vector2 start = DuelHandLayout.GetCardCenter(cardIndex, engine.Human.Hand.Count,
                GetScreenAspectRatio(), false);
            RectTransform canvasRect = (RectTransform)canvas.transform;
            draggedCardVisual = CreateAnimatedCard(card.Definition, true, start,
                new Vector2(source.rect.width, source.rect.height));
            UpdateDraggedCardVisual(eventData);
        }

        private void UpdateDraggedCardVisual(PointerEventData eventData)
        {
            if (draggedCardVisual == null)
                return;

            RectTransform canvasRect = (RectTransform)canvas.transform;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRect, eventData.position, eventData.pressEventCamera, out Vector2 localPosition))
                draggedCardVisual.anchoredPosition = localPosition;
        }

        private void EndHandCardDrag(PointerEventData eventData)
        {
            if (draggingHandCard == null)
                return;

            RectTransform canvasRect = (RectTransform)canvas.transform;
            bool overHand = RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, eventData.position, eventData.pressEventCamera, out Vector2 localPosition) &&
                localPosition.y / canvasRect.rect.height + 0.5f <= 0.2f;
            if (!dragDropHandled && overHand)
                ReorderHandCard(draggingHandCard, localPosition.x / canvasRect.rect.width + 0.5f);

            if (draggedCardVisual != null)
                Destroy(draggedCardVisual.gameObject);
            if (draggedCardSourceGroup != null)
                draggedCardSourceGroup.alpha = 1f;
            draggingHandCard = null;
            draggedCardVisual = null;
            draggedCardSourceGroup = null;
            Render();
        }

        private void ReorderHandCard(CardInstance card, float normalizedX)
        {
            List<CardInstance> hand = engine.Human.Hand;
            int oldIndex = hand.IndexOf(card);
            if (oldIndex < 0 || hand.Count < 2)
                return;

            int targetIndex = 0;
            float closestDistance = float.MaxValue;
            for (int i = 0; i < hand.Count; i++)
            {
                float centerX = DuelHandLayout.GetCardCenter(i, hand.Count, GetScreenAspectRatio(), false).x;
                float distance = Mathf.Abs(centerX - normalizedX);
                if (distance >= closestDistance)
                    continue;
                closestDistance = distance;
                targetIndex = i;
            }

            if (targetIndex == oldIndex)
                return;
            hand.RemoveAt(oldIndex);
            hand.Insert(Mathf.Clamp(targetIndex, 0, hand.Count), card);
        }

        private bool HandleMonsterZoneDrop(CardInstance card, int zoneIndex)
        {
            if (!CanPlayDraggedHandCard(card))
                return false;
            if (!card.Definition.IsMonster)
                return false;

            if (card.Definition.Effect == CardEffect.Berserkion ||
                card.Definition.Effect == CardEffect.Valkyrion)
                return CompleteDragAction(() => engine.TrySpecialSummonBoss(card, zoneIndex));
            if (!card.Definition.CanNormalSummon)
            {
                notice = "This monster needs its card-specific Special Summon procedure.";
                return false;
            }

            int tributeCount = card.Definition.Level >= 7 ? 2 : card.Definition.Level >= 5 ? 1 : 0;
            if (tributeCount > 0)
            {
                pendingTributeSummonCard = card;
                pendingTributeSummonZone = zoneIndex;
                selectedMaterials.Clear();
                selectedHandCard = card;
                dragDropHandled = true;
                notice = "";
                return true;
            }

            return CompleteDragAction(() => engine.TryNormalSummon(
                card, Array.Empty<CardInstance>(), preferredMonsterZone: zoneIndex));
        }

        private bool HandleSpellTrapZoneDrop(CardInstance card, int zoneIndex)
        {
            if (!CanPlayDraggedHandCard(card))
                return false;
            if (card.Definition.Kind == CardKind.Trap)
                return CompleteDragAction(() => engine.TrySetBackrow(card, zoneIndex));
            if (card.Definition.Kind != CardKind.Spell ||
                card.Definition.Subtype == CardSubtype.Field)
                return false;

            if (card.Definition.Effect == CardEffect.ReviveMonsterFromGraveyard)
            {
                selectedHandCard = card;
                selectedTarget = null;
                dragDropHandled = true;
                OpenGraveyard(engine.Human);
                return true;
            }

            CardInstance target = selectedTarget ?? selectedMonsterCard;
            return CompleteDragAction(() => engine.TryActivate(card, target));
        }

        private bool HandleFieldSpellZoneDrop(CardInstance card)
        {
            if (!CanPlayDraggedHandCard(card) ||
                card.Definition.Kind != CardKind.Spell ||
                card.Definition.Subtype != CardSubtype.Field)
                return false;

            return CompleteDragAction(() => engine.TryActivate(card));
        }

        private bool CanPlayDraggedHandCard(CardInstance card)
        {
            return engine != null && screen == ScreenMode.Duel && engine.IsHumanActionWindow &&
                   (engine.Phase == DuelPhase.MainOne || engine.Phase == DuelPhase.MainTwo) &&
                   card != null && engine.Human.Hand.Contains(card);
        }

        private bool CompleteDragAction(Func<bool> action)
        {
            bool succeeded = action();
            if (succeeded)
            {
                dragDropHandled = true;
                notice = "";
                ClearSelection();
            }
            else if (engine.Messages.Count > 0)
                notice = engine.Messages[engine.Messages.Count - 1];
            return succeeded;
        }

        private void DrawZoneRow(CardInstance[] zones, float y, bool opponent)
        {
            for (int i = 0; i < zones.Length; i++)
            {
                int zoneIndex = i;
                float xMin = MonsterCardStartX + i * MonsterCardStepX;
                float xMax = xMin + BoardCardWidth;
                CardInstance card = zones[i];
                CardInstance displayedCard = card != null && animatingCardArrivals.Contains(card) ? null : card;
                string label = displayedCard == null ? "MONSTER\nSLOT" : CardLabel(displayedCard, opponent);
                Color color = displayedCard == null ? new Color(0.09f, 0.13f, 0.18f) :
                    (selectedMaterials.Contains(displayedCard) || displayedCard == selectedMonsterCard ||
                     displayedCard == selectedAttacker
                        ? new Color(0.11f, 0.45f, 0.43f)
                        : CardZoneColor(displayedCard));
                RectTransform slot = AddButton(contentRoot, label, xMin, y, xMax, y + BoardCardHeight, color,
                    () => OnZoneClicked(card, opponent), 11,
                    displayedCard != null && (!opponent || displayedCard.FaceUp)
                        ? displayedCard.Definition : null);
                AddSynchroMaterialHighlight(slot, displayedCard, opponent);
                if (!opponent)
                    ConfigureCardDropTarget(slot, draggedCard => HandleMonsterZoneDrop(draggedCard, zoneIndex));
                AddCardArtPlaceholder(slot,
                    displayedCard != null && (!opponent || displayedCard.FaceUp) ? displayedCard.Definition : null,
                    0f, 0f, 1f, 1f, displayedCard == null || displayedCard.FaceUp);
                RotateDefenseMonster(slot, displayedCard);
                FitMonsterSlotStats(slot, displayedCard);
            }
        }

        private void AddSynchroMaterialHighlight(
            RectTransform slot,
            CardInstance card,
            bool opponent)
        {
            if (opponent || card == null || !card.FaceUp ||
                selectedSynchro == null || selectedSynchro.Kind != CardKind.Synchro ||
                (engine.Phase != DuelPhase.MainOne && engine.Phase != DuelPhase.MainTwo))
                return;

            if (!selectedMaterials.Contains(card))
                return;

            Outline outline = slot.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 0.82f, 0.28f);
            outline.effectDistance = new Vector2(4f, 4f);
            outline.useGraphicAlpha = false;
        }

        private void DrawSpellTrapRow(CardInstance[] zones, float y)
        {
            for (int i = 0; i < zones.Length; i++)
            {
                int zoneIndex = i;
                float xMin = MonsterCardStartX + i * MonsterCardStepX;
                CardInstance card = zones[i];
                CardInstance displayedCard = card != null && animatingCardArrivals.Contains(card) ? null : card;
                string label = displayedCard == null ? "SPELL/TRAP\nSLOT" :
                    displayedCard.FaceUp ? CardLabel(displayedCard, true) : "SET CARD";
                Color color = displayedCard == null ? new Color(0.09f, 0.13f, 0.18f) :
                    displayedCard == selectedTarget ? new Color(0.11f, 0.45f, 0.43f) : CardZoneColor(displayedCard);
                RectTransform slot = AddButton(contentRoot, label, xMin, y, xMin + BoardCardWidth, y + BoardCardHeight, color,
                    () => OnSpellTrapZoneClicked(card), 10,
                    displayedCard != null && displayedCard.FaceUp ? displayedCard.Definition : null);
                ConfigureCardDropTarget(slot, draggedCard => HandleSpellTrapZoneDrop(draggedCard, zoneIndex));
                AddCardArtPlaceholder(slot, displayedCard != null && displayedCard.FaceUp
                        ? displayedCard.Definition : null,
                    0f, 0f, 1f, 1f, displayedCard == null || displayedCard.FaceUp);
                if (displayedCard != null && !displayedCard.FaceUp &&
                    engine != null && engine.IsTrapActivationBlockedByJinzo(displayedCard))
                    AddActivationBlockedIcon(slot);
                AddSpellTrapTargetHover(slot, card);
            }
        }

        private static void AddActivationBlockedIcon(RectTransform parent)
        {
            if (activationBlockedIconSprite == null)
            {
                const int size = 48;
                Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                texture.name = "Trap activation blocked icon";
                Color32[] pixels = new Color32[size * size];
                Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        Vector2 point = new Vector2(x, y);
                        float radius = Vector2.Distance(point, center);
                        float slashDistance = Mathf.Abs((x + y) - (size - 1)) / Mathf.Sqrt(2f);
                        bool ring = radius >= 16f && radius <= 21f;
                        bool slash = slashDistance <= 2.5f && radius <= 20f;
                        pixels[y * size + x] = ring || slash
                            ? new Color32(235, 35, 45, 255)
                            : new Color32(0, 0, 0, 0);
                    }
                }
                texture.SetPixels32(pixels);
                texture.Apply();
                activationBlockedIconSprite = Sprite.Create(texture, new Rect(0, 0, size, size),
                    new Vector2(0.5f, 0.5f));
            }

            GameObject iconObject = new GameObject("Activation blocked", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image));
            iconObject.transform.SetParent(parent, false);
            RectTransform rect = iconObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.68f, 0.68f);
            rect.anchorMax = new Vector2(0.96f, 0.96f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            Image image = iconObject.GetComponent<Image>();
            image.sprite = activationBlockedIconSprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
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
            CardInstance displayedCard = card != null && animatingCardArrivals.Contains(card) ? null : card;
            float xMin = opponent ? OpponentDeckX : 0.205f;
            float yMin = GetFieldSpellZoneY(opponent);
            float labelY = screen == ScreenMode.AiLab
                ? (opponent ? 0.73f : 0.17f)
                : yMin + BoardCardHeight + 0.002f;
            if (!opponent)
                AddText(contentRoot, "FIELD", xMin, labelY, xMin + BoardCardWidth,
                    labelY + 0.02f, 9, mutedColor, TextAnchor.MiddleCenter, true);
            RectTransform slot = AddButton(contentRoot,
                displayedCard == null ? "FIELD\nSLOT" : CardLabel(displayedCard, opponent),
                xMin, yMin, xMin + BoardCardWidth, yMin + BoardCardHeight,
                displayedCard == null ? new Color(0.09f, 0.13f, 0.18f) :
                    displayedCard == selectedTarget ? new Color(0.11f, 0.45f, 0.43f) : CardZoneColor(displayedCard),
                () => OnSpellTrapZoneClicked(card), 9,
                displayedCard != null && (!opponent || displayedCard.FaceUp)
                    ? displayedCard.Definition : null);
            if (!opponent)
                ConfigureCardDropTarget(slot, HandleFieldSpellZoneDrop);
            AddCardArtPlaceholder(slot,
                displayedCard != null && (!opponent || displayedCard.FaceUp) ? displayedCard.Definition : null,
                0f, 0f, 1f, 1f, displayedCard == null || displayedCard.FaceUp);
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
            if (screen == ScreenMode.Duel && engine != null && owner == engine.Human)
            {
                RightClickAction rightClick = button.gameObject.AddComponent<RightClickAction>();
                rightClick.OnRightClick = OpenForfeitConfirmation;
            }
            AddCardArtPlaceholder(button, null, 0.06f, 0.06f, 0.94f, 0.94f, false);
            Text label = button.Find("Label").GetComponent<Text>();
            SetAnchors(label.rectTransform, 0.02f, 0.02f, 0.98f, 0.4f);
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 5;
            label.resizeTextMaxSize = 9;
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
            WheelScrollRect scroll = viewport.gameObject.AddComponent<WheelScrollRect>();
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
            scroll.verticalScrollbar = CreateVerticalScrollbar(0.501f, 0.17f, 0.508f, 0.79f);
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
            bool selectingMonsterRebornTarget = selectedHandCard != null &&
                engine.Human.Hand.Contains(selectedHandCard) &&
                selectedHandCard.Definition.Effect == CardEffect.ReviveMonsterFromGraveyard;
            AddPanel(contentRoot, 0, 0.09f, 1, 0.9f, new Color(0.01f, 0.02f, 0.035f, 0.88f));
            AddPanel(contentRoot, 0.08f, 0.12f, 0.92f, 0.88f, panelColor);
            AddText(contentRoot, graveyardOwner.Name + " GRAVEYARD  •  " + graveyardOwner.Graveyard.Count + " cards",
                0.12f, 0.81f, selectingMonsterRebornTarget ? 0.45f : 0.72f, 0.86f,
                22, Color.white, TextAnchor.MiddleLeft, true);
            if (selectingMonsterRebornTarget)
            {
                AddButton(contentRoot, "YOU", 0.48f, 0.81f, 0.59f, 0.86f,
                    graveyardOwner == engine.Human ? accentColor : new Color(0.15f, 0.22f, 0.31f),
                    () => OpenGraveyard(engine.Human), 11);
                AddButton(contentRoot, "OPPONENT", 0.60f, 0.81f, 0.77f, 0.86f,
                    graveyardOwner == engine.Opponent ? accentColor : new Color(0.15f, 0.22f, 0.31f),
                    () => OpenGraveyard(engine.Opponent), 11);
            }
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
            if (selectedHandCard != null && engine.Human.Hand.Contains(selectedHandCard) &&
                selectedHandCard.Definition.Effect == CardEffect.ReviveMonsterFromGraveyard)
            {
                bool canSelectForReborn = selected != null && selected.Definition.IsMonster &&
                    selected.Definition.Kind != CardKind.Token;
                AddButton(contentRoot, "SPECIAL SUMMON SELECTED MONSTER", 0.55f, 0.17f, 0.88f, 0.23f,
                    canSelectForReborn ? accentColor : new Color(0.2f, 0.25f, 0.31f),
                    () =>
                    {
                        if (!canSelectForReborn)
                            return;
                        Act(() =>
                        {
                            bool succeeded = engine.TryActivate(selectedHandCard, selected);
                            if (succeeded)
                            {
                                graveyardOwner = null;
                                selectedGraveyardCard = null;
                            }
                            return succeeded;
                        });
                    }, 10);
            }
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
                return "SET CARD";

            if (!card.Definition.IsMonster)
                return card.Definition.DisplayName;

            return card.Definition.DisplayName + "\nATK " + card.CurrentAttack + " / DEF " +
                   card.Definition.Defense;
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
                else if (IsSelectingSilverwindTargets())
                {
                    if (selectedMaterials.Contains(card))
                        selectedMaterials.Remove(card);
                    else if (selectedMaterials.Count < 2)
                        selectedMaterials.Add(card);
                    else
                        notice = "Silverwind can select up to two monsters.";
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
            else if (selectedSynchro != null && selectedSynchro.Kind == CardKind.Synchro &&
                     (engine.Phase == DuelPhase.MainOne || engine.Phase == DuelPhase.MainTwo))
            {
                if (!card.FaceUp && !selectedMaterials.Contains(card))
                {
                    notice = "Synchro Materials must be face-up monsters.";
                    Render();
                    return;
                }

                if (!selectedMaterials.Add(card))
                    selectedMaterials.Remove(card);
                selectedMonsterCard = null;
                notice = GetSynchroMaterialSummary();
            }
            else if (IsSelectingSilverwindTargets())
            {
                if (selectedMaterials.Contains(card))
                    selectedMaterials.Remove(card);
                else if (selectedMaterials.Count < 2)
                    selectedMaterials.Add(card);
                else
                    notice = "Silverwind can select up to two monsters.";
                selectedTarget = null;
            }
            else if (card.Definition.Effect == CardEffect.BlackwingSilverwind)
            {
                selectedMonsterCard = card;
                selectedMaterials.Clear();
                selectedTarget = null;
            }
            else
            {
                selectedMonsterCard = card;
                if (!selectedMaterials.Add(card))
                    selectedMaterials.Remove(card);
            }
            Render();
        }

        private bool IsSelectingSilverwindTargets()
        {
            return selectedMonsterCard != null &&
                selectedMonsterCard.Definition.Effect == CardEffect.BlackwingSilverwind &&
                selectedMonsterCard.SummonedTurnNumber == engine.TurnNumber &&
                engine.Human.MonsterZones.Contains(selectedMonsterCard) &&
                (engine.Phase == DuelPhase.MainOne || engine.Phase == DuelPhase.MainTwo);
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
                if (selectedSynchro != null && selectedSynchro.Kind == CardKind.Synchro)
                {
                    bool canSummon = engine.IsHumanActionWindow &&
                        engine.CanSynchroSummon(selectedSynchro, selectedMaterials.ToList(), engine.Human);
                    AddContextAction(ref actionX,
                        canSummon ? "SYNCHRO SUMMON" : "SELECT MATERIALS",
                        canSummon ? accentColor : new Color(0.2f, 0.25f, 0.31f),
                        canSummon
                            ? () =>
                            {
                                CardDefinition synchro = selectedSynchro;
                                List<CardInstance> materials = selectedMaterials.ToList();
                                Act(() => engine.TrySynchroSummon(synchro, materials));
                            }
                            : () => { });
                    AddContextAction(ref actionX, "CANCEL",
                        new Color(0.32f, 0.2f, 0.23f), () =>
                        {
                            selectedSynchro = null;
                            selectedMaterials.Clear();
                            notice = "";
                            Render();
                        });
                }
                else if (selectedRitualMonster != null)
                {
                    if (engine.Human.Hand.Any(card => card.Definition.Kind == CardKind.Spell &&
                                                      card.Definition.Subtype == CardSubtype.Ritual))
                        AddContextAction(ref actionX, "RITUAL SUMMON", accentColor, () => Act(() =>
                            engine.TryRitualSummon(selectedRitualMonster, selectedMaterials.ToList())));
                    else
                        AddContextAction(ref actionX, "RITUAL SPELL REQUIRED",
                            new Color(0.2f, 0.25f, 0.31f), () => { });
                }
                else if (selectedHandCard != null && selectedHandCard.Definition.IsMonster)
                {
                    if (selectedMonsterCard != null &&
                        selectedMonsterCard.Definition.Effect == CardEffect.AncientFairyDragon &&
                        selectedHandCard.Definition.Level <= 4)
                        AddContextAction(ref actionX, "ANCIENT FAIRY: SPECIAL SUMMON",
                            accentColor, () => Act(() =>
                                engine.TryActivateAncientFairyDragonSummon(
                                    selectedMonsterCard, selectedHandCard)));
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
                        if (selectedHandCard.Definition.Effect == CardEffect.QuickdrawSynchron)
                            AddContextAction(ref actionX, "SPECIAL SUMMON", accentColor, () => Act(() =>
                                engine.TryActivate(selectedHandCard)));

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
                                : selectedHandCard.Definition.Effect == CardEffect.NoblemanOfCrossout
                                    ? selectedTarget ?? selectedMonsterCard
                                : selectedMonsterCard;
                            string actionLabel = selectedHandCard.Definition.Effect == CardEffect.DestroySpellTrap &&
                                selectedTarget != null
                                ? "DESTROY SELECTED"
                                : selectedHandCard.Definition.Effect == CardEffect.NoblemanOfCrossout &&
                                  effectTarget != null
                                    ? "CROSS OUT SELECTED"
                                : selectedHandCard.Definition.Effect == CardEffect.ReviveMonsterFromGraveyard
                                    ? "OPEN GRAVEYARD TO SELECT"
                                : "ACTIVATE SPELL";
                            bool needsGraveyardChoice = selectedHandCard.Definition.Effect ==
                                CardEffect.ReviveMonsterFromGraveyard;
                            AddContextAction(ref actionX, actionLabel,
                                needsGraveyardChoice ? new Color(0.2f, 0.34f, 0.46f) : accentColor,
                                needsGraveyardChoice
                                    ? () => OpenGraveyard(engine.Human)
                                    : () => Act(() => engine.TryActivate(selectedHandCard, effectTarget)));
                        }
                    }
                    if (selectedHandCard.Definition.Subtype != CardSubtype.Field)
                        AddContextAction(ref actionX, "SET BACKROW", new Color(0.2f, 0.34f, 0.46f), () => Act(() =>
                            engine.TrySetBackrow(selectedHandCard)));
                }

                if (selectedHandCard == null && selectedMonsterCard != null && selectedMonsterCard.FaceUp)
                {
                    if (selectedMonsterCard.Definition.Effect == CardEffect.AncientFairyDragon)
                        AddContextAction(ref actionX, "DESTROY FIELD SPELLS + GAIN 1000 LP",
                            accentColor, () => Act(() =>
                                engine.TryActivateAncientFairyDragonFieldEffect(selectedMonsterCard)));
                    if (selectedMonsterCard.Definition.Effect == CardEffect.BlackwingSilverwind &&
                        selectedMonsterCard.SummonedTurnNumber == engine.TurnNumber)
                        AddContextAction(ref actionX, "SILVERWIND: DESTROY SELECTED (" +
                            selectedMaterials.Count + "/2)",
                            selectedMaterials.Count <= 2 ? accentColor : new Color(0.2f, 0.25f, 0.31f),
                            () => Act(() => engine.TryActivateBlackwingSilverwind(
                                selectedMonsterCard, selectedMaterials.ToList())));
                    else if (selectedMonsterCard.Definition.Effect != CardEffect.None &&
                             selectedMonsterCard.Definition.Effect != CardEffect.AncientFairyDragon)
                        AddContextAction(ref actionX, "ACTIVATE EFFECT", new Color(0.2f, 0.34f, 0.46f), () => Act(() =>
                            engine.TryActivate(selectedMonsterCard, selectedMonsterCard)));
                    AddContextAction(ref actionX, "CHANGE POSITION", new Color(0.2f, 0.34f, 0.46f), () => Act(() =>
                        engine.TryChangePosition(selectedMonsterCard)));
                }

                if (selectedMonsterCard != null && !selectedMonsterCard.FaceUp &&
                    engine.CanFlipSummon(selectedMonsterCard))
                    AddContextAction(ref actionX, "FLIP SUMMON", accentColor, () => Act(() =>
                        engine.TryFlipSummon(selectedMonsterCard)));

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
                        if (definition.Kind == CardKind.Synchro &&
                            engine.Phase != DuelPhase.MainOne && engine.Phase != DuelPhase.MainTwo)
                        {
                            notice = "Synchro Summons are only available during a Main Phase.";
                            extraDeckOpen = false;
                            Render();
                            return;
                        }
                        selectedSynchro = definition;
                        selectedMaterials.Clear();
                        selectedMonsterCard = null;
                        selectedTarget = null;
                        if (definition.Kind == CardKind.Synchro)
                        {
                            selectedHandCard = null;
                            selectedAttacker = null;
                        }
                        extraDeckOpen = false;
                        notice = definition.Kind == CardKind.Synchro
                            ? "Select face-up field monsters: 1 Tuner + 1 or more non-Tuners totaling Level " +
                              definition.Level + ". Selected materials will be outlined in gold."
                            : "";
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
            float width = DuelHandLayout.GetCardWidth(count, GetScreenAspectRatio(), false);
            float height = GetPortraitHeight(width);
            for (int i = 0; i < count; i++)
            {
                CardInstance card = engine.Human.Hand[i];
                Vector2 center = DuelHandLayout.GetCardCenter(i, count, GetScreenAspectRatio(), false);
                float x = center.x - width * 0.5f;
                float bottom = center.y - height * 0.5f;
                RectTransform cardButton = AddButton(contentRoot, card.Definition.DisplayName, x, bottom,
                    x + width, bottom + height,
                    selectedHandCard == card || selectedMaterials.Contains(card)
                        ? Color.Lerp(CardFrameColor(card.Definition), accentColor, 0.5f)
                        : CardFrameColor(card.Definition),
                    () =>
                    {
                        if (selectedSynchro != null && selectedSynchro.Kind == CardKind.Synchro)
                        {
                            notice = "Synchro Materials must be face-up monsters on your field.";
                            Render();
                            return;
                        }
                        if (selectedRitualMonster == card)
                        {
                            selectedRitualMonster = null;
                            selectedHandCard = null;
                            selectedMaterials.Clear();
                            Render();
                            return;
                        }
                        if (selectedRitualMonster != null)
                        {
                            if (!card.Definition.IsMonster)
                            {
                                notice = "Only monsters can be selected as Ritual Materials.";
                                Render();
                                return;
                            }
                            if (!selectedMaterials.Add(card))
                                selectedMaterials.Remove(card);
                            selectedHandCard = selectedRitualMonster;
                            Render();
                            return;
                        }
                        if (card.Definition.IsRitualMonster)
                        {
                            selectedRitualMonster = card;
                            selectedHandCard = card;
                            selectedMonsterCard = null;
                            selectedTarget = null;
                            selectedMaterials.Clear();
                            notice = "";
                            Render();
                            return;
                        }
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
                        CardInstance ancientFairySource =
                            selectedMonsterCard != null &&
                            selectedMonsterCard.Definition.Effect == CardEffect.AncientFairyDragon
                                ? selectedMonsterCard
                                : null;
                        selectedHandCard = card;
                        selectedTarget = null;
                        notice = "";
                        if (ancientFairySource == null)
                            selectedMonsterCard = null;
                        if (card.Definition.Effect != CardEffect.FusionSummon)
                        {
                            selectedSynchro = null;
                            selectedMaterials.Clear();
                        }
                        Render();
                    }, 10, card.Definition, true);
                DuelCardDragHandler dragHandler = cardButton.gameObject.AddComponent<DuelCardDragHandler>();
                dragHandler.Card = card;
                cardButton.gameObject.AddComponent<DuelHandCardVisual>().Card = card;
                dragHandler.OnBeginDragAction = eventData =>
                    BeginHandCardDrag(card, cardButton, eventData);
                dragHandler.OnDragAction = UpdateDraggedCardVisual;
                dragHandler.OnEndDragAction = EndHandCardDrag;
                if (animatedHandCards.Contains(card))
                    cardButton.gameObject.AddComponent<CanvasGroup>().alpha = 0f;
                AddCardArtPlaceholder(cardButton, card.Definition, 0f, 0f, 1f, 1f, true);
                Text label = cardButton.Find("Label").GetComponent<Text>();
                SetAnchors(label.rectTransform, 0.02f, 0.02f, 0.98f, 0.28f);
                label.alignment = TextAnchor.MiddleCenter;
            }
        }

        private void DrawReplayControls()
        {
            if (replayStates.Count == 0 || (screen != ScreenMode.Duel && screen != ScreenMode.AiLab))
                return;

            bool atStart = replayIndex <= 0;
            bool atEnd = replayIndex >= replayStates.Count - 1;
            AddPanel(contentRoot, 0.75f, 0.015f, 0.995f, 0.12f, new Color(0.04f, 0.08f, 0.12f, 0.9f));
            AddText(contentRoot, "REPLAY " + (replayIndex + 1) + " / " + replayStates.Count,
                0.76f, 0.065f, 0.985f, 0.11f, 9, mutedColor, TextAnchor.MiddleCenter, true);
            AddButton(contentRoot, "|<", 0.76f, 0.02f, 0.81f, 0.055f,
                atStart ? new Color(0.25f, 0.29f, 0.34f) : new Color(0.15f, 0.28f, 0.38f),
                () => { if (!atStart) ReplayToStart(); }, 10);
            AddButton(contentRoot, "<", 0.815f, 0.02f, 0.865f, 0.055f,
                atStart ? new Color(0.25f, 0.29f, 0.34f) : new Color(0.15f, 0.22f, 0.31f),
                () => { if (!atStart) ReplayStepBack(); }, 10);
            AddButton(contentRoot, ">", 0.87f, 0.02f, 0.92f, 0.055f,
                atEnd ? new Color(0.25f, 0.29f, 0.34f) : new Color(0.15f, 0.22f, 0.31f),
                () => { if (!atEnd) ReplayStepForward(); }, 10);
            AddButton(contentRoot, ">|", 0.925f, 0.02f, 0.98f, 0.055f,
                atEnd ? new Color(0.25f, 0.29f, 0.34f) : new Color(0.15f, 0.28f, 0.38f),
                () => { if (!atEnd) ReplayToEnd(); }, 10);
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
        }

        private void DrawSynchroMaterialPrompt()
        {
            if (selectedSynchro == null || selectedSynchro.Kind != CardKind.Synchro ||
                (engine.Phase != DuelPhase.MainOne && engine.Phase != DuelPhase.MainTwo))
                return;

            RectTransform panel = AddPanel(contentRoot, 0.22f, 0.905f, 0.82f, 0.99f,
                new Color(0.035f, 0.12f, 0.16f, 0.96f));
            panel.GetComponent<Image>().raycastTarget = false;
            AddText(contentRoot,
                "SYNCHRO MATERIALS  •  " + selectedSynchro.DisplayName + "  (LEVEL " +
                selectedSynchro.Level + ")",
                0.235f, 0.95f, 0.805f, 0.985f, 14, new Color(1f, 0.82f, 0.28f),
                TextAnchor.MiddleCenter, true);
            AddText(contentRoot,
                "Click face-up field monsters to add/remove: exactly 1 Tuner + 1+ non-Tuners. " +
                "Gold outline = selected. " +
                GetSynchroMaterialSummary(),
                0.235f, 0.91f, 0.805f, 0.95f, 11, Color.white,
                TextAnchor.MiddleCenter);
        }

        private string GetSynchroMaterialSummary()
        {
            if (selectedSynchro == null || selectedSynchro.Kind != CardKind.Synchro)
                return "";

            List<CardInstance> materials = selectedMaterials
                .Where(card => engine.Human.MonsterZones.Contains(card))
                .ToList();
            int selectedLevel = materials.Sum(card =>
                DuelEngine.GetSynchroMaterialLevel(selectedSynchro, card));
            int tunerCount = materials.Count(card => card.Definition.IsTuner);
            return "Selected Level " + selectedLevel + " / " + selectedSynchro.Level +
                   "  •  Tuners " + tunerCount + " / 1  •  Materials " + materials.Count;
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
                StartDuelFromMenu();
            }, 20);
        }

        private void DrawForfeitConfirmationOverlay()
        {
            AddPanel(contentRoot, 0, 0, 1, 1, new Color(0.01f, 0.02f, 0.035f, 0.78f));
            AddPanel(contentRoot, 0.3f, 0.35f, 0.7f, 0.65f, panelColor);
            AddText(contentRoot, "FORFEIT DUEL?", 0.33f, 0.56f, 0.67f, 0.62f,
                22, Color.white, TextAnchor.MiddleCenter, true);
            AddText(contentRoot, "Are you sure you want to forfeit this match?",
                0.33f, 0.49f, 0.67f, 0.55f, 14, mutedColor, TextAnchor.MiddleCenter);
            AddButton(contentRoot, "FORFEIT", 0.36f, 0.39f, 0.49f, 0.46f,
                new Color(0.55f, 0.2f, 0.22f), ForfeitDuel, 13);
            AddButton(contentRoot, "CANCEL", 0.51f, 0.39f, 0.64f, 0.46f,
                new Color(0.2f, 0.34f, 0.46f), () =>
                {
                    forfeitConfirmOpen = false;
                    Render();
                }, 13);
        }

        private void DrawTributeSelectionOverlay()
        {
            int tributeCount = pendingTributeSummonCard.Definition.Level >= 7 ? 2 : 1;
            AddPanel(contentRoot, 0f, 0f, 1f, 1f, new Color(0.01f, 0.02f, 0.035f, 0.88f));
            AddPanel(contentRoot, 0.2f, 0.3f, 0.8f, 0.7f, panelColor);
            AddText(contentRoot, "TRIBUTE SUMMON", 0.24f, 0.63f, 0.76f, 0.68f,
                21, Color.white, TextAnchor.MiddleCenter, true);
            AddText(contentRoot, "Choose " + tributeCount + " monster" +
                    (tributeCount == 1 ? "" : "s") + " to Tribute for " +
                    pendingTributeSummonCard.Definition.DisplayName + ".",
                0.24f, 0.57f, 0.76f, 0.62f, 13, mutedColor, TextAnchor.MiddleCenter);

            List<CardInstance> fieldMonsters = engine.Human.MonsterZones
                .Where(card => card != null)
                .ToList();
            float buttonWidth = Mathf.Min(0.14f, 0.68f / Mathf.Max(1, fieldMonsters.Count));
            float totalWidth = fieldMonsters.Count * buttonWidth;
            float startX = 0.5f - totalWidth * 0.5f;
            for (int i = 0; i < fieldMonsters.Count; i++)
            {
                CardInstance monster = fieldMonsters[i];
                bool selected = selectedMaterials.Contains(monster);
                AddButton(contentRoot,
                    monster.Definition.DisplayName + "\nATK " + monster.CurrentAttack,
                    startX + i * buttonWidth, 0.43f,
                    startX + (i + 1) * buttonWidth - 0.008f, 0.55f,
                    selected ? new Color(0.12f, 0.52f, 0.43f) : CardZoneColor(monster),
                    () =>
                    {
                        if (!selectedMaterials.Add(monster))
                            selectedMaterials.Remove(monster);
                        Render();
                    }, 10);
            }
            if (fieldMonsters.Count == 0)
                AddText(contentRoot, "You have no monsters available to Tribute.",
                    0.28f, 0.44f, 0.72f, 0.52f, 13, mutedColor, TextAnchor.MiddleCenter);

            bool canConfirm = selectedMaterials.Count == tributeCount;
            AddButton(contentRoot, "SUMMON",
                0.34f, 0.34f, 0.49f, 0.41f,
                canConfirm ? accentColor : new Color(0.2f, 0.25f, 0.31f),
                () =>
                {
                    if (!canConfirm)
                        return;
                    CardInstance card = pendingTributeSummonCard;
                    bool summoned = engine.TryNormalSummon(card,
                        selectedMaterials.Where(material => engine.Human.MonsterZones.Contains(material)).ToList(),
                        preferredMonsterZone: pendingTributeSummonZone);
                    pendingTributeSummonCard = null;
                    pendingTributeSummonZone = -1;
                    if (summoned)
                    {
                        notice = "";
                        ClearSelection();
                    }
                    else if (engine.Messages.Count > 0)
                        notice = engine.Messages[engine.Messages.Count - 1];
                    Render();
                }, 12);
            AddButton(contentRoot, "CANCEL",
                0.51f, 0.34f, 0.66f, 0.41f,
                new Color(0.32f, 0.2f, 0.23f), () =>
                {
                    pendingTributeSummonCard = null;
                    pendingTributeSummonZone = -1;
                    ClearSelection();
                    Render();
                }, 12);
        }

        private string PhasePrompt()
        {
            if (!engine.IsHumanActionWindow)
                return "Opponent is resolving its turn.";
            if (selectedRitualMonster != null)
            {
                if (!engine.Human.Hand.Any(card => card.Definition.Kind == CardKind.Spell &&
                                                   card.Definition.Subtype == CardSubtype.Ritual))
                    return "Select a Ritual Monster, then add a Ritual Spell to your hand.";
                int materialLevel = selectedMaterials.Where(card =>
                    engine.Human.Hand.Contains(card) || engine.Human.MonsterZones.Contains(card))
                    .Sum(card => card.Definition.Level);
                return "Select Ritual Materials from your hand or field. Selected Level " + materialLevel +
                       " / required " + selectedRitualMonster.Definition.Level + ".";
            }
            if (selectedSynchro != null && selectedSynchro.Kind == CardKind.Synchro &&
                (engine.Phase == DuelPhase.MainOne || engine.Phase == DuelPhase.MainTwo))
            {
                return "Select face-up field monsters: exactly 1 Tuner + 1 or more non-Tuners. " +
                       GetSynchroMaterialSummary() + ". Press SYNCHRO SUMMON when valid.";
            }
            switch (engine.Phase)
            {
                case DuelPhase.Draw:
                    return "Draw Phase. Review your hand, then select Standby or a later phase.";
                case DuelPhase.Standby:
                    return "Standby Phase. Select Main 1 when ready.";
                case DuelPhase.MainOne:
                    if (engine.Human.BattlePhaseSkippedTurn == engine.TurnNumber)
                        return "Your Battle Phase is skipped this turn. Continue to Main Phase 2.";
                    if (selectedHandCard != null && selectedHandCard.Definition.IsMonster)
                    {
                        if (!selectedHandCard.Definition.CanNormalSummon)
                            return "This card cannot be Normal Summoned; it needs its listed Special Summon procedure.";
                        int tributes = selectedHandCard.Definition.Level >= 7 ? 2 :
                            selectedHandCard.Definition.Level >= 5 ? 1 : 0;
                        if (tributes > 0)
                            return "Select exactly " + tributes +
                                   " of your monsters as Tribute, then choose Tribute Summon or Set.";
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
                notice = "";
                selectedHandCard = null;
                selectedRitualMonster = null;
                selectedMonsterCard = null;
                selectedAttacker = null;
                selectedTarget = null;
                selectedMaterials.Clear();
                selectedSynchro = null;
            }
            else if (engine.Messages.Count > 0)
                notice = engine.Messages[engine.Messages.Count - 1];
            Render();
        }

        private void ClearSelection()
        {
            selectedHandCard = null;
            selectedRitualMonster = null;
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
            bool withinCopyLimits = HasValidDeckCopyCounts(profile.deck.Select(StarterCatalog.Find));
            bool valid = mainCount >= DuelEngine.MinimumMainDeckSize &&
                mainCount <= 40 && extraCount <= 15 && withinCopyLimits;
            AddText(contentRoot, "DECK BUILDER", 0.035f, 0.84f, 0.38f, 0.89f,
                24, Color.white, TextAnchor.MiddleLeft, true);
            AddText(contentRoot, "MAIN " + mainCount + "/40     EXTRA " + extraCount + "/15",
                0.44f, 0.845f, 0.7f, 0.89f, 13,
                valid ? accentColor : new Color(1f, 0.65f, 0.32f), TextAnchor.MiddleRight, true);
            string deckStatus = valid ? "READY TO DUEL" :
                !withinCopyLimits ? "CARD COPY LIMIT EXCEEDED" :
                mainCount < DuelEngine.MinimumMainDeckSize ? "MAIN DECK NEEDS 40 CARDS" :
                mainCount > 40 ? "MAIN DECK MAX IS 40 CARDS" : "EXTRA DECK MAX IS 15 CARDS";
            AddText(contentRoot, deckStatus,
                0.71f, 0.845f, 0.965f, 0.89f, 12,
                valid ? accentColor : new Color(1f, 0.65f, 0.32f), TextAnchor.MiddleRight, true);

            AddPanel(contentRoot, 0.035f, 0.12f, 0.405f, 0.82f, panelColor);
            AddText(contentRoot, "YOUR DECK", 0.055f, 0.775f, 0.27f, 0.81f,
                17, Color.white, TextAnchor.MiddleLeft, true);
            AddText(contentRoot, mainCount + " main  •  " + extraCount + " extra",
                0.27f, 0.775f, 0.385f, 0.81f, 11, mutedColor, TextAnchor.MiddleRight);

            RectTransform deckViewport = AddPanel(contentRoot, 0.05f, 0.15f, 0.385f, 0.76f, backgroundColor);
            deckViewport.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            WheelScrollRect deckScroll = deckViewport.gameObject.AddComponent<WheelScrollRect>();
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
            deckScroll.verticalScrollbar = CreateVerticalScrollbar(0.39f, 0.15f, 0.397f, 0.76f);
            deckScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            for (int i = 0; i < deckCards.Count; i++)
                AddDeckListRow(deckList, deckCards[i], i, deckRowHeight);
            if (deckCards.Count == 0)
                AddText(contentRoot, "Your deck is empty.", 0.07f, 0.43f, 0.37f, 0.5f,
                    16, mutedColor, TextAnchor.MiddleCenter);

            AddText(contentRoot, "CARD LIBRARY", 0.435f, 0.775f, 0.68f, 0.81f,
                17, Color.white, TextAnchor.MiddleLeft, true);
            AddText(contentRoot, "Scroll to load the next page. Hover cards to preview.",
                0.51f, 0.775f, 0.975f, 0.815f, 11, mutedColor, TextAnchor.MiddleCenter);
            AddDeckSearchField(0.435f, 0.695f, 0.685f, 0.74f);
            AddButton(contentRoot, CardLibraryFilterLabel(cardLibraryFilter) + "  ▼",
                0.695f, 0.695f, 0.855f, 0.74f, CardFilterColor(cardLibraryFilter),
                () => { cardFilterOptionsOpen = !cardFilterOptionsOpen; Render(); }, 11);
            AddButton(contentRoot, "SORT: " + DeckSortLabel(),
                0.865f, 0.695f, 0.965f, 0.74f, new Color(0.2f, 0.34f, 0.46f),
                () =>
                {
                    deckSortMode = (deckSortMode + 1) % 4;
                    deckLibraryPage = 0;
                    deckLibraryScrollPosition = 1f;
                    Render();
                }, 10);

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

            RectTransform libraryViewport = AddPanel(contentRoot, 0.435f, 0.18f, 0.95f, 0.68f, backgroundColor);
            libraryViewport.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            WheelScrollRect libraryScroll = libraryViewport.gameObject.AddComponent<WheelScrollRect>();
            libraryScroll.horizontal = false;
            libraryScroll.vertical = true;
            libraryScroll.movementType = ScrollRect.MovementType.Clamped;
            ConfigureMouseWheelScrolling(libraryScroll);
            libraryScroll.PageBoundaryScrolled = direction =>
            {
                int nextPage = deckLibraryPage + direction;
                if (nextPage < 0 || nextPage >= deckPageCount)
                    return;
                deckLibraryPage = nextPage;
                deckLibraryScrollPosition = direction > 0 ? 1f : 0f;
                Render();
            };
            libraryScroll.onValueChanged.AddListener(_ =>
                deckLibraryScrollPosition = libraryScroll.verticalNormalizedPosition);
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
            libraryScroll.verticalScrollbar = CreateVerticalScrollbar(0.955f, 0.18f, 0.963f, 0.68f);
            libraryScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            for (int i = 0; i < visibleLibraryCards.Count; i++)
                AddDeckLibraryRow(libraryList, visibleLibraryCards[i], i, libraryRowHeight);
            libraryScroll.verticalNormalizedPosition = deckLibraryScrollPosition;
            if (libraryCards.Count == 0)
                AddText(contentRoot, "No cards match these filters.", 0.46f, 0.4f, 0.94f, 0.48f,
                    15, mutedColor, TextAnchor.MiddleCenter);

            AddButton(contentRoot, "<", 0.435f, 0.125f, 0.49f, 0.165f,
                new Color(0.2f, 0.34f, 0.46f), () =>
                {
                    deckLibraryPage = Mathf.Max(0, deckLibraryPage - 1);
                    deckLibraryScrollPosition = 0f;
                    Render();
                }, 12);
            AddText(contentRoot, "PAGE " + (deckLibraryPage + 1) + " / " + deckPageCount,
                0.5f, 0.125f, 0.9f, 0.165f, 11, mutedColor, TextAnchor.MiddleCenter);
            AddButton(contentRoot, ">", 0.91f, 0.125f, 0.965f, 0.165f,
                new Color(0.2f, 0.34f, 0.46f), () =>
                {
                    deckLibraryPage = Mathf.Min(deckPageCount - 1, deckLibraryPage + 1);
                    deckLibraryScrollPosition = 1f;
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
            WheelScrollRect scroll = parent.GetComponentInParent<WheelScrollRect>();
            if (scroll != null)
                AttachScrollWheelForwarder(row.gameObject, scroll);
            AttachCardHover(row, card);
            string kind = card.IsExtraDeckCard ? "EXTRA" : CardTypeLabel(card);
            AddText(row, card.DisplayName + "\n" + kind + "  •  " +
                count + "/" + card.MaxDeckCopies + " copies",
                0.16f, 0.08f, 0.975f, 0.92f, 12, Color.white, TextAnchor.MiddleLeft, true);
            AddButton(row, "−", 0.025f, 0.16f, 0.14f, 0.84f,
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
            WheelScrollRect scroll = parent.GetComponentInParent<WheelScrollRect>();
            if (scroll != null)
                AttachScrollWheelForwarder(row.gameObject, scroll);
            AttachCardHover(row, card);
            string details = card.IsMonster
                ? CardTypeLabel(card) + "  •  " + card.Level + "  •  " +
                  card.AttackDisplay + "/" + card.DefenseDisplay
                : CardTypeLabel(card);
            AddText(row, card.DisplayName + "\n" + details + "  •  OWNED " + owned + "  •  IN DECK " +
                inDeck + "/" + card.MaxDeckCopies,
                0.16f, 0.08f, 0.82f, 0.92f, 11, Color.white, TextAnchor.MiddleLeft, true);
            AddButton(row, "+", 0.025f, 0.16f, 0.14f, 0.84f,
                new Color(0.14f, 0.38f, 0.34f), () => ChangeDeck(card, 1), 15);
        }

        private void AddDeckSearchField(float xMin, float yMin, float xMax, float yMax)
        {
            AddCatalogSearchField(xMin, yMin, xMax, yMax, deckSearchQuery, "Search card names...", value =>
            {
                if (deckSearchQuery == value)
                    return;
                deckSearchQuery = value;
                deckLibraryPage = 0;
                deckLibraryScrollPosition = 1f;
                StartCoroutine(RefreshCatalogAfterInput());
            });
        }

        private void AddCatalogSearchField(
            float xMin,
            float yMin,
            float xMax,
            float yMax,
            string query,
            string placeholderText,
            Action<string> onQueryChanged)
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
            placeholder.text = placeholderText;
            placeholder.alignment = TextAnchor.MiddleLeft;
            placeholder.raycastTarget = false;
            input.placeholder = placeholder;
            input.text = query;
            input.onEndEdit.AddListener(value => onQueryChanged(value));
        }

        private IEnumerator RefreshCatalogAfterInput()
        {
            yield return null;
            if (screen == ScreenMode.Deck || screen == ScreenMode.Cards)
                Render();
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
                        deckLibraryPage = 0;
                        deckLibraryScrollPosition = 1f;
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
                .Where(MatchesCardLibrarySearch)
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
            AddCatalogSearchField(0.025f, 0.775f, 0.455f, 0.815f, cardLibrarySearchQuery,
                "Search all card names...", value =>
                {
                    if (cardLibrarySearchQuery == value)
                        return;
                    cardLibrarySearchQuery = value;
                    cardLibraryPage = 0;
                    cardLibraryScrollPosition = 1f;
                    StartCoroutine(RefreshCatalogAfterInput());
                });
            AddText(contentRoot, "Scroll at either end of the list to change pages.",
                0.51f, 0.775f, 0.975f, 0.815f, 11, mutedColor, TextAnchor.MiddleCenter);
            RectTransform viewport = AddPanel(contentRoot, 0.025f, 0.12f, 0.445f, 0.765f, panelColor);
            Mask mask = viewport.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = true;
            WheelScrollRect scroll = viewport.gameObject.AddComponent<WheelScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            ConfigureMouseWheelScrolling(scroll);
            scroll.PageBoundaryScrolled = direction =>
            {
                int nextPage = cardLibraryPage + direction;
                if (nextPage < 0 || nextPage >= cardPageCount)
                    return;
                cardLibraryPage = nextPage;
                cardLibraryScrollPosition = direction > 0 ? 1f : 0f;
                Render();
            };
            scroll.onValueChanged.AddListener(_ =>
                cardLibraryScrollPosition = scroll.verticalNormalizedPosition);

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
            scroll.verticalScrollbar = CreateVerticalScrollbar(0.45f, 0.12f, 0.458f, 0.765f);
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            for (int i = 0; i < visibleCards.Count; i++)
                AddCardLibraryRow(listContent, visibleCards[i], i, rowHeight);
            scroll.verticalNormalizedPosition = cardLibraryScrollPosition;

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
                    cardLibraryPage = Mathf.Max(0, cardLibraryPage - 1);
                    cardLibraryScrollPosition = 0f;
                    Render();
                }, 12);
            AddText(contentRoot, "PAGE " + (cardLibraryPage + 1) + " / " + cardPageCount,
                0.375f, 0.075f, 0.625f, 0.11f, 11, mutedColor, TextAnchor.MiddleCenter);
            AddButton(contentRoot, ">", 0.63f, 0.075f, 0.69f, 0.11f,
                new Color(0.2f, 0.34f, 0.46f), () =>
                {
                    cardLibraryPage = Mathf.Min(cardPageCount - 1, cardLibraryPage + 1);
                    cardLibraryScrollPosition = 1f;
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

        private bool MatchesCardLibrarySearch(CardDefinition card)
        {
            return string.IsNullOrWhiteSpace(cardLibrarySearchQuery) ||
                   card.DisplayName.IndexOf(cardLibrarySearchQuery.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
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
                        cardLibraryPage = 0;
                        cardLibraryScrollPosition = 1f;
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
            WheelScrollRect scroll = parent.GetComponentInParent<WheelScrollRect>();
            if (scroll != null)
                AttachScrollWheelForwarder(row.gameObject, scroll);

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
                if (count >= card.MaxDeckCopies)
                    notice = "Copy limit reached (" + card.MaxDeckCopies + " per card).";
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
                notice = "Keep at least 40 cards in your Main Deck.";
            else
            {
                profile.deck.Remove(card.CardId);
                if (engine != null)
                    notice = "Deck changed. Start a new duel to use the updated list.";
            }
            SaveProfile();
            Render();
        }

        private static bool HasValidDeckCopyCounts(IEnumerable<CardDefinition> deck)
        {
            return deck != null && deck.All(card => card != null) &&
                   deck.GroupBy(card => card.CardId, StringComparer.Ordinal)
                       .All(group => group.Count() <= group.First().MaxDeckCopies);
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
                        ? "Step advances one AI action. Autoplay plays actions in sequence and pauses between turns."
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
            DrawGraveyardButton(engine.Opponent, 0.205f, OpponentSpellTrapY);
            DrawDeckButton(engine.Opponent, OpponentDeckX, OpponentSpellTrapY);
            DrawBanishedIndicator(engine.Opponent, 0.205f, OpponentMonsterY);
            DrawSpellTrapRow(engine.Opponent.SpellTrapZones, OpponentSpellTrapY);
            DrawFieldSpellSlot(engine.Opponent.FieldSpellZone, true);
            DrawReadOnlyZones(engine.Opponent.MonsterZones, OpponentMonsterY);
            DrawAiVisibleHand(engine.Opponent, true);

            AddPanel(contentRoot, 0.025f, 0.465f, 0.195f, 0.565f,
                Color.Lerp(panelColor, GetTurnColor(firstPlayerTurn), 0.3f));
            AddText(contentRoot, "TURN " + engine.TurnNumber + "  •  " + engine.CurrentPlayer.Name,
                0.03f, 0.535f, 0.19f, 0.558f, 9, accentColor, TextAnchor.MiddleCenter, true);
            AddText(contentRoot, engine.Phase == DuelPhase.GameOver
                    ? engine.Winner + " WINS"
                    : engine.PhaseLabel.ToUpperInvariant(),
                0.03f, 0.476f, 0.19f, 0.53f, 10, Color.white, TextAnchor.MiddleCenter, true);

            AddButton(contentRoot, "STEP",
                0.855f, 0.705f, 0.975f, 0.74f,
                new Color(0.2f, 0.34f, 0.46f), () =>
                {
                    if (running)
                    {
                        engine.StepAiTurn();
                        RecordReplayState();
                        Render();
                    }
                }, 10);
            AddButton(contentRoot, "PACE " + aiAutoplayPaces[aiAutoplayPaceIndex].ToString("0") + "s",
                0.855f, 0.665f, 0.975f, 0.7f,
                new Color(0.2f, 0.34f, 0.46f), () =>
                {
                    aiAutoplayPaceIndex = (aiAutoplayPaceIndex + 1) % aiAutoplayPaces.Length;
                    Render();
                }, 10);
            AddText(contentRoot, aiAutoplay ? "SPACE: PAUSE AUTO" : "SPACE: START AUTO",
                0.855f, 0.625f, 0.975f, 0.66f, 9,
                aiAutoplay ? new Color(1f, 0.78f, 0.35f) : mutedColor,
                TextAnchor.MiddleCenter, true);
            AddButton(contentRoot, "SIMULATE",
                0.855f, 0.585f, 0.975f, 0.62f,
                new Color(0.15f, 0.38f, 0.66f), SimulateAiLabDuel, 10);
            AddButton(contentRoot, "HANDS",
                0.855f, 0.545f, 0.975f, 0.58f,
                new Color(0.15f, 0.42f, 0.4f), () =>
                {
                    aiHandsOpen = true;
                    Render();
                }, 10);
            AddButton(contentRoot, "SELECT DECKS",
                0.855f, 0.505f, 0.975f, 0.54f,
                new Color(0.2f, 0.34f, 0.46f), () =>
                {
                    aiAutoplay = false;
                    engine = null;
                    aiHandsOpen = false;
                    Render();
                }, 9);
            AddButton(contentRoot, "RESET",
                0.855f, 0.465f, 0.975f, 0.5f,
                new Color(0.36f, 0.22f, 0.24f), StartAiMatch, 10);

            DrawReadOnlyZones(engine.Human.MonsterZones, HumanMonsterY);
            DrawSpellTrapRow(engine.Human.SpellTrapZones, HumanSpellTrapY);
            DrawFieldSpellSlot(engine.Human.FieldSpellZone, false);
            DrawPlayerPortrait("P1", 0.025f, 0.2f, 0.085f, 0.296f,
                new Color(0.12f, 0.36f, 0.4f), firstPlayerTurn);
            AddText(contentRoot, engine.Human.Name + "\n" + engine.Human.LifePoints +
                " LP   HAND " + engine.Human.Hand.Count, 0.095f, 0.2f, 0.195f, 0.296f,
                17, Color.white, TextAnchor.MiddleLeft, true);
            if (firstPlayerTurn)
                DrawCurrentTurnLabel(0.095f, 0.3f, 0.195f, 0.34f);
            DrawDeckButton(engine.Human, 0.205f, HumanSpellTrapY);
            DrawBanishedIndicator(engine.Human, 0.74f, HumanMonsterY);
            DrawGraveyardButton(engine.Human, 0.74f, HumanSpellTrapY);
            DrawAiVisibleHand(engine.Human, false);
            AddText(contentRoot, string.Join("\n", engine.Messages.Reverse().Take(3).Reverse()),
                0.025f, 0.14f, 0.975f, 0.175f, 10, mutedColor, TextAnchor.MiddleLeft);
        }

        private void DrawTestLabSetupScreen()
        {
            List<CardDefinition> candidates = StarterCatalog.All
                .Where(card => card != null && !string.IsNullOrWhiteSpace(card.CardId) &&
                    (showFailedUnitTests || IsTestCardPassing(card)) &&
                    MatchesTestCardFilters(card, TestLabFilter.None))
                .OrderBy(card => card.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            List<CardDefinition> importedCards = StarterCatalog.All
                .Take(StarterCatalog.ImportedCardCount)
                .Where(card => card != null)
                .ToList();
            int passingTestCount = importedCards.Count(IsTestCardPassing);
            int totalUnitTests = StarterCatalog.ImportedCardCount;
            int passingPercent = totalUnitTests == 0
                ? 0
                : Mathf.RoundToInt(passingTestCount * 100f / totalUnitTests);
            const int pageSize = 20;
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
            AddButton(contentRoot, (showFailedUnitTests ? "[x] " : "[ ] ") + "Show Failed Unit Tests",
                0.025f, 0.655f, 0.235f, 0.69f,
                showFailedUnitTests ? accentColor : new Color(0.16f, 0.24f, 0.33f),
                () =>
                {
                    showFailedUnitTests = !showFailedUnitTests;
                    testCardPage = 0;
                    testCardScrollPosition = 1f;
                    Render();
                }, 8);
            AddText(contentRoot, passingTestCount + " / " + totalUnitTests +
                " unit tests passed  •  " + passingPercent + "%",
                0.24f, 0.655f, 0.475f, 0.69f, 9, accentColor, TextAnchor.MiddleRight, true);

            RectTransform testListViewport = AddPanel(contentRoot, 0.025f, 0.17f, 0.475f, 0.65f, panelColor);
            testListViewport.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            WheelScrollRect testListScroll = testListViewport.gameObject.AddComponent<WheelScrollRect>();
            testListScroll.horizontal = false;
            testListScroll.vertical = true;
            testListScroll.movementType = ScrollRect.MovementType.Clamped;
            ConfigureMouseWheelScrolling(testListScroll);
            testListScroll.PageBoundaryScrolled = direction =>
            {
                int nextPage = testCardPage + direction;
                if (nextPage < 0 || nextPage >= pageCount)
                    return;
                testCardPage = nextPage;
                testCardScrollPosition = direction > 0 ? 1f : 0f;
                Render();
            };
            testListScroll.onValueChanged.AddListener(_ =>
                testCardScrollPosition = testListScroll.verticalNormalizedPosition);

            const float rowHeight = 42f;
            RectTransform testList = CreateRect("Test Card List", testListViewport);
            testList.anchorMin = new Vector2(0f, 1f);
            testList.anchorMax = new Vector2(1f, 1f);
            testList.pivot = new Vector2(0.5f, 1f);
            testList.sizeDelta = new Vector2(0f, page.Count * rowHeight);
            testListScroll.content = testList;
            testListScroll.viewport = testListViewport;
            for (int i = 0; i < page.Count; i++)
            {
                CardDefinition card = page[i];
                bool selected = selectedTestCard == card;
                AddScrollListButton(testList, card.DisplayName + "   •   " + CardTypeLabel(card) +
                    "   •   " + TestEffectStatusLabel(card), i, rowHeight,
                    selected ? Color.Lerp(CardFrameColor(card), accentColor, 0.42f) : CardFrameColor(card),
                    () =>
                    {
                        selectedTestCard = card;
                        Render();
                    }, 10, card);
            }
            testListScroll.verticalNormalizedPosition = testCardScrollPosition;
            if (page.Count == 0)
                AddText(contentRoot, "No cards match these filters.", 0.06f, 0.43f, 0.44f, 0.5f,
                    15, mutedColor, TextAnchor.MiddleCenter);

            AddButton(contentRoot, "<", 0.15f, 0.115f, 0.22f, 0.155f,
                new Color(0.2f, 0.34f, 0.46f), () =>
                {
                    testCardPage = Mathf.Max(0, testCardPage - 1);
                    testCardScrollPosition = 0f;
                    Render();
                }, 12);
            AddText(contentRoot, "PAGE " + (testCardPage + 1) + " / " + pageCount,
                0.225f, 0.115f, 0.355f, 0.155f, 11, mutedColor, TextAnchor.MiddleCenter);
            AddButton(contentRoot, ">", 0.36f, 0.115f, 0.43f, 0.155f,
                new Color(0.2f, 0.34f, 0.46f), () =>
                {
                    testCardPage = Mathf.Min(pageCount - 1, testCardPage + 1);
                    testCardScrollPosition = 1f;
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
            string setupHint = selectedTestCard != null &&
                selectedTestCard.Effect == CardEffect.BlackwingSpecialSummon
                ? "The focused card starts in your hand; another Blackwing is added to your field automatically."
                : "The focused card starts in your hand; the CPU opponent still takes normal turns.";
            AddText(contentRoot, setupHint,
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
            testCardScrollPosition = 1f;
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

        private static bool IsTestCardPassing(CardDefinition definition)
        {
            return definition.Effect != CardEffect.None || !RequiresEffectResolver(definition);
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
                testCardScrollPosition = 1f;
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
                " cards  •  revealed", 0.14f, 0.69f, 0.86f, 0.75f,
                15, Color.white, TextAnchor.MiddleLeft, true);
            DrawAiHandRow(engine.Opponent, false, 0.43f, 0.68f);

            AddText(contentRoot, engine.Human.Name + "  •  " + engine.Human.Hand.Count +
                " cards  •  revealed", 0.14f, 0.35f, 0.86f, 0.41f,
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
                CardInstance displayedCard = card != null && animatingCardArrivals.Contains(card) ? null : card;
                string label = displayedCard == null ? "MONSTER\nSLOT" : CardLabel(displayedCard, true);
                Color zoneColor = displayedCard == null
                    ? new Color(0.09f, 0.13f, 0.18f)
                    : CardZoneColor(displayedCard);
                RectTransform slot = AddButton(contentRoot, label, x, y, x + BoardCardWidth, y + BoardCardHeight,
                    zoneColor, () => { }, 10,
                    displayedCard != null && displayedCard.FaceUp ? displayedCard.Definition : null);
                AddCardArtPlaceholder(slot,
                    displayedCard != null && displayedCard.FaceUp ? displayedCard.Definition : null,
                    0f, 0f, 1f, 1f, displayedCard == null || displayedCard.FaceUp);
                RotateDefenseMonster(slot, displayedCard);
                FitMonsterSlotStats(slot, displayedCard);
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
            WheelScrollRect scroll = list.GetComponentInParent<WheelScrollRect>();
            if (scroll != null)
                AttachScrollWheelForwarder(button.gameObject, scroll);
            return button;
        }

        private static void AttachScrollWheelForwarder(GameObject target, WheelScrollRect scroll)
        {
            ScrollWheelForwarder forwarder = target.GetComponent<ScrollWheelForwarder>();
            if (forwarder == null)
                forwarder = target.AddComponent<ScrollWheelForwarder>();
            forwarder.ScrollTarget = scroll;
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

        private void AttachCardHover(
            RectTransform rect,
            CardDefinition definition,
            bool centerPreviewOnHover = false)
        {
            if (rect == null || definition == null)
                return;
            EventTrigger trigger = rect.gameObject.AddComponent<EventTrigger>();
            EventTrigger.Entry enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(data =>
            {
                if (screen != ScreenMode.Duel || centerPreviewOnHover)
                    ShowCardPreview(definition, data as PointerEventData, centerPreviewOnHover);
            });
            trigger.triggers.Add(enter);
            EventTrigger.Entry exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => HideCardPreview());
            trigger.triggers.Add(exit);

            RightClickAction rightClick = rect.gameObject.AddComponent<RightClickAction>();
            rightClick.OnRightClickWithEventData = eventData =>
            {
                if (screen == ScreenMode.Duel)
                    ShowCardPreview(definition, eventData);
            };
        }

        private void ShowCardPreview(
            CardDefinition definition,
            PointerEventData eventData,
            bool centerOnScreen = false)
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

                if (centerOnScreen)
                {
                    cardPreviewRoot.localPosition = new Vector3(-previewWidth * 0.5f, previewHeight * 0.5f, 0);
                }
                else if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
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
            CardDefinition hoverCard = null,
            bool centerPreviewOnHover = false)
        {
            RectTransform rect = CreateRect("Button", parent);
            SetAnchors(rect, xMin, yMin, xMax, yMax);
            return ConfigureButton(rect, label, color, callback, fontSize, hoverCard, centerPreviewOnHover);
        }

        private RectTransform AddCenteredButton(
            RectTransform parent,
            string label,
            float xOffset,
            float size,
            Color color,
            Action callback,
            int fontSize)
        {
            RectTransform rect = CreateRect("Button", parent);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(xOffset, 0f);
            rect.sizeDelta = new Vector2(size, size);
            return ConfigureButton(rect, label, color, callback, fontSize, null, false);
        }

        private RectTransform ConfigureButton(
            RectTransform rect,
            string label,
            Color color,
            Action callback,
            int fontSize,
            CardDefinition hoverCard,
            bool centerPreviewOnHover)
        {
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

            AttachCardHover(rect, hoverCard, centerPreviewOnHover);
            return rect;
        }

        private static float GetPortraitHeight(float normalizedWidth)
        {
            return normalizedWidth * GetScreenAspectRatio() * 7f / 5f;
        }

        private static float GetPortraitWidth(float normalizedHeight)
        {
            return normalizedHeight * 5f / 7f / GetScreenAspectRatio();
        }

        private static float GetScreenAspectRatio()
        {
            return Screen.width > 0 && Screen.height > 0
                ? (float)Screen.width / Screen.height
                : 16f / 9f;
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
                observedEngine.TributeSummoned -= OnTributeSummoned;
                observedEngine.MonsterDestroyed -= OnMonsterDestroyed;
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
