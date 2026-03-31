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
    public static class NormalizeLiveTeamBaseLayouts
    {
        const string Menu = "Castle Defender/Environment/Audit And Normalize Live Team Base Layouts";
        const string ReportPath = "projects/live-team-base-normalization-report.md";
        static readonly string[] Teams = { "Blue", "Yellow", "Green" };
        static readonly string[] Cats = { "Tier1_Economy", "Tier2_Barracks", "Tier3_Tech", "Tier4_Advanced", "Economy_Advanced", "Walls" };
        static readonly string[] Marks = { "SpawnPoint", "Marker_1", "Marker_2", "Marker_3", "Marker_4", "Marker_5" };

        [MenuItem(Menu)]
        static void Run()
        {
            var env = FindEnv();
            var red = env ? env.transform.Find("Red_Row") : null;
            if (!env || !red)
            {
                Debug.LogError("[NormalizeLiveTeamBaseLayouts] Open the live scene that contains Map/GameEnvironment first.");
                return;
            }

            var report = new StringBuilder();
            report.AppendLine("# Live Team Base Layout Normalization Report\n");
            report.AppendLine($"Environment Root: `{PathOf(env.transform)}`\n");
            report.AppendLine("Red is the source of truth and is not modified.\n");

            var audits = new List<Audit>();
            bool blocked = false;
            foreach (var team in Teams)
            {
                var row = env.transform.Find(team + "_Row");
                var audit = AuditRow(team, red, row);
                audits.Add(audit);
                AppendAudit(report, audit, "Pre-Change Audit");
                blocked |= audit.Ambiguous.Count > 0;
            }

            if (blocked)
            {
                report.AppendLine("## Outcome\n");
                report.AppendLine("- normalization aborted before edits because at least one prefab/object reference was ambiguous\n");
                WriteReport(report.ToString());
                Debug.LogWarning("[NormalizeLiveTeamBaseLayouts] Aborted before edits. See report for exact object paths.");
                return;
            }

            Undo.IncrementCurrentGroup();
            var undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Normalize Live Team Base Layouts");

            foreach (var team in Teams)
            {
                var row = env.transform.Find(team + "_Row");
                var sum = FixRow(team, red, row);
                var after = AuditRow(team, red, row);
                AppendFix(report, sum, after);
            }

            Undo.CollapseUndoOperations(undo);
            EditorSceneManager.MarkSceneDirty(env.scene);
            report.AppendLine("## Final Confirmation\n");
            report.AppendLine("- Red_Row was left untouched");
            var finalOk = Teams.All(t => AuditRow(t, red, env.transform.Find(t + "_Row")).Exact);
            report.AppendLine($"- all three non-red teams now match Red exactly in composition and progression state: `{finalOk}`\n");
            WriteReport(report.ToString());
            Debug.Log("[NormalizeLiveTeamBaseLayouts] Complete. Review the generated report.");
        }

        static GameObject FindEnv()
        {
            var map = GameObject.Find("Map");
            if (map)
            {
                var child = map.transform.Find("GameEnvironment");
                if (child) return child.gameObject;
            }
            return Resources.FindObjectsOfTypeAll<GameObject>().FirstOrDefault(g => g && g.scene.IsValid() && g.scene.isLoaded && g.name == "GameEnvironment");
        }

        static Audit AuditRow(string team, Transform redRow, Transform row)
        {
            var a = new Audit(team);
            if (!row) { a.Ambiguous.Add($"missing row `{team}_Row`"); a.Update(); return a; }
            foreach (var catName in Cats)
            {
                var redCat = redRow.Find(catName);
                var cat = row.Find(catName);
                if (!redCat) { a.Ambiguous.Add($"missing Red category `{PathOf(redRow)}/{catName}`"); continue; }
                if (!cat) { a.Missing.Add($"missing category `{PathOf(row)}/{catName}`"); continue; }
                var desired = new HashSet<string>(StringComparer.Ordinal);
                foreach (Transform redChild in redCat)
                {
                    var name = TeamName(team, redChild.name);
                    desired.Add(name);
                    var hit = cat.Find(name);
                    var moved = hit ? null : FindByName(row, name, cat);
                    var target = hit ? hit : moved;
                    if (!target) { a.Missing.Add($"missing `{PathOf(cat)}/{name}`"); continue; }
                    if (moved) a.Repositioned.Add($"{PathOf(moved)} should move under `{PathOf(cat)}`");
                    if (!SameVariant(redChild.gameObject, target.gameObject)) a.Wrong.Add($"{PathOf(target)} variant differs from `{redChild.name}`");
                    if (!SameXform(redChild, target)) a.Repositioned.Add($"{PathOf(target)} local transform differs from Red");
                    if (catName == "Tier2_Barracks") AuditMarks(a, redChild, target);
                }
                foreach (Transform child in cat) if (!desired.Contains(child.name) && child.name != "__Holding") a.Extras.Add(PathOf(child));
            }
            a.Update();
            return a;
        }

        static void AuditMarks(Audit a, Transform redBarracks, Transform barracks)
        {
            foreach (var m in Marks)
            {
                var red = redBarracks.Find(m);
                var cur = barracks.Find(m);
                if (!red) a.Ambiguous.Add($"missing Red marker `{PathOf(redBarracks)}/{m}`");
                else if (!cur) a.Missing.Add($"missing `{PathOf(barracks)}/{m}`");
                else if (!SameXform(red, cur)) a.Repositioned.Add($"{PathOf(cur)} local transform differs from Red");
            }
        }

        static Summary FixRow(string team, Transform redRow, Transform row)
        {
            var s = new Summary(team);
            foreach (var catName in Cats)
            {
                var redCat = redRow.Find(catName);
                var cat = row.Find(catName);
                if (!redCat || !cat) { if (redCat && !cat) s.Ambiguous.Add($"missing category `{PathOf(row)}/{catName}`"); continue; }
                var desired = new HashSet<string>(StringComparer.Ordinal);
                foreach (Transform redChild in redCat)
                {
                    var name = TeamName(team, redChild.name);
                    desired.Add(name);
                    var cur = cat.Find(name) ?? FindByName(row, name, cat) ?? SpawnExact(name, cat, s);
                    if (!cur) { s.Ambiguous.Add($"could not resolve `{PathOf(cat)}/{name}`"); continue; }
                    if (!SameVariant(redChild.gameObject, cur.gameObject))
                    {
                        var swapped = ReplaceWithExactPrefab(cat, cur, name, s);
                        if (swapped) cur = swapped;
                        else s.Ambiguous.Add($"exact variant replacement unclear for `{PathOf(cur)}`");
                    }
                    if (cur.parent != cat) { Undo.SetTransformParent(cur, cat, "Normalize Team Layout Parent"); s.Repositioned.Add($"{PathOf(cur)} reparented to `{PathOf(cat)}`"); }
                    if (cur.name != name) { Undo.RecordObject(cur.gameObject, "Rename Team Layout Object"); cur.name = name; }
                    MatchXform(redChild, cur, s.Repositioned);
                    if (catName == "Tier2_Barracks") FixMarks(redChild, cur, s);
                }
                MoveExtras(row, cat, desired, s);
            }
            return s;
        }

        static void FixMarks(Transform redBarracks, Transform barracks, Summary s)
        {
            foreach (var m in Marks)
            {
                var red = redBarracks.Find(m);
                if (!red) { s.Ambiguous.Add($"missing Red marker `{PathOf(redBarracks)}/{m}`"); continue; }
                var cur = barracks.Find(m);
                if (!cur)
                {
                    var go = UnityEngine.Object.Instantiate(red.gameObject, barracks);
                    Undo.RegisterCreatedObjectUndo(go, "Create Barracks Marker");
                    go.name = m;
                    cur = go.transform;
                    s.Added.Add($"{PathOf(cur)} duplicated from Red marker");
                }
                MatchXform(red, cur, s.Repositioned);
            }
        }

        static Transform SpawnExact(string exactName, Transform parent, Summary s)
        {
            var sceneObj = FindByName(parent.root, exactName, null);
            if (sceneObj) { s.Added.Add($"{exactName} restored from existing `{PathOf(sceneObj)}`"); return sceneObj; }
            foreach (var guid in AssetDatabase.FindAssets($"t:Prefab {exactName}"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (!prefab || prefab.name != exactName) continue;
                var go = PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject;
                if (!go) break;
                Undo.RegisterCreatedObjectUndo(go, "Instantiate Team Layout Object");
                go.name = exactName;
                s.Added.Add($"{exactName} instantiated from `{path}`");
                return go.transform;
            }
            return null;
        }

        static Transform ReplaceWithExactPrefab(Transform parent, Transform current, string exactName, Summary s)
        {
            foreach (var guid in AssetDatabase.FindAssets($"t:Prefab {exactName}"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (!prefab || prefab.name != exactName) continue;
                var go = PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject;
                if (!go) continue;
                Undo.RegisterCreatedObjectUndo(go, "Swap Team Layout Variant");
                go.name = exactName;
                go.transform.SetSiblingIndex(current.GetSiblingIndex());
                go.transform.localPosition = current.localPosition;
                go.transform.localRotation = current.localRotation;
                go.transform.localScale = current.localScale;
                Undo.RecordObject(current.gameObject, "Stage Old Team Layout Variant");
                current.name = "__Old_" + exactName;
                s.Swapped.Add($"{PathOf(current)} swapped with prefab `{path}`");
                return go.transform;
            }
            return null;
        }

        static void MoveExtras(Transform row, Transform cat, HashSet<string> desired, Summary s)
        {
            var extras = cat.Cast<Transform>().Where(t => t.name != "__Holding" && !desired.Contains(t.name)).ToList();
            if (extras.Count == 0) return;
            var hold = row.Find("__Holding");
            if (!hold)
            {
                var go = new GameObject("__Holding");
                Undo.RegisterCreatedObjectUndo(go, "Create Holding Root");
                hold = go.transform;
                hold.SetParent(row, false);
            }
            var bucket = hold.Find(cat.name);
            if (!bucket)
            {
                var go = new GameObject(cat.name);
                Undo.RegisterCreatedObjectUndo(go, "Create Holding Bucket");
                bucket = go.transform;
                bucket.SetParent(hold, false);
            }
            var anchor = HoldAnchor(row);
            for (int i = 0; i < extras.Count; i++)
            {
                var t = extras[i];
                Undo.SetTransformParent(t, bucket, "Move Extra Layout Object");
                t.localPosition = anchor + new Vector3((i % 4) * 4f, 0f, (i / 4) * 4f);
                t.localRotation = Quaternion.identity;
                t.localScale = Vector3.one;
                PrefabUtility.RecordPrefabInstancePropertyModifications(t);
                s.Moved.Add($"{PathOf(t)} moved to holding area");
            }
        }

        static Vector3 HoldAnchor(Transform row)
        {
            var pos = row.Cast<Transform>().Where(t => t.name != "__Holding").Select(t => t.localPosition).ToList();
            if (pos.Count == 0) return new Vector3(12f, 0f, 12f);
            return new Vector3(pos.Max(p => p.x) + 12f, 0f, pos.Max(p => p.z) + 12f);
        }

        static Transform FindByName(Transform root, string name, Transform skip)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root) continue;
                if (skip && (t == skip || t.IsChildOf(skip))) continue;
                if (t.name == name) return t;
            }
            return null;
        }

        static bool SameVariant(GameObject red, GameObject cur)
        {
            if (!red || !cur || BaseName(red.name) != BaseName(cur.name)) return false;
            var rr = red.GetComponentsInChildren<Renderer>(true).Length;
            var cr = cur.GetComponentsInChildren<Renderer>(true).Length;
            var rt = red.GetComponentsInChildren<Transform>(true).Length;
            var ct = cur.GetComponentsInChildren<Transform>(true).Length;
            return rr == cr && rt == ct;
        }

        static bool SameXform(Transform a, Transform b)
        {
            return Vector3.Distance(a.localPosition, b.localPosition) <= 0.01f &&
                   Quaternion.Angle(a.localRotation, b.localRotation) <= 0.1f &&
                   Vector3.Distance(a.localScale, b.localScale) <= 0.001f;
        }

        static void MatchXform(Transform src, Transform dst, List<string> log)
        {
            if (SameXform(src, dst)) return;
            Undo.RecordObject(dst, "Normalize Team Layout Transform");
            dst.localPosition = src.localPosition;
            dst.localRotation = src.localRotation;
            dst.localScale = src.localScale;
            PrefabUtility.RecordPrefabInstancePropertyModifications(dst);
            log.Add($"{PathOf(dst)} aligned to Red local transform");
        }

        static string TeamName(string team, string redName) => Marks.Contains(redName) ? redName : $"{team}_{BaseName(redName)}";
        static string BaseName(string name)
        {
            foreach (var p in new[] { "Red_", "Blue_", "Yellow_", "Green_" }) if (name.StartsWith(p, StringComparison.Ordinal)) return name.Substring(p.Length);
            return name;
        }

        static string PathOf(Transform t)
        {
            var parts = new Stack<string>();
            while (t) { parts.Push(t.name); t = t.parent; }
            return string.Join("/", parts);
        }

        static void AppendAudit(StringBuilder r, Audit a, string title)
        {
            r.AppendLine($"## {a.Team}\n");
            r.AppendLine($"### {title}\n");
            AddList(r, "missing buildings added", a.Missing);
            AddList(r, "wrong variants swapped", a.Wrong);
            AddList(r, "extra objects moved to holding area", a.Extras);
            AddList(r, "objects repositioned", a.Repositioned);
            AddList(r, "ambiguous cases not auto-fixed", a.Ambiguous);
            r.AppendLine($"- matches Red exactly right now: `{a.Exact}`\n");
        }

        static void AppendFix(StringBuilder r, Summary s, Audit after)
        {
            r.AppendLine($"### {s.Team} Fix Summary\n");
            AddList(r, "missing buildings added", s.Added);
            AddList(r, "wrong variants swapped", s.Swapped);
            AddList(r, "extra objects moved to holding area", s.Moved);
            AddList(r, "objects repositioned", s.Repositioned);
            AddList(r, "ambiguous cases not auto-fixed", s.Ambiguous);
            r.AppendLine($"- matches Red after fix: `{after.Exact}`\n");
        }

        static void AddList(StringBuilder r, string label, List<string> items)
        {
            r.AppendLine($"- {label}:");
            if (items.Count == 0) r.AppendLine("  - none");
            else foreach (var i in items) r.AppendLine($"  - {i}");
        }

        static void WriteReport(string text)
        {
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            var path = Path.Combine(root, ReportPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? root);
            File.WriteAllText(path, text);
            AssetDatabase.Refresh();
            Debug.Log($"[NormalizeLiveTeamBaseLayouts] Report written to {path}");
        }

        sealed class Audit
        {
            public Audit(string team) { Team = team; }
            public string Team;
            public List<string> Missing = new(), Wrong = new(), Extras = new(), Repositioned = new(), Ambiguous = new();
            public bool Exact;
            public void Update() => Exact = Missing.Count == 0 && Wrong.Count == 0 && Extras.Count == 0 && Repositioned.Count == 0 && Ambiguous.Count == 0;
        }

        sealed class Summary
        {
            public Summary(string team) { Team = team; }
            public string Team;
            public List<string> Added = new(), Swapped = new(), Moved = new(), Repositioned = new(), Ambiguous = new();
        }
    }
}
#endif
