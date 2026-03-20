using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class TextureInvertTool
{
    [MenuItem("Tools/Textures/Invert Selected Maps (Roughness -> Smoothness)")]
    private static void InvertSelectedMaps()
    {
        string[] texturePaths = CollectSelectedTexturePaths();
        if (texturePaths.Length == 0)
        {
            Debug.LogWarning("TextureInvertTool: no textures selected.");
            return;
        }

        int converted = 0;
        for (int i = 0; i < texturePaths.Length; i++)
        {
            if (TryConvertTexture(texturePaths[i], out string createdPath))
            {
                converted++;
                Debug.Log($"TextureInvertTool: created {createdPath}");
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"TextureInvertTool: done. Converted {converted}/{texturePaths.Length} texture(s).");
    }

    [MenuItem("Tools/Textures/Invert Selected Maps (Roughness -> Smoothness)", true)]
    private static bool ValidateInvertSelectedMaps()
    {
        return CollectSelectedTexturePaths().Length > 0;
    }

    private static string[] CollectSelectedTexturePaths()
    {
        HashSet<string> result = new HashSet<string>();
        Object[] selected = Selection.objects;
        for (int i = 0; i < selected.Length; i++)
        {
            string path = AssetDatabase.GetAssetPath(selected[i]);
            if (string.IsNullOrEmpty(path))
                continue;

            if (AssetDatabase.IsValidFolder(path))
            {
                string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { path });
                for (int g = 0; g < guids.Length; g++)
                {
                    string texturePath = AssetDatabase.GUIDToAssetPath(guids[g]);
                    if (!string.IsNullOrEmpty(texturePath))
                        result.Add(texturePath);
                }
                continue;
            }

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture != null)
                result.Add(path);
        }

        string[] array = new string[result.Count];
        result.CopyTo(array);
        return array;
    }

    private static bool TryConvertTexture(string sourcePath, out string createdPath)
    {
        createdPath = null;
        TextureImporter sourceImporter = AssetImporter.GetAtPath(sourcePath) as TextureImporter;
        if (sourceImporter == null)
        {
            Debug.LogWarning($"TextureInvertTool: skipped {sourcePath} (not a texture importer).");
            return false;
        }

        bool wasReadable = sourceImporter.isReadable;
        try
        {
            if (!wasReadable)
            {
                sourceImporter.isReadable = true;
                sourceImporter.SaveAndReimport();
            }

            Texture2D sourceTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);
            if (sourceTexture == null)
            {
                Debug.LogWarning($"TextureInvertTool: failed to load {sourcePath}.");
                return false;
            }

            Color32[] pixels = sourceTexture.GetPixels32();
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 c = pixels[i];
                c.r = (byte)(255 - c.r);
                c.g = (byte)(255 - c.g);
                c.b = (byte)(255 - c.b);
                pixels[i] = c;
            }

            Texture2D outputTexture = new Texture2D(sourceTexture.width, sourceTexture.height, TextureFormat.RGBA32, false, true);
            outputTexture.SetPixels32(pixels);
            outputTexture.Apply(false, false);

            string directory = Path.GetDirectoryName(sourcePath)?.Replace("\\", "/") ?? "Assets";
            string baseName = Path.GetFileNameWithoutExtension(sourcePath);
            string outputAssetPath = AssetDatabase.GenerateUniqueAssetPath($"{directory}/{baseName}_smoothness.png");
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            string outputAbsolutePath = Path.Combine(projectRoot, outputAssetPath);

            byte[] pngBytes = outputTexture.EncodeToPNG();
            Object.DestroyImmediate(outputTexture);
            File.WriteAllBytes(outputAbsolutePath, pngBytes);

            AssetDatabase.ImportAsset(outputAssetPath, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter outputImporter = AssetImporter.GetAtPath(outputAssetPath) as TextureImporter;
            if (outputImporter != null)
            {
                TextureImporterSettings settings = new TextureImporterSettings();
                sourceImporter.ReadTextureSettings(settings);
                outputImporter.SetTextureSettings(settings);
                outputImporter.sRGBTexture = false;
                outputImporter.alphaSource = sourceImporter.alphaSource;
                outputImporter.alphaIsTransparency = sourceImporter.alphaIsTransparency;
                outputImporter.wrapMode = sourceImporter.wrapMode;
                outputImporter.filterMode = sourceImporter.filterMode;
                outputImporter.textureCompression = sourceImporter.textureCompression;
                outputImporter.mipmapEnabled = sourceImporter.mipmapEnabled;
                outputImporter.SaveAndReimport();
            }

            createdPath = outputAssetPath;
            return true;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"TextureInvertTool: failed for {sourcePath}\n{ex}");
            return false;
        }
        finally
        {
            if (!wasReadable && sourceImporter != null && sourceImporter.isReadable)
            {
                sourceImporter.isReadable = false;
                sourceImporter.SaveAndReimport();
            }
        }
    }
}
