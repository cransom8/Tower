using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;

public static class SetupLavaPostProcess
{
    private const string ProfilePath = "Assets/PostProcess/LavaScene.asset";

    [MenuItem("Castle Defender/Setup/Setup Lava Post-Process")]
    public static void Setup()
    {
        // Ensure folder exists
        if (!AssetDatabase.IsValidFolder("Assets/PostProcess"))
            AssetDatabase.CreateFolder("Assets", "PostProcess");

        // Create or load the profile
        VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
        }

        // --- Tonemapping (ACES) ---
        if (!profile.TryGet<Tonemapping>(out var tm))
            tm = profile.Add<Tonemapping>(false);
        tm.active = true;
        tm.mode.overrideState = true;
        tm.mode.value = TonemappingMode.ACES;

        // --- Bloom ---
        if (!profile.TryGet<Bloom>(out var bloom))
            bloom = profile.Add<Bloom>(false);
        bloom.active = true;
        bloom.threshold.overrideState = true;
        bloom.threshold.value = 0.8f;
        bloom.intensity.overrideState = true;
        bloom.intensity.value = 0.4f;
        bloom.scatter.overrideState = true;
        bloom.scatter.value = 0.7f;
        bloom.highQualityFiltering.overrideState = true;
        bloom.highQualityFiltering.value = false;

        // --- Vignette ---
        if (!profile.TryGet<Vignette>(out var vignette))
            vignette = profile.Add<Vignette>(false);
        vignette.active = true;
        vignette.intensity.overrideState = true;
        vignette.intensity.value = 0.5f;
        vignette.smoothness.overrideState = true;
        vignette.smoothness.value = 0.4f;
        vignette.color.overrideState = true;
        vignette.color.value = new Color(0.05f, 0f, 0f, 1f); // very dark red-black

        // --- Color Adjustments (warm tint) ---
        if (!profile.TryGet<ColorAdjustments>(out var colorAdj))
            colorAdj = profile.Add<ColorAdjustments>(false);
        colorAdj.active = true;
        colorAdj.colorFilter.overrideState = true;
        colorAdj.colorFilter.value = new Color(1f, 0.88f, 0.72f, 1f); // warm orange-white
        colorAdj.postExposure.overrideState = true;
        colorAdj.postExposure.value = -0.3f; // slightly darker overall
        colorAdj.contrast.overrideState = true;
        colorAdj.contrast.value = 15f; // punch up contrast

        // --- Lift Gamma Gain (warm shadows, dark midtones) ---
        if (!profile.TryGet<LiftGammaGain>(out var lgg))
            lgg = profile.Add<LiftGammaGain>(false);
        lgg.active = true;
        lgg.lift.overrideState = true;
        // Shadows: push toward warm orange-red, slightly lifted
        lgg.lift.value = new Vector4(0.03f, -0.01f, -0.04f, -0.05f);
        lgg.gamma.overrideState = true;
        // Midtones: slightly dark with warm bias
        lgg.gamma.value = new Vector4(0.02f, -0.01f, -0.03f, -0.08f);
        lgg.gain.overrideState = true;
        // Highlights: stay neutral
        lgg.gain.value = new Vector4(0f, 0f, 0f, 0f);

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();

        // Assign to GlobalVolume in active scene
        int assigned = 0;
        foreach (var vol in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
        {
            if (vol.isGlobal)
            {
                vol.sharedProfile = profile;
                EditorUtility.SetDirty(vol);
                assigned++;
                Debug.Log($"[SetupLavaPostProcess] Assigned LavaScene profile to {vol.gameObject.name}");
            }
        }

        if (assigned == 0)
            Debug.LogWarning("[SetupLavaPostProcess] No global Volume found in scene. Profile created but not assigned.");

        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Debug.Log($"[SetupLavaPostProcess] Done. Profile at {ProfilePath}. Assigned to {assigned} volume(s). Save scene to persist.");
    }
}
