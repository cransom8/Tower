using UnityEditor;

namespace CastleDefender.Editor
{
    public static class RemoteContentBuildCli
    {
        public static void BuildAddressablesContent()
        {
            RemoteContentBuildAddressables.BuildForTarget(BuildTarget.Android);
        }
    }
}
