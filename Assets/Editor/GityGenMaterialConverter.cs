using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class CityGenMaterialConverter : EditorWindow
{
    [MenuItem("Tools/Fix CityGen3D Materials")]
    public static void ShowWindow()
    {
        GetWindow<CityGenMaterialConverter>("CityGen Material Fixer");
    }

    void OnGUI()
    {
        GUILayout.Label("CityGen3D Material Converter", EditorStyles.boldLabel);

        EditorGUILayout.HelpBox("This will convert all CityGen3D materials to URP/Lit while preserving textures.", MessageType.Info);

        if (GUILayout.Button("Convert All CityGen Materials"))
        {
            ConvertCityGenMaterials();
        }

        if (GUILayout.Button("Backup Materials First"))
        {
            BackupMaterials();
        }
    }

    static void ConvertCityGenMaterials()
    {
        string[] materialGUIDs = AssetDatabase.FindAssets("t:Material");
        List<Material> convertedMaterials = new List<Material>();

        Shader urpLitShader = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLitShader == null)
        {
            Debug.LogError("URP/Lit shader not found! Make sure URP is properly installed.");
            return;
        }

        foreach (string guid in materialGUIDs)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (mat == null || mat.shader == null) continue;

            // Check if it's a CityGen3D shader
            if (mat.shader.name.StartsWith("CityGen3D/"))
            {
                Debug.Log($"Converting material: {mat.name} from shader: {mat.shader.name}");

                // Store current textures
                Texture mainTex = mat.GetTexture("_MainTex");
                Texture normalMap = mat.GetTexture("_BumpMap");
                Texture metallicMap = mat.GetTexture("_MetallicGlossMap");
                Color mainColor = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;

                // Change to URP shader
                mat.shader = urpLitShader;

                // Restore textures with URP property names
                if (mainTex != null)
                    mat.SetTexture("_BaseMap", mainTex);
                if (normalMap != null)
                    mat.SetTexture("_BumpMap", normalMap);
                if (metallicMap != null)
                    mat.SetTexture("_MetallicGlossMap", metallicMap);

                mat.SetColor("_BaseColor", mainColor);

                EditorUtility.SetDirty(mat);
                convertedMaterials.Add(mat);
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"Converted {convertedMaterials.Count} CityGen3D materials to URP/Lit");
    }

    static void BackupMaterials()
    {
        string backupPath = "Assets/CityGen3D_Material_Backup";
        if (!AssetDatabase.IsValidFolder(backupPath))
        {
            AssetDatabase.CreateFolder("Assets", "CityGen3D_Material_Backup");
        }

        string[] materialGUIDs = AssetDatabase.FindAssets("t:Material");
        int backupCount = 0;

        foreach (string guid in materialGUIDs)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (mat != null && mat.shader != null && mat.shader.name.StartsWith("CityGen3D/"))
            {
                string fileName = $"{mat.name}_backup.mat";
                string backupFilePath = $"{backupPath}/{fileName}";
                AssetDatabase.CopyAsset(path, backupFilePath);
                backupCount++;
            }
        }

        AssetDatabase.Refresh();
        Debug.Log($"Backed up {backupCount} CityGen3D materials to {backupPath}");
    }
}