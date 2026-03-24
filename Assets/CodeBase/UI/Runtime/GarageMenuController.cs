using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class GarageMenuController : MonoBehaviour
{
    private const string DefaultMultiplayerMapId = "city_default";
    private static readonly Color ScreenBackdrop = new Color(0.02f, 0.03f, 0.04f, 0.38f);
    private static readonly Color PanelColor = new Color(0.05f, 0.06f, 0.08f, 0.88f);
    private static readonly Color PanelSoft = new Color(0.08f, 0.09f, 0.12f, 0.84f);
    private static readonly Color PanelBorder = new Color(0.32f, 0.37f, 0.42f, 0.55f);
    private static readonly Color AccentHot = new Color(1.0f, 0.33f, 0.53f, 1.0f);
    private static readonly Color AccentWarm = new Color(1.0f, 0.73f, 0.20f, 1.0f);
    private static readonly Color AccentCool = new Color(0.25f, 0.86f, 0.92f, 1.0f);
    private static readonly Color TextPrimary = new Color(0.97f, 0.97f, 0.98f, 1.0f);
    private static readonly Color TextSecondary = new Color(0.74f, 0.79f, 0.84f, 1.0f);
    private static readonly Color TextMuted = new Color(0.52f, 0.58f, 0.64f, 1.0f);
    private static readonly Color CardIdle = new Color(0.11f, 0.12f, 0.15f, 0.96f);
    private static readonly Color CardSelected = new Color(0.94f, 0.60f, 0.15f, 1.0f);
    private static readonly Color CardBorderSelected = new Color(1.0f, 0.86f, 0.41f, 1.0f);

    [Header("References")]
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private PlayerCar previewCar;
    [SerializeField] private MainMenuCameraController garageCamera;
    [SerializeField] private bool blockVehicleInput = true;
    [SerializeField] private bool freezePreviewRigidbody = true;
    [SerializeField] private Behaviour[] disableInGarage;

    [Header("Cars")]
    [SerializeField] private List<CarLoadoutConfig> carLoadouts = new List<CarLoadoutConfig>();

    [Header("Scene")]
    [SerializeField] private string gameSceneName = "Game";
    [SerializeField] private bool debugOverlay;

    [Header("PurrNet Solo")]
    [SerializeField] private string purrNetSoloHost = "93.183.80.30";
    [SerializeField, Min(1024)] private int purrNetSoloPort = 5000;
    [SerializeField, Range(10, 120)] private int purrNetSoloTickRate = 30;

    private readonly List<string> bodySetOptionNames = new List<string>();
    private readonly List<CustomizationSelectorState> rawSelectorStates = new List<CustomizationSelectorState>();
    private readonly List<DetailCategoryState> detailCategories = new List<DetailCategoryState>();
    private readonly Dictionary<string, Texture2D> generatedIcons = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

    private VisualElement rootOverlay;
    private VisualElement headerBreadcrumbs;
    private VisualElement leftRailPanel;
    private VisualElement carRail;
    private VisualElement statRail;
    private VisualElement centerStage;
    private VisualElement rightDrawer;
    private VisualElement rightDrawerContent;
    private VisualElement footerBar;
    private Label footerHintLabel;
    private Label carNameLabel;
    private Label carSubtitleLabel;
    private Label heroTitleLabel;
    private Label heroSubtitleLabel;
    private Label sectionTitleLabel;
    private Label sectionSubtitleLabel;
    private Label statHorsepower;
    private Label statFinalDrive;
    private Label statRideHeight;
    private Label statPaint;
    private Label statBody;
    private Label statCategory;
    private Label debugStatusLabel;

    private int selectedCarIndex;
    private int selectedBodySetIndex;
    private int selectedEngineIndex;
    private int selectedSuspensionIndex;
    private int selectedPaintIndex;
    private int selectedCategoryIndex = -1;
    private Rigidbody previewRigidbody;
    private bool uiBuilt;
    private bool usingDeclarativeLayout;
    private string debugStatus;
    private string multiplayerStatus = "Multiplayer is idle.";
    private string lastLobbySyncedSelectionJson;
    private bool multiplayerBusy;
    private bool multiplayerSceneLaunchPending;
    private readonly List<BackendLobbySummary> backendLobbies = new List<BackendLobbySummary>();
    private GarageView currentView = GarageView.Home;

    private enum GarageView
    {
        Home,
        StylingCategories,
        StylingVariants,
        Performance,
        Paint,
        MultiplayerBrowser,
        MultiplayerLobby
    }

    private enum DetailCategoryKind
    {
        BodySet,
        CustomSelector
    }

    private CarLoadoutConfig SelectedLoadout
    {
        get
        {
            if (carLoadouts == null || carLoadouts.Count == 0)
                return null;

            selectedCarIndex = Mathf.Clamp(selectedCarIndex, 0, carLoadouts.Count - 1);
            return carLoadouts[selectedCarIndex];
        }
    }

    private DetailCategoryState SelectedCategory
    {
        get
        {
            if (detailCategories.Count == 0)
                return null;

            selectedCategoryIndex = Mathf.Clamp(selectedCategoryIndex, 0, detailCategories.Count - 1);
            return detailCategories[selectedCategoryIndex];
        }
    }

    private void Awake()
    {
        PurrNetSessionRuntime.Reset();

        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();
        if (previewCar == null)
            previewCar = FindFirstObjectByType<PlayerCar>();
        if (garageCamera == null)
            garageCamera = FindFirstObjectByType<MainMenuCameraController>();
        if (previewCar != null)
            previewRigidbody = previewCar.GetComponent<Rigidbody>();

        ApplyGarageMode();
        TryBuildMenuUi();
        SetCursorForMenu();
        Time.timeScale = 1.0f;
    }

    private void OnEnable()
    {
        SubscribeBackendEvents();
        TryBuildMenuUi();
    }

    private void OnDisable()
    {
        UnsubscribeBackendEvents();
    }

    private void TryBuildMenuUi()
    {
        if (uiBuilt)
            return;

        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();
        if (uiDocument == null)
        {
            debugStatus = "UIDocument missing";
            Debug.LogError("GarageMenuController: UIDocument not found.", this);
            return;
        }

        if (uiDocument.rootVisualElement == null)
        {
            debugStatus = "rootVisualElement missing";
            Debug.LogError("GarageMenuController: rootVisualElement is null.", this);
            return;
        }

        BuildMenuUi();
        uiBuilt = true;
        debugStatus = "UI built";
        RebuildStateData(true);
        RefreshVisibleView();
    }

    private void BuildMenuUi()
    {
        VisualElement root = uiDocument.rootVisualElement;
        if (TryBuildDeclarativeLayout(root))
            return;

        usingDeclarativeLayout = false;
        root.Clear();
        root.style.flexGrow = 1.0f;
        root.style.backgroundColor = ScreenBackdrop;
        root.style.paddingLeft = 22;
        root.style.paddingRight = 22;
        root.style.paddingTop = 22;
        root.style.paddingBottom = 16;

        rootOverlay = new VisualElement();
        rootOverlay.style.flexGrow = 1.0f;
        rootOverlay.style.flexDirection = FlexDirection.Column;
        root.Add(rootOverlay);

        rootOverlay.Add(BuildHeader());

        VisualElement content = new VisualElement();
        content.style.flexGrow = 1.0f;
        content.style.flexDirection = FlexDirection.Row;
        content.style.marginTop = 12;
        content.style.marginBottom = 12;
        rootOverlay.Add(content);

        leftRailPanel = BuildLeftRail();
        content.Add(leftRailPanel);

        centerStage = new VisualElement();
        centerStage.style.flexGrow = 1.0f;
        centerStage.style.marginLeft = 18;
        centerStage.style.marginRight = 0;
        centerStage.style.justifyContent = Justify.FlexEnd;
        content.Add(centerStage);

        rightDrawer = CreatePanel(0, 0);
        rightDrawer.style.flexShrink = 0;
        rightDrawer.style.marginTop = 12;
        rootOverlay.Add(rightDrawer);

        footerBar = BuildFooterBar();
        rootOverlay.Add(footerBar);
    }

    private bool TryBuildDeclarativeLayout(VisualElement root)
    {
        VisualTreeAsset layout = Resources.Load<VisualTreeAsset>("UI/Garage/GarageMenu");
        if (layout == null)
            return false;

        StyleSheet styles = Resources.Load<StyleSheet>("UI/Garage/GarageMenu");

        root.Clear();
        root.style.flexGrow = 1.0f;
        root.style.paddingLeft = 0;
        root.style.paddingRight = 0;
        root.style.paddingTop = 0;
        root.style.paddingBottom = 0;
        if (styles != null && !root.styleSheets.Contains(styles))
            root.styleSheets.Add(styles);

        layout.CloneTree(root);
        usingDeclarativeLayout = true;

        rootOverlay = root.Q<VisualElement>("garage-root");
        headerBreadcrumbs = root.Q<VisualElement>("header-breadcrumbs");
        leftRailPanel = root.Q<VisualElement>("left-rail");
        carRail = root.Q<ScrollView>("car-rail");
        statRail = root.Q<VisualElement>("stat-rail");
        centerStage = root.Q<VisualElement>("center-stage");
        rightDrawer = root.Q<VisualElement>("right-drawer");
        rightDrawerContent = root.Q<VisualElement>("right-drawer-content");
        footerBar = root.Q<VisualElement>("footer-bar");
        footerHintLabel = root.Q<Label>("footer-hint");
        carNameLabel = root.Q<Label>("car-name");
        carSubtitleLabel = root.Q<Label>("car-subtitle");
        heroTitleLabel = root.Q<Label>("hero-title");
        heroSubtitleLabel = root.Q<Label>("hero-subtitle");
        sectionTitleLabel = root.Q<Label>("section-title");
        sectionSubtitleLabel = root.Q<Label>("section-subtitle");
        statHorsepower = root.Q<Label>("stat-horsepower");
        statFinalDrive = root.Q<Label>("stat-final-drive");
        statRideHeight = root.Q<Label>("stat-ride-height");
        statPaint = root.Q<Label>("stat-paint");
        statBody = root.Q<Label>("stat-body");
        statCategory = root.Q<Label>("stat-category");
        debugStatusLabel = root.Q<Label>("debug-status");
        return rootOverlay != null &&
        headerBreadcrumbs != null &&
        leftRailPanel != null &&
        carRail != null &&
        centerStage != null &&
        rightDrawer != null &&
               footerBar != null;
    }

    private VisualElement BuildHeader()
    {
        VisualElement header = new VisualElement();
        header.style.height = 84;
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        header.style.justifyContent = Justify.SpaceBetween;
        header.style.backgroundColor = new Color(0.04f, 0.05f, 0.07f, 0.88f);
        header.style.borderTopWidth = 1;
        header.style.borderBottomWidth = 1;
        header.style.borderLeftWidth = 1;
        header.style.borderRightWidth = 1;
        header.style.borderTopColor = PanelBorder;
        header.style.borderBottomColor = PanelBorder;
        header.style.borderLeftColor = PanelBorder;
        header.style.borderRightColor = PanelBorder;
        header.style.borderTopLeftRadius = 16;
        header.style.borderTopRightRadius = 16;
        header.style.borderBottomLeftRadius = 16;
        header.style.borderBottomRightRadius = 16;
        header.style.paddingLeft = 22;
        header.style.paddingRight = 22;
        header.style.paddingTop = 14;
        header.style.paddingBottom = 14;

        VisualElement branding = new VisualElement();
        branding.style.flexDirection = FlexDirection.Column;
        branding.style.minWidth = 280;
        header.Add(branding);

        Label kicker = new Label("GARAGE");
        kicker.style.color = AccentHot;
        kicker.style.fontSize = 12;
        kicker.style.unityFontStyleAndWeight = FontStyle.Bold;
        branding.Add(kicker);

        Label title = new Label("RUSSIAN ROAD RAGE");
        title.style.color = TextPrimary;
        title.style.fontSize = 28;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        branding.Add(title);

        heroSubtitleLabel = new Label("Street setup, styling and launch flow.");
        heroSubtitleLabel.style.color = TextSecondary;
        heroSubtitleLabel.style.fontSize = 12;
        heroSubtitleLabel.style.marginTop = 2;
        branding.Add(heroSubtitleLabel);

        headerBreadcrumbs = new VisualElement();
        headerBreadcrumbs.style.flexDirection = FlexDirection.Row;
        headerBreadcrumbs.style.alignItems = Align.Center;
        headerBreadcrumbs.style.justifyContent = Justify.FlexEnd;
        headerBreadcrumbs.style.flexGrow = 1.0f;
        header.Add(headerBreadcrumbs);

        return header;
    }

    private VisualElement BuildLeftRail()
    {
        VisualElement rail = CreatePanel(380, 0);
        rail.style.flexDirection = FlexDirection.Column;

        VisualElement identity = new VisualElement();
        identity.style.marginBottom = 14;
        rail.Add(identity);

        carNameLabel = new Label("No Loadout");
        carNameLabel.style.fontSize = 30;
        carNameLabel.style.color = TextPrimary;
        carNameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        identity.Add(carNameLabel);

        carSubtitleLabel = new Label("Assign at least one car loadout to the garage.");
        carSubtitleLabel.style.color = TextSecondary;
        carSubtitleLabel.style.fontSize = 12;
        carSubtitleLabel.style.marginTop = 2;
        identity.Add(carSubtitleLabel);

        rail.Add(CreateDivider());

        Label carPickerLabel = CreateSectionTitle("MY RIDES");
        rail.Add(carPickerLabel);

        carRail = new ScrollView(ScrollViewMode.Vertical);
        carRail.style.flexGrow = 0;
        carRail.style.height = 226;
        carRail.style.backgroundColor = PanelSoft;
        carRail.style.borderTopLeftRadius = 12;
        carRail.style.borderTopRightRadius = 12;
        carRail.style.borderBottomLeftRadius = 12;
        carRail.style.borderBottomRightRadius = 12;
        carRail.style.paddingLeft = 6;
        carRail.style.paddingRight = 6;
        carRail.style.paddingTop = 6;
        carRail.style.paddingBottom = 6;
        rail.Add(carRail);

        rail.Add(CreateDivider());

        Label statsLabel = CreateSectionTitle("SNAPSHOT");
        rail.Add(statsLabel);

        statRail = new VisualElement();
        statRail.style.backgroundColor = PanelSoft;
        statRail.style.borderTopLeftRadius = 14;
        statRail.style.borderTopRightRadius = 14;
        statRail.style.borderBottomLeftRadius = 14;
        statRail.style.borderBottomRightRadius = 14;
        statRail.style.paddingLeft = 12;
        statRail.style.paddingRight = 12;
        statRail.style.paddingTop = 10;
        statRail.style.paddingBottom = 10;
        rail.Add(statRail);

        statRail.Add(CreateStatRow("Horsepower", out statHorsepower));
        statRail.Add(CreateStatRow("Final drive", out statFinalDrive));
        statRail.Add(CreateStatRow("Ride height", out statRideHeight));
        statRail.Add(CreateStatRow("Paint", out statPaint));
        statRail.Add(CreateStatRow("Body", out statBody));
        statRail.Add(CreateStatRow("Focus", out statCategory));

        debugStatusLabel = new Label();
        debugStatusLabel.style.marginTop = 12;
        debugStatusLabel.style.fontSize = 10;
        debugStatusLabel.style.color = TextMuted;
        rail.Add(debugStatusLabel);
        return rail;
    }

    private VisualElement BuildFooterBar()
    {
        VisualElement footer = new VisualElement();
        footer.style.height = 90;
        footer.style.flexDirection = FlexDirection.Row;
        footer.style.alignItems = Align.Stretch;
        footer.style.backgroundColor = new Color(0.03f, 0.04f, 0.06f, 0.88f);
        footer.style.borderTopLeftRadius = 16;
        footer.style.borderTopRightRadius = 16;
        footer.style.borderBottomLeftRadius = 16;
        footer.style.borderBottomRightRadius = 16;
        footer.style.borderTopWidth = 1;
        footer.style.borderBottomWidth = 1;
        footer.style.borderLeftWidth = 1;
        footer.style.borderRightWidth = 1;
        footer.style.borderTopColor = PanelBorder;
        footer.style.borderBottomColor = PanelBorder;
        footer.style.borderLeftColor = PanelBorder;
        footer.style.borderRightColor = PanelBorder;
        footer.style.paddingLeft = 12;
        footer.style.paddingRight = 12;
        footer.style.paddingTop = 12;
        footer.style.paddingBottom = 12;
        return footer;
    }

    private VisualElement CreatePanel(float width, float minHeight)
    {
        VisualElement panel = new VisualElement();
        if (width > 0)
            panel.style.width = width;
        if (minHeight > 0)
            panel.style.minHeight = minHeight;
        panel.style.backgroundColor = PanelColor;
        panel.style.borderTopLeftRadius = 18;
        panel.style.borderTopRightRadius = 18;
        panel.style.borderBottomLeftRadius = 18;
        panel.style.borderBottomRightRadius = 18;
        panel.style.borderTopWidth = 1;
        panel.style.borderBottomWidth = 1;
        panel.style.borderLeftWidth = 1;
        panel.style.borderRightWidth = 1;
        panel.style.borderTopColor = PanelBorder;
        panel.style.borderBottomColor = PanelBorder;
        panel.style.borderLeftColor = PanelBorder;
        panel.style.borderRightColor = PanelBorder;
        panel.style.paddingLeft = 14;
        panel.style.paddingRight = 14;
        panel.style.paddingTop = 14;
        panel.style.paddingBottom = 14;
        return panel;
    }

    private void RebuildStateData(bool resetLoadoutDefaults)
    {
        BuildBodySetOptionNames();
        if (resetLoadoutDefaults)
            ApplyDefaultSelectionForLoadout();

        BuildCustomizationState();
        BuildDetailCategories();
        PreviewSelection();
    }

    private void SubscribeBackendEvents()
    {
        BackendClient client = Backend.Client;
        client.LobbyChanged -= HandleBackendLobbyChanged;
        client.LobbyChanged += HandleBackendLobbyChanged;
        client.MatchInfoChanged -= HandleBackendMatchInfoChanged;
        client.MatchInfoChanged += HandleBackendMatchInfoChanged;
        client.RealtimeErrorReceived -= HandleBackendRealtimeError;
        client.RealtimeErrorReceived += HandleBackendRealtimeError;
        client.SessionChanged -= HandleBackendSessionChanged;
        client.SessionChanged += HandleBackendSessionChanged;
    }

    private void UnsubscribeBackendEvents()
    {
        BackendClient client = Backend.Client;
        client.LobbyChanged -= HandleBackendLobbyChanged;
        client.MatchInfoChanged -= HandleBackendMatchInfoChanged;
        client.RealtimeErrorReceived -= HandleBackendRealtimeError;
        client.SessionChanged -= HandleBackendSessionChanged;
    }

    private void HandleBackendSessionChanged(BackendSessionResponse session)
    {
        multiplayerStatus = session != null
            ? string.Format("Connected as {0}.", session.player_name)
            : "Multiplayer session is missing.";

        if (uiBuilt)
            RefreshVisibleView();
    }

    private void HandleBackendLobbyChanged(BackendLobbyDetails lobby)
    {
        if (lobby != null)
        {
            multiplayerStatus = string.Format(
                "{0}: {1}/{2} players, status {3}.",
                string.IsNullOrWhiteSpace(lobby.name) ? lobby.lobby_id : lobby.name,
                lobby.current_players,
                lobby.max_players,
                lobby.status);

            if (currentView == GarageView.MultiplayerBrowser)
                currentView = GarageView.MultiplayerLobby;
        }
        else if (currentView == GarageView.MultiplayerLobby)
        {
            multiplayerStatus = "Lobby closed.";
            currentView = GarageView.MultiplayerBrowser;
        }

        if (uiBuilt)
            RefreshVisibleView();
    }

    private void HandleBackendMatchInfoChanged(BackendMatchInfo matchInfo)
    {
        if (matchInfo == null)
            return;

        multiplayerStatus = string.Format("Match {0} is {1}.", matchInfo.match_id, matchInfo.status);

        if (!multiplayerSceneLaunchPending &&
            !string.IsNullOrWhiteSpace(matchInfo.match_id) &&
            (string.Equals(matchInfo.status, "starting", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(matchInfo.status, "running", StringComparison.OrdinalIgnoreCase)))
        {
            multiplayerSceneLaunchPending = true;
            StartMultiplayerGame();
            return;
        }

        if (uiBuilt)
            RefreshVisibleView();
    }

    private void HandleBackendRealtimeError(BackendRealtimeErrorMessage error)
    {
        multiplayerStatus = error != null && !string.IsNullOrWhiteSpace(error.message)
            ? error.message
            : "Realtime connection error.";

        if (uiBuilt)
            RefreshVisibleView();
    }

    private void BuildBodySetOptionNames()
    {
        bodySetOptionNames.Clear();

        CarLoadoutConfig loadout = SelectedLoadout;
        if (loadout == null)
            return;

        if (ShouldIncludeStockBodyOption(loadout))
            bodySetOptionNames.Add("Stock body");

        if (loadout.BodySets != null)
        {
            for (int i = 0; i < loadout.BodySets.Count; i++)
            {
                BodySetConfig bodySet = loadout.BodySets[i];
                bodySetOptionNames.Add(bodySet != null ? bodySet.DisplayName : "Missing body set");
            }
        }

        if (bodySetOptionNames.Count == 0)
            bodySetOptionNames.Add("Stock body");

        selectedBodySetIndex = Mathf.Clamp(selectedBodySetIndex, 0, bodySetOptionNames.Count - 1);
    }

    private void ApplyDefaultSelectionForLoadout()
    {
        CarLoadoutConfig loadout = SelectedLoadout;
        if (loadout == null)
        {
            selectedBodySetIndex = 0;
            selectedEngineIndex = 0;
            selectedSuspensionIndex = 0;
            selectedPaintIndex = 0;
            return;
        }

        if (ShouldIncludeStockBodyOption(loadout))
            selectedBodySetIndex = Mathf.Max(0, loadout.DefaultBodySetIndex + 1);
        else
            selectedBodySetIndex = Mathf.Max(0, loadout.DefaultBodySetIndex);

        selectedEngineIndex = Mathf.Max(0, loadout.DefaultEngineIndex);
        selectedSuspensionIndex = Mathf.Max(0, loadout.DefaultSuspensionIndex);
        selectedPaintIndex = Mathf.Max(0, loadout.DefaultPaintIndex);
    }

    private void BuildCustomizationState()
    {
        rawSelectorStates.Clear();

        CarLoadoutConfig loadout = SelectedLoadout;
        GameObject bodyPrefab = loadout != null && loadout.PlayerCarConfig != null && loadout.PlayerCarConfig.Visual != null
            ? loadout.PlayerCarConfig.Visual.bodyPrefab
            : null;

        List<CarCustomizationUtility.SelectorDefinition> definitions = CarCustomizationUtility.DiscoverSelectors(bodyPrefab);
        for (int i = 0; i < definitions.Count; i++)
        {
            CarCustomizationUtility.SelectorDefinition definition = definitions[i];
            if (definition == null || definition.VariantNames == null || definition.VariantNames.Count == 0)
                continue;

            CustomizationSelectorState state = new CustomizationSelectorState();
            state.SelectorPath = definition.SelectorPath;
            state.DisplayName = ResolveDisplayName(definition.SelectorPath);
            state.FamilyKey = ResolveFamilyKey(definition.SelectorPath);
            state.Options.AddRange(definition.VariantNames);
            state.SelectedIndex = 0;
            rawSelectorStates.Add(state);
        }
    }

    private void BuildDetailCategories()
    {
        detailCategories.Clear();

        DetailCategoryState bodySetCategory = new DetailCategoryState();
        bodySetCategory.Kind = DetailCategoryKind.BodySet;
        bodySetCategory.Id = "body-sets";
        bodySetCategory.DisplayName = "Body Kits";
        bodySetCategory.Subtitle = "Swap complete exterior packages.";
        bodySetCategory.FamilyKey = "body-kit";
        bodySetCategory.Icon = GetGeneratedIcon("body-kit");
        bodySetCategory.SelectedVariantIndex = Mathf.Clamp(selectedBodySetIndex, 0, Mathf.Max(0, bodySetOptionNames.Count - 1));
        bodySetCategory.VariantOptions.AddRange(bodySetOptionNames);
        detailCategories.Add(bodySetCategory);

        Dictionary<string, List<CustomizationSelectorState>> groupedSelectors = new Dictionary<string, List<CustomizationSelectorState>>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < rawSelectorStates.Count; i++)
        {
            CustomizationSelectorState selector = rawSelectorStates[i];
            List<CustomizationSelectorState> bucket;
            if (!groupedSelectors.TryGetValue(selector.FamilyKey, out bucket))
            {
                bucket = new List<CustomizationSelectorState>();
                groupedSelectors.Add(selector.FamilyKey, bucket);
            }

            bucket.Add(selector);
        }

        foreach (KeyValuePair<string, List<CustomizationSelectorState>> entry in groupedSelectors)
        {
            DetailCategoryState category = BuildGroupedCategory(entry.Key, entry.Value);
            if (category != null)
                detailCategories.Add(category);
        }

        selectedCategoryIndex = detailCategories.Count == 0
            ? -1
            : Mathf.Clamp(selectedCategoryIndex < 0 ? 0 : selectedCategoryIndex, 0, detailCategories.Count - 1);
    }

    private DetailCategoryState BuildGroupedCategory(string familyKey, List<CustomizationSelectorState> selectors)
    {
        if (selectors == null || selectors.Count == 0)
            return null;

        List<string> mergedVariants = BuildSharedVariantList(selectors);
        if (mergedVariants.Count == 0)
        {
            for (int i = 0; i < selectors.Count; i++)
            {
                DetailCategoryState single = BuildSingleSelectorCategory(selectors[i]);
                if (single != null)
                    detailCategories.Add(single);
            }

            return null;
        }

        DetailCategoryState category = new DetailCategoryState();
        category.Kind = DetailCategoryKind.CustomSelector;
        category.Id = familyKey;
        category.DisplayName = ResolveFamilyDisplayName(familyKey);
        category.Subtitle = selectors.Count > 1
            ? "Applies the same variant across mirrored or linked parts."
            : selectors[0].DisplayName;
        category.FamilyKey = familyKey;
        category.Icon = GetGeneratedIcon(familyKey);
        category.Selectors.AddRange(selectors);
        category.VariantOptions.AddRange(mergedVariants);
        category.SelectedVariantIndex = Mathf.Clamp(GetSharedSelectedIndex(selectors, mergedVariants), 0, Mathf.Max(0, mergedVariants.Count - 1));
        if (!ShouldShowStylingCategory(category))
            return null;
        return category;
    }

    private DetailCategoryState BuildSingleSelectorCategory(CustomizationSelectorState selector)
    {
        if (selector == null || selector.Options.Count == 0)
            return null;

        DetailCategoryState category = new DetailCategoryState();
        category.Kind = DetailCategoryKind.CustomSelector;
        category.Id = selector.SelectorPath;
        category.DisplayName = selector.DisplayName;
        category.Subtitle = "Single selector channel.";
        category.FamilyKey = selector.FamilyKey;
        category.Icon = GetGeneratedIcon(selector.FamilyKey);
        category.Selectors.Add(selector);
        category.VariantOptions.AddRange(selector.Options);
        category.SelectedVariantIndex = Mathf.Clamp(selector.SelectedIndex, 0, selector.Options.Count - 1);
        if (!ShouldShowStylingCategory(category))
            return null;
        return category;
    }

    private static bool ShouldShowStylingCategory(DetailCategoryState category)
    {
        if (category == null)
            return false;
        if (category.Kind == DetailCategoryKind.BodySet)
            return true;
        if (category.VariantOptions == null || category.VariantOptions.Count == 0)
            return false;
        if (category.VariantOptions.Count > 1)
            return true;

        return !IsStockLikeVariant(category.VariantOptions[0]);
    }

    private static bool IsStockLikeVariant(string variantName)
    {
        if (string.IsNullOrWhiteSpace(variantName))
            return true;

        string normalized = variantName.Replace(" ", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
        return normalized == "default" ||
               normalized == "stock" ||
               normalized == "seta";
    }

    private static List<string> BuildSharedVariantList(List<CustomizationSelectorState> selectors)
    {
        List<string> shared = new List<string>();
        if (selectors == null || selectors.Count == 0 || selectors[0].Options.Count == 0)
            return shared;

        for (int i = 0; i < selectors[0].Options.Count; i++)
            shared.Add(selectors[0].Options[i]);

        for (int selectorIndex = 1; selectorIndex < selectors.Count; selectorIndex++)
        {
            List<string> options = selectors[selectorIndex].Options;
            for (int optionIndex = shared.Count - 1; optionIndex >= 0; optionIndex--)
            {
                if (!ContainsOption(options, shared[optionIndex]))
                    shared.RemoveAt(optionIndex);
            }
        }

        return shared;
    }

    private static bool ContainsOption(List<string> options, string value)
    {
        for (int i = 0; i < options.Count; i++)
        {
            if (string.Equals(options[i], value, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static int GetSharedSelectedIndex(List<CustomizationSelectorState> selectors, List<string> sharedOptions)
    {
        if (selectors == null || selectors.Count == 0 || sharedOptions == null || sharedOptions.Count == 0)
            return 0;

        string selectedName = selectors[0].Options[Mathf.Clamp(selectors[0].SelectedIndex, 0, selectors[0].Options.Count - 1)];
        for (int i = 0; i < sharedOptions.Count; i++)
        {
            if (string.Equals(sharedOptions[i], selectedName, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return 0;
    }

    private void PreviewSelection()
    {
        CarLoadoutConfig loadout = SelectedLoadout;
        BodySetConfig bodySet = GetSelectedBodySet();
        EngineGearboxConfig engine = GetSelectedEngine();
        SuspensionConfig suspension = GetSelectedSuspension();
        Color? paint = GetSelectedPaint();
        List<CarCustomizationSelection> customizations = GetSelectedCustomizations();
        DetailCategoryState focusCategory = SelectedCategory;

        carNameLabel.text = loadout != null ? loadout.DisplayName : "No loadout";
        carSubtitleLabel.text = loadout != null
            ? string.Format("{0} body kits, {1} engine builds, {2} suspension presets.", loadout.BodySets.Count, loadout.EngineConfigs.Count, loadout.SuspensionConfigs.Count)
            : "Assign car loadouts to this menu controller.";

        if (heroTitleLabel != null)
            heroTitleLabel.text = ResolveHeroTitle();
        if (heroSubtitleLabel != null)
            heroSubtitleLabel.text = ResolveHeroSubtitle(loadout, focusCategory);

        if (sectionTitleLabel != null)
            sectionTitleLabel.text = ResolveDrawerTitle();
        if (sectionSubtitleLabel != null)
            sectionSubtitleLabel.text = ResolveDrawerSubtitle(focusCategory);

        if (statHorsepower != null)
            statHorsepower.text = engine != null ? string.Format("{0:0} hp", engine.horsepower) : "--";
        if (statFinalDrive != null)
            statFinalDrive.text = engine != null ? string.Format("{0:0.00}", engine.gearbox.finalDrive) : "--";
        if (statRideHeight != null)
            statRideHeight.text = suspension != null ? string.Format("{0:0.00}", suspension.visualWheelHeight) : "--";
        if (statPaint != null)
            statPaint.text = paint.HasValue ? GetSelectedPaintLabel(paint.Value) : "Default";
        if (statBody != null)
            statBody.text = bodySet != null ? bodySet.DisplayName : "Stock body";
        if (statCategory != null)
            statCategory.text = focusCategory != null ? focusCategory.DisplayName : "Home";
        if (debugStatusLabel != null)
            debugStatusLabel.text = debugOverlay ? debugStatus : string.Empty;

        if (previewCar != null)
        {
            previewCar.OverrideLoadout(
                loadout != null ? loadout.PlayerCarConfig : null,
                loadout != null ? loadout.HandlingConfig : null,
                bodySet,
                engine,
                suspension,
                customizations);

            if (paint.HasValue)
                previewCar.SetPaint(paint.Value);
        }

        CommitCurrentSelection();
        TrySyncLobbyCarConfig();
    }

    private void RefreshVisibleView()
    {
        if (!uiBuilt)
            return;

        RefreshLayoutVisibility();
        RefreshBreadcrumbs();
        RebuildCarCards();
        RebuildCenterStage();
        RebuildRightDrawer();
        RebuildFooter();
        PreviewSelection();
        RefreshCameraFocus();
    }

    private void RefreshLayoutVisibility()
    {
        bool showLoadoutRail = currentView == GarageView.Home || currentView == GarageView.MultiplayerBrowser || currentView == GarageView.MultiplayerLobby;

        if (leftRailPanel != null)
        {
            leftRailPanel.style.display = showLoadoutRail ? DisplayStyle.Flex : DisplayStyle.None;
            if (!usingDeclarativeLayout)
                leftRailPanel.style.width = showLoadoutRail ? 380 : 0;
        }

        if (centerStage != null)
        {
            centerStage.style.marginLeft = showLoadoutRail ? 18 : 0;
        }
    }

    private void RefreshBreadcrumbs()
    {
        if (headerBreadcrumbs == null)
            return;

        headerBreadcrumbs.Clear();

        AddCrumb("Garage", GarageView.Home);
        if (currentView == GarageView.StylingCategories || currentView == GarageView.StylingVariants)
            AddCrumb("Styling", GarageView.StylingCategories);
        if (currentView == GarageView.StylingVariants && SelectedCategory != null)
            AddCrumb(SelectedCategory.DisplayName, GarageView.StylingVariants);
        if (currentView == GarageView.Performance)
            AddCrumb("Performance", GarageView.Performance);
        if (currentView == GarageView.Paint)
            AddCrumb("Paint", GarageView.Paint);
        if (currentView == GarageView.MultiplayerBrowser || currentView == GarageView.MultiplayerLobby)
            AddCrumb("Multiplayer", GarageView.MultiplayerBrowser);
        if (currentView == GarageView.MultiplayerLobby)
            AddCrumb("Lobby", GarageView.MultiplayerLobby);
    }

    private void AddCrumb(string text, GarageView targetView)
    {
        Button crumb = new Button(delegate
        {
            if (targetView == GarageView.StylingVariants)
                return;
            SetView(targetView);
        });
        crumb.text = text;
        crumb.style.height = 30;
        crumb.style.marginLeft = 6;
        crumb.style.backgroundColor = targetView == currentView ? AccentHot : new Color(0.10f, 0.12f, 0.15f, 0.95f);
        crumb.style.color = targetView == currentView ? Color.black : TextPrimary;
        crumb.style.unityFontStyleAndWeight = FontStyle.Bold;
        crumb.style.borderTopLeftRadius = 16;
        crumb.style.borderTopRightRadius = 16;
        crumb.style.borderBottomLeftRadius = 16;
        crumb.style.borderBottomRightRadius = 16;
        crumb.style.paddingLeft = 14;
        crumb.style.paddingRight = 14;
        headerBreadcrumbs.Add(crumb);
    }

    private void RebuildCarCards()
    {
        if (carRail == null)
            return;

        carRail.Clear();

        if (carLoadouts == null || carLoadouts.Count == 0)
        {
            carRail.Add(CreatePlaceholder("No car loadouts configured."));
            return;
        }

        for (int i = 0; i < carLoadouts.Count; i++)
        {
            CarLoadoutConfig loadout = carLoadouts[i];
            int carIndex = i;
            bool selected = i == selectedCarIndex;
            carRail.Add(CreateMenuCard(
                loadout != null ? loadout.DisplayName : "Missing loadout",
                loadout != null && loadout.Icon != null ? loadout.Icon.texture : null,
                loadout != null ? string.Format("{0} styling categories", CountPotentialCategories(loadout)) : "Broken asset reference",
                selected,
                delegate
                {
                    selectedCarIndex = carIndex;
                    selectedCategoryIndex = 0;
                    RebuildStateData(true);
                    RefreshVisibleView();
                }));
        }
    }

    private int CountPotentialCategories(CarLoadoutConfig loadout)
    {
        if (loadout == null || loadout.PlayerCarConfig == null || loadout.PlayerCarConfig.Visual == null)
            return 0;

        int count = 1 + (loadout.BodySets != null ? loadout.BodySets.Count : 0);
        List<CarCustomizationUtility.SelectorDefinition> selectors = CarCustomizationUtility.DiscoverSelectors(loadout.PlayerCarConfig.Visual.bodyPrefab);
        count += selectors.Count;
        return count;
    }

    private void RebuildCenterStage()
    {
        if (usingDeclarativeLayout)
            return;

        centerStage.Clear();

        VisualElement hero = CreatePanel(0, 0);
        hero.style.flexGrow = 1.0f;
        hero.style.justifyContent = Justify.SpaceBetween;
        hero.style.backgroundColor = new Color(0.04f, 0.05f, 0.07f, 0.42f);
        centerStage.Add(hero);

        VisualElement top = new VisualElement();
        top.style.flexDirection = FlexDirection.Column;
        top.style.marginTop = 8;
        hero.Add(top);

        heroTitleLabel = new Label();
        heroTitleLabel.style.fontSize = 42;
        heroTitleLabel.style.color = TextPrimary;
        heroTitleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        heroTitleLabel.style.marginBottom = 4;
        top.Add(heroTitleLabel);

        Label accentLine = new Label("HEAT GARAGE FLOW");
        accentLine.style.color = AccentWarm;
        accentLine.style.fontSize = 12;
        accentLine.style.unityFontStyleAndWeight = FontStyle.Bold;
        accentLine.style.marginBottom = 10;
        top.Add(accentLine);

        Label helper = new Label();
        helper.text = "Mouse drag rotates the preview car. Navigate from high-level sections into deeper part variants.";
        helper.style.color = TextSecondary;
        helper.style.fontSize = 13;
        helper.style.maxWidth = 520;
        top.Add(helper);

        VisualElement pulseStrip = new VisualElement();
        pulseStrip.style.flexDirection = FlexDirection.Row;
        pulseStrip.style.alignItems = Align.FlexEnd;
        pulseStrip.style.height = 74;
        pulseStrip.style.marginTop = 18;
        hero.Add(pulseStrip);

        pulseStrip.Add(CreatePulseBar(52, AccentHot));
        pulseStrip.Add(CreatePulseBar(34, AccentWarm));
        pulseStrip.Add(CreatePulseBar(64, AccentCool));
        pulseStrip.Add(CreatePulseBar(42, AccentHot));
        pulseStrip.Add(CreatePulseBar(58, AccentWarm));
    }

    private static VisualElement CreatePulseBar(float height, Color color)
    {
        VisualElement bar = new VisualElement();
        bar.style.width = 12;
        bar.style.height = height;
        bar.style.marginRight = 8;
        bar.style.backgroundColor = color;
        bar.style.borderTopLeftRadius = 4;
        bar.style.borderTopRightRadius = 4;
        return bar;
    }

    private void RebuildRightDrawer()
    {
        if (usingDeclarativeLayout && rightDrawerContent != null)
        {
            rightDrawerContent.Clear();
        }
        else
        {
            rightDrawer.Clear();

            VisualElement titleBlock = new VisualElement();
            titleBlock.style.marginBottom = 12;
            rightDrawer.Add(titleBlock);

            sectionTitleLabel = new Label();
            sectionTitleLabel.style.fontSize = 28;
            sectionTitleLabel.style.color = TextPrimary;
            sectionTitleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleBlock.Add(sectionTitleLabel);

            sectionSubtitleLabel = new Label();
            sectionSubtitleLabel.style.fontSize = 12;
            sectionSubtitleLabel.style.color = TextSecondary;
            sectionSubtitleLabel.style.marginTop = 4;
            titleBlock.Add(sectionSubtitleLabel);

            rightDrawer.Add(CreateDivider());
            rightDrawerContent = new VisualElement();
            rightDrawerContent.style.flexGrow = 1.0f;
            rightDrawer.Add(rightDrawerContent);
        }

        switch (currentView)
        {
            case GarageView.Home:
                BuildHomeDrawer();
                break;
            case GarageView.StylingCategories:
                BuildStylingCategoriesDrawer();
                break;
            case GarageView.StylingVariants:
                BuildStylingVariantsDrawer();
                break;
            case GarageView.Performance:
                BuildPerformanceDrawer();
                break;
            case GarageView.Paint:
                BuildPaintDrawer();
                break;
            case GarageView.MultiplayerBrowser:
                BuildMultiplayerBrowserDrawer();
                break;
            case GarageView.MultiplayerLobby:
                BuildMultiplayerLobbyDrawer();
                break;
        }
    }

    private void BuildHomeDrawer()
    {
        ScrollView view = CreateDrawerScroll(true);
        rightDrawerContent.Add(view);

        view.Add(CreateActionTile("Single Player", "start", "Lock loadout and enter the city immediately.", false, StartSinglePlayerGame));
        view.Add(CreateActionTile("Multiplayer", "performance", "Create or join a lobby while keeping this garage flow.", false, OpenMultiplayerBrowser));
        view.Add(CreateActionTile("Styling", "styling", "Dive into body kits, bumpers, lights and aero.", false, delegate { SetView(GarageView.StylingCategories); }));
        view.Add(CreateActionTile("Performance", "performance", "Choose engine and suspension presets.", false, delegate { SetView(GarageView.Performance); }));
        view.Add(CreateActionTile("Paint", "paint", "Swap color presets and apply preview instantly.", false, delegate { SetView(GarageView.Paint); }));
    }

    private void BuildStylingCategoriesDrawer()
    {
        ScrollView view = CreateDrawerScroll(true);
        rightDrawerContent.Add(view);

        if (detailCategories.Count == 0)
        {
            view.Add(CreatePlaceholder("No styling categories detected on this body prefab."));
            return;
        }

        for (int i = 0; i < detailCategories.Count; i++)
        {
            DetailCategoryState category = detailCategories[i];
            int categoryIndex = i;
            bool selected = i == selectedCategoryIndex;
            string subtitle = string.Format("{0} variants", category.VariantOptions.Count);
            view.Add(CreateMenuCard(
                category.DisplayName,
                category.Icon,
                subtitle,
                selected,
                delegate
                {
                    selectedCategoryIndex = categoryIndex;
                    SetView(GarageView.StylingVariants);
                }));
        }
    }

    private void BuildStylingVariantsDrawer()
    {
        DetailCategoryState category = SelectedCategory;
        ScrollView view = CreateDrawerScroll(true);
        rightDrawerContent.Add(view);

        if (category == null)
        {
            view.Add(CreatePlaceholder("Select a styling category first."));
            return;
        }

        Button backButton = new Button(delegate { SetView(GarageView.StylingCategories); });
        backButton.text = "Back to Categories";
        backButton.style.height = 92;
        backButton.style.minWidth = 180;
        backButton.style.marginRight = 10;
        backButton.style.backgroundColor = new Color(0.11f, 0.13f, 0.16f, 0.96f);
        backButton.style.color = TextPrimary;
        backButton.style.unityFontStyleAndWeight = FontStyle.Bold;
        backButton.style.borderTopLeftRadius = 12;
        backButton.style.borderTopRightRadius = 12;
        backButton.style.borderBottomLeftRadius = 12;
        backButton.style.borderBottomRightRadius = 12;
        view.Add(backButton);

        for (int i = 0; i < category.VariantOptions.Count; i++)
        {
            int variantIndex = i;
            bool selected = i == category.SelectedVariantIndex;
            string variantName = BeautifyVariantName(category.VariantOptions[i]);
            view.Add(CreateMenuCard(
                variantName,
                category.Icon,
                category.Kind == DetailCategoryKind.BodySet ? "Complete body package" : "Part variant",
                selected,
                delegate
                {
                    ApplyVariantSelection(category, variantIndex);
                    RefreshVisibleView();
                }));
        }
    }

    private void BuildPerformanceDrawer()
    {
        ScrollView view = CreateDrawerScroll(true);
        rightDrawerContent.Add(view);

        CarLoadoutConfig loadout = SelectedLoadout;
        if (loadout == null)
        {
            view.Add(CreatePlaceholder("No active car loadout."));
            return;
        }

        VisualElement enginesGroup = CreateDrawerGroup("ENGINES");
        view.Add(enginesGroup);
        if (loadout.EngineConfigs == null || loadout.EngineConfigs.Count == 0)
        {
            enginesGroup.Add(CreatePlaceholder("No engine presets configured."));
        }
        else
        {
            for (int i = 0; i < loadout.EngineConfigs.Count; i++)
            {
                EngineGearboxConfig engine = loadout.EngineConfigs[i];
                int engineIndex = i;
                bool selected = i == selectedEngineIndex;
                string text = engine != null
                    ? string.Format("{0:0} hp | FD {1:0.00}", engine.horsepower, engine.gearbox.finalDrive)
                    : "Missing engine config";

                enginesGroup.Add(CreateMenuCard(
                    engine != null ? engine.name : "Missing",
                    GetGeneratedIcon("performance"),
                    text,
                    selected,
                    delegate
                    {
                        selectedEngineIndex = engineIndex;
                        PreviewSelection();
                        RebuildFooter();
                    }));
            }
        }

        VisualElement suspensionGroup = CreateDrawerGroup("SUSPENSION");
        view.Add(suspensionGroup);
        if (loadout.SuspensionConfigs == null || loadout.SuspensionConfigs.Count == 0)
        {
            suspensionGroup.Add(CreatePlaceholder("No suspension presets configured."));
        }
        else
        {
            for (int i = 0; i < loadout.SuspensionConfigs.Count; i++)
            {
                SuspensionConfig suspension = loadout.SuspensionConfigs[i];
                int suspensionIndex = i;
                bool selected = i == selectedSuspensionIndex;
                string text = suspension != null
                    ? string.Format("Freq {0:0.00} | Height {1:0.00}", suspension.suspensionFrequency, suspension.visualWheelHeight)
                    : "Missing suspension config";

                suspensionGroup.Add(CreateMenuCard(
                    suspension != null ? suspension.name : "Missing",
                    GetGeneratedIcon("suspension"),
                    text,
                    selected,
                    delegate
                    {
                        selectedSuspensionIndex = suspensionIndex;
                        PreviewSelection();
                        RebuildFooter();
                    }));
            }
        }
    }

    private void BuildPaintDrawer()
    {
        ScrollView view = CreateDrawerScroll(true);
        rightDrawerContent.Add(view);

        CarLoadoutConfig loadout = SelectedLoadout;
        if (loadout == null || loadout.PaintOptions == null || loadout.PaintOptions.Count == 0)
        {
            view.Add(CreatePlaceholder("No paint presets configured."));
            return;
        }

        for (int i = 0; i < loadout.PaintOptions.Count; i++)
        {
            PaintConfig paint = loadout.PaintOptions[i];
            int paintIndex = i;
            bool selected = i == selectedPaintIndex;
            VisualElement row = CreatePaintCard(paint, selected, delegate
            {
                selectedPaintIndex = paintIndex;
                PreviewSelection();
                RebuildFooter();
            });
            view.Add(row);
        }
    }

    private void BuildMultiplayerBrowserDrawer()
    {
        ScrollView view = CreateDrawerScroll(false);
        rightDrawerContent.Add(view);

        view.Add(CreateActionTile(
            "Refresh Lobbies",
            "performance",
            multiplayerBusy ? "Refreshing backend state..." : multiplayerStatus,
            false,
            RefreshMultiplayerBrowser));

        view.Add(CreateActionTile(
            "Create Duel Lobby",
            "start",
            "Create a 2-player waiting room on city_default with the current car config.",
            false,
            delegate { CreateLobby(2); }));

        view.Add(CreateActionTile(
            "Create Crew Lobby",
            "styling",
            "Create a 4-player lobby for a larger test run.",
            false,
            delegate { CreateLobby(4); }));

        if (backendLobbies.Count == 0)
        {
            view.Add(CreatePlaceholder("No public lobbies found. Create one from this garage and share the slot."));
            return;
        }

        for (int i = 0; i < backendLobbies.Count; i++)
        {
            BackendLobbySummary lobby = backendLobbies[i];
            bool joinable = lobby != null &&
                            string.Equals(lobby.status, "waiting", StringComparison.OrdinalIgnoreCase) &&
                            lobby.current_players < lobby.max_players;
            string subtitle = lobby == null
                ? "Broken lobby response."
                : string.Format(
                    "{0} | {1}/{2} players | map {3}",
                    joinable ? "Joinable" : "Unavailable",
                    lobby.current_players,
                    lobby.max_players,
                    string.IsNullOrWhiteSpace(lobby.map_id) ? DefaultMultiplayerMapId : lobby.map_id);

            view.Add(CreateMenuCard(
                lobby != null ? lobby.name : "Missing lobby",
                GetGeneratedIcon(joinable ? "start" : "default"),
                subtitle,
                false,
                joinable
                    ? (Action)delegate { JoinLobby(lobby.lobby_id); }
                    : null));
        }
    }

    private void BuildMultiplayerLobbyDrawer()
    {
        ScrollView view = CreateDrawerScroll(false);
        rightDrawerContent.Add(view);

        BackendLobbyDetails lobby = Backend.Client.CurrentLobby;
        if (lobby == null)
        {
            view.Add(CreatePlaceholder("No active lobby. Return to browser and create or join one."));
            return;
        }

        view.Add(CreateActionTile(
            "Leave Lobby",
            "rear-bumper",
            "Drop out of the waiting room and go back to browser.",
            false,
            LeaveCurrentLobby));

        view.Add(CreateActionTile(
            "Refresh Room",
            "performance",
            multiplayerBusy ? "Refreshing lobby state..." : multiplayerStatus,
            false,
            RefreshCurrentLobby));

        if (CanStartSoloLobby(lobby))
        {
            view.Add(CreateActionTile(
                "Start Solo",
                "start",
                multiplayerBusy
                    ? "Connecting to the PurrNet dedicated server..."
                    : "Connect directly to the dedicated PurrNet server and test the new prediction path in the Game scene.",
                false,
                StartSoloLobby));
        }

        if (lobby.players == null || lobby.players.Count == 0)
        {
            view.Add(CreatePlaceholder("Lobby has no registered players yet."));
            return;
        }

        for (int i = 0; i < lobby.players.Count; i++)
        {
            BackendLobbyPlayer player = lobby.players[i];
            bool isSelf = Backend.Client.Session != null &&
                          player != null &&
                          string.Equals(player.player_id, Backend.Client.Session.player_id, StringComparison.OrdinalIgnoreCase);
            string connectionLabel = player != null && player.is_server_controlled
                ? "server-controlled"
                : (string.IsNullOrWhiteSpace(player != null ? player.connection_state : null) ? "unknown" : player.connection_state);
            string subtitle = player == null
                ? "Missing player payload."
                : string.Format(
                    "{0} | {1}",
                    connectionLabel,
                    player.car_config != null
                        ? player.car_config.ResolveDisplayName()
                        : "car config pending");

            view.Add(CreateMenuCard(
                player != null ? player.player_name : "Unknown player",
                GetGeneratedIcon(player != null && player.is_server_controlled ? "start" : (isSelf ? "paint" : "default")),
                subtitle,
                isSelf,
                null));
        }
    }

    private void RebuildFooter()
    {
        footerBar.Clear();
        string hint = ResolveFooterHint();
        if (usingDeclarativeLayout && footerHintLabel != null)
        {
            footerHintLabel.text = hint;
            footerBar.Add(footerHintLabel);
            return;
        }

        Label label = new Label(hint);
        label.style.color = TextSecondary;
        label.style.fontSize = 12;
        label.style.whiteSpace = WhiteSpace.Normal;
        label.style.flexGrow = 1.0f;
        footerBar.Add(label);
    }

    private ScrollView CreateDrawerScroll(bool horizontal)
    {
        ScrollView scroll = new ScrollView(horizontal ? ScrollViewMode.Horizontal : ScrollViewMode.Vertical);
        scroll.style.flexGrow = 1.0f;
        if (horizontal)
        {
            scroll.style.paddingBottom = 2;
            scroll.contentContainer.style.flexDirection = FlexDirection.Row;
            scroll.contentContainer.style.alignItems = Align.Stretch;
        }
        else
        {
            scroll.style.paddingRight = 2;
        }
        return scroll;
    }

    private VisualElement CreateActionTile(string title, string iconKey, string subtitle, bool selected, Action onClick)
    {
        return CreateMenuCard(title, GetGeneratedIcon(iconKey), subtitle, selected, onClick);
    }

    private VisualElement CreateMenuCard(string title, Texture texture, string subtitle, bool selected, Action onClick)
    {
        VisualElement card = new VisualElement();
        ApplyCardStyle(card, selected);
        card.RegisterCallback<ClickEvent>(delegate { if (onClick != null) onClick.Invoke(); });

        VisualElement row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        card.Add(row);

        Image image = new Image();
        image.image = texture;
        image.scaleMode = ScaleMode.ScaleToFit;
        image.style.width = 42;
        image.style.height = 42;
        image.style.marginRight = 12;
        image.style.unityBackgroundImageTintColor = selected ? new Color(0.05f, 0.05f, 0.05f, 1.0f) : new Color(0.96f, 0.97f, 0.99f, 1.0f);
        row.Add(image);

        VisualElement textColumn = new VisualElement();
        textColumn.style.flexGrow = 1.0f;
        row.Add(textColumn);

        Label header = new Label(title);
        header.style.fontSize = 15;
        header.style.color = selected ? Color.black : TextPrimary;
        header.style.unityFontStyleAndWeight = FontStyle.Bold;
        textColumn.Add(header);

        Label detail = new Label(subtitle);
        detail.style.fontSize = 11;
        detail.style.marginTop = 3;
        detail.style.color = selected ? new Color(0.18f, 0.18f, 0.18f, 1.0f) : TextSecondary;
        detail.style.whiteSpace = WhiteSpace.Normal;
        textColumn.Add(detail);
        return card;
    }

    private VisualElement CreatePaintCard(PaintConfig paint, bool selected, Action onClick)
    {
        Color swatchColor = paint != null ? paint.Color : Color.white;
        float luminance = (swatchColor.r * 0.2126f) + (swatchColor.g * 0.7152f) + (swatchColor.b * 0.0722f);

        VisualElement card = new VisualElement();
        card.tooltip = paint != null && !string.IsNullOrWhiteSpace(paint.DisplayName) ? paint.DisplayName : "Paint";
        card.RegisterCallback<ClickEvent>(delegate { if (onClick != null) onClick.Invoke(); });
        card.style.width = 58;
        card.style.height = 58;
        card.style.minWidth = 58;
        card.style.maxWidth = 58;
        card.style.marginRight = 8;
        card.style.marginBottom = 8;
        card.style.paddingLeft = 4;
        card.style.paddingRight = 4;
        card.style.paddingTop = 4;
        card.style.paddingBottom = 4;
        card.style.justifyContent = Justify.Center;
        card.style.alignItems = Align.Center;
        card.style.backgroundColor = new Color(0.08f, 0.09f, 0.12f, 0.92f);
        card.style.borderTopLeftRadius = 12;
        card.style.borderTopRightRadius = 12;
        card.style.borderBottomLeftRadius = 12;
        card.style.borderBottomRightRadius = 12;
        card.style.borderTopWidth = selected ? 2 : 1;
        card.style.borderBottomWidth = selected ? 2 : 1;
        card.style.borderLeftWidth = selected ? 2 : 1;
        card.style.borderRightWidth = selected ? 2 : 1;
        card.style.borderTopColor = selected ? AccentWarm : PanelBorder;
        card.style.borderBottomColor = selected ? AccentWarm : PanelBorder;
        card.style.borderLeftColor = selected ? AccentWarm : PanelBorder;
        card.style.borderRightColor = selected ? AccentWarm : PanelBorder;

        VisualElement swatch = new VisualElement();
        swatch.style.width = 48;
        swatch.style.height = 48;
        swatch.style.backgroundColor = swatchColor;
        swatch.style.borderTopLeftRadius = 9;
        swatch.style.borderTopRightRadius = 9;
        swatch.style.borderBottomLeftRadius = 9;
        swatch.style.borderBottomRightRadius = 9;
        swatch.style.borderTopWidth = 1;
        swatch.style.borderBottomWidth = 1;
        swatch.style.borderLeftWidth = 1;
        swatch.style.borderRightWidth = 1;
        Color innerBorder = luminance > 0.7f
            ? new Color(0.10f, 0.11f, 0.14f, 0.45f)
            : new Color(1.0f, 1.0f, 1.0f, 0.16f);
        swatch.style.borderTopColor = innerBorder;
        swatch.style.borderBottomColor = innerBorder;
        swatch.style.borderLeftColor = innerBorder;
        swatch.style.borderRightColor = innerBorder;
        card.Add(swatch);

        if (selected)
        {
            VisualElement marker = new VisualElement();
            marker.style.position = Position.Absolute;
            marker.style.right = 4;
            marker.style.bottom = 4;
            marker.style.width = 12;
            marker.style.height = 12;
            marker.style.backgroundColor = AccentWarm;
            marker.style.borderTopLeftRadius = 6;
            marker.style.borderTopRightRadius = 6;
            marker.style.borderBottomLeftRadius = 6;
            marker.style.borderBottomRightRadius = 6;
            marker.style.borderTopWidth = 2;
            marker.style.borderBottomWidth = 2;
            marker.style.borderLeftWidth = 2;
            marker.style.borderRightWidth = 2;
            marker.style.borderTopColor = new Color(0.05f, 0.05f, 0.06f, 1.0f);
            marker.style.borderBottomColor = new Color(0.05f, 0.05f, 0.06f, 1.0f);
            marker.style.borderLeftColor = new Color(0.05f, 0.05f, 0.06f, 1.0f);
            marker.style.borderRightColor = new Color(0.05f, 0.05f, 0.06f, 1.0f);
            card.Add(marker);
        }

        return card;
    }

    private static void ApplyCardStyle(VisualElement card, bool selected)
    {
        card.style.marginRight = 10;
        card.style.marginBottom = 10;
        card.style.minWidth = 228;
        card.style.maxWidth = 228;
        card.style.paddingLeft = 12;
        card.style.paddingRight = 12;
        card.style.paddingTop = 12;
        card.style.paddingBottom = 12;
        card.style.backgroundColor = selected ? CardSelected : CardIdle;
        card.style.borderTopWidth = 2;
        card.style.borderBottomWidth = 2;
        card.style.borderLeftWidth = 2;
        card.style.borderRightWidth = 2;
        card.style.borderTopColor = selected ? CardBorderSelected : PanelBorder;
        card.style.borderBottomColor = selected ? CardBorderSelected : PanelBorder;
        card.style.borderLeftColor = selected ? CardBorderSelected : PanelBorder;
        card.style.borderRightColor = selected ? CardBorderSelected : PanelBorder;
        card.style.borderTopLeftRadius = 14;
        card.style.borderTopRightRadius = 14;
        card.style.borderBottomLeftRadius = 14;
        card.style.borderBottomRightRadius = 14;
    }

    private Label CreateSectionTitle(string text)
    {
        Label label = new Label(text);
        label.style.color = AccentWarm;
        label.style.fontSize = 12;
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        label.style.marginBottom = 8;
        return label;
    }

    private VisualElement CreateDrawerGroup(string text)
    {
        VisualElement group = new VisualElement();
        group.style.minWidth = 252;
        group.style.marginRight = 14;

        Label label = new Label(text);
        label.style.color = AccentCool;
        label.style.fontSize = 12;
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        label.style.marginTop = 4;
        label.style.marginBottom = 8;
        group.Add(label);
        return group;
    }

    private VisualElement CreatePlaceholder(string text)
    {
        VisualElement block = new VisualElement();
        block.style.backgroundColor = PanelSoft;
        block.style.borderTopLeftRadius = 14;
        block.style.borderTopRightRadius = 14;
        block.style.borderBottomLeftRadius = 14;
        block.style.borderBottomRightRadius = 14;
        block.style.paddingLeft = 14;
        block.style.paddingRight = 14;
        block.style.paddingTop = 14;
        block.style.paddingBottom = 14;

        Label label = new Label(text);
        label.style.color = TextSecondary;
        label.style.whiteSpace = WhiteSpace.Normal;
        block.Add(label);
        return block;
    }

    private VisualElement CreateStatRow(string title, out Label valueLabel)
    {
        VisualElement row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.justifyContent = Justify.SpaceBetween;
        row.style.marginBottom = 6;

        Label left = new Label(title);
        left.style.color = TextMuted;
        left.style.fontSize = 11;
        row.Add(left);

        Label right = new Label("--");
        right.style.color = TextPrimary;
        right.style.fontSize = 11;
        right.style.unityFontStyleAndWeight = FontStyle.Bold;
        row.Add(right);

        valueLabel = right;
        return row;
    }

    private static VisualElement CreateDivider()
    {
        VisualElement divider = new VisualElement();
        divider.style.height = 1;
        divider.style.marginTop = 10;
        divider.style.marginBottom = 10;
        divider.style.backgroundColor = PanelBorder;
        return divider;
    }

    private void SetView(GarageView view)
    {
        currentView = view;
        if (currentView == GarageView.StylingVariants && SelectedCategory == null)
            currentView = GarageView.StylingCategories;
        if (currentView == GarageView.MultiplayerLobby && Backend.Client.CurrentLobby == null)
            currentView = GarageView.MultiplayerBrowser;
        RefreshVisibleView();
    }

    private void RefreshCameraFocus()
    {
        if (garageCamera == null)
            return;

        if (currentView == GarageView.StylingVariants && SelectedCategory != null)
        {
            string selectedVariant = SelectedCategory.SelectedVariantIndex >= 0 &&
                                     SelectedCategory.SelectedVariantIndex < SelectedCategory.VariantOptions.Count
                ? SelectedCategory.VariantOptions[SelectedCategory.SelectedVariantIndex]
                : null;
            string[] selectorPaths = null;
            if (SelectedCategory.Selectors.Count > 0)
            {
                selectorPaths = new string[SelectedCategory.Selectors.Count];
                for (int i = 0; i < SelectedCategory.Selectors.Count; i++)
                    selectorPaths[i] = SelectedCategory.Selectors[i].SelectorPath;
            }

            garageCamera.FocusSelection(SelectedCategory.FamilyKey, selectedVariant, selectorPaths);
            return;
        }

        garageCamera.ResetFocus();
    }

    private void ApplyVariantSelection(DetailCategoryState category, int variantIndex)
    {
        if (category == null || category.VariantOptions.Count == 0)
            return;

        variantIndex = Mathf.Clamp(variantIndex, 0, category.VariantOptions.Count - 1);
        category.SelectedVariantIndex = variantIndex;

        if (category.Kind == DetailCategoryKind.BodySet)
        {
            selectedBodySetIndex = variantIndex;
            PreviewSelection();
            return;
        }

        string selectedVariant = category.VariantOptions[variantIndex];
        for (int i = 0; i < category.Selectors.Count; i++)
        {
            CustomizationSelectorState selector = category.Selectors[i];
            selector.SelectedIndex = FindOptionIndex(selector.Options, selectedVariant);
        }

        PreviewSelection();
    }

    private static int FindOptionIndex(List<string> options, string selectedVariant)
    {
        if (options == null || options.Count == 0)
            return 0;

        for (int i = 0; i < options.Count; i++)
        {
            if (string.Equals(options[i], selectedVariant, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return 0;
    }

    private List<CarCustomizationSelection> GetSelectedCustomizations()
    {
        List<CarCustomizationSelection> selections = new List<CarCustomizationSelection>();
        for (int i = 0; i < rawSelectorStates.Count; i++)
        {
            CustomizationSelectorState state = rawSelectorStates[i];
            if (state == null || state.Options.Count == 0)
                continue;

            state.SelectedIndex = Mathf.Clamp(state.SelectedIndex, 0, state.Options.Count - 1);
            selections.Add(new CarCustomizationSelection(state.SelectorPath, state.Options[state.SelectedIndex]));
        }

        return selections;
    }

    private EngineGearboxConfig GetSelectedEngine()
    {
        CarLoadoutConfig loadout = SelectedLoadout;
        if (loadout == null || loadout.EngineConfigs == null || loadout.EngineConfigs.Count == 0)
            return null;

        selectedEngineIndex = Mathf.Clamp(selectedEngineIndex, 0, loadout.EngineConfigs.Count - 1);
        return loadout.EngineConfigs[selectedEngineIndex];
    }

    private SuspensionConfig GetSelectedSuspension()
    {
        CarLoadoutConfig loadout = SelectedLoadout;
        if (loadout == null || loadout.SuspensionConfigs == null || loadout.SuspensionConfigs.Count == 0)
            return null;

        selectedSuspensionIndex = Mathf.Clamp(selectedSuspensionIndex, 0, loadout.SuspensionConfigs.Count - 1);
        return loadout.SuspensionConfigs[selectedSuspensionIndex];
    }

    private BodySetConfig GetSelectedBodySet()
    {
        CarLoadoutConfig loadout = SelectedLoadout;
        if (loadout == null)
            return null;
        if (bodySetOptionNames.Count == 0)
            return null;

        selectedBodySetIndex = Mathf.Clamp(selectedBodySetIndex, 0, bodySetOptionNames.Count - 1);
        return GetBodySetForOption(loadout, selectedBodySetIndex);
    }

    private Color? GetSelectedPaint()
    {
        PaintConfig option = GetSelectedPaintConfig();
        return option != null ? option.Color : (Color?)null;
    }

    private PaintConfig GetSelectedPaintConfig()
    {
        CarLoadoutConfig loadout = SelectedLoadout;
        if (loadout == null || loadout.PaintOptions == null || loadout.PaintOptions.Count == 0)
            return null;

        selectedPaintIndex = Mathf.Clamp(selectedPaintIndex, 0, loadout.PaintOptions.Count - 1);
        return loadout.PaintOptions[selectedPaintIndex];
    }

    private string GetSelectedPaintLabel(Color paint)
    {
        CarLoadoutConfig loadout = SelectedLoadout;
        if (loadout != null && loadout.PaintOptions != null && loadout.PaintOptions.Count > 0)
        {
            selectedPaintIndex = Mathf.Clamp(selectedPaintIndex, 0, loadout.PaintOptions.Count - 1);
            PaintConfig option = loadout.PaintOptions[selectedPaintIndex];
            if (option != null && !string.IsNullOrWhiteSpace(option.DisplayName))
                return option.DisplayName;
        }

        return string.Format("RGB {0:0.00}, {1:0.00}, {2:0.00}", paint.r, paint.g, paint.b);
    }

    private static bool ShouldIncludeStockBodyOption(CarLoadoutConfig loadout)
    {
        if (loadout == null)
            return true;
        return loadout.IncludeStockBodyOption || loadout.BodySets == null || loadout.BodySets.Count == 0;
    }

    private static BodySetConfig GetBodySetForOption(CarLoadoutConfig loadout, int optionIndex)
    {
        if (loadout == null || loadout.BodySets == null || loadout.BodySets.Count == 0)
            return null;

        if (ShouldIncludeStockBodyOption(loadout))
        {
            if (optionIndex <= 0)
                return null;

            int bodySetIndex = optionIndex - 1;
            return bodySetIndex >= 0 && bodySetIndex < loadout.BodySets.Count
                ? loadout.BodySets[bodySetIndex]
                : null;
        }

        return optionIndex >= 0 && optionIndex < loadout.BodySets.Count
            ? loadout.BodySets[optionIndex]
            : null;
    }

    private void OpenMultiplayerBrowser()
    {
        _ = OpenMultiplayerBrowserAsync();
    }

    private async Task OpenMultiplayerBrowserAsync()
    {
        currentView = GarageView.MultiplayerBrowser;
        multiplayerStatus = "Connecting to backend...";
        multiplayerBusy = true;
        RefreshVisibleView();

        try
        {
            await EnsureMultiplayerReadyAsync(true);
            if (Backend.Client.CurrentLobby != null)
                currentView = GarageView.MultiplayerLobby;
        }
        catch (Exception ex)
        {
            multiplayerStatus = ex.Message;
        }
        finally
        {
            multiplayerBusy = false;
            RefreshVisibleView();
        }
    }

    private void RefreshMultiplayerBrowser()
    {
        _ = RefreshMultiplayerBrowserAsync();
    }

    private async Task RefreshMultiplayerBrowserAsync()
    {
        multiplayerBusy = true;
        multiplayerStatus = "Refreshing lobbies...";
        RefreshVisibleView();

        try
        {
            await EnsureMultiplayerReadyAsync(true);
        }
        catch (Exception ex)
        {
            multiplayerStatus = ex.Message;
        }
        finally
        {
            multiplayerBusy = false;
            RefreshVisibleView();
        }
    }

    private void RefreshCurrentLobby()
    {
        _ = RefreshCurrentLobbyAsync();
    }

    private async Task RefreshCurrentLobbyAsync()
    {
        BackendLobbyDetails currentLobby = Backend.Client.CurrentLobby;
        if (currentLobby == null)
        {
            currentView = GarageView.MultiplayerBrowser;
            RefreshVisibleView();
            return;
        }

        multiplayerBusy = true;
        multiplayerStatus = "Refreshing lobby room...";
        RefreshVisibleView();

        try
        {
            await EnsureMultiplayerReadyAsync(false);
            await Backend.Client.GetLobbyAsync(currentLobby.lobby_id);
        }
        catch (Exception ex)
        {
            multiplayerStatus = ex.Message;
        }
        finally
        {
            multiplayerBusy = false;
            RefreshVisibleView();
        }
    }

    private void CreateLobby(int maxPlayers)
    {
        _ = CreateLobbyAsync(maxPlayers);
    }

    private async Task CreateLobbyAsync(int maxPlayers)
    {
        multiplayerBusy = true;
        multiplayerStatus = "Creating lobby...";
        RefreshVisibleView();

        try
        {
            await EnsureMultiplayerReadyAsync(false);
            PlayerCarSelectionPayload payload = CommitCurrentSelection();
            CarLoadoutConfig loadout = SelectedLoadout;
            string lobbyName = loadout != null && !string.IsNullOrWhiteSpace(loadout.DisplayName)
                ? loadout.DisplayName + " Lobby"
                : "Russian Road Rage Lobby";

            BackendCreateLobbyResponse response = await Backend.Client.CreateLobbyAsync(lobbyName, DefaultMultiplayerMapId, maxPlayers, payload);
            await Backend.Client.SubscribeLobbyAsync(response.lobby_id);
            currentView = GarageView.MultiplayerLobby;
            multiplayerStatus = "Lobby created. Waiting for players...";
        }
        catch (Exception ex)
        {
            multiplayerStatus = ex.Message;
        }
        finally
        {
            multiplayerBusy = false;
            RefreshVisibleView();
        }
    }

    private void JoinLobby(string lobbyId)
    {
        _ = JoinLobbyAsync(lobbyId);
    }

    private async Task JoinLobbyAsync(string lobbyId)
    {
        multiplayerBusy = true;
        multiplayerStatus = "Joining lobby...";
        RefreshVisibleView();

        try
        {
            await EnsureMultiplayerReadyAsync(false);
            PlayerCarSelectionPayload payload = CommitCurrentSelection();
            await Backend.Client.JoinLobbyAsync(lobbyId, payload);
            await Backend.Client.SubscribeLobbyAsync(lobbyId);
            currentView = GarageView.MultiplayerLobby;
            multiplayerStatus = "Joined lobby. Waiting for server start...";
        }
        catch (Exception ex)
        {
            multiplayerStatus = ex.Message;
        }
        finally
        {
            multiplayerBusy = false;
            RefreshVisibleView();
        }
    }

    private void StartSoloLobby()
    {
        _ = StartSoloLobbyAsync();
    }

    private async Task StartSoloLobbyAsync()
    {
        await StartPurrNetSoloLobbyAsync();
    }

    private async Task StartPurrNetSoloLobbyAsync()
    {
        multiplayerBusy = true;
        multiplayerStatus = "Connecting to dedicated PurrNet server...";
        RefreshVisibleView();

        try
        {
            CommitCurrentSelection();

            try
            {
                if (Backend.Client.IsRealtimeConnected)
                    await Backend.Client.DisconnectRealtimeAsync();
            }
            catch
            {
            }

            ushort port = (ushort)Mathf.Clamp(purrNetSoloPort, 1024, 65535);
            int tickRate = Mathf.Clamp(purrNetSoloTickRate, 10, 120);
            string host = string.IsNullOrWhiteSpace(purrNetSoloHost) ? "93.183.80.30" : purrNetSoloHost.Trim();
            host = await ResolvePurrNetHostAsync(host);
            PurrNetSessionRuntime.ConfigureClient(host, port, tickRate);
            multiplayerSceneLaunchPending = true;
            SceneManager.LoadScene(gameSceneName);
        }
        catch (Exception ex)
        {
            PurrNetSessionRuntime.Reset();
            multiplayerStatus = ex.Message;
            multiplayerBusy = false;
            RefreshVisibleView();
        }
    }

    private static async Task<string> ResolvePurrNetHostAsync(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return "93.183.80.30";

        if (IPAddress.TryParse(host, out _))
            return host;

        try
        {
            IPAddress[] addresses = await Dns.GetHostAddressesAsync(host);
            for (int i = 0; i < addresses.Length; i++)
            {
                if (addresses[i] != null && addresses[i].AddressFamily == AddressFamily.InterNetwork)
                    return addresses[i].ToString();
            }

            for (int i = 0; i < addresses.Length; i++)
            {
                if (addresses[i] != null)
                    return addresses[i].ToString();
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"GarageMenuController: failed to resolve PurrNet host '{host}'. {ex.Message}");
        }

        return host;
    }

    private void LeaveCurrentLobby()
    {
        _ = LeaveCurrentLobbyAsync();
    }

    private async Task LeaveCurrentLobbyAsync()
    {
        BackendLobbyDetails lobby = Backend.Client.CurrentLobby;
        if (lobby == null)
        {
            currentView = GarageView.MultiplayerBrowser;
            RefreshVisibleView();
            return;
        }

        multiplayerBusy = true;
        multiplayerStatus = "Leaving lobby...";
        RefreshVisibleView();

        try
        {
            await Backend.Client.UnsubscribeLobbyAsync(lobby.lobby_id);
            await Backend.Client.LeaveLobbyAsync(lobby.lobby_id);
            currentView = GarageView.MultiplayerBrowser;
            multiplayerStatus = "Left lobby.";
            await RefreshBackendLobbiesAsync();
        }
        catch (Exception ex)
        {
            multiplayerStatus = ex.Message;
        }
        finally
        {
            multiplayerBusy = false;
            RefreshVisibleView();
        }
    }

    private async Task EnsureMultiplayerReadyAsync(bool refreshLobbies)
    {
        if (Backend.Client.Session == null)
        {
            string playerName = BuildGuestPlayerName();
            await Backend.Client.CreateGuestSessionAsync(playerName);
        }

        if (!Backend.Client.IsRealtimeConnected)
            await Backend.Client.ConnectRealtimeAsync();

        if (refreshLobbies)
            await RefreshBackendLobbiesAsync();
    }

    private async Task RefreshBackendLobbiesAsync()
    {
        BackendLobbiesResponse response = await Backend.Client.GetLobbiesAsync();
        backendLobbies.Clear();
        if (response != null && response.items != null)
        {
            backendLobbies.AddRange(response.items);
            backendLobbies.Sort(CompareLobbySummary);
        }

        multiplayerStatus = backendLobbies.Count > 0
            ? string.Format("Found {0} lobbies on the backend.", backendLobbies.Count)
            : "No public lobbies found.";
    }

    private bool CanStartSoloLobby(BackendLobbyDetails lobby)
    {
        if (lobby == null ||
            multiplayerBusy ||
            !string.Equals(lobby.status, "waiting", StringComparison.OrdinalIgnoreCase) ||
            Backend.Client.Session == null ||
            !string.Equals(lobby.owner_player_id, Backend.Client.Session.player_id, StringComparison.OrdinalIgnoreCase) ||
            lobby.players == null)
        {
            return false;
        }

        int humanPlayerCount = 0;
        for (int i = 0; i < lobby.players.Count; i++)
        {
            BackendLobbyPlayer player = lobby.players[i];
            if (player == null)
                continue;
            if (player.is_server_controlled)
                return false;
            humanPlayerCount++;
        }

        return humanPlayerCount == 1;
    }

    private static int CompareLobbySummary(BackendLobbySummary left, BackendLobbySummary right)
    {
        int leftRank = GetLobbyStatusRank(left != null ? left.status : null);
        int rightRank = GetLobbyStatusRank(right != null ? right.status : null);
        int rankCompare = leftRank.CompareTo(rightRank);
        if (rankCompare != 0)
            return rankCompare;

        string leftName = left != null ? left.name : string.Empty;
        string rightName = right != null ? right.name : string.Empty;
        return string.Compare(leftName, rightName, StringComparison.OrdinalIgnoreCase);
    }

    private static int GetLobbyStatusRank(string status)
    {
        if (string.Equals(status, "waiting", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (string.Equals(status, "starting", StringComparison.OrdinalIgnoreCase))
            return 1;
        if (string.Equals(status, "in_game", StringComparison.OrdinalIgnoreCase))
            return 2;
        if (string.Equals(status, "closed", StringComparison.OrdinalIgnoreCase))
            return 3;
        return 4;
    }

    private void TrySyncLobbyCarConfig()
    {
        if (Backend.Client.CurrentLobby == null || string.IsNullOrWhiteSpace(PlayerCarSelection.SelectionJson))
            return;

        if (string.Equals(lastLobbySyncedSelectionJson, PlayerCarSelection.SelectionJson, StringComparison.Ordinal))
            return;

        BackendLobbyDetails lobby = Backend.Client.CurrentLobby;
        if (!string.Equals(lobby.status, "waiting", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(lobby.status, "starting", StringComparison.OrdinalIgnoreCase))
            return;

        lastLobbySyncedSelectionJson = PlayerCarSelection.SelectionJson;
        _ = SyncLobbyCarConfigAsync(lobby.lobby_id);
    }

    private async Task SyncLobbyCarConfigAsync(string lobbyId)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(lobbyId))
                await Backend.Client.UpdateCarConfigAsync(lobbyId, CommitCurrentSelection());
        }
        catch (Exception ex)
        {
            multiplayerStatus = ex.Message;
            if (uiBuilt)
                RefreshVisibleView();
        }
    }

    private string BuildGuestPlayerName()
    {
        CarLoadoutConfig loadout = SelectedLoadout;
        string prefix = loadout != null && !string.IsNullOrWhiteSpace(loadout.DisplayName)
            ? loadout.DisplayName.Replace(" ", string.Empty)
            : "Guest";
        return string.Format("{0}_{1}", prefix, UnityEngine.Random.Range(1000, 9999));
    }

    private PlayerCarSelectionPayload CommitCurrentSelection()
    {
        CarLoadoutConfig loadout = SelectedLoadout;
        BodySetConfig bodySet = GetSelectedBodySet();
        EngineGearboxConfig engine = GetSelectedEngine();
        SuspensionConfig suspension = GetSelectedSuspension();
        PaintConfig paintConfig = GetSelectedPaintConfig();
        Color? paint = paintConfig != null ? paintConfig.Color : (Color?)null;
        List<CarCustomizationSelection> customizations = GetSelectedCustomizations();

        PlayerCarSelection.Set(
            loadout,
            loadout != null ? loadout.PlayerCarConfig : null,
            loadout != null ? loadout.HandlingConfig : null,
            bodySet,
            selectedBodySetIndex,
            engine,
            selectedEngineIndex,
            suspension,
            selectedSuspensionIndex,
            paintConfig,
            selectedPaintIndex,
            paint ?? Color.white,
            paint.HasValue,
            customizations);

        PlayerCarSelection.TryGetPayload(out PlayerCarSelectionPayload payload);
        return payload;
    }

    private void StartSinglePlayerGame()
    {
        PurrNetSessionRuntime.Reset();
        CommitCurrentSelection();
        SceneManager.LoadScene(gameSceneName);
    }

    private void StartMultiplayerGame()
    {
        PurrNetSessionRuntime.Reset();
        CommitCurrentSelection();
        SceneManager.LoadScene(gameSceneName);
    }

    private string ResolveHeroTitle()
    {
        switch (currentView)
        {
            case GarageView.StylingCategories:
                return "Choose a Styling Zone";
            case GarageView.StylingVariants:
                return SelectedCategory != null ? SelectedCategory.DisplayName : "Styling Variants";
            case GarageView.Performance:
                return "Tune the Build";
            case GarageView.Paint:
                return "Paint Shop";
            case GarageView.MultiplayerBrowser:
                return "Find A Lobby";
            case GarageView.MultiplayerLobby:
                return "Lobby Room";
            default:
                return "Night Garage";
        }
    }

    private string ResolveHeroSubtitle(CarLoadoutConfig loadout, DetailCategoryState category)
    {
        switch (currentView)
        {
            case GarageView.StylingCategories:
                return "Select a detail family and drill down into all available variants.";
            case GarageView.StylingVariants:
                return category != null
                    ? string.Format("{0} offers {1} visible variants on the active car.", category.DisplayName, category.VariantOptions.Count)
                    : "Choose a styling category.";
            case GarageView.Performance:
                return "Engine and suspension presets update the preview immediately.";
            case GarageView.Paint:
                return "Cycle through curated paint presets for the current ride.";
            case GarageView.MultiplayerBrowser:
                return "Create a room or join an existing lobby without leaving the garage.";
            case GarageView.MultiplayerLobby:
                return Backend.Client.CurrentLobby != null
                    ? string.Format(
                        "{0} is {1} with {2}/{3} players.",
                        Backend.Client.CurrentLobby.name,
                        Backend.Client.CurrentLobby.status,
                        Backend.Client.CurrentLobby.current_players,
                        Backend.Client.CurrentLobby.max_players)
                    : "Waiting for lobby snapshot.";
            default:
                return loadout != null
                    ? "Branch into styling, performance or paint before launching the run."
                    : "Assign a car loadout to start building the garage flow.";
        }
    }

    private string ResolveDrawerTitle()
    {
        switch (currentView)
        {
            case GarageView.StylingCategories:
                return "Styling";
            case GarageView.StylingVariants:
                return SelectedCategory != null ? SelectedCategory.DisplayName : "Variants";
            case GarageView.Performance:
                return "Performance";
            case GarageView.Paint:
                return "Paint";
            case GarageView.MultiplayerBrowser:
                return "Multiplayer";
            case GarageView.MultiplayerLobby:
                return Backend.Client.CurrentLobby != null ? Backend.Client.CurrentLobby.name : "Lobby";
            default:
                return "Main Menu";
        }
    }

    private string ResolveDrawerSubtitle(DetailCategoryState category)
    {
        switch (currentView)
        {
            case GarageView.StylingCategories:
                return "Main menu -> Styling -> Category -> Variant.";
            case GarageView.StylingVariants:
                return category != null ? category.Subtitle : "Select a category first.";
            case GarageView.Performance:
                return "Use presets instead of granular sliders for a cleaner game flow.";
            case GarageView.Paint:
                return "Paint options remain reversible and preview-only until launch.";
            case GarageView.MultiplayerBrowser:
                return "Create a lobby, refresh the public list, or join any waiting room.";
            case GarageView.MultiplayerLobby:
                return "The server starts the match automatically once the room is full and connected.";
            default:
                return "A NFS: Heat-inspired garage hub built on the current loadout system.";
        }
    }

    private string ResolveFooterHint()
    {
        switch (currentView)
        {
            case GarageView.StylingCategories:
                return "Choose a category, then drill into variants. LMB rotates the car.";
            case GarageView.StylingVariants:
                return "Camera focus follows the current category. Go back to restore the full-car view.";
            case GarageView.Performance:
                return "Performance uses quick presets; styling focus is disabled here.";
            case GarageView.Paint:
                return "Paint stays in full-car view so you can judge the whole body surface.";
            case GarageView.MultiplayerBrowser:
                return "Single Player still launches instantly from Home. Multiplayer uses the backend lobby flow.";
            case GarageView.MultiplayerLobby:
                return "Any car change you make here is pushed back to the lobby payload automatically.";
            default:
                return "Select a section on the right. LMB rotates the preview car.";
        }
    }

    private static string BeautifyVariantName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "Default";

        string result = raw.Replace('_', ' ').Trim();
        if (string.Equals(result, "Default", StringComparison.OrdinalIgnoreCase))
            return "Default / Stock";

        return result;
    }

    private static string ResolveDisplayName(string selectorPath)
    {
        string familyKey = ResolveFamilyKey(selectorPath);
        return ResolveFamilyDisplayName(familyKey);
    }

    private static string ResolveFamilyKey(string selectorPath)
    {
        string key = string.IsNullOrWhiteSpace(selectorPath) ? string.Empty : selectorPath.Replace(" ", string.Empty).ToLowerInvariant();
        if (key.Contains("bumperf"))
            return "front-bumper";
        if (key.Contains("bumperr"))
            return "rear-bumper";
        if (key.Contains("splitter"))
            return "splitter";
        if (key.Contains("skirts"))
            return "skirts";
        if (key.Contains("spoiler"))
            return "spoiler";
        if (key.Contains("hood"))
            return "hood";
        if (key.Contains("mirror"))
            return "mirrors";
        if (key.Contains("grille"))
            return "grille";
        if (key.Contains("headlight"))
            return "headlights";
        if (key.Contains("taillight"))
            return "taillights";
        if (key.Contains("diffuser"))
            return "diffuser";
        if (key.Contains("exhaust"))
            return "exhaust";
        if (key.Contains("fenderfl") || key.Contains("fenderfr") || key.Contains("fenderschassisf"))
            return "front-fenders";
        if (key.Contains("fendersr") || key.Contains("fenderschassisr"))
            return "rear-fenders";
        if (key.Contains("body"))
            return "body-kit";
        if (key.Contains("wheel"))
            return "wheels";
        return key;
    }

    private static string ResolveFamilyDisplayName(string familyKey)
    {
        switch (familyKey)
        {
            case "front-bumper":
                return "Front Bumper";
            case "rear-bumper":
                return "Rear Bumper";
            case "splitter":
                return "Splitter";
            case "skirts":
                return "Side Skirts";
            case "spoiler":
                return "Spoiler";
            case "hood":
                return "Hood";
            case "mirrors":
                return "Mirrors";
            case "grille":
                return "Grille";
            case "headlights":
                return "Headlights";
            case "taillights":
                return "Taillights";
            case "diffuser":
                return "Diffuser";
            case "exhaust":
                return "Exhaust";
            case "front-fenders":
                return "Front Fenders";
            case "rear-fenders":
                return "Rear Fenders";
            case "body-kit":
                return "Body Kits";
            case "wheels":
                return "Wheels";
            case "performance":
                return "Performance";
            case "paint":
                return "Paint";
            case "styling":
                return "Styling";
            case "suspension":
                return "Suspension";
            case "start":
                return "Start Ride";
            default:
                return string.IsNullOrWhiteSpace(familyKey) ? "Parts" : familyKey.Replace('-', ' ');
        }
    }

    private Texture2D GetGeneratedIcon(string iconKey)
    {
        if (string.IsNullOrWhiteSpace(iconKey))
            iconKey = "default";

        Texture2D resourceIcon = Resources.Load<Texture2D>(string.Format("Art/UI/{0}", iconKey));
        if (resourceIcon != null)
            return resourceIcon;

        Texture2D texture;
        if (generatedIcons.TryGetValue(iconKey, out texture) && texture != null)
            return texture;

        texture = BuildIconTexture(iconKey);
        generatedIcons[iconKey] = texture;
        return texture;
    }

    private Texture2D BuildIconTexture(string iconKey)
    {
        const int size = 64;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Point;

        Color transparent = new Color(0f, 0f, 0f, 0f);
        Color line = Color.white;
        Color accent = AccentWarm;

        Color[] pixels = new Color[size * size];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = transparent;
        texture.SetPixels(pixels);

        DrawRect(texture, 6, 6, 52, 52, new Color(1f, 1f, 1f, 0.06f));
        DrawRectOutline(texture, 6, 6, 52, 52, new Color(1f, 1f, 1f, 0.18f));

        switch (iconKey)
        {
            case "body-kit":
                DrawLine(texture, 14, 37, 24, 27, line);
                DrawLine(texture, 24, 27, 42, 27, line);
                DrawLine(texture, 42, 27, 50, 35, line);
                DrawLine(texture, 16, 39, 48, 39, accent);
                DrawCircle(texture, 22, 42, 5, line);
                DrawCircle(texture, 42, 42, 5, line);
                break;
            case "front-bumper":
                DrawLine(texture, 14, 26, 20, 22, line);
                DrawLine(texture, 20, 22, 44, 22, line);
                DrawLine(texture, 44, 22, 50, 26, line);
                DrawLine(texture, 14, 26, 18, 40, accent);
                DrawLine(texture, 50, 26, 46, 40, accent);
                DrawLine(texture, 18, 40, 46, 40, line);
                break;
            case "rear-bumper":
                DrawLine(texture, 14, 22, 50, 22, line);
                DrawLine(texture, 18, 22, 14, 38, accent);
                DrawLine(texture, 46, 22, 50, 38, accent);
                DrawLine(texture, 14, 38, 50, 38, line);
                DrawRect(texture, 24, 29, 16, 5, line);
                break;
            case "splitter":
                DrawLine(texture, 14, 34, 50, 34, line);
                DrawLine(texture, 18, 28, 14, 40, accent);
                DrawLine(texture, 46, 28, 50, 40, accent);
                DrawLine(texture, 22, 34, 32, 22, line);
                DrawLine(texture, 32, 22, 42, 34, line);
                break;
            case "skirts":
                DrawRect(texture, 14, 33, 36, 7, line);
                DrawLine(texture, 14, 33, 8, 42, accent);
                DrawLine(texture, 50, 33, 56, 42, accent);
                break;
            case "spoiler":
                DrawLine(texture, 12, 20, 52, 20, accent);
                DrawLine(texture, 20, 22, 18, 42, line);
                DrawLine(texture, 44, 22, 46, 42, line);
                DrawLine(texture, 16, 42, 48, 42, line);
                break;
            case "hood":
                DrawLine(texture, 18, 20, 46, 20, line);
                DrawLine(texture, 18, 20, 12, 46, accent);
                DrawLine(texture, 46, 20, 52, 46, accent);
                DrawLine(texture, 12, 46, 52, 46, line);
                DrawLine(texture, 32, 20, 32, 46, line);
                break;
            case "mirrors":
                DrawRectOutline(texture, 12, 20, 14, 18, line);
                DrawRectOutline(texture, 38, 20, 14, 18, line);
                DrawLine(texture, 26, 29, 18, 38, accent);
                DrawLine(texture, 38, 29, 46, 38, accent);
                break;
            case "grille":
                DrawRectOutline(texture, 16, 18, 32, 28, line);
                DrawLine(texture, 24, 18, 24, 46, accent);
                DrawLine(texture, 32, 18, 32, 46, accent);
                DrawLine(texture, 40, 18, 40, 46, accent);
                DrawLine(texture, 16, 26, 48, 26, line);
                DrawLine(texture, 16, 34, 48, 34, line);
                break;
            case "headlights":
                DrawCircle(texture, 20, 32, 8, line);
                DrawCircle(texture, 44, 32, 8, line);
                DrawRect(texture, 18, 30, 4, 4, accent);
                DrawRect(texture, 42, 30, 4, 4, accent);
                break;
            case "taillights":
                DrawRectOutline(texture, 12, 22, 16, 20, line);
                DrawRectOutline(texture, 36, 22, 16, 20, line);
                DrawRect(texture, 16, 26, 8, 12, accent);
                DrawRect(texture, 40, 26, 8, 12, accent);
                break;
            case "diffuser":
                DrawLine(texture, 14, 20, 50, 20, line);
                DrawLine(texture, 18, 20, 14, 44, accent);
                DrawLine(texture, 46, 20, 50, 44, accent);
                DrawLine(texture, 22, 24, 22, 44, line);
                DrawLine(texture, 32, 24, 32, 48, line);
                DrawLine(texture, 42, 24, 42, 44, line);
                break;
            case "exhaust":
                DrawCircle(texture, 22, 36, 8, line);
                DrawCircle(texture, 42, 36, 8, line);
                DrawRect(texture, 18, 18, 28, 6, accent);
                break;
            case "front-fenders":
            case "rear-fenders":
                DrawLine(texture, 12, 40, 20, 24, line);
                DrawLine(texture, 20, 24, 44, 24, line);
                DrawLine(texture, 44, 24, 52, 40, line);
                DrawCircle(texture, 32, 41, 9, accent);
                break;
            case "wheels":
            case "performance":
                DrawCircle(texture, 32, 32, 17, line);
                DrawCircle(texture, 32, 32, 7, accent);
                DrawLine(texture, 32, 15, 32, 49, line);
                DrawLine(texture, 15, 32, 49, 32, line);
                DrawLine(texture, 20, 20, 44, 44, accent);
                DrawLine(texture, 20, 44, 44, 20, accent);
                break;
            case "suspension":
                DrawLine(texture, 22, 14, 22, 50, line);
                DrawLine(texture, 42, 14, 42, 50, line);
                for (int i = 0; i < 4; i++)
                {
                    DrawLine(texture, 22, 18 + (i * 8), 42, 24 + (i * 8), accent);
                    DrawLine(texture, 42, 24 + (i * 8), 22, 30 + (i * 8), accent);
                }
                break;
            case "paint":
                DrawCircle(texture, 24, 28, 10, line);
                DrawCircle(texture, 40, 24, 9, accent);
                DrawCircle(texture, 42, 40, 8, line);
                DrawCircle(texture, 24, 42, 7, accent);
                break;
            case "styling":
                DrawLine(texture, 16, 42, 32, 18, line);
                DrawLine(texture, 32, 18, 48, 42, line);
                DrawLine(texture, 16, 42, 48, 42, accent);
                DrawLine(texture, 24, 32, 40, 32, accent);
                break;
            case "start":
                DrawLine(texture, 20, 16, 48, 32, line);
                DrawLine(texture, 20, 48, 48, 32, line);
                DrawLine(texture, 20, 16, 20, 48, accent);
                break;
            default:
                DrawLine(texture, 16, 16, 48, 48, line);
                DrawLine(texture, 16, 48, 48, 16, accent);
                break;
        }

        texture.Apply();
        return texture;
    }

    private static void DrawRect(Texture2D texture, int x, int y, int width, int height, Color color)
    {
        for (int px = x; px < x + width; px++)
        {
            for (int py = y; py < y + height; py++)
                SetPixelSafe(texture, px, py, color);
        }
    }

    private static void DrawRectOutline(Texture2D texture, int x, int y, int width, int height, Color color)
    {
        DrawLine(texture, x, y, x + width, y, color);
        DrawLine(texture, x, y + height, x + width, y + height, color);
        DrawLine(texture, x, y, x, y + height, color);
        DrawLine(texture, x + width, y, x + width, y + height, color);
    }

    private static void DrawCircle(Texture2D texture, int centerX, int centerY, int radius, Color color)
    {
        int radiusSquared = radius * radius;
        int innerRadiusSquared = Mathf.Max(0, (radius - 2) * (radius - 2));
        for (int x = -radius; x <= radius; x++)
        {
            for (int y = -radius; y <= radius; y++)
            {
                int distanceSquared = x * x + y * y;
                if (distanceSquared <= radiusSquared && distanceSquared >= innerRadiusSquared)
                    SetPixelSafe(texture, centerX + x, centerY + y, color);
            }
        }
    }

    private static void DrawLine(Texture2D texture, int x0, int y0, int x1, int y1, Color color)
    {
        int dx = Mathf.Abs(x1 - x0);
        int dy = Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;

        while (true)
        {
            SetPixelSafe(texture, x0, y0, color);
            SetPixelSafe(texture, x0 + 1, y0, color);
            SetPixelSafe(texture, x0, y0 + 1, color);

            if (x0 == x1 && y0 == y1)
                break;

            int e2 = err * 2;
            if (e2 > -dy)
            {
                err -= dy;
                x0 += sx;
            }

            if (e2 < dx)
            {
                err += dx;
                y0 += sy;
            }
        }
    }

    private static void SetPixelSafe(Texture2D texture, int x, int y, Color color)
    {
        if (texture == null)
            return;
        if (x < 0 || y < 0 || x >= texture.width || y >= texture.height)
            return;
        texture.SetPixel(x, y, color);
    }

    private static void SetCursorForMenu()
    {
        UnityEngine.Cursor.lockState = CursorLockMode.None;
        UnityEngine.Cursor.visible = true;
    }

    private void ApplyGarageMode()
    {
        if (blockVehicleInput)
            DisableGameplayComponentsInGarage();

        if (!freezePreviewRigidbody || previewRigidbody == null)
            return;

        previewRigidbody.isKinematic = false;
        previewRigidbody.linearVelocity = Vector3.zero;
        previewRigidbody.angularVelocity = Vector3.zero;
        previewRigidbody.Sleep();
    }

    private void DisableGameplayComponentsInGarage()
    {
        if (disableInGarage != null && disableInGarage.Length > 0)
        {
            for (int i = 0; i < disableInGarage.Length; i++)
            {
                if (disableInGarage[i] != null)
                    disableInGarage[i].enabled = false;
            }

            return;
        }

        CarControllerBase[] cars = FindObjectsByType<CarControllerBase>(FindObjectsSortMode.None);
        for (int i = 0; i < cars.Length; i++)
            cars[i].SetInputEnabled(false);

        CarGunShooter[] shooters = FindObjectsByType<CarGunShooter>(FindObjectsSortMode.None);
        for (int i = 0; i < shooters.Length; i++)
            shooters[i].enabled = false;

        FollowCarCamera[] followCameras = FindObjectsByType<FollowCarCamera>(FindObjectsSortMode.None);
        for (int i = 0; i < followCameras.Length; i++)
            followCameras[i].enabled = false;
    }

    private void OnGUI()
    {
        if (!debugOverlay)
            return;

        string rootState = uiDocument != null && uiDocument.rootVisualElement != null ? "root ok" : "root missing";
        GUI.Label(new Rect(12, 12, 800, 24), string.Format("GarageMenuController: {0} | loadouts {1} | {2}", debugStatus, carLoadouts != null ? carLoadouts.Count.ToString() : "null", rootState));
    }

    private sealed class CustomizationSelectorState
    {
        public string SelectorPath;
        public string DisplayName;
        public string FamilyKey;
        public int SelectedIndex;
        public readonly List<string> Options = new List<string>();
    }

    private sealed class DetailCategoryState
    {
        public string Id;
        public string DisplayName;
        public string Subtitle;
        public string FamilyKey;
        public Texture2D Icon;
        public DetailCategoryKind Kind;
        public int SelectedVariantIndex;
        public readonly List<string> VariantOptions = new List<string>();
        public readonly List<CustomizationSelectorState> Selectors = new List<CustomizationSelectorState>();
    }
}
