using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using CastleDefender.UI;

namespace CastleDefender.Editor
{
    public static class SetupMobileMatchHud
    {
        const string MenuPath = "Castle Defender/Setup/Setup Mobile Match HUD";

        [MenuItem(MenuPath)]
        public static void Run()
        {
            var canvas = Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
            if (canvas == null)
            {
                Debug.LogError("[SetupMobileMatchHud] No Canvas found in active scene.");
                return;
            }

            var existing = canvas.transform.Find("WaveHUD");
            GameObject hudGo;
            if (existing != null)
            {
                hudGo = existing.gameObject;
            }
            else
            {
                hudGo = new GameObject("WaveHUD", typeof(RectTransform), typeof(UnityEngine.UI.Image));
                hudGo.transform.SetParent(canvas.transform, false);
            }

            var infoBar = hudGo.GetComponent<InfoBar>();
            if (infoBar == null)
                infoBar = Undo.AddComponent<InfoBar>(hudGo);

            var mobileHud = hudGo.GetComponent<MobileMatchHud>();
            if (mobileHud == null)
                mobileHud = Undo.AddComponent<MobileMatchHud>(hudGo);

            EditorUtility.SetDirty(hudGo);
            EditorUtility.SetDirty(infoBar);
            EditorUtility.SetDirty(mobileHud);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[SetupMobileMatchHud] Mobile match HUD component attached to WaveHUD.");
        }
    }
}
