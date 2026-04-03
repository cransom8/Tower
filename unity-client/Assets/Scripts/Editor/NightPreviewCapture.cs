#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CastleDefender.Editor
{
    [InitializeOnLoad]
    public static class NightPreviewCapture
    {
        const string EditModeOutputPath = "projects/night-preview-editmode.png";
        const string PlayModeOutputPath = "projects/night-preview-playmode.png";
        const int CaptureWidth = 1600;
        const int CaptureHeight = 900;
        const string PendingPlayCaptureKey = "CastleDefender.NightPreviewCapture.PendingPlayCapture";
        const string PendingPlayFramesKey = "CastleDefender.NightPreviewCapture.PendingPlayFrames";
        const int WarmupFrames = 12;

        static NightPreviewCapture()
        {
            EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
            EditorApplication.update -= HandleEditorUpdate;
            EditorApplication.update += HandleEditorUpdate;
        }

        [MenuItem("Castle Defender/Remote Content/Capture Staged Night Preview Screenshot")]
        static void CaptureStagedNightPreviewScreenshot()
        {
            CaptureFromMainCamera(EditModeOutputPath, "staged preview");
        }

        [MenuItem("Castle Defender/Remote Content/Capture Live Night Preview Screenshot")]
        static void CaptureLiveNightPreviewScreenshot()
        {
            if (Application.isPlaying)
            {
                CaptureFromMainCamera(PlayModeOutputPath, "live preview");
                return;
            }

            SessionState.SetBool(PendingPlayCaptureKey, true);
            SessionState.SetInt(PendingPlayFramesKey, WarmupFrames);
            EditorApplication.isPlaying = true;
            Debug.Log("[NightPreviewCapture] Entering play mode to capture a live night preview screenshot.");
        }

        static void HandlePlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                SessionState.SetInt(PendingPlayFramesKey, 0);
            }
        }

        static void HandleEditorUpdate()
        {
            if (!Application.isPlaying)
                return;
            if (!SessionState.GetBool(PendingPlayCaptureKey, false))
                return;

            int framesRemaining = SessionState.GetInt(PendingPlayFramesKey, 0);
            if (framesRemaining > 0)
            {
                SessionState.SetInt(PendingPlayFramesKey, framesRemaining - 1);
                return;
            }

            SessionState.SetBool(PendingPlayCaptureKey, false);
            CaptureFromMainCamera(PlayModeOutputPath, "live preview");
            EditorApplication.isPlaying = false;
        }

        static void CaptureFromMainCamera(string outputPath, string label)
        {
            var camera = Object.FindFirstObjectByType<Camera>();
            if (camera == null)
            {
                Debug.LogError("[NightPreviewCapture] No camera found in the active scene.");
                return;
            }

            string directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;

            var renderTexture = new RenderTexture(CaptureWidth, CaptureHeight, 24, RenderTextureFormat.ARGB32);
            var texture = new Texture2D(CaptureWidth, CaptureHeight, TextureFormat.RGBA32, false);

            try
            {
                camera.targetTexture = renderTexture;
                RenderTexture.active = renderTexture;
                camera.Render();

                texture.ReadPixels(new Rect(0f, 0f, CaptureWidth, CaptureHeight), 0, 0);
                texture.Apply(false, false);

                File.WriteAllBytes(outputPath, texture.EncodeToPNG());
                Debug.Log($"[NightPreviewCapture] Captured {label} screenshot to '{outputPath}'.");
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;

                Object.DestroyImmediate(renderTexture);
                Object.DestroyImmediate(texture);
            }
        }
    }
}
#endif
