using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CastleDefender.UI
{
    [RequireComponent(typeof(CanvasRenderer))]
    public class LineGraphUI : MaskableGraphic
    {
        [Header("Appearance")]
        public Color[] LineColors = { new Color(0.24f, 0.50f, 0.92f), new Color(0.86f, 0.25f, 0.22f), new Color(0.92f, 0.74f, 0.20f), new Color(0.20f, 0.72f, 0.42f) };
        public float LineThickness = 2f;
        public Color BackgroundColor = new Color(0f, 0f, 0f, 0.45f);
        public int GridLineCount = 4;

        [Header("Annotation Overlay")]
        public Color AnnotationColor = new Color(0.93f, 0.95f, 0.98f, 0.96f);
        public int MaxXAxisLabels = 4;
        public float PlotPaddingLeft = 82f;
        public float PlotPaddingRight = 18f;
        public float PlotPaddingTop = 52f;
        public float PlotPaddingBottom = 38f;

        [Header("Legend (optional - one entry per series)")]
        public GameObject[] LegendEntries;

        float[][] _series;
        string[] _labels;
        string _chartTitle;
        string _valueFormat = "F0";
        string[] _xAxisLabels;
        Color[] _runtimeLineColors;

        RectTransform _annotationRoot;
        TextMeshProUGUI _titleText;
        TextMeshProUGUI _legendSummaryText;
        TextMeshProUGUI _emptyStateText;
        readonly List<TextMeshProUGUI> _yAxisTexts = new();
        readonly List<TextMeshProUGUI> _xAxisTexts = new();

        public void SetData(
            float[][] series,
            string[] labels,
            string chartTitle = null,
            string valueFormat = "F0",
            string[] xAxisLabels = null,
            Color[] lineColors = null)
        {
            _series = series;
            _labels = labels;
            _chartTitle = chartTitle ?? string.Empty;
            _valueFormat = string.IsNullOrWhiteSpace(valueFormat) ? "F0" : valueFormat;
            _xAxisLabels = xAxisLabels;
            _runtimeLineColors = lineColors;
            EnsureAnnotationObjects();
            RefreshAnnotations();
            SetVerticesDirty();
            UpdateLegend();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            EnsureAnnotationObjects();
            RefreshAnnotations();
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            RefreshAnnotations();
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var fullRect = GetPixelAdjustedRect();
            AddQuad(vh, fullRect.min, fullRect.max, BackgroundColor);

            if (!TryGetSeriesMetrics(out var yMin, out var yMax, out var pointCount))
                return;

            var plotRect = GetPlotRect(fullRect);
            var gridColor = new Color(1f, 1f, 1f, 0.10f);
            var axisColor = new Color(1f, 1f, 1f, 0.18f);

            AddQuad(vh, plotRect.min, plotRect.max, new Color(1f, 1f, 1f, 0.03f));

            var xLabelIndices = BuildXAxisLabelIndices(pointCount);
            for (int i = 0; i < xLabelIndices.Count; i++)
            {
                int index = xLabelIndices[i];
                float x = GetXPosition(index, pointCount, plotRect);
                AddVerticalLine(vh, x, plotRect.yMin, plotRect.yMax, 1f, i == 0 || i == xLabelIndices.Count - 1 ? axisColor : gridColor);
            }

            for (int gridIndex = 0; gridIndex <= GridLineCount; gridIndex++)
            {
                float t = GridLineCount <= 0 ? 0f : gridIndex / (float)GridLineCount;
                float y = Mathf.Lerp(plotRect.yMin, plotRect.yMax, t);
                AddHorizontalLine(vh, plotRect.xMin, plotRect.xMax, y, 1f, gridIndex == 0 || gridIndex == GridLineCount ? axisColor : gridColor);
            }

            for (int seriesIndex = 0; seriesIndex < _series.Length; seriesIndex++)
            {
                var currentSeries = _series[seriesIndex];
                if (currentSeries == null || currentSeries.Length == 0)
                    continue;

                Color lineColor = ResolveSeriesColor(seriesIndex);

                for (int pointIndex = 0; pointIndex < currentSeries.Length - 1; pointIndex++)
                {
                    float x0 = GetXPosition(pointIndex, pointCount, plotRect);
                    float x1 = GetXPosition(pointIndex + 1, pointCount, plotRect);
                    float y0 = GetYPosition(currentSeries[pointIndex], yMin, yMax, plotRect);
                    float y1 = GetYPosition(currentSeries[pointIndex + 1], yMin, yMax, plotRect);
                    AddSegment(vh, new Vector2(x0, y0), new Vector2(x1, y1), LineThickness, lineColor);
                }

                for (int pointIndex = 0; pointIndex < currentSeries.Length; pointIndex++)
                {
                    float x = GetXPosition(pointIndex, pointCount, plotRect);
                    float y = GetYPosition(currentSeries[pointIndex], yMin, yMax, plotRect);
                    float dotRadius = LineThickness * 1.6f;
                    AddQuad(
                        vh,
                        new Vector2(x - dotRadius, y - dotRadius),
                        new Vector2(x + dotRadius, y + dotRadius),
                        lineColor);
                }
            }
        }

        void EnsureAnnotationObjects()
        {
            if (_annotationRoot == null)
            {
                var root = transform.Find("AnnotationRoot");
                if (root != null)
                {
                    _annotationRoot = root.GetComponent<RectTransform>();
                }
                else
                {
                    var go = new GameObject("AnnotationRoot", typeof(RectTransform));
                    go.transform.SetParent(transform, false);
                    _annotationRoot = go.GetComponent<RectTransform>();
                    _annotationRoot.anchorMin = Vector2.zero;
                    _annotationRoot.anchorMax = Vector2.one;
                    _annotationRoot.offsetMin = Vector2.zero;
                    _annotationRoot.offsetMax = Vector2.zero;
                    _annotationRoot.SetAsLastSibling();
                }
            }

            _titleText ??= CreateAnnotationText("Title", 14f, TextAlignmentOptions.TopLeft, FontStyles.Bold);
            _legendSummaryText ??= CreateAnnotationText("LegendSummary", 12f, TextAlignmentOptions.TopRight, FontStyles.Normal);
            _emptyStateText ??= CreateAnnotationText("EmptyState", 12f, TextAlignmentOptions.Center, FontStyles.Normal);

            EnsureTextCount(_yAxisTexts, 3, "YAxisLabel", 12f, TextAlignmentOptions.MidlineRight, FontStyles.Normal);
            EnsureTextCount(_xAxisTexts, Mathf.Max(1, MaxXAxisLabels), "XAxisLabel", 12f, TextAlignmentOptions.Top, FontStyles.Normal);
        }

        void RefreshAnnotations()
        {
            if (_annotationRoot == null)
                return;

            var fullRect = GetPixelAdjustedRect();
            var plotRect = GetPlotRect(fullRect);
            Vector2 offset = new Vector2(-fullRect.xMin, -fullRect.yMin);

            if (_titleText != null)
            {
                bool hasTitle = !string.IsNullOrWhiteSpace(_chartTitle);
                _titleText.gameObject.SetActive(hasTitle);
                if (hasTitle)
                {
                    _titleText.text = _chartTitle;
                    SetTextRect(
                        _titleText.rectTransform,
                        new Vector2(plotRect.xMin, fullRect.yMax - 8f) + offset,
                        new Vector2(Mathf.Max(180f, plotRect.width * 0.45f), 22f),
                        new Vector2(0f, 1f));
                }
            }

            if (!TryGetSeriesMetrics(out var yMin, out var yMax, out var pointCount))
            {
                SetTextListActive(_yAxisTexts, false);
                SetTextListActive(_xAxisTexts, false);
                if (_legendSummaryText != null)
                    _legendSummaryText.gameObject.SetActive(false);
                if (_emptyStateText != null)
                {
                    _emptyStateText.gameObject.SetActive(true);
                    _emptyStateText.text = "No chart values were recorded.";
                    SetTextRect(
                        _emptyStateText.rectTransform,
                        new Vector2(fullRect.center.x, fullRect.center.y) + offset,
                        new Vector2(Mathf.Max(200f, plotRect.width), 24f),
                        new Vector2(0.5f, 0.5f));
                }
                return;
            }

            if (_emptyStateText != null)
                _emptyStateText.gameObject.SetActive(false);

            if (_legendSummaryText != null)
            {
                _legendSummaryText.gameObject.SetActive(true);
                _legendSummaryText.text = BuildLegendSummary();
                int lineCount = Mathf.Max(1, CountLegendLines());
                SetTextRect(
                    _legendSummaryText.rectTransform,
                    new Vector2(plotRect.xMax, fullRect.yMax - 8f) + offset,
                    new Vector2(Mathf.Max(220f, plotRect.width * 0.42f), 18f * lineCount + 6f),
                    new Vector2(1f, 1f));
            }

            SetYAxisLabels(yMin, yMax, plotRect, offset);
            SetXAxisLabels(pointCount, plotRect, offset);
        }

        void SetYAxisLabels(float yMin, float yMax, Rect plotRect, Vector2 offset)
        {
            if (_yAxisTexts.Count < 3)
                return;

            float midValue = (yMin + yMax) * 0.5f;
            float[] values = { yMax, midValue, yMin };
            float[] positions = { plotRect.yMax, (plotRect.yMin + plotRect.yMax) * 0.5f, plotRect.yMin };

            for (int i = 0; i < _yAxisTexts.Count; i++)
            {
                var text = _yAxisTexts[i];
                text.gameObject.SetActive(true);
                text.text = FormatValue(values[i]);
                SetTextRect(
                    text.rectTransform,
                    new Vector2(plotRect.xMin - 10f, positions[i]) + offset,
                    new Vector2(Mathf.Max(48f, PlotPaddingLeft - 14f), 20f),
                    new Vector2(1f, 0.5f));
            }
        }

        void SetXAxisLabels(int pointCount, Rect plotRect, Vector2 offset)
        {
            var indices = BuildXAxisLabelIndices(pointCount);
            for (int i = 0; i < _xAxisTexts.Count; i++)
            {
                bool active = i < indices.Count;
                _xAxisTexts[i].gameObject.SetActive(active);
                if (!active)
                    continue;

                int pointIndex = indices[i];
                _xAxisTexts[i].text = GetXAxisLabel(pointIndex);
                SetTextRect(
                    _xAxisTexts[i].rectTransform,
                    new Vector2(GetXPosition(pointIndex, pointCount, plotRect), plotRect.yMin - 8f) + offset,
                    new Vector2(72f, 20f),
                    new Vector2(0.5f, 1f));
            }
        }

        string BuildLegendSummary()
        {
            if (_series == null || _series.Length == 0)
                return string.Empty;

            var builder = new StringBuilder();
            for (int seriesIndex = 0; seriesIndex < _series.Length; seriesIndex++)
            {
                var currentSeries = _series[seriesIndex];
                if (currentSeries == null || currentSeries.Length == 0)
                    continue;

                if (builder.Length > 0)
                    builder.AppendLine();

                string seriesLabel = GetSeriesLabel(seriesIndex);
                Color seriesColor = ResolveSeriesColor(seriesIndex);
                string colorHex = ColorUtility.ToHtmlStringRGB(seriesColor);
                float startValue = currentSeries[0];
                float endValue = currentSeries[currentSeries.Length - 1];
                builder
                    .Append("<color=#")
                    .Append(colorHex)
                    .Append("><b>")
                    .Append(seriesLabel)
                    .Append("</b></color>  ")
                    .Append("Start ")
                    .Append(FormatValue(startValue));

                if (currentSeries.Length > 1)
                    builder.Append("  Final ").Append(FormatValue(endValue));
            }

            return builder.ToString();
        }

        int CountLegendLines()
        {
            if (_series == null)
                return 0;

            int count = 0;
            for (int i = 0; i < _series.Length; i++)
            {
                if (_series[i] != null && _series[i].Length > 0)
                    count++;
            }
            return count;
        }

        List<int> BuildXAxisLabelIndices(int pointCount)
        {
            var indices = new List<int>();
            if (pointCount <= 0)
                return indices;

            if (pointCount == 1)
            {
                indices.Add(0);
                return indices;
            }

            int targetCount = Mathf.Clamp(MaxXAxisLabels, 2, 6);
            if (pointCount <= targetCount)
            {
                for (int i = 0; i < pointCount; i++)
                    indices.Add(i);
                return indices;
            }

            int lastIndex = pointCount - 1;
            var used = new HashSet<int>();
            for (int i = 0; i < targetCount; i++)
            {
                int index = Mathf.RoundToInt(lastIndex * (i / (float)(targetCount - 1)));
                if (used.Add(index))
                    indices.Add(index);
            }

            if (!used.Contains(lastIndex))
                indices.Add(lastIndex);

            indices.Sort();
            return indices;
        }

        bool TryGetSeriesMetrics(out float yMin, out float yMax, out int pointCount)
        {
            yMin = float.MaxValue;
            yMax = float.MinValue;
            pointCount = 0;
            bool foundValue = false;

            if (_series == null || _series.Length == 0)
                return false;

            foreach (var currentSeries in _series)
            {
                if (currentSeries == null)
                    continue;

                pointCount = Mathf.Max(pointCount, currentSeries.Length);
                foreach (float value in currentSeries)
                {
                    if (!float.IsFinite(value))
                        continue;

                    yMin = Mathf.Min(yMin, value);
                    yMax = Mathf.Max(yMax, value);
                    foundValue = true;
                }
            }

            if (!foundValue || pointCount <= 0)
                return false;

            if (Mathf.Approximately(yMin, yMax))
            {
                if (Mathf.Approximately(yMin, 0f))
                {
                    yMin = 0f;
                    yMax = 1f;
                }
                else
                {
                    float padding = Mathf.Max(1f, Mathf.Abs(yMin) * 0.1f);
                    yMin -= padding;
                    yMax += padding;
                }
            }

            return true;
        }

        Rect GetPlotRect(Rect fullRect)
        {
            float left = Mathf.Clamp(PlotPaddingLeft, 36f, Mathf.Max(36f, fullRect.width * 0.45f));
            float right = Mathf.Clamp(PlotPaddingRight, 12f, Mathf.Max(12f, fullRect.width * 0.25f));
            float top = Mathf.Clamp(PlotPaddingTop, 28f, Mathf.Max(28f, fullRect.height * 0.35f));
            float bottom = Mathf.Clamp(PlotPaddingBottom, 24f, Mathf.Max(24f, fullRect.height * 0.28f));
            return new Rect(
                fullRect.xMin + left,
                fullRect.yMin + bottom,
                Mathf.Max(1f, fullRect.width - left - right),
                Mathf.Max(1f, fullRect.height - top - bottom));
        }

        float GetXPosition(int pointIndex, int pointCount, Rect plotRect)
        {
            if (pointCount <= 1)
                return plotRect.center.x;
            return plotRect.xMin + plotRect.width * pointIndex / (pointCount - 1f);
        }

        float GetYPosition(float value, float yMin, float yMax, Rect plotRect)
        {
            if (Mathf.Approximately(yMax, yMin))
                return plotRect.center.y;
            return plotRect.yMin + plotRect.height * (value - yMin) / (yMax - yMin);
        }

        string GetXAxisLabel(int pointIndex)
        {
            if (_xAxisLabels != null && pointIndex >= 0 && pointIndex < _xAxisLabels.Length && !string.IsNullOrWhiteSpace(_xAxisLabels[pointIndex]))
                return _xAxisLabels[pointIndex];
            return $"P{pointIndex + 1}";
        }

        string GetSeriesLabel(int seriesIndex)
        {
            if (_labels != null && seriesIndex >= 0 && seriesIndex < _labels.Length && !string.IsNullOrWhiteSpace(_labels[seriesIndex]))
                return _labels[seriesIndex];
            return $"Series {seriesIndex + 1}";
        }

        string FormatValue(float value)
        {
            return value.ToString(_valueFormat, CultureInfo.InvariantCulture);
        }

        Color ResolveSeriesColor(int seriesIndex)
        {
            if (_runtimeLineColors != null && seriesIndex >= 0 && seriesIndex < _runtimeLineColors.Length)
                return _runtimeLineColors[seriesIndex];
            if (LineColors != null && seriesIndex >= 0 && seriesIndex < LineColors.Length)
                return LineColors[seriesIndex];
            return Color.white;
        }

        void UpdateLegend()
        {
            if (LegendEntries == null)
                return;

            for (int i = 0; i < LegendEntries.Length; i++)
            {
                if (LegendEntries[i] == null)
                    continue;

                bool active = _series != null && i < _series.Length;
                LegendEntries[i].SetActive(active);
                if (!active)
                    continue;

                var image = LegendEntries[i].GetComponentInChildren<Image>();
                if (image != null)
                    image.color = ResolveSeriesColor(i);

                var label = LegendEntries[i].GetComponentInChildren<TMP_Text>();
                if (label != null)
                    label.text = GetSeriesLabel(i);
            }
        }

        TextMeshProUGUI CreateAnnotationText(string name, float fontSize, TextAlignmentOptions alignment, FontStyles fontStyle)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(_annotationRoot, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            if (TMP_Settings.defaultFontAsset != null)
                text.font = TMP_Settings.defaultFontAsset;
            text.fontSize = Mathf.Max(12f, fontSize);
            text.fontStyle = fontStyle;
            text.color = AnnotationColor;
            text.alignment = alignment;
            text.richText = true;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            return text;
        }

        void EnsureTextCount(List<TextMeshProUGUI> texts, int count, string prefix, float fontSize, TextAlignmentOptions alignment, FontStyles fontStyle)
        {
            while (texts.Count < count)
            {
                var text = CreateAnnotationText($"{prefix}_{texts.Count}", fontSize, alignment, fontStyle);
                texts.Add(text);
            }
        }

        static void SetTextListActive(List<TextMeshProUGUI> texts, bool active)
        {
            for (int i = 0; i < texts.Count; i++)
            {
                if (texts[i] != null)
                    texts[i].gameObject.SetActive(active);
            }
        }

        static void SetTextRect(RectTransform rectTransform, Vector2 anchoredPosition, Vector2 sizeDelta, Vector2 pivot)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.zero;
            rectTransform.pivot = pivot;
            rectTransform.anchoredPosition = anchoredPosition;
            rectTransform.sizeDelta = sizeDelta;
        }

        static void AddQuad(VertexHelper vh, Vector2 min, Vector2 max, Color color)
        {
            int baseIndex = vh.currentVertCount;
            vh.AddVert(new Vector3(min.x, min.y), color, Vector2.zero);
            vh.AddVert(new Vector3(max.x, min.y), color, Vector2.zero);
            vh.AddVert(new Vector3(max.x, max.y), color, Vector2.zero);
            vh.AddVert(new Vector3(min.x, max.y), color, Vector2.zero);
            vh.AddTriangle(baseIndex, baseIndex + 1, baseIndex + 2);
            vh.AddTriangle(baseIndex, baseIndex + 2, baseIndex + 3);
        }

        static void AddSegment(VertexHelper vh, Vector2 start, Vector2 end, float thickness, Color color)
        {
            var direction = (end - start).normalized;
            if (direction.sqrMagnitude <= 0f)
                direction = Vector2.right;
            var perpendicular = new Vector2(-direction.y, direction.x) * (thickness * 0.5f);
            int baseIndex = vh.currentVertCount;
            vh.AddVert(new Vector3(start.x - perpendicular.x, start.y - perpendicular.y), color, Vector2.zero);
            vh.AddVert(new Vector3(end.x - perpendicular.x, end.y - perpendicular.y), color, Vector2.zero);
            vh.AddVert(new Vector3(end.x + perpendicular.x, end.y + perpendicular.y), color, Vector2.zero);
            vh.AddVert(new Vector3(start.x + perpendicular.x, start.y + perpendicular.y), color, Vector2.zero);
            vh.AddTriangle(baseIndex, baseIndex + 1, baseIndex + 2);
            vh.AddTriangle(baseIndex, baseIndex + 2, baseIndex + 3);
        }

        static void AddHorizontalLine(VertexHelper vh, float xMin, float xMax, float y, float thickness, Color color)
        {
            AddQuad(vh, new Vector2(xMin, y - thickness * 0.5f), new Vector2(xMax, y + thickness * 0.5f), color);
        }

        static void AddVerticalLine(VertexHelper vh, float x, float yMin, float yMax, float thickness, Color color)
        {
            AddQuad(vh, new Vector2(x - thickness * 0.5f, yMin), new Vector2(x + thickness * 0.5f, yMax), color);
        }
    }
}
