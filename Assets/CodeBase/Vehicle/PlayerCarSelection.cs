using UnityEngine;
using System.Collections.Generic;

public static class PlayerCarSelection
{
    public static CarLoadoutConfig SelectedLoadout;
    public static PlayerCarConfig SelectedCarConfig;
    public static VehicleSettings SelectedHandling;
    public static BodySetConfig SelectedBodySet;
    public static EngineGearboxConfig SelectedEngine;
    public static SuspensionConfig SelectedSuspension;
    public static Color SelectedPaint;
    public static bool HasPaint;
    public static List<CarCustomizationSelection> SelectedCustomizations = new List<CarCustomizationSelection>();
    public static string SelectionJson;

    public static void Set(
        CarLoadoutConfig loadout,
        PlayerCarConfig carConfig,
        VehicleSettings handling,
        BodySetConfig bodySet,
        int bodySetOptionIndex,
        EngineGearboxConfig engine,
        int engineIndex,
        SuspensionConfig suspension,
        int suspensionIndex,
        PaintConfig paintConfig,
        int paintIndex,
        Color paint,
        bool hasPaint,
        IReadOnlyList<CarCustomizationSelection> customizations)
    {
        SelectedLoadout = loadout;
        SelectedCarConfig = carConfig;
        SelectedHandling = handling;
        SelectedBodySet = bodySet;
        SelectedEngine = engine;
        SelectedSuspension = suspension;
        SelectedPaint = paint;
        HasPaint = hasPaint;
        SelectedCustomizations = customizations != null
            ? new List<CarCustomizationSelection>(customizations)
            : new List<CarCustomizationSelection>();

        PlayerCarSelectionPayload payload = PlayerCarSelectionPayload.Create(
            loadout,
            bodySetOptionIndex,
            bodySet,
            engineIndex,
            engine,
            suspensionIndex,
            suspension,
            paintIndex,
            paintConfig,
            paint,
            hasPaint,
            SelectedCustomizations);

        SelectionJson = JsonUtility.ToJson(payload);
    }

    public static bool TryGetPayload(out PlayerCarSelectionPayload payload)
    {
        payload = null;
        if (string.IsNullOrWhiteSpace(SelectionJson))
            return false;

        try
        {
            payload = JsonUtility.FromJson<PlayerCarSelectionPayload>(SelectionJson);
        }
        catch
        {
            payload = null;
        }

        return payload != null;
    }
}
