using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class GameHudController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private CarControllerBase target;

    [Header("Layout")]
    [SerializeField] private float maxRpmFallback = 7000.0f;

    private Label speedLabel;
    private Label gearLabel;
    private Label rpmLabel;
    private Label shiftLabel;
    private VisualElement rpmFill;
    private VisualElement rpmTrack;
    private VisualElement nitroFill;
    private VisualElement nitroTrack;

    private void Awake()
    {
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();
        if (target == null)
            target = FindFirstObjectByType<CarControllerBase>();

        BuildUi();
    }

    private void Update()
    {
        if (target == null)
            return;

        float rpm = target.CurrentRpm;
        float maxRpm = Mathf.Max(1.0f, target.MaxRpm > 1.0f ? target.MaxRpm : maxRpmFallback);
        float rpm01 = Mathf.Clamp01(rpm / maxRpm);

        if (rpmLabel != null)
            rpmLabel.text = $"RPM {Mathf.RoundToInt(rpm)}";
        if (speedLabel != null)
            speedLabel.text = $"Speed {Mathf.RoundToInt(target.SpeedKph)} km/h";
        if (gearLabel != null)
            gearLabel.text = $"Gear {FormatGear(target.CurrentGear)}";
        if (shiftLabel != null)
            shiftLabel.text = $"Shift {target.ShiftTimeRemaining:0.00}s";

        if (rpmFill != null && rpmTrack != null)
        {
            float width = Mathf.Max(1.0f, rpmTrack.resolvedStyle.width);
            rpmFill.style.width = width * rpm01;
        }

        if (nitroFill != null && nitroTrack != null)
        {
            float width = Mathf.Max(1.0f, nitroTrack.resolvedStyle.width);
            nitroFill.style.width = width * Mathf.Clamp01(target.NitroAmount);
        }
    }

    private void BuildUi()
    {
        VisualElement root = uiDocument.rootVisualElement;
        root.Clear();
        root.style.flexGrow = 1.0f;
        root.style.flexDirection = FlexDirection.Column;
        root.style.justifyContent = Justify.FlexEnd;
        root.style.alignItems = Align.FlexStart;
        root.style.paddingLeft = 16;
        root.style.paddingBottom = 16;

        var panel = new VisualElement();
        panel.style.width = 280;
        panel.style.paddingLeft = 12;
        panel.style.paddingRight = 12;
        panel.style.paddingTop = 8;
        panel.style.paddingBottom = 8;
        panel.style.backgroundColor = new Color(0.05f, 0.06f, 0.07f, 0.85f);
        panel.style.borderTopWidth = 2;
        panel.style.borderBottomWidth = 2;
        panel.style.borderLeftWidth = 2;
        panel.style.borderRightWidth = 2;
        panel.style.borderTopColor = new Color(0.67f, 0.58f, 0.37f, 1f);
        panel.style.borderBottomColor = new Color(0.67f, 0.58f, 0.37f, 1f);
        panel.style.borderLeftColor = new Color(0.67f, 0.58f, 0.37f, 1f);
        panel.style.borderRightColor = new Color(0.67f, 0.58f, 0.37f, 1f);
        panel.style.borderTopLeftRadius = 6;
        panel.style.borderTopRightRadius = 6;
        panel.style.borderBottomLeftRadius = 6;
        panel.style.borderBottomRightRadius = 6;
        root.Add(panel);

        rpmTrack = new VisualElement();
        rpmTrack.style.height = 10;
        rpmTrack.style.backgroundColor = new Color(0.12f, 0.13f, 0.15f, 1f);
        rpmTrack.style.borderTopLeftRadius = 3;
        rpmTrack.style.borderTopRightRadius = 3;
        rpmTrack.style.borderBottomLeftRadius = 3;
        rpmTrack.style.borderBottomRightRadius = 3;
        rpmTrack.style.marginBottom = 6;
        panel.Add(rpmTrack);

        rpmFill = new VisualElement();
        rpmFill.style.height = 10;
        rpmFill.style.width = 1;
        rpmFill.style.backgroundColor = new Color(0.25f, 0.78f, 0.38f, 0.95f);
        rpmFill.style.borderTopLeftRadius = 3;
        rpmFill.style.borderBottomLeftRadius = 3;
        rpmTrack.Add(rpmFill);

        nitroTrack = new VisualElement();
        nitroTrack.style.height = 8;
        nitroTrack.style.backgroundColor = new Color(0.08f, 0.1f, 0.12f, 1f);
        nitroTrack.style.borderTopLeftRadius = 3;
        nitroTrack.style.borderTopRightRadius = 3;
        nitroTrack.style.borderBottomLeftRadius = 3;
        nitroTrack.style.borderBottomRightRadius = 3;
        nitroTrack.style.marginBottom = 6;
        panel.Add(nitroTrack);

        nitroFill = new VisualElement();
        nitroFill.style.height = 8;
        nitroFill.style.width = 1;
        nitroFill.style.backgroundColor = new Color(0.2f, 0.55f, 0.95f, 0.95f);
        nitroFill.style.borderTopLeftRadius = 3;
        nitroFill.style.borderBottomLeftRadius = 3;
        nitroTrack.Add(nitroFill);

        rpmLabel = CreateLabel(panel, "RPM 0", 12, TextAnchor.MiddleLeft);
        speedLabel = CreateLabel(panel, "Speed 0 km/h", 12, TextAnchor.MiddleLeft);

        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.justifyContent = Justify.SpaceBetween;
        row.style.marginTop = 2;
        panel.Add(row);

        shiftLabel = CreateLabel(row, "Shift 0.00s", 11, TextAnchor.MiddleLeft);
        gearLabel = CreateLabel(row, "Gear 1", 11, TextAnchor.MiddleRight);
    }

    private static Label CreateLabel(VisualElement parent, string text, int size, TextAnchor align)
    {
        var label = new Label(text);
        label.style.color = new Color(0.9f, 0.92f, 0.94f, 1f);
        label.style.fontSize = size;
        label.style.unityTextAlign = align;
        parent.Add(label);
        return label;
    }

    private static string FormatGear(int gear)
    {
        if (gear > 0)
            return gear.ToString();
        if (gear < 0)
            return "R";
        return "N";
    }
}
