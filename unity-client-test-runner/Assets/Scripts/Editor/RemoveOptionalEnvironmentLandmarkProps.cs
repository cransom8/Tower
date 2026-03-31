using System;
using UnityEditor;
using UnityEngine;

namespace CastleDefender.Editor
{
    public static class RemoveOptionalEnvironmentLandmarkProps
    {
        const string OptionalPrefabPath = "Assets/AddressableContent/Environment/GameEnvironmentOptional.prefab";
        const string LandmarkPropsPath = "VisualOuterRing/LandmarkProps";

        [MenuItem("Castle Defender/Remote Content/Remove Optional Environment LandmarkProps")]
        static void Remove()
        {
            try
            {
                using var editScope = new PrefabUtility.EditPrefabContentsScope(OptionalPrefabPath);
                var landmarkProps = editScope.prefabContentsRoot.transform.Find(LandmarkPropsPath);
                if (landmarkProps == null)
                {
                    Debug.Log("[RemoveOptionalEnvironmentLandmarkProps] LandmarkProps was already absent.");
                    return;
                }

                UnityEngine.Object.DestroyImmediate(landmarkProps.gameObject);
                Debug.Log("[RemoveOptionalEnvironmentLandmarkProps] Removed VisualOuterRing/LandmarkProps from GameEnvironmentOptional.");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                throw;
            }
        }
    }
}
