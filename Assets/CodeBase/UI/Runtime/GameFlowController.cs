using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class GameFlowController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private PlayerCar playerCar;
    [SerializeField] private MonoBehaviour[] gameplayComponents;
    [SerializeField] private bool showMenuOnStart = false;

    [Header("Configs")]
    [SerializeField] private List<EngineGearboxConfig> engineConfigs = new List<EngineGearboxConfig>();
    [SerializeField] private List<SuspensionConfig> suspensionConfigs = new List<SuspensionConfig>();

    private readonly List<string> engineOptionNames = new List<string>();
    private readonly List<string> suspensionOptionNames = new List<string>();

    private GameStateMachine stateMachine;
    private MainMenuState mainMenuState;
    private GameState gameState;

    private VisualElement menuRoot;
    private DropdownField engineDropdown;
    private DropdownField suspensionDropdown;
    private Label engineDescription;
    private Label suspensionDescription;
    private Button playButton;

    private int selectedEngineIndex;
    private int selectedSuspensionIndex;

    private void Awake()
    {
        ResolveReferences();
        BuildMenuUi();
        stateMachine = new GameStateMachine();
        mainMenuState = new MainMenuState(this);
        gameState = new GameState(this);
        stateMachine.ChangeState(showMenuOnStart ? (IGameState)mainMenuState : gameState);
    }

    private void Update()
    {
        stateMachine?.Tick();
    }

    public void OpenMenu()
    {
        stateMachine?.ChangeState(mainMenuState);
    }

    public void ShowMainMenu()
    {
        Time.timeScale = 0.0f;
        SetGameplayEnabled(false);
        SetCursorForMenu();
        if (menuRoot != null)
            menuRoot.style.display = DisplayStyle.Flex;
    }

    public void HideMainMenu()
    {
        if (menuRoot != null)
            menuRoot.style.display = DisplayStyle.None;
    }

    public void StartGameplay()
    {
        ApplySelectedConfigsToCar();
        Time.timeScale = 1.0f;
        SetGameplayEnabled(true);
        SetCursorForGameplay();
    }

    public void StopGameplay()
    {
        SetGameplayEnabled(false);
    }

    private void ResolveReferences()
    {
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();

        if (playerCar == null)
            playerCar = FindFirstObjectByType<PlayerCar>();

        if (gameplayComponents == null || gameplayComponents.Length == 0)
        {
            var items = new List<MonoBehaviour>();
            FollowCarCamera cameraController = FindFirstObjectByType<FollowCarCamera>();
            if (cameraController != null)
                items.Add(cameraController);
            CarGunShooter shooter = FindFirstObjectByType<CarGunShooter>();
            if (shooter != null)
                items.Add(shooter);
            gameplayComponents = items.ToArray();
        }
    }

    private void BuildMenuUi()
    {
        VisualElement root = uiDocument.rootVisualElement;
        root.Clear();

        menuRoot = new VisualElement();
        menuRoot.style.flexGrow = 1.0f;
        menuRoot.style.justifyContent = Justify.Center;
        menuRoot.style.alignItems = Align.Center;
        menuRoot.style.backgroundColor = new Color(0.04f, 0.05f, 0.06f, 0.9f);
        root.Add(menuRoot);

        var panel = new VisualElement();
        panel.style.width = 620.0f;
        panel.style.paddingTop = 20.0f;
        panel.style.paddingBottom = 20.0f;
        panel.style.paddingLeft = 20.0f;
        panel.style.paddingRight = 20.0f;
        panel.style.backgroundColor = new Color(0.12f, 0.14f, 0.16f, 0.98f);
        panel.style.borderTopWidth = 2.0f;
        panel.style.borderBottomWidth = 2.0f;
        panel.style.borderLeftWidth = 2.0f;
        panel.style.borderRightWidth = 2.0f;
        panel.style.borderTopColor = new Color(0.65f, 0.55f, 0.35f, 1.0f);
        panel.style.borderBottomColor = new Color(0.65f, 0.55f, 0.35f, 1.0f);
        panel.style.borderLeftColor = new Color(0.65f, 0.55f, 0.35f, 1.0f);
        panel.style.borderRightColor = new Color(0.65f, 0.55f, 0.35f, 1.0f);
        menuRoot.Add(panel);

        var title = new Label("RUSSIAN ROAD RAGE");
        title.style.fontSize = 32;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = new Color(0.95f, 0.84f, 0.64f, 1.0f);
        title.style.unityTextAlign = TextAnchor.MiddleCenter;
        title.style.marginBottom = 16;
        panel.Add(title);

        var subtitle = new Label("Выбери мотор и подвеску перед стартом");
        subtitle.style.fontSize = 14;
        subtitle.style.color = new Color(0.82f, 0.85f, 0.88f, 1.0f);
        subtitle.style.unityTextAlign = TextAnchor.MiddleCenter;
        subtitle.style.marginBottom = 16;
        panel.Add(subtitle);

        engineDropdown = CreateDropdown("Двигатель и коробка", panel);
        suspensionDropdown = CreateDropdown("Подвеска", panel);

        engineDescription = CreateDescriptionLabel(panel);
        suspensionDescription = CreateDescriptionLabel(panel);

        playButton = new Button(StartGameFromMenu)
        {
            text = "Поехали"
        };
        playButton.style.height = 42;
        playButton.style.marginTop = 16;
        playButton.style.backgroundColor = new Color(0.76f, 0.22f, 0.18f, 1.0f);
        playButton.style.color = Color.white;
        playButton.style.unityFontStyleAndWeight = FontStyle.Bold;
        panel.Add(playButton);

        RefreshOptions();
    }

    private DropdownField CreateDropdown(string labelText, VisualElement parent)
    {
        var label = new Label(labelText);
        label.style.fontSize = 15;
        label.style.color = new Color(0.92f, 0.93f, 0.94f, 1.0f);
        label.style.marginTop = 8;
        parent.Add(label);

        var dropdown = new DropdownField();
        dropdown.style.height = 32;
        dropdown.style.marginBottom = 8;
        dropdown.style.backgroundColor = new Color(0.18f, 0.2f, 0.22f, 1.0f);
        dropdown.style.color = new Color(0.96f, 0.96f, 0.96f, 1.0f);
        parent.Add(dropdown);
        return dropdown;
    }

    private static Label CreateDescriptionLabel(VisualElement parent)
    {
        var label = new Label();
        label.style.fontSize = 12;
        label.style.color = new Color(0.75f, 0.8f, 0.84f, 1.0f);
        label.style.marginBottom = 4;
        parent.Add(label);
        return label;
    }

    private void RefreshOptions()
    {
        engineOptionNames.Clear();
        for (int i = 0; i < engineConfigs.Count; i++)
            engineOptionNames.Add(engineConfigs[i] != null ? engineConfigs[i].name : "<missing>");

        suspensionOptionNames.Clear();
        for (int i = 0; i < suspensionConfigs.Count; i++)
            suspensionOptionNames.Add(suspensionConfigs[i] != null ? suspensionConfigs[i].name : "<missing>");

        if (engineOptionNames.Count == 0)
            engineOptionNames.Add("Нет конфигов");
        if (suspensionOptionNames.Count == 0)
            suspensionOptionNames.Add("Нет конфигов");

        selectedEngineIndex = Mathf.Clamp(selectedEngineIndex, 0, engineOptionNames.Count - 1);
        selectedSuspensionIndex = Mathf.Clamp(selectedSuspensionIndex, 0, suspensionOptionNames.Count - 1);

        engineDropdown.choices = engineOptionNames;
        engineDropdown.index = selectedEngineIndex;

        suspensionDropdown.choices = suspensionOptionNames;
        suspensionDropdown.index = selectedSuspensionIndex;

        engineDropdown.RegisterValueChangedCallback(_ =>
        {
            selectedEngineIndex = Mathf.Max(0, engineDropdown.index);
            PreviewSelectedConfigs();
        });

        suspensionDropdown.RegisterValueChangedCallback(_ =>
        {
            selectedSuspensionIndex = Mathf.Max(0, suspensionDropdown.index);
            PreviewSelectedConfigs();
        });

        playButton.SetEnabled(engineConfigs.Count > 0 && suspensionConfigs.Count > 0 && playerCar != null);
        PreviewSelectedConfigs();
    }

    private void PreviewSelectedConfigs()
    {
        EngineGearboxConfig engineConfig = GetSelectedEngineConfig();
        SuspensionConfig suspensionConfig = GetSelectedSuspensionConfig();

        if (engineConfig != null)
            engineDescription.text = $"{engineConfig.horsepower:0} hp | Final Drive {engineConfig.gearbox.finalDrive:0.00} | Max RPM {engineConfig.engine.maxRpm:0}";
        else
            engineDescription.text = "Нет двигателя/коробки";

        if (suspensionConfig != null)
            suspensionDescription.text = $"Freq {suspensionConfig.suspensionFrequency:0.00} | Damping {suspensionConfig.suspensionDamping:0.00} | Visual height {suspensionConfig.visualWheelHeight:0.00}";
        else
            suspensionDescription.text = "Нет подвески";

        if (playerCar != null)
            playerCar.OverrideDriveConfigs(engineConfig, suspensionConfig);
    }

    private void ApplySelectedConfigsToCar()
    {
        if (playerCar == null)
            return;

        playerCar.OverrideDriveConfigs(GetSelectedEngineConfig(), GetSelectedSuspensionConfig());
    }

    private EngineGearboxConfig GetSelectedEngineConfig()
    {
        if (engineConfigs == null || engineConfigs.Count == 0)
            return null;
        selectedEngineIndex = Mathf.Clamp(selectedEngineIndex, 0, engineConfigs.Count - 1);
        return engineConfigs[selectedEngineIndex];
    }

    private SuspensionConfig GetSelectedSuspensionConfig()
    {
        if (suspensionConfigs == null || suspensionConfigs.Count == 0)
            return null;
        selectedSuspensionIndex = Mathf.Clamp(selectedSuspensionIndex, 0, suspensionConfigs.Count - 1);
        return suspensionConfigs[selectedSuspensionIndex];
    }

    private void StartGameFromMenu()
    {
        stateMachine.ChangeState(gameState);
    }

    private void SetGameplayEnabled(bool enabled)
    {
        if (gameplayComponents == null)
            return;

        for (int i = 0; i < gameplayComponents.Length; i++)
        {
            if (gameplayComponents[i] != null)
                gameplayComponents[i].enabled = enabled;
        }
    }

    private static void SetCursorForMenu()
    {
        UnityEngine.Cursor.lockState = CursorLockMode.None;
        UnityEngine.Cursor.visible = true;
    }

    private static void SetCursorForGameplay()
    {
        UnityEngine.Cursor.lockState = CursorLockMode.Locked;
        UnityEngine.Cursor.visible = false;
    }
}
