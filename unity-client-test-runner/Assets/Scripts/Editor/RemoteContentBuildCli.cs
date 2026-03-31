using UnityEditor;

namespace CastleDefender.Editor
{
    public static class RemoteContentBuildCli
    {
        public static void BuildAddressablesContent()
        {
            if (!EditorApplication.ExecuteMenuItem("Castle Defender/Remote Content/Build Addressables Content"))
                throw new System.InvalidOperationException("Failed to execute Addressables build menu item.");
        }
    }
}
