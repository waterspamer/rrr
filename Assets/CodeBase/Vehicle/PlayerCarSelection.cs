using UnityEngine;

public static class PlayerCarSelection
{
    public static PlayerCarConfig SelectedCarConfig;
    public static VehicleSettings SelectedHandling;
    public static EngineGearboxConfig SelectedEngine;
    public static SuspensionConfig SelectedSuspension;
    public static Color SelectedPaint;
    public static bool HasPaint;

    public static void Set(PlayerCarConfig carConfig, VehicleSettings handling, EngineGearboxConfig engine, SuspensionConfig suspension, Color paint, bool hasPaint)
    {
        SelectedCarConfig = carConfig;
        SelectedHandling = handling;
        SelectedEngine = engine;
        SelectedSuspension = suspension;
        SelectedPaint = paint;
        HasPaint = hasPaint;
    }
}
