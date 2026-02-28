using UnityEngine;
using UnityEngine.UI;

public class CarHud : MonoBehaviour
{
    [SerializeField] private CarControllerBase target;
    [SerializeField] private Slider rpmSlider;
    [SerializeField] private Text rpmText;
    [SerializeField] private Text speedText;
    [SerializeField] private Text gearText;
    [SerializeField] private Text shiftText;
    [SerializeField] private bool autoFindTarget = true;

    public void SetTarget(CarControllerBase newTarget)
    {
        target = newTarget;
        UpdateSliderRange();
    }

    private void Awake()
    {
        if (autoFindTarget && target == null)
            target = FindObjectOfType<CarControllerBase>();
    }

    private void Start()
    {
        if (rpmSlider == null || rpmText == null || gearText == null)
            BuildRuntimeUi();

        UpdateSliderRange();
    }

    private void Update()
    {
        if (target == null)
            return;

        UpdateSliderRange();
        if (rpmSlider != null)
            rpmSlider.value = target.CurrentRpm;

        if (rpmText != null)
            rpmText.text = $"RPM {Mathf.RoundToInt(target.CurrentRpm)}";

        if (speedText != null)
            speedText.text = $"Speed {Mathf.RoundToInt(target.SpeedKph)} km/h";

        if (gearText != null)
            gearText.text = $"Gear {FormatGear(target.CurrentGear)}";

        if (shiftText != null)
            shiftText.text = $"Shift {target.ShiftTimeRemaining:0.00}s";
    }

    private void UpdateSliderRange()
    {
        if (target == null || rpmSlider == null)
            return;

        rpmSlider.minValue = target.IdleRpm;
        rpmSlider.maxValue = target.MaxRpm;
    }

    private static string FormatGear(int gear)
    {
        if (gear > 0)
            return gear.ToString();
        if (gear < 0)
            return "R";
        return "N";
    }

    private void BuildRuntimeUi()
    {
        GameObject canvasObject = new GameObject("CarHudCanvas");
        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer >= 0)
            canvasObject.layer = uiLayer;
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        canvasObject.AddComponent<CanvasScaler>();
        canvasObject.AddComponent<GraphicRaycaster>();

        GameObject panel = new GameObject("Panel");
        panel.transform.SetParent(canvasObject.transform, false);
        RectTransform panelRect = panel.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.02f, 0.02f);
        panelRect.anchorMax = new Vector2(0.35f, 0.18f);
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        Image panelImage = panel.AddComponent<Image>();
        panelImage.color = new Color(0.05f, 0.05f, 0.05f, 0.6f);

        GameObject sliderObject = new GameObject("RpmSlider");
        sliderObject.transform.SetParent(panel.transform, false);
        rpmSlider = sliderObject.AddComponent<Slider>();
        rpmSlider.direction = Slider.Direction.LeftToRight;
        rpmSlider.transition = Selectable.Transition.None;
        rpmSlider.wholeNumbers = false;
        RectTransform sliderRect = sliderObject.GetComponent<RectTransform>();
        sliderRect.anchorMin = new Vector2(0.0f, 0.45f);
        sliderRect.anchorMax = new Vector2(1.0f, 0.9f);
        sliderRect.offsetMin = new Vector2(10.0f, 0.0f);
        sliderRect.offsetMax = new Vector2(-10.0f, 0.0f);

        GameObject sliderBackground = CreateUiImage("Background", sliderObject.transform, new Color(0.1f, 0.1f, 0.1f, 0.85f));
        GameObject sliderFill = CreateUiImage("Fill", sliderObject.transform, new Color(0.2f, 0.75f, 0.35f, 0.95f));
        GameObject sliderHandle = CreateUiImage("Handle", sliderObject.transform, Color.white);

        Image backgroundImage = sliderBackground.GetComponent<Image>();
        Image fillImage = sliderFill.GetComponent<Image>();
        Image handleImage = sliderHandle.GetComponent<Image>();

        RectTransform backgroundRect = sliderBackground.GetComponent<RectTransform>();
        backgroundRect.anchorMin = new Vector2(0.0f, 0.2f);
        backgroundRect.anchorMax = new Vector2(1.0f, 0.8f);
        backgroundRect.offsetMin = Vector2.zero;
        backgroundRect.offsetMax = Vector2.zero;

        RectTransform fillRect = sliderFill.GetComponent<RectTransform>();
        fillRect.anchorMin = new Vector2(0.0f, 0.2f);
        fillRect.anchorMax = new Vector2(1.0f, 0.8f);
        fillRect.offsetMin = new Vector2(0.0f, 0.0f);
        fillRect.offsetMax = new Vector2(0.0f, 0.0f);

        RectTransform handleRect = sliderHandle.GetComponent<RectTransform>();
        handleRect.anchorMin = new Vector2(0.0f, 0.2f);
        handleRect.anchorMax = new Vector2(0.0f, 0.8f);
        handleRect.sizeDelta = new Vector2(14.0f, 24.0f);

        rpmSlider.targetGraphic = handleImage;
        rpmSlider.fillRect = fillRect;
        rpmSlider.handleRect = handleRect;

        GameObject rpmTextObject = CreateUiText("RpmText", panel.transform, "RPM");
        rpmText = rpmTextObject.GetComponent<Text>();
        RectTransform rpmTextRect = rpmTextObject.GetComponent<RectTransform>();
        rpmTextRect.anchorMin = new Vector2(0.0f, 0.0f);
        rpmTextRect.anchorMax = new Vector2(0.45f, 0.35f);
        rpmTextRect.offsetMin = new Vector2(10.0f, 0.0f);
        rpmTextRect.offsetMax = new Vector2(-10.0f, 0.0f);

        GameObject speedTextObject = CreateUiText("SpeedText", panel.transform, "Speed");
        speedText = speedTextObject.GetComponent<Text>();
        RectTransform speedTextRect = speedTextObject.GetComponent<RectTransform>();
        speedTextRect.anchorMin = new Vector2(0.0f, 0.3f);
        speedTextRect.anchorMax = new Vector2(1.0f, 0.45f);
        speedTextRect.offsetMin = new Vector2(10.0f, 0.0f);
        speedTextRect.offsetMax = new Vector2(-10.0f, 0.0f);
        speedText.alignment = TextAnchor.MiddleLeft;

        GameObject gearTextObject = CreateUiText("GearText", panel.transform, "Gear");
        gearText = gearTextObject.GetComponent<Text>();
        RectTransform gearTextRect = gearTextObject.GetComponent<RectTransform>();
        gearTextRect.anchorMin = new Vector2(0.75f, 0.0f);
        gearTextRect.anchorMax = new Vector2(1.0f, 0.35f);
        gearTextRect.offsetMin = new Vector2(0.0f, 0.0f);
        gearTextRect.offsetMax = new Vector2(-10.0f, 0.0f);
        gearText.alignment = TextAnchor.MiddleRight;

        GameObject shiftTextObject = CreateUiText("ShiftText", panel.transform, "Shift");
        shiftText = shiftTextObject.GetComponent<Text>();
        RectTransform shiftTextRect = shiftTextObject.GetComponent<RectTransform>();
        shiftTextRect.anchorMin = new Vector2(0.45f, 0.0f);
        shiftTextRect.anchorMax = new Vector2(0.75f, 0.35f);
        shiftTextRect.offsetMin = new Vector2(10.0f, 0.0f);
        shiftTextRect.offsetMax = new Vector2(-10.0f, 0.0f);
        shiftText.alignment = TextAnchor.MiddleCenter;
    }

    private static GameObject CreateUiImage(string name, Transform parent, Color color)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        Image image = obj.AddComponent<Image>();
        image.color = color;
        return obj;
    }

    private static GameObject CreateUiText(string name, Transform parent, string text)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        Text uiText = obj.AddComponent<Text>();
        uiText.text = text;
        uiText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        uiText.fontSize = 18;
        uiText.alignment = TextAnchor.MiddleLeft;
        uiText.color = Color.white;
        uiText.raycastTarget = false;
        return obj;
    }
}
