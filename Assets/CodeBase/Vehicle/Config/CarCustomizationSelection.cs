using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class CarCustomizationSelection
{
    public string selectorPath;
    public string variantName;

    public CarCustomizationSelection()
    {
    }

    public CarCustomizationSelection(string selectorPath, string variantName)
    {
        this.selectorPath = selectorPath;
        this.variantName = variantName;
    }
}

public static class CarCustomizationUtility
{
    public sealed class SelectorDefinition
    {
        public string SelectorPath;
        public string DisplayName;
        public List<string> VariantNames = new List<string>();
    }

    public static List<SelectorDefinition> DiscoverSelectors(GameObject bodyPrefab)
    {
        List<SelectorDefinition> selectors = new List<SelectorDefinition>();
        if (bodyPrefab == null)
            return selectors;

        Transform customsRoot = bodyPrefab.transform.Find("Customs");
        if (customsRoot == null)
            return selectors;

        CollectSelectorDefinitions(customsRoot, string.Empty, selectors);
        return selectors;
    }

    public static void ApplySelections(Transform bodyRoot, IReadOnlyList<CarCustomizationSelection> selections)
    {
        if (bodyRoot == null)
            return;

        Transform customsRoot = bodyRoot.Find("Customs");
        if (customsRoot == null)
            return;

        Dictionary<string, string> selectedVariants = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (selections != null)
        {
            for (int i = 0; i < selections.Count; i++)
            {
                CarCustomizationSelection selection = selections[i];
                if (selection == null ||
                    string.IsNullOrWhiteSpace(selection.selectorPath) ||
                    string.IsNullOrWhiteSpace(selection.variantName))
                    continue;

                selectedVariants[selection.selectorPath] = selection.variantName;
            }
        }

        ApplySelectionsRecursive(customsRoot, string.Empty, selectedVariants);
    }

    private static void CollectSelectorDefinitions(Transform node, string currentPath, List<SelectorDefinition> selectors)
    {
        if (node == null)
            return;

        if (IsSelectorRoot(node))
        {
            SelectorDefinition definition = new SelectorDefinition
            {
                SelectorPath = currentPath,
                DisplayName = currentPath.Replace('/', ' ')
            };

            for (int i = 0; i < node.childCount; i++)
                definition.VariantNames.Add(node.GetChild(i).name);

            selectors.Add(definition);
            return;
        }

        for (int i = 0; i < node.childCount; i++)
        {
            Transform child = node.GetChild(i);
            string childPath = string.IsNullOrEmpty(currentPath)
                ? child.name
                : $"{currentPath}/{child.name}";
            CollectSelectorDefinitions(child, childPath, selectors);
        }
    }

    private static void ApplySelectionsRecursive(Transform node, string currentPath, Dictionary<string, string> selectedVariants)
    {
        if (node == null)
            return;

        if (IsSelectorRoot(node))
        {
            string selectedVariantName = null;
            selectedVariants.TryGetValue(currentPath, out selectedVariantName);

            int selectedIndex = 0;
            if (!string.IsNullOrWhiteSpace(selectedVariantName))
            {
                for (int i = 0; i < node.childCount; i++)
                {
                    if (string.Equals(node.GetChild(i).name, selectedVariantName, StringComparison.OrdinalIgnoreCase))
                    {
                        selectedIndex = i;
                        break;
                    }
                }
            }

            for (int i = 0; i < node.childCount; i++)
                node.GetChild(i).gameObject.SetActive(i == selectedIndex);

            return;
        }

        for (int i = 0; i < node.childCount; i++)
        {
            Transform child = node.GetChild(i);
            string childPath = string.IsNullOrEmpty(currentPath)
                ? child.name
                : $"{currentPath}/{child.name}";
            ApplySelectionsRecursive(child, childPath, selectedVariants);
        }
    }

    private static bool IsSelectorRoot(Transform node)
    {
        if (node == null || node.childCount == 0)
            return false;

        for (int i = 0; i < node.childCount; i++)
        {
            string childName = node.GetChild(i).name;
            if (!string.Equals(childName, "Default", StringComparison.OrdinalIgnoreCase) &&
                !childName.StartsWith("Set", StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }
}
