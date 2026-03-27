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
    public static class RenameRedPerimeterObjects
    {
        const string DryRunMenuPath = "Castle Defender/Environment/Rename Red Perimeter/Dry Run";
        const string ApplyMenuPath = "Castle Defender/Environment/Rename Red Perimeter/Apply";
        const string ReportRelativePath = "projects/red-perimeter-rename-report.md";

        [MenuItem(DryRunMenuPath)]
        static void DryRun() => Execute(applyChanges: false);

        [MenuItem(ApplyMenuPath)]
        static void Apply() => Execute(applyChanges: true);

        static void Execute(bool applyChanges)
        {
            if (!TryBuildContext(out var context, out string error))
            {
                Debug.LogError($"[RenameRedPerimeterObjects] {error}");
                return;
            }

            var report = PlanRename(context);

            if (applyChanges)
            {
                foreach (var entry in report.ProposedRenames.Where(entry => entry.CanApply && entry.CurrentName != entry.ProposedName))
                {
                    Undo.RecordObject(entry.Transform.gameObject, "Rename Red Perimeter Object");
                    entry.Transform.gameObject.name = entry.ProposedName;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(entry.Transform.gameObject);
                    entry.WasRenamed = true;
                }

                EditorSceneManager.MarkSceneDirty(context.Scene);
            }
            else
            {
                foreach (var entry in report.ProposedRenames.Where(entry => entry.CanApply))
                    Debug.Log($"[RenameRedPerimeterObjects][DryRun] {entry.DisplayPath}: {entry.CurrentName} -> {entry.ProposedName}");
            }

            string reportText = BuildReportText(context, report, applyChanges);
            string reportPath = WriteReport(reportText);

            Debug.Log(
                $"[RenameRedPerimeterObjects] {(applyChanges ? "Apply" : "Dry run")} complete. " +
                $"Renames={report.ProposedRenames.Count(entry => entry.CanApply && entry.CurrentName != entry.ProposedName)}, " +
                $"Skipped={report.Skipped.Count}, Duplicates={report.Duplicates.Count}, Ambiguous={report.Ambiguous.Count}. " +
                $"Report: {reportPath}");
        }

        static bool TryBuildContext(out SceneContext context, out string error)
        {
            context = null;
            error = null;

            var scene = EditorSceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                error = "No loaded active scene is available.";
                return false;
            }

            var mapRoot = scene.GetRootGameObjects().FirstOrDefault(go => go.name == "Map");
            if (mapRoot == null)
            {
                error = "Could not find root object 'Map' in the active scene.";
                return false;
            }

            var environmentRoot = mapRoot.transform.Find("GameEnvironment");
            if (environmentRoot == null)
            {
                error = "Could not find 'Map/GameEnvironment' in the active scene.";
                return false;
            }

            var redRow = environmentRoot.Find("Red_Row");
            if (redRow == null)
            {
                error = "Could not find 'Map/GameEnvironment/Red_Row' in the active scene.";
                return false;
            }

            var walls = redRow.Find("Walls");
            if (walls == null)
            {
                error = "Could not find 'Map/GameEnvironment/Red_Row/Walls'.";
                return false;
            }

            var scanned = new List<Transform>();
            var seen = new HashSet<int>();

            void AddDirectChildren(Transform parent)
            {
                if (parent == null)
                    return;

                foreach (Transform child in parent)
                {
                    if (seen.Add(child.gameObject.GetInstanceID()))
                        scanned.Add(child);
                }
            }

            AddDirectChildren(walls);
            AddDirectChildren(redRow.Find("Gates"));
            AddDirectChildren(redRow.Find("Red_Gates"));
            AddDirectChildren(walls.Find("Gates"));
            AddDirectChildren(walls.Find("Red_Gates"));
            AddDirectChildren(redRow.Find("Towers"));
            AddDirectChildren(redRow.Find("Red_Towers"));

            var perimeterPoints = walls.Cast<Transform>()
                .Where(child => ClassifyKind(child.name) != ObjectKind.Other && !IsContainerOnly(child))
                .Select(child => redRow.InverseTransformPoint(child.position))
                .ToList();

            if (perimeterPoints.Count == 0)
            {
                error = "No Red wall, tower, or gate objects were found under the Red perimeter containers.";
                return false;
            }

            float minX = perimeterPoints.Min(point => point.x);
            float maxX = perimeterPoints.Max(point => point.x);
            float minZ = perimeterPoints.Min(point => point.z);
            float maxZ = perimeterPoints.Max(point => point.z);
            float xRange = Mathf.Max(0.001f, maxX - minX);
            float zRange = Mathf.Max(0.001f, maxZ - minZ);

            context = new SceneContext
            {
                Scene = scene,
                RedRow = redRow,
                Walls = walls,
                ScannedObjects = scanned,
                FrontX = maxX,
                BackX = minX,
                LeftZ = maxZ,
                RightZ = minZ,
                CenterX = (minX + maxX) * 0.5f,
                CenterZ = (minZ + maxZ) * 0.5f,
                EdgeXTolerance = Mathf.Max(1.25f, xRange * 0.07f),
                EdgeZTolerance = Mathf.Max(1.25f, zRange * 0.07f),
                CenterXTolerance = Mathf.Max(2.5f, xRange * 0.22f),
                CenterZTolerance = Mathf.Max(2.5f, zRange * 0.22f),
            };

            return true;
        }

        static RenameReport PlanRename(SceneContext context)
        {
            var report = new RenameReport();
            var towerBuckets = new Dictionary<string, List<Candidate>>(StringComparer.Ordinal);
            var gateBuckets = new Dictionary<string, List<Candidate>>(StringComparer.Ordinal);
            var wallBuckets = new Dictionary<string, List<Candidate>>(StringComparer.Ordinal);

            foreach (Transform transform in context.ScannedObjects)
            {
                var candidate = new Candidate(context, transform);
                switch (ClassifyKind(transform.name))
                {
                    case ObjectKind.Wall:
                        AddToBucket(wallBuckets, ClassifyWallSlot(context, candidate), candidate, report);
                        break;
                    case ObjectKind.Tower:
                        AddToBucket(towerBuckets, ClassifyTowerSlot(context, candidate), candidate, report);
                        break;
                    case ObjectKind.Gate:
                        if (IsContainerOnly(transform))
                            report.Skipped.Add(new ReportEntry(candidate, "Container placeholder preserved."));
                        else
                            AddToBucket(gateBuckets, ClassifyGateSlot(context, candidate), candidate, report);
                        break;
                    default:
                        report.Skipped.Add(new ReportEntry(candidate, "Out of scope for wall/tower/gate renaming."));
                        break;
                }
            }

            AddSingleSlotPlan(towerBuckets, "Front_Left", "Red_Tower_Front_Left_Lvl_1", report);
            AddSingleSlotPlan(towerBuckets, "Front_Right", "Red_Tower_Front_Right_Lvl_1", report);
            AddSingleSlotPlan(towerBuckets, "Back_Left", "Red_Tower_Back_Left_Lvl_1", report);
            AddSingleSlotPlan(towerBuckets, "Back_Right", "Red_Tower_Back_Right_Lvl_1", report);

            AddSingleSlotPlan(gateBuckets, "Front_Center", "Red_Gate_Front_Center", report);
            AddSingleSlotPlan(gateBuckets, "Left", "Red_Gate_Left", report);
            AddSingleSlotPlan(gateBuckets, "Right", "Red_Gate_Right", report);
            AddSingleSlotPlan(gateBuckets, "Back_Center", "Red_Gate_Back_Center", report);

            AddWallPlans(wallBuckets, "Front_Left", report);
            AddWallPlans(wallBuckets, "Front_Right", report);
            AddWallPlans(wallBuckets, "Back_Left", report);
            AddWallPlans(wallBuckets, "Back_Right", report);

            return report;
        }

        static void AddToBucket(Dictionary<string, List<Candidate>> buckets, string slot, Candidate candidate, RenameReport report)
        {
            if (string.IsNullOrEmpty(slot))
            {
                report.Ambiguous.Add(new ReportEntry(candidate, "Position grouping was ambiguous."));
                return;
            }

            if (!buckets.TryGetValue(slot, out var list))
            {
                list = new List<Candidate>();
                buckets[slot] = list;
            }

            list.Add(candidate);
        }

        static void AddSingleSlotPlan(Dictionary<string, List<Candidate>> buckets, string slot, string targetName, RenameReport report)
        {
            if (!buckets.TryGetValue(slot, out var list) || list.Count == 0)
                return;

            if (list.Count > 1)
            {
                foreach (var candidate in list)
                    report.Duplicates.Add(new ReportEntry(candidate, $"Duplicate candidates mapped to '{targetName}'."));
                return;
            }

            report.ProposedRenames.Add(new RenameEntry(list[0], targetName));
        }

        static void AddWallPlans(Dictionary<string, List<Candidate>> buckets, string slot, RenameReport report)
        {
            if (!buckets.TryGetValue(slot, out var list) || list.Count == 0)
                return;

            IEnumerable<Candidate> ordered = slot switch
            {
                "Front_Left" => list.OrderByDescending(item => item.LocalPosition.x).ThenBy(item => item.LocalPosition.z).ThenBy(item => item.CurrentName, StringComparer.Ordinal),
                "Front_Right" => list.OrderByDescending(item => item.LocalPosition.x).ThenByDescending(item => item.LocalPosition.z).ThenBy(item => item.CurrentName, StringComparer.Ordinal),
                "Back_Left" => list.OrderBy(item => item.LocalPosition.x).ThenBy(item => item.LocalPosition.z).ThenBy(item => item.CurrentName, StringComparer.Ordinal),
                "Back_Right" => list.OrderBy(item => item.LocalPosition.x).ThenByDescending(item => item.LocalPosition.z).ThenBy(item => item.CurrentName, StringComparer.Ordinal),
                _ => list.OrderBy(item => item.CurrentName, StringComparer.Ordinal),
            };

            int index = 1;
            foreach (var candidate in ordered)
            {
                string targetName = $"Red_Wall_{slot}_{index:00}_Lvl_1";
                report.ProposedRenames.Add(new RenameEntry(candidate, targetName));
                index++;
            }
        }

        static string ClassifyWallSlot(SceneContext context, Candidate candidate)
        {
            bool nearFront = IsNear(candidate.LocalPosition.x, context.FrontX, context.EdgeXTolerance);
            bool nearBack = IsNear(candidate.LocalPosition.x, context.BackX, context.EdgeXTolerance);
            bool nearLeft = IsNear(candidate.LocalPosition.z, context.LeftZ, context.EdgeZTolerance);
            bool nearRight = IsNear(candidate.LocalPosition.z, context.RightZ, context.EdgeZTolerance);

            if (nearFront && candidate.LocalPosition.z > context.CenterZ + context.CenterZTolerance)
                return "Front_Left";
            if (nearFront && candidate.LocalPosition.z < context.CenterZ - context.CenterZTolerance)
                return "Front_Right";
            if (nearBack && candidate.LocalPosition.z > context.CenterZ + context.CenterZTolerance)
                return "Back_Left";
            if (nearBack && candidate.LocalPosition.z < context.CenterZ - context.CenterZTolerance)
                return "Back_Right";
            if (nearLeft && candidate.LocalPosition.x > context.CenterX)
                return "Front_Left";
            if (nearLeft && candidate.LocalPosition.x < context.CenterX)
                return "Back_Left";
            if (nearRight && candidate.LocalPosition.x > context.CenterX)
                return "Front_Right";
            if (nearRight && candidate.LocalPosition.x < context.CenterX)
                return "Back_Right";

            return null;
        }

        static string ClassifyTowerSlot(SceneContext context, Candidate candidate)
        {
            bool nearFront = IsNear(candidate.LocalPosition.x, context.FrontX, context.EdgeXTolerance);
            bool nearBack = IsNear(candidate.LocalPosition.x, context.BackX, context.EdgeXTolerance);
            bool nearLeft = IsNear(candidate.LocalPosition.z, context.LeftZ, context.EdgeZTolerance);
            bool nearRight = IsNear(candidate.LocalPosition.z, context.RightZ, context.EdgeZTolerance);

            if (nearFront && nearLeft) return "Front_Left";
            if (nearFront && nearRight) return "Front_Right";
            if (nearBack && nearLeft) return "Back_Left";
            if (nearBack && nearRight) return "Back_Right";
            return null;
        }

        static string ClassifyGateSlot(SceneContext context, Candidate candidate)
        {
            bool nearFront = IsNear(candidate.LocalPosition.x, context.FrontX, context.EdgeXTolerance);
            bool nearBack = IsNear(candidate.LocalPosition.x, context.BackX, context.EdgeXTolerance);
            bool nearLeft = IsNear(candidate.LocalPosition.z, context.LeftZ, context.EdgeZTolerance);
            bool nearRight = IsNear(candidate.LocalPosition.z, context.RightZ, context.EdgeZTolerance);

            if (nearFront && Mathf.Abs(candidate.LocalPosition.z - context.CenterZ) <= context.CenterZTolerance)
                return "Front_Center";
            if (nearBack && Mathf.Abs(candidate.LocalPosition.z - context.CenterZ) <= context.CenterZTolerance)
                return "Back_Center";
            if (nearLeft && Mathf.Abs(candidate.LocalPosition.x - context.CenterX) <= context.CenterXTolerance)
                return "Left";
            if (nearRight && Mathf.Abs(candidate.LocalPosition.x - context.CenterX) <= context.CenterXTolerance)
                return "Right";
            return null;
        }

        static ObjectKind ClassifyKind(string name)
        {
            string lowered = name.ToLowerInvariant();
            if (lowered.Contains("tower"))
                return ObjectKind.Tower;
            if (lowered.Contains("gate"))
                return ObjectKind.Gate;
            if (lowered.Contains("wall"))
                return ObjectKind.Wall;
            return ObjectKind.Other;
        }

        static bool IsContainerOnly(Transform transform)
        {
            string lowered = transform.name.ToLowerInvariant();
            return transform.childCount == 0 && (lowered == "gates" || lowered == "red_gates");
        }

        static bool IsNear(float value, float target, float tolerance) => Mathf.Abs(value - target) <= tolerance;

        static string BuildReportText(SceneContext context, RenameReport report, bool applyChanges)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# Red Perimeter Rename Report");
            builder.AppendLine();
            builder.AppendLine($"Mode: `{(applyChanges ? "Apply" : "Dry Run")}`");
            builder.AppendLine($"Scene: `{context.Scene.path}`");
            builder.AppendLine($"Red Root: `Map/GameEnvironment/Red_Row`");
            builder.AppendLine();

            builder.AppendLine("## Proposed Or Renamed Objects");
            builder.AppendLine();
            if (report.ProposedRenames.Count == 0)
            {
                builder.AppendLine("- none");
            }
            else
            {
                foreach (var entry in report.ProposedRenames.OrderBy(entry => entry.Path, StringComparer.Ordinal))
                {
                    string action = applyChanges && entry.WasRenamed ? "renamed" : "proposed";
                    if (entry.CurrentName == entry.ProposedName)
                        action = "already-matched";
                    builder.AppendLine($"- {action}: `{entry.DisplayPath}`");
                    builder.AppendLine($"  current: `{entry.CurrentName}`");
                    builder.AppendLine($"  target: `{entry.ProposedName}`");
                }
            }

            AppendSection(builder, "Skipped Objects", report.Skipped);
            AppendSection(builder, "Duplicate Cases", report.Duplicates);
            AppendSection(builder, "Ambiguous Cases", report.Ambiguous);
            return builder.ToString();
        }

        static void AppendSection(StringBuilder builder, string title, List<ReportEntry> entries)
        {
            builder.AppendLine();
            builder.AppendLine($"## {title}");
            builder.AppendLine();
            if (entries.Count == 0)
            {
                builder.AppendLine("- none");
                return;
            }

            foreach (var entry in entries.OrderBy(item => item.Path, StringComparer.Ordinal))
            {
                builder.AppendLine($"- `{entry.DisplayPath}`");
                builder.AppendLine($"  reason: {entry.Reason}");
                builder.AppendLine($"  transform: {entry.TransformSummary}");
            }
        }

        static string WriteReport(string text)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            string reportPath = Path.Combine(projectRoot, ReportRelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath) ?? projectRoot);
            File.WriteAllText(reportPath, text);
            return reportPath;
        }

        sealed class SceneContext
        {
            public UnityEngine.SceneManagement.Scene Scene;
            public Transform RedRow;
            public Transform Walls;
            public List<Transform> ScannedObjects;
            public float FrontX;
            public float BackX;
            public float LeftZ;
            public float RightZ;
            public float CenterX;
            public float CenterZ;
            public float EdgeXTolerance;
            public float EdgeZTolerance;
            public float CenterXTolerance;
            public float CenterZTolerance;
        }

        sealed class Candidate
        {
            public Candidate(SceneContext context, Transform transform)
            {
                Transform = transform;
                InstanceId = transform.gameObject.GetInstanceID();
                CurrentName = transform.name;
                Path = GetPath(transform);
                DisplayPath = $"{Path} [id:{InstanceId}]";
                LocalPosition = context.RedRow.InverseTransformPoint(transform.position);
                LocalEuler = transform.localEulerAngles;
                LocalScale = transform.localScale;
            }

            public Transform Transform { get; }
            public int InstanceId { get; }
            public string CurrentName { get; }
            public string Path { get; }
            public string DisplayPath { get; }
            public Vector3 LocalPosition { get; }
            public Vector3 LocalEuler { get; }
            public Vector3 LocalScale { get; }
        }

        sealed class RenameEntry
        {
            public RenameEntry(Candidate candidate, string proposedName)
            {
                Transform = candidate.Transform;
                Path = candidate.Path;
                DisplayPath = candidate.DisplayPath;
                CurrentName = candidate.CurrentName;
                ProposedName = proposedName;
                CanApply = true;
            }

            public Transform Transform { get; }
            public string Path { get; }
            public string DisplayPath { get; }
            public string CurrentName { get; }
            public string ProposedName { get; }
            public bool CanApply { get; }
            public bool WasRenamed { get; set; }
        }

        sealed class ReportEntry
        {
            public ReportEntry(Candidate candidate, string reason)
            {
                Path = candidate.Path;
                DisplayPath = candidate.DisplayPath;
                Reason = reason;
                TransformSummary =
                    $"instanceId={candidate.InstanceId}, " +
                    $"localPos=({candidate.LocalPosition.x:F2}, {candidate.LocalPosition.y:F2}, {candidate.LocalPosition.z:F2}), " +
                    $"localRot=({candidate.LocalEuler.x:F2}, {candidate.LocalEuler.y:F2}, {candidate.LocalEuler.z:F2}), " +
                    $"localScale=({candidate.LocalScale.x:F2}, {candidate.LocalScale.y:F2}, {candidate.LocalScale.z:F2})";
            }

            public string Path { get; }
            public string DisplayPath { get; }
            public string Reason { get; }
            public string TransformSummary { get; }
        }

        sealed class RenameReport
        {
            public List<RenameEntry> ProposedRenames { get; } = new();
            public List<ReportEntry> Skipped { get; } = new();
            public List<ReportEntry> Duplicates { get; } = new();
            public List<ReportEntry> Ambiguous { get; } = new();
        }

        enum ObjectKind
        {
            Other,
            Wall,
            Tower,
            Gate,
        }

        static string GetPath(Transform transform)
        {
            var parts = new Stack<string>();
            var current = transform;
            while (current != null)
            {
                parts.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", parts);
        }
    }
}
#endif
