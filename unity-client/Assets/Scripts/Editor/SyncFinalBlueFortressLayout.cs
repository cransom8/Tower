#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace CastleDefender.Editor
{
    public static class SyncFinalBlueFortressLayout
    {
        const string MenuPath = "Castle Defender/Environment/Sync Final Blue Fortress Layout To All Teams";
        const string ReportRelativePath = "projects/final-blue-fortress-sync-report.md";
        const string GitPrefabAssetPath = "unity-client/Assets/AddressableContent/Environment/GameEnvironment.prefab";

        static readonly string[] TargetTeams = { "Red", "Green", "Yellow" };
        static readonly Regex BlockHeader = new(@"^--- !u!(?<type>\d+) &(?<id>-?\d+)$", RegexOptions.Compiled);
        static readonly Regex FileId = new(@"\{fileID:\s*(?<id>-?\d+)\}", RegexOptions.Compiled);
        static readonly Regex Vector3Regex = new(@"\{x:\s*(?<x>[-+0-9eE\.]+),\s*y:\s*(?<y>[-+0-9eE\.]+),\s*z:\s*(?<z>[-+0-9eE\.]+)\}", RegexOptions.Compiled);
        static readonly Regex QuaternionRegex = new(@"\{x:\s*(?<x>[-+0-9eE\.]+),\s*y:\s*(?<y>[-+0-9eE\.]+),\s*z:\s*(?<z>[-+0-9eE\.]+),\s*w:\s*(?<w>[-+0-9eE\.]+)\}", RegexOptions.Compiled);

        static readonly Dictionary<string, Dictionary<string, string[]>> TeamAliases =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Red"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["barrackscenter"] = new[] { "barrackscenterstartingbarracks" },
                    ["barracksleft"] = new[] { "barracksleftpurchaserequired" },
                    ["barracksright"] = new[] { "barracksrightpurchaserequired" },
                    ["house"] = new[] { "housestartingfortcore" },
                    ["bannera"] = new[] { "banner" },
                },
            };

        [MenuItem(MenuPath)]
        static void Run()
        {
            if (!TryBuildContext(out var context, out string error))
            {
                Debug.LogError($"[SyncFinalBlueFortressLayout] {error}");
                return;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Sync Final Blue Fortress Layout");

            var report = new StringBuilder();
            report.AppendLine("# Final Blue Fortress Sync Report");
            report.AppendLine();
            report.AppendLine($"Scene: `{context.Scene.path}`");
            report.AppendLine($"Environment Root: `{PathOf(context.EnvironmentRoot)}`");
            report.AppendLine($"Source Root: `{PathOf(context.BlueRoot)}`");
            report.AppendLine("Mode: `delta from git HEAD when a baseline exists; duplicate-relative fallback when possible; local-copy fallback for unmatched Blue-only additions`");
            report.AppendLine();

            var summaries = TargetTeams.ToDictionary(team => team, team => new Summary(team), StringComparer.OrdinalIgnoreCase);

            foreach (var source in context.BlueObjects)
            {
                context.Head.ByExact.TryGetValue(source.name, out var headBlue);

                foreach (var team in TargetTeams)
                {
                    var summary = summaries[team];
                    var targetContext = context.Targets[team];
                    if (!TryResolveCurrentTarget(source, team, targetContext, summary, out var target))
                        continue;

                    TransformRecord headTarget = ResolveHeadTarget(team, target.name, context.Head);
                    bool changed = headBlue != null && headTarget != null
                        ? ApplyDeltaFromHead(source, target, headBlue, headTarget, summary)
                        : TryApplyRelativeCopyFromBase(source, target, context.BlueContext, targetContext, summary)
                            || ApplyDirectLocalCopy(source, target, summary);

                    if (!changed)
                        continue;
                }
            }

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(context.Scene);

            foreach (var team in TargetTeams)
                AppendSummary(report, summaries[team]);

            string reportPath = WriteReport(report.ToString());
            Debug.Log(
                $"[SyncFinalBlueFortressLayout] Complete. " +
                string.Join(", ", TargetTeams.Select(team =>
                {
                    var summary = summaries[team];
                    return $"{team}: delta={summary.DeltaApplied.Count}, copied={summary.DirectCopies.Count}, created={summary.Created.Count}, missing={summary.Missing.Count}, ambiguous={summary.Ambiguous.Count}";
                })) +
                $". Report: {reportPath}");
        }

        static bool ApplyDeltaFromHead(
            Transform source,
            Transform target,
            TransformRecord headBlue,
            TransformRecord headTarget,
            Summary summary)
        {
            Vector3 deltaPosition = source.localPosition - headBlue.LocalPosition;
            Quaternion deltaRotation = source.localRotation * Quaternion.Inverse(headBlue.LocalRotation);
            Vector3 scaleRatio = Divide(source.localScale, headBlue.LocalScale);

            Vector3 desiredLocalPosition = headTarget.LocalPosition + deltaPosition;
            Quaternion desiredLocalRotation = deltaRotation * headTarget.LocalRotation;
            Vector3 desiredLocalScale = Multiply(headTarget.LocalScale, scaleRatio);

            bool changed = ApplyLocalState(source, target, desiredLocalPosition, desiredLocalRotation, desiredLocalScale);
            if (changed)
                summary.DeltaApplied.Add($"{PathOf(target)} <= {source.name}");

            return changed;
        }

        static bool ApplyDirectLocalCopy(Transform source, Transform target, Summary summary)
        {
            bool changed = ApplyLocalState(source, target, source.localPosition, source.localRotation, source.localScale);
            if (changed)
                summary.DirectCopies.Add($"{PathOf(target)} <= {source.name}");

            return changed;
        }

        static bool TryApplyRelativeCopyFromBase(
            Transform source,
            Transform target,
            TeamContext blueContext,
            TeamContext targetContext,
            Summary summary)
        {
            if (!HasDuplicateSuffix(source.name))
                return false;

            string blueBaseName = RemoveDuplicateSuffix(source.name);
            if (!TryResolveRelativeBase(blueBaseName, "Blue", blueContext, out var blueBase))
                return false;

            string targetBaseName = ReplaceTeamPrefix(blueBaseName, "Blue", summary.Team);
            if (!TryResolveRelativeBase(targetBaseName, summary.Team, targetContext, out var targetBase))
                return false;

            Vector3 deltaPosition = source.localPosition - blueBase.localPosition;
            Quaternion deltaRotation = source.localRotation * Quaternion.Inverse(blueBase.localRotation);
            Vector3 scaleRatio = Divide(source.localScale, blueBase.localScale);

            Vector3 desiredLocalPosition = targetBase.localPosition + deltaPosition;
            Quaternion desiredLocalRotation = deltaRotation * targetBase.localRotation;
            Vector3 desiredLocalScale = Multiply(targetBase.localScale, scaleRatio);

            bool changed = ApplyLocalState(source, target, desiredLocalPosition, desiredLocalRotation, desiredLocalScale);
            if (changed)
                summary.DirectCopies.Add($"{PathOf(target)} <= relative {targetBase.name} from {source.name}");

            return changed;
        }

        static bool ApplyLocalState(
            Transform source,
            Transform target,
            Vector3 desiredLocalPosition,
            Quaternion desiredLocalRotation,
            Vector3 desiredLocalScale)
        {
            bool changed = false;

            if ((target.localPosition - desiredLocalPosition).sqrMagnitude > 0.0001f)
            {
                Undo.RecordObject(target, "Sync Fortress Layout Local Position");
                target.localPosition = desiredLocalPosition;
                changed = true;
            }

            if (Quaternion.Angle(target.localRotation, desiredLocalRotation) > 0.1f)
            {
                Undo.RecordObject(target, "Sync Fortress Layout Local Rotation");
                target.localRotation = desiredLocalRotation;
                changed = true;
            }

            if ((target.localScale - desiredLocalScale).sqrMagnitude > 0.000001f)
            {
                Undo.RecordObject(target, "Sync Fortress Layout Local Scale");
                target.localScale = desiredLocalScale;
                changed = true;
            }

            if (target.gameObject.activeSelf != source.gameObject.activeSelf)
            {
                Undo.RecordObject(target.gameObject, "Sync Fortress Layout Active State");
                target.gameObject.SetActive(source.gameObject.activeSelf);
                changed = true;
            }

            if (changed)
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(target);
                PrefabUtility.RecordPrefabInstancePropertyModifications(target.gameObject);
            }

            return changed;
        }

        static bool TryResolveCurrentTarget(
            Transform source,
            string team,
            TeamContext context,
            Summary summary,
            out Transform target)
        {
            target = null;

            string expectedName = ReplaceTeamPrefix(source.name, "Blue", team);
            string normalizedExact = NormalizeSemantic(expectedName);
            string normalizedBase = NormalizeSemantic(RemoveDuplicateSuffix(expectedName));

            if (TryFindSingle(context.ByRawName, expectedName, out target, out string rawReason) ||
                TryFindSingle(context.ByNormalized, normalizedExact, out target, out string exactReason) ||
                TryFindAlias(team, normalizedExact, context.ByNormalized, out target, out string aliasReason))
            {
                return true;
            }

            if (TryCreateFromTemplate(expectedName, normalizedBase, team, context, summary, out target))
                return true;

            summary.Missing.Add($"{expectedName} <= {source.name} ({rawReason ?? exactReason ?? aliasReason ?? "no target"})");
            return false;
        }

        static bool TryCreateFromTemplate(
            string expectedName,
            string normalizedBase,
            string team,
            TeamContext context,
            Summary summary,
            out Transform target)
        {
            target = null;
            if (!TryFindTemplate(context, team, normalizedBase, out var template))
                return false;

            var clone = UnityEngine.Object.Instantiate(template.gameObject, template.parent);
            Undo.RegisterCreatedObjectUndo(clone, "Create Missing Team Fortress Piece");
            clone.name = expectedName;
            target = clone.transform;

            RegisterTarget(context, target);
            summary.Created.Add($"{PathOf(target)} cloned from {PathOf(template)}");
            return true;
        }

        static bool TryFindTemplate(TeamContext context, string team, string normalizedBase, out Transform template)
        {
            template = FirstTemplate(context.ByBaseNormalized, normalizedBase);
            if (template != null)
                return true;

            foreach (var alias in AliasKeys(team, normalizedBase))
            {
                template = FirstTemplate(context.ByBaseNormalized, alias);
                if (template != null)
                    return true;
            }

            return false;
        }

        static Transform FirstTemplate(Dictionary<string, List<Transform>> lookup, string key)
        {
            if (string.IsNullOrEmpty(key) || !lookup.TryGetValue(key, out var matches) || matches.Count == 0)
                return null;

            return matches
                .OrderBy(match => HasDuplicateSuffix(match.name) ? 1 : 0)
                .ThenBy(match => match.name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        static bool TryFindAlias(
            string team,
            string normalizedName,
            Dictionary<string, List<Transform>> lookup,
            out Transform target,
            out string reason)
        {
            target = null;
            reason = null;
            foreach (var alias in AliasKeys(team, normalizedName))
            {
                if (TryFindSingle(lookup, alias, out target, out reason))
                    return true;
            }

            return false;
        }

        static bool TryResolveRelativeBase(
            string expectedName,
            string team,
            TeamContext context,
            out Transform target)
        {
            target = null;
            string normalizedExact = NormalizeSemantic(expectedName);
            string normalizedBase = NormalizeSemantic(RemoveDuplicateSuffix(expectedName));

            return TryFindSingle(context.ByRawName, expectedName, out target, out _) ||
                TryFindSingle(context.ByNormalized, normalizedExact, out target, out _) ||
                TryFindAlias(team, normalizedExact, context.ByNormalized, out target, out _) ||
                TryFindSingle(context.ByBaseNormalized, normalizedBase, out target, out _) ||
                TryFindAlias(team, normalizedBase, context.ByBaseNormalized, out target, out _);
        }

        static IEnumerable<string> AliasKeys(string team, string normalizedName)
        {
            if (!TeamAliases.TryGetValue(team, out var teamAliases))
                yield break;

            if (!teamAliases.TryGetValue(normalizedName, out var aliases))
                yield break;

            foreach (var alias in aliases)
                yield return alias;
        }

        static bool TryFindSingle(
            Dictionary<string, List<Transform>> lookup,
            string key,
            out Transform target,
            out string reason)
        {
            target = null;
            reason = null;
            if (string.IsNullOrEmpty(key) || !lookup.TryGetValue(key, out var matches) || matches.Count == 0)
            {
                reason = $"missing key `{key}`";
                return false;
            }

            if (matches.Count > 1)
            {
                reason = $"ambiguous key `{key}`";
                return false;
            }

            target = matches[0];
            return true;
        }

        static TransformRecord ResolveHeadTarget(string team, string targetName, HeadLayout head)
        {
            if (head.ByExact.TryGetValue(targetName, out var exact))
                return exact;

            string normalizedExact = NormalizeSemantic(targetName);
            if (TryFindSingle(head.ByNormalized, normalizedExact, out var normalized))
                return normalized;

            foreach (var alias in AliasKeys(team, normalizedExact))
            {
                if (TryFindSingle(head.ByNormalized, alias, out normalized))
                    return normalized;
            }

            string normalizedBase = NormalizeSemantic(RemoveDuplicateSuffix(targetName));
            if (TryFindSingle(head.ByBaseNormalized, normalizedBase, out normalized))
                return normalized;

            foreach (var alias in AliasKeys(team, normalizedBase))
            {
                if (TryFindSingle(head.ByBaseNormalized, alias, out normalized))
                    return normalized;
            }

            return null;
        }

        static bool TryFindSingle(Dictionary<string, List<TransformRecord>> lookup, string key, out TransformRecord record)
        {
            record = null;
            if (string.IsNullOrEmpty(key) || !lookup.TryGetValue(key, out var matches) || matches.Count == 0)
                return false;

            record = matches.Count == 1
                ? matches[0]
                : matches.OrderBy(match => HasDuplicateSuffix(match.Name) ? 1 : 0).ThenBy(match => match.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            return record != null;
        }

        static bool TryBuildContext(out SyncContext context, out string error)
        {
            context = null;
            error = null;

            var scene = EditorSceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                error = "No loaded active scene is available.";
                return false;
            }

            var map = scene.GetRootGameObjects().FirstOrDefault(go => go.name == "Map");
            if (map == null)
            {
                error = "Could not find root object 'Map' in the active scene.";
                return false;
            }

            var environmentRoot = map.transform.Find("GameEnvironment");
            if (environmentRoot == null)
            {
                error = "Could not find 'Map/GameEnvironment' in the active scene.";
                return false;
            }

            var blueRoot = environmentRoot.Find("Blue Fort");
            if (blueRoot == null)
            {
                error = "Could not find 'Map/GameEnvironment/Blue Fort' in the active scene.";
                return false;
            }

            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            if (!TryReadGitHeadFile(projectRoot, GitPrefabAssetPath, out var headText, out error))
                return false;

            var head = ParseHeadLayout(headText);
            if (head.ByExact.Count == 0)
            {
                error = $"Could not parse any transform records from git HEAD:{GitPrefabAssetPath}.";
                return false;
            }

            var targets = new Dictionary<string, TeamContext>(StringComparer.OrdinalIgnoreCase);
            foreach (var team in TargetTeams)
            {
                var teamRoot = environmentRoot.Find($"{team} Fort");
                if (teamRoot == null)
                {
                    error = $"Could not find 'Map/GameEnvironment/{team} Fort' in the active scene.";
                    return false;
                }

                targets[team] = BuildTeamContext(team, environmentRoot);
            }

            var blueObjects = environmentRoot
                .GetComponentsInChildren<Transform>(true)
                .Where(t => t != blueRoot && t.name.StartsWith("Blue_", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (blueObjects.Count == 0)
            {
                error = "No Blue-prefixed fortress objects were found under Map/GameEnvironment.";
                return false;
            }

            context = new SyncContext
            {
                Scene = scene,
                EnvironmentRoot = environmentRoot,
                BlueRoot = blueRoot,
                BlueObjects = blueObjects,
                BlueContext = BuildTeamContext("Blue", environmentRoot),
                Targets = targets,
                Head = head,
            };
            return true;
        }

        static bool TryReadGitHeadFile(string projectRoot, string assetPath, out string text, out string error)
        {
            text = null;
            error = null;

            try
            {
                var start = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = $"show HEAD:{assetPath.Replace('\\', '/')}",
                    WorkingDirectory = projectRoot,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                using var process = Process.Start(start);
                if (process == null)
                {
                    error = "Failed to start `git show`.";
                    return false;
                }

                text = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(text))
                {
                    error = $"`git show HEAD:{assetPath}` failed: {stderr}".Trim();
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = $"Reading git HEAD for `{assetPath}` failed: {ex.Message}";
                return false;
            }
        }

        static HeadLayout ParseHeadLayout(string yaml)
        {
            var gameObjectNames = new Dictionary<string, string>(StringComparer.Ordinal);
            var transformsByGameObject = new Dictionary<string, TransformRecord>(StringComparer.Ordinal);
            var lines = yaml.Replace("\r\n", "\n").Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                var headerMatch = BlockHeader.Match(lines[i]);
                if (!headerMatch.Success)
                    continue;

                string type = headerMatch.Groups["type"].Value;
                string id = headerMatch.Groups["id"].Value;
                int end = FindBlockEnd(lines, i + 1);

                if (type == "1")
                {
                    string name = FindValue(lines, i, end, "  m_Name: ");
                    if (!string.IsNullOrEmpty(name))
                        gameObjectNames[id] = name;
                }
                else if (type == "4")
                {
                    string gameObjectId = FindFileIdValue(lines, i, end, "  m_GameObject: ");
                    if (string.IsNullOrEmpty(gameObjectId))
                    {
                        i = end - 1;
                        continue;
                    }

                    var localPosition = ParseVector3(FindValue(lines, i, end, "  m_LocalPosition: "));
                    var localRotation = ParseQuaternion(FindValue(lines, i, end, "  m_LocalRotation: "));
                    var localScale = ParseVector3(FindValue(lines, i, end, "  m_LocalScale: "));

                    transformsByGameObject[gameObjectId] = new TransformRecord
                    {
                        GameObjectId = gameObjectId,
                        LocalPosition = localPosition,
                        LocalRotation = localRotation,
                        LocalScale = localScale,
                    };
                }

                i = end - 1;
            }

            var layout = new HeadLayout();
            foreach (var pair in transformsByGameObject)
            {
                if (!gameObjectNames.TryGetValue(pair.Key, out var name))
                    continue;

                pair.Value.Name = name;
                layout.ByExact[name] = pair.Value;
                Add(layout.ByNormalized, NormalizeSemantic(name), pair.Value);
                Add(layout.ByBaseNormalized, NormalizeSemantic(RemoveDuplicateSuffix(name)), pair.Value);
            }

            return layout;
        }

        static int FindBlockEnd(string[] lines, int start)
        {
            for (int i = start; i < lines.Length; i++)
            {
                if (lines[i].StartsWith("--- !u!", StringComparison.Ordinal))
                    return i;
            }

            return lines.Length;
        }

        static string FindValue(string[] lines, int start, int end, string prefix)
        {
            for (int i = start; i < end; i++)
            {
                if (lines[i].StartsWith(prefix, StringComparison.Ordinal))
                    return lines[i].Substring(prefix.Length).Trim();
            }

            return null;
        }

        static string FindFileIdValue(string[] lines, int start, int end, string prefix)
        {
            string line = FindValue(lines, start, end, prefix);
            if (string.IsNullOrEmpty(line))
                return null;

            var match = FileId.Match(line);
            return match.Success ? match.Groups["id"].Value : null;
        }

        static Vector3 ParseVector3(string value)
        {
            if (string.IsNullOrEmpty(value))
                return Vector3.zero;

            var match = Vector3Regex.Match(value);
            return match.Success
                ? new Vector3(ParseFloat(match.Groups["x"].Value), ParseFloat(match.Groups["y"].Value), ParseFloat(match.Groups["z"].Value))
                : Vector3.zero;
        }

        static Quaternion ParseQuaternion(string value)
        {
            if (string.IsNullOrEmpty(value))
                return Quaternion.identity;

            var match = QuaternionRegex.Match(value);
            return match.Success
                ? new Quaternion(
                    ParseFloat(match.Groups["x"].Value),
                    ParseFloat(match.Groups["y"].Value),
                    ParseFloat(match.Groups["z"].Value),
                    ParseFloat(match.Groups["w"].Value))
                : Quaternion.identity;
        }

        static float ParseFloat(string value) =>
            float.Parse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture);

        static TeamContext BuildTeamContext(string team, Transform environmentRoot)
        {
            var context = new TeamContext(team);
            foreach (var transform in environmentRoot.GetComponentsInChildren<Transform>(true))
            {
                if (!transform.name.StartsWith(team + "_", StringComparison.OrdinalIgnoreCase))
                    continue;

                RegisterTarget(context, transform);
            }

            return context;
        }

        static void RegisterTarget(TeamContext context, Transform transform)
        {
            Add(context.ByRawName, transform.name, transform);
            Add(context.ByNormalized, NormalizeSemantic(transform.name), transform);
            Add(context.ByBaseNormalized, NormalizeSemantic(RemoveDuplicateSuffix(transform.name)), transform);
        }

        static void Add<T>(Dictionary<string, List<T>> lookup, string key, T value)
        {
            if (string.IsNullOrEmpty(key))
                return;

            if (!lookup.TryGetValue(key, out var list))
            {
                list = new List<T>();
                lookup[key] = list;
            }

            if (!list.Contains(value))
                list.Add(value);
        }

        static string ReplaceTeamPrefix(string name, string from, string to)
        {
            if (name.StartsWith(from + "_", StringComparison.OrdinalIgnoreCase))
                return to + name.Substring(from.Length);

            return name;
        }

        static string RemoveDuplicateSuffix(string name)
        {
            int open = name.LastIndexOf('(');
            int close = name.LastIndexOf(')');
            if (open < 0 || close <= open)
                return name;

            string inside = name.Substring(open + 1, close - open - 1);
            return inside.All(char.IsDigit) ? name.Substring(0, open).TrimEnd() : name;
        }

        static bool HasDuplicateSuffix(string name)
        {
            int open = name.LastIndexOf('(');
            int close = name.LastIndexOf(')');
            if (open < 0 || close <= open)
                return false;

            string inside = name.Substring(open + 1, close - open - 1);
            return inside.All(char.IsDigit);
        }

        static string NormalizeSemantic(string name)
        {
            name = StripTeamPrefix(name);
            name = RemoveKnownSuffix(name, "_starting Barracks");
            name = RemoveKnownSuffix(name, "_purchase required");
            name = RemoveKnownSuffix(name, "_starting Fort Core");
            name = RemoveKnownSuffix(name, "_road");

            var builder = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                if (char.IsLetterOrDigit(c))
                    builder.Append(char.ToLowerInvariant(c));
            }

            return builder.ToString();
        }

        static string StripTeamPrefix(string name)
        {
            foreach (var prefix in new[] { "Blue_", "Red_", "Green_", "Yellow_" })
            {
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return name.Substring(prefix.Length);
            }

            return name;
        }

        static string RemoveKnownSuffix(string value, string suffix)
        {
            return value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                ? value.Substring(0, value.Length - suffix.Length)
                : value;
        }

        static Vector3 Divide(Vector3 a, Vector3 b) => new(
            SafeDivide(a.x, b.x),
            SafeDivide(a.y, b.y),
            SafeDivide(a.z, b.z));

        static Vector3 Multiply(Vector3 a, Vector3 b) => new(a.x * b.x, a.y * b.y, a.z * b.z);

        static float SafeDivide(float numerator, float denominator) =>
            Mathf.Abs(denominator) <= 0.0001f ? 1f : numerator / denominator;

        static void AppendSummary(StringBuilder report, Summary summary)
        {
            report.AppendLine($"## {summary.Team}");
            report.AppendLine();
            AppendList(report, "delta-applied", summary.DeltaApplied);
            AppendList(report, "direct-local-copies", summary.DirectCopies);
            AppendList(report, "created", summary.Created);
            AppendList(report, "missing", summary.Missing);
            AppendList(report, "ambiguous", summary.Ambiguous);
        }

        static void AppendList(StringBuilder report, string title, List<string> items)
        {
            report.AppendLine($"### {title}");
            report.AppendLine();
            if (items.Count == 0)
            {
                report.AppendLine("- none");
            }
            else
            {
                foreach (var item in items)
                    report.AppendLine($"- {item}");
            }

            report.AppendLine();
        }

        static string WriteReport(string text)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            string reportPath = Path.Combine(projectRoot, ReportRelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath) ?? projectRoot);
            File.WriteAllText(reportPath, text);
            AssetDatabase.Refresh();
            return reportPath;
        }

        static string PathOf(Transform transform)
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

        sealed class SyncContext
        {
            public UnityEngine.SceneManagement.Scene Scene;
            public Transform EnvironmentRoot;
            public Transform BlueRoot;
            public List<Transform> BlueObjects;
            public TeamContext BlueContext;
            public Dictionary<string, TeamContext> Targets;
            public HeadLayout Head;
        }

        sealed class TeamContext
        {
            public TeamContext(string team) { Team = team; }
            public string Team { get; }
            public Dictionary<string, List<Transform>> ByRawName { get; } = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, List<Transform>> ByNormalized { get; } = new(StringComparer.Ordinal);
            public Dictionary<string, List<Transform>> ByBaseNormalized { get; } = new(StringComparer.Ordinal);
        }

        sealed class HeadLayout
        {
            public Dictionary<string, TransformRecord> ByExact { get; } = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, List<TransformRecord>> ByNormalized { get; } = new(StringComparer.Ordinal);
            public Dictionary<string, List<TransformRecord>> ByBaseNormalized { get; } = new(StringComparer.Ordinal);
        }

        sealed class TransformRecord
        {
            public string Name;
            public string GameObjectId;
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
            public Vector3 LocalScale;
        }

        sealed class Summary
        {
            public Summary(string team) { Team = team; }
            public string Team;
            public List<string> DeltaApplied = new();
            public List<string> DirectCopies = new();
            public List<string> Created = new();
            public List<string> Missing = new();
            public List<string> Ambiguous = new();
        }
    }
}
#endif
