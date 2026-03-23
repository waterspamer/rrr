using System.Collections.Generic;
using UnityEngine;

public static class PlayerCarLoadoutUtility
{
    public static CarLoadoutConfig ApplySelectedLoadout(PlayerCar playerCar, PlayerCarSelectionPayload payload)
    {
        if (playerCar == null)
            return null;

        CarLoadoutConfig loadout = ResolveLoadout(payload);
        PlayerCarConfig carConfig = loadout != null && loadout.PlayerCarConfig != null
            ? loadout.PlayerCarConfig
            : PlayerCarSelection.SelectedCarConfig;
        VehicleSettings handling = ResolveHandling(loadout, payload);
        BodySetConfig bodySet = ResolveBodySet(loadout, payload);
        EngineGearboxConfig engine = ResolveEngine(loadout, payload);
        SuspensionConfig suspension = ResolveSuspension(loadout, payload);

        playerCar.OverrideLoadout(
            carConfig,
            handling,
            bodySet,
            engine,
            suspension,
            ResolveCustomizations(payload));

        if (TryResolvePaint(loadout, payload, out Color paint))
            playerCar.SetPaint(paint);

        return loadout;
    }

    public static CarLoadoutConfig ResolveLoadout(PlayerCarSelectionPayload payload)
    {
        return CarLoadoutResolver.Resolve(payload);
    }

    public static VehicleSettings ResolveHandling(CarLoadoutConfig loadout, PlayerCarSelectionPayload payload)
    {
        if (loadout != null && loadout.HandlingConfig != null)
        {
            if (payload == null || string.IsNullOrWhiteSpace(payload.handlingName) ||
                string.Equals(loadout.HandlingConfig.name, payload.handlingName, System.StringComparison.OrdinalIgnoreCase))
            {
                return loadout.HandlingConfig;
            }
        }

        if (PlayerCarSelection.SelectedHandling != null &&
            (payload == null || string.IsNullOrWhiteSpace(payload.handlingName) ||
             string.Equals(PlayerCarSelection.SelectedHandling.name, payload.handlingName, System.StringComparison.OrdinalIgnoreCase)))
        {
            return PlayerCarSelection.SelectedHandling;
        }

        return loadout != null ? loadout.HandlingConfig : PlayerCarSelection.SelectedHandling;
    }

    public static BodySetConfig ResolveBodySet(CarLoadoutConfig loadout, PlayerCarSelectionPayload payload)
    {
        return CarLoadoutResolver.ResolveBodySet(loadout, payload, PlayerCarSelection.SelectedBodySet);
    }

    public static EngineGearboxConfig ResolveEngine(CarLoadoutConfig loadout, PlayerCarSelectionPayload payload)
    {
        return CarLoadoutResolver.ResolveEngine(loadout, payload, PlayerCarSelection.SelectedEngine);
    }

    public static SuspensionConfig ResolveSuspension(CarLoadoutConfig loadout, PlayerCarSelectionPayload payload)
    {
        return CarLoadoutResolver.ResolveSuspension(loadout, payload, PlayerCarSelection.SelectedSuspension);
    }

    public static List<CarCustomizationSelection> ResolveCustomizations(PlayerCarSelectionPayload payload)
    {
        if (payload == null || payload.customizations == null || payload.customizations.Count == 0)
            return PlayerCarSelection.SelectedCustomizations;

        List<CarCustomizationSelection> resolved = new List<CarCustomizationSelection>(payload.customizations.Count);
        for (int i = 0; i < payload.customizations.Count; i++)
        {
            PlayerCarCustomizationPayload customization = payload.customizations[i];
            if (customization == null || string.IsNullOrWhiteSpace(customization.selectorPath))
                continue;

            resolved.Add(new CarCustomizationSelection(customization.selectorPath, customization.variantName));
        }

        return resolved;
    }

    public static bool TryResolvePaint(CarLoadoutConfig loadout, PlayerCarSelectionPayload payload, out Color paint)
    {
        paint = PlayerCarSelection.SelectedPaint;
        if (payload == null)
            return PlayerCarSelection.HasPaint;

        if (loadout != null && loadout.PaintOptions != null && payload.paintIndex >= 0 && payload.paintIndex < loadout.PaintOptions.Count)
        {
            PaintConfig config = CarLoadoutResolver.ResolvePaint(loadout, payload, loadout.PaintOptions[payload.paintIndex]);
            if (config != null)
            {
                paint = config.Color;
                return true;
            }
        }

        if (payload.hasPaint)
        {
            paint = payload.paint.ToColor();
            return true;
        }

        return PlayerCarSelection.HasPaint;
    }
}
