using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace LR2.EditorTools
{
    /// <summary>Shared helpers for the LR2 content builders (idempotent asset creation, materials, textures).</summary>
    public static class LR2EditorUtil
    {
        public const string Root = "Assets/LR2";

        public static void EnsureFolder(string path)
        {
            path = path.Replace("\\", "/").TrimEnd('/');
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace("\\", "/");
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        /// <summary>Creates or updates an asset in place (keeps its GUID so references survive rebuilds).</summary>
        public static T SaveAsset<T>(T asset, string path) where T : Object
        {
            EnsureFolder(Path.GetDirectoryName(path));
            var existing = AssetDatabase.LoadAssetAtPath<Object>(path);
            if (existing != null && existing.GetType() == asset.GetType())
            {
                EditorUtility.CopySerialized(asset, existing);
                existing.name = Path.GetFileNameWithoutExtension(path);
                Object.DestroyImmediate(asset);
                EditorUtility.SetDirty(existing);
                return (T)existing;
            }
            if (existing != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        public static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a != null) return a;
            EnsureFolder(Path.GetDirectoryName(path));
            a = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(a, path);
            return a;
        }

        public static GameObject SavePrefab(GameObject go, string path)
        {
            EnsureFolder(Path.GetDirectoryName(path));
            int removed = RemoveMissingScripts(go);
            if (removed > 0) Debug.Log($"[LR2] Removed {removed} missing-script component(s) (URP leftovers) from {go.name}.");
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path, out bool success);
            Object.DestroyImmediate(go);
            if (!success || prefab == null) throw new System.Exception($"[LR2] Failed to save prefab {path}");
            return prefab;
        }

        /// <summary>Strips components whose script no longer exists (e.g. URP camera data on demo prefabs).</summary>
        public static int RemoveMissingScripts(GameObject root)
        {
            int count = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                count += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            return count;
        }

        public static AudioClip[] LoadClips(string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder)) return new AudioClip[0];
            var guids = AssetDatabase.FindAssets("t:AudioClip", new[] { folder });
            var list = new System.Collections.Generic.List<AudioClip>();
            foreach (var g in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (Path.GetDirectoryName(p).Replace("\\", "/") != folder) continue; // direct children only
                var c = AssetDatabase.LoadAssetAtPath<AudioClip>(p);
                if (c != null) list.Add(c);
            }
            return list.ToArray();
        }

        public static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform t in go.transform) SetLayerRecursive(t.gameObject, layer);
        }

        public static Shader HdrpLitShader => Shader.Find("HDRP/Lit");

        public static Material NewLit(Color color, float smoothness = 0.4f, float metallic = 0f, Texture baseMap = null,
            Texture normal = null, Vector2? tiling = null, Texture mask = null)
        {
            var m = new Material(HdrpLitShader);
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            if (baseMap != null) m.SetTexture("_BaseColorMap", baseMap);
            if (normal != null)
            {
                m.SetTexture("_NormalMap", normal);
                m.SetFloat("_NormalScale", 1f);
            }
            if (mask != null)
            {
                m.SetTexture("_MaskMap", mask);
                m.SetFloat("_AORemapMin", 0f);
                m.SetFloat("_AORemapMax", 1f);
                m.SetFloat("_SmoothnessRemapMax", smoothness);
            }
            if (tiling.HasValue)
            {
                m.SetTextureScale("_BaseColorMap", tiling.Value);
                m.SetTextureScale("_NormalMap", tiling.Value);
                m.SetTextureScale("_MaskMap", tiling.Value);
            }
            HDMaterial.ValidateMaterial(m);
            return m;
        }

        public static Material NewEmissive(Color baseColor, Color emissive, float nits)
        {
            var m = NewLit(baseColor, 0.5f);
            HDMaterial.SetUseEmissiveIntensity(m, true);
            HDMaterial.SetEmissiveColor(m, emissive);
            HDMaterial.SetEmissiveIntensity(m, nits, EmissiveIntensityUnit.Nits);
            HDMaterial.ValidateMaterial(m);
            return m;
        }

        public static Material NewParticle(Texture tex, Color tint, float intensity, bool additive)
        {
            var shader = Shader.Find("LR2/ParticleUnlit");
            var m = new Material(shader != null ? shader : Shader.Find("HDRP/Unlit"));
            m.SetTexture("_MainTex", tex);
            m.SetColor("_Color", tint);
            m.SetFloat("_Intensity", intensity);
            m.SetFloat("_SrcBlend", additive ? (float)UnityEngine.Rendering.BlendMode.SrcAlpha : (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)UnityEngine.Rendering.BlendMode.One : (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.renderQueue = 3000;
            return m;
        }

        public static Material SaveMaterial(Material m, string path) => SaveAsset(m, path);

        /// <summary>Writes a PNG and imports it with the given settings.</summary>
        public static Texture2D SaveTexture(Texture2D tex, string path, bool sprite = false, bool alphaIsTransparency = true, bool mipmaps = true)
        {
            EnsureFolder(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = sprite ? TextureImporterType.Sprite : TextureImporterType.Default;
            importer.alphaIsTransparency = alphaIsTransparency;
            importer.mipmapEnabled = mipmaps && !sprite;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.sRGBTexture = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        public static T FindAsset<T>(string nameFilter, params string[] folders) where T : Object
        {
            var guids = AssetDatabase.FindAssets(nameFilter + " t:" + typeof(T).Name, folders);
            foreach (var g in guids)
            {
                var a = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(g));
                if (a != null) return a;
            }
            return null;
        }

        public static System.Type FindType(string fullName)
        {
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName);
                if (t != null) return t;
            }
            return null;
        }

        /// <summary>Copies every serialized field that exists on both components (script swap without data loss).</summary>
        public static void CopyMatchingFields(Component from, Component to)
        {
            var src = new SerializedObject(from);
            var dst = new SerializedObject(to);
            var it = src.GetIterator();
            bool enter = true;
            while (it.NextVisible(enter))
            {
                enter = false;
                if (it.name == "m_Script") continue;
                if (dst.FindProperty(it.propertyPath) != null) dst.CopyFromSerializedProperty(it);
            }
            dst.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetField(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogWarning($"[LR2] Field '{field}' not found on {target.GetType().Name}"); return; }
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
