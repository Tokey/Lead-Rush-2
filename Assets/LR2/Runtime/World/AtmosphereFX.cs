using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace LR2.World
{
    /// <summary>
    /// Lightweight, map-placeable ground atmosphere. The low layer uses a bounded HDRP fog volume
    /// plus particles so it remains readable at gameplay distance; force-field particles give
    /// soldiers a visible wake without changing the global fog budget.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AtmosphereFX : MonoBehaviour
    {
        [Header("Ground layer")]
        [SerializeField, Min(0.3f)] private float layerHeight = 0.55f;
        [SerializeField, Min(0.1f)] private float radius = 18f;
        [SerializeField, Range(0f, 1f)] private float mistOpacity = 0.16f;
        [SerializeField, Min(0f)] private float mistRate = 28f;

        [Header("Ambient motes")]
        [SerializeField, Range(0f, 1f)] private float dustOpacity = 0.3f;
        [SerializeField, Min(0f)] private float dustRate = 16f;
        [SerializeField] private Color mistColor = new Color(0.7f, 0.75f, 0.76f, 1f);
        [SerializeField] private Color dustColor = new Color(0.48f, 0.43f, 0.36f, 1f);

        private ParticleSystem _mist;
        private ParticleSystem _dust;
        private ParticleSystemForceField _forceField;
        private LocalVolumetricFog _localFog;

        private void Awake()
        {
            _localFog = CreateLocalFog();
            _mist = CreateLayer("Ground Mist", mistColor, mistOpacity, mistRate, layerHeight, 0.5f);
            _dust = CreateLayer("Dust Ash Embers", dustColor, dustOpacity, dustRate, Mathf.Max(1f, layerHeight + 1.2f), 1.4f);
            var forceObject = new GameObject("Soldier Reactive Air");
            forceObject.transform.SetParent(transform, false);
            _forceField = forceObject.AddComponent<ParticleSystemForceField>();
            _forceField.enabled = true;
        }

        private LocalVolumetricFog CreateLocalFog()
        {
            var go = new GameObject("Ground Local Fog");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.up * Mathf.Clamp(layerHeight * 0.5f, 0.15f, 0.4f);
            var fog = go.AddComponent<LocalVolumetricFog>();
            var parameters = new LocalVolumetricFogArtistParameters(mistColor, 70f, 0f)
            {
                size = new Vector3(radius * 2f, Mathf.Clamp(layerHeight, 0.3f, 0.8f), radius * 2f),
                positiveFade = Vector3.one * 0.35f,
                negativeFade = Vector3.one * 0.35f,
                distanceFadeStart = 25f,
                distanceFadeEnd = 40f,
                blendingMode = LocalVolumetricFogBlendingMode.Additive,
                falloffMode = LocalVolumetricFogFalloffMode.Linear
            };
            fog.parameters = parameters;
            return fog;
        }

        private ParticleSystem CreateLayer(string name, Color color, float opacity, float rate, float height, float lifetime)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.7f, lifetime * 1.3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.03f, 0.12f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.65f);
            main.startColor = new Color(color.r, color.g, color.b, Mathf.Clamp01(opacity));
            main.maxParticles = 256;
            var emission = ps.emission;
            emission.rateOverTime = Mathf.Max(0f, rate);
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius;
            go.transform.localPosition = Vector3.up * Mathf.Clamp(height * 0.5f, 0.3f, 1.5f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.25f;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            var fxLib = LR2.FX.LR2FxLibrary.Instance;
            if (renderer != null && fxLib != null && fxLib.mist != null)
            {
                // D60: the library mist material (alpha-blended, built into the player); the particle colour carries the tint
                renderer.sharedMaterial = fxLib.mist;
                LR2.FX.LR2FxLibrary.ApplyFlipbook(ps, fxLib.mistTiles);
            }
            else if (renderer != null)
            {
                Shader shader = Shader.Find("HDRP/Unlit") ?? Shader.Find("Unlit/Color");
                if (shader != null)
                {
                    var mat = new Material(shader) { name = name + "_Mat" };
                    var softTex = CreateSoftMistTexture();
                    if (mat.HasProperty("_UnlitColorMap")) mat.SetTexture("_UnlitColorMap", softTex);
                    if (mat.HasProperty("_BaseColorMap")) mat.SetTexture("_BaseColorMap", softTex);
                    if (mat.HasProperty("_UnlitColor")) mat.SetColor("_UnlitColor", new Color(color.r, color.g, color.b, Mathf.Clamp01(opacity)));
                    if (mat.HasProperty("_SurfaceType")) mat.SetFloat("_SurfaceType", 1f);
                    if (mat.HasProperty("_BlendMode")) mat.SetFloat("_BlendMode", 0f);
                    HDMaterial.ValidateMaterial(mat);
                    renderer.sharedMaterial = mat;
                }
            }
            ps.Play();
            return ps;
        }

        private static Texture2D _softMistTex;
        private static Texture2D CreateSoftMistTexture()
        {
            if (_softMistTex != null) return _softMistTex;
            const int S = 64;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = (x + 0.5f) / S * 2f - 1f, v = (y + 0.5f) / S * 2f - 1f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(u * u + v * v));
                px[y * S + x] = new Color(1f, 1f, 1f, a * a * 0.85f);
            }
            tex.SetPixels(px);
            tex.Apply(false, true);
            _softMistTex = tex;
            return _softMistTex;
        }

        private void OnDestroy()
        {
            if (_mist != null) Destroy(_mist.gameObject);
            if (_dust != null) Destroy(_dust.gameObject);
            if (_forceField != null) Destroy(_forceField.gameObject);
            if (_localFog != null) Destroy(_localFog.gameObject);
        }
    }
}
