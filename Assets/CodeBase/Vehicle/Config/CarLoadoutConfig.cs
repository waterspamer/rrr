using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Vehicles/Car Loadout", fileName = "CarLoadout")]
public class CarLoadoutConfig : ScriptableObject
{
    [Header("UI")]
    [SerializeField] private Sprite icon;

    [SerializeField] private string displayName = "Car";
    [SerializeField] private PlayerCarConfig playerCarConfig;
    [SerializeField] private VehicleSettings handlingConfig;
    [SerializeField] private bool includeStockBodyOption;
    [SerializeField] private List<BodySetConfig> bodySets = new List<BodySetConfig>();
    [SerializeField] private List<EngineGearboxConfig> engineConfigs = new List<EngineGearboxConfig>();
    [SerializeField] private List<SuspensionConfig> suspensionConfigs = new List<SuspensionConfig>();
    [SerializeField] private List<PaintConfig> paintOptions = new List<PaintConfig>();
    [SerializeField] private int defaultBodySetIndex;
    [SerializeField, Min(0)] private int defaultEngineIndex;
    [SerializeField, Min(0)] private int defaultSuspensionIndex;
    [SerializeField, Min(0)] private int defaultPaintIndex;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public Sprite Icon => icon;
    public PlayerCarConfig PlayerCarConfig => playerCarConfig;
    public VehicleSettings HandlingConfig => handlingConfig;
    public bool IncludeStockBodyOption => includeStockBodyOption;
    public List<BodySetConfig> BodySets => bodySets;
    public int DefaultBodySetIndex => defaultBodySetIndex;
    public List<EngineGearboxConfig> EngineConfigs => engineConfigs;
    public List<SuspensionConfig> SuspensionConfigs => suspensionConfigs;
    public List<PaintConfig> PaintOptions => paintOptions;
    public int DefaultEngineIndex => defaultEngineIndex;
    public int DefaultSuspensionIndex => defaultSuspensionIndex;
    public int DefaultPaintIndex => defaultPaintIndex;

    private void OnValidate()
    {
        defaultBodySetIndex = includeStockBodyOption ? Mathf.Max(-1, defaultBodySetIndex) : Mathf.Max(0, defaultBodySetIndex);
        defaultEngineIndex = Mathf.Max(0, defaultEngineIndex);
        defaultSuspensionIndex = Mathf.Max(0, defaultSuspensionIndex);
        defaultPaintIndex = Mathf.Max(0, defaultPaintIndex);
    }
}
