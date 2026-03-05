using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;
using System.Collections.Generic;

public class AutoAssignTextures : EditorWindow
{
    [MenuItem("Tools/Auto Assign Building Textures")]
    static void Init()
    {
        AutoAssignTextures window = GetWindow<AutoAssignTextures>();
        window.Show();
    }
    
    [MenuItem("Tools/Auto Assign Building Textures (Run Now)")]
    static void RunNow()
    {
        // Сначала извлечь материалы из модели, если они встроены
        ExtractMaterialsIfNeeded();
        // Затем назначить текстуры
        AssignTextures();
    }
    
    static void ExtractMaterialsIfNeeded()
    {
        // Найти модель city_building
        string[] modelGuids = AssetDatabase.FindAssets("city_building t:GameObject");
        if (modelGuids.Length == 0)
        {
            Debug.LogError("city_building model not found!");
            return;
        }
        
        string modelPath = AssetDatabase.GUIDToAssetPath(modelGuids[0]);
        ModelImporter importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
        
        if (importer == null)
        {
            Debug.LogError("Could not get ModelImporter for city_building!");
            return;
        }
        
        // Проверить, встроены ли материалы
        if (importer.materialLocation == ModelImporterMaterialLocation.InPrefab)
        {
            Debug.Log("Materials are embedded. Extracting materials...");
            
            // Изменить настройки импорта
            importer.materialLocation = ModelImporterMaterialLocation.External;
            importer.materialName = ModelImporterMaterialName.BasedOnMaterialName;
            importer.materialSearch = ModelImporterMaterialSearch.Everywhere;
            
            // Применить изменения
            AssetDatabase.ImportAsset(modelPath, ImportAssetOptions.ForceUpdate);
            
            // Подождать, чтобы Unity создал материалы
            AssetDatabase.Refresh();
            System.Threading.Thread.Sleep(500); // Небольшая задержка
            
            Debug.Log("Materials extracted! Now assigning textures...");
        }
        else
        {
            Debug.Log("Materials are already external. Proceeding to assign textures...");
        }
    }

    void OnGUI()
    {
        GUILayout.Label("Auto Assign Textures to Building Materials", EditorStyles.boldLabel);
        
        if (GUILayout.Button("Assign Textures to ALL Materials"))
        {
            AssignTextures();
        }
    }

    static void AssignTextures()
    {
        // Найти модель city_building
        string[] modelGuids = AssetDatabase.FindAssets("city_building t:GameObject");
        if (modelGuids.Length == 0)
        {
            Debug.LogError("city_building model not found!");
            return;
        }
        
        // Найти все материалы, которые используются объектами city_building в сцене
        HashSet<Material> cityBuildingMaterials = new HashSet<Material>();
        Dictionary<Material, string> materialToObjectName = new Dictionary<Material, string>();
        Dictionary<Material, List<MeshRenderer>> materialToRenderers = new Dictionary<Material, List<MeshRenderer>>();
        
        // Найти все объекты city_building в открытых сценах
        foreach (GameObject obj in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (obj.name.Contains("city_building") && obj.scene.isLoaded)
            {
                MeshRenderer[] renderers = obj.GetComponentsInChildren<MeshRenderer>(true);
                foreach (MeshRenderer renderer in renderers)
                {
                    for (int i = 0; i < renderer.sharedMaterials.Length; i++)
                    {
                        Material mat = renderer.sharedMaterials[i];
                        if (mat != null)
                        {
                            // Пропускаем материалы из пакетов Unity
                            string matPath = AssetDatabase.GetAssetPath(mat);
                            if (matPath != null && matPath.StartsWith("Packages/"))
                            {
                                continue;
                            }
                            
                            cityBuildingMaterials.Add(mat);
                            if (!materialToObjectName.ContainsKey(mat))
                            {
                                materialToObjectName[mat] = renderer.gameObject.name.ToLower();
                            }
                            
                            if (!materialToRenderers.ContainsKey(mat))
                            {
                                materialToRenderers[mat] = new List<MeshRenderer>();
                            }
                            materialToRenderers[mat].Add(renderer);
                        }
                    }
                }
            }
        }
        
        // Если не нашли в сцене, ищем материалы по названию (Red_Build, depot и т.д.)
        // ИЛИ материалы, которые находятся рядом с моделью (после извлечения)
        if (cityBuildingMaterials.Count == 0)
        {
            // Сначала попробуем найти материалы рядом с моделью
            string modelPath = AssetDatabase.GUIDToAssetPath(modelGuids[0]);
            string modelDir = Path.GetDirectoryName(modelPath);
            string materialsDir = Path.Combine(modelDir, "Materials");
            
            if (Directory.Exists(materialsDir))
            {
                string[] materialPaths = Directory.GetFiles(materialsDir, "*.mat");
                foreach (string matPath in materialPaths)
                {
                    Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                    if (mat != null)
                    {
                        cityBuildingMaterials.Add(mat);
                        Debug.Log($"Found extracted material: {mat.name} at {matPath}");
                    }
                }
            }
            
            // Если всё ещё не нашли, ищем по всему проекту
            if (cityBuildingMaterials.Count == 0)
            {
                Material[] allMaterials = Resources.FindObjectsOfTypeAll<Material>();
                foreach (Material mat in allMaterials)
                {
                    string matPath = AssetDatabase.GetAssetPath(mat);
                    // Пропускаем материалы из Packages
                    if (matPath != null && matPath.StartsWith("Packages/"))
                        continue;
                        
                    string matName = mat.name.ToLower();
                    if (matName.Contains("red_build") || matName.Contains("red_building") || 
                        matName.Contains("depot") || matName.Contains("city_building") ||
                        matName == "metal" || matName == "concrete" || matName == "misc" ||
                        matName.Contains("sidewalk") || matName == "lit")
                    {
                        cityBuildingMaterials.Add(mat);
                        if (!materialToObjectName.ContainsKey(mat))
                        {
                            materialToObjectName[mat] = "found_by_name";
                        }
                    }
                }
            }
        }
        
        if (cityBuildingMaterials.Count == 0)
        {
            Debug.LogWarning("No materials found for city_building model!");
            return;
        }
        
        // Вывести список всех материалов для отладки
        Debug.Log("=== Found Materials ===");
        foreach (Material mat in cityBuildingMaterials)
        {
            string objName = materialToObjectName.ContainsKey(mat) ? materialToObjectName[mat] : "unknown";
            Debug.Log($"Material: {mat.name} | Object: {objName}");
        }
        
        // Найти все текстуры Downtown
        string[] downtownTextures = AssetDatabase.FindAssets("Downtown t:Texture2D");
        string[] downtownNormals = AssetDatabase.FindAssets("Downtown t:Texture2D");
        string[] depotDiff = AssetDatabase.FindAssets("depotWindows t:Texture2D");
        string[] depotNormal = AssetDatabase.FindAssets("depotWindows t:Texture2D");
        
        Debug.Log($"Found textures: Downtown={downtownTextures.Length}, depotWindows={depotDiff.Length}");
        
        int assignedCount = 0;
        
        foreach (Material mat in cityBuildingMaterials)
        {
            string matName = mat.name.ToLower();
            string objName = materialToObjectName.ContainsKey(mat) ? materialToObjectName[mat] : "";
            bool assigned = false;
            
            Debug.Log($"Processing material: {mat.name} (lowercase: {matName}) from object: {objName}");
            
            // ОКНА - приоритет: ищем по названию объекта или материала
            if (objName.Contains("window") || objName.Contains("glass") || 
                matName.Contains("window") || matName.Contains("glass") || matName.Contains("depot") ||
                matName == "lit") // "Lit" обычно означает освещённые окна
            {
                Debug.Log($"  → Material '{mat.name}' matches WINDOW (Lit/Glass)");
                Debug.Log($"  → Found {depotDiff.Length} depot window textures");
                
                // Найти именно Diff текстуру, а не AO
                string[] depotDiffTextures = depotDiff.Where(g => 
                {
                    string path = AssetDatabase.GUIDToAssetPath(g).ToLower();
                    return path.Contains("diff") && !path.Contains("ao") && !path.Contains("normal");
                }).ToArray();
                
                if (depotDiffTextures.Length > 0)
                {
                    string texturePath = AssetDatabase.GUIDToAssetPath(depotDiffTextures[0]);
                    Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                    
                    if (texture != null)
                    {
                        mat.SetTexture("_BaseMap", texture);
                        
                        if (depotNormal.Length > 0)
                        {
                            Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(depotNormal[0]));
                            if (normal != null)
                            {
                                mat.SetTexture("_BumpMap", normal);
                                mat.EnableKeyword("_NORMALMAP");
                            }
                        }
                        
                        mat.SetFloat("_Smoothness", 0.1f);
                        mat.SetFloat("_Metallic", 0f);
                        EditorUtility.SetDirty(mat);
                        assigned = true;
                        assignedCount++;
                        Debug.Log($"✓ WINDOW: Assigned {texture.name} to {mat.name} (Object: {objName})");
                    }
                }
                else if (depotDiff.Length > 0)
                {
                    // Fallback: если не нашли Diff, используем первую доступную
                    string texturePath = AssetDatabase.GUIDToAssetPath(depotDiff[0]);
                    Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                    
                    if (texture != null && !texturePath.ToLower().Contains("ao"))
                    {
                        mat.SetTexture("_BaseMap", texture);
                        
                        if (depotNormal.Length > 0)
                        {
                            string[] normalTextures = depotNormal.Where(g => 
                            {
                                string path = AssetDatabase.GUIDToAssetPath(g).ToLower();
                                return path.Contains("normal") && !path.Contains("ao");
                            }).ToArray();
                            
                            if (normalTextures.Length > 0)
                            {
                                Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(normalTextures[0]));
                                if (normal != null)
                                {
                                    mat.SetTexture("_BumpMap", normal);
                                    mat.EnableKeyword("_NORMALMAP");
                                }
                            }
                        }
                        
                        mat.SetFloat("_Smoothness", 0.1f);
                        mat.SetFloat("_Metallic", 0f);
                        EditorUtility.SetDirty(mat);
                        assigned = true;
                        assignedCount++;
                        Debug.Log($"✓ WINDOW: Assigned {texture.name} to {mat.name} (Object: {objName})");
                    }
                }
            }
            // СТЕНЫ/ФАСАДЫ - для красных зданий и стен
            else if (matName.Contains("red_build") || matName.Contains("red_building") || 
                     objName.Contains("wall") || objName.Contains("facade") || 
                     matName.Contains("facade") || matName.Contains("wall"))
            {
                // Найти текстуру фасада
                string[] facadeTextures = downtownTextures.Where(g => 
                {
                    string path = AssetDatabase.GUIDToAssetPath(g).ToLower();
                    return path.Contains("facade") && path.Contains("_dif");
                }).ToArray();
                
                if (facadeTextures.Length > 0)
                {
                    // Используем первую подходящую текстуру фасада
                    string texturePath = AssetDatabase.GUIDToAssetPath(facadeTextures[0]);
                    Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                    
                    if (texture != null)
                    {
                        mat.SetTexture("_BaseMap", texture);
                        
                        // Найти Normal map (Height map)
                        string baseName = Path.GetFileNameWithoutExtension(texturePath).ToLower();
                        string baseNameClean = baseName.Replace("_dif", "").Replace("_dif", "");
                        string[] normalTextures = downtownNormals.Where(g => 
                        {
                            string path = AssetDatabase.GUIDToAssetPath(g).ToLower();
                            return path.Contains(baseNameClean) && path.Contains("height");
                        }).ToArray();
                        
                        if (normalTextures.Length > 0)
                        {
                            Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(normalTextures[0]));
                            if (normal != null)
                            {
                                mat.SetTexture("_BumpMap", normal);
                                mat.EnableKeyword("_NORMALMAP");
                            }
                        }
                        
                        mat.SetFloat("_Smoothness", 0.2f);
                        mat.SetFloat("_Metallic", 0f);
                        EditorUtility.SetDirty(mat);
                        assigned = true;
                        assignedCount++;
                        Debug.Log($"✓ WALL: Assigned {texture.name} to {mat.name} (Object: {objName})");
                    }
                }
            }
            // БЕТОН/КОНКРЕТ
            else if (matName.Contains("concrete"))
            {
                Debug.Log($"  → Material '{mat.name}' matches CONCRETE");
                string[] concreteTextures = downtownTextures.Where(g => 
                {
                    string path = AssetDatabase.GUIDToAssetPath(g).ToLower();
                    return path.Contains("concrete") && path.Contains("_dif");
                }).ToArray();
                
                Debug.Log($"  → Found {concreteTextures.Length} concrete textures");
                
                if (concreteTextures.Length > 0)
                {
                    string texturePath = AssetDatabase.GUIDToAssetPath(concreteTextures[0]);
                    Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                    
                    if (texture != null)
                    {
                        mat.SetTexture("_BaseMap", texture);
                        mat.SetFloat("_Smoothness", 0.1f);
                        mat.SetFloat("_Metallic", 0f);
                        EditorUtility.SetDirty(mat);
                        assigned = true;
                        assignedCount++;
                        Debug.Log($"✓ CONCRETE: Assigned {texture.name} to {mat.name} (Object: {objName})");
                    }
                }
            }
            // МЕТАЛЛ (рамы окон, детали)
            else if (matName.Contains("metal"))
            {
                Debug.Log($"  → Material '{mat.name}' matches METAL");
                // Используем текстуру для металла или серую текстуру
                string[] metalTextures = downtownTextures.Where(g => 
                {
                    string path = AssetDatabase.GUIDToAssetPath(g).ToLower();
                    return path.Contains("_dif");
                }).ToArray();
                
                Debug.Log($"  → Found {metalTextures.Length} metal textures");
                
                if (metalTextures.Length > 0)
                {
                    string texturePath = AssetDatabase.GUIDToAssetPath(metalTextures[0]);
                    Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                    
                    if (texture != null)
                    {
                        mat.SetTexture("_BaseMap", texture);
                        mat.SetFloat("_Smoothness", 0.3f);
                        mat.SetFloat("_Metallic", 0.5f); // Металл должен быть металлическим
                        EditorUtility.SetDirty(mat);
                        assigned = true;
                        assignedCount++;
                        Debug.Log($"✓ METAL: Assigned {texture.name} to {mat.name} (Object: {objName})");
                    }
                }
            }
            // ТРОТУАРЫ
            else if (matName.Contains("sidewalk") || matName.Contains("sidewalks"))
            {
                Debug.Log($"  → Material '{mat.name}' matches SIDEWALK");
                // Используем текстуру для тротуаров или бетона
                string[] sidewalkTextures = downtownTextures.Where(g => 
                {
                    string path = AssetDatabase.GUIDToAssetPath(g).ToLower();
                    return (path.Contains("concrete") || path.Contains("firstfloor")) && path.Contains("_dif");
                }).ToArray();
                
                Debug.Log($"  → Found {sidewalkTextures.Length} sidewalk textures");
                
                if (sidewalkTextures.Length > 0)
                {
                    string texturePath = AssetDatabase.GUIDToAssetPath(sidewalkTextures[0]);
                    Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                    
                    if (texture != null)
                    {
                        mat.SetTexture("_BaseMap", texture);
                        mat.SetFloat("_Smoothness", 0.1f);
                        mat.SetFloat("_Metallic", 0f);
                        EditorUtility.SetDirty(mat);
                        assigned = true;
                        assignedCount++;
                        Debug.Log($"✓ SIDEWALK: Assigned {texture.name} to {mat.name} (Object: {objName})");
                    }
                }
            }
            // РАЗНЫЕ ДЕТАЛИ (Misc)
            else if (matName.Contains("misc"))
            {
                Debug.Log($"  → Material '{mat.name}' matches MISC");
                // Используем любую подходящую текстуру
                string[] miscTextures = downtownTextures.Where(g => 
                {
                    string path = AssetDatabase.GUIDToAssetPath(g).ToLower();
                    return path.Contains("_dif");
                }).ToArray();
                
                Debug.Log($"  → Found {miscTextures.Length} misc textures");
                
                if (miscTextures.Length > 0)
                {
                    string texturePath = AssetDatabase.GUIDToAssetPath(miscTextures[0]);
                    Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                    
                    if (texture != null)
                    {
                        mat.SetTexture("_BaseMap", texture);
                        mat.SetFloat("_Smoothness", 0.2f);
                        mat.SetFloat("_Metallic", 0f);
                        EditorUtility.SetDirty(mat);
                        assigned = true;
                        assignedCount++;
                        Debug.Log($"✓ MISC: Assigned {texture.name} to {mat.name} (Object: {objName})");
                    }
                }
            }
            // ДРУГИЕ ЧАСТИ ЗДАНИЯ (крыша, первый этаж и т.д.)
            else if (matName.Contains("build") || objName.Contains("build"))
            {
                // Попробовать найти текстуру по контексту
                string[] anyDowntown = downtownTextures.Where(g => 
                {
                    string path = AssetDatabase.GUIDToAssetPath(g).ToLower();
                    return path.Contains("_dif");
                }).ToArray();
                
                // Приоритет: Firstfloor, затем Facade, затем любая
                string[] preferredTextures = anyDowntown.Where(g => 
                {
                    string path = AssetDatabase.GUIDToAssetPath(g).ToLower();
                    return path.Contains("firstfloor") || path.Contains("facade");
                }).ToArray();
                
                string[] texturesToUse = preferredTextures.Length > 0 ? preferredTextures : anyDowntown;
                
                if (texturesToUse.Length > 0)
                {
                    string texturePath = AssetDatabase.GUIDToAssetPath(texturesToUse[0]);
                    Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                    
                    if (texture != null)
                    {
                        mat.SetTexture("_BaseMap", texture);
                        mat.SetFloat("_Smoothness", 0.2f);
                        mat.SetFloat("_Metallic", 0f);
                        EditorUtility.SetDirty(mat);
                        assigned = true;
                        assignedCount++;
                        Debug.Log($"✓ BUILDING: Assigned {texture.name} to {mat.name} (Object: {objName})");
                    }
                }
            }
            
            if (!assigned)
            {
                Debug.LogWarning($"⚠ Could not assign texture to {mat.name} (Object: {objName})");
            }
        }
        
        // Принудительно обновить все рендереры на сцене
        foreach (GameObject obj in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (obj.name.Contains("city_building") && obj.scene.isLoaded)
            {
                MeshRenderer[] renderers = obj.GetComponentsInChildren<MeshRenderer>(true);
                foreach (MeshRenderer renderer in renderers)
                {
                    Material[] materials = renderer.sharedMaterials;
                    bool changed = false;
                    
                    for (int i = 0; i < materials.Length; i++)
                    {
                        if (materials[i] != null && cityBuildingMaterials.Contains(materials[i]))
                        {
                            // Обновить ссылку на материал
                            Material updatedMat = materials[i];
                            materials[i] = null; // Сбросить
                            materials[i] = updatedMat; // Установить заново
                            changed = true;
                        }
                    }
                    
                    if (changed)
                    {
                        renderer.sharedMaterials = materials;
                        EditorUtility.SetDirty(renderer);
                    }
                }
            }
        }
        
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        
        // Обновить сцену
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
        
        Debug.Log($"\n=== COMPLETE: Assigned textures to {assignedCount} materials! ===");
        Debug.Log("Please check the scene - materials should be updated now.");
    }
}

