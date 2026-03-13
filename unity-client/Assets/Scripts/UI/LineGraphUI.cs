// LineGraphUI.cs — Custom MaskableGraphic line chart for the post-game stats panel.
//
// SETUP:
//   Add LineGraphUI component to a UI RectTransform.
//   Assign LineColors (one per data series) and optional legend label GameObjects.
//
// Usage:
//   lineGraph.SetData(float[][] series, string[] labels)

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace CastleDefender.UI
{
    public class LineGraphUI : MaskableGraphic
    {
        [Header("Appearance")]
        public Color[]  LineColors    = { Color.cyan, Color.yellow, Color.green, Color.red };
        public float    LineThickness = 2f;
        public Color    BackgroundColor = new Color(0f, 0f, 0f, 0.3f);
        public int      GridLineCount   = 4;

        [Header("Legend (optional — one entry per series)")]
        public GameObject[] LegendEntries; // each should have a child Image and TMP_Text

        private float[][]  _series;
        private string[]   _labels;

        // ── Public API ────────────────────────────────────────────────────────

        public void SetData(float[][] series, string[] labels)
        {
            _series = series;
            _labels = labels;
            SetVerticesDirty();
            UpdateLegend();
        }

        // ── MaskableGraphic override ──────────────────────────────────────────

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = GetPixelAdjustedRect();

            // Background
            AddQuad(vh, r.min, r.max, BackgroundColor);

            if (_series == null || _series.Length == 0) return;

            // Compute global Y range across all series
            float yMin = float.MaxValue, yMax = float.MinValue;
            int   xMax = 0;
            foreach (var s in _series)
            {
                if (s == null) continue;
                if (s.Length > xMax) xMax = s.Length;
                foreach (var v in s)
                {
                    if (v < yMin) yMin = v;
                    if (v > yMax) yMax = v;
                }
            }
            if (xMax < 2) return;
            if (Mathf.Approximately(yMax, yMin)) yMax = yMin + 1f;

            float w = r.width;
            float h = r.height;
            float ox = r.xMin;
            float oy = r.yMin;

            // Horizontal grid lines
            var gridCol = new Color(1f, 1f, 1f, 0.1f);
            for (int gi = 1; gi <= GridLineCount; gi++)
            {
                float gy = oy + h * gi / (GridLineCount + 1);
                AddQuad(vh,
                    new Vector2(ox,     gy - 0.5f),
                    new Vector2(ox + w, gy + 0.5f),
                    gridCol);
            }

            // Draw each series
            for (int si = 0; si < _series.Length; si++)
            {
                var s = _series[si];
                if (s == null || s.Length < 2) continue;
                var col = LineColors != null && si < LineColors.Length ? LineColors[si] : Color.white;

                for (int xi = 0; xi < s.Length - 1; xi++)
                {
                    float x0 = ox + w * xi       / (xMax - 1);
                    float x1 = ox + w * (xi + 1) / (xMax - 1);
                    float y0 = oy + h * (s[xi]     - yMin) / (yMax - yMin);
                    float y1 = oy + h * (s[xi + 1] - yMin) / (yMax - yMin);
                    AddSegment(vh, new Vector2(x0, y0), new Vector2(x1, y1), LineThickness, col);
                }

                // Dots at each point
                for (int xi = 0; xi < s.Length; xi++)
                {
                    float px = ox + w * xi / (xMax - 1);
                    float py = oy + h * (s[xi] - yMin) / (yMax - yMin);
                    float r2 = LineThickness * 1.5f;
                    AddQuad(vh,
                        new Vector2(px - r2, py - r2),
                        new Vector2(px + r2, py + r2),
                        col);
                }
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        static void AddQuad(VertexHelper vh, Vector2 min, Vector2 max, Color col)
        {
            int i = vh.currentVertCount;
            vh.AddVert(new Vector3(min.x, min.y), col, Vector2.zero);
            vh.AddVert(new Vector3(max.x, min.y), col, Vector2.zero);
            vh.AddVert(new Vector3(max.x, max.y), col, Vector2.zero);
            vh.AddVert(new Vector3(min.x, max.y), col, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i, i + 2, i + 3);
        }

        static void AddSegment(VertexHelper vh, Vector2 a, Vector2 b, float thickness, Color col)
        {
            var dir = (b - a).normalized;
            var perp = new Vector2(-dir.y, dir.x) * (thickness * 0.5f);
            int i = vh.currentVertCount;
            vh.AddVert(new Vector3(a.x - perp.x, a.y - perp.y), col, Vector2.zero);
            vh.AddVert(new Vector3(b.x - perp.x, b.y - perp.y), col, Vector2.zero);
            vh.AddVert(new Vector3(b.x + perp.x, b.y + perp.y), col, Vector2.zero);
            vh.AddVert(new Vector3(a.x + perp.x, a.y + perp.y), col, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i, i + 2, i + 3);
        }

        void UpdateLegend()
        {
            if (LegendEntries == null) return;
            for (int i = 0; i < LegendEntries.Length; i++)
            {
                if (LegendEntries[i] == null) continue;
                bool active = _series != null && i < _series.Length;
                LegendEntries[i].SetActive(active);
                if (!active) continue;

                var img = LegendEntries[i].GetComponentInChildren<Image>();
                if (img != null && LineColors != null && i < LineColors.Length)
                    img.color = LineColors[i];

                var lbl = LegendEntries[i].GetComponentInChildren<TMP_Text>();
                if (lbl != null && _labels != null && i < _labels.Length)
                    lbl.text = _labels[i];
            }
        }
    }
}
