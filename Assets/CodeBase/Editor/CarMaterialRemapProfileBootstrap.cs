using System.IO;
using UnityEditor;
using UnityEngine;

internal static class CarMaterialRemapProfileBootstrap
{
    private const string MaterialsFolder = "Assets/Art/CarMaterials";
    private const string ProfilePath = MaterialsFolder + "/DefaultCarMaterialRemapProfile.asset";

    [InitializeOnLoadMethod]
    private static void EnsureDefaultProfile()
    {
        EditorApplication.delayCall += CreateProfileIfMissing;
    }

    private static void CreateProfileIfMissing()
    {
        if (AssetDatabase.LoadAssetAtPath<CarMaterialRemapProfile>(ProfilePath) != null)
            return;

        if (!AssetDatabase.IsValidFolder("Assets/Art"))
            AssetDatabase.CreateFolder("Assets", "Art");
        if (!AssetDatabase.IsValidFolder(MaterialsFolder))
            AssetDatabase.CreateFolder("Assets/Art", "CarMaterials");

        CarMaterialRemapProfile profile = ScriptableObject.CreateInstance<CarMaterialRemapProfile>();
        SerializedObject serializedObject = new SerializedObject(profile);
        serializedObject.FindProperty("fallbackMaterial").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<Material>(MaterialsFolder + "/MetallicRough.mat");

        SerializedProperty rules = serializedObject.FindProperty("rules");
        AddRule(rules, "CarPaint", MaterialsFolder + "/CarPaint.mat");
        AddRule(rules, "Glass", MaterialsFolder + "/Glass.mat");
        AddRule(rules, "PlasticSmooth", MaterialsFolder + "/PlasticSmooth.mat");
        AddRule(rules, "RubberSmooth", MaterialsFolder + "/PlasticSmooth.mat");
        AddRule(rules, "PlasticRough", MaterialsFolder + "/PlasticRough.mat");
        AddRule(rules, "RubberRough", MaterialsFolder + "/PlasticRough.mat");
        AddRule(rules, "Chassis", MaterialsFolder + "/MetallicRough.mat");
        AddRule(rules, "Interior", MaterialsFolder + "/MetallicRough.mat");
        AddRule(rules, "Engine", MaterialsFolder + "/MetallicRough.mat");
        AddRule(rules, "ChromeSmooth", MaterialsFolder + "/MetallicSmooth.mat");
        AddRule(rules, "NickelSmooth", MaterialsFolder + "/MetallicSmooth.mat");
        AddRule(rules, "NickelRough", MaterialsFolder + "/MetallicRough.mat");
        AddRule(rules, "Badge", MaterialsFolder + "/MetallicSmooth.mat");
        AddRule(rules, "Rim", MaterialsFolder + "/MetallicSmooth.mat");
        AddRule(rules, "Grille", MaterialsFolder + "/MetallicSmooth.mat");
        AddRule(rules, "LicensePlate", MaterialsFolder + "/MetallicSmooth.mat");
        AddRule(rules, "LightBucket", MaterialsFolder + "/MetallicSmooth.mat");
        AddRule(rules, "GenericDetail", MaterialsFolder + "/MetallicSmooth.mat");
        AddRule(rules, "PaintSolidBlack", MaterialsFolder + "/PlasticSmooth.mat");
        serializedObject.ApplyModifiedPropertiesWithoutUndo();

        AssetDatabase.CreateAsset(profile, ProfilePath);
        AssetDatabase.SaveAssets();
    }

    private static void AddRule(SerializedProperty rules, string token, string materialPath)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
            return;

        int index = rules.arraySize;
        rules.InsertArrayElementAtIndex(index);
        SerializedProperty entry = rules.GetArrayElementAtIndex(index);
        entry.FindPropertyRelative("matchToken").stringValue = token;
        entry.FindPropertyRelative("templateMaterial").objectReferenceValue = material;
    }
}
