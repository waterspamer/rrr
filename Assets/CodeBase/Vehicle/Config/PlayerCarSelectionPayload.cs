using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class PlayerCarSelectionPayload
{
    public int version = 1;
    public string loadoutName;
    public string loadoutDisplayName;
    public int bodySetOptionIndex = -1;
    public int engineIndex = -1;
    public int suspensionIndex = -1;
    public int paintIndex = -1;
    public string bodySetName;
    public string engineName;
    public string suspensionName;
    public string paintName;
    public bool hasPaint;
    public SerializableColor paint;
    public List<PlayerCarCustomizationPayload> customizations = new List<PlayerCarCustomizationPayload>();

    public static PlayerCarSelectionPayload Create(
        CarLoadoutConfig loadout,
        int bodySetOptionIndex,
        BodySetConfig bodySet,
        int engineIndex,
        EngineGearboxConfig engine,
        int suspensionIndex,
        SuspensionConfig suspension,
        int paintIndex,
        PaintConfig paintConfig,
        Color paint,
        bool hasPaint,
        IReadOnlyList<CarCustomizationSelection> selections)
    {
        PlayerCarSelectionPayload payload = new PlayerCarSelectionPayload
        {
            loadoutName = loadout != null ? loadout.name : string.Empty,
            loadoutDisplayName = loadout != null ? loadout.DisplayName : string.Empty,
            bodySetOptionIndex = bodySetOptionIndex,
            engineIndex = engineIndex,
            suspensionIndex = suspensionIndex,
            paintIndex = paintIndex,
            bodySetName = bodySet != null ? bodySet.name : string.Empty,
            engineName = engine != null ? engine.name : string.Empty,
            suspensionName = suspension != null ? suspension.name : string.Empty,
            paintName = paintConfig != null ? paintConfig.DisplayName : string.Empty,
            hasPaint = hasPaint,
            paint = SerializableColor.FromColor(paint)
        };

        if (selections != null)
        {
            for (int i = 0; i < selections.Count; i++)
            {
                CarCustomizationSelection selection = selections[i];
                if (selection == null || string.IsNullOrWhiteSpace(selection.selectorPath))
                    continue;

                payload.customizations.Add(new PlayerCarCustomizationPayload
                {
                    selectorPath = selection.selectorPath,
                    variantName = selection.variantName
                });
            }
        }

        return payload;
    }
}

[Serializable]
public struct SerializableColor
{
    public float r;
    public float g;
    public float b;
    public float a;

    public static SerializableColor FromColor(Color color)
    {
        return new SerializableColor
        {
            r = color.r,
            g = color.g,
            b = color.b,
            a = color.a
        };
    }

    public Color ToColor()
    {
        return new Color(r, g, b, a);
    }
}

[Serializable]
public sealed class PlayerCarCustomizationPayload
{
    public string selectorPath;
    public string variantName;
}
