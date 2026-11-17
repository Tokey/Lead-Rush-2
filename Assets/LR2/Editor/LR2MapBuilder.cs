using System.Collections.Generic;
using System.IO;
using LR2.AI;
using LR2.Audio;
using LR2.Combat;
using LR2.Core;
using LR2.Game;
using LR2.UI;
using LR2.World;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using static LR2.EditorTools.LR2EditorUtil;

namespace LR2.EditorTools
{
    /// <summary>
    /// Generates "Saltworks": a compact, vertical industrial harbour map (~54 x 46 m playable) for 1v1 - 2v2, with
    /// photoreal HDRP lighting, dense volumetrics, a bodycam lens volume, gameplay markers, NavMesh and minimap.
    /// Also builds the main menu scene and the build settings scene list.
    /// </summary>
    public static partial class LR2MapBuilder
    {
        public const string ScenePath = "Assets/LR2/Scenes/LR2_Saltworks.unity";
        public const string DeadsectorScenePath = "Assets/LR2/Scenes/LR2_Deadsector.unity";
        public const string FoundryScenePath = "Assets/LR2/Scenes/LR2_Foundry.unity";
        public const string TestRangeScenePath = "Assets/LR2/Scenes/LR2_TestRange.unity";
        public const string MenuScenePath = "Assets/LR2/Scenes/LR2_MainMenu.unity";
        private const string MatDir = "Assets/LR2/Materials/Environment";
        private const string LightDir = "Assets/LR2/Data/Lighting";
        private const string ConfigPath = "Assets/LR2/Data/Map/SaltworksConfig.asset";
        private static readonly Vector2 PlayMin = new Vector2(-27f, -23f), PlayMax = new Vector2(27f, 23f);

        private static Transform _structures, _props, _background, _lighting, _gameplay, _spawns, _cover, _tactical, _systems, _decals;
        private static Mats _m;
        private static MapBuildConfig _cfg;
        private static System.Random _rnd;
        private static readonly Dictionary<string, Mesh> _procMeshes = new Dictionary<string, Mesh>();

        private class Mats
        {
            public Material concrete, asphalt, wallPlaster, metal, rust, corrugated, roof, wood, salt, curb, water, glass, darkMetal, steelGrate;
            public Material[] containers;
        }

        // ================================================================================================= entry
        public static void Build()
        {
            _cfg = LoadOrCreate<MapBuildConfig>(ConfigPath);
            ApplyReclaimedPreset(_cfg);
            _rnd = new System.Random(_cfg.seed);
            ConfigureNavAgent();
            EnsureFolder("Assets/LR2/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var root = new GameObject("Saltworks");
            root.AddComponent<MapRoot>().config = _cfg;
            var env = Child(root.transform, "Environment");
            _structures = Child(env, "Structures");
            _props = Child(env, "Props");
            _background = Child(env, "Background");
            _decals = Child(env, "Decals");
            _lighting = Child(root.transform, "Lighting");
            _gameplay = Child(root.transform, "Gameplay");
            _spawns = Child(_gameplay, "Spawns");
            _cover = Child(_gameplay, "Cover");
            _tactical = Child(_gameplay, "Tactical");
            _systems = Child(root.transform, "Systems");

            _m = MakeMaterials();
            BuildGround();
            BuildControlHouse();
            BuildWorkshop();
            BuildWarehouse();
            BuildLoadingDock();
            BuildContainerYard();
            BuildQuay();
            BuildMidYard();
            BuildBoundary();
            if (_cfg.backgroundDressing) BuildBackground();
            if (_cfg.decals) PlaceDecals();
            BuildLighting();
            BuildGameplay();
            MarkStatic(env);
            BuildNature();   // after MarkStatic: swaying plants must stay dynamic

            EliminateSceneClipping("LR2_Saltworks", _structures, _props, _nature, _spawns, _gameplay);
            SetupDestructibleProps(root);

            Physics.SyncTransforms();
            BakeNavMesh(root, "Assets/LR2/Scenes/LR2_Saltworks_NavMesh.asset", true);
            GenerateCoverPoints(PlayMin, PlayMax, new[] { 0.6f, 3.7f, 5.3f });
            BuildMinimap("Assets/LR2/UI/Minimap/Saltworks_Minimap.png", PlayMin, PlayMax);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("[LR2] Map built: " + ScenePath);
        }

        // ================================================================================================= materials
        private static Mats MakeMaterials()
        {
            // Photoscanned Leartes texture sets (base colour + normal + ORM/ARD converted to HDRP mask maps), triplanar.
            var m = new Mats
            {
                concrete = PhotoMat("M_LR2_Concrete", "T_DamagedConcrete_BC", "T_DamagedConcrete_N", "T_DamagedConcrete_ORM", false, 0.33f, new Color(0.74f, 0.75f, 0.7f)),
                asphalt = PhotoMat("M_LR2_Ground", "T_BrokenAsphalt_BC", "T_BrokenAsphalt_N", "T_Brokenasphalt_ARD", true, 0.2f, new Color(0.66f, 0.68f, 0.64f)),
                wallPlaster = PhotoMat("M_LR2_Wall", "T_Concrete_BC", "T_Concrete_N", "T_Concrete_ORM", false, 0.36f, new Color(0.68f, 0.7f, 0.64f)),
                metal = PhotoMat("M_LR2_Metal", "T_Steel_BC", "T_Steel_N", "T_Steel_ORM", false, 0.8f, new Color(0.75f, 0.75f, 0.75f)),
                rust = PhotoMat("M_LR2_Rust", "T_Rustymetal_N", "T_Rustymetal_N", "T_Rustymetal_ORM", false, 0.6f, Color.white, "T_Rustymetal_BC"),
                corrugated = PhotoMat("M_LR2_Corrugated", "T_MetalplaneRusty_N", "T_MetalplaneRusty_N", "T_MetalplaneRusty_ORM", false, 0.45f, new Color(0.8f, 0.8f, 0.8f), "T_MetalplaneRusty_BC"),
                roof = PhotoMat("M_LR2_Roof", "T_TileRust_BC", "T_TileRust_N", "T_TileRust_ORM", false, 0.4f, new Color(0.78f, 0.78f, 0.78f)),
                wood = PhotoMat("M_LR2_Wood", "T_Planks01_B", "T_Planks01_N", "T_Planks01_ORM", false, 0.7f, new Color(0.75f, 0.72f, 0.68f)),
                salt = PhotoMat("M_LR2_Gravel", "T_Mud_BC", "T_Mud_N", "T_Mud_ARD", true, 0.45f, new Color(0.8f, 0.78f, 0.72f)),
                curb = SaveMaterial(NewLit(new Color(0.45f, 0.42f, 0.18f), 0.2f), MatDir + "/M_LR2_SafetyYellow.mat"),
                darkMetal = PhotoMat("M_LR2_DarkMetal", "T_Steel_BC", "T_Steel_N", "T_Steel_ORM", false, 0.9f, new Color(0.22f, 0.23f, 0.24f)),
                steelGrate = PhotoMat("M_LR2_Grate", "T_Metalplane_BC", "T_Metalplane_N", "T_Metalplane_ORM", false, 0.8f, new Color(0.6f, 0.6f, 0.6f)),
                glass = FindLeartes("M_Glass") ?? SaveMaterial(NewLit(new Color(0.2f, 0.25f, 0.27f), 0.95f), MatDir + "/M_LR2_Glass.mat"),
                water = FindLeartes("M_Ocean") ?? SaveMaterial(NewLit(new Color(0.03f, 0.07f, 0.08f), 0.96f), MatDir + "/M_LR2_Water.mat"),
            };
            Color[] containerColors =
            {
                new Color(0.62f, 0.25f, 0.18f), new Color(0.24f, 0.34f, 0.46f), new Color(0.28f, 0.4f, 0.28f),
                new Color(0.72f, 0.5f, 0.24f), new Color(0.5f, 0.5f, 0.5f), new Color(0.62f, 0.58f, 0.5f)
            };
            m.containers = new Material[containerColors.Length];
            for (int i = 0; i < containerColors.Length; i++)
                m.containers[i] = PhotoMat($"M_LR2_Container_{i}", "T_MetalplaneMid_BC", "T_MetalplaneMid_N", "T_MetalplaneMid_ORM", false, 0.42f, containerColors[i] * 1.3f);
            return m;
        }

        private static Texture2D FindTex(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var root in new[] { "Assets/LeartesStudios", "Assets/ScansFactory" })
            {
                if (!AssetDatabase.IsValidFolder(root)) continue;
                foreach (var g in AssetDatabase.FindAssets(name + " t:Texture2D", new[] { root }))
                {
                    var p = AssetDatabase.GUIDToAssetPath(g);
                    if (string.Equals(Path.GetFileNameWithoutExtension(p), name, System.StringComparison.OrdinalIgnoreCase))
                        return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                }
            }
            return null;
        }

        /// <summary>Converts an Unreal-style ORM (AO, Roughness, Metal) or ARD (AO, Roughness, Displacement) map into an
        /// HDRP mask map (R metallic, G AO, B detail/height, A smoothness).</summary>
        private static Texture2D MakeMask(Texture2D src, bool ard, string outPath)
        {
            if (src == null) return null;
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
            if (existing != null) return existing;
            int w = Mathf.Min(src.width, 1024), h = Mathf.Min(src.height, 1024);
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var read = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            read.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            read.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var px = read.GetPixels32();
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                px[i] = new Color32(ard ? (byte)0 : c.b, c.r, ard ? c.b : (byte)0, (byte)(255 - c.g));
            }
            read.SetPixels32(px);
            read.Apply();
            EnsureFolder(Path.GetDirectoryName(outPath).Replace('\\', '/'));
            File.WriteAllBytes(outPath, read.EncodeToPNG());
            Object.DestroyImmediate(read);
            AssetDatabase.ImportAsset(outPath, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(outPath);
            if (imp != null)
            {
                imp.sRGBTexture = false;
                imp.mipmapEnabled = true;
                imp.wrapMode = TextureWrapMode.Repeat;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
        }

        private static Shader _hdrpLit;

        /// <summary>HDRP/Lit triplanar material from a photoscan texture set. altBase: base colour if it lives under another name.</summary>
        private static Material PhotoMat(string outName, string baseName, string normalName, string maskName, bool ard, float worldScale, Color tint, string altBase = null)
        {
            if (_hdrpLit == null) _hdrpLit = Shader.Find("HDRP/Lit");
            var baseTex = FindTex(altBase ?? baseName);
            if (baseTex == null && altBase != null) baseTex = FindTex(baseName.Replace("_N", "_BC"));
            var normal = FindTex(normalName);
            var mask = MakeMask(FindTex(maskName), ard, $"{MatDir}/Masks/{maskName}_HDRPMask.png");
            if (baseTex == null) Debug.LogWarning($"[LR2] Texture not found for {outName}: {altBase ?? baseName}");
            var m = new Material(_hdrpLit);
            m.SetColor("_BaseColor", tint);
            if (baseTex != null) m.SetTexture("_BaseColorMap", baseTex);
            if (normal != null)
            {
                m.SetTexture("_NormalMap", normal);
                m.SetFloat("_NormalScale", 1.25f);
            }
            if (mask != null)
            {
                m.SetTexture("_MaskMap", mask);
                m.SetFloat("_MetallicRemapMin", 0f);
                m.SetFloat("_MetallicRemapMax", 1f);
                m.SetFloat("_AORemapMin", 0.1f);
                m.SetFloat("_AORemapMax", 1f);
                m.SetFloat("_SmoothnessRemapMin", 0f);
                m.SetFloat("_SmoothnessRemapMax", 0.8f);
            }
            else m.SetFloat("_Smoothness", 0.22f);
            m.SetFloat("_UVBase", 5f);             // triplanar world mapping: no stretching on scaled blocks
            m.SetFloat("_TexWorldScale", worldScale);
            HDMaterial.ValidateMaterial(m);
            return SaveMaterial(m, $"{MatDir}/{outName}.mat");
        }

        private static Material FindLeartes(string name)
        {
            foreach (var g in AssetDatabase.FindAssets(name + " t:Material", new[] { "Assets/LeartesStudios" }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (Path.GetFileNameWithoutExtension(p) == name) return AssetDatabase.LoadAssetAtPath<Material>(p);
            }
            return null;
        }

        /// <summary>Clones a Leartes HDRP/Lit material and switches it to triplanar world mapping (no stretching on scaled blocks).</summary>
        private static Material EnvMat(string outName, string[] candidates, Color fallback, float worldScale, float smoothness)
        {
            Material src = null;
            foreach (var c in candidates)
            {
                src = FindLeartes(c);
                if (src != null && src.shader != null && src.shader.name == "HDRP/Lit") break;
                src = null;
            }
            Material m;
            if (src != null)
            {
                m = Object.Instantiate(src);
                m.SetFloat("_UVBase", 5f);              // Triplanar
                m.SetFloat("_TexWorldScale", worldScale);
                m.SetFloat("_UVDetail", 5f);
                HDMaterial.ValidateMaterial(m);
            }
            else m = NewLit(fallback, smoothness);
            return SaveMaterial(m, $"{MatDir}/{outName}.mat");
        }

        // ================================================================================================= helpers
        private static Transform Child(Transform parent, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        private const string ProcMeshDir = "Assets/LR2/Meshes/Procedural";

        private static Mesh GetOrBuildProcMesh(string key, int nx, int ny, int nz, System.Func<Vector3, Vector3, Vector3> deform)
        {
            if (_procMeshes.TryGetValue(key, out var cached) && cached != null) return cached;
            EnsureFolder(ProcMeshDir);
            string path = $"{ProcMeshDir}/{key}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            var mesh = existing != null ? existing : new Mesh { name = key };
            mesh.Clear();

            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            void AddFace(Vector3 normal, Vector3 uAxis, Vector3 vAxis, int nu, int nv)
            {
                if (Vector3.Dot(Vector3.Cross(uAxis, vAxis), normal) < 0f) (uAxis, vAxis) = (vAxis, uAxis);
                int start = verts.Count;
                for (int iv = 0; iv <= nv; iv++)
                {
                    float fv = iv / (float)nv;
                    for (int iu = 0; iu <= nu; iu++)
                    {
                        float fu = iu / (float)nu;
                        Vector3 raw = normal * 0.5f + (fu - 0.5f) * uAxis + (fv - 0.5f) * vAxis;
                        verts.Add(deform != null ? deform(raw, normal) : raw);
                        uvs.Add(new Vector2(fu, fv));
                    }
                }
                int stride = nu + 1;
                for (int iv = 0; iv < nv; iv++)
                {
                    for (int iu = 0; iu < nu; iu++)
                    {
                        int i00 = start + iv * stride + iu;
                        int i10 = i00 + 1;
                        int i01 = i00 + stride;
                        int i11 = i01 + 1;
                        tris.Add(i00); tris.Add(i10); tris.Add(i11);
                        tris.Add(i00); tris.Add(i11); tris.Add(i01);
                    }
                }
            }

            AddFace(Vector3.forward, Vector3.right, Vector3.up, nx, ny);
            AddFace(Vector3.back, Vector3.left, Vector3.up, nx, ny);
            AddFace(Vector3.right, Vector3.back, Vector3.up, nz, ny);
            AddFace(Vector3.left, Vector3.forward, Vector3.up, nz, ny);
            AddFace(Vector3.up, Vector3.right, Vector3.back, nx, nz);
            AddFace(Vector3.down, Vector3.right, Vector3.forward, nx, nz);

            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();

            if (existing == null) AssetDatabase.CreateAsset(mesh, path);
            else EditorUtility.SetDirty(mesh);
            _procMeshes[key] = mesh;
            return mesh;
        }

        private static Mesh GetWarpedContainerMesh(int variant)
        {
            int v = Mathf.Abs(variant) % 4;
            float s = v * 17.3f + 3.1f;
            return GetOrBuildProcMesh($"SM_LR2_Container_Warp{v}", 16, 6, 6, (p, _) =>
            {
                // Keep vertical corner posts crisper while corroding/denting corrugated sides & roof panels.
                // Pure position-only deformation keeps every cube seam 100% watertight.
                float edgeMaskX = Mathf.Clamp01((0.48f - Mathf.Abs(p.x)) * 12f);
                float edgeMaskY = Mathf.Clamp01((0.46f - Mathf.Abs(p.y)) * 10f);
                float edgeMaskZ = Mathf.Clamp01((0.46f - Mathf.Abs(p.z)) * 10f);
                float panelMask = edgeMaskX * edgeMaskY;

                if (Mathf.Abs(p.z) > 0.35f)
                    p.z += Mathf.Sign(p.z) * Mathf.Sin(p.x * 28f * Mathf.PI) * 0.013f * panelMask;
                if (p.y > 0.45f)
                    p.y += Mathf.Sin(p.x * 22f * Mathf.PI) * 0.008f * edgeMaskX * edgeMaskZ;

                // 3 localized blast/forklift dents per variant
                float dent = 0f;
                for (int k = 0; k < 3; k++)
                {
                    float cx = Mathf.Sin(s + k * 2.4f) * 0.34f;
                    float cy = Mathf.Cos(s * 0.7f + k * 3.1f) * 0.22f;
                    float cz = (k % 2 == 0 ? 1f : -1f) * 0.5f;
                    float dx = (p.x - cx) * 3.8f, dy = (p.y - cy) * 3.2f, dz = (p.z - cz) * 2.6f;
                    dent += Mathf.Exp(-(dx * dx + dy * dy + dz * dz) * 3.2f) * (0.045f + k * 0.014f);
                }
                if (Mathf.Abs(p.z) > 0.35f) p.z -= Mathf.Sign(p.z) * dent * panelMask;
                if (p.y > 0.45f) p.y -= dent * 0.45f * edgeMaskX * edgeMaskZ;

                // Subtle frame twist / beam sag along length
                float bow = (Mathf.PerlinNoise(p.x * 2.3f + s, p.y * 1.9f + s) - 0.5f) * 0.026f;
                p.z += bow;
                p.y += (Mathf.PerlinNoise(p.x * 1.7f + s + 5f, p.z * 2.1f) - 0.55f) * 0.022f * (p.y > 0f ? 1f : 0.25f);
                return p;
            });
        }

        private static Mesh GetWarpedWallMesh(int variant, bool brokenTop, float breakDepth = 0.22f, float warpAmp = 0.032f)
        {
            int v = Mathf.Abs(variant) % 4;
            float s = v * 23.7f + 7.4f;
            string tag = brokenTop ? $"SM_LR2_Wall_Broken{v}_{Mathf.RoundToInt(breakDepth * 100f)}" : $"SM_LR2_Masonry_Warp{v}";
            return GetOrBuildProcMesh(tag, brokenTop ? 18 : 10, 6, brokenTop ? 4 : 2, (p, _) =>
            {
                float ny = p.y + 0.5f; // 0 bottom .. 1 top
                // Pure position-only masonry warp keeps all vertical corner seams watertight (zero corner slits)
                float hFade = Mathf.Clamp01(ny * 1.8f);
                float wobbleZ = (Mathf.PerlinNoise(p.x * 3.4f + s, ny * 2.8f + s) - 0.5f) * 2f * warpAmp * hFade;
                float wobbleX = (Mathf.PerlinNoise(p.z * 3.4f + s + 11.3f, ny * 2.8f + s) - 0.5f) * 1.1f * warpAmp * hFade;
                p.z += wobbleZ * Mathf.Clamp01(Mathf.Abs(p.z) * 2f);
                p.x += wobbleX * Mathf.Clamp01(Mathf.Abs(p.x) * 2f);

                if (brokenTop && p.y > 0.12f)
                {
                    float topFade = Mathf.Clamp01((p.y - 0.12f) / 0.38f);
                    float ridgeA = Mathf.PerlinNoise(p.x * 3.1f + s, s * 0.37f);
                    float ridgeB = Mathf.PerlinNoise(p.x * 7.8f + s * 1.9f, p.z * 2.5f + 1.1f);
                    // Sharp angular craters/bite-outs along the upper coping
                    float bite = Mathf.Min(0.24f, Mathf.Pow(Mathf.Clamp01(ridgeA * 0.72f + ridgeB * 0.48f), 1.45f) * breakDepth);
                    p.y -= bite * topFade;
                    if (topFade > 0.6f && Mathf.Abs(p.z) > 0.42f)
                        p.z *= 1f - ridgeB * 0.08f * topFade;
                }
                else if (!brokenTop)
                {
                    // Mild chipped corners & weathered edge bevel on structural piers/blocks (position-only)
                    float chip = Mathf.PerlinNoise(p.x * 5.1f + s, p.y * 5.7f + p.z * 3.3f + s);
                    Vector3 inDir = new Vector3(
                        Mathf.Abs(p.x) > 0.38f ? Mathf.Sign(p.x) * Mathf.Clamp01((Mathf.Abs(p.x) - 0.38f) * 8f) : 0f,
                        Mathf.Abs(p.y) > 0.35f ? Mathf.Sign(p.y) * Mathf.Clamp01((Mathf.Abs(p.y) - 0.35f) * 6f) : 0f,
                        Mathf.Abs(p.z) > 0.38f ? Mathf.Sign(p.z) * Mathf.Clamp01((Mathf.Abs(p.z) - 0.38f) * 8f) : 0f);
                    p -= inDir * (chip * 0.025f);
                }
                return p;
            });
        }

        private static Mesh GetRubbleMesh(int variant)
        {
            int v = Mathf.Abs(variant) % 3;
            float s = v * 31.1f + 4.9f;
            return GetOrBuildProcMesh($"SM_LR2_Rubble{v}", 4, 3, 4, (p, n) =>
            {
                float d = Mathf.PerlinNoise(p.x * 2.8f + p.z * 1.9f + s, p.y * 3.1f + s);
                float rock = 0.62f + d * 0.65f;
                p.x *= rock;
                p.z *= rock;
                if (p.y > 0f) p.y *= 0.45f + d * 0.75f;
                return p;
            });
        }

        private static void ApplyMeshKeepBoxCollider(GameObject go, Mesh mesh)
        {
            if (go == null || mesh == null) return;
            var mf = go.GetComponent<MeshFilter>();
            if (mf != null) mf.sharedMesh = mesh;
        }

        private static void AddWarTornWallDetails(Transform parent, Vector3 wallCenter, float wallLen, float wallTopY, float yaw, Material mat, int seedBase, bool includeRebar = true, bool includeRubble = true)
        {
            Vector3 right = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
            Vector3 fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            if (includeRebar)
            {
                int rods = Mathf.Clamp(Mathf.RoundToInt(wallLen * 0.35f), 2, 7);
                for (int i = 0; i < rods; i++)
                {
                    float h1 = Mathf.PerlinNoise(i * 3.7f + seedBase, seedBase * 0.3f);
                    float h2 = Mathf.PerlinNoise(seedBase * 0.5f, i * 5.3f + 2.1f);
                    float along = Mathf.Lerp(-wallLen * 0.42f, wallLen * 0.42f, (i + 0.35f + h1 * 0.3f) / rods);
                    float y = wallTopY - 0.14f - h2 * 0.28f;
                    var rod = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    rod.name = "Rebar";
                    rod.transform.SetParent(parent, false);
                    rod.transform.localPosition = wallCenter + right * along + Vector3.up * (y - wallCenter.y) + fwd * ((h1 - 0.5f) * 0.16f);
                    rod.transform.localRotation = Quaternion.Euler(12f + h2 * 28f, yaw + (h1 - 0.5f) * 45f, (h2 - 0.5f) * 35f);
                    rod.transform.localScale = new Vector3(0.022f, 0.22f + h1 * 0.18f, 0.022f);
                    rod.GetComponent<Renderer>().sharedMaterial = _m.rust;
                    rod.layer = LR2Layers.Debris;
                    Object.DestroyImmediate(rod.GetComponent<Collider>());
                }
            }
            if (includeRubble && wallCenter.y < 3.5f)
            {
                int chunks = Mathf.Clamp(Mathf.RoundToInt(wallLen * 0.22f), 2, 6);
                for (int i = 0; i < chunks; i++)
                {
                    float h1 = Mathf.PerlinNoise(i * 7.1f + seedBase, 1.3f);
                    float h2 = Mathf.PerlinNoise(2.9f, i * 4.7f + seedBase);
                    float along = Mathf.Lerp(-wallLen * 0.44f, wallLen * 0.44f, (i + h1 * 0.6f) / chunks);
                    float sideOff = (i % 2 == 0 ? 1f : -1f) * (0.36f + h2 * 0.45f);
                    var rb = Box("RubbleChunk", parent, new Vector3(wallCenter.x, 0.14f, wallCenter.z) + right * along + fwd * sideOff,
                        new Vector3(0.42f + h1 * 0.55f, 0.24f + h2 * 0.26f, 0.36f + h2 * 0.45f), mat, LR2Layers.Debris, SurfaceType.Concrete, h1 * 360f);
                    ApplyMeshKeepBoxCollider(rb, GetRubbleMesh(seedBase + i));
                    Object.DestroyImmediate(rb.GetComponent<Collider>());
                }
            }
        }

        private static GameObject Box(string name, Transform parent, Vector3 center, Vector3 size, Material mat, int layer = LR2Layers.Default, SurfaceType? surface = null, float yaw = 0f)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            go.layer = layer;
            if (surface.HasValue) go.AddComponent<SurfaceIdentifier>().surface = surface.Value;
            return go;
        }

        /// <summary>Wall from a to b (XZ) with rectangular openings: (offset along wall, width, bottom, top).</summary>
        private static void Wall(string name, Transform parent, Vector3 a, Vector3 b, float height, float thick, Material mat, params Vector4[] openings)
            => WallWithFloor(name, parent, a, b, height, thick, mat, 0.102f, openings);

        private static void WallWithFloor(string name, Transform parent, Vector3 a, Vector3 b, float height, float thick, Material mat, float floorOffset, params Vector4[] openings)
        {
            var root = Child(parent, name);
            Vector3 dir = (b - a);
            dir.y = 0f;
            float len = dir.magnitude;
            dir /= len;
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg - 90f;
            bool xAxis = Mathf.Abs(dir.x) >= Mathf.Abs(dir.z); // X-walls span corners; Z-walls butt cleanly inside them (no Z-fighting)
            var ops = new List<Vector4>(openings);
            ops.Sort((p, q) => p.x.CompareTo(q.x));
            float cursor = 0f;
            int i = 0;
            void Seg(float from, float to, float y0, float y1)
            {
                if (to - from < 0.01f || y1 - y0 < 0.01f) return;
                float trimFrom = Mathf.Approximately(from, 0f) ? (xAxis ? -thick * 0.5f : thick * 0.502f) : from;
                float trimTo = Mathf.Approximately(to, len) ? len + (xAxis ? thick * 0.5f : -thick * 0.502f) : to;
                if (trimTo - trimFrom < 0.01f) return;
                if (y0 < 0.01f && a.y < 0.05f && y1 > 0.14f && floorOffset > 0.001f) y0 = floorOffset;
                Vector3 mid = a + dir * ((trimFrom + trimTo) * 0.5f);
                var seg = Box($"Seg{i++}", root, new Vector3(mid.x, a.y + (y0 + y1) * 0.5f, mid.z), new Vector3(trimTo - trimFrom, y1 - y0, thick), mat, LR2Layers.Default, SurfaceType.Concrete, yaw);
                if (mat == _m.wallPlaster || mat == _m.concrete || mat == _m.corrugated)
                    ApplyMeshKeepBoxCollider(seg, GetWarpedWallMesh(i + name.GetHashCode(), false, 0.14f, 0.018f));
            }
            foreach (var o in ops)
            {
                float s = o.x - o.y * 0.5f, e = o.x + o.y * 0.5f;
                Seg(cursor, s, 0f, height);
                Seg(s, e, 0f, o.z);          // below opening (window sill)
                Seg(s, e, o.w, height);      // above opening (lintel)
                cursor = e;
            }
            Seg(cursor, len, 0f, height);
        }

        /// <summary>Stairs: visual steps + a smooth invisible ramp collider (PlayerClip) that soldiers and the NavMesh use.</summary>
        private static void Stairs(string name, Transform parent, Vector3 bottom, Vector3 top, float width, Material stepMat, bool railings = true)
        {
            var root = Child(parent, name);
            Vector3 flat = top - bottom;
            float rise = flat.y;
            flat.y = 0f;
            float run = flat.magnitude;
            Vector3 dir = flat / run;
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            int steps = Mathf.Max(2, Mathf.RoundToInt(rise / 0.18f));
            float stepRun = run / steps, stepRise = rise / steps;
            for (int s = 0; s < steps; s++)
            {
                Vector3 p = bottom + dir * (stepRun * (s + 0.5f)) + Vector3.up * (stepRise * (s + 0.5f));
                var step = Box("Step" + s, root, p, new Vector3(width, stepRise, stepRun * 1.02f), stepMat, LR2Layers.Debris, SurfaceType.Metal, yaw);
                Object.DestroyImmediate(step.GetComponent<Collider>());
            }
            // Ramp collider
            float slopeLen = Mathf.Sqrt(run * run + rise * rise);
            float angle = Mathf.Atan2(rise, run) * Mathf.Rad2Deg;
            var ramp = new GameObject("RampCollider");
            ramp.transform.SetParent(root, false);
            ramp.transform.localPosition = (bottom + top) * 0.5f + Vector3.up * 0.02f;
            ramp.transform.localRotation = Quaternion.Euler(-angle, yaw, 0f);
            var bc = ramp.AddComponent<BoxCollider>();
            bc.size = new Vector3(width, 0.1f, slopeLen + 0.2f);
            ramp.layer = LR2Layers.PlayerClip;
            ramp.AddComponent<SurfaceIdentifier>().surface = SurfaceType.Metal;
            if (!railings) return;
            Vector3 side = Vector3.Cross(Vector3.up, dir) * (width * 0.5f);
            foreach (float sgn in new[] { -1f, 1f })
            {
                var rail = Box("Rail", root, (bottom + top) * 0.5f + side * sgn + Vector3.up * 0.95f, new Vector3(0.05f, 0.05f, slopeLen), _m.darkMetal, LR2Layers.Debris);
                rail.transform.localRotation = Quaternion.Euler(-angle, yaw, 0f);
                Object.DestroyImmediate(rail.GetComponent<Collider>());
            }
        }

        private static void Railing(Transform parent, Vector3 a, Vector3 b, float height = 1.05f)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            Box("RailTop", parent, (a + b) * 0.5f + Vector3.up * height, new Vector3(0.05f, 0.05f, len), _m.darkMetal, LR2Layers.Default, SurfaceType.Metal, yaw);
            Box("RailMid", parent, (a + b) * 0.5f + Vector3.up * height * 0.5f, new Vector3(0.04f, 0.04f, len), _m.darkMetal, LR2Layers.Default, SurfaceType.Metal, yaw);
            int posts = Mathf.Max(2, Mathf.CeilToInt(len / 1.5f) + 1);
            for (int i = 0; i < posts; i++)
                Box("Post", parent, Vector3.Lerp(a, b, i / (float)(posts - 1)) + Vector3.up * height * 0.5f, new Vector3(0.05f, height, 0.05f), _m.darkMetal, LR2Layers.Default, SurfaceType.Metal);
        }

        private static GameObject Container(Transform parent, string name, Vector3 basePos, float yaw, int color)
        {
            var mat = _m.containers[Mathf.Abs(color) % _m.containers.Length];
            bool stacked = basePos.y > 0.5f;
            float settleY = stacked ? basePos.y : basePos.y - 0.02f;
            float jYaw = yaw + R(-1.3f, 1.3f);
            var c = Box(name, parent, new Vector3(basePos.x, settleY + 1.295f, basePos.z), new Vector3(6.06f, 2.59f, 2.44f), mat, LR2Layers.Default, SurfaceType.Metal, jYaw);
            c.transform.localRotation = Quaternion.Euler(stacked ? R(-0.25f, 0.25f) : R(-0.75f, 0.75f), jYaw, stacked ? R(-0.25f, 0.25f) : R(-0.95f, 0.95f));
            ApplyMeshKeepBoxCollider(c, GetWarpedContainerMesh(Mathf.Abs(color) + name.GetHashCode()));
            // door end detail (flush on container face, slightly scarred frame)
            var door = Box("Doors", c.transform, new Vector3(0.503f, 0f, 0f), new Vector3(0.01f, 0.93f, 0.92f), _m.darkMetal);
            Object.DestroyImmediate(door.GetComponent<Collider>());
            return c;
        }

        /// <summary>Instantiates a Leartes prefab by name, grounds it, ensures a collider. Returns null if not found.</summary>
        private static GameObject Prop(string prefabName, Transform parent, Vector3 pos, float yaw, float scale = 1f, bool addCollider = true)
        {
            GameObject prefab = null;
            foreach (var g in AssetDatabase.FindAssets(prefabName + " t:Prefab", new[] { "Assets/LeartesStudios" }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (Path.GetFileNameWithoutExtension(p) == prefabName)
                {
                    prefab = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                    break;
                }
            }
            if (prefab == null) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = Vector3.one * scale;
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return go;
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            go.transform.position += Vector3.up * (pos.y - b.min.y); // sit on the target height
            if (addCollider && go.GetComponentInChildren<Collider>() == null)
            {
                b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                var bc = go.AddComponent<BoxCollider>();
                bc.center = go.transform.InverseTransformPoint(b.center);
                Vector3 ls = go.transform.lossyScale;
                bc.size = new Vector3(b.size.x / Mathf.Max(0.001f, ls.x), b.size.y / Mathf.Max(0.001f, ls.y), b.size.z / Mathf.Max(0.001f, ls.z));
            }
            return go;
        }

        private static float R(float a, float b) => (float)(a + _rnd.NextDouble() * (b - a));

        // ================================================================================================= geometry
        // ------------------------------------------------------------------ terrain
        private static Terrain _terrain;
        private const float TerrainBaseY = -3f, TerrainHeight = 30f;

        /// <summary>Building footprints that must be perfectly flat (min.x, min.z, max.x, max.z).</summary>
        private static readonly Vector4[] FlatZones =
        {
            new Vector4(-26.5f, -8.5f, -15.5f, 4.5f),   // workshop
            new Vector4(-4.9f, -3.3f, 5.9f, 5.8f),      // control house + stairs
            new Vector4(-15.8f, -22.4f, 15.8f, -9.4f),  // warehouse + loading dock + ramps
            new Vector4(11.2f, -14.8f, 26.6f, 8.0f),    // container yard
            new Vector4(-19.6f, 11.2f, -10.6f, 18.6f),  // quay west stack + loose container + stairs
            new Vector4(-11.2f, 15.6f, 9.4f, 18.5f),    // quay stacks + bridge
            new Vector4(11.6f, 18.0f, 18.6f, 21.0f),    // quay loose container (east)
            new Vector4(-30f, 20.5f, 30f, 26f),         // quay strip (playable)
            new Vector4(-76f, 21.5f, 76f, 26.5f),       // full QuaySlab width so hillside never pierces slab outside play
        };

        /// <summary>Designed ground height (metres, relative to y = 0) at a world XZ position.</summary>
        private static float DesignHeight(float x, float z)
        {
            // Rolling ground: two octaves, ~±1.6 m in the open yard
            float h = (Mathf.PerlinNoise(x * 0.05f + 11.3f, z * 0.05f + 7.1f) - 0.5f) * 3.2f
                    + (Mathf.PerlinNoise(x * 0.16f + 50f, z * 0.16f + 50f) - 0.5f) * 0.6f;
            float Gauss(float cx, float cz, float rx, float rz, float amp)
            {
                float dx = (x - cx) / rx, dz = (z - cz) / rz;
                return amp * Mathf.Exp(-(dx * dx + dz * dz));
            }
            h += Gauss(-12.5f, 8.5f, 7f, 6f, 3.0f);      // NW berm: high ground over the north yard
            h += Gauss(4f, 9.5f, 6f, 3.5f, 2.2f);         // central hill between house and quay
            h += Gauss(11f, -3f, 5f, 4.5f, 1.8f);         // east hump next to mid
            h += Gauss(3f, -6.4f, 9f, 1.6f, -1.5f);       // drainage ditch across mid-south (trench)
            h += Gauss(-11f, -2f, 4f, 4f, -1.2f);         // west dip in front of the workshop
            // Outside the walls: hills for the skyline
            float ox = Mathf.Max(0f, Mathf.Abs(x) - 27.5f), oz = Mathf.Max(0f, -23.5f - z);
            float outside = Mathf.Sqrt(ox * ox + oz * oz);
            if (outside > 0f)
                h += outside * 0.25f + Mathf.SmoothStep(0f, 1f, outside / 22f) * (Mathf.PerlinNoise(x * 0.02f, z * 0.02f) * 16f);
            // Flatten building pads (just below 0 so concrete slabs sit on top without z-fighting)
            foreach (var r in FlatZones)
            {
                float dx = Mathf.Max(r.x - x, 0f, x - r.z), dz = Mathf.Max(r.y - z, 0f, z - r.w);
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(d / 4.5f));
                h = Mathf.Lerp(-0.02f, h, t);
            }
            return h;
        }

        private static float GroundY(float x, float z)
        {
            if (_terrain == null) return 0f;
            return _terrain.SampleHeight(new Vector3(x, 0f, z)) + _terrain.transform.position.y;
        }

        /// <summary>Puts a ground-level design position (y = 0) onto the terrain.</summary>
        private static Vector3 G(Vector3 p, float offset = 0f) => new Vector3(p.x, Mathf.Max(GroundY(p.x, p.z), -0.02f) + offset, p.z);

        /// <summary>Standing position for spawns/markers: on terrain, never below building floors.</summary>
        private static Vector3 Stand(Vector3 p) => new Vector3(p.x, Mathf.Max(GroundY(p.x, p.z) + 0.05f, 0.12f), p.z);

        private static void BuildTerrain(Transform parent)
        {
            const int res = 257;
            var data = new TerrainData { heightmapResolution = res };
            Vector3 origin = new Vector3(-80f, TerrainBaseY, -70f);
            data.size = new Vector3(160f, TerrainHeight, 96f);
            var heights = new float[res, res];
            for (int iz = 0; iz < res; iz++)
                for (int ix = 0; ix < res; ix++)
                {
                    float x = origin.x + data.size.x * ix / (res - 1), z = origin.z + data.size.z * iz / (res - 1);
                    heights[iz, ix] = Mathf.Clamp01((DesignHeight(x, z) - TerrainBaseY) / TerrainHeight);
                }
            data.SetHeights(0, 0, heights);

            // Layers from Leartes photoscan textures
            TerrainLayer Layer(string name, string baseName, string normalName, string maskName, bool ard, Color tint, float tile)
            {
                var l = new TerrainLayer
                {
                    diffuseTexture = FindTex(baseName),
                    normalMapTexture = FindTex(normalName),
                    maskMapTexture = MakeMask(FindTex(maskName), ard, $"{MatDir}/Masks/{maskName}_HDRPMask.png"),
                    tileSize = new Vector2(tile, tile),
                    diffuseRemapMax = tint,
                    normalScale = 1.2f,
                    smoothness = 0.12f
                };
                return SaveAsset(l, $"{MatDir}/Terrain/{name}.terrainlayer");
            }
            // forest floor (layer 0 = old yard, now moss/leaf litter), mud, gravel/old concrete
            var asphalt = Layer("TL_ForestFloor", "T_en_landscape_01_01", "T_en_landscape_01_01_n", "", false, new Color(0.8f, 0.8f, 0.78f, 1f), 7f);
            if (asphalt.diffuseTexture == null) asphalt = Layer("TL_ForestFloor", "T_BrokenAsphalt_BC", "T_BrokenAsphalt_N", "T_Brokenasphalt_ARD", true, new Color(0.78f, 0.78f, 0.78f, 1f), 6f);
            var dirt = Layer("TL_Mud", "T_Mud_BC", "T_Mud_N", "T_Mud_ARD", true, new Color(0.8f, 0.76f, 0.7f, 1f), 4f);
            var concrete = Layer("TL_OldConcrete", "T_BrokenAsphalt_BC", "T_BrokenAsphalt_N", "T_Brokenasphalt_ARD", true, new Color(0.7f, 0.72f, 0.68f, 1f), 5f);
            data.terrainLayers = new[] { asphalt, dirt, concrete };
            int ar = data.alphamapResolution = 256;
            var alpha = new float[ar, ar, 3];
            for (int iz = 0; iz < ar; iz++)
                for (int ix = 0; ix < ar; ix++)
                {
                    float x = origin.x + data.size.x * ix / (ar - 1), z = origin.z + data.size.z * iz / (ar - 1);
                    float h = DesignHeight(x, z);
                    float slope = data.GetSteepness((float)ix / (ar - 1), (float)iz / (ar - 1));
                    bool outside = Mathf.Abs(x) > 28f || z < -24f;
                    float dirtW = Mathf.Clamp01((slope - 8f) / 12f) + Mathf.Clamp01((h - 0.9f) * 0.8f) + (outside ? 1f : 0f)
                                  + Mathf.Clamp01((Mathf.PerlinNoise(x * 0.09f + 3f, z * 0.09f + 9f) - 0.62f) * 4f);
                    float concW = 0f;
                    foreach (var r in FlatZones)
                    {
                        float dx = Mathf.Max(r.x - x, 0f, x - r.z), dz = Mathf.Max(r.y - z, 0f, z - r.w);
                        concW = Mathf.Max(concW, 1f - Mathf.Clamp01(Mathf.Sqrt(dx * dx + dz * dz) / 2.5f));
                    }
                    dirtW = Mathf.Clamp01(dirtW) * (1f - concW);
                    float asphaltW = Mathf.Clamp01(1f - dirtW - concW);
                    float sum = asphaltW + dirtW + concW + 1e-4f;
                    alpha[iz, ix, 0] = asphaltW / sum;
                    alpha[iz, ix, 1] = dirtW / sum;
                    alpha[iz, ix, 2] = concW / sum;
                }
            data.SetAlphamaps(0, 0, alpha);
            var savedData = SaveAsset(data, "Assets/LR2/Scenes/LR2_Saltworks_Terrain.asset");

            var go = Terrain.CreateTerrainGameObject(savedData);
            go.name = "Terrain";
            go.transform.SetParent(parent, false);
            go.transform.position = origin;
            go.layer = LR2Layers.Default;
            _terrain = go.GetComponent<Terrain>();
            _terrain.drawInstanced = true;
            _terrain.basemapDistance = 220f;
            _terrain.heightmapPixelError = 4f;
            go.AddComponent<SurfaceIdentifier>().surface = SurfaceType.Dirt;
        }

        private static void BuildGround()
        {
            var g = Child(_structures, "Ground");
            BuildTerrain(g);
            Box("QuaySlab", g, new Vector3(0f, -0.22f, 24f), new Vector3(150f, 0.5f, 4f), _m.concrete, LR2Layers.Default, SurfaceType.Concrete);
            // Painted safety lines (conformed to terrain slope so ends never float)
            for (int i = -5; i <= 5; i++)
            {
                float lx = i * 4.5f, lz = -8.4f;
                float yL = GroundY(lx - 1.2f, lz), yR = GroundY(lx + 1.2f, lz);
                float slopeDeg = Mathf.Atan2(yR - yL, 2.4f) * Mathf.Rad2Deg;
                var line = Box("Line", g, new Vector3(lx, Mathf.Max((yL + yR) * 0.5f, -0.02f) + 0.025f, lz), new Vector3(2.4f, 0.015f, 0.12f), _m.curb);
                line.transform.localRotation = Quaternion.Euler(0f, 0f, slopeDeg);
                Object.DestroyImmediate(line.GetComponent<Collider>());
            }
            // Water beyond the quay
            var water = GameObject.CreatePrimitive(PrimitiveType.Plane);
            water.name = "Water";
            water.transform.SetParent(g, false);
            water.transform.localPosition = new Vector3(0f, -1.3f, 100f);
            water.transform.localScale = new Vector3(40f, 1f, 15f);
            water.GetComponent<Renderer>().sharedMaterial = _m.water;
            Object.DestroyImmediate(water.GetComponent<Collider>());
            Box("QuayWall", g, new Vector3(0f, -1.0f, 26.1f), new Vector3(150f, 2.2f, 0.4f), _m.concrete, LR2Layers.Default, SurfaceType.Concrete);
        }

        private static void BuildControlHouse()
        {
            var h = Child(_structures, "ControlHouse");
            const float x0 = -4.5f, x1 = 4.5f, z0 = -1.5f, z1 = 5.5f, hgt = 3.4f, t = 0.25f;
            Vector4 Door(float off) => new Vector4(off, 1.4f, 0f, 2.3f);
            Vector4 Win(float off) => new Vector4(off, 1.6f, 1.0f, 2.1f);
            Wall("WallWest", h, new Vector3(x0, 0f, z0), new Vector3(x0, 0f, z1), hgt, t, _m.wallPlaster, Door(3.5f));
            Wall("WallEast", h, new Vector3(x1, 0f, z0), new Vector3(x1, 0f, z1), hgt, t, _m.wallPlaster, Door(3.5f));
            Wall("WallNorth", h, new Vector3(x0, 0f, z1), new Vector3(x1, 0f, z1), hgt, t, _m.wallPlaster, Win(2.5f), Win(6.5f));
            Wall("WallSouth", h, new Vector3(x0, 0f, z0), new Vector3(x1, 0f, z0), hgt, t, _m.wallPlaster, Win(3f), Win(7f));
            Box("Floor", h, new Vector3(0f, 0.05f, 2f), new Vector3(9f, 0.1f, 7f), _m.concrete, LR2Layers.Default, SurfaceType.Concrete);
            Box("Roof", h, new Vector3(0f, hgt + 0.1f, 2f), new Vector3(9.3f, 0.2f, 7.3f), _m.concrete, LR2Layers.Default, SurfaceType.Concrete);
            float py = hgt + 0.2f;
            // Parapet (gap at the south-west for the stairs landing; non-overlapping corners)
            Box("ParapetN", h, new Vector3(0f, py + 0.5f, z1 + 0.05f), new Vector3(9.3f, 1.0f, 0.2f), _m.wallPlaster, LR2Layers.Default, SurfaceType.Concrete);
            Box("ParapetE", h, new Vector3(x1 + 0.05f, py + 0.5f, 2f), new Vector3(0.2f, 1.0f, 6.89f), _m.wallPlaster, LR2Layers.Default, SurfaceType.Concrete);
            Box("ParapetW", h, new Vector3(x0 - 0.05f, py + 0.5f, 2.095f), new Vector3(0.2f, 1.0f, 7.09f), _m.wallPlaster, LR2Layers.Default, SurfaceType.Concrete);
            Box("ParapetS", h, new Vector3(0.95f, py + 0.5f, z0 - 0.05f), new Vector3(7.09f, 1.0f, 0.2f), _m.wallPlaster, LR2Layers.Default, SurfaceType.Concrete);
            // Exterior steel stairs (south) + landing onto the roof (1 cm gap before Roof edge to avoid top Z-fighting)
            Stairs("Stairs", h, new Vector3(5.6f, 0f, -2.45f), new Vector3(-1.4f, py, -2.45f), 1.3f, _m.steelGrate);
            Box("Landing", h, new Vector3(-2.75f, py - 0.05f, -2.38f), new Vector3(2.7f, 0.1f, 1.44f), _m.steelGrate, LR2Layers.Default, SurfaceType.Metal);
            Railing(h, new Vector3(-4.1f, py, -3.1f), new Vector3(-1.4f, py, -3.1f));
            // Roof clutter: AC unit + vent (cover on the power position)
            Box("ACUnit", h, new Vector3(2.2f, py + 0.55f, 3.6f), new Vector3(1.8f, 1.1f, 1.2f), _m.metal, LR2Layers.Default, SurfaceType.Metal);
            Box("Vent", h, new Vector3(-2.4f, py + 0.4f, 4.2f), new Vector3(0.9f, 0.8f, 0.9f), _m.rust, LR2Layers.Default, SurfaceType.Metal);
            // Interior: desk + lockers + crates
            Box("Desk", h, new Vector3(-2.6f, 0.4f, 4.4f), new Vector3(2.2f, 0.8f, 0.9f), _m.wood, LR2Layers.Default, SurfaceType.Wood);
            Box("Lockers", h, new Vector3(3.9f, 1.0f, 4.6f), new Vector3(0.6f, 2.0f, 1.6f), _m.metal, LR2Layers.Default, SurfaceType.Metal);
            Box("Crate", h, new Vector3(1.2f, 0.45f, 0.2f), new Vector3(0.9f, 0.9f, 0.9f), _m.wood, LR2Layers.Default, SurfaceType.Wood);
            // Windows glass (thin, bullets penetrate)
            foreach (var (pos, rotY) in new[] { (new Vector3(-2f, 1.55f, z1), 0f), (new Vector3(2f, 1.55f, z1), 0f), (new Vector3(-1.5f, 1.55f, z0), 0f), (new Vector3(2.5f, 1.55f, z0), 0f) })
                Box("Glass", h, pos, new Vector3(1.6f, 1.1f, 0.03f), _m.glass, LR2Layers.Default, SurfaceType.Glass, rotY);
        }

        private static void BuildWorkshop()
        {
            var w = Child(_structures, "Workshop");
            const float x0 = -26f, x1 = -16f, z0 = -8f, z1 = 4f, hgt = 7f, t = 0.3f;
            Wall("WallEast", w, new Vector3(x1, 0f, z0), new Vector3(x1, 0f, z1), hgt, t, _m.corrugated,
                new Vector4(6f, 5f, 0f, 3.2f), new Vector4(6.5f, 3f, 4.4f, 5.8f));
            Wall("WallWest", w, new Vector3(x0, 0f, z0), new Vector3(x0, 0f, z1), hgt, t, _m.corrugated);
            Wall("WallNorth", w, new Vector3(x0, 0f, z1), new Vector3(x1, 0f, z1), hgt, t, _m.corrugated, new Vector4(7f, 1.4f, 0f, 2.3f), new Vector4(3f, 2.2f, 4.5f, 5.6f));
            Wall("WallSouth", w, new Vector3(x0, 0f, z0), new Vector3(x1, 0f, z0), hgt, t, _m.corrugated, new Vector4(3f, 1.4f, 0f, 2.3f));
            Box("Floor", w, new Vector3(-21f, 0.05f, -2f), new Vector3(10f, 0.1f, 12f), _m.concrete, LR2Layers.Default, SurfaceType.Concrete);
            // Upper floor with stairwell opening (west side)
            Box("UpperA", w, new Vector3(-20.2f, 3.5f, -2f), new Vector3(8.4f, 0.2f, 12f), _m.wood, LR2Layers.Default, SurfaceType.Wood);
            Box("UpperB", w, new Vector3(-25.2f, 3.5f, 2.5f), new Vector3(1.6f, 0.2f, 3f), _m.wood, LR2Layers.Default, SurfaceType.Wood);
            Box("UpperC", w, new Vector3(-25.2f, 3.5f, -7.25f), new Vector3(1.6f, 0.2f, 1.5f), _m.wood, LR2Layers.Default, SurfaceType.Wood);
            Stairs("InteriorStairs", w, new Vector3(-25.2f, 0f, -6.4f), new Vector3(-25.2f, 3.6f, 0.9f), 1.3f, _m.wood, false);
            Railing(w, new Vector3(-24.4f, 3.6f, -6.5f), new Vector3(-24.4f, 3.6f, 1.0f));
            Box("Roof", w, new Vector3(-21f, hgt + 0.1f, -2f), new Vector3(10.6f, 0.2f, 12.6f), _m.roof, LR2Layers.Default, SurfaceType.Metal);
            // Upstairs: sandbag-ish cover at the east window + workbench
            Box("WindowCover", w, new Vector3(-16.9f, 4.05f, -2f), new Vector3(0.6f, 0.9f, 2.6f), _m.wood, LR2Layers.Default, SurfaceType.Wood);
            Box("Workbench", w, new Vector3(-22f, 0.45f, 3.2f), new Vector3(3f, 0.9f, 0.9f), _m.wood, LR2Layers.Default, SurfaceType.Wood);
            Box("ToolRack", w, new Vector3(-16.35f, 1.1f, 2.2f), new Vector3(0.4f, 2.2f, 2.5f), _m.metal, LR2Layers.Default, SurfaceType.Metal); // against east wall, clear of InteriorStairs
        }

        private static void BuildWarehouse()
        {
            var wh = Child(_structures, "Warehouse");
            const float x0 = -14f, x1 = 14f, z0 = -22f, z1 = -12f, hgt = 7.5f, t = 0.3f;
            Wall("WallSouth", wh, new Vector3(x0, 0f, z0), new Vector3(x1, 0f, z0), hgt, t, _m.corrugated);
            Wall("WallNorth", wh, new Vector3(x0, 0f, z1), new Vector3(x1, 0f, z1), hgt, t, _m.corrugated,
                new Vector4(7f, 4f, 0f, 4.2f), new Vector4(13.75f, 1.4f, 1.2f, 3.5f), new Vector4(19f, 4f, 0f, 4.2f));
            Wall("WallWest", wh, new Vector3(x0, 0f, z0), new Vector3(x0, 0f, z1), hgt, t, _m.corrugated, new Vector4(5f, 3.2f, 0f, 3.6f));
            Wall("WallEast", wh, new Vector3(x1, 0f, z0), new Vector3(x1, 0f, z1), hgt, t, _m.corrugated, new Vector4(5f, 3.2f, 0f, 3.6f));
            Box("Floor", wh, new Vector3(0f, 0.05f, -17f), new Vector3(28.2f, 0.1f, 10.2f), _m.concrete, LR2Layers.Default, SurfaceType.Concrete);
            Box("Roof", wh, new Vector3(0f, hgt + 0.1f, -17f), new Vector3(28.6f, 0.2f, 10.6f), _m.roof, LR2Layers.Default, SurfaceType.Metal);
            // Steel columns
            for (int i = -2; i <= 2; i++) Box("Column", wh, new Vector3(i * 6f, hgt * 0.5f, -17f), new Vector3(0.35f, hgt, 0.35f), _m.darkMetal, LR2Layers.Default, SurfaceType.Metal);
            // Racking rows (cover + sightline breakers)
            void Rack(Vector3 c, float len)
            {
                var rack = Child(wh, "Rack");
                for (int lvl = 0; lvl < 3; lvl++)
                    Box("Shelf", rack, c + Vector3.up * (0.35f + lvl * 1.1f), new Vector3(len, 0.08f, 1.1f), _m.metal, LR2Layers.Default, SurfaceType.Metal);
                for (int s = 0; s <= 2; s++)
                    Box("Upright", rack, c + new Vector3((s - 1) * len * 0.5f, 1.3f, 0f), new Vector3(0.08f, 2.6f, 1.1f), _m.curb, LR2Layers.Default, SurfaceType.Metal);
                Box("Goods", rack, c + Vector3.up * 0.75f, new Vector3(len * 0.85f, 0.7f, 0.95f), _m.wood, LR2Layers.Default, SurfaceType.Wood);
            }
            Rack(new Vector3(-8f, 0f, -19.5f), 6f);
            Rack(new Vector3(1f, 0f, -19.5f), 6f);
            Rack(new Vector3(9.5f, 0f, -19.5f), 5f);
            Rack(new Vector3(-4.5f, 0f, -15.6f), 5f);
            Rack(new Vector3(5.5f, 0f, -15.6f), 4f);
            // Smooth interior steps/ramps connecting Warehouse floor (y=0.1) up to LoadingDock platform (y=1.2)
            Stairs("DockApproachW", wh, new Vector3(-7f, 0.1f, -14.6f), new Vector3(-7f, 1.2f, -12.1f), 3.2f, _m.concrete, false);
            Stairs("DockApproachE", wh, new Vector3(5f, 0.1f, -14.6f), new Vector3(5f, 1.2f, -12.1f), 3.2f, _m.concrete, false);
        }

        private static void BuildLoadingDock()
        {
            var d = Child(_structures, "LoadingDock");
            Box("Platform", d, new Vector3(0f, 0.6f, -10.8f), new Vector3(24f, 1.2f, 2.4f), _m.concrete, LR2Layers.Default, SurfaceType.Concrete);
            Box("EdgeStripe", d, new Vector3(0f, 1.21f, -9.65f), new Vector3(24f, 0.02f, 0.12f), _m.curb);
            // Ramps (high end meets Platform at x=±12, y=1.2; low end meets ground at x=±15.6)
            foreach (float s in new[] { -1f, 1f })
            {
                var ramp = Box("Ramp", d, new Vector3(s * 13.8f, 0.55f, -10.8f), new Vector3(3.8f, 0.2f, 2.4f), _m.concrete, LR2Layers.Default, SurfaceType.Concrete);
                ramp.transform.localRotation = Quaternion.Euler(0f, 0f, -s * 18f);
            }
            for (int i = -3; i <= 3; i += 2) Box("Bumper", d, new Vector3(i * 3.2f, 0.65f, -9.5f), new Vector3(0.5f, 0.6f, 0.2f), _m.darkMetal, LR2Layers.Default, SurfaceType.Plastic);
        }

        private static void BuildContainerYard()
        {
            var y = Child(_structures, "ContainerYard");
            // Stack A (2-high) and stack B (2-high) joined by a catwalk on top
            Container(y, "A1", new Vector3(20f, 0f, -6f), 0f, 0);
            Container(y, "A2", new Vector3(20f, 0f, -3.5f), 0f, 1);
            Container(y, "A3", new Vector3(20f, 2.59f, -6f), 0f, 3);
            Container(y, "A4", new Vector3(20f, 2.59f, -3.5f), 0f, 4);
            Container(y, "B1", new Vector3(20f, 0f, 4f), 0f, 2);
            Container(y, "B2", new Vector3(20f, 0f, 6.5f), 0f, 5);
            Container(y, "B3", new Vector3(20f, 2.59f, 4f), 0f, 1);
            Container(y, "B4", new Vector3(20f, 2.59f, 6.5f), 0f, 0);
            Box("Catwalk", y, new Vector3(20f, 5.13f, 0.25f), new Vector3(1.5f, 0.1f, 5f), _m.steelGrate, LR2Layers.Default, SurfaceType.Metal);
            Railing(y, new Vector3(19.25f, 5.18f, -2.2f), new Vector3(19.25f, 5.18f, 2.7f));
            Railing(y, new Vector3(20.75f, 5.18f, -2.2f), new Vector3(20.75f, 5.18f, 2.7f));
            Stairs("StairsA", y, new Vector3(24.6f, 0f, -14.2f), new Vector3(24.6f, 5.18f, -7.2f), 1.3f, _m.steelGrate);
            Box("StairsTopLanding", y, new Vector3(24.06f, 5.13f, -6.5f), new Vector3(2.02f, 0.1f, 1.6f), _m.steelGrate, LR2Layers.Default, SurfaceType.Metal);
            // Loose containers (clear of LoadingDock East Ramp)
            Container(y, "C1", new Vector3(18.4f, 0f, -12.4f), 90f, 3);
            Container(y, "C2", new Vector3(25.2f, 0f, 1.2f), 90f, 4);
            Container(y, "C3", new Vector3(14.5f, 0f, 1.5f), 0f, 2);
        }

        private static void BuildQuay()
        {
            var q = Child(_structures, "Quay");
            Container(q, "W1", new Vector3(-8f, 0f, 17f), 0f, 5);
            Container(q, "W2", new Vector3(-8f, 2.59f, 17f), 0f, 0);
            Container(q, "E1", new Vector3(6f, 0f, 17f), 0f, 1);
            Container(q, "E2", new Vector3(6f, 2.59f, 17f), 0f, 3);
            Container(q, "L1", new Vector3(-18.5f, 0f, 11.6f), 90f, 2); // clear of StairsWest bottom at (-17.2, 0, 17)
            Container(q, "L2", new Vector3(15f, 0f, 19.5f), 0f, 4);
            Container(q, "L3", new Vector3(-1f, 0f, 21.2f), 0f, 0);
            // Long catwalk bridge between the stacks (flush between W2 and E2, no overlap Z-fighting)
            Box("Bridge", q, new Vector3(-1f, 5.13f, 17f), new Vector3(7.92f, 0.1f, 1.5f), _m.steelGrate, LR2Layers.Default, SurfaceType.Metal);
            Railing(q, new Vector3(-5f, 5.18f, 16.25f), new Vector3(3f, 5.18f, 16.25f));
            Railing(q, new Vector3(-5f, 5.18f, 17.75f), new Vector3(3f, 5.18f, 17.75f));
            Stairs("StairsWest", q, new Vector3(-17.2f, 0f, 17f), new Vector3(-11.1f, 5.18f, 17f), 1.3f, _m.steelGrate);
            // Quay edge: curb + bollards
            Box("Curb", q, new Vector3(0f, 0.15f, 22.9f), new Vector3(60f, 0.3f, 0.4f), _m.curb, LR2Layers.Default, SurfaceType.Concrete);
            for (float x = -24f; x <= 24f; x += 6f)
            {
                var b = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                b.name = "Bollard";
                b.transform.SetParent(q, false);
                b.transform.localPosition = new Vector3(x, 0.35f, 22.4f);
                b.transform.localScale = new Vector3(0.35f, 0.35f, 0.35f);
                b.GetComponent<Renderer>().sharedMaterial = _m.darkMetal;
                b.AddComponent<SurfaceIdentifier>().surface = SurfaceType.Metal;
            }
        }

        private static void BuildMidYard()
        {
            var y = Child(_structures, "MidYard");
            void RubbleCluster(string name, Vector3 c, float yaw, float scale)
            {
                var rubble = LoadRuins(name);
                if (rubble != null)
                {
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(rubble, _props);
                    go.transform.position = G(c);
                    go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                    go.transform.localScale = Vector3.one * scale;
                    if (go.GetComponentInChildren<Collider>() == null)
                    {
                        var rs = go.GetComponentsInChildren<Renderer>();
                        if (rs.Length > 0)
                        {
                            var b = rs[0].bounds;
                            foreach (var r in rs) b.Encapsulate(r.bounds);
                            var bc = go.AddComponent<BoxCollider>();
                            bc.center = go.transform.InverseTransformPoint(b.center);
                            bc.size = b.size / Mathf.Max(0.01f, scale);
                        }
                    }
                    go.AddComponent<SurfaceIdentifier>().surface = SurfaceType.Concrete;
                }
            }
            RubbleCluster("SM_Rubble_01_01", new Vector3(-9f, 0f, -4.5f), 25f, 1.15f);
            RubbleCluster("SM_Rubble_02_01", new Vector3(9.5f, 0f, 6.5f), -40f, 1.15f);
            Prop("SM_Jersey", _props, G(new Vector3(-9.5f, 0f, -2.5f)), 25f);
            Prop("SM_Jersey", _props, G(new Vector3(9f, 0f, 4.5f)), -35f);
            void Block(Vector3 p, float yaw)
            {
                var blk = Box("ConcreteBlock", y, G(p, 0.44f), new Vector3(1.6f, 0.9f, 0.8f), _m.concrete, LR2Layers.Default, SurfaceType.Concrete, yaw + R(-3f, 3f));
                ApplyMeshKeepBoxCollider(blk, GetWarpedWallMesh(Mathf.RoundToInt(p.x * 13 + p.z), true, 0.16f, 0.038f));
            }
            Block(new Vector3(-4f, 0f, -8f), 0f);
            Block(new Vector3(6f, 0f, -6.5f), 15f);
            Block(new Vector3(-12.5f, 0f, 8f), 80f);
            Block(new Vector3(10f, 0f, 12.5f), 0f);
            Block(new Vector3(-6f, 0f, 11f), -20f);
            Block(new Vector3(12.5f, 0f, -4f), 90f);
        }

        private static void BuildBoundary()
        {
            var b = Child(_structures, "Boundary");
            void BrokenBoundary(string n, Vector3 center, float len, float height, float thick, float yaw, int seed)
            {
                var w = Box(n, b, center, new Vector3(len, height, thick), _m.wallPlaster, LR2Layers.Default, SurfaceType.Concrete, yaw);
                ApplyMeshKeepBoxCollider(w, GetWarpedWallMesh(seed, true, 0.20f, 0.05f));
                AddWarTornWallDetails(b, center, len, center.y + height * 0.44f, yaw, _m.wallPlaster, seed, true, false);
            }
            BrokenBoundary("WallWest", new Vector3(-28.3f, 1.15f, 0f), 52f, 6.4f, 0.55f, 90f, 11);
            BrokenBoundary("WallSouth", new Vector3(0f, 1.15f, -24.3f), 57f, 6.4f, 0.55f, 0f, 23);
            BrokenBoundary("WallEast", new Vector3(28.3f, 1.15f, -6f), 37f, 6.4f, 0.55f, 90f, 37);
            // East perimeter container wall (3-high) for a "full" skyline
            for (int i = 0; i < 4; i++)
                for (int lvl = 0; lvl < 3; lvl++)
                    Container(b, $"EastWall{i}_{lvl}", new Vector3(30.5f, lvl * 2.59f, -18f + i * 6.4f), 90f, i + lvl);
            // Invisible clip walls (block soldiers, not bullets)
            var clip = Child(b, "PlayerClip");
            void Clip(Vector3 c, Vector3 s)
            {
                var go = new GameObject("Clip");
                go.transform.SetParent(clip, false);
                go.transform.localPosition = c;
                go.layer = LR2Layers.PlayerClip;
                go.AddComponent<BoxCollider>().size = s;
            }
            Clip(new Vector3(-27.9f, 6f, 0f), new Vector3(0.4f, 12f, 52f));
            Clip(new Vector3(27.9f, 6f, 0f), new Vector3(0.4f, 12f, 52f));
            Clip(new Vector3(0f, 6f, -23.9f), new Vector3(57f, 12f, 0.4f));
            Clip(new Vector3(0f, 6f, 23.4f), new Vector3(57f, 12f, 0.4f));
            Clip(new Vector3(0f, 12.2f, 0f), new Vector3(57f, 0.4f, 52f)); // ceiling: no climbing out
            var ceiling = clip.GetChild(clip.childCount - 1).gameObject;
            var mod = ceiling.AddComponent<NavMeshModifier>();
            mod.ignoreFromBuild = true; // never bake walkable area on top of the ceiling
        }

        private static void BuildBackground()
        {
            var bg = _background;
            int found = 0;
            void P(string n, Vector3 p, float yaw, float s = 1f) { if (Prop(n, bg, p.y == 0f ? G(p) : p, yaw, s, false) != null) found++; }
            P("SM_Silo", new Vector3(-12f, 0f, -36f), 0f, 1.2f);
            P("SM_Slio1", new Vector3(-2f, 0f, -38f), 30f, 1.2f);
            P("SM_Slio2", new Vector3(9f, 0f, -35f), 60f, 1.2f);
            P("SM_CylenderBuilding", new Vector3(22f, 0f, -36f), 0f, 1f);
            P("SM_Crane_01", new Vector3(-20f, 0f, 30f), 90f, 1f);
            P("SM_Crane_02", new Vector3(16f, 0f, 31f), -90f, 1f);
            P("SM_MainBuilding1", new Vector3(-46f, 0f, -12f), 90f, 1f);
            P("SM_MainBuilding3", new Vector3(-48f, 0f, 14f), 90f, 1f);
            P("SM_MainBuilding5", new Vector3(46f, 0f, -22f), -90f, 1f);
            P("SM_MainBuilding7", new Vector3(50f, 0f, 12f), -90f, 1f);
            P("SM_Boat", new Vector3(2f, -1.3f, 44f), 75f, 1f);
            P("SM_Ship_Part_01", new Vector3(-30f, -1.3f, 58f), 10f, 1f);
            // Procedural silhouettes behind the walls (always present)
            Box("WarehouseFar", bg, G(new Vector3(-6f, 0f, -46f), 5f), new Vector3(40f, 12f, 14f), _m.corrugated);
            Box("WarehouseWest", bg, G(new Vector3(-40f, 0f, -2f), 4f), new Vector3(14f, 10f, 30f), _m.corrugated);
            Box("WarehouseEast", bg, G(new Vector3(42f, 0f, -6f), 6f), new Vector3(16f, 14f, 34f), _m.wallPlaster);
            Debug.Log($"[LR2] Background: {found} Leartes set pieces placed.");
        }

        private static void PlaceDecals()
        {
            string[] names = { "M_CrackDecal", "M_CrackDecal_2", "M_LeakDecal", "M_RustDecal", "M_GraffittiDecal", "M_Decal_Puddle_A_MIC", "M_Decal_Salt", "M_LeakDecal_2", "M_RustDecal_2" };
            int placed = PlaceProjectedDecals(PlayMin, PlayMax, 220, names);
            Debug.Log($"[LR2] Decals placed: {placed}");
        }

        private static int PlaceProjectedDecals(Vector2 playMin, Vector2 playMax, int tries, string[] matNames)
        {
            var mats = new List<Material>();
            foreach (var n in matNames)
            {
                var m = FindLeartes(n);
                if (m != null) mats.Add(m);
            }
            if (mats.Count == 0) return 0;
            var placedPoints = new List<Vector3>();
            int placed = 0;
            for (int i = 0; i < tries; i++)
            {
                Vector3 o = new Vector3(R(playMin.x, playMax.x), 11f, R(playMin.y, playMax.y));
                Vector3 dir = i % 3 == 0 ? Vector3.down : new Vector3(R(-1f, 1f), -0.15f, R(-1f, 1f)).normalized;
                if (i % 3 != 0) o.y = R(0.6f, 3.6f);
                if (!Physics.Raycast(o, dir, out var hit, 28f, LR2Layers.EnvironmentMask, QueryTriggerInteraction.Ignore)) continue;
                var mat = mats[i % mats.Count];
                bool floorDecal = hit.normal.y > 0.7f;
                if ((mat.name.Contains("Puddle") || mat.name.Contains("Salt")) && !floorDecal) continue;
                if (hit.collider != null && hit.collider.GetComponentInParent<DestructibleProp>() != null) continue;
                // Keep decals spaced apart so overlapping projectors never shimmer or Z-fight
                bool tooClose = false;
                for (int j = 0; j < placedPoints.Count; j++)
                    if ((placedPoints[j] - hit.point).sqrMagnitude < 1.6f * 1.6f) { tooClose = true; break; }
                if (tooClose) continue;
                var dp = CreateDecal(mat, hit.point, hit.normal, floorDecal ? R(1.5f, 3.2f) : R(0.8f, 2.0f), floorDecal);
                if (dp != null) { placedPoints.Add(hit.point); placed++; }
            }
            return placed;
        }

        private static DecalProjector CreateDecal(Material mat, Vector3 point, Vector3 normal, float sizeXY, bool floorDecal)
        {
            var go = new GameObject("Decal_" + mat.name);
            go.transform.SetParent(_decals, false);
            go.transform.position = point + normal * 0.02f;
            go.transform.rotation = Quaternion.LookRotation(-normal, floorDecal ? Vector3.forward : Vector3.up) * Quaternion.Euler(0f, 0f, R(0f, 360f));
            var dp = go.AddComponent<DecalProjector>();
            dp.material = mat;
            dp.size = new Vector3(sizeXY, sizeXY, 0.20f); // slim depth prevents bleed-through on thin walls
            dp.startAngleFade = 65f;
            dp.endAngleFade = 95f;
            dp.drawDistance = 75f;
            return dp;
        }

        private static void DressProps()
        {
            // called from BuildGameplay after structures exist (needs colliders for spacing checks)
        }

        private static void MarkStatic(Transform t)
        {
            var flags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic |
                        StaticEditorFlags.ReflectionProbeStatic | StaticEditorFlags.ContributeGI;
            foreach (var r in t.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(r.gameObject, flags);
        }

        // ================================================================================================= lighting
        private static void BuildLighting()
        {
            // Sun
            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(_lighting, false);
            sunGo.transform.rotation = Quaternion.Euler(_cfg.sunElevation, _cfg.sunAzimuth, 0f);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.lightUnit = LightUnit.Lux;
            sun.intensity = _cfg.sunLux;
            sun.useColorTemperature = true;
            sun.colorTemperature = _cfg.sunTemperature;
            sun.color = Color.white;
            sun.shadows = LightShadows.Soft;
            var hdSun = sunGo.AddComponent<HDAdditionalLightData>();
            hdSun.angularDiameter = _cfg.sunAngularDiameter;
            hdSun.SetShadowResolutionOverride(true);
            hdSun.SetShadowResolution(4096);
            RenderSettings.sun = sun;

            // Global volume (photoreal grade + atmosphere)
            var profile = NewProfile(LightDir + "/LR2_Saltworks_Profile.asset");
            var ve = profile.Add<VisualEnvironment>(true);
            ve.skyType.Override((int)SkyType.PhysicallyBased);
            ve.skyAmbientMode.Override(SkyAmbientMode.Dynamic);
            var sky = profile.Add<PhysicallyBasedSky>(true);
            sky.aerosolDensity.Override(0.22f);
            var fog = profile.Add<Fog>(true);
            fog.enabled.Override(true);
            fog.meanFreePath.Override(_cfg.fogMeanFreePath);
            fog.baseHeight.Override(_cfg.fogBaseHeight);
            fog.maximumHeight.Override(_cfg.fogMaxHeight);
            fog.enableVolumetricFog.Override(true);
            fog.albedo.Override(_cfg.fogAlbedo);
            fog.anisotropy.Override(_cfg.fogAnisotropy);
            fog.depthExtent.Override(_cfg.fogDepthExtent);
            fog.maxFogDistance.Override(3000f);
            var exposure = profile.Add<Exposure>(true);
            exposure.mode.Override(ExposureMode.AutomaticHistogram);
            exposure.compensation.Override(_cfg.exposureCompensation);
            exposure.limitMin.Override(7.8f);
            exposure.limitMax.Override(12.8f);
            exposure.adaptationMode.Override(AdaptationMode.Progressive);
            exposure.adaptationSpeedDarkToLight.Override(2.6f);
            exposure.adaptationSpeedLightToDark.Override(1.5f);
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(Mathf.Min(_cfg.bloom, 0.16f));
            bloom.threshold.Override(1.1f);
            bloom.scatter.Override(0.6f);
            var color = profile.Add<ColorAdjustments>(true);
            color.contrast.Override(_cfg.contrast);
            color.saturation.Override(_cfg.saturation);
            var wb = profile.Add<WhiteBalance>(true);
            wb.temperature.Override(_cfg.temperature);
            var smh = profile.Add<ShadowsMidtonesHighlights>(true);
            smh.shadows.Override(new Vector4(0.92f, 0.98f, 1.05f, -0.06f));
            smh.highlights.Override(new Vector4(1.04f, 1f, 0.95f, 0.04f));
            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(_cfg.vignette);
            vig.smoothness.Override(0.45f);
            var ao = profile.Add<ScreenSpaceAmbientOcclusion>(true);
            ao.intensity.Override(0.65f);
            ao.radius.Override(1.5f);
            ao.directLightingStrength.Override(0.2f);
            var ssr = profile.Add<ScreenSpaceReflection>(true);
            ssr.enabled.Override(false); // half-res SSR shimmered on wet/rough surfaces; sky/probe reflections stay stable
            var gi = profile.Add<GlobalIllumination>(true);
            gi.enable.Override(false); // SSGI caused noisy flicker on foliage
            var contact = profile.Add<ContactShadows>(true);
            contact.enable.Override(true);
            contact.length.Override(0.18f);
            var micro = profile.Add<MicroShadowing>(true);
            micro.enable.Override(true);
            var shadows = profile.Add<HDShadowSettings>(true);
            shadows.maxShadowDistance.Override(90f);
            shadows.cascadeShadowSplitCount.Override(4);
            DisableMotionBlur(profile);
            SaveProfile(profile);

            var vol = new GameObject("GlobalVolume");
            vol.transform.SetParent(_lighting, false);
            var v = vol.AddComponent<Volume>();
            v.isGlobal = true;
            v.priority = 0f;
            v.sharedProfile = profile;

            // Bodycam lens volume (player-toggleable)
            var bodycam = NewProfile(LightDir + "/LR2_Bodycam_Profile.asset");
            var ld = bodycam.Add<LensDistortion>(true);
            ld.intensity.Override(_cfg.lensDistortion);
            ld.scale.Override(1.06f);
            var ca = bodycam.Add<ChromaticAberration>(true);
            ca.intensity.Override(_cfg.chromaticAberration);
            var grain = bodycam.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Medium3);
            grain.intensity.Override(_cfg.filmGrain);
            grain.response.Override(0.75f);
            DisableMotionBlur(bodycam);
            var bvig = bodycam.Add<Vignette>(true);
            bvig.intensity.Override(Mathf.Clamp01(_cfg.vignette + 0.12f));
            bvig.smoothness.Override(0.42f);
            SaveProfile(bodycam);
            var bvGo = new GameObject("BodycamLensVolume");
            bvGo.transform.SetParent(_lighting, false);
            var bv = bvGo.AddComponent<Volume>();
            bv.isGlobal = true;
            bv.priority = 10f;
            bv.sharedProfile = bodycam;
            bvGo.AddComponent<BodycamPostFX>();

            // Warm subtle indoor emergency point lights (keeps sheltered rooms readable without decal flicker)
            AddLight(_lighting, "EmergencyHouse", new Vector3(0f, 2.9f, 2f), Quaternion.identity, LightType.Point, 3200f, 2200f, 11f, 0f, false);
            AddLight(_lighting, "EmergencyWorkshopDown", new Vector3(-21f, 2.9f, -2f), Quaternion.identity, LightType.Point, 3200f, 2400f, 12f, 0f, false);
            AddLight(_lighting, "EmergencyWorkshopUp", new Vector3(-21f, 6.2f, -2f), Quaternion.identity, LightType.Point, 3200f, 1800f, 10f, 0f, false);
            AddLight(_lighting, "EmergencyWarehouseW", new Vector3(-7f, 5.6f, -17f), Quaternion.identity, LightType.Point, 3300f, 2800f, 15f, 0f, false);
            AddLight(_lighting, "EmergencyWarehouseE", new Vector3(7f, 5.6f, -17f), Quaternion.identity, LightType.Point, 3300f, 2800f, 15f, 0f, false);

            if (!_cfg.practicalLights) return; // abandoned for a century: no power anywhere
            // Practical lights: sodium lamp poles + warehouse/house interiors
            Vector3[] poles = { new Vector3(-12f, 0f, 2f), new Vector3(10f, 0f, -6f), new Vector3(-3f, 0f, 14f), new Vector3(16f, 0f, 12f), new Vector3(-20f, 0f, -14f), new Vector3(24f, 0f, -16f) };
            for (int i = 0; i < poles.Length; i++)
            {
                var pole = Child(_lighting, "LampPole" + i);
                pole.position = G(poles[i]);
                Box("Pole", pole, new Vector3(0f, 4f, 0f), new Vector3(0.18f, 8f, 0.18f), _m.darkMetal, LR2Layers.Default, SurfaceType.Metal);
                Box("Arm", pole, new Vector3(0.6f, 7.9f, 0f), new Vector3(1.3f, 0.12f, 0.25f), _m.darkMetal, LR2Layers.Default, SurfaceType.Metal);
                AddLight(pole, "Sodium", new Vector3(1.2f, 7.75f, 0f), Quaternion.Euler(90f, 0f, 0f), LightType.Spot, 2100f, 9000f, 22f, 115f, i < 2);
            }
            Vector3[] whLights = { new Vector3(-9f, 7.0f, -17f), new Vector3(-3f, 7.0f, -17f), new Vector3(3f, 7.0f, -17f), new Vector3(9f, 7.0f, -17f) };
            for (int i = 0; i < whLights.Length; i++)
            {
                var l = AddLight(_lighting, "WarehouseLamp" + i, whLights[i], Quaternion.Euler(90f, 0f, 0f), LightType.Spot, 4200f, 7000f, 14f, 120f, false);
                if (i == 2) l.gameObject.AddComponent<LightFlicker>();
            }
            AddLight(_lighting, "HouseLamp", new Vector3(0f, 3.2f, 2f), Quaternion.Euler(90f, 0f, 0f), LightType.Spot, 3600f, 2600f, 8f, 120f, false);
            AddLight(_lighting, "WorkshopLamp", new Vector3(-21f, 3.2f, -2f), Quaternion.Euler(90f, 0f, 0f), LightType.Spot, 3400f, 3000f, 9f, 120f, false);
            AddLight(_lighting, "WorkshopUpLamp", new Vector3(-21f, 6.7f, -2f), Quaternion.Euler(90f, 0f, 0f), LightType.Spot, 3400f, 2200f, 8f, 120f, false);
        }

        private static Light AddLight(Transform parent, string name, Vector3 pos, Quaternion rot, LightType type, float kelvin, float lumen, float range, float angle, bool shadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            var l = go.AddComponent<Light>();
            l.type = type;
            l.useColorTemperature = true;
            l.colorTemperature = kelvin;
            l.lightUnit = LightUnit.Lumen;
            l.intensity = lumen;
            l.range = range;
            if (type == LightType.Spot)
            {
                l.spotAngle = angle;
                l.innerSpotAngle = angle * 0.55f;
            }
            l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            go.AddComponent<HDAdditionalLightData>();
            return l;
        }

        private static VolumeProfile NewProfile(string path)
        {
            EnsureFolder(Path.GetDirectoryName(path));
            var existing = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (existing != null) AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
            return profile;
        }

        private static void DisableMotionBlur(VolumeProfile profile)
        {
            var mb = profile.Add<MotionBlur>(true);
            mb.intensity.Override(0f);
            mb.maximumVelocity.Override(0f);
            mb.cameraMotionBlur.Override(false);
            if (!profile.name.Contains("Menu"))
            {
                var dof = profile.TryGet<DepthOfField>(out var existingDof) ? existingDof : profile.Add<DepthOfField>(true);
                dof.focusMode.Override(DepthOfFieldMode.Off);
            }
        }

        private static void SaveProfile(VolumeProfile profile)
        {
            if (profile == null) return;
            foreach (var comp in profile.components)
            {
                if (comp == null) continue;
                comp.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                if (!AssetDatabase.Contains(comp)) AssetDatabase.AddObjectToAsset(comp, profile);
                EditorUtility.SetDirty(comp);
            }
            EditorUtility.SetDirty(profile);
        }

        private static void AddSpawn(Vector3 pos, float yaw, TeamId team, bool drones = true, bool snapToGround = true)
        {
            var go = new GameObject($"Spawn_{team}");
            go.transform.SetParent(_spawns, false);
            go.transform.localPosition = snapToGround ? Stand(pos) : pos;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            var sp = go.AddComponent<SpawnPoint>();
            sp.team = team;
            sp.allowFreeForAll = true;
            sp.allowDrones = drones;
        }

        private static void AddTac(Vector3 pos, TacticalPointType type, float weight, bool snapLowToGround = true)
        {
            var go = new GameObject($"Tac_{type}");
            go.transform.SetParent(_tactical, false);
            go.transform.localPosition = snapLowToGround && pos.y < 0.1f ? Stand(pos) : pos;
            var tp = go.AddComponent<TacticalPoint>();
            tp.type = type;
            tp.weight = weight;
        }

        private static void AddAmmoCrate(Vector3 pos)
        {
            var crate = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LR2/Prefabs/Props/LR2_AmmoCrate.prefab");
            if (crate == null) return;
            var c = (GameObject)PrefabUtility.InstantiatePrefab(crate, _gameplay);
            c.transform.position = pos;
        }

        private static void AddPlayerClipBox(Vector2 playMin, Vector2 playMax, float height = 12f)
        {
            var clip = Child(_structures, "PlayerClip");
            void Clip(Vector3 c, Vector3 s, bool noNav = false)
            {
                var go = new GameObject("Clip");
                go.transform.SetParent(clip, false);
                go.transform.localPosition = c;
                go.layer = LR2Layers.PlayerClip;
                go.AddComponent<BoxCollider>().size = s;
                if (noNav) go.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
            }
            float w = playMax.x - playMin.x;
            float d = playMax.y - playMin.y;
            float cx = (playMin.x + playMax.x) * 0.5f;
            float cz = (playMin.y + playMax.y) * 0.5f;
            Clip(new Vector3(playMin.x - 0.3f, height * 0.5f, cz), new Vector3(0.4f, height, d + 2f));
            Clip(new Vector3(playMax.x + 0.3f, height * 0.5f, cz), new Vector3(0.4f, height, d + 2f));
            Clip(new Vector3(cx, height * 0.5f, playMin.y - 0.3f), new Vector3(w + 2f, height, 0.4f));
            Clip(new Vector3(cx, height * 0.5f, playMax.y + 0.3f), new Vector3(w + 2f, height, 0.4f));
            Clip(new Vector3(cx, height + 0.2f, cz), new Vector3(w + 2f, 0.4f, d + 2f), true);
        }

        private static void AddMatchSystems(float reverbSize = 35f)
        {
            var mmGo = new GameObject("MatchManager");
            mmGo.transform.SetParent(_systems, false);
            var mm = mmGo.AddComponent<MatchManager>();
            SetField(mm, "defaultSettings", AssetDatabase.LoadAssetAtPath<MatchSettings>("Assets/LR2/Data/Game/MatchSettings_Duel.asset"));
            SetField(mm, "playerPrefab", AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LR2/Prefabs/Characters/LR2_Soldier_Player.prefab"));
            SetField(mm, "botPrefab", AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LR2/Prefabs/Characters/LR2_Soldier_Bot.prefab"));
            SetField(mm, "orbPrefab", AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LR2/Prefabs/Enemies/LR2_OrbDrone.prefab"));
            var so = new SerializedObject(mm);
            var diffs = so.FindProperty("difficulties");
            string[] names = { "Recruit", "Regular", "Hardened", "Veteran" };
            diffs.arraySize = names.Length;
            for (int i = 0; i < names.Length; i++)
                diffs.GetArrayElementAtIndex(i).objectReferenceValue = AssetDatabase.LoadAssetAtPath<BotDifficulty>($"Assets/LR2/Data/AI/BotDifficulty_{names[i]}.asset");
            so.ApplyModifiedPropertiesWithoutUndo();

            var ui = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LR2/Prefabs/UI/LR2_GameUI.prefab");
            if (ui != null) PrefabUtility.InstantiatePrefab(ui, _systems);
            LR2UIBuilder.EnsureEventSystem();

            var audio = new GameObject("AudioEnvironment");
            audio.transform.SetParent(_systems, false);
            var env = audio.AddComponent<LR2AudioEnvironment>();
            var common = AssetDatabase.LoadAssetAtPath<LR2CommonSounds>("Assets/LR2/Resources/LR2/LR2CommonSounds.asset");
            if (common != null) env.ambienceLoops = common.ambienceLoops;
            Reverb(_systems, "Reverb_Arena", new Vector3(0f, 2f, 0f), reverbSize * 0.7f, reverbSize * 1.2f, AudioReverbPreset.City);

            var kz = new GameObject("KillZone");
            kz.transform.SetParent(_systems, false);
            kz.AddComponent<KillZone>();
        }

        // ================================================================================================= gameplay
        private static void BuildGameplay()
        {
            // Spawns
            void Spawn(Vector3 p, float yaw, TeamId team, bool drones = true) => AddSpawn(p, yaw, team, drones, true);
            Spawn(new Vector3(-22f, 0.05f, -5f), 90f, TeamId.Alpha);
            Spawn(new Vector3(-19f, 0.05f, 1.5f), 90f, TeamId.Alpha);
            Spawn(new Vector3(-24f, 0.05f, 2.5f), 120f, TeamId.Alpha);
            Spawn(new Vector3(-18.5f, 0.05f, -6.5f), 70f, TeamId.Alpha);
            Spawn(new Vector3(-24f, 0.05f, -12f), 45f, TeamId.Alpha);
            Spawn(new Vector3(-23.5f, 0.05f, 9f), 120f, TeamId.Alpha);
            Spawn(new Vector3(24f, 0.05f, -11f), -60f, TeamId.Bravo);
            Spawn(new Vector3(23f, 0.05f, 9.5f), -120f, TeamId.Bravo);
            Spawn(new Vector3(25f, 0.05f, -3f), -90f, TeamId.Bravo);
            Spawn(new Vector3(17.5f, 0.05f, -15.5f), -45f, TeamId.Bravo);
            Spawn(new Vector3(23.5f, 0.05f, 4f), -90f, TeamId.Bravo);
            Spawn(new Vector3(17f, 0.05f, 15f), -135f, TeamId.Bravo);
            Spawn(new Vector3(0f, 0.05f, 19.5f), 180f, TeamId.None);
            Spawn(new Vector3(-10f, 0.05f, -20f), 0f, TeamId.None);
            Spawn(new Vector3(10f, 0.05f, -20f), 0f, TeamId.None);
            Spawn(new Vector3(0f, 0.05f, -6f), 0f, TeamId.None, false);

            // Tactical points
            void Tac(Vector3 p, TacticalPointType type, float weight) => AddTac(p, type, weight, true);
            Tac(new Vector3(0.5f, 3.65f, 2.5f), TacticalPointType.Overlook, 0.9f);       // control house roof
            Tac(new Vector3(-17.6f, 3.65f, -2f), TacticalPointType.Overlook, 0.8f);      // workshop window
            Tac(new Vector3(20f, 5.2f, -4.8f), TacticalPointType.Overlook, 0.75f);       // container stack A
            Tac(new Vector3(20f, 5.2f, 5.2f), TacticalPointType.Overlook, 0.6f);         // container stack B
            Tac(new Vector3(-1f, 5.2f, 17f), TacticalPointType.Sniper, 0.7f);            // quay bridge
            Tac(new Vector3(0f, 0.05f, -17f), TacticalPointType.Patrol, 0.6f);           // warehouse interior
            Tac(new Vector3(-9f, 0.05f, -17.5f), TacticalPointType.Patrol, 0.5f);
            Tac(new Vector3(9f, 0.05f, -17.5f), TacticalPointType.Patrol, 0.5f);
            Tac(new Vector3(0f, 1.25f, -10.8f), TacticalPointType.Patrol, 0.55f);         // loading dock
            Tac(new Vector3(0f, 0.05f, 10f), TacticalPointType.Patrol, 0.5f);             // north yard
            Tac(new Vector3(-2f, 0.05f, 19.5f), TacticalPointType.Patrol, 0.5f);          // quay lane
            Tac(new Vector3(12f, 0.05f, 0f), TacticalPointType.Patrol, 0.5f);
            Tac(new Vector3(-12f, 0.05f, 4f), TacticalPointType.Patrol, 0.5f);
            Tac(new Vector3(-4.9f, 0.05f, 2f), TacticalPointType.Choke, 0.6f);            // house doors
            Tac(new Vector3(4.9f, 0.05f, 2f), TacticalPointType.Choke, 0.6f);
            Tac(new Vector3(-7f, 0.05f, -11.6f), TacticalPointType.Choke, 0.5f);          // warehouse openings
            Tac(new Vector3(5f, 0.05f, -11.6f), TacticalPointType.Choke, 0.5f);

            // Props from the Leartes kit (cover + set dressing)
            DressYard();

            // Ammo crates
            var crate = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LR2/Prefabs/Props/LR2_AmmoCrate.prefab");
            if (crate != null)
            {
                foreach (var p in new[] { new Vector3(-1.5f, 0f, -8.6f), new Vector3(-3f, 0f, 12.5f) })
                {
                    var c = (GameObject)PrefabUtility.InstantiatePrefab(crate, _gameplay);
                    c.transform.position = G(p);
                }
            }

            // Systems
            var mmGo = new GameObject("MatchManager");
            mmGo.transform.SetParent(_systems, false);
            var mm = mmGo.AddComponent<MatchManager>();
            SetField(mm, "defaultSettings", AssetDatabase.LoadAssetAtPath<MatchSettings>("Assets/LR2/Data/Game/MatchSettings_Duel.asset"));
            SetField(mm, "playerPrefab", AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LR2/Prefabs/Characters/LR2_Soldier_Player.prefab"));
            SetField(mm, "botPrefab", AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LR2/Prefabs/Characters/LR2_Soldier_Bot.prefab"));
            SetField(mm, "orbPrefab", AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LR2/Prefabs/Enemies/LR2_OrbDrone.prefab"));
            var so = new SerializedObject(mm);
            var diffs = so.FindProperty("difficulties");
            string[] names = { "Recruit", "Regular", "Hardened", "Veteran" };
            diffs.arraySize = names.Length;
            for (int i = 0; i < names.Length; i++)
                diffs.GetArrayElementAtIndex(i).objectReferenceValue = AssetDatabase.LoadAssetAtPath<BotDifficulty>($"Assets/LR2/Data/AI/BotDifficulty_{names[i]}.asset");
            so.ApplyModifiedPropertiesWithoutUndo();

            var ui = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LR2/Prefabs/UI/LR2_GameUI.prefab");
            if (ui != null) PrefabUtility.InstantiatePrefab(ui, _systems);
            LR2UIBuilder.EnsureEventSystem();

            var audio = new GameObject("AudioEnvironment");
            audio.transform.SetParent(_systems, false);
            var env = audio.AddComponent<LR2AudioEnvironment>();
            var common = AssetDatabase.LoadAssetAtPath<LR2CommonSounds>("Assets/LR2/Resources/LR2/LR2CommonSounds.asset");
            if (common != null) env.ambienceLoops = common.ambienceLoops;
            Reverb(_systems, "Reverb_Warehouse", new Vector3(0f, 3f, -17f), 9f, 15f, AudioReverbPreset.Hangar);
            Reverb(_systems, "Reverb_House", new Vector3(0f, 1.7f, 2f), 3.5f, 5.5f, AudioReverbPreset.Room);
            Reverb(_systems, "Reverb_Workshop", new Vector3(-21f, 3f, -2f), 5f, 8f, AudioReverbPreset.Livingroom);
            Reverb(_systems, "Reverb_Yard", new Vector3(0f, 2f, 0f), 30f, 45f, AudioReverbPreset.City);

            var kz = new GameObject("KillZone");
            kz.transform.SetParent(_systems, false);
            kz.AddComponent<KillZone>();
        }

        private static void Reverb(Transform parent, string name, Vector3 pos, float min, float max, AudioReverbPreset preset)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var rz = go.AddComponent<AudioReverbZone>();
            rz.minDistance = min;
            rz.maxDistance = max;
            rz.reverbPreset = preset;
        }

        private static void DressYard()
        {
            int placed = 0;
            void P(string n, Vector3 p, float yaw = 0f, float s = 1f)
            {
                if (_rnd.NextDouble() > _cfg.propDensity + 0.1f) return;
                if (Prop(n, _props, p.y == 0f ? G(p) : p, yaw + R(-12f, 12f), s) != null) placed++;
            }
            // Barrel clusters
            foreach (var c in new[] { new Vector3(-13f, 0f, -9f), new Vector3(12f, 0f, 9.5f), new Vector3(-6f, 0f, 21.5f), new Vector3(7f, 0f, -22.8f) + new Vector3(0f, 0f, 1.5f), new Vector3(23f, 0f, 13f) })
            {
                P("SM_Barrel_01", c, R(0, 360));
                P("SM_Barrel_02", c + new Vector3(0.8f, 0f, 0.3f), R(0, 360));
                P("SM_Barrel_04", c + new Vector3(0.2f, 0f, 0.95f), R(0, 360));
            }
            // Pallet stacks + crates
            foreach (var c in new[] { new Vector3(-10f, 0f, 10f), new Vector3(8f, 0f, -8.6f), new Vector3(-17f, 0f, -16f), new Vector3(11f, 0f, 16f), new Vector3(-2f, 1.2f, -10.8f) })
            {
                P("SM_Woodpallet_1", c, R(0, 90));
                P("SM_Crate_01", c + new Vector3(1.4f, 0f, 0.2f), R(0, 90));
                P("SM_Crate_02", c + new Vector3(-1.2f, 0f, -0.4f), R(0, 90));
            }
            // Jersey barriers (mantle-height cover)
            foreach (var (p, yaw) in new[] { (new Vector3(-6f, 0f, -2f), 90f), (new Vector3(6.5f, 0f, -1.5f), 90f), (new Vector3(-14f, 0f, 14f), 0f), (new Vector3(2f, 0f, 12f), 10f), (new Vector3(-1f, 0f, -24f + 1.5f), 0f), (new Vector3(14f, 0f, -9f), 80f) })
                P("SM_Jersey", p, yaw);
            // Industrial clutter
            P("SM_Cablespool", new Vector3(-4f, 0f, 8.5f), 0f);
            P("SM_Cablespool", new Vector3(17.5f, 0f, -8.5f), 40f);
            P("SM_Gascylender", new Vector3(-15.2f, 0f, -5.8f), 0f);
            P("SM_Tire", new Vector3(13f, 0f, 3f), 0f);
            P("SM_Tire", new Vector3(13.4f, 0f, 3.9f), 30f);
            P("SM_Trashcan", new Vector3(-5.2f, 0f, -1.2f), 0f);
            P("SM_Firehydrant", new Vector3(5.2f, 0f, 6.2f), 0f);
            P("SM_Conebarrier", new Vector3(3f, 0f, -9f), 0f);
            P("SM_Conebarrier", new Vector3(-8f, 0f, -9.3f), 0f);
            P("SM_fireextinguisher", new Vector3(-15.6f, 0f, 3.5f), 0f);
            P("SM_BigPipe", new Vector3(-24f, 0f, 15f), 0f);
            P("SM_PipeMadule1", new Vector3(26f, 0f, 16f), 90f);
            P("SM_Anchor", new Vector3(10f, 0f, 22f), 0f);
            P("SM_Plasticbarrel", new Vector3(-12.4f, 0f, -9.6f), 0f);
            Debug.Log($"[LR2] Props placed from Leartes kit: {placed}");
        }

        // ================================================================================================= mathematical anti-clipping
        private static void EliminateSceneClipping(string mapName, Transform structures, Transform props, Transform nature, Transform spawns, Transform gameplay)
        {
            Physics.SyncTransforms();

            // Collect immovable static obstacles (walls, containers, racks, crates, stairs, silos) — ignore terrain & flat ground/floor slabs.
            var staticObs = new List<(Collider col, Bounds bounds)>();
            if (structures != null)
            {
                foreach (var c in structures.GetComponentsInChildren<Collider>(true))
                {
                    if (c == null || c.isTrigger || c is TerrainCollider) continue;
                    var b = c.bounds;
                    if (b.size.y < 0.35f && (b.size.x > 6f || b.size.z > 6f)) continue; // floor / road / quay slab
                    staticObs.Add((c, b));
                }
            }

            // Collect keep-clear anchors (spawns, ammo crates, and tactical doorway chokes) so no prop/bush blocks movement.
            var keepClear = new List<(Vector3 pos, float pushRadius, float killRadius)>();
            if (spawns != null)
                foreach (var sp in spawns.GetComponentsInChildren<SpawnPoint>(true)) keepClear.Add((sp.transform.position, 1.7f, 1.35f));
            if (gameplay != null)
                foreach (var ac in gameplay.GetComponentsInChildren<AmmoCrate>(true)) keepClear.Add((ac.transform.position, 1.7f, 1.35f));
            var tactical = structures != null && structures.parent != null ? structures.parent.Find("Tactical") : null;
            if (tactical != null)
            {
                foreach (Transform t in tactical)
                    if (t.name.StartsWith("Choke")) keepClear.Add((t.position, 2.5f, 2.1f));
            }
            if (structures != null)
            {
                // Keep both ends of every staircase/ramp collider clear of props & foliage
                foreach (var c in structures.GetComponentsInChildren<BoxCollider>(true))
                {
                    if (c.name != "RampCollider") continue;
                    Vector3 fwd = c.transform.forward * (c.size.z * 0.45f);
                    keepClear.Add((c.transform.position - fwd, 1.8f, 1.4f));
                    keepClear.Add((c.transform.position + fwd, 1.8f, 1.4f));
                }
            }

            // Gather movable props & nature items (immediate children under each container group).
            var candidates = new List<Transform>();
            void GatherDirectChildren(Transform parent)
            {
                if (parent == null) return;
                for (int i = 0; i < parent.childCount; i++)
                {
                    var ch = parent.GetChild(i);
                    if (ch.GetComponent<ParticleSystem>() != null || ch.GetComponent<Light>() != null || ch.GetComponent<DecalProjector>() != null) continue;
                    candidates.Add(ch);
                }
            }
            GatherDirectChildren(props);
            if (nature != null)
            {
                GatherDirectChildren(nature.Find("Trees"));
                GatherDirectChildren(nature.Find("Undergrowth"));
                GatherDirectChildren(nature.Find("Overgrowth"));
            }

            // Static structure lookup so rays hit structures/terrain, not movable props.
            var propColliderSet = new HashSet<Collider>();
            foreach (var tr in candidates)
                foreach (var c in tr.GetComponentsInChildren<Collider>(true)) propColliderSet.Add(c);

            bool TryBounds(Transform tr, out Bounds bounds)
            {
                bounds = default;
                var rs = tr.GetComponentsInChildren<Renderer>(true);
                bool has = false;
                foreach (var r in rs)
                {
                    if (r is ParticleSystemRenderer) continue;
                    if (!has) { bounds = r.bounds; has = true; }
                    else bounds.Encapsulate(r.bounds);
                }
                return has;
            }

            bool RaycastStaticFloor(Vector3 origin, float maxDist, out RaycastHit bestHit)
            {
                bestHit = default;
                var hits = Physics.RaycastAll(origin, Vector3.down, maxDist, LR2Layers.EnvironmentMask, QueryTriggerInteraction.Ignore);
                float bestDist = float.MaxValue;
                bool found = false;
                for (int i = 0; i < hits.Length; i++)
                {
                    var h = hits[i];
                    if (propColliderSet.Contains(h.collider)) continue;
                    if (h.normal.y < 0.55f) continue;
                    if (h.distance < bestDist)
                    {
                        bestDist = h.distance;
                        bestHit = h;
                        found = true;
                    }
                }
                return found;
            }

            void SnapToSurface(Transform tr)
            {
                if (!TryBounds(tr, out var b)) return;
                Vector3 probe = new Vector3(b.center.x, b.min.y + Mathf.Min(1.2f, b.size.y * 0.6f + 0.35f), b.center.z);
                if (RaycastStaticFloor(probe, 3.0f, out var hit))
                    tr.position += Vector3.up * (hit.point.y - b.min.y - 0.01f);
            }

            int adjusted = 0, removed = 0;

            // Pass 1: Snap props to static ground/floor and reject large props overhanging steep ledges.
            for (int i = candidates.Count - 1; i >= 0; i--)
            {
                var tr = candidates[i];
                if (tr == null) { candidates.RemoveAt(i); continue; }
                // Wall-mounted ivy / roof moss / webs should keep their surface alignment; only ground props snap.
                bool isOvergrowth = nature != null && tr.parent != null && tr.parent.name == "Overgrowth";
                bool isFoliage = nature != null && tr.IsChildOf(nature);
                if (isOvergrowth) continue;

                SnapToSurface(tr);
                if (!TryBounds(tr, out var b)) continue;
                if (!isFoliage && Mathf.Max(b.size.x, b.size.z) > 1.1f)
                {
                    float minY = float.MaxValue, maxY = float.MinValue;
                    bool unsupported = false;
                    float[] cx = { b.min.x + 0.1f, b.max.x - 0.1f };
                    float[] cz = { b.min.z + 0.1f, b.max.z - 0.1f };
                    foreach (float x in cx)
                        foreach (float z in cz)
                        {
                            if (!RaycastStaticFloor(new Vector3(x, b.min.y + 0.6f, z), 1.6f, out var ch)) { unsupported = true; break; }
                            minY = Mathf.Min(minY, ch.point.y);
                            maxY = Mathf.Max(maxY, ch.point.y);
                        }
                    if (unsupported || maxY - minY > 0.55f)
                    {
                        Object.DestroyImmediate(tr.gameObject);
                        candidates.RemoveAt(i);
                        removed++;
                        continue;
                    }
                }
            }

            // Helper: compute horizontal Minimum Translation Vector to push box A out of box B.
            bool TryBoxMtv(Bounds a, Bounds b, float margin, out Vector3 mtv)
            {
                mtv = Vector3.zero;
                float ox = Mathf.Min(a.max.x, b.max.x) - Mathf.Max(a.min.x, b.min.x) - margin;
                float oy = Mathf.Min(a.max.y, b.max.y) - Mathf.Max(a.min.y, b.min.y) - margin;
                float oz = Mathf.Min(a.max.z, b.max.z) - Mathf.Max(a.min.z, b.min.z) - margin;
                if (ox <= 0f || oy <= 0f || oz <= 0f) return false;
                if (ox <= oz) mtv = new Vector3(a.center.x >= b.center.x ? ox : -ox, 0f, 0f);
                else mtv = new Vector3(0f, 0f, a.center.z >= b.center.z ? oz : -oz);
                return true;
            }

            // Pass 2: 4-iteration mathematical MTV separation against spawns, static walls/containers, and peer props.
            for (int iter = 0; iter < 4; iter++)
            {
                Physics.SyncTransforms();
                for (int i = 0; i < candidates.Count; i++)
                {
                    var tr = candidates[i];
                    if (tr == null) continue;
                    bool isOvergrowth = nature != null && tr.parent != null && tr.parent.name == "Overgrowth";
                    bool isFoliage = nature != null && tr.IsChildOf(nature);
                    if (isOvergrowth || !TryBounds(tr, out var bi)) continue;

                    Vector3 totalPush = Vector3.zero;

                    // Keep spawns, ammo crates & doorway chokes unobstructed
                    for (int k = 0; k < keepClear.Count; k++)
                    {
                        var (kcPos, pushR, _) = keepClear[k];
                        if (Mathf.Abs(bi.min.y - kcPos.y) > 2.0f) continue;
                        Vector3 d = new Vector3(bi.center.x - kcPos.x, 0f, bi.center.z - kcPos.z);
                        float r = pushR + Mathf.Min(bi.extents.x, bi.extents.z);
                        float dist = d.magnitude;
                        if (dist < r)
                        {
                            Vector3 n = dist > 0.01f ? d / dist : Vector3.forward;
                            totalPush += n * (r - dist);
                        }
                    }

                    // Static structures (exact ComputePenetration when prop has a collider, else AABB MTV; keep in-play undergrowth clear of walls/props)
                    bool inPlayUndergrowth = isFoliage && tr.parent != null && tr.parent.name == "Undergrowth" && InPlay(bi.center, 3f);
                    if (!isFoliage || inPlayUndergrowth)
                    {
                        var itemCol = tr.GetComponentInChildren<Collider>(true);
                        for (int s = 0; s < staticObs.Count; s++)
                        {
                            var (sc, sb) = staticObs[s];
                            if (sc == null || !TryBoxMtv(bi, sb, 0.05f, out var aabbMtv)) continue;
                            if (!isFoliage && itemCol != null && !itemCol.isTrigger)
                            {
                                if (Physics.ComputePenetration(itemCol, itemCol.transform.position, itemCol.transform.rotation,
                                        sc, sc.transform.position, sc.transform.rotation, out var dir, out float depth) && depth > 0.03f)
                                {
                                    dir.y = 0f;
                                    if (dir.sqrMagnitude > 0.001f) totalPush += dir.normalized * (depth + 0.04f);
                                    else totalPush += aabbMtv;
                                }
                            }
                            else
                            {
                                totalPush += aabbMtv;
                            }
                        }

                        if (!isFoliage)
                        {
                            // Peer props (skip non-colliding grass; separate colliding props by MTV)
                            for (int j = 0; j < candidates.Count; j++)
                            {
                                if (i == j || candidates[j] == null) continue;
                                if (nature != null && candidates[j].IsChildOf(nature) && candidates[j].GetComponentInChildren<Collider>(true) == null) continue;
                                if (!TryBounds(candidates[j], out var bj)) continue;
                                if (TryBoxMtv(bi, bj, 0.06f, out var pmv))
                                {
                                    if (pmv.sqrMagnitude > 0.002f) totalPush += pmv * 0.55f;
                                }
                            }
                        }
                    }

                    if (totalPush.sqrMagnitude > 0.0004f)
                    {
                        totalPush.y = 0f;
                        if (totalPush.magnitude > 2.5f) totalPush = totalPush.normalized * 2.5f;
                        tr.position += totalPush;
                        SnapToSurface(tr);
                        adjusted++;
                    }
                }
            }

            // Pass 3: Remove any non-tree prop that still penetrates a wall/container or blocks a spawn/crate.
            Physics.SyncTransforms();
            for (int i = candidates.Count - 1; i >= 0; i--)
            {
                var tr = candidates[i];
                if (tr == null) continue;
                bool isOvergrowth = nature != null && tr.parent != null && tr.parent.name == "Overgrowth";
                if (isOvergrowth || !TryBounds(tr, out var bi)) continue;
                bool keepTree = nature != null && tr.parent != null && tr.parent.name == "Trees";

                bool bad = false;
                for (int k = 0; k < keepClear.Count && !bad; k++)
                {
                    var (kcPos, _, killR) = keepClear[k];
                    if (Mathf.Abs(bi.min.y - kcPos.y) > 2.0f) continue;
                    float dist = Vector2.Distance(new Vector2(bi.center.x, bi.center.z), new Vector2(kcPos.x, kcPos.z));
                    if (dist < killR) bad = true;
                }
                if (!bad && !keepTree)
                {
                    var itemCol = tr.GetComponentInChildren<Collider>(true);
                    if (itemCol != null && !itemCol.isTrigger)
                    {
                        for (int s = 0; s < staticObs.Count && !bad; s++)
                        {
                            var (sc, sb) = staticObs[s];
                            if (sc == null || !bi.Intersects(sb)) continue;
                            if (Physics.ComputePenetration(itemCol, itemCol.transform.position, itemCol.transform.rotation,
                                    sc, sc.transform.position, sc.transform.rotation, out _, out float depth) && depth > 0.09f)
                                bad = true;
                        }
                    }
                    else if (nature != null && tr.parent != null && tr.parent.name == "Undergrowth" && InPlay(bi.center, 3f))
                    {
                        for (int s = 0; s < staticObs.Count && !bad; s++)
                        {
                            var (sc, sb) = staticObs[s];
                            if (sc != null && TryBoxMtv(bi, sb, -0.05f, out _)) bad = true;
                        }
                    }
                }
                if (bad)
                {
                    Object.DestroyImmediate(tr.gameObject);
                    removed++;
                }
            }

            Physics.SyncTransforms();
            Debug.Log($"[LR2] Anti-clip ({mapName}): adjusted={adjusted}, removed={removed} overlapping props.");
        }

        // ================================================================================================= destructible objects
        private static void SetupDestructibleProps(GameObject root)
        {
            var explosionFx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LR2/Prefabs/FX/FX_Explosion_Small.prefab");
            var candidates = new List<GameObject>();

            if (_props != null)
            {
                for (int i = 0; i < _props.childCount; i++)
                {
                    var ch = _props.GetChild(i).gameObject;
                    if (ch.GetComponent<Collider>() != null || ch.GetComponentInChildren<Collider>() != null)
                        candidates.Add(ch);
                }
            }

            if (_structures != null)
            {
                string[] breakableNames = { "Glass", "Desk", "Lockers", "Crate", "Goods", "Workbench", "ToolRack", "WindowCover", "ACUnit", "Vent", "ConcreteBlock", "Bollard", "Barricade", "Sandbag" };
                foreach (var t in _structures.GetComponentsInChildren<Transform>(true))
                {
                    if (t == _structures) continue;
                    bool match = false;
                    foreach (var bn in breakableNames)
                        if (t.name.IndexOf(bn, System.StringComparison.OrdinalIgnoreCase) >= 0) { match = true; break; }
                    if (match && (t.GetComponent<Collider>() != null || t.GetComponentInChildren<Collider>() != null))
                        candidates.Add(t.gameObject);
                }
            }

            int count = 0, explosives = 0;
            var seen = new HashSet<GameObject>();
            foreach (var go in candidates)
            {
                if (go == null || !seen.Add(go)) continue;
                if (go.GetComponent<SpawnPoint>() != null || go.GetComponent<AmmoCrate>() != null || go.GetComponentInParent<DestructibleProp>() != null) continue;

                // Ensure transform isn't frozen into a static batch mesh so hit jolt & shatter work cleanly.
                foreach (var tr in go.GetComponentsInChildren<Transform>(true))
                    GameObjectUtility.SetStaticEditorFlags(tr.gameObject, 0);

                var dp = go.GetComponent<DestructibleProp>();
                if (dp == null) dp = go.AddComponent<DestructibleProp>();
                string lower = go.name.ToLowerInvariant();
                if (lower.Contains("glass") || lower.Contains("window"))
                {
                    dp.maxHealth = 25f;
                    dp.surface = SurfaceType.Glass;
                }
                else if (lower.Contains("barrel_01") || lower.Contains("gascyl") || lower.Contains("fuel"))
                {
                    dp.maxHealth = 65f;
                    dp.surface = SurfaceType.Metal;
                    dp.explosive = true;
                    dp.blastRadius = 5.5f;
                    dp.blastDamage = 90f;
                    dp.explosionPrefab = explosionFx;
                    explosives++;
                }
                else if (lower.Contains("wood") || lower.Contains("crate") || lower.Contains("pallet") || lower.Contains("desk") ||
                         lower.Contains("workbench") || lower.Contains("table") || lower.Contains("chair") || lower.Contains("goods") || lower.Contains("log"))
                {
                    dp.maxHealth = 85f;
                    dp.surface = SurfaceType.Wood;
                }
                else if (lower.Contains("plastic") || lower.Contains("cone") || lower.Contains("tire") || lower.Contains("trash") || lower.Contains("extinguisher"))
                {
                    dp.maxHealth = 75f;
                    dp.surface = SurfaceType.Plastic;
                }
                else if (lower.Contains("barrel") || lower.Contains("locker") || lower.Contains("spool") || lower.Contains("hydrant") ||
                         lower.Contains("pipe") || lower.Contains("rack") || lower.Contains("acunit") || lower.Contains("vent") || lower.Contains("bollard") || lower.Contains("anchor"))
                {
                    dp.maxHealth = 120f;
                    dp.surface = SurfaceType.Metal;
                }
                else
                {
                    dp.maxHealth = 200f;
                    dp.surface = SurfaceType.Concrete;
                }

                if (go.GetComponent<SurfaceIdentifier>() == null)
                    go.AddComponent<SurfaceIdentifier>().surface = dp.surface;

                // Dynamic NavMesh carving on solid floor-level cover + intact glass panels (destruction opens paths in real time).
                var rs = go.GetComponentsInChildren<Renderer>(true);
                if (rs.Length > 0)
                {
                    Bounds localBounds = ComputeLocalBounds(go.transform, rs);
                    Vector3 worldSize = Vector3.Scale(localBounds.size, new Vector3(
                        Mathf.Abs(go.transform.lossyScale.x),
                        Mathf.Abs(go.transform.lossyScale.y),
                        Mathf.Abs(go.transform.lossyScale.z)));
                    bool glassPanel = dp.surface == SurfaceType.Glass && worldSize.y > 1.2f && Mathf.Max(worldSize.x, worldSize.z) > 1.2f;
                    bool solidCover = dp.surface != SurfaceType.Glass && worldSize.y > 0.35f && worldSize.magnitude < 8.0f;
                    if (glassPanel || solidCover)
                    {
                        var mod = go.GetComponent<NavMeshModifier>();
                        if (mod == null) mod = go.AddComponent<NavMeshModifier>();
                        mod.ignoreFromBuild = true;
                        var obs = go.GetComponent<NavMeshObstacle>();
                        if (obs == null) obs = go.AddComponent<NavMeshObstacle>();
                        obs.shape = NavMeshObstacleShape.Box;
                        obs.carving = true;
                        obs.carveOnlyStationary = true;
                        obs.center = localBounds.center;
                        obs.size = new Vector3(
                            Mathf.Max(0.2f, localBounds.size.x),
                            Mathf.Max(0.2f, localBounds.size.y),
                            Mathf.Max(0.2f, localBounds.size.z));
                    }
                }
                count++;
            }
            Debug.Log($"[LR2] Destructible objects configured: {count} ({explosives} explosive).");
        }

        private static Bounds ComputeLocalBounds(Transform root, Renderer[] rs)
        {
            Bounds local = default;
            bool has = false;
            Matrix4x4 inv = root.worldToLocalMatrix;
            foreach (var r in rs)
            {
                if (r is ParticleSystemRenderer) continue;
                var mf = r.GetComponent<MeshFilter>();
                Bounds src = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds : r.bounds;
                Matrix4x4 m = mf != null && mf.sharedMesh != null ? inv * r.transform.localToWorldMatrix : inv;
                Vector3 c = src.center, e = src.extents;
                for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Vector3 pt = m.MultiplyPoint3x4(c + new Vector3(e.x * sx, e.y * sy, e.z * sz));
                    if (!has) { local = new Bounds(pt, Vector3.zero); has = true; }
                    else local.Encapsulate(pt);
                }
            }
            return has ? local : new Bounds(Vector3.zero, Vector3.one);
        }

        // ================================================================================================= navigation / cover / minimap
        private static void ConfigureNavAgent()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/NavMeshAreas.asset");
            if (assets == null || assets.Length == 0) return;
            var so = new SerializedObject(assets[0]);
            void F(string p, float v) { var prop = so.FindProperty(p); if (prop != null) prop.floatValue = v; }
            F("m_Settings.Array.data[0].agentRadius", 0.35f);
            F("m_Settings.Array.data[0].agentHeight", 1.8f);
            F("m_Settings.Array.data[0].agentClimb", 0.45f);
            F("m_Settings.Array.data[0].agentSlope", 46f);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BakeNavMesh(GameObject root, string navAssetPath = "Assets/LR2/Scenes/LR2_Saltworks_NavMesh.asset", bool saltworksDropLinks = true)
        {
            var surface = root.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = LR2Layers.ToMask(LR2Layers.Default, LR2Layers.Obstacle, LR2Layers.PlayerClip);
            surface.BuildNavMesh();
            if (surface.navMeshData != null)
            {
                if (AssetDatabase.LoadAssetAtPath<NavMeshData>(navAssetPath) != null) AssetDatabase.DeleteAsset(navAssetPath);
                AssetDatabase.CreateAsset(surface.navMeshData, navAssetPath);
                EditorUtility.SetDirty(surface);
            }

            if (saltworksDropLinks)
            {
                void Link(Vector3 a, Vector3 b)
                {
                    var go = new GameObject("DropLink");
                    go.transform.SetParent(_gameplay, false);
                    var l = go.AddComponent<NavMeshLink>();
                    l.startPoint = a;
                    l.endPoint = b;
                    l.bidirectional = false;
                    l.width = 1f;
                }
                Link(new Vector3(0f, 3.62f, 5.2f), new Vector3(0f, 0.05f, 6.8f));
                Link(new Vector3(4.2f, 3.62f, 2f), new Vector3(5.6f, 0.05f, 3.6f));
                Link(new Vector3(20.9f, 5.2f, 0.3f), new Vector3(22.6f, 0.05f, 0.3f));
                Link(new Vector3(0f, 1.25f, -9.8f), new Vector3(0f, 0.05f, -8.6f));
            }
            Debug.Log($"[LR2] NavMesh baked: {navAssetPath}");
        }

        private static void GenerateCoverPoints(Vector2 playMin, Vector2 playMax, float[] sampleLevels)
        {
            int made = 0;
            var positions = new List<Vector3>();
            Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right, new Vector3(1, 0, 1).normalized, new Vector3(-1, 0, 1).normalized, new Vector3(1, 0, -1).normalized, new Vector3(-1, 0, -1).normalized };
            for (float x = playMin.x + 1f; x < playMax.x; x += 2.2f)
                for (float z = playMin.y + 1f; z < playMax.y; z += 2.2f)
                {
                foreach (float level in sampleLevels)
                {
                    if (!NavMesh.SamplePosition(new Vector3(x, level, z), out var hit, 1.3f, NavMesh.AllAreas)) continue;
                    Vector3 p = hit.position;
                    if (Mathf.Abs(p.y - level) > 1.4f) continue;
                    if (positions.Exists(q => (q - p).sqrMagnitude < 6f)) continue;
                    foreach (var d in dirs)
                    {
                        if (!Physics.Raycast(p + Vector3.up * 0.9f, d, out var wall, 1.3f, LR2Layers.EnvironmentMask, QueryTriggerInteraction.Ignore)) continue;
                        bool high = Physics.Raycast(p + Vector3.up * 1.6f, d, 1.4f, LR2Layers.EnvironmentMask, QueryTriggerInteraction.Ignore);
                        var go = new GameObject("Cover");
                        go.transform.SetParent(_cover, false);
                        go.transform.position = p;
                        go.transform.rotation = Quaternion.LookRotation(-wall.normal);
                        go.AddComponent<CoverPoint>().crouchCover = !high;
                        positions.Add(p);
                        made++;
                        break;
                    }
                }
                }
            Debug.Log($"[LR2] Cover points generated: {made}");
        }

        /// <summary>Tactical minimap rendered by top-down raycasts (independent of GPU/batch-mode rendering).</summary>
        private static void BuildMinimap(string textureOutPath, Vector2 playMin, Vector2 playMax)
        {
            Vector2 min = playMin - new Vector2(3f, 3f), max = playMax + new Vector2(3f, 3f);
            int w = 600, h = Mathf.RoundToInt(w * (max.y - min.y) / (max.x - min.x));
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var heights = new float[w, h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float wx = Mathf.Lerp(min.x, max.x, (x + 0.5f) / w), wz = Mathf.Lerp(min.y, max.y, (y + 0.5f) / h);
                    heights[x, y] = Physics.Raycast(new Vector3(wx, 18f, wz), Vector3.down, out var hit, 28f, LR2Layers.EnvironmentMask, QueryTriggerInteraction.Ignore) ? hit.point.y : -2f;
                }
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float ht = heights[x, y];
                    float edge = 0f;
                    if (x > 0 && y > 0 && x < w - 1 && y < h - 1)
                        edge = Mathf.Abs(heights[x + 1, y] - heights[x - 1, y]) + Mathf.Abs(heights[x, y + 1] - heights[x, y - 1]);
                    Color c;
                    if (ht < -1f) c = new Color(0.05f, 0.1f, 0.13f, 1f); // water
                    else
                    {
                        float lum = 0.16f + Mathf.Clamp01(ht / 8f) * 0.42f;
                        c = new Color(lum * 0.92f, lum, lum * 1.02f, 1f);
                        if (edge > 0.4f) c = Color.Lerp(c, new Color(0.85f, 0.88f, 0.9f), 0.65f);
                    }
                    tex.SetPixel(x, y, c);
                }
            tex.Apply();
            var saved = SaveTexture(tex, textureOutPath, false, false, false);
            var data = new GameObject("MinimapData").AddComponent<MinimapData>();
            data.transform.SetParent(_systems, false);
            data.mapTexture = saved;
            data.worldMin = min;
            data.worldMax = max;
        }

        // ================================================================================================= main menu scene
        public static void BuildMenuScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cfg = LoadOrCreate<MapBuildConfig>(ConfigPath);
            if (_m == null) _m = MakeMaterials();
            var set = new GameObject("MenuSet").transform;
            Box("Floor", set, new Vector3(0f, -0.25f, 0f), new Vector3(40f, 0.5f, 40f), _m.asphalt);
            Container(set, "BackA", new Vector3(-1.5f, 0f, 4.2f), 0f, 0);
            Container(set, "BackB", new Vector3(-1.5f, 2.59f, 4.2f), 0f, 3);
            Container(set, "Side", new Vector3(5.2f, 0f, 1.5f), 70f, 1);
            Box("Crate", set, new Vector3(1.6f, 0.45f, 1.4f), new Vector3(0.9f, 0.9f, 0.9f), _m.wood);
            Box("Crate2", set, new Vector3(1.9f, 1.25f, 1.5f), new Vector3(0.7f, 0.7f, 0.7f), _m.wood, LR2Layers.Default, null, 20f);

            var bot = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LR2/Prefabs/Characters/LR2_Soldier_Bot.prefab");
            if (bot != null)
            {
                var hero = (GameObject)PrefabUtility.InstantiatePrefab(bot);
                hero.name = "HeroSoldier";
                hero.transform.SetPositionAndRotation(new Vector3(0.4f, 0f, 1.6f), Quaternion.Euler(0f, 205f, 0f));
                var bc = hero.GetComponent<BotController>();
                if (bc != null) Object.DestroyImmediate(bc);
                var agent = hero.GetComponent<NavMeshAgent>();
                if (agent != null) Object.DestroyImmediate(agent);
                var c = hero.GetComponent<Combatant>();
                if (c != null) c.DisplayName = "OPERATOR";
            }

            var camGo = new GameObject("MenuCamera");
            camGo.tag = "MainCamera";
            camGo.transform.SetPositionAndRotation(new Vector3(-1.05f, 1.48f, -1.85f), Quaternion.Euler(3f, 28.5f, 0f));
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 38f;
            var hd = camGo.AddComponent<HDAdditionalCameraData>();
            hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
            hd.TAAQuality = HDAdditionalCameraData.TAAQualityLevel.High;
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<MenuCameraDrift>();

            var lights = new GameObject("Lighting").transform;
            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(lights, false);
            sunGo.transform.rotation = Quaternion.Euler(9f, 150f, 0f);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.lightUnit = LightUnit.Lux;
            sun.intensity = 9000f;
            sun.useColorTemperature = true;
            sun.colorTemperature = 3600f;
            sun.shadows = LightShadows.Soft;
            sunGo.AddComponent<HDAdditionalLightData>();
            AddLight(lights, "RimLight", new Vector3(2.2f, 2.6f, 3.4f), Quaternion.LookRotation(new Vector3(0.4f, 1.2f, 1.6f) - new Vector3(2.2f, 2.6f, 3.4f)), LightType.Spot, 5200f, 16000f, 10f, 50f, true);
            AddLight(lights, "KeyLight", new Vector3(-2.5f, 2.2f, -1f), Quaternion.LookRotation(new Vector3(0.4f, 1.3f, 1.6f) - new Vector3(-2.5f, 2.2f, -1f)), LightType.Spot, 3200f, 6000f, 9f, 60f, false);

            var profile = NewProfile(LightDir + "/LR2_Menu_Profile.asset");
            var ve = profile.Add<VisualEnvironment>(true);
            ve.skyType.Override((int)SkyType.PhysicallyBased);
            profile.Add<PhysicallyBasedSky>(true);
            var fog = profile.Add<Fog>(true);
            fog.enabled.Override(true);
            fog.enableVolumetricFog.Override(true);
            fog.meanFreePath.Override(28f);
            fog.albedo.Override(new Color(0.6f, 0.66f, 0.72f));
            fog.anisotropy.Override(0.8f);
            var ex = profile.Add<Exposure>(true);
            ex.mode.Override(ExposureMode.AutomaticHistogram);
            ex.compensation.Override(0.4f);
            ex.adaptationMode.Override(AdaptationMode.Progressive);
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);
            var color = profile.Add<ColorAdjustments>(true);
            color.contrast.Override(30f);
            color.saturation.Override(-12f);
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.4f);
            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(0.38f);
            var grain = profile.Add<FilmGrain>(true);
            grain.intensity.Override(0.25f);
            var dof = profile.Add<DepthOfField>(true);
            dof.focusMode.Override(DepthOfFieldMode.Manual);
            dof.nearFocusStart.Override(0f);
            dof.nearFocusEnd.Override(0.4f);
            dof.farFocusStart.Override(4.5f);
            dof.farFocusEnd.Override(12f);
            profile.Add<ScreenSpaceAmbientOcclusion>(true).intensity.Override(0.6f);
            profile.Add<ContactShadows>(true).enable.Override(true);
            DisableMotionBlur(profile);
            SaveProfile(profile);
            var vol = new GameObject("Volume").AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;

            var ui = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LR2/Prefabs/UI/LR2_MainMenuUI.prefab");
            if (ui != null) PrefabUtility.InstantiatePrefab(ui);
            LR2UIBuilder.EnsureEventSystem();
            var amb = new GameObject("Ambience").AddComponent<LR2AudioEnvironment>();
            var common = AssetDatabase.LoadAssetAtPath<LR2CommonSounds>("Assets/LR2/Resources/LR2/LR2CommonSounds.asset");
            if (common != null) amb.ambienceLoops = common.ambienceLoops;

            EditorSceneManager.SaveScene(scene, MenuScenePath);
            SetBuildScenes();
            Debug.Log("[LR2] Main menu scene built: " + MenuScenePath);
        }

        public static void SetBuildScenes()
        {
            var required = new[] { MenuScenePath, ScenePath, DeadsectorScenePath, FoundryScenePath, TestRangeScenePath };
            var list = new List<EditorBuildSettingsScene>();
            var added = new HashSet<string>();
            foreach (var p in required)
            {
                if (File.Exists(p) && added.Add(p)) list.Add(new EditorBuildSettingsScene(p, true));
            }
            foreach (var s in EditorBuildSettings.scenes)
                if (added.Add(s.path) && File.Exists(s.path)) list.Add(new EditorBuildSettingsScene(s.path, false));
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
