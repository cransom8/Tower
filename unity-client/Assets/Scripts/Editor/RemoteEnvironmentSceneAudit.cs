#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CastleDefender.Editor
{
    public static class RemoteEnvironmentSceneAudit
    {
        const string ScenePath = "Assets/Scenes/Game_ML.unity";
        const string MapRootName = "Map";
        const string ReportRelativePath = "projects/Game_ML Environment Audit.md";

        static readonly HashSet<Type> AllowedVisualComponentTypes = new()
        {
            typeof(Transform),
            typeof(MeshFilter),
            typeof(MeshRenderer),
            typeof(SkinnedMeshRenderer),
            typeof(ParticleSystem),
            typeof(ParticleSystemRenderer),
            typeof(TrailRenderer),
            typeof(LineRenderer),
            typeof(Animator),
            typeof(LODGroup),
            typeof(SpriteRenderer),
            typeof(Light),
            typeof(ReflectionProbe),
            typeof(Projector),
            typeof(FlareLayer),
        };

        [MenuItem("Castle Defender/Remote Content/Audit Game_ML Environment")]
        static void AuditGameMlEnvironment()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (!string.Equals(scene.path, ScenePath, StringComparison.OrdinalIgnoreCase))
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var mapRoot = GameObject.Find(MapRootName);
            if (mapRoot == null)
            {
                Debug.LogError($"[RemoteEnvironmentSceneAudit] Could not find '{MapRootName}' in {scene.path}.");
                return;
            }

            var audits = mapRoot.transform.Cast<Transform>()
                .Select(AuditBranch)
                .OrderByDescending(audit => audit.IsLikelyVisualOnly)
                .ThenBy(audit => audit.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            string reportPath = Path.Combine(projectRoot, ReportRelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath) ?? projectRoot);
            File.WriteAllText(reportPath, BuildReport(scene.path, mapRoot.name, audits));

            Debug.Log($"[RemoteEnvironmentSceneAudit] Wrote environment audit report to {reportPath}");
            Debug.Log(
                "[RemoteEnvironmentSceneAudit] Audit complete. " +
                "Run 'Castle Defender/Remote Content/Extract Game_ML Environment' explicitly if you intend to rebuild the critical environment prefab.");
        }

        [MenuItem("Castle Defender/Remote Content/Extract Game_ML Environment")]
        static void ExtractGameMlEnvironment()
        {
            SetupRemoteEnvironmentAddressables.ExtractGameMlEnvironment();
        }

        static BranchAudit AuditBranch(Transform root)
        {
            var audit = new BranchAudit
            {
                Name = root.name,
                Path = BuildPath(root),
            };

            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                audit.NodeCount++;
                var components = transform.GetComponents<Component>();
                foreach (var component in components)
                {
                    if (component == null)
                    {
                        audit.MissingScriptCount++;
                        continue;
                    }

                    var type = component.GetType();
                    if (type == typeof(Transform))
                        continue;

                    if (component is Renderer)
                        audit.RendererCount++;
                    if (component is ParticleSystem)
                        audit.ParticleSystemCount++;
                    if (component is Light)
                        audit.LightCount++;

                    if (component is Collider collider)
                    {
                        audit.ColliderTypes.Add(type.Name + (collider.isTrigger ? " (Trigger)" : string.Empty));
                        continue;
                    }

                    if (component is MonoBehaviour)
                    {
                        audit.CustomComponentTypes.Add(type.FullName ?? type.Name);
                        continue;
                    }

                    if (!AllowedVisualComponentTypes.Contains(type))
                        audit.OtherComponentTypes.Add(type.FullName ?? type.Name);
                }
            }

            audit.ColliderTypes.Sort(StringComparer.OrdinalIgnoreCase);
            audit.CustomComponentTypes.Sort(StringComparer.OrdinalIgnoreCase);
            audit.OtherComponentTypes.Sort(StringComparer.OrdinalIgnoreCase);
            return audit;
        }

        static string BuildReport(string scenePath, string mapRootName, IReadOnlyList<BranchAudit> audits)
        {
            var likelyVisual = audits.Where(audit => audit.IsLikelyVisualOnly).ToList();
            var colliderStripCandidates = audits.Where(audit => audit.IsVisualAfterColliderStrip).ToList();
            var reviewRequired = audits.Where(audit => !audit.IsLikelyVisualOnly && !audit.IsVisualAfterColliderStrip).ToList();

            var builder = new StringBuilder();
            builder.AppendLine("# Game_ML Environment Audit");
            builder.AppendLine();
            builder.AppendLine($"Scene: `{scenePath}`");
            builder.AppendLine($"Map Root: `{mapRootName}`");
            builder.AppendLine($"Generated: `{DateTime.Now:yyyy-MM-dd HH:mm:ss}`");
            builder.AppendLine();
            builder.AppendLine("## Likely Visual-Only Branches");
            builder.AppendLine();

            if (likelyVisual.Count == 0)
            {
                builder.AppendLine("No clean visual-only branches were detected.");
                builder.AppendLine();
            }
            else
            {
                foreach (var audit in likelyVisual)
                    AppendAudit(builder, audit);
            }

            builder.AppendLine("## Review / Keep Local");
            builder.AppendLine();

            if (colliderStripCandidates.Count > 0)
            {
                builder.AppendLine("## Likely Visual After Collider Strip");
                builder.AppendLine();
                foreach (var audit in colliderStripCandidates)
                    AppendAudit(builder, audit);
            }

            builder.AppendLine("## Review / Keep Local");
            builder.AppendLine();

            if (reviewRequired.Count == 0)
            {
                builder.AppendLine("No branches were flagged for local-only review.");
                builder.AppendLine();
            }
            else
            {
                foreach (var audit in reviewRequired)
                    AppendAudit(builder, audit);
            }

            builder.AppendLine("## Extraction Rule Of Thumb");
            builder.AppendLine();
            builder.AppendLine("- Branches with custom gameplay scripts should stay local unless reviewed manually.");
            builder.AppendLine("- Branches with only renderers, particles, animators, or lights are the safest first extraction candidates.");
            builder.AppendLine("- Branches with only basic colliders and no custom scripts are good extraction candidates after collider removal is reviewed.");
            builder.AppendLine("- Waypoints, spawn points, tile grids, triggers, and anything referenced by gameplay code should remain in-scene.");
            return builder.ToString();
        }

        static void AppendAudit(StringBuilder builder, BranchAudit audit)
        {
            builder.AppendLine($"### `{audit.Name}`");
            builder.AppendLine();
            builder.AppendLine($"Path: `{audit.Path}`");
            builder.AppendLine($"Nodes: `{audit.NodeCount}`");
            builder.AppendLine($"Renderers: `{audit.RendererCount}`");
            builder.AppendLine($"Particles: `{audit.ParticleSystemCount}`");
            builder.AppendLine($"Lights: `{audit.LightCount}`");
            builder.AppendLine($"Missing Scripts: `{audit.MissingScriptCount}`");
            builder.AppendLine($"Likely Visual Only: `{audit.IsLikelyVisualOnly}`");

            if (audit.ColliderTypes.Count > 0)
                builder.AppendLine($"Colliders: `{string.Join(", ", audit.ColliderTypes.Distinct(StringComparer.OrdinalIgnoreCase))}`");
            if (audit.CustomComponentTypes.Count > 0)
                builder.AppendLine($"Custom Components: `{string.Join(", ", audit.CustomComponentTypes.Distinct(StringComparer.OrdinalIgnoreCase))}`");
            if (audit.OtherComponentTypes.Count > 0)
                builder.AppendLine($"Other Components: `{string.Join(", ", audit.OtherComponentTypes.Distinct(StringComparer.OrdinalIgnoreCase))}`");

            builder.AppendLine();
        }

        static string BuildPath(Transform transform)
        {
            var segments = new Stack<string>();
            var current = transform;
            while (current != null)
            {
                segments.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", segments);
        }

        sealed class BranchAudit
        {
            public string Name;
            public string Path;
            public int NodeCount;
            public int RendererCount;
            public int ParticleSystemCount;
            public int LightCount;
            public int MissingScriptCount;
            public List<string> ColliderTypes { get; } = new();
            public List<string> CustomComponentTypes { get; } = new();
            public List<string> OtherComponentTypes { get; } = new();

            public bool IsLikelyVisualOnly =>
                RendererCount + ParticleSystemCount + LightCount > 0
                && MissingScriptCount == 0
                && ColliderTypes.Count == 0
                && CustomComponentTypes.Count == 0
                && OtherComponentTypes.Count == 0;

            public bool IsVisualAfterColliderStrip =>
                RendererCount + ParticleSystemCount + LightCount > 0
                && MissingScriptCount == 0
                && ColliderTypes.Count > 0
                && CustomComponentTypes.Count == 0
                && OtherComponentTypes.Count == 0;
        }
    }
}
#endif
