using System;
using UnityEngine;

namespace CastleDefender.Game
{
    [DisallowMultipleComponent]
    public sealed class TeamColorMaterialProfile : MonoBehaviour
    {
        [Serializable]
        public struct Target
        {
            public Renderer renderer;
            public bool replaceAllMaterials;
            public int materialIndex;
        }

        [Header("Team Materials")]
        [SerializeField] Material redMaterial;
        [SerializeField] Material blueMaterial;
        [SerializeField] Material yellowMaterial;
        [SerializeField] Material greenMaterial;

        [Header("Tint Targets")]
        [SerializeField] Target[] targets = Array.Empty<Target>();

        public bool HasTargets => targets != null && targets.Length > 0;

        public bool HasMaterialForTeam(BattleTeam team) => ResolveMaterial(team) != null;

        public Material GetMaterialForTeam(BattleTeam team) => ResolveMaterial(team);

        public bool Apply(BattleTeam team)
        {
            Material teamMaterial = ResolveMaterial(team);
            if (teamMaterial == null || !HasTargets)
                return false;

            bool appliedAny = false;
            for (int i = 0; i < targets.Length; i++)
            {
                var target = targets[i];
                if (target.renderer == null)
                    continue;

                var materials = Application.isPlaying
                    ? target.renderer.materials
                    : target.renderer.sharedMaterials;
                if (materials == null || materials.Length == 0)
                    continue;

                if (target.replaceAllMaterials || target.materialIndex < 0)
                {
                    for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                        materials[materialIndex] = teamMaterial;
                }
                else if (target.materialIndex < materials.Length)
                {
                    materials[target.materialIndex] = teamMaterial;
                }
                else
                {
                    continue;
                }

                if (Application.isPlaying)
                    target.renderer.materials = materials;
                else
                    target.renderer.sharedMaterials = materials;

                appliedAny = true;
            }

            return appliedAny;
        }

        public void ConfigureForEditor(
            Material red,
            Material blue,
            Material yellow,
            Material green,
            Target[] configuredTargets)
        {
            redMaterial = red;
            blueMaterial = blue;
            yellowMaterial = yellow;
            greenMaterial = green;
            targets = configuredTargets ?? Array.Empty<Target>();
        }

        Material ResolveMaterial(BattleTeam team)
        {
            return team switch
            {
                BattleTeam.Red => redMaterial,
                BattleTeam.Blue => blueMaterial,
                BattleTeam.Yellow => yellowMaterial,
                BattleTeam.Green => greenMaterial,
                _ => null,
            };
        }
    }
}
