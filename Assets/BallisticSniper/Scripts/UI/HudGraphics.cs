using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BallisticSniper
{
    public sealed class AimDragSurface : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public Action<Vector2> Dragged;

        public void OnPointerDown(PointerEventData eventData) { }
        public void OnDrag(PointerEventData eventData) => Dragged?.Invoke(eventData.delta);
        public void OnPointerUp(PointerEventData eventData) { }
    }

    public sealed class HoldDragButton : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler, IPointerExitHandler
    {
        public Action<bool> HoldChanged;
        public Action<Vector2> Dragged;
        private int pointerId = int.MinValue;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (pointerId != int.MinValue) return;
            pointerId = eventData.pointerId;
            HoldChanged?.Invoke(true);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.pointerId == pointerId) Dragged?.Invoke(eventData.delta);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId != pointerId) return;
            pointerId = int.MinValue;
            HoldChanged?.Invoke(false);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            // Deliberately keep capture outside the button: the same finger can
            // hold breath and keep aiming anywhere on screen.
        }

        private void OnDisable()
        {
            if (pointerId == int.MinValue) return;
            pointerId = int.MinValue;
            HoldChanged?.Invoke(false);
        }
    }

    public sealed class SafeAreaFitter : MonoBehaviour
    {
        private Rect lastSafeArea;
        private Vector2Int lastScreen;

        private void OnEnable() => Apply();

        private void Update()
        {
            if (lastSafeArea != Screen.safeArea || lastScreen.x != Screen.width || lastScreen.y != Screen.height)
            {
                Apply();
            }
        }

        private void Apply()
        {
            Rect safe = Screen.safeArea;
            RectTransform rect = (RectTransform)transform;
            rect.anchorMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
            rect.anchorMax = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            lastSafeArea = safe;
            lastScreen = new Vector2Int(Screen.width, Screen.height);
        }
    }

    public sealed class ScopeOverlayGraphic : Graphic
    {
        public float RadiusFractionOfHeight = 0.455f;
        public float RadiusFractionOfWidth = 0.32f;
        public float ScopeRadius { get; private set; }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            Rect rect = rectTransform.rect;
            Vector2 centre = rect.center;
            ScopeRadius = Mathf.Min(rect.height * RadiusFractionOfHeight, rect.width * RadiusFractionOfWidth);
            float outer = Mathf.Sqrt(rect.width * rect.width + rect.height * rect.height) * 0.72f;
            const int segments = 128;
            UIVertex vertex = UIVertex.simpleVert;
            vertex.color = color;

            for (int i = 0; i < segments; i++)
            {
                float angle0 = Mathf.PI * 2f * i / segments;
                float angle1 = Mathf.PI * 2f * (i + 1) / segments;
                Vector2 direction0 = new Vector2(Mathf.Cos(angle0), Mathf.Sin(angle0));
                Vector2 direction1 = new Vector2(Mathf.Cos(angle1), Mathf.Sin(angle1));
                int start = vertexHelper.currentVertCount;
                vertex.position = centre + direction0 * ScopeRadius;
                vertexHelper.AddVert(vertex);
                vertex.position = centre + direction1 * ScopeRadius;
                vertexHelper.AddVert(vertex);
                vertex.position = centre + direction1 * outer;
                vertexHelper.AddVert(vertex);
                vertex.position = centre + direction0 * outer;
                vertexHelper.AddVert(vertex);
                vertexHelper.AddTriangle(start, start + 1, start + 2);
                vertexHelper.AddTriangle(start, start + 2, start + 3);
            }
        }
    }

    public sealed class ReticleGraphic : Graphic
    {
        public float PixelsPerMil = 36f;
        public float ScopeRadius = 420f;
        public int Zoom = 4;
        public Color MajorColor = new Color(0.05f, 0.08f, 0.07f, 0.92f);
        public Color AccentColor = new Color(0.86f, 0.18f, 0.12f, 0.88f);

        public void SetScale(float pixelsPerMil, float scopeRadius, int zoom)
        {
            if (Mathf.Abs(PixelsPerMil - pixelsPerMil) < 0.05f && Mathf.Abs(ScopeRadius - scopeRadius) < 0.05f && Zoom == zoom)
                return;
            PixelsPerMil = pixelsPerMil;
            ScopeRadius = scopeRadius;
            Zoom = zoom;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Vector2 centre = rectTransform.rect.center;
            float radius = Mathf.Max(10f, ScopeRadius);
            float majorWidth = Mathf.Max(1.3f, radius * 0.0032f);
            float minorWidth = Mathf.Max(0.85f, majorWidth * 0.63f);

            AddLine(vh, centre + Vector2.left * radius * 0.88f, centre + Vector2.left * radius * 0.035f, majorWidth, MajorColor);
            AddLine(vh, centre + Vector2.right * radius * 0.035f, centre + Vector2.right * radius * 0.88f, majorWidth, MajorColor);
            AddLine(vh, centre + Vector2.down * radius * 0.88f, centre + Vector2.down * radius * 0.035f, majorWidth, MajorColor);
            AddLine(vh, centre + Vector2.up * radius * 0.035f, centre + Vector2.up * radius * 0.88f, majorWidth, MajorColor);

            int subdivisions = Zoom >= 16 ? 4 : Zoom >= 8 ? 2 : 1;
            float maxMil = Mathf.Min(12f, radius * 0.83f / Mathf.Max(1f, PixelsPerMil));
            int minorCount = Mathf.FloorToInt(maxMil * subdivisions);
            for (int i = 1; i <= minorCount; i++)
            {
                float mil = i / (float)subdivisions;
                float offset = mil * PixelsPerMil;
                bool major = i % subdivisions == 0;
                float tick = major ? radius * 0.030f : radius * 0.016f;
                Color tickColor = major ? MajorColor : new Color(MajorColor.r, MajorColor.g, MajorColor.b, MajorColor.a * 0.70f);
                float width = major ? majorWidth : minorWidth;

                AddLine(vh, centre + new Vector2(offset, -tick), centre + new Vector2(offset, tick), width, tickColor);
                AddLine(vh, centre + new Vector2(-offset, -tick), centre + new Vector2(-offset, tick), width, tickColor);
                AddLine(vh, centre + new Vector2(-tick, offset), centre + new Vector2(tick, offset), width, tickColor);
                AddLine(vh, centre + new Vector2(-tick, -offset), centre + new Vector2(tick, -offset), width, tickColor);
            }

            AddCircle(vh, centre, radius, Mathf.Max(2.2f, majorWidth * 1.55f), new Color(0.04f, 0.05f, 0.05f, 0.96f), 128);
            AddCircle(vh, centre, radius - Mathf.Max(5f, radius * 0.018f), Mathf.Max(1f, majorWidth * 0.55f), new Color(0.72f, 0.77f, 0.71f, 0.42f), 128);
            AddCircle(vh, centre, radius * 0.012f, Mathf.Max(1.3f, majorWidth), AccentColor, 28);
        }

        private static void AddLine(VertexHelper vh, Vector2 a, Vector2 b, float width, Color lineColor)
        {
            Vector2 direction = (b - a).normalized;
            Vector2 normal = new Vector2(-direction.y, direction.x) * (width * 0.5f);
            int start = vh.currentVertCount;
            AddVertex(vh, a - normal, lineColor);
            AddVertex(vh, a + normal, lineColor);
            AddVertex(vh, b + normal, lineColor);
            AddVertex(vh, b - normal, lineColor);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }

        private static void AddCircle(VertexHelper vh, Vector2 centre, float radius, float width, Color lineColor, int segments)
        {
            for (int i = 0; i < segments; i++)
            {
                float a0 = Mathf.PI * 2f * i / segments;
                float a1 = Mathf.PI * 2f * (i + 1) / segments;
                Vector2 p0 = centre + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * radius;
                Vector2 p1 = centre + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * radius;
                AddLine(vh, p0, p1, width, lineColor);
            }
        }

        private static void AddVertex(VertexHelper vh, Vector2 position, Color vertexColor)
        {
            UIVertex vertex = UIVertex.simpleVert;
            vertex.position = position;
            vertex.color = vertexColor;
            vh.AddVert(vertex);
        }
    }
}
