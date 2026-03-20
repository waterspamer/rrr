using System;
using System.Collections.Generic;
using UnityEngine;

public static class CarLoadoutResolver
{
    public static CarLoadoutConfig Resolve(PlayerCarSelectionPayload payload, IEnumerable<CarLoadoutConfig> extraCandidates = null)
    {
        if (payload == null)
            return PlayerCarSelection.SelectedLoadout;

        List<CarLoadoutConfig> candidates = BuildCandidates(extraCandidates);
        CarLoadoutConfig best = null;
        int bestScore = int.MinValue;
        for (int i = 0; i < candidates.Count; i++)
        {
            CarLoadoutConfig candidate = candidates[i];
            if (candidate == null)
                continue;

            int score = ScoreCandidate(candidate, payload);
            if (score <= bestScore)
                continue;

            best = candidate;
            bestScore = score;
        }

        if (best != null)
            return best;

        return PlayerCarSelection.SelectedLoadout;
    }

    public static BodySetConfig ResolveBodySet(CarLoadoutConfig loadout, PlayerCarSelectionPayload payload, BodySetConfig fallback = null)
    {
        if (loadout == null)
            return fallback;

        string expectedName = payload != null ? payload.bodySetName : null;
        if (!string.IsNullOrWhiteSpace(expectedName) && loadout.BodySets != null)
        {
            for (int i = 0; i < loadout.BodySets.Count; i++)
            {
                BodySetConfig bodySet = loadout.BodySets[i];
                if (bodySet != null && string.Equals(bodySet.name, expectedName, StringComparison.OrdinalIgnoreCase))
                    return bodySet;
            }
        }

        int optionIndex = payload != null ? payload.bodySetOptionIndex : -1;
        if (loadout.BodySets == null || loadout.BodySets.Count == 0)
            return fallback;

        if (loadout.IncludeStockBodyOption)
        {
            if (optionIndex <= 0)
                return null;

            int bodySetIndex = optionIndex - 1;
            if (bodySetIndex >= 0 && bodySetIndex < loadout.BodySets.Count)
                return loadout.BodySets[bodySetIndex];
            return fallback;
        }

        if (optionIndex >= 0 && optionIndex < loadout.BodySets.Count)
            return loadout.BodySets[optionIndex];

        return fallback;
    }

    public static EngineGearboxConfig ResolveEngine(CarLoadoutConfig loadout, PlayerCarSelectionPayload payload, EngineGearboxConfig fallback = null)
    {
        if (loadout == null || loadout.EngineConfigs == null || loadout.EngineConfigs.Count == 0)
            return fallback;

        string expectedName = payload != null ? payload.engineName : null;
        if (!string.IsNullOrWhiteSpace(expectedName))
        {
            for (int i = 0; i < loadout.EngineConfigs.Count; i++)
            {
                EngineGearboxConfig engine = loadout.EngineConfigs[i];
                if (engine != null && string.Equals(engine.name, expectedName, StringComparison.OrdinalIgnoreCase))
                    return engine;
            }
        }

        int index = payload != null ? payload.engineIndex : -1;
        if (index >= 0 && index < loadout.EngineConfigs.Count)
            return loadout.EngineConfigs[index];

        return fallback;
    }

    public static SuspensionConfig ResolveSuspension(CarLoadoutConfig loadout, PlayerCarSelectionPayload payload, SuspensionConfig fallback = null)
    {
        if (loadout == null || loadout.SuspensionConfigs == null || loadout.SuspensionConfigs.Count == 0)
            return fallback;

        string expectedName = payload != null ? payload.suspensionName : null;
        if (!string.IsNullOrWhiteSpace(expectedName))
        {
            for (int i = 0; i < loadout.SuspensionConfigs.Count; i++)
            {
                SuspensionConfig suspension = loadout.SuspensionConfigs[i];
                if (suspension != null && string.Equals(suspension.name, expectedName, StringComparison.OrdinalIgnoreCase))
                    return suspension;
            }
        }

        int index = payload != null ? payload.suspensionIndex : -1;
        if (index >= 0 && index < loadout.SuspensionConfigs.Count)
            return loadout.SuspensionConfigs[index];

        return fallback;
    }

    public static PaintConfig ResolvePaint(CarLoadoutConfig loadout, PlayerCarSelectionPayload payload, PaintConfig fallback = null)
    {
        if (loadout == null || loadout.PaintOptions == null || loadout.PaintOptions.Count == 0)
            return fallback;

        string expectedName = payload != null ? payload.paintName : null;
        if (!string.IsNullOrWhiteSpace(expectedName))
        {
            for (int i = 0; i < loadout.PaintOptions.Count; i++)
            {
                PaintConfig paint = loadout.PaintOptions[i];
                if (paint == null)
                    continue;
                if (string.Equals(paint.name, expectedName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(paint.DisplayName, expectedName, StringComparison.OrdinalIgnoreCase))
                    return paint;
            }
        }

        int index = payload != null ? payload.paintIndex : -1;
        if (index >= 0 && index < loadout.PaintOptions.Count)
            return loadout.PaintOptions[index];

        return fallback;
    }

    private static List<CarLoadoutConfig> BuildCandidates(IEnumerable<CarLoadoutConfig> extraCandidates)
    {
        List<CarLoadoutConfig> candidates = new List<CarLoadoutConfig>();
        AddCandidate(candidates, PlayerCarSelection.SelectedLoadout);

        if (extraCandidates != null)
        {
            foreach (CarLoadoutConfig candidate in extraCandidates)
                AddCandidate(candidates, candidate);
        }

        CarLoadoutConfig[] resourceLoadouts = Resources.LoadAll<CarLoadoutConfig>("Vehicles");
        for (int i = 0; i < resourceLoadouts.Length; i++)
            AddCandidate(candidates, resourceLoadouts[i]);

        return candidates;
    }

    private static void AddCandidate(List<CarLoadoutConfig> candidates, CarLoadoutConfig candidate)
    {
        if (candidate == null || candidates.Contains(candidate))
            return;

        candidates.Add(candidate);
    }

    private static int ScoreCandidate(CarLoadoutConfig candidate, PlayerCarSelectionPayload payload)
    {
        int score = 0;
        if (!string.IsNullOrWhiteSpace(payload.loadoutName))
        {
            if (string.Equals(candidate.name, payload.loadoutName, StringComparison.OrdinalIgnoreCase))
                score += 1000;
            else
                score -= 500;
        }

        if (!string.IsNullOrWhiteSpace(payload.loadoutDisplayName))
        {
            if (string.Equals(candidate.DisplayName, payload.loadoutDisplayName, StringComparison.OrdinalIgnoreCase))
                score += 400;
            else if (string.Equals(candidate.name, payload.loadoutDisplayName, StringComparison.OrdinalIgnoreCase))
                score += 120;
        }

        if (!string.IsNullOrWhiteSpace(payload.handlingName) &&
            candidate.HandlingConfig != null &&
            string.Equals(candidate.HandlingConfig.name, payload.handlingName, StringComparison.OrdinalIgnoreCase))
        {
            score += 250;
        }

        score += ScoreBodySet(candidate, payload);
        score += ScoreEngine(candidate, payload);
        score += ScoreSuspension(candidate, payload);
        score += ScorePaint(candidate, payload);
        return score;
    }

    private static int ScoreBodySet(CarLoadoutConfig candidate, PlayerCarSelectionPayload payload)
    {
        if (candidate == null)
            return 0;

        if (!string.IsNullOrWhiteSpace(payload.bodySetName) && candidate.BodySets != null)
        {
            for (int i = 0; i < candidate.BodySets.Count; i++)
            {
                BodySetConfig bodySet = candidate.BodySets[i];
                if (bodySet != null && string.Equals(bodySet.name, payload.bodySetName, StringComparison.OrdinalIgnoreCase))
                    return 160;
            }
        }

        if (payload.bodySetOptionIndex < 0)
            return 20;

        int maxOptionIndex = candidate.IncludeStockBodyOption ? candidate.BodySets.Count : candidate.BodySets.Count - 1;
        return payload.bodySetOptionIndex <= maxOptionIndex ? 40 : -120;
    }

    private static int ScoreEngine(CarLoadoutConfig candidate, PlayerCarSelectionPayload payload)
    {
        if (candidate == null || candidate.EngineConfigs == null)
            return 0;

        if (!string.IsNullOrWhiteSpace(payload.engineName))
        {
            for (int i = 0; i < candidate.EngineConfigs.Count; i++)
            {
                EngineGearboxConfig engine = candidate.EngineConfigs[i];
                if (engine != null && string.Equals(engine.name, payload.engineName, StringComparison.OrdinalIgnoreCase))
                    return 220;
            }
        }

        if (payload.engineIndex >= 0)
            return payload.engineIndex < candidate.EngineConfigs.Count ? 80 : -180;

        return 0;
    }

    private static int ScoreSuspension(CarLoadoutConfig candidate, PlayerCarSelectionPayload payload)
    {
        if (candidate == null || candidate.SuspensionConfigs == null)
            return 0;

        if (!string.IsNullOrWhiteSpace(payload.suspensionName))
        {
            for (int i = 0; i < candidate.SuspensionConfigs.Count; i++)
            {
                SuspensionConfig suspension = candidate.SuspensionConfigs[i];
                if (suspension != null && string.Equals(suspension.name, payload.suspensionName, StringComparison.OrdinalIgnoreCase))
                    return 220;
            }
        }

        if (payload.suspensionIndex >= 0)
            return payload.suspensionIndex < candidate.SuspensionConfigs.Count ? 80 : -180;

        return 0;
    }

    private static int ScorePaint(CarLoadoutConfig candidate, PlayerCarSelectionPayload payload)
    {
        if (candidate == null || candidate.PaintOptions == null)
            return 0;

        if (!string.IsNullOrWhiteSpace(payload.paintName))
        {
            for (int i = 0; i < candidate.PaintOptions.Count; i++)
            {
                PaintConfig paint = candidate.PaintOptions[i];
                if (paint == null)
                    continue;
                if (string.Equals(paint.name, payload.paintName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(paint.DisplayName, payload.paintName, StringComparison.OrdinalIgnoreCase))
                    return 120;
            }
        }

        if (payload.paintIndex >= 0)
            return payload.paintIndex < candidate.PaintOptions.Count ? 40 : -80;

        return 0;
    }
}
