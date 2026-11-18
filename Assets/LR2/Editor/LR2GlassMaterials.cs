using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace LR2.EditorTools
{
    /// <summary>Builds the public, reusable HDRP glass materials used by the map builders.</summary>
    public static class LR2GlassMaterials
    {
        private const string MaterialFolder = "Assets/LR2/Materials/Environment";
        private const string TextureFolder = "Assets/LR2/Materials/Environment/Masks";
        private const int TextureSize = 256;

        [MenuItem("LR2/Build Glass Materials")]
        public static void Build()
        {
            LR2EditorUtil.EnsureFolder(MaterialFolder);
            LR2EditorUtil.EnsureFolder(TextureFolder);

            var grime = BuildGrimeTexture();
            var cracks = BuildCrackNormalTexture();
            SaveGlass("M_LR2_Glass_Clean", grime, cracks, 0.94f, 0.14f, new Color(0.82f, 0.94f, 0.91f, 0.24f), false);
            SaveGlass("M_LR2_Glass_Dirty", grime, cracks, 0.62f, 0.42f, new Color(0.58f, 0.76f, 0.70f, 0.38f), false);
            SaveGlass("M_LR2_Glass_Cracked", grime, cracks, 0.48f, 0.56f, new Color(0.72f, 0.88f, 0.84f, 0.44f), true);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[LR2] Built M_LR2_Glass_Clean, M_LR2_Glass_Dirty, and M_LR2_Glass_Cracked.");
        }

        private static Texture2D BuildGrimeTexture()
        {
            string path = $"{TextureFolder}/T_LR2_Glass_Grime.png";
            var tex = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false, true)
            {
                name = "T_LR2_Glass_Grime",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var pixels = new Color[TextureSize * TextureSize];
            for (int y = 0; y < TextureSize; y++)
            for (int x = 0; x < TextureSize; x++)
            {
                float u = x / (TextureSize - 1f);
                float v = y / (TextureSize - 1f);
                float edge = 1f - Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v)) / 0.16f;
                float noise = Mathf.PerlinNoise(u * 11.5f + 1.7f, v * 11.5f + 4.2f);
                float streak = Mathf.PerlinNoise(u * 28f + 2.4f, v * 3.2f + 0.7f);
                float grime = Mathf.Clamp01(Mathf.Max(0f, edge) * (0.32f + noise * 0.55f) + Mathf.Max(0f, streak - 0.62f) * 0.55f);
                pixels[y * TextureSize + x] = new Color(grime, grime, grime, 1f);
            }

            tex.SetPixels(pixels);
            tex.Apply(false, true);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.sRGBTexture = false;
                importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Texture2D BuildCrackNormalTexture()
        {
            string path = $"{TextureFolder}/T_LR2_Glass_Cracks.png";
            var tex = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false, true)
            {
                name = "T_LR2_Glass_Cracks",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var pixels = new Color[TextureSize * TextureSize];
            for (int y = 0; y < TextureSize; y++)
            for (int x = 0; x < TextureSize; x++)
            {
                float u = x / (TextureSize - 1f);
                float v = y / (TextureSize - 1f);
                float lineA = Mathf.Clamp01(1f - Mathf.Abs(v - (0.18f + u * 0.52f)) / 0.012f);
                float lineB = Mathf.Clamp01(1f - Mathf.Abs(v - (0.78f - u * 0.43f)) / 0.009f);
                float crack = Mathf.Max(lineA, lineB);
                float nx = (0.5f - crack * 0.28f);
                float ny = (0.5f + crack * 0.24f);
                pixels[y * TextureSize + x] = new Color(nx, ny, 1f, 1f);
            }
            tex.SetPixels(pixels);
            tex.Apply(false, true);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.sRGBTexture = false;
                importer.textureType = TextureImporterType.NormalMap;
                importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static void SaveGlass(string name, Texture2D grime, Texture2D cracks, float smoothness, float grimeAmount, Color baseColor, bool cracked)
        {
            var shader = Shader.Find("HDRP/Lit");
            if (shader == null) throw new System.InvalidOperationException("HDRP/Lit shader is unavailable.");

            var material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", baseColor);
            material.SetTexture("_BaseColorMap", grime);
            material.SetTexture("_MaskMap", grime);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_SmoothnessRemapMin", Mathf.Clamp01(smoothness - grimeAmount * 0.45f));
            material.SetFloat("_SmoothnessRemapMax", smoothness);
            material.SetFloat("_SurfaceType", 1f);
            material.SetFloat("_BlendMode", 0f);
            material.SetFloat("_DoubleSidedEnable", 1f);
            material.SetFloat("_CullMode", 0f);
            material.SetFloat("_RefractionModel", 3f);
            material.SetFloat("_Ior", 1.52f);
            material.SetFloat("_Thickness", 0.008f);
            material.SetColor("_TransmittanceColor", new Color(0.72f, 0.90f, 0.84f, 1f));
            material.SetTexture("_NormalMap", cracks);
            material.SetFloat("_NormalScale", cracked ? 0.65f : (grimeAmount > 0.3f ? 0.24f : 0.10f));
            if (cracked)
            {
                material.SetFloat("_SmoothnessRemapMin", 0.25f);
            }
            HDMaterial.ValidateMaterial(material);
            LR2EditorUtil.SaveMaterial(material, $"{MaterialFolder}/{name}.mat");
        }
    }
}
