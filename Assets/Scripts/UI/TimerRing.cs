using UnityEngine;
using UnityEngine.UI;

namespace TakeMeToTheMoon.UI
{
    // UI mesh: no texture, bitmap editing, or per-frame material allocation.
    public sealed class TimerRing : MaskableGraphic
    {
        [SerializeField, Range(0, 1)] private float fill = 1;
        [SerializeField, Min(1)] private float thickness = 6;
        [SerializeField] private bool gradientEnabled;
        [SerializeField] private Color bottomColor = new Color(.55f, .02f, .05f, 1f);
        public float Fill
        {
            get => fill;
            set { float next = Mathf.Clamp01(value); if (fill == next) return; fill = next; SetVerticesDirty(); }
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (fill <= 0) return;
            var rect = GetPixelAdjustedRect();
            float outer = Mathf.Min(rect.width, rect.height) * .5f;
            float inner = Mathf.Max(0, outer - thickness);
            int segments = Mathf.Max(1, Mathf.CeilToInt(128 * fill));
            for (int i = 0; i <= segments; i++)
            {
                float angle = (90 - 360 * fill * i / segments) * Mathf.Deg2Rad;
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                // Fixed vertical gradient: seamless at the top, even while the arc shrinks.
                Color vertexColor = gradientEnabled
                    ? Color.Lerp(bottomColor, color, (direction.y + 1f) * .5f) : color;
                vh.AddVert(rect.center + direction * outer, vertexColor, Vector2.zero);
                vh.AddVert(rect.center + direction * inner, vertexColor, Vector2.zero);
                if (i == 0) continue;
                int k = i * 2;
                vh.AddTriangle(k - 2, k, k - 1);
                vh.AddTriangle(k, k + 1, k - 1);
            }
        }
    }
}
