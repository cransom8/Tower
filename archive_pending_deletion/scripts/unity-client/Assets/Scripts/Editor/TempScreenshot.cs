using UnityEngine;
using UnityEditor;
public class TempScreenshot
{
    public static void Execute()
    {
        string path = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop), "game_screenshot.png");
        ScreenCapture.CaptureScreenshot(path);
        Debug.Log("[TempScreenshot] Saved to: " + path);
    }
}
