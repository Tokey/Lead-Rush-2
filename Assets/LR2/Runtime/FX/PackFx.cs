using System.Collections.Generic;
using LR2.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace LR2.FX
{
    /// <summary>
    /// Unity Particle Pack prefabs in HDRP (B112). The pack's materials use built-in pipeline shaders (magenta in HDRP) and
    /// its textures have no alpha, so every spawned instance gets its renderers re-materialised on the keyword-free
    /// LR2/ParticleUnlit shader: texture and tint carried over, HDR tint folded into intensity, alpha from luminance,
    /// soft particles; distortion layers are dropped and the pack's built-in lights switched off (HDRP reads them wrong).
    /// </summary>
    public static class PackFx
    {
        private static readonly Dictionary<Material, Material> Cache = new Dictionary<Material, Material>();
        private static Shader _shader;

        public static GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, float scale, float lifetime)
        {
            if (prefab == null) return null;
            var go = LR2Pool.Spawn(prefab, position, rotation, lifetime);
            if (go != null) Prepare(go, scale);
            return go;
        }

        public static void Prepare(GameObject go, float scale)
        {
            if (go == null) return;
            go.transform.localScale = Vector3.one * scale;
            foreach (var l in go.GetComponentsInChildren<Light>(true)) l.enabled = false;
            foreach (var r in go.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                var m = Convert(r.sharedMaterial);
                if (m == null) { r.enabled = false; continue; }
                r.sharedMaterial = m;
                if (r.trailMaterial != null) r.trailMaterial = Convert(r.trailMaterial);
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            var all = go.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var ps in all)
            {
                var main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }
            foreach (var ps in all)
            {
                ps.Clear(false);
                ps.Play(false);
            }
        }

        public static Material Convert(Material src)
        {
            if (src == null) return null;
            if (_shader == null) _shader = Shader.Find("LR2/ParticleUnlit");
            if (_shader == null || src.shader == _shader) return src;
            if (Cache.TryGetValue(src, out var hit)) return hit;

            string n = src.name.ToLowerInvariant();
            Material m = null;
            if (!n.Contains("distortion"))
            {
                bool smoke = n.Contains("smoke") || n.Contains("steam") || n.Contains("dust");
                bool fireball = !smoke && n.Contains("explosion") && !n.Contains("energy");
                bool additive = !smoke && !fireball; // sparks, embers, energy, lightning, shockwave, flash
                m = new Material(_shader) { name = "LR2Pack_" + src.name, hideFlags = HideFlags.DontSave };
                var tex = FirstTexture(src, "_MainTex", "_BaseMap", "_BaseColorMap", "_UnlitColorMap");
                if (tex != null) m.SetTexture("_MainTex", tex);
                Color c = FirstColor(src, "_TintColor", "_Color", "_BaseColor", "_UnlitColor");
                float peak = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
                if (peak > 1f) c = new Color(c.r / peak, c.g / peak, c.b / peak, c.a);
                if (smoke) c = Color.Lerp(c, new Color(0.86f, 0.88f, 0.9f, c.a), 0.5f);
                m.SetColor("_Color", c);
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_Intensity", smoke ? 1f : fireball ? 3.2f : 3f);
                m.SetFloat("_LuminanceToAlpha", 1f);
                m.SetFloat("_LuminanceAlphaScale", smoke ? 0.55f : 1f);
                m.SetFloat("_SoftFade", additive ? 0.05f : 0.22f);
                m.renderQueue = additive ? 3100 : 3000;
            }
            Cache[src] = m;
            return m;
        }

        private static Texture FirstTexture(Material m, params string[] names)
        {
            foreach (var p in names)
                if (m.HasProperty(p) && m.GetTexture(p) != null) return m.GetTexture(p);
            return m.mainTexture;
        }

        private static Color FirstColor(Material m, params string[] names)
        {
            foreach (var p in names)
                if (m.HasProperty(p)) return m.GetColor(p);
            return Color.white;
        }
    }
}
