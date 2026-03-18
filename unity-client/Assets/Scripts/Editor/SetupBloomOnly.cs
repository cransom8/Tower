#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class SetupBloomOnly
{
    private const string ProfilePath = "Assets/PostProcess/LavaBloom.asset";

    [MenuItem("Castle Defender/Setup/Setup Bloom (Game_ML)")]
    public static void SetupBothScenes()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var sceneML = EditorSceneManager.OpenScene("Assets/Scenes/Game_ML.unity", OpenSceneMode.Single);
        Apply();
        EditorSceneManager.SaveScene(sceneML);

        Debug.Log("[SetupBloomOnly] Bloom applied and saved to Game_ML.");
    }

    static void Apply()
    {
        if (!AssetDatabase.IsValidFolder("Assets/PostProcess"))
            AssetDatabase.CreateFolder("Assets", "PostProcess");

        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
        }

        if (!profile.TryGet<Bloom>(out var bloom))
            bloom = profile.Add<Bloom>(false);
        bloom.active = true;
        bloom.threshold.overrideState = true;
        bloom.threshold.value = 0.4f;
        bloom.intensity.overrideState = true;
        bloom.intensity.value = 0f;
        bloom.scatter.overrideState = true;
        bloom.scatter.value = 0.7f;
        bloom.highQualityFiltering.overrideState = true;
        bloom.highQualityFiltering.value = false;

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();

        var vol = Object.FindFirstObjectByType<Volume>();
        if (vol == null)
        {
            var go = new GameObject("GlobalVolume_Bloom");
            vol = go.AddComponent<Volume>();
        }
        vol.isGlobal = true;
        vol.priority = 10f;
        vol.sharedProfile = profile;
        vol.gameObject.SetActive(true);
        EditorUtility.SetDirty(vol);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }
}
#endif
