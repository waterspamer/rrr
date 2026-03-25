using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

[DefaultExecutionOrder(1320)]
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkVehicleEntity))]
public sealed class PurrVehicleWorldSpaceStatusUi : MonoBehaviour
{
    [SerializeField] private NetworkVehicleEntity entity;
    [SerializeField] private bool showForLocalPlayer;
    [SerializeField, Min(0.5f)] private float verticalOffset = 2.35f;
    [SerializeField, Min(0.25f)] private float maxVisibleDistance = 90.0f;
    [SerializeField] private Vector2 worldPanelSize = new Vector2(1.9f, 0.62f);
    [SerializeField, Min(128)] private int textureWidth = 512;
    [SerializeField, Min(64)] private int textureHeight = 196;
    [SerializeField] private int sortingOrder = 240;

    private Transform root;
    private MeshRenderer panelRenderer;
    private UIDocument uiDocument;
    private PanelSettings runtimePanelSettings;
    private RenderTexture renderTexture;
    private Material panelMaterial;
    private Camera targetCamera;

    private bool uiBuilt;
    private Label nameLabel;
    private Label detailLabel;
    private Label telemetryLabel;
    private Label hpValueLabel;
    private VisualElement hpFill;

    private void Awake()
    {
        if (entity == null)
            entity = GetComponent<NetworkVehicleEntity>();

        EnsureWorldPanel();
        RefreshVisualState();
    }

    private void OnEnable()
    {
        if (entity == null)
            entity = GetComponent<NetworkVehicleEntity>();

        if (entity != null)
        {
            entity.IdentityChanged -= HandleEntityChanged;
            entity.IdentityChanged += HandleEntityChanged;
            entity.StatsChanged -= HandleEntityChanged;
            entity.StatsChanged += HandleEntityChanged;
        }

        EnsureWorldPanel();
        RefreshVisualState();
    }

    private void OnDisable()
    {
        if (entity == null)
            return;

        entity.IdentityChanged -= HandleEntityChanged;
        entity.StatsChanged -= HandleEntityChanged;
    }

    private void OnDestroy()
    {
        if (renderTexture != null)
        {
            renderTexture.Release();
            Destroy(renderTexture);
        }

        if (panelMaterial != null)
            Destroy(panelMaterial);
        if (runtimePanelSettings != null)
            Destroy(runtimePanelSettings);
    }

    private void LateUpdate()
    {
        EnsureWorldPanel();
        EnsureUiBuilt();
        targetCamera = ResolveCamera();
        if (root == null)
            return;

        bool visible = entity != null && (!entity.IsLocalPlayer || showForLocalPlayer) && targetCamera != null;
        if (visible)
        {
            float distance = Vector3.Distance(targetCamera.transform.position, transform.position);
            visible = distance <= maxVisibleDistance;
        }

        if (root.gameObject.activeSelf != visible)
            root.gameObject.SetActive(visible);
        if (!visible)
            return;

        Vector3 worldPosition = transform.position + Vector3.up * verticalOffset;
        root.position = worldPosition;
        Vector3 toCamera = worldPosition - targetCamera.transform.position;
        if (toCamera.sqrMagnitude > 0.0001f)
            root.rotation = Quaternion.LookRotation(toCamera.normalized, targetCamera.transform.up);
    }

    private void HandleEntityChanged(NetworkVehicleEntity changedEntity)
    {
        RefreshVisualState();
    }

    private void RefreshVisualState()
    {
        EnsureWorldPanel();
        EnsureUiBuilt();
        if (!uiBuilt || entity == null || nameLabel == null || detailLabel == null || telemetryLabel == null || hpValueLabel == null || hpFill == null)
            return;

        nameLabel.text = entity.PlayerName;
        nameLabel.style.color = entity.IsBot
            ? new Color(1.0f, 0.84f, 0.30f, 1.0f)
            : entity.IsLocalPlayer
                ? new Color(0.34f, 0.95f, 1.0f, 1.0f)
                : Color.white;

        float healthNormalized = 1.0f;
        if (!entity.TryGetNormalizedHealth(out healthNormalized))
            healthNormalized = 1.0f;

        float currentHealth = healthNormalized * 100.0f;
        float maxHealth = 100.0f;
        if (!entity.TryGetNumberStat("current_health", out currentHealth))
            currentHealth = healthNormalized * 100.0f;
        if (!entity.TryGetNumberStat("max_health", out maxHealth) || maxHealth <= 0.01f)
            maxHealth = 100.0f;

        long gear = 0;
        entity.TryGetIntegerStat("gear", out gear);
        float speedKph = 0.0f;
        entity.TryGetNumberStat("speed_kph", out speedKph);
        telemetryLabel.text = $"{Mathf.RoundToInt(speedKph)} km/h  |  G{gear}";

        if (entity.TryGetGaragePublicSummary(out PurrPlayerGaragePublicSummary garageSummary) && garageSummary != null)
        {
            detailLabel.text = $"{garageSummary.selectedCarDisplayName}  |  ${garageSummary.balanceSoft}";
        }
        else
        {
            detailLabel.text = entity.IsBot ? "Server-controlled vehicle" : "Player data pending";
        }

        hpValueLabel.text = $"HP {Mathf.RoundToInt(currentHealth)}/{Mathf.RoundToInt(maxHealth)}";
        float clampedHealth = Mathf.Clamp01(healthNormalized);
        hpFill.style.width = Length.Percent(clampedHealth * 100.0f);
        hpFill.style.backgroundColor = ResolveHealthColor(clampedHealth);
    }

    private void EnsureWorldPanel()
    {
        if (root != null)
            return;

        GameObject rootObject = new GameObject("PurrVehicleWorldSpaceStatusUi");
        rootObject.transform.SetParent(transform, false);
        rootObject.transform.localPosition = Vector3.up * verticalOffset;
        rootObject.transform.localRotation = Quaternion.identity;
        root = rootObject.transform;

        GameObject panelObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
        panelObject.name = "Panel";
        panelObject.transform.SetParent(root, false);
        panelObject.transform.localPosition = Vector3.zero;
        panelObject.transform.localRotation = Quaternion.Euler(0.0f, 180.0f, 0.0f);
        panelObject.transform.localScale = new Vector3(worldPanelSize.x, worldPanelSize.y, 1.0f);

        Collider panelCollider = panelObject.GetComponent<Collider>();
        if (panelCollider != null)
            Destroy(panelCollider);

        panelRenderer = panelObject.GetComponent<MeshRenderer>();
        panelRenderer.shadowCastingMode = ShadowCastingMode.Off;
        panelRenderer.receiveShadows = false;

        renderTexture = new RenderTexture(textureWidth, textureHeight, 0, RenderTextureFormat.ARGB32)
        {
            name = $"{name}_StatusUi",
            useMipMap = false,
            autoGenerateMips = false
        };
        renderTexture.Create();

        panelMaterial = CreatePanelMaterial(renderTexture);
        panelRenderer.sharedMaterial = panelMaterial;

        runtimePanelSettings = CreateRuntimePanelSettings(renderTexture);
        uiDocument = panelObject.AddComponent<UIDocument>();
        uiDocument.panelSettings = runtimePanelSettings;
        uiDocument.sortingOrder = sortingOrder;
    }

    private void EnsureUiBuilt()
    {
        if (uiBuilt || uiDocument == null || uiDocument.rootVisualElement == null)
            return;

        BuildUi(uiDocument.rootVisualElement);
        uiBuilt = true;
    }

    private void BuildUi(VisualElement rootElement)
    {
        rootElement.Clear();
        rootElement.style.width = Length.Percent(100.0f);
        rootElement.style.height = Length.Percent(100.0f);
        rootElement.style.justifyContent = Justify.Center;
        rootElement.style.alignItems = Align.Stretch;
        rootElement.style.backgroundColor = Color.clear;
        rootElement.style.paddingLeft = 10;
        rootElement.style.paddingRight = 10;
        rootElement.style.paddingTop = 10;
        rootElement.style.paddingBottom = 10;

        VisualElement card = new VisualElement();
        card.style.flexGrow = 1.0f;
        card.style.paddingLeft = 12;
        card.style.paddingRight = 12;
        card.style.paddingTop = 10;
        card.style.paddingBottom = 10;
        card.style.backgroundColor = new Color(0.03f, 0.04f, 0.05f, 0.84f);
        card.style.borderTopWidth = 2;
        card.style.borderBottomWidth = 2;
        card.style.borderLeftWidth = 2;
        card.style.borderRightWidth = 2;
        card.style.borderTopColor = new Color(0.80f, 0.69f, 0.36f, 0.95f);
        card.style.borderBottomColor = new Color(0.80f, 0.69f, 0.36f, 0.95f);
        card.style.borderLeftColor = new Color(0.80f, 0.69f, 0.36f, 0.95f);
        card.style.borderRightColor = new Color(0.80f, 0.69f, 0.36f, 0.95f);
        card.style.borderTopLeftRadius = 12;
        card.style.borderTopRightRadius = 12;
        card.style.borderBottomLeftRadius = 12;
        card.style.borderBottomRightRadius = 12;
        rootElement.Add(card);

        nameLabel = CreateLabel(card, 28, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.98f, 0.98f, 0.98f, 1.0f));
        detailLabel = CreateLabel(card, 16, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.82f, 0.84f, 0.88f, 0.98f));
        detailLabel.style.marginTop = 2;

        VisualElement separator = new VisualElement();
        separator.style.height = 2;
        separator.style.marginTop = 8;
        separator.style.marginBottom = 8;
        separator.style.backgroundColor = new Color(0.26f, 0.29f, 0.33f, 0.95f);
        card.Add(separator);

        VisualElement hpRow = new VisualElement();
        hpRow.style.flexDirection = FlexDirection.Row;
        hpRow.style.alignItems = Align.Center;
        hpRow.style.justifyContent = Justify.SpaceBetween;
        hpRow.style.marginBottom = 6;
        card.Add(hpRow);

        Label hpTitleLabel = CreateLabel(hpRow, 15, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.96f, 0.97f, 0.99f, 1.0f));
        hpTitleLabel.text = "STRUCTURAL HP";

        hpValueLabel = CreateLabel(hpRow, 15, FontStyle.Bold, TextAnchor.MiddleRight, new Color(0.96f, 0.97f, 0.99f, 1.0f));

        VisualElement hpTrack = new VisualElement();
        hpTrack.style.height = 18;
        hpTrack.style.backgroundColor = new Color(0.10f, 0.11f, 0.13f, 0.98f);
        hpTrack.style.borderTopLeftRadius = 8;
        hpTrack.style.borderTopRightRadius = 8;
        hpTrack.style.borderBottomLeftRadius = 8;
        hpTrack.style.borderBottomRightRadius = 8;
        hpTrack.style.overflow = Overflow.Hidden;
        hpTrack.style.marginBottom = 6;
        card.Add(hpTrack);

        hpFill = new VisualElement();
        hpFill.style.height = Length.Percent(100.0f);
        hpFill.style.width = Length.Percent(100.0f);
        hpFill.style.backgroundColor = ResolveHealthColor(1.0f);
        hpTrack.Add(hpFill);

        telemetryLabel = CreateLabel(card, 14, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.76f, 0.80f, 0.86f, 0.98f));
    }

    private static Label CreateLabel(VisualElement parent, int size, FontStyle fontStyle, TextAnchor align, Color color)
    {
        Label label = new Label(string.Empty);
        label.style.fontSize = size;
        label.style.unityFontStyleAndWeight = fontStyle;
        label.style.unityTextAlign = align;
        label.style.color = color;
        parent.Add(label);
        return label;
    }

    private static PanelSettings CreateRuntimePanelSettings(RenderTexture targetTexture)
    {
        PanelSettings baseSettings = null;
        UIDocument[] documents = Resources.FindObjectsOfTypeAll<UIDocument>();
        for (int i = 0; i < documents.Length; i++)
        {
            UIDocument document = documents[i];
            if (document == null || document.panelSettings == null)
                continue;
            baseSettings = document.panelSettings;
            break;
        }

        if (baseSettings == null)
        {
            PanelSettings[] loadedSettings = Resources.FindObjectsOfTypeAll<PanelSettings>();
            if (loadedSettings != null && loadedSettings.Length > 0)
                baseSettings = loadedSettings[0];
        }

        PanelSettings runtimeSettings = baseSettings != null
            ? Instantiate(baseSettings)
            : ScriptableObject.CreateInstance<PanelSettings>();
        runtimeSettings.hideFlags = HideFlags.HideAndDontSave;
        runtimeSettings.name = "PurrVehicleStatusPanelSettings";
        runtimeSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
        runtimeSettings.clearColor = true;
        runtimeSettings.colorClearValue = new Color(0.0f, 0.0f, 0.0f, 0.0f);
        runtimeSettings.targetTexture = targetTexture;
        runtimeSettings.sortingOrder = 240;
        return runtimeSettings;
    }

    private static Material CreatePanelMaterial(Texture renderTexture)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Texture");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        Material material = new Material(shader)
        {
            name = "PurrVehicleStatusUiMaterial"
        };

        if (material.HasProperty("_BaseMap"))
            material.SetTexture("_BaseMap", renderTexture);
        if (material.HasProperty("_MainTex"))
            material.SetTexture("_MainTex", renderTexture);
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", Color.white);
        if (material.HasProperty("_Surface"))
            material.SetFloat("_Surface", 1.0f);
        if (material.HasProperty("_Blend"))
            material.SetFloat("_Blend", 0.0f);
        if (material.HasProperty("_SrcBlend"))
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend"))
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        if (material.HasProperty("_ZWrite"))
            material.SetFloat("_ZWrite", 0.0f);
        if (material.HasProperty("_Cull"))
            material.SetFloat("_Cull", (float)CullMode.Off);

        material.renderQueue = (int)RenderQueue.Transparent;
        return material;
    }

    private static Camera ResolveCamera()
    {
        if (Camera.main != null)
            return Camera.main;

        return FindFirstObjectByType<Camera>();
    }

    private static Color ResolveHealthColor(float healthNormalized)
    {
        if (healthNormalized >= 0.6f)
            return new Color(0.32f, 1.0f, 0.46f, 0.96f);
        if (healthNormalized >= 0.3f)
            return new Color(1.0f, 0.82f, 0.26f, 0.96f);
        return new Color(1.0f, 0.34f, 0.32f, 0.96f);
    }
}
