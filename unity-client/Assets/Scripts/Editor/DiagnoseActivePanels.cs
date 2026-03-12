using UnityEngine;
using UnityEditor;
using UnityEngine.UI;

public static class DiagnoseActivePanels
{
    [MenuItem("Castle Defender/Debug/Diagnose Active Panels")]
    public static void Run()
    {
        var all = Object.FindObjectsByType<Image>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int found = 0;
        foreach (var img in all)
        {
            var rt = img.GetComponent<RectTransform>();
            if (rt == null) continue;
            if (rt.rect.width < 80) continue;
            bool active = img.gameObject.activeInHierarchy;
            if (active)
            {
                Debug.Log($"[ACTIVE PANEL] {img.gameObject.name} | path={GetPath(img.transform)} | size={rt.rect.width:0}x{rt.rect.height:0} | color={img.color} | activeSelf={img.gameObject.activeSelf}");
                found++;
            }
        }
        Debug.Log($"[DiagnoseActivePanels] {found} active Image panels found.");
    }

    static string GetPath(Transform t)
    {
        string path = t.name;
        while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
        return path;
    }
}
