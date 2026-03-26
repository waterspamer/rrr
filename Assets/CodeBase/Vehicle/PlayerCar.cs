using UnityEngine;
using System.Collections.Generic;

[DefaultExecutionOrder(-500)]
[DisallowMultipleComponent]
public class PlayerCar : MonoBehaviour
{
    private static readonly int PaintPropertyId = Shader.PropertyToID("_MainColor");

    [SerializeField] private PlayerCarConfig config;
    [SerializeField] private VehicleSettings handlingConfig;
    [SerializeField] private EngineGearboxConfig engineGearboxConfig;
    [SerializeField] private SuspensionConfig suspensionConfig;
    [SerializeField] private BodySetConfig bodySetConfig;
    [SerializeField] private CarControllerBase controller;
    [SerializeField] private RiggedCarController riggedController;
    [SerializeField] private CarDamageController damageController;
    [SerializeField] private List<CarCustomizationSelection> customizationSelections = new List<CarCustomizationSelection>();
    private bool missingControllerWarningShown;
    private Color currentPaint = Color.white;
    private bool hasPaintOverride;
    private bool paintPending;

    public PlayerCarConfig Config => config;
    public CarControllerBase Controller => controller;
    public CarDamageController DamageController => damageController;
    public VehicleSettings HandlingConfig => handlingConfig;
    public EngineGearboxConfig EngineConfig => engineGearboxConfig;
    public SuspensionConfig SuspensionConfig => suspensionConfig;

    public void SetInitialLoadout(PlayerCarConfig carConfig, VehicleSettings handling, EngineGearboxConfig engineConfig, SuspensionConfig suspension)
    {
        if (carConfig != null)
            config = carConfig;
        if (handling != null)
            handlingConfig = handling;
        if (engineConfig != null)
            engineGearboxConfig = engineConfig;
        if (suspension != null)
            suspensionConfig = suspension;
        bodySetConfig = null;
    }

    private void Reset()
    {
        ResolveComponents();
        EnsureName();
    }

    private void Awake()
    {
        ResolveComponents();
        ApplyConfig();
        EnsureName();
    }

    private void OnValidate()
    {
        ResolveComponents();
        if (!Application.isPlaying)
            ApplyConfig();
        EnsureName();
    }

    private void ResolveComponents()
    {
        if (controller == null)
            controller = GetComponent<CarControllerBase>();
        if (riggedController == null)
            riggedController = GetComponent<RiggedCarController>();
        if (damageController == null)
            damageController = GetComponent<CarDamageController>();
    }

    private void ApplyConfig()
    {
        if (controller == null)
        {
            if (!missingControllerWarningShown)
            {
                Debug.LogWarning("PlayerCar: CarControllerBase not found. Add RiggedCarController (or another CarControllerBase).", this);
                missingControllerWarningShown = true;
            }
            return;
        }

        missingControllerWarningShown = false;

        if (controller != null)
        {
            controller.SetVehicleSettings(handlingConfig);
            controller.SetEngineGearboxSettings(engineGearboxConfig);
            controller.SetSuspensionSettings(suspensionConfig);
        }

        PlayerCarVisualSettings resolvedVisual = ResolveVisualSettings();

        if (riggedController != null)
        {
            if (resolvedVisual != null)
                riggedController.ApplyVisualSettings(resolvedVisual);
            riggedController.ApplyBodySetConfig(bodySetConfig);
            riggedController.ApplyCustomizationSelections(customizationSelections);
            riggedController.ApplySuspensionVisualSettings(suspensionConfig);
        }

        if (damageController != null && config != null)
            damageController.ApplyDamageSettings(config.Damage);

        if (resolvedVisual != null && resolvedVisual.useDefaultPaint && !hasPaintOverride)
            SetPaint(resolvedVisual.defaultPaint);
        else
            ApplyPaintDeferred();
    }

    public void OverrideDriveConfigs(EngineGearboxConfig engineConfig, SuspensionConfig suspension)
    {
        engineGearboxConfig = engineConfig;
        suspensionConfig = suspension;
        ApplyConfig();
    }

    public void OverrideLoadout(
        PlayerCarConfig carConfig,
        VehicleSettings handling,
        BodySetConfig bodySet,
        EngineGearboxConfig engineConfig,
        SuspensionConfig suspension,
        IReadOnlyList<CarCustomizationSelection> customizations = null)
    {
        if (carConfig != null)
            config = carConfig;
        if (handling != null)
            handlingConfig = handling;
        bodySetConfig = bodySet;
        customizationSelections = customizations != null
            ? new List<CarCustomizationSelection>(customizations)
            : new List<CarCustomizationSelection>();
        if (engineConfig != null)
            engineGearboxConfig = engineConfig;
        if (suspension != null)
            suspensionConfig = suspension;
        ApplyConfig();
    }

    public void SetPaint(Color paint)
    {
        currentPaint = paint;
        hasPaintOverride = true;
        ApplyPaintDeferred();
    }

    private void EnsureName()
    {
        if (gameObject.name != "PlayerCar")
            gameObject.name = "PlayerCar";
    }

    private void ApplyPaintDeferred()
    {
        if (paintPending)
            return;
        paintPending = true;
        StartCoroutine(ApplyPaintNextFrame());
    }

    private System.Collections.IEnumerator ApplyPaintNextFrame()
    {
        yield return null;
        yield return null;
        ApplyPaintNow();
        paintPending = false;
    }

    private void ApplyPaintNow()
    {
        if (config == null || config.Visual == null)
            return;

        PlayerCarVisualSettings visual = config.Visual;
        int propertyId = !string.IsNullOrWhiteSpace(visual.paintProperty)
            ? Shader.PropertyToID(visual.paintProperty)
            : PaintPropertyId;

        Renderer[] renderers = null;
        if (visual.paintRenderers != null && visual.paintRenderers.Length > 0)
            renderers = visual.paintRenderers;
        else if (visual.paintAllChildRenderers)
            renderers = GetComponentsInChildren<Renderer>(true);

        if (renderers == null || renderers.Length == 0)
            return;

        var block = new MaterialPropertyBlock();
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            bool hasProperty = false;
            Material[] materials = renderer.sharedMaterials;
            for (int m = 0; m < materials.Length; m++)
            {
                Material mat = materials[m];
                if (mat != null && mat.HasProperty(propertyId))
                {
                    hasProperty = true;
                    break;
                }
            }
            if (!hasProperty)
                continue;

            renderer.GetPropertyBlock(block);
            block.SetColor(propertyId, currentPaint);
            renderer.SetPropertyBlock(block);
        }
    }

    private PlayerCarVisualSettings ResolveVisualSettings()
    {
        if (config == null || config.Visual == null)
            return null;

        return CloneVisualSettings(config.Visual);
    }

    private static PlayerCarVisualSettings CloneVisualSettings(PlayerCarVisualSettings source)
    {
        if (source == null)
            return null;

        return new PlayerCarVisualSettings
        {
            bodyPrefab = source.bodyPrefab,
            wheelPrefab = source.wheelPrefab,
            addBodyCollider = source.addBodyCollider,
            generateConvexBodyColliders = source.generateConvexBodyColliders,
            wheelBase = source.wheelBase,
            axleWidth = source.axleWidth,
            zOffset = source.zOffset,
            wheelHeight = source.wheelHeight,
            bodyRootHeightFactor = source.bodyRootHeightFactor,
            liveWheelPositions = source.liveWheelPositions,
            useDefaultPaint = source.useDefaultPaint,
            defaultPaint = source.defaultPaint,
            paintProperty = source.paintProperty,
            paintAllChildRenderers = source.paintAllChildRenderers,
            paintRenderers = source.paintRenderers
        };
    }
}
