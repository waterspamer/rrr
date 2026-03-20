using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Vehicle/Car Material Remap Profile", fileName = "CarMaterialRemapProfile")]
public sealed class CarMaterialRemapProfile : ScriptableObject
{
    [Serializable]
    public sealed class Rule
    {
        public string matchToken;
        public Material templateMaterial;
    }

    [SerializeField] private Material fallbackMaterial;
    [SerializeField] private List<Rule> rules = new List<Rule>();

    public Material ResolveTemplate(string sourceMaterialName)
    {
        if (string.IsNullOrWhiteSpace(sourceMaterialName))
            return fallbackMaterial;

        for (int i = 0; i < rules.Count; i++)
        {
            Rule rule = rules[i];
            if (rule == null || string.IsNullOrWhiteSpace(rule.matchToken) || rule.templateMaterial == null)
                continue;

            if (sourceMaterialName.IndexOf(rule.matchToken, StringComparison.OrdinalIgnoreCase) >= 0)
                return rule.templateMaterial;
        }

        return fallbackMaterial;
    }
}
