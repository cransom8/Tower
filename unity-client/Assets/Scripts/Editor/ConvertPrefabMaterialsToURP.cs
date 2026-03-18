// ConvertPrefabMaterialsToURP.cs — Editor utility: bakes URP/Lit shader onto all
// non-URP materials used by prefabs under Assets/Prefabs/Units and Assets/Prefabs/Towers.
// Run once before shipping to eliminate the runtime UpgradeToURP() path.
//
// Castle Defender → Setup → Convert Prefab Materials to URP

using UnityEditor;
using UnityEngine;
using System.IO;

namespace CastleDefender.Editor
{
    public static class ConvertPrefabMaterialsToURP
    {
        [MenuItem("Castle Defender/Setup/Convert Prefab Materials to URP")]
        static void Run()
        {
            var urpLit = Shader.Find("Universal Render Pipeline/Lit");
            if (urpLit == null)
            {
                EditorUtility.DisplayDialog("Convert Prefab Materials",
                    "URP/Lit shader not found. Make sure URP is installed.", "OK");
                return;
            }

            string[] searchFolders = { "Assets/Prefabs" };
            string[] guids = AssetDatabase.FindAssets("t:Prefab", searchFolders);
            int converted = 0;

            try
            {
                for (int gi = 0; gi < guids.Length; gi++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[gi]);
                    EditorUtility.DisplayProgressBar("Converting Materials",
                        Path.GetFileName(path), (float)gi / guids.Length);

                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null) continue;

                    bool dirty = false;
                    foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
                    {
                        if (r.GetComponent<ToonShaderBridge>() != null) continue;

                        var mats = r.sharedMaterials;
                        bool changed = false;
                        for (int mi = 0; mi < mats.Length; mi++)
                        {
                            var mat = mats[mi];
                            if (mat == null) continue;
                            if (mat.shader != null &&
                                mat.shader.name.StartsWith("Universal Render Pipeline")) continue;

                            // Build a converted material next to the original
                            string matPath = AssetDatabase.GetAssetPath(mat);
                            string dir     = string.IsNullOrEmpty(matPath)
                                ? "Assets/Materials/Converted"
                                : Path.GetDirectoryName(matPath);
                            string baseName = string.IsNullOrEmpty(matPath)
                                ? mat.name
                                : Path.GetFileNameWithoutExtension(matPath);
                            string outPath  = $"{dir}/{baseName}_URP.mat";

                            if (!AssetDatabase.LoadAssetAtPath<Material>(outPath))
                            {
                                if (!AssetDatabase.IsValidFolder(dir))
                                    Directory.CreateDirectory(dir);

                                var converted_mat = new Material(urpLit);
                                if (mat.HasProperty("_MainTex"))
                                {
                                    var tex = mat.GetTexture("_MainTex");
                                    if (tex != null) converted_mat.SetTexture("_BaseMap", tex);
                                }
                                var col = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
                                converted_mat.SetColor("_BaseColor", col);
                                converted_mat.name = baseName + "_URP";

                                AssetDatabase.CreateAsset(converted_mat, outPath);
                                converted++;
                            }

                            mats[mi] = AssetDatabase.LoadAssetAtPath<Material>(outPath);
                            changed  = true;
                        }

                        if (changed)
                        {
                            r.sharedMaterials = mats;
                            EditorUtility.SetDirty(r);
                            dirty = true;
                        }
                    }

                    if (dirty)
                        PrefabUtility.SavePrefabAsset(prefab);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Convert Prefab Materials",
                $"Done. {converted} material(s) converted to URP/Lit.", "OK");
        }
    }
}
