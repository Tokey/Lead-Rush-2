using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace LR2.FX
{
    /// <summary>Small, distortion-only plume used for subtle muzzle and flame heat shimmer.</summary>
    [DisallowMultipleComponent]
    public sealed class HeatHazeEmitter : MonoBehaviour
    {
        private static Mesh _quadMesh;
        private static Texture2D _vectorMap;
        private MeshRenderer _renderer;
        private Material _material;
        private Vector2 _size = new Vector2(0.06f, 0.18f);
        private float _strength;
        private float _scroll;

        private void Awake()
        {
            // A copied gun (dropped weapon, killcam puppet) already carries the components this adds.
            var filter = GetComponent<MeshFilter>();
            if (filter == null) filter = gameObject.AddComponent<MeshFilter>();
            _renderer = GetComponent<MeshRenderer>();
            if (_renderer == null) _renderer = gameObject.AddComponent<MeshRenderer>();
            filter.sharedMesh = GetQuadMesh();
            Shader shader = Shader.Find("HDRP/Unlit");
            if (shader == null)
            {
                _renderer.enabled = false;
                return;
            }

            _material = new Material(shader)
            {
                name = "LR2 Heat Distortion Only",
                hideFlags = HideFlags.HideAndDontSave
            };
            Texture2D vectorMap = GetVectorMap();
            if (_material.HasProperty("_UnlitColorMap")) _material.SetTexture("_UnlitColorMap", vectorMap);
            if (_material.HasProperty("_DistortionVectorMap")) _material.SetTexture("_DistortionVectorMap", vectorMap);
            if (_material.HasProperty("_SurfaceType")) _material.SetFloat("_SurfaceType", 1f);
            if (_material.HasProperty("_BlendMode")) _material.SetFloat("_BlendMode", 0f);
            if (_material.HasProperty("_DistortionEnable")) _material.SetFloat("_DistortionEnable", 1f);
            if (_material.HasProperty("_DistortionOnly")) _material.SetFloat("_DistortionOnly", 1f);
            if (_material.HasProperty("_DistortionDepthTest")) _material.SetFloat("_DistortionDepthTest", 1f);
            if (_material.HasProperty("_DistortionVectorScale")) _material.SetFloat("_DistortionVectorScale", 2f);
            if (_material.HasProperty("_DistortionVectorBias")) _material.SetFloat("_DistortionVectorBias", -1f);
            if (_material.HasProperty("_DoubleSidedEnable")) _material.SetFloat("_DoubleSidedEnable", 1f);
            HDMaterial.ValidateMaterial(_material);
            _renderer.sharedMaterial = _material;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.enabled = false;
            transform.localScale = new Vector3(_size.x, _size.y, 1f);
        }

        public void Configure(Vector2 size, float strength)
        {
            _size = new Vector2(Mathf.Max(0.005f, size.x), Mathf.Max(0.005f, size.y));
            transform.localScale = new Vector3(_size.x, _size.y, 1f);
            SetStrength(strength);
        }

        public void SetStrength(float strength)
        {
            _strength = Mathf.Clamp01(strength);
            if (_renderer != null) _renderer.enabled = _strength > 0.001f && _material != null;
            if (_material == null) return;
            if (_material.HasProperty("_UnlitColor"))
                _material.SetColor("_UnlitColor", new Color(1f, 1f, 1f, _strength));
            if (_material.HasProperty("_DistortionScale"))
                _material.SetFloat("_DistortionScale", _strength * 0.035f);
        }

        private void LateUpdate()
        {
            if (_renderer == null || !_renderer.enabled) return;
            Camera view = Camera.main;
            if (view != null)
                transform.rotation = Quaternion.LookRotation(view.transform.position - transform.position, Vector3.up);
            _scroll = (_scroll + Time.deltaTime * 0.45f) % 1f;
            if (_material != null && _material.HasProperty("_DistortionVectorMap"))
                _material.SetTextureOffset("_DistortionVectorMap", new Vector2(0f, _scroll));
        }

        private static Mesh GetQuadMesh()
        {
            if (_quadMesh != null) return _quadMesh;
            _quadMesh = new Mesh { name = "LR2 Heat Haze Quad", hideFlags = HideFlags.HideAndDontSave };
            _quadMesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f)
            };
            _quadMesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
            _quadMesh.triangles = new[] { 0, 1, 2, 2, 1, 3 };
            _quadMesh.RecalculateNormals();
            _quadMesh.RecalculateBounds();
            return _quadMesh;
        }

        private static Texture2D GetVectorMap()
        {
            if (_vectorMap != null) return _vectorMap;
            const int width = 32;
            const int height = 64;
            _vectorMap = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "LR2 Heat Distortion Vectors",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float u = (x + 0.5f) / width;
                float v = (y + 0.5f) / height;
                float edge = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(u * 2f - 1f)), 2f) * Mathf.Clamp01((v + 0.08f) * 1.4f);
                float wave = Mathf.Sin(v * 21f + Mathf.Sin(u * 13f) * 2.2f);
                float crossWave = Mathf.Sin(v * 34f + u * 8f + 1.7f);
                float dx = 0.5f + (wave * 0.10f + crossWave * 0.035f) * edge;
                float dy = 0.5f + Mathf.Sin(v * 17f + u * 9f) * 0.025f * edge;
                pixels[y * width + x] = new Color(dx, dy, 0.5f, edge);
            }
            _vectorMap.SetPixels(pixels);
            _vectorMap.Apply(false, true);
            return _vectorMap;
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }
    }
}
