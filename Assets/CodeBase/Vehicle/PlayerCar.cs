using UnityEngine;

[DefaultExecutionOrder(-500)]
[DisallowMultipleComponent]
public class PlayerCar : MonoBehaviour
{
    private static readonly int PaintPropertyId = Shader.PropertyToID("_MainColor");

    [SerializeField] private PlayerCarConfig config;
    [SerializeField] private VehicleSettings handlingConfig;
    [SerializeField] private EngineGearboxConfig engineGearboxConfig;
    [SerializeField] private SuspensionConfig suspensionConfig;
    [SerializeField] private CarControllerBase controller;
    [SerializeField] private RiggedCarController riggedController;
    [SerializeField] private CarDamageController damageController;
    private bool missingControllerWarningShown;
    private Color currentPaint = Color.white;
    private bool hasPaintOverride;
    private bool paintPending;

    public PlayerCarConfig Config => config;

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

        if (riggedController != null)
        {
            if (config != null)
                riggedController.ApplyVisualSettings(config.Visual);
            riggedController.ApplySuspensionVisualSettings(suspensionConfig);
        }

        if (damageController != null && config != null)
            damageController.ApplyDamageSettings(config.Damage);

        if (config != null && config.Visual != null && config.Visual.useDefaultPaint && !hasPaintOverride)
            SetPaint(config.Visual.defaultPaint);
        else
            ApplyPaintDeferred();
    }

    public void OverrideDriveConfigs(EngineGearboxConfig engineConfig, SuspensionConfig suspension)
    {
        engineGearboxConfig = engineConfig;
        suspensionConfig = suspension;
        ApplyConfig();
    }

    public void OverrideLoadout(PlayerCarConfig carConfig, VehicleSettings handling, EngineGearboxConfig engineConfig, SuspensionConfig suspension)
    {
        if (carConfig != null)
            config = carConfig;
        if (handling != null)
            handlingConfig = handling;
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
}
