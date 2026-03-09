// SetupSideRailLayout.cs — Moves CmdBar to the left rail and InfoBar to the
// right rail so the bridge spans the full height of the screen unobstructed.
//
// Menu: Castle Defender → Setup → Setup Side Rail Layout
//
// Run once on Game_ML. Re-running is safe.

using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace CastleDefender.Editor
{
    public static class SetupSideRailLayout
    {
        const float RAIL_W   = 160f;
        const float SPACING  = 4f;
        const int   PAD_SIDE = 4;
        const int   PAD_ENDS = 6;

        [MenuItem("Castle Defender/Setup/Setup Side Rail Layout")]
        public static void Run()
        {
            // Find by component type — avoids name-search quirks
            var cmdBarComp  = Object.FindFirstObjectByType<CastleDefender.UI.CmdBar>(FindObjectsInactive.Include);
            var infoBarComp = Object.FindFirstObjectByType<CastleDefender.UI.InfoBar>(FindObjectsInactive.Include);

            if (cmdBarComp == null)  Debug.LogWarning("[SideRailLayout] CmdBar component not found.");
            if (infoBarComp == null) Debug.LogWarning("[SideRailLayout] InfoBar component not found.");

            if (cmdBarComp != null)
                SetupRail(cmdBarComp.GetComponent<RectTransform>(),
                    anchorMin: new Vector2(0f, 0f),
                    anchorMax: new Vector2(0f, 1f),
                    pivot:     new Vector2(0f, 0.5f),
                    label:     "CmdBar to Left Rail");

            if (infoBarComp != null)
                SetupRail(infoBarComp.GetComponent<RectTransform>(),
                    anchorMin: new Vector2(1f, 0f),
                    anchorMax: new Vector2(1f, 1f),
                    pivot:     new Vector2(1f, 0.5f),
                    label:     "InfoBar to Right Rail");

            // Hide horizontal top/bottom bars
            HideByComponentName("LaneTabs");
            HideByComponentName("LaneViewBar");

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[SideRailLayout] Done — CmdBar left rail, InfoBar right rail.");
        }

        static void SetupRail(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax,
                               Vector2 pivot, string label)
        {
            if (rt == null) return;
            Undo.RecordObject(rt, label);

            rt.anchorMin        = anchorMin;
            rt.anchorMax        = anchorMax;
            rt.pivot            = pivot;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta        = new Vector2(RAIL_W, 0f);

            var vlg = rt.GetComponent<VerticalLayoutGroup>();
            if (vlg == null)
                vlg = Undo.AddComponent<VerticalLayoutGroup>(rt.gameObject);

            Undo.RecordObject(vlg, label + " VLG");
            vlg.childAlignment         = TextAnchor.UpperCenter;
            vlg.spacing                = SPACING;
            vlg.padding                = new RectOffset(PAD_SIDE, PAD_SIDE, PAD_ENDS, PAD_ENDS);
            vlg.childControlWidth      = true;
            vlg.childControlHeight     = true;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = true;

            var csf = rt.GetComponent<ContentSizeFitter>();
            if (csf != null) Undo.DestroyObjectImmediate(csf);

            EditorUtility.SetDirty(rt.gameObject);
            Debug.Log($"[SideRailLayout] {label} applied to {rt.gameObject.name}");
        }

        static void HideByComponentName(string goName)
        {
            var go = GameObject.Find(goName);
            if (go == null) return;
            Undo.RecordObject(go, "Hide " + goName);
            go.SetActive(false);
            EditorUtility.SetDirty(go);
            Debug.Log($"[SideRailLayout] Hidden {goName}");
        }
    }
}
