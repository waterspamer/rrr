using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class PurrPlayerMetadataPair
{
    public string key;
    public string value;

    public PurrPlayerMetadataPair Clone()
    {
        return new PurrPlayerMetadataPair
        {
            key = key,
            value = value
        };
    }
}

[Serializable]
public sealed class PurrPlayerDataSection
{
    public string sectionId;
    public string title;
    public string contentType = "application/json";
    public string payloadJson;

    public PurrPlayerDataSection Clone()
    {
        return new PurrPlayerDataSection
        {
            sectionId = sectionId,
            title = title,
            contentType = contentType,
            payloadJson = payloadJson
        };
    }
}

[Serializable]
public sealed class PurrPlayerDataBag
{
    public int version = 1;
    public long updatedAtUnixMs;
    public List<PurrPlayerDataSection> sections = new List<PurrPlayerDataSection>();

    public void SetSectionJson(string sectionId, string title, string payloadJson, string contentType = "application/json")
    {
        if (string.IsNullOrWhiteSpace(sectionId))
            return;

        PurrPlayerDataSection section = FindSection(sectionId);
        if (section == null)
        {
            section = new PurrPlayerDataSection
            {
                sectionId = sectionId
            };
            sections.Add(section);
        }

        section.title = string.IsNullOrWhiteSpace(title) ? sectionId : title;
        section.payloadJson = payloadJson ?? string.Empty;
        section.contentType = string.IsNullOrWhiteSpace(contentType) ? "application/json" : contentType;
        updatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    public bool TryGetSectionJson(string sectionId, out string payloadJson)
    {
        payloadJson = string.Empty;
        PurrPlayerDataSection section = FindSection(sectionId);
        if (section == null || string.IsNullOrWhiteSpace(section.payloadJson))
            return false;

        payloadJson = section.payloadJson;
        return true;
    }

    public PurrPlayerDataBag Clone()
    {
        PurrPlayerDataBag clone = new PurrPlayerDataBag
        {
            version = version,
            updatedAtUnixMs = updatedAtUnixMs
        };

        for (int i = 0; i < sections.Count; i++)
        {
            PurrPlayerDataSection section = sections[i];
            if (section != null)
                clone.sections.Add(section.Clone());
        }

        return clone;
    }

    private PurrPlayerDataSection FindSection(string sectionId)
    {
        if (string.IsNullOrWhiteSpace(sectionId))
            return null;

        for (int i = 0; i < sections.Count; i++)
        {
            PurrPlayerDataSection section = sections[i];
            if (section == null)
                continue;
            if (string.Equals(section.sectionId, sectionId, StringComparison.Ordinal))
                return section;
        }

        return null;
    }
}

[Serializable]
public sealed class PurrPlayerGaragePublicSummary
{
    public int version = 1;
    public string accountId;
    public string playerId;
    public string displayName;
    public int balanceSoft;
    public int balancePremium;
    public int level = 1;
    public int experience;
    public string selectedCarId;
    public string selectedCarDisplayName;
    public int ownedCarCount;
    public List<string> ownedCarIds = new List<string>();
    public List<PurrPlayerMetadataPair> publicFlags = new List<PurrPlayerMetadataPair>();
    public string source;
}

public static class PurrPlayerPublicDataFactory
{
    public const string GarageSummarySectionId = "garage_public_summary";

    public static PurrPlayerDataBag BuildFromBackendProfile(BackendPlayerProfile backendProfile, PlayerCarSelectionPayload loadout = null)
    {
        if (backendProfile == null)
            return BuildFallback(string.Empty, string.Empty, loadout);

        BackendPlayerGarage garage = backendProfile.garage;
        PurrPlayerGaragePublicSummary summary = new PurrPlayerGaragePublicSummary
        {
            accountId = backendProfile.account_id,
            playerId = backendProfile.player_id,
            displayName = backendProfile.display_name,
            balanceSoft = backendProfile.balance != null ? backendProfile.balance.soft : 0,
            balancePremium = backendProfile.balance != null ? backendProfile.balance.premium : 0,
            level = backendProfile.progression != null ? Mathf.Max(1, backendProfile.progression.level) : 1,
            experience = backendProfile.progression != null ? backendProfile.progression.experience : 0,
            selectedCarId = garage != null && !string.IsNullOrWhiteSpace(garage.selected_car_id)
                ? garage.selected_car_id
                : ResolveLoadoutId(loadout),
            selectedCarDisplayName = garage != null && !string.IsNullOrWhiteSpace(garage.selected_car_display_name)
                ? garage.selected_car_display_name
                : ResolveLoadoutDisplayName(loadout),
            ownedCarCount = garage != null ? Mathf.Max(garage.owned_car_count, 0) : 0,
            source = "backend_profile"
        };

        if (garage != null && garage.owned_cars != null)
        {
            for (int i = 0; i < garage.owned_cars.Count; i++)
            {
                BackendPlayerGarageCar ownedCar = garage.owned_cars[i];
                if (ownedCar == null || string.IsNullOrWhiteSpace(ownedCar.car_id))
                    continue;
                summary.ownedCarIds.Add(ownedCar.car_id);
            }
        }

        if (backendProfile.public_flags != null)
        {
            for (int i = 0; i < backendProfile.public_flags.Count; i++)
            {
                BackendMetadataEntry entry = backendProfile.public_flags[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.key))
                    continue;
                summary.publicFlags.Add(new PurrPlayerMetadataPair
                {
                    key = entry.key,
                    value = entry.value
                });
            }
        }

        if (summary.ownedCarCount <= 0)
            summary.ownedCarCount = summary.ownedCarIds.Count;

        return BuildBag(summary);
    }

    public static PurrPlayerDataBag BuildFallback(string accountId, string displayName, PlayerCarSelectionPayload loadout = null)
    {
        string resolvedDisplayName = string.IsNullOrWhiteSpace(displayName) ? "Guest" : displayName.Trim();
        string selectedCarId = ResolveLoadoutId(loadout);
        string selectedCarDisplayName = ResolveLoadoutDisplayName(loadout);

        PurrPlayerGaragePublicSummary summary = new PurrPlayerGaragePublicSummary
        {
            accountId = string.IsNullOrWhiteSpace(accountId) ? "guest_local" : accountId,
            playerId = string.Empty,
            displayName = resolvedDisplayName,
            balanceSoft = 0,
            balancePremium = 0,
            level = 1,
            experience = 0,
            selectedCarId = selectedCarId,
            selectedCarDisplayName = selectedCarDisplayName,
            ownedCarCount = 1,
            source = "local_placeholder"
        };
        if (!string.IsNullOrWhiteSpace(selectedCarId))
            summary.ownedCarIds.Add(selectedCarId);

        return BuildBag(summary);
    }

    private static PurrPlayerDataBag BuildBag(PurrPlayerGaragePublicSummary summary)
    {
        PurrPlayerDataBag bag = new PurrPlayerDataBag();
        bag.SetSectionJson(GarageSummarySectionId, "Garage Public Summary", JsonUtility.ToJson(summary));
        return bag;
    }

    private static string ResolveLoadoutId(PlayerCarSelectionPayload loadout)
    {
        if (loadout != null)
        {
            if (!string.IsNullOrWhiteSpace(loadout.loadoutName))
                return loadout.loadoutName;
            if (!string.IsNullOrWhiteSpace(loadout.loadoutDisplayName))
                return loadout.loadoutDisplayName;
        }

        return "cooper";
    }

    private static string ResolveLoadoutDisplayName(PlayerCarSelectionPayload loadout)
    {
        if (loadout != null)
        {
            if (!string.IsNullOrWhiteSpace(loadout.loadoutDisplayName))
                return loadout.loadoutDisplayName;
            if (!string.IsNullOrWhiteSpace(loadout.loadoutName))
                return loadout.loadoutName;
        }

        return "Cooper";
    }
}
