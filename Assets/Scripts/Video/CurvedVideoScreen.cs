using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TakeMeToTheMoon.Video
{
    /// <summary>A cylindrical video surface. Local -Z faces the viewer; no Canvas or XR input is required.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class CurvedVideoScreen : MonoBehaviour
    {
        [Header("Shape (metres at scale 1)")]
        [Min(0.1f)] public float width = 3.2f;
        [Min(0.1f)] public float radius = 1.8f;
        [Min(0.1f)] public float aspectRatio = 832f / 480f;
        [Range(8, 256)] public int segments = 64;
        [Tooltip("Round the four corners in metres on the unrolled surface. Zero keeps square corners.")]
        [Min(0f)] public float cornerRadius = 0.12f;
        [Header("Video")]
        public Texture sourceTexture;
        public bool matchTextureAspect = true;
        public bool flipVertical;
        public Material screenMaterial;

        Mesh _mesh;
        MeshFilter _filter;
        MeshRenderer _renderer;
        MaterialPropertyBlock _properties;
        bool _dirty = true;
        Texture _lastTexture;
        float _lastWidth, _lastRadius, _lastAspect, _lastCornerRadius;
        int _lastSegments;
        bool _lastFlip;
        Material _lastMaterial;

        public MeshRenderer ScreenRenderer => GetComponent<MeshRenderer>();
        public float ArcDegrees => Mathf.Min(width / Mathf.Max(.1f, radius), Mathf.PI * 1.9f) * Mathf.Rad2Deg;

        public void SetTexture(Texture texture)
        {
            sourceTexture = texture;
            _dirty = true;
        }

        void OnEnable() { _dirty = true; Refresh(); }
        void OnValidate() { _dirty = true; } // Mesh operations run on the main thread in Update.
        void Update() { Refresh(); }

        void Refresh()
        {
            float safeRadius = Mathf.Max(.1f, radius);
            float safeWidth = Mathf.Clamp(width, .1f, safeRadius * Mathf.PI * 1.9f);
            float aspect = matchTextureAspect && sourceTexture != null && sourceTexture.height > 0
                ? (float)sourceTexture.width / sourceTexture.height : Mathf.Max(.1f, aspectRatio);
            int count = Mathf.Clamp(segments, 8, 256);
            float halfHeight = safeWidth / aspect * .5f;
            float corner = Mathf.Clamp(cornerRadius, 0, Mathf.Min(safeWidth * .5f, halfHeight));
            bool shapeChanged = _mesh == null || safeWidth != _lastWidth || safeRadius != _lastRadius
                || aspect != _lastAspect || count != _lastSegments || flipVertical != _lastFlip
                || corner != _lastCornerRadius;
            if (!_dirty && !shapeChanged && sourceTexture == _lastTexture && screenMaterial == _lastMaterial) return;
            if (_filter == null) _filter = GetComponent<MeshFilter>();
            if (_renderer == null) _renderer = GetComponent<MeshRenderer>();
            if (shapeChanged)
            {
                if (_mesh == null) _mesh = new Mesh { name = "Curved Video Surface", hideFlags = HideFlags.HideAndDontSave };
                var columns = BuildColumns(safeWidth, corner, count);
                int strips = columns.Count - 1;
                var vertices = new Vector3[columns.Count * 2];
                var uv = new Vector2[vertices.Length];
                var triangles = new int[strips * 6];
                for (int i = 0; i < columns.Count; i++)
                {
                    float surfaceX = columns[i];
                    float u = surfaceX / safeWidth + .5f;
                    float angle = surfaceX / safeRadius;
                    float edgeDistance = Mathf.Max(0, Mathf.Abs(surfaceX) - (safeWidth * .5f - corner));
                    float top = halfHeight;
                    if (corner > 0 && edgeDistance > 0)
                        top = halfHeight - corner + Mathf.Sqrt(Mathf.Max(0, corner * corner - edgeDistance * edgeDistance));
                    float x = Mathf.Sin(angle) * safeRadius;
                    float z = (Mathf.Cos(angle) - 1) * safeRadius;
                    int v = i * 2;
                    vertices[v] = new Vector3(x, -top, z);
                    vertices[v + 1] = new Vector3(x, top, z);
                    // Keep the original video mapping: clip corners instead of squeezing the image.
                    float bottomV = .5f - top / (halfHeight * 2);
                    float topV = .5f + top / (halfHeight * 2);
                    uv[v] = new Vector2(u, flipVertical ? 1 - bottomV : bottomV);
                    uv[v + 1] = new Vector2(u, flipVertical ? 1 - topV : topV);
                    if (i == strips) continue;
                    int t = i * 6;
                    triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
                    triangles[t + 3] = v + 2; triangles[t + 4] = v + 1; triangles[t + 5] = v + 3;
                }
                _mesh.Clear();
                _mesh.vertices = vertices; _mesh.uv = uv; _mesh.triangles = triangles;
                _mesh.RecalculateNormals(); _mesh.RecalculateBounds();
                _filter.sharedMesh = _mesh;
            }
            if (screenMaterial != null) _renderer.sharedMaterial = screenMaterial;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            if (_properties == null) _properties = new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(_properties);
            var texture = sourceTexture != null ? sourceTexture : Texture2D.whiteTexture;
            _properties.SetTexture("_BaseMap", texture);
            _properties.SetTexture("_MainTex", texture);
            _renderer.SetPropertyBlock(_properties);
            _lastWidth = safeWidth; _lastRadius = safeRadius; _lastAspect = aspect;
            _lastSegments = count; _lastFlip = flipVertical; _lastCornerRadius = corner;
            _lastTexture = sourceTexture; _lastMaterial = screenMaterial; _dirty = false;
        }

        static List<float> BuildColumns(float width, float corner, int segments)
        {
            var columns = new List<float>(segments + 19);
            for (int i = 0; i <= segments; i++) columns.Add(((float)i / segments - .5f) * width);
            if (corner > 0)
            {
                // Extra samples keep small corners smooth even with a wide, low-segment screen.
                for (int i = 0; i <= 8; i++)
                {
                    float x = width * .5f - corner + corner * Mathf.Cos(i / 8f * Mathf.PI * .5f);
                    columns.Add(x); columns.Add(-x);
                }
            }
            columns.Sort();
            for (int i = columns.Count - 1; i > 0; i--)
                if (columns[i] - columns[i - 1] < .000001f) columns.RemoveAt(i);
            return columns;
        }

        void OnDisable()
        {
            if (_filter != null && _filter.sharedMesh == _mesh) _filter.sharedMesh = null;
            if (_renderer != null) _renderer.SetPropertyBlock(null);
            if (_mesh != null)
            {
                if (Application.isPlaying) Destroy(_mesh); else DestroyImmediate(_mesh);
                _mesh = null;
            }
        }
    }
}
