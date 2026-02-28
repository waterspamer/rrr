using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class GarageMenuController : MonoBehaviour
{
    private static readonly Color Backdrop = new Color(0.0f, 0.0f, 0.0f, 0.0f);
    private static readonly Color PanelDark = new Color(0.07f, 0.08f, 0.09f, 0.95f);
    private static readonly Color PanelLight = new Color(0.1f, 0.11f, 0.12f, 0.95f);
    private static readonly Color PanelLine = new Color(0.22f, 0.24f, 0.26f, 1f);
    private static readonly Color Accent = new Color(0.9f, 0.58f, 0.18f, 1f);
    private static readonly Color AccentStrong = new Color(0.95f, 0.2f, 0.18f, 1f);
    private static readonly Color AccentSoft = new Color(0.4f, 0.22f, 0.08f, 0.35f);
    private static readonly Color TextPrimary = new Color(0.96f, 0.93f, 0.9f, 1f);
    private static readonly Color TextSecondary = new Color(0.76f, 0.8f, 0.84f, 1f);
    private static readonly Color TextMuted = new Color(0.58f, 0.62f, 0.66f, 1f);
    private static readonly Color FieldBackground = new Color(0.14f, 0.15f, 0.16f, 1f);

    [Header("References")]
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private PlayerCar previewCar;
    [SerializeField] private bool blockVehicleInput = true;
    [SerializeField] private bool freezePreviewRigidbody = true;
    [SerializeField] private Behaviour[] disableInGarage;

    [Header("Cars")]
    [SerializeField] private List<CarLoadoutConfig> carLoadouts = new List<CarLoadoutConfig>();

    [Header("Scene")]
    [SerializeField] private string gameSceneName = "Game";
    [SerializeField] private bool debugOverlay = false;

    private readonly List<string> carOptionNames = new List<string>();
    private readonly List<string> engineOptionNames = new List<string>();
    private readonly List<string> suspensionOptionNames = new List<string>();

    private VisualElement carGrid;
    private VisualElement engineGrid;
    private VisualElement suspensionGrid;
    private Label carDescription;
    private Label engineDescription;
    private Label suspensionDescription;
    private Label paintDescription;
    private VisualElement paintSwatchRow;
    private readonly List<VisualElement> paintSwatches = new List<VisualElement>();
    private readonly List<VisualElement> carCards = new List<VisualElement>();
    private readonly List<VisualElement> engineCards = new List<VisualElement>();
    private readonly List<VisualElement> suspensionCards = new List<VisualElement>();
    private Label statHorsepower;
    private Label statFinalDrive;
    private Label statRideHeight;
    private CardStyle cardStyle;
    private CardStyle cardStyleSelected;

    private int selectedCarIndex;
    private int selectedEngineIndex;
    private int selectedSuspensionIndex;
    private int selectedPaintIndex;
    private Rigidbody previewRigidbody;
    private bool uiBuilt;
    private string debugStatus;

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

    private void Awake()
    {
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();
        if (previewCar == null)
            previewCar = FindFirstObjectByType<PlayerCar>();
        if (previewCar != null)
            previewRigidbody = previewCar.GetComponent<Rigidbody>();

        ApplyGarageMode();
        TryBuildMenuUi();
        SetCursorForMenu();
        Time.timeScale = 1.0f;
    }

    private void OnEnable()
    {
        TryBuildMenuUi();
    }

    private void TryBuildMenuUi()
    {
        if (uiBuilt)
            return;
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();
        if (uiDocument == null)
        {
            Debug.LogError("GarageMenuController: UIDocument not found.", this);
            debugStatus = "UIDocument not found";
            return;
        }
        if (uiDocument.rootVisualElement == null)
        {
            Debug.LogError("GarageMenuController: UIDocument rootVisualElement is null.", this);
            debugStatus = "rootVisualElement is null";
            return;
        }
        BuildMenuUi();
        uiBuilt = true;
        debugStatus = "UI built";
    }

    private void BuildMenuUi()
    {
        VisualElement root = uiDocument.rootVisualElement;
        root.Clear();
        root.style.flexGrow = 1.0f;
        root.style.justifyContent = Justify.FlexStart;
        root.style.alignItems = Align.Stretch;
        root.style.paddingRight = 18;
        root.style.paddingTop = 18;
        root.style.paddingBottom = 18;
        root.style.paddingLeft = 18;

        var overlay = new VisualElement();
        overlay.style.flexGrow = 1.0f;
        overlay.style.flexDirection = FlexDirection.Row;
        overlay.style.justifyContent = Justify.SpaceBetween;
        overlay.style.alignItems = Align.Stretch;
        overlay.style.backgroundColor = Backdrop;
        overlay.style.paddingLeft = 12;
        overlay.style.paddingRight = 12;
        overlay.style.paddingTop = 10;
        overlay.style.paddingBottom = 10;
        overlay.style.borderTopLeftRadius = 10;
        overlay.style.borderTopRightRadius = 10;
        overlay.style.borderBottomLeftRadius = 10;
        overlay.style.borderBottomRightRadius = 10;
        root.Add(overlay);

        var leftPanel = CreatePanel(380, PanelDark);
        overlay.Add(leftPanel);

        var rightPanel = CreatePanel(360, PanelLight);
        overlay.Add(rightPanel);

        BuildLeftPanel(leftPanel);
        BuildRightPanel(rightPanel);

        RefreshOptions();
    }

    private VisualElement CreatePanel(float width, Color background)
    {
        var panel = new VisualElement();
        panel.style.width = width;
        panel.style.flexShrink = 0;
        panel.style.paddingLeft = 14;
        panel.style.paddingRight = 14;
        panel.style.paddingTop = 12;
        panel.style.paddingBottom = 12;
        panel.style.backgroundColor = background;
        panel.style.borderTopWidth = 2;
        panel.style.borderBottomWidth = 2;
        panel.style.borderLeftWidth = 2;
        panel.style.borderRightWidth = 2;
        panel.style.borderTopColor = PanelLine;
        panel.style.borderBottomColor = PanelLine;
        panel.style.borderLeftColor = PanelLine;
        panel.style.borderRightColor = PanelLine;
        panel.style.borderTopLeftRadius = 8;
        panel.style.borderTopRightRadius = 8;
        panel.style.borderBottomLeftRadius = 8;
        panel.style.borderBottomRightRadius = 8;
        return panel;
    }

    private void BuildLeftPanel(VisualElement panel)
    {
        var header = new VisualElement();
        header.style.marginBottom = 10;
        panel.Add(header);

        var title = new Label("Russian Road Rage");
        title.style.fontSize = 24;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = TextPrimary;
        header.Add(title);

        var sub = new Label("Garage selection");
        sub.style.color = TextSecondary;
        sub.style.fontSize = 11;
        header.Add(sub);

        panel.Add(CreateDivider());

        VisualElement carSection = CreateSection("Choose Car", panel);
        carGrid = CreateOptionList(carSection, 260);
        carDescription = CreateDescriptionLabel(carSection);

        panel.Add(CreateDivider());

        var startButton = new Button(StartGame)
        {
            text = "Start Ride"
        };
        startButton.style.marginTop = 8;
        startButton.style.height = 40;
        startButton.style.backgroundColor = AccentStrong;
        startButton.style.color = Color.white;
        startButton.style.unityFontStyleAndWeight = FontStyle.Bold;
        startButton.style.borderTopLeftRadius = 6;
        startButton.style.borderTopRightRadius = 6;
        startButton.style.borderBottomLeftRadius = 6;
        startButton.style.borderBottomRightRadius = 6;
        panel.Add(startButton);
    }

    private void BuildRightPanel(VisualElement panel)
    {
        var header = new VisualElement();
        header.style.marginBottom = 6;
        panel.Add(header);

        var title = new Label("Tune & Style");
        title.style.fontSize = 18;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = TextPrimary;
        header.Add(title);

        var sub = new Label("Pick engine, suspension, paint");
        sub.style.color = TextSecondary;
        sub.style.fontSize = 11;
        header.Add(sub);

        panel.Add(CreateDivider());

        VisualElement engineSection = CreateSection("Engine + Gearbox", panel);
        engineGrid = CreateOptionList(engineSection, 140);
        engineDescription = CreateDescriptionLabel(engineSection);

        VisualElement suspensionSection = CreateSection("Suspension", panel);
        suspensionGrid = CreateOptionList(suspensionSection, 140);
        suspensionDescription = CreateDescriptionLabel(suspensionSection);

        VisualElement paintSection = CreateSection("Paint", panel);
        paintSwatchRow = CreateSwatchRow(paintSection);
        paintDescription = CreateDescriptionLabel(paintSection);

        panel.Add(CreateDivider());
        panel.Add(CreateStatsBlock());
    }

    private VisualElement CreateStatsBlock()
    {
        var stats = new VisualElement();
        stats.style.paddingTop = 6;
        stats.style.paddingBottom = 6;
        stats.style.flexDirection = FlexDirection.Column;

        stats.Add(CreateStatRow("Horsepower", out statHorsepower));
        stats.Add(CreateStatRow("Final Drive", out statFinalDrive));
        stats.Add(CreateStatRow("Ride Height", out statRideHeight));
        return stats;
    }

    private VisualElement CreateStatRow(string label, out Label valueLabel)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.justifyContent = Justify.SpaceBetween;
        row.style.marginBottom = 2;

        var left = new Label(label);
        left.style.fontSize = 11;
        left.style.color = TextMuted;
        row.Add(left);

        var right = new Label("—");
        right.style.fontSize = 11;
        right.style.color = TextPrimary;
        row.Add(right);
        valueLabel = right;
        return row;
    }

    private static VisualElement CreateDivider()
    {
        var divider = new VisualElement();
        divider.style.height = 1;
        divider.style.marginTop = 6;
        divider.style.marginBottom = 6;
        divider.style.backgroundColor = PanelLine;
        return divider;
    }

    private static VisualElement CreateSection(string title, VisualElement parent)
    {
        var section = new VisualElement();
        section.style.marginTop = 6;
        section.style.paddingLeft = 8;
        section.style.paddingRight = 8;
        section.style.paddingTop = 6;
        section.style.paddingBottom = 4;
        section.style.backgroundColor = AccentSoft;
        section.style.borderTopLeftRadius = 6;
        section.style.borderTopRightRadius = 6;
        section.style.borderBottomLeftRadius = 6;
        section.style.borderBottomRightRadius = 6;
        parent.Add(section);

        var label = new Label(title);
        label.style.fontSize = 12;
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        label.style.color = TextPrimary;
        label.style.marginBottom = 6;
        section.Add(label);

        return section;
    }

    private static Label CreateDescriptionLabel(VisualElement parent)
    {
        var label = new Label();
        label.style.fontSize = 11;
        label.style.color = TextMuted;
        label.style.marginBottom = 2;
        parent.Add(label);
        return label;
    }

    private void RefreshOptions()
    {
        RefreshCarOptions();
        RefreshDriveOptionsForSelectedCar();

        PreviewSelection();
    }

    private void RefreshCarOptions()
    {
        carOptionNames.Clear();
        if (carGrid != null)
            carGrid.Clear();
        carCards.Clear();

        for (int i = 0; i < carLoadouts.Count; i++)
        {
            string name = carLoadouts[i] != null ? carLoadouts[i].DisplayName : "<missing>";
            carOptionNames.Add(name);
            int index = i;
            VisualElement card = CreateOptionCard(name, carLoadouts[i] != null ? carLoadouts[i].Icon : null, () =>
            {
                selectedCarIndex = index;
                UpdateCardSelection(carCards, selectedCarIndex);
                RefreshDriveOptionsForSelectedCar();
            });
            carCards.Add(card);
            carGrid?.Add(card);
        }

        if (carOptionNames.Count == 0)
        {
            VisualElement empty = CreateOptionCard("No cars", null, null);
            carCards.Add(empty);
            carGrid?.Add(empty);
        }

        selectedCarIndex = Mathf.Clamp(selectedCarIndex, 0, Mathf.Max(0, carCards.Count - 1));
        UpdateCardSelection(carCards, selectedCarIndex);
    }

    private void RefreshDriveOptionsForSelectedCar()
    {
        CarLoadoutConfig loadout = SelectedLoadout;

        engineOptionNames.Clear();
        suspensionOptionNames.Clear();
        paintSwatches.Clear();
        engineCards.Clear();
        suspensionCards.Clear();
        if (engineGrid != null)
            engineGrid.Clear();
        if (suspensionGrid != null)
            suspensionGrid.Clear();

        if (loadout != null)
        {
            for (int i = 0; i < loadout.EngineConfigs.Count; i++)
                engineOptionNames.Add(loadout.EngineConfigs[i] != null ? loadout.EngineConfigs[i].name : "<missing>");
            for (int i = 0; i < loadout.SuspensionConfigs.Count; i++)
                suspensionOptionNames.Add(loadout.SuspensionConfigs[i] != null ? loadout.SuspensionConfigs[i].name : "<missing>");

            if (engineOptionNames.Count > 0)
                selectedEngineIndex = Mathf.Clamp(loadout.DefaultEngineIndex, 0, engineOptionNames.Count - 1);
            if (suspensionOptionNames.Count > 0)
                selectedSuspensionIndex = Mathf.Clamp(loadout.DefaultSuspensionIndex, 0, suspensionOptionNames.Count - 1);
            if (loadout.PaintOptions.Count > 0)
                selectedPaintIndex = Mathf.Clamp(loadout.DefaultPaintIndex, 0, loadout.PaintOptions.Count - 1);
        }

        if (engineOptionNames.Count == 0)
            engineOptionNames.Add("No engine configs");
        if (suspensionOptionNames.Count == 0)
            suspensionOptionNames.Add("No suspension configs");
        selectedEngineIndex = Mathf.Clamp(selectedEngineIndex, 0, engineOptionNames.Count - 1);
        selectedSuspensionIndex = Mathf.Clamp(selectedSuspensionIndex, 0, suspensionOptionNames.Count - 1);
        selectedPaintIndex = Mathf.Max(0, selectedPaintIndex);

        for (int i = 0; i < engineOptionNames.Count; i++)
        {
            int index = i;
            Sprite icon = loadout != null && loadout.EngineConfigs.Count > i && loadout.EngineConfigs[i] != null
                ? loadout.EngineConfigs[i].icon
                : null;
            VisualElement card = CreateOptionCard(engineOptionNames[i], icon, () =>
            {
                selectedEngineIndex = index;
                UpdateCardSelection(engineCards, selectedEngineIndex);
                PreviewSelection();
            });
            engineCards.Add(card);
            engineGrid?.Add(card);
        }

        for (int i = 0; i < suspensionOptionNames.Count; i++)
        {
            int index = i;
            Sprite icon = loadout != null && loadout.SuspensionConfigs.Count > i && loadout.SuspensionConfigs[i] != null
                ? loadout.SuspensionConfigs[i].icon
                : null;
            VisualElement card = CreateOptionCard(suspensionOptionNames[i], icon, () =>
            {
                selectedSuspensionIndex = index;
                UpdateCardSelection(suspensionCards, selectedSuspensionIndex);
                PreviewSelection();
            });
            suspensionCards.Add(card);
            suspensionGrid?.Add(card);
        }

        UpdateCardSelection(engineCards, selectedEngineIndex);
        UpdateCardSelection(suspensionCards, selectedSuspensionIndex);

        RefreshPaintSwatches();

        PreviewSelection();
    }

    private void PreviewSelection()
    {
        CarLoadoutConfig loadout = SelectedLoadout;
        EngineGearboxConfig engine = GetSelectedEngine();
        SuspensionConfig suspension = GetSelectedSuspension();
        Color? paint = GetSelectedPaint();

        carDescription.text = loadout != null
            ? $"{loadout.DisplayName} | presets: {loadout.EngineConfigs.Count} engines, {loadout.SuspensionConfigs.Count} suspensions"
            : "No car loadouts configured";

        engineDescription.text = engine != null
            ? $"{engine.horsepower:0} hp | FD {engine.gearbox.finalDrive:0.00} | MaxRPM {engine.engine.maxRpm:0}"
            : "No engine preset";

        suspensionDescription.text = suspension != null
            ? $"Freq {suspension.suspensionFrequency:0.00} | Damp {suspension.suspensionDamping:0.00} | RideHeight {suspension.visualWheelHeight:0.00}"
            : "No suspension preset";

        if (statHorsepower != null)
            statHorsepower.text = engine != null ? $"{engine.horsepower:0} hp" : "—";
        if (statFinalDrive != null)
            statFinalDrive.text = engine != null ? $"{engine.gearbox.finalDrive:0.00}" : "—";
        if (statRideHeight != null)
            statRideHeight.text = suspension != null ? $"{suspension.visualWheelHeight:0.00}" : "—";

        paintDescription.text = paint.HasValue
            ? GetSelectedPaintLabel(paint.Value)
            : "Default paint";

        if (previewCar != null)
        {
            previewCar.OverrideLoadout(
                loadout != null ? loadout.PlayerCarConfig : null,
                loadout != null ? loadout.HandlingConfig : null,
                engine,
                suspension);
            if (paint.HasValue)
                previewCar.SetPaint(paint.Value);
        }
    }

    private void StartGame()
    {
        CarLoadoutConfig loadout = SelectedLoadout;
        PlayerCarSelection.Set(
            loadout != null ? loadout.PlayerCarConfig : null,
            loadout != null ? loadout.HandlingConfig : null,
            GetSelectedEngine(),
            GetSelectedSuspension(),
            GetSelectedPaint() ?? Color.white,
            GetSelectedPaint().HasValue);

        SceneManager.LoadScene(gameSceneName);
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

    private Color? GetSelectedPaint()
    {
        CarLoadoutConfig loadout = SelectedLoadout;
        if (loadout == null || loadout.PaintOptions == null || loadout.PaintOptions.Count == 0)
            return null;

        selectedPaintIndex = Mathf.Clamp(selectedPaintIndex, 0, loadout.PaintOptions.Count - 1);
        PaintConfig option = loadout.PaintOptions[selectedPaintIndex];
        if (option == null)
            return null;
        return option.Color;
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

        return $"RGB {paint.r:0.00}, {paint.g:0.00}, {paint.b:0.00}";
    }

    private VisualElement CreateSwatchRow(VisualElement parent)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.flexWrap = Wrap.Wrap;
        row.style.marginBottom = 6;
        parent.Add(row);
        return row;
    }

    private VisualElement CreateOptionList(VisualElement parent, float minHeight)
    {
        var list = new ScrollView(ScrollViewMode.Vertical);
        list.style.marginBottom = 6;
        list.style.minHeight = minHeight;
        list.style.maxHeight = minHeight;
        list.style.flexGrow = 1.0f;
        list.style.backgroundColor = FieldBackground;
        list.style.borderTopLeftRadius = 6;
        list.style.borderTopRightRadius = 6;
        list.style.borderBottomLeftRadius = 6;
        list.style.borderBottomRightRadius = 6;
        parent.Add(list);
        return list;
    }

    private VisualElement CreateOptionCard(string text, Sprite icon, System.Action onClick)
    {
        EnsureCardStyles();
        var card = new VisualElement();
        ApplyCardStyle(card, cardStyle);
        card.style.width = Length.Percent(100);

        var iconBox = new VisualElement();
        ApplyIconBoxStyle(iconBox, icon != null);
        card.Add(iconBox);

        if (icon != null)
        {
            var img = new Image();
            img.image = icon.texture;
            img.scaleMode = ScaleMode.ScaleToFit;
            img.style.width = 22;
            img.style.height = 22;
            img.style.unityBackgroundImageTintColor = Color.black;
            img.style.marginLeft = 5;
            img.style.marginTop = 5;
            iconBox.Add(img);
        }

        var label = new Label(text);
        label.style.fontSize = 11;
        label.style.unityTextAlign = TextAnchor.MiddleLeft;
        label.style.color = TextPrimary;
        label.style.whiteSpace = WhiteSpace.Normal;
        label.style.flexGrow = 1;
        card.Add(label);

        card.userData = new CardParts
        {
            Label = label,
            IconBox = iconBox
        };

        if (onClick != null)
        {
            card.RegisterCallback<ClickEvent>(_ => onClick.Invoke());
        }

        return card;
    }

    private void UpdateCardSelection(List<VisualElement> cards, int selectedIndex)
    {
        if (cards == null)
            return;
        for (int i = 0; i < cards.Count; i++)
        {
            bool selected = i == selectedIndex;
            SetCardSelected(cards[i], selected);
        }
    }

    private void SetCardSelected(VisualElement card, bool selected)
    {
        if (card == null)
            return;
        ApplyCardStyle(card, selected ? cardStyleSelected : cardStyle);
        if (card.userData is CardParts parts)
        {
            if (parts.Label != null)
                parts.Label.style.color = selected ? new Color(0.1f, 0.1f, 0.1f, 1f) : TextPrimary;
            if (parts.IconBox != null)
                ApplyIconBoxStyle(parts.IconBox, true, selected);
        }
    }

    private void EnsureCardStyles()
    {
        if (cardStyle != null)
            return;

        cardStyle = new CardStyle
        {
            Width = 320,
            Height = 48,
            Margin = 6,
            Background = new Color(0.12f, 0.13f, 0.15f, 1f),
            Border = PanelLine,
            Radius = 4,
            PaddingX = 8
        };

        cardStyleSelected = new CardStyle
        {
            Width = 320,
            Height = 48,
            Margin = 6,
            Background = Accent,
            Border = AccentStrong,
            Radius = 4,
            PaddingX = 8
        };
    }

    private static void ApplyCardStyle(VisualElement card, CardStyle style)
    {
        card.style.width = style.Width;
        card.style.height = style.Height;
        card.style.marginRight = style.Margin;
        card.style.marginBottom = style.Margin;
        card.style.backgroundColor = style.Background;
        card.style.borderTopLeftRadius = style.Radius;
        card.style.borderTopRightRadius = style.Radius;
        card.style.borderBottomLeftRadius = style.Radius;
        card.style.borderBottomRightRadius = style.Radius;
        card.style.borderTopWidth = 2;
        card.style.borderBottomWidth = 2;
        card.style.borderLeftWidth = 2;
        card.style.borderRightWidth = 2;
        card.style.borderTopColor = style.Border;
        card.style.borderBottomColor = style.Border;
        card.style.borderLeftColor = style.Border;
        card.style.borderRightColor = style.Border;
        card.style.flexDirection = FlexDirection.Row;
        card.style.alignItems = Align.Center;
        card.style.paddingLeft = style.PaddingX;
        card.style.paddingRight = style.PaddingX;
    }

    private void ApplyIconBoxStyle(VisualElement iconBox, bool hasIcon, bool selected = false)
    {
        iconBox.style.width = 32;
        iconBox.style.height = 32;
        iconBox.style.marginRight = 8;
        iconBox.style.borderTopLeftRadius = 4;
        iconBox.style.borderTopRightRadius = 4;
        iconBox.style.borderBottomLeftRadius = 4;
        iconBox.style.borderBottomRightRadius = 4;
        if (selected)
            iconBox.style.backgroundColor = new Color(0.1f, 0.1f, 0.1f, 1f);
        else if (hasIcon)
            iconBox.style.backgroundColor = Color.white;
        else
            iconBox.style.backgroundColor = new Color(0.2f, 0.22f, 0.24f, 1f);
    }

    private sealed class CardStyle
    {
        public float Width;
        public float Height;
        public float Margin;
        public float Radius;
        public float PaddingX;
        public Color Background;
        public Color Border;
    }

    private sealed class CardParts
    {
        public Label Label;
        public VisualElement IconBox;
    }

    private void RefreshPaintSwatches()
    {
        if (paintSwatchRow == null)
            return;

        paintSwatchRow.Clear();
        paintSwatches.Clear();

        CarLoadoutConfig loadout = SelectedLoadout;
        List<PaintConfig> options = loadout != null ? loadout.PaintOptions : null;
        if (options == null || options.Count == 0)
        {
            CreateDefaultPaintSwatch();
            return;
        }

        selectedPaintIndex = Mathf.Clamp(selectedPaintIndex, 0, options.Count - 1);
        for (int i = 0; i < options.Count; i++)
        {
            PaintConfig option = options[i];
            if (option == null)
                continue;
            CreatePaintSwatch(option, i);
        }

        UpdatePaintSwatchSelection();
    }

    private void CreateDefaultPaintSwatch()
    {
        Color fallback = Color.white;
        CarLoadoutConfig loadout = SelectedLoadout;
        if (loadout != null && loadout.PlayerCarConfig != null)
        {
            PlayerCarVisualSettings visual = loadout.PlayerCarConfig.Visual;
            if (visual != null && visual.useDefaultPaint)
                fallback = visual.defaultPaint;
        }

        var swatch = CreateSwatchElement(fallback, "Default paint");
        paintSwatches.Add(swatch);
        paintSwatchRow.Add(swatch);
        SetSwatchSelected(swatch, true);
    }

    private void CreatePaintSwatch(PaintConfig option, int index)
    {
        var swatch = CreateSwatchElement(option.Color, option.DisplayName);
        swatch.RegisterCallback<ClickEvent>(_ =>
        {
            selectedPaintIndex = index;
            UpdatePaintSwatchSelection();
            PreviewSelection();
        });
        paintSwatches.Add(swatch);
        paintSwatchRow.Add(swatch);
    }

    private VisualElement CreateSwatchElement(Color color, string tooltip)
    {
        var swatch = new VisualElement();
        swatch.style.width = 28;
        swatch.style.height = 18;
        swatch.style.marginRight = 6;
        swatch.style.marginBottom = 6;
        swatch.style.backgroundColor = color;
        swatch.style.borderTopLeftRadius = 2;
        swatch.style.borderTopRightRadius = 2;
        swatch.style.borderBottomLeftRadius = 2;
        swatch.style.borderBottomRightRadius = 2;
        swatch.style.borderTopWidth = 2;
        swatch.style.borderBottomWidth = 2;
        swatch.style.borderLeftWidth = 2;
        swatch.style.borderRightWidth = 2;
        swatch.tooltip = tooltip;
        return swatch;
    }

    private void UpdatePaintSwatchSelection()
    {
        for (int i = 0; i < paintSwatches.Count; i++)
        {
            bool selected = i == selectedPaintIndex;
            SetSwatchSelected(paintSwatches[i], selected);
        }
    }

    private void SetSwatchSelected(VisualElement swatch, bool selected)
    {
        if (swatch == null)
            return;

        Color border = selected ? Accent : PanelLine;
        swatch.style.borderTopColor = border;
        swatch.style.borderBottomColor = border;
        swatch.style.borderLeftColor = border;
        swatch.style.borderRightColor = border;
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

        string status = string.IsNullOrEmpty(debugStatus) ? "no status" : debugStatus;
        string loadouts = carLoadouts != null ? carLoadouts.Count.ToString() : "null";
        string rootState = uiDocument != null && uiDocument.rootVisualElement != null ? "root ok" : "root null";
        GUI.Label(new Rect(10, 10, 600, 24), $"GarageMenuController: {status} | loadouts {loadouts} | {rootState}");
    }
}
