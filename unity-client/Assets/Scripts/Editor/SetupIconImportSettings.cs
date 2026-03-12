// SetupIconImportSettings.cs — Sets TextureType=Sprite on all icon PNGs, then
// wires them into CmdBar and TileMenuUI components in Game_ML.unity.
// Castle Defender → Setup → Setup Icon Import Settings

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using System.IO;

namespace CastleDefender.Editor
{
    public static class SetupIconImportSettings
    {
        static readonly string[] UnitIconPaths =
        {
            "Art/icons/units/goblin_send_icon.png",
            "Art/icons/units/orc_send_icon.png",
            "Art/icons/units/troll_send_icon.png",
            "Art/icons/units/vampire_send_icon.png",
            "Art/icons/units/wyvern_send_icon.png",
        };

        static readonly string[] TowerIconPaths =
        {
            "Resources/Icons/towers/archer_icon.png",
            "Resources/Icons/towers/fighter_icon.png",
            "Resources/Icons/towers/mage_icon.png",
            "Resources/Icons/towers/ballista_icon.png",
            "Resources/Icons/towers/cannon_icon.png",
        };

        [MenuItem("Castle Defender/Setup/Setup Icon Import Settings")]
        static void Run()
        {
            // ── 1: Re-import all icons as Sprite ────────────────────────────────
            int changed = 0;
            foreach (var rel in UnitIconPaths)  changed += EnsureSprite("Assets/" + rel);
            foreach (var rel in TowerIconPaths) changed += EnsureSprite("Assets/" + rel);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[SetupIcons] {changed} texture(s) re-imported as Sprite.");

            // ── 2: Wire sprites into Game_ML scene ──────────────────────────────
            WireSceneIcons();
        }

        static int EnsureSprite(string assetPath)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null) { Debug.LogWarning($"[SetupIcons] Not found: {assetPath}"); return 0; }
            if (importer.textureType == TextureImporterType.Sprite) return 0;
            importer.textureType         = TextureImporterType.Sprite;
            importer.spriteImportMode    = SpriteImportMode.Single;
            importer.mipmapEnabled       = false;
            importer.alphaIsTransparency = true;
            importer.maxTextureSize      = 256;
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            return 1;
        }

        static void WireSceneIcons()
        {
            // Load sprites
            var unitSprites  = new Sprite[UnitIconPaths.Length];
            var towerSprites = new Sprite[TowerIconPaths.Length];
            for (int i = 0; i < UnitIconPaths.Length;  i++) unitSprites[i]  = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/" + UnitIconPaths[i]);
            for (int i = 0; i < TowerIconPaths.Length; i++) towerSprites[i] = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/" + TowerIconPaths[i]);

            // Open Game_ML scene
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Game_ML.unity", OpenSceneMode.Single);
            if (!scene.IsValid()) { Debug.LogError("[SetupIcons] Could not open Game_ML.unity"); return; }

            bool dirty = false;

            // Wire CmdBar.UnitIcons
            var cmdBar = Object.FindFirstObjectByType<CastleDefender.UI.CmdBar>();
            if (cmdBar != null)
            {
                cmdBar.UnitIcons = unitSprites;
                EditorUtility.SetDirty(cmdBar);
                dirty = true;
                Debug.Log("[SetupIcons] CmdBar.UnitIcons wired.");
            }
            else Debug.LogWarning("[SetupIcons] CmdBar not found in Game_ML scene.");

            // Wire TileMenuUI.TowerIcons (private field via SerializedObject) — all instances
            var allTileMenuUIs = Object.FindObjectsByType<CastleDefender.UI.TileMenuUI>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (allTileMenuUIs.Length > 0)
            {
                foreach (var tileMenuUI in allTileMenuUIs)
                {
                    var so  = new SerializedObject(tileMenuUI);
                    var arr = so.FindProperty("TowerIcons");
                    if (arr == null) continue;
                    arr.arraySize = towerSprites.Length;
                    for (int i = 0; i < towerSprites.Length; i++)
                        arr.GetArrayElementAtIndex(i).objectReferenceValue = towerSprites[i];
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(tileMenuUI);
                    dirty = true;
                }
                Debug.Log($"[SetupIcons] TileMenuUI.TowerIcons wired on {allTileMenuUIs.Length} instance(s).");
            }
            else Debug.LogWarning("[SetupIcons] No TileMenuUI found in Game_ML scene.");

            if (dirty) EditorSceneManager.SaveScene(scene);

            EditorUtility.DisplayDialog("Setup Icon Import Settings",
                "Done!\n\n• Icons imported as Sprite\n• CmdBar.UnitIcons assigned\n• TileMenuUI.TowerIcons assigned",
                "OK");
        }
    }
}
