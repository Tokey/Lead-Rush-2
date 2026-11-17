using System.Collections.Generic;
using LR2.AI;
using LR2.Audio;
using LR2.Combat;
using LR2.Core;
using LR2.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using static LR2.EditorTools.LR2EditorUtil;

namespace LR2.EditorTools
{
    public static partial class LR2MapBuilder
    {
        // =================================================================================================
        // Map 2: Deadsector — Dark, war-torn abandoned city block (no foliage)
        // =================================================================================================
        public static void BuildDeadsector()
        {
            var root = BeginExtraMap("Deadsector", "Assets/LR2/Data/Map/DeadsectorConfig.asset", 4021, out var scene);
            var env = root.transform.Find("Environment");
            Vector2 pMin = new Vector2(-28f, -28f), pMax = new Vector2(28f, 28f);

            // Ground: damaged asphalt intersection + 4 distinct corner sidewalks (no overlapping slabs)
            var g = Child(_structures, "Ground");
            Box("AsphaltBase", g, new Vector3(0f, -0.25f, 0f), new Vector3(130f, 0.5f, 130f), _m.asphalt, LR2Layers.Default, SurfaceType.Concrete);
            void Sidewalk(string n, float cx, float cz, float sx, float sz) =>
                Box(n, g, new Vector3(cx, 0.06f, cz), new Vector3(sx, 0.12f, sz), _m.concrete, LR2Layers.Default, SurfaceType.Concrete);
            Sidewalk("SidewalkNW", -17.5f, 17.5f, 21f, 21f);
            Sidewalk("SidewalkNE", 17.5f, 17.5f, 21f, 21f);
            Sidewalk("SidewalkSW", -17.5f, -17.5f, 21f, 21f);
            Sidewalk("SidewalkSE", 17.5f, -17.5f, 21f, 21f);
            // Road markings
            for (int i = -24; i <= 24; i += 6)
            {
                if (Mathf.Abs(i) < 7) continue;
                var sx = Box("LaneX", g, new Vector3(i, 0.008f, 0f), new Vector3(2.4f, 0.008f, 0.14f), _m.curb);
                Object.DestroyImmediate(sx.GetComponent<Collider>());
                var sz = Box("LaneZ", g, new Vector3(0f, 0.008f, i), new Vector3(0.14f, 0.008f, 2.4f), _m.curb);
                Object.DestroyImmediate(sz.GetComponent<Collider>());
            }

            // 4 Enterable war-torn corner buildings (wide doorways, damaged parapets, destructible windows & furniture)
            void CornerRuins(string name, float x0, float x1, float z0, float z1, bool brokenRoof)
            {
                var b = Child(_structures, name);
                float cx = (x0 + x1) * 0.5f, cz = (z0 + z1) * 0.5f;
                float w = x1 - x0, d = z1 - z0, hgt = 4.0f, th = 0.28f;
                Box("Floor", b, new Vector3(cx, 0.08f, cz), new Vector3(w, 0.12f, d), _m.concrete, LR2Layers.Default, SurfaceType.Concrete);
                // Side facing X=0 and Z=0 gets big doorways & broken windows
                float innerDoor = 2.4f;
                Vector4 dSpec(float off) => new Vector4(off, innerDoor, 0f, 2.5f);
                Vector4 wSpec(float off) => new Vector4(off, 1.8f, 0.95f, 2.4f);
                Wall("WallN", b, new Vector3(x0, 0.04f, z1), new Vector3(x1, 0.04f, z1), hgt, th, _m.wallPlaster, d < 14f ? dSpec(w * 0.5f) : wSpec(w * 0.3f), dSpec(w * 0.72f));
                Wall("WallS", b, new Vector3(x0, 0.04f, z0), new Vector3(x1, 0.04f, z0), hgt, th, _m.wallPlaster, dSpec(w * 0.35f), wSpec(w * 0.75f));
                Wall("WallW", b, new Vector3(x0, 0.04f, z0), new Vector3(x0, 0.04f, z1), hgt, th, _m.wallPlaster, dSpec(d * 0.55f));
                Wall("WallE", b, new Vector3(x1, 0.04f, z0), new Vector3(x1, 0.04f, z1), hgt, th, _m.wallPlaster, dSpec(d * 0.45f), wSpec(d * 0.8f));
                if (brokenRoof)
                {
                    Box("RoofWest", b, new Vector3(x0 + w * 0.26f, hgt + 0.14f, cz), new Vector3(w * 0.52f, 0.2f, d + 0.4f), _m.concrete, LR2Layers.Default, SurfaceType.Concrete);
                    Box("RoofEast", b, new Vector3(x1 - w * 0.16f, hgt + 0.14f, cz), new Vector3(w * 0.32f, 0.2f, d + 0.4f), _m.concrete, LR2Layers.Default, SurfaceType.Concrete);
                }
                else
                {
                    Box("Roof", b, new Vector3(cx, hgt + 0.14f, cz), new Vector3(w + 0.4f, 0.2f, d + 0.4f), _m.concrete, LR2Layers.Default, SurfaceType.Concrete);
                }
                // Interior destructible cover props + glass + warm interior emergency bulb
                Box("Desk", b, new Vector3(cx - 1.5f, 0.54f, cz + 1.2f), new Vector3(2.0f, 0.8f, 0.85f), _m.wood, LR2Layers.Default, SurfaceType.Wood);
                Box("Crate", b, new Vector3(cx + 1.8f, 0.59f, cz - 1.4f), new Vector3(0.95f, 0.9f, 0.95f), _m.wood, LR2Layers.Default, SurfaceType.Wood);
                Box("Lockers", b, new Vector3(cx - 2.2f, 1.14f, cz - 2.0f), new Vector3(0.6f, 2.0f, 1.6f), _m.darkMetal, LR2Layers.Default, SurfaceType.Metal);
                AddLight(_lighting, "Emergency_" + name, new Vector3(cx, 3.4f, cz), Quaternion.identity, LightType.Point, 3100f, 2400f, 13f, 0f, false);
            }
            CornerRuins("TransitDepot_NW", -24f, -11f, 11f, 23f, true);
            CornerRuins("Pharmacy_NE", 11f, 24f, 11f, 23f, false);
            CornerRuins("Apartment_SW", -24f, -11f, -23f, -11f, false);
            CornerRuins("Garage_SE", 11f, 24f, -23f, -11f, true);

            // Breakable street-level windows on building facades facing mid intersection
            Box("Glass_NW", _structures, new Vector3(-14.25f, 1.71f, 11f), new Vector3(1.8f, 1.35f, 0.04f), _m.glass, LR2Layers.Default, SurfaceType.Glass);
            Box("Glass_NE", _structures, new Vector3(14.25f, 1.71f, 11f), new Vector3(1.8f, 1.35f, 0.04f), _m.glass, LR2Layers.Default, SurfaceType.Glass);
            Box("Glass_SW", _structures, new Vector3(-14.25f, 1.71f, -11f), new Vector3(1.8f, 1.35f, 0.04f), _m.glass, LR2Layers.Default, SurfaceType.Glass);
            Box("Glass_SE", _structures, new Vector3(14.25f, 1.71f, -11f), new Vector3(1.8f, 1.35f, 0.04f), _m.glass, LR2Layers.Default, SurfaceType.Glass);

            // Central checkpoint ruined bus/container + crossroad cover
            Container(_structures, "CheckpointContA", new Vector3(-2.5f, 0f, 2.5f), 22f, 2);
            Container(_structures, "CheckpointContB", new Vector3(3.2f, 0f, -3.5f), -68f, 0);

            // Perimeter walls + Clip + War-torn city skyline
            var bnd = Child(_structures, "Boundary");
            Box("WallN", bnd, new Vector3(0f, 2.2f, 28.5f), new Vector3(58f, 4.4f, 0.5f), _m.wallPlaster, LR2Layers.Default, SurfaceType.Concrete);
            Box("WallS", bnd, new Vector3(0f, 2.2f, -28.5f), new Vector3(58f, 4.4f, 0.5f), _m.wallPlaster, LR2Layers.Default, SurfaceType.Concrete);
            Box("WallW", bnd, new Vector3(-28.5f, 2.2f, 0f), new Vector3(0.5f, 4.4f, 58f), _m.wallPlaster, LR2Layers.Default, SurfaceType.Concrete);
            Box("WallE", bnd, new Vector3(28.5f, 2.2f, 0f), new Vector3(0.5f, 4.4f, 58f), _m.wallPlaster, LR2Layers.Default, SurfaceType.Concrete);
            AddPlayerClipBox(pMin, pMax, 10f);

            // Urban skyline ruins beyond walls (solid block silhouettes + cranes/silos, no open modular wall strips)
            foreach (var (tn, tp, ts) in new[]
            {
                ("SkyTower_W1", new Vector3(-40f, 6.5f, -16f), new Vector3(10f, 13f, 14f)),
                ("SkyTower_W2", new Vector3(-41f, 8.0f, 16f),  new Vector3(11f, 16f, 14f)),
                ("SkyTower_E1", new Vector3(40f, 7.0f, -16f),  new Vector3(10f, 14f, 14f)),
                ("SkyTower_E2", new Vector3(41f, 6.0f, 16f),   new Vector3(11f, 12f, 14f)),
                ("SkyTower_N1", new Vector3(-16f, 7.5f, 40f),  new Vector3(14f, 15f, 10f)),
                ("SkyTower_N2", new Vector3(16f, 6.5f, 41f),   new Vector3(14f, 13f, 10f)),
                ("SkyTower_S1", new Vector3(-16f, 6.0f, -40f), new Vector3(14f, 12f, 10f)),
                ("SkyTower_S2", new Vector3(16f, 8.0f, -41f),  new Vector3(14f, 16f, 10f)),
            })
            {
                var st = Box(tn, _background, tp, ts, _m.concrete, LR2Layers.Default, SurfaceType.Concrete);
                Object.DestroyImmediate(st.GetComponent<Collider>());
            }
            Prop("SM_Crane_01", _background, new Vector3(-35f, 0f, 36f), 35f, 1f, false);
            Prop("SM_Crane_02", _background, new Vector3(36f, 0f, -35f), -45f, 1f, false);

            // Grungy war-torn street cover & props (every prop placed deliberately in clear road/plaza spaces)
            foreach (var (pos, yaw) in new[]
            {
                (new Vector3(-7.2f, 0f, 1.5f), 85f), (new Vector3(7.4f, 0f, -1.2f), 95f),
                (new Vector3(1.5f, 0f, 8.2f), 10f), (new Vector3(-1.5f, 0f, -8.5f), -8f),
                (new Vector3(-8.5f, 0.12f, 17f), 90f), (new Vector3(8.5f, 0.12f, -17f), 90f),
                (new Vector3(17f, 0.12f, 8.5f), 0f), (new Vector3(-17f, 0.12f, -8.5f), 0f),
            }) Prop("SM_Jersey", _props, pos, yaw);

            foreach (var (pos, yaw) in new[]
            {
                (new Vector3(-5.8f, 0f, 6.4f), 25f), (new Vector3(6.2f, 0f, 7.1f), -40f),
                (new Vector3(-6.4f, 0f, -6.2f), 115f), (new Vector3(6.0f, 0f, -6.8f), 15f),
                (new Vector3(-20f, 0f, 2f), 45f), (new Vector3(20f, 0f, -2f), 130f),
            })
            {
                Prop("SM_Barrel_01", _props, pos, yaw);
                Prop("SM_Barrel_02", _props, pos + new Vector3(0.85f, 0f, 0.25f), yaw + 35f);
            }

            foreach (var (pos, yaw) in new[]
            {
                (new Vector3(-14f, 0f, 4f), 10f), (new Vector3(14f, 0f, -4f), 80f),
                (new Vector3(3f, 0f, 16f), 0f), (new Vector3(-3f, 0f, -16f), 45f),
                (new Vector3(-16f, 0.12f, 16f), 15f), (new Vector3(16f, 0.12f, -16f), -20f),
            })
            {
                Prop("SM_Crate_01", _props, pos, yaw);
                Prop("SM_Woodpallet_1", _props, pos + new Vector3(1.5f, 0f, 0f), yaw + 25f);
            }

            Prop("SM_Gascylender", _props, new Vector3(-9.5f, 0.12f, 11.5f), 0f);
            Prop("SM_Gascylender", _props, new Vector3(9.5f, 0.12f, -11.5f), 0f);
            Prop("SM_Cablespool", _props, new Vector3(-2f, 0f, 21f), 20f);
            Prop("SM_Cablespool", _props, new Vector3(2f, 0f, -21f), -15f);
            Prop("SM_Tire", _props, new Vector3(-10f, 0f, -3f), 0f);
            Prop("SM_Tire", _props, new Vector3(10f, 0f, 3f), 40f);
            Prop("SM_Firehydrant", _props, new Vector3(8.5f, 0.12f, 8.5f), 0f);
            Prop("SM_Firehydrant", _props, new Vector3(-8.5f, 0.12f, -8.5f), 180f);
            Prop("SM_Trashcan", _props, new Vector3(-10f, 0.12f, 9f), 0f);
            Prop("SM_Trashcan_1", _props, new Vector3(10f, 0.12f, -9f), 90f);

            // Rubble mounds (non-foliage debris)
            foreach (var (name, pos, yaw) in new[]
            {
                ("SM_Rubble_01_01", new Vector3(-4.5f, 0f, 14f), 20f),
                ("SM_Rubble_02_01", new Vector3(4.5f, 0f, -14f), -50f),
                ("SM_Rubble_02_02", new Vector3(-19f, 0f, -4.5f), 75f),
                ("SM_Rubble_02_03", new Vector3(19f, 0f, 4.5f), -30f),
            })
            {
                var rp = LoadRuins(name);
                if (rp != null)
                {
                    var rgo = (GameObject)PrefabUtility.InstantiatePrefab(rp, _props);
                    rgo.transform.position = pos;
                    rgo.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                    rgo.AddComponent<SurfaceIdentifier>().surface = SurfaceType.Concrete;
                    if (rgo.GetComponentInChildren<Collider>() == null) rgo.AddComponent<BoxCollider>();
                }
            }

            // 4 Burning Barrel Fires (ambient glow, flickering light, safe particle systems)
            foreach (var fp in new[] { new Vector3(-8.5f, 0f, 4.5f), new Vector3(8.5f, 0f, -4.5f), new Vector3(4.5f, 0f, 19f), new Vector3(-4.5f, 0f, -19f) })
                SpawnBurningBarrel(_lighting, _props, fp);

            // Spawns, TacticalPoints & AmmoCrates
            AddSpawn(new Vector3(-25f, 0.15f, -3f), 90f, TeamId.Alpha, true, false);
            AddSpawn(new Vector3(-22f, 0.15f, 7f), 110f, TeamId.Alpha, true, false);
            AddSpawn(new Vector3(-18f, 0.15f, -20f), 45f, TeamId.Alpha, true, false);
            AddSpawn(new Vector3(-4f, 0.05f, -24f), 0f, TeamId.Alpha, true, false);
            AddSpawn(new Vector3(25f, 0.15f, 3f), -90f, TeamId.Bravo, true, false);
            AddSpawn(new Vector3(22f, 0.15f, -7f), -70f, TeamId.Bravo, true, false);
            AddSpawn(new Vector3(18f, 0.15f, 20f), -135f, TeamId.Bravo, true, false);
            AddSpawn(new Vector3(4f, 0.05f, 24f), 180f, TeamId.Bravo, true, false);
            AddSpawn(new Vector3(0f, 0.05f, 24f), 180f, TeamId.None, false, false);
            AddSpawn(new Vector3(0f, 0.05f, -24f), 0f, TeamId.None, false, false);
            AddSpawn(new Vector3(-24f, 0.05f, 0f), 90f, TeamId.None, false, false);
            AddSpawn(new Vector3(24f, 0.05f, 0f), -90f, TeamId.None, false, false);

            AddTac(new Vector3(-17.5f, 0.18f, 17f), TacticalPointType.Patrol, 0.7f, false);
            AddTac(new Vector3(17.5f, 0.18f, 17f), TacticalPointType.Patrol, 0.7f, false);
            AddTac(new Vector3(-17.5f, 0.18f, -17f), TacticalPointType.Patrol, 0.7f, false);
            AddTac(new Vector3(17.5f, 0.18f, -17f), TacticalPointType.Patrol, 0.7f, false);
            AddTac(new Vector3(0f, 0.05f, 0f), TacticalPointType.Choke, 0.85f, false);
            AddTac(new Vector3(-8f, 0.05f, 0f), TacticalPointType.Choke, 0.65f, false);
            AddTac(new Vector3(8f, 0.05f, 0f), TacticalPointType.Choke, 0.65f, false);

            AddAmmoCrate(new Vector3(-5.5f, 0.01f, -2.5f));
            AddAmmoCrate(new Vector3(5.5f, 0.01f, 2.5f));

            // Moody overcast dusk HDRP lighting + warm streetlamps + weather rain (safe curve modes)
            foreach (var (sx, sz) in new[] { (-8f, 8f), (8f, 8f), (-8f, -8f), (8f, -8f) })
                AddLight(_lighting, $"StreetLamp_{(sx < 0 ? "W" : "E")}_{(sz < 0 ? "S" : "N")}",
                    new Vector3(sx, 4.5f, sz), Quaternion.identity, LightType.Point, 2800f, 4800f, 22f, 0f, false);
            foreach (float oz in new[] { -14f, 14f })
                AddLight(_lighting, $"Overhead_{(oz < 0 ? "S" : "N")}",
                    new Vector3(0f, 5.5f, oz), Quaternion.identity, LightType.Point, 3200f, 4500f, 22f, 0f, false);
            BuildCustomLighting("LR2_Deadsector_Profile", sunElev: 25f, sunAz: 225f, sunLux: 20000f, sunTemp: 4300f,
                fogMfp: 42f, fogColor: new Color(0.46f, 0.50f, 0.54f), exposureComp: 0.65f, contrast: 18f, saturation: -16f,
                rainDefaultPreset: 2);
            PlaceMapDecals(pMin, pMax, 140);
            AddMatchSystems(40f);
            MarkStatic(env);

            EliminateSceneClipping("LR2_Deadsector", _structures, _props, null, _spawns, _gameplay);
            SetupDestructibleProps(root);

            Physics.SyncTransforms();
            BakeNavMesh(root, "Assets/LR2/Scenes/LR2_Deadsector_NavMesh.asset", false);
            GenerateCoverPoints(pMin, pMax, new[] { 0.4f });
            BuildMinimap("Assets/LR2/UI/Minimap/Deadsector_Minimap.png", pMin, pMax);

            EditorSceneManager.SaveScene(scene, DeadsectorScenePath);
            SetBuildScenes();
            Debug.Log("[LR2] Map built: " + DeadsectorScenePath);
        }

        // =================================================================================================
        // Map 3: Foundry — Multi-level industrial substation & sunken pipe trench (no foliage)
        // =================================================================================================
        public static void BuildFoundry()
        {
            var root = BeginExtraMap("Foundry", "Assets/LR2/Data/Map/FoundryConfig.asset", 7042, out var scene);
            var env = root.transform.Find("Environment");
            Vector2 pMin = new Vector2(-28f, -28f), pMax = new Vector2(28f, 28f);

            // Ground slabs surrounding Central Sunken Pipe Trench (x [-5.5, 5.5], z [-14, 14], floor at y = -1.5)
            var g = Child(_structures, "Ground");
            Box("FloorN", g, new Vector3(0f, -0.25f, 22f), new Vector3(60f, 0.5f, 16f), _m.concrete, LR2Layers.Default, SurfaceType.Concrete);
            Box("FloorS", g, new Vector3(0f, -0.25f, -22f), new Vector3(60f, 0.5f, 16f), _m.concrete, LR2Layers.Default, SurfaceType.Concrete);
            Box("FloorW", g, new Vector3(-18f, -0.25f, 0f), new Vector3(25f, 0.5f, 28f), _m.concrete, LR2Layers.Default, SurfaceType.Concrete);
            Box("FloorE", g, new Vector3(18f, -0.25f, 0f), new Vector3(25f, 0.5f, 28f), _m.concrete, LR2Layers.Default, SurfaceType.Concrete);
            Box("TrenchFloor", g, new Vector3(0f, -1.75f, 0f), new Vector3(12f, 0.5f, 30f), _m.asphalt, LR2Layers.Default, SurfaceType.Concrete);

            // Retaining walls around trench leaving North, South, East, West wide access ramps
            void Retain(string n, float cx, float cz, float sx, float sz) =>
                Box(n, g, new Vector3(cx, -0.75f, cz), new Vector3(sx, 1.5f, sz), _m.wallPlaster, LR2Layers.Default, SurfaceType.Concrete);
            Retain("RetW_N", -5.65f, 8.2f, 0.35f, 11.4f);
            Retain("RetW_S", -5.65f, -8.2f, 0.35f, 11.4f);
            Retain("RetE_N", 5.65f, 8.2f, 0.35f, 11.4f);
            Retain("RetE_S", 5.65f, -8.2f, 0.35f, 11.4f);
            Retain("RetN_L", -4.0f, 14.1f, 3.2f, 0.35f);
            Retain("RetN_R", 4.0f, 14.1f, 3.2f, 0.35f);
            Retain("RetS_L", -4.0f, -14.1f, 3.2f, 0.35f);
            Retain("RetS_R", 4.0f, -14.1f, 3.2f, 0.35f);

            // 4 Graded walk-down ramps into the trench
            Stairs("RampTrenchN", _structures, new Vector3(0f, -1.5f, 9.5f), new Vector3(0f, 0f, 14.2f), 4.6f, _m.steelGrate, false);
            Stairs("RampTrenchS", _structures, new Vector3(0f, -1.5f, -9.5f), new Vector3(0f, 0f, -14.2f), 4.6f, _m.steelGrate, false);
            Stairs("RampTrenchW", _structures, new Vector3(-2.5f, -1.5f, 0f), new Vector3(-5.8f, 0f, 0f), 3.6f, _m.steelGrate, false);
            Stairs("RampTrenchE", _structures, new Vector3(2.5f, -1.5f, 0f), new Vector3(5.8f, 0f, 0f), 3.6f, _m.steelGrate, false);

            // Overhead pipe bridges spanning trench
            Box("PipeBridgeN", _structures, new Vector3(0f, 3.1f, 7.5f), new Vector3(13f, 0.45f, 1.4f), _m.rust, LR2Layers.Default, SurfaceType.Metal);
            Box("PipeBridgeS", _structures, new Vector3(0f, 3.1f, -7.5f), new Vector3(13f, 0.45f, 1.4f), _m.rust, LR2Layers.Default, SurfaceType.Metal);

            // North Pump Hall & South Turbine Shed (wide bay doors, interior machinery cover)
            void IndustrialHall(string name, float zCenter)
            {
                var h = Child(_structures, name);
                float z0 = zCenter - 4.5f, z1 = zCenter + 4.5f;
                float x0 = -13f, x1 = 13f, hgt = 6.2f, t = 0.3f;
                Wall("WallN", h, new Vector3(x0, 0f, z1), new Vector3(x1, 0f, z1), hgt, t, _m.corrugated, new Vector4(6f, 3.2f, 0f, 3.4f), new Vector4(20f, 3.2f, 0f, 3.4f));
                Wall("WallS", h, new Vector3(x0, 0f, z0), new Vector3(x1, 0f, z0), hgt, t, _m.corrugated, new Vector4(6f, 3.2f, 0f, 3.4f), new Vector4(20f, 3.2f, 0f, 3.4f));
                Wall("WallW", h, new Vector3(x0, 0f, z0), new Vector3(x0, 0f, z1), hgt, t, _m.corrugated, new Vector4(4.5f, 2.8f, 0f, 3.2f));
                Wall("WallE", h, new Vector3(x1, 0f, z0), new Vector3(x1, 0f, z1), hgt, t, _m.corrugated, new Vector4(4.5f, 2.8f, 0f, 3.2f));
                Box("Roof", h, new Vector3(0f, hgt + 0.1f, zCenter), new Vector3(26.6f, 0.2f, 9.6f), _m.roof, LR2Layers.Default, SurfaceType.Metal);
                // Interior generators + destructible consoles
                Box("GeneratorW", h, new Vector3(-6.5f, 1.0f, zCenter), new Vector3(3.4f, 2.0f, 1.8f), _m.darkMetal, LR2Layers.Default, SurfaceType.Metal);
                Box("GeneratorE", h, new Vector3(6.5f, 1.0f, zCenter), new Vector3(3.4f, 2.0f, 1.8f), _m.darkMetal, LR2Layers.Default, SurfaceType.Metal);
                Box("ControlConsole", h, new Vector3(0f, 0.6f, zCenter), new Vector3(2.4f, 1.2f, 0.9f), _m.metal, LR2Layers.Default, SurfaceType.Metal);
                AddLight(_lighting, "HallLamp_" + name, new Vector3(0f, 5.2f, zCenter), Quaternion.identity, LightType.Point, 3500f, 3600f, 18f, 0f, false);
            }
            IndustrialHall("NorthPumpHall", 20.5f);
            IndustrialHall("SouthTurbineShed", -20.5f);

            // East Elevated Catwalk Network (y = 3.0) overlooking central trench
            var cat = Child(_structures, "EastCatwalk");
            Box("CatDeck", cat, new Vector3(17.5f, 2.95f, 0f), new Vector3(3.6f, 0.14f, 20f), _m.steelGrate, LR2Layers.Default, SurfaceType.Metal);
            Box("CatDeckOut", cat, new Vector3(21.5f, 2.95f, 0f), new Vector3(4.4f, 0.14f, 8f), _m.steelGrate, LR2Layers.Default, SurfaceType.Metal);
            for (int i = -1; i <= 1; i++) Box("CatPillar", cat, new Vector3(17.5f, 1.45f, i * 8f), new Vector3(0.28f, 2.9f, 0.28f), _m.darkMetal, LR2Layers.Default, SurfaceType.Metal);
            Stairs("CatStairsN", cat, new Vector3(17.5f, 0f, 14.5f), new Vector3(17.5f, 3.02f, 10.0f), 2.2f, _m.steelGrate);
            Stairs("CatStairsS", cat, new Vector3(17.5f, 0f, -14.5f), new Vector3(17.5f, 3.02f, -10.0f), 2.2f, _m.steelGrate);
            Railing(cat, new Vector3(15.75f, 3.02f, -10f), new Vector3(15.75f, 3.02f, -2.5f));
            Railing(cat, new Vector3(15.75f, 3.02f, 2.5f), new Vector3(15.75f, 3.02f, 10f));
            Box("CatCoverCrate", cat, new Vector3(16.6f, 3.47f, 0f), new Vector3(0.9f, 0.9f, 1.8f), _m.wood, LR2Layers.Default, SurfaceType.Wood);

            // West Container Maze
            var wYard = Child(_structures, "WestContainers");
            Container(wYard, "ContW1", new Vector3(-16f, 0f, 6f), 0f, 1);
            Container(wYard, "ContW2", new Vector3(-16f, 0f, -6f), 12f, 3);
            Container(wYard, "ContW3", new Vector3(-23f, 0f, 0f), 90f, 0);
            Container(wYard, "ContW2_Top", new Vector3(-16f, 2.59f, 6f), 0f, 2);

            // Boundary + Skyline Silos/Cranes
            var bnd = Child(_structures, "Boundary");
            Box("WallN", bnd, new Vector3(0f, 2.5f, 28.5f), new Vector3(58f, 5f, 0.5f), _m.corrugated, LR2Layers.Default, SurfaceType.Metal);
            Box("WallS", bnd, new Vector3(0f, 2.5f, -28.5f), new Vector3(58f, 5f, 0.5f), _m.corrugated, LR2Layers.Default, SurfaceType.Metal);
            Box("WallW", bnd, new Vector3(-28.5f, 2.5f, 0f), new Vector3(0.5f, 5f, 58f), _m.corrugated, LR2Layers.Default, SurfaceType.Metal);
            Box("WallE", bnd, new Vector3(28.5f, 2.5f, 0f), new Vector3(0.5f, 5f, 58f), _m.corrugated, LR2Layers.Default, SurfaceType.Metal);
            AddPlayerClipBox(pMin, pMax, 11f);

            Prop("SM_Silo", _background, new Vector3(-37f, 0f, 15f), 0f, 1.2f, false);
            Prop("SM_Slio1", _background, new Vector3(-37f, 0f, -15f), 45f, 1.2f, false);
            Prop("SM_Slio2", _background, new Vector3(37f, 0f, 14f), 20f, 1.2f, false);
            Prop("SM_CylenderBuilding", _background, new Vector3(37f, 0f, -14f), 0f, 1.1f, false);
            Prop("SM_Crane_01", _background, new Vector3(-20f, 0f, 36f), 90f, 1f, false);
            Prop("SM_Crane_02", _background, new Vector3(20f, 0f, -36f), -90f, 1f, false);

            // Destructible industrial cover & barrels (placed in trench, courtyard, catwalks)
            Prop("SM_Jersey", _props, new Vector3(-8.5f, 0f, 4f), 90f);
            Prop("SM_Jersey", _props, new Vector3(-8.5f, 0f, -4f), 90f);
            Prop("SM_Jersey", _props, new Vector3(8.5f, 0f, 4f), 90f);
            Prop("SM_Jersey", _props, new Vector3(8.5f, 0f, -4f), 90f);
            Prop("SM_Jersey", _props, new Vector3(0f, -1.5f, 4.5f), 0f);
            Prop("SM_Jersey", _props, new Vector3(0f, -1.5f, -4.5f), 0f);
            Prop("SM_Barrel_01", _props, new Vector3(-2.5f, -1.5f, 2.5f), 0f);
            Prop("SM_Barrel_01", _props, new Vector3(2.5f, -1.5f, -2.5f), 45f);
            Prop("SM_Gascylender", _props, new Vector3(11.5f, 0f, 9f), 0f);
            Prop("SM_Gascylender", _props, new Vector3(-11.5f, 0f, -9f), 0f);
            Prop("SM_Crate_01", _props, new Vector3(-12f, 0f, 0f), 15f);
            Prop("SM_Woodpallet_1", _props, new Vector3(12f, 0f, 0f), -15f);
            Prop("SM_Cablespool", _props, new Vector3(21.5f, 3.02f, 0f), 0f);
            Prop("SM_Barrel_02", _props, new Vector3(-20f, 0f, 12f), 20f);
            Prop("SM_Barrel_04", _props, new Vector3(20f, 0f, -12f), -30f);

            // Spawns, Tactical points & Ammo crates
            AddSpawn(new Vector3(-23f, 0.05f, -11f), 45f, TeamId.Alpha, true, false);
            AddSpawn(new Vector3(-20f, 0.05f, -22f), 30f, TeamId.Alpha, true, false);
            AddSpawn(new Vector3(-8f, 0.05f, -20.5f), 0f, TeamId.Alpha, true, false);
            AddSpawn(new Vector3(-24f, 0.05f, 11f), 120f, TeamId.Alpha, true, false);
            AddSpawn(new Vector3(23f, 0.05f, 11f), -135f, TeamId.Bravo, true, false);
            AddSpawn(new Vector3(20f, 0.05f, 22f), -150f, TeamId.Bravo, true, false);
            AddSpawn(new Vector3(8f, 0.05f, 20.5f), 180f, TeamId.Bravo, true, false);
            AddSpawn(new Vector3(24f, 0.05f, -11f), -60f, TeamId.Bravo, true, false);
            AddSpawn(new Vector3(0f, 0.05f, 20.5f), 180f, TeamId.None, false, false);
            AddSpawn(new Vector3(0f, 0.05f, -20.5f), 0f, TeamId.None, false, false);
            AddSpawn(new Vector3(19f, 3.08f, 0f), -90f, TeamId.None, false, false);
            AddSpawn(new Vector3(-16f, 0.05f, 0f), 90f, TeamId.None, false, false);

            AddTac(new Vector3(17.5f, 3.08f, 0f), TacticalPointType.Overlook, 0.9f, false);
            AddTac(new Vector3(0f, -1.45f, 0f), TacticalPointType.Choke, 0.8f, false);
            AddTac(new Vector3(0f, 0.05f, 20.5f), TacticalPointType.Patrol, 0.7f, false);
            AddTac(new Vector3(0f, 0.05f, -20.5f), TacticalPointType.Patrol, 0.7f, false);
            AddTac(new Vector3(-16f, 0.05f, 0f), TacticalPointType.Patrol, 0.65f, false);

            AddAmmoCrate(new Vector3(0f, -1.5f, -1.8f));
            AddAmmoCrate(new Vector3(21.5f, 3.02f, 2.2f));

            foreach (var (fx, fz) in new[] { (-18f, 18f), (18f, 18f), (-18f, -18f), (18f, -18f) })
                AddLight(_lighting, $"YardFlood_{(fx < 0 ? "W" : "E")}_{(fz < 0 ? "S" : "N")}",
                    new Vector3(fx, 5.5f, fz), Quaternion.identity, LightType.Point, 3800f, 4500f, 22f, 0f, false);
            BuildCustomLighting("LR2_Foundry_Profile", sunElev: 28f, sunAz: 245f, sunLux: 14000f, sunTemp: 4100f,
                fogMfp: 55f, fogColor: new Color(0.55f, 0.56f, 0.54f), exposureComp: 0.45f, contrast: 20f, saturation: -6f,
                rainDefaultPreset: 0);
            PlaceMapDecals(pMin, pMax, 130);
            AddMatchSystems(38f);
            MarkStatic(env);

            EliminateSceneClipping("LR2_Foundry", _structures, _props, null, _spawns, _gameplay);
            SetupDestructibleProps(root);

            Physics.SyncTransforms();
            BakeNavMesh(root, "Assets/LR2/Scenes/LR2_Foundry_NavMesh.asset", false);
            GenerateCoverPoints(pMin, pMax, new[] { -1.2f, 0.35f, 3.3f });
            BuildMinimap("Assets/LR2/UI/Minimap/Foundry_Minimap.png", pMin, pMax);

            EditorSceneManager.SaveScene(scene, FoundryScenePath);
            SetBuildScenes();
            Debug.Log("[LR2] Map built: " + FoundryScenePath);
        }

        // =================================================================================================
        // Map 4: Test Range — Super-simple, crisp, visually sharp tactical & LLM bot testing arena
        // =================================================================================================
        public static void BuildTestRange()
        {
            var root = BeginExtraMap("TestRange", "Assets/LR2/Data/Map/TestRangeConfig.asset", 1001, out var scene);
            var env = root.transform.Find("Environment");
            Vector2 pMin = new Vector2(-30f, -30f), pMax = new Vector2(30f, 30f);

            // Clean flat court + center lane asphalt + safety range stripes (zero Z-fighting)
            var g = Child(_structures, "Ground");
            Box("CourtWest", g, new Vector3(-20f, -0.25f, 0f), new Vector3(20f, 0.5f, 64f), _m.wallPlaster, LR2Layers.Default, SurfaceType.Concrete);
            Box("CourtCenter", g, new Vector3(0f, -0.25f, 0f), new Vector3(20f, 0.5f, 64f), _m.wallPlaster, LR2Layers.Default, SurfaceType.Concrete);
            Box("CourtEast", g, new Vector3(20f, -0.25f, 0f), new Vector3(20f, 0.5f, 64f), _m.wallPlaster, LR2Layers.Default, SurfaceType.Concrete);

            // Clean yardage markings across the range floor (-20, -10, 0, +10, +20 m)
            for (int z = -20; z <= 20; z += 10)
            {
                var line = Box($"RangeLine_{z}", g, new Vector3(0f, 0.008f, z), new Vector3(54f, 0.008f, z == 0 ? 0.22f : 0.12f), _m.concrete);
                Object.DestroyImmediate(line.GetComponent<Collider>());
            }

            // Perimeter walls + corner architectural towers for clean skyline framing
            var bnd = Child(_structures, "Boundary");
            Box("WallN", bnd, new Vector3(0f, 2.25f, 30.25f), new Vector3(61f, 4.5f, 0.5f), _m.wallPlaster, LR2Layers.Default, SurfaceType.Concrete);
            Box("WallS", bnd, new Vector3(0f, 2.25f, -30.25f), new Vector3(61f, 4.5f, 0.5f), _m.wallPlaster, LR2Layers.Default, SurfaceType.Concrete);
            Box("WallW", bnd, new Vector3(-30.25f, 2.25f, 0f), new Vector3(0.5f, 4.5f, 61f), _m.wallPlaster, LR2Layers.Default, SurfaceType.Concrete);
            Box("WallE", bnd, new Vector3(30.25f, 2.25f, 0f), new Vector3(0.5f, 4.5f, 61f), _m.wallPlaster, LR2Layers.Default, SurfaceType.Concrete);
            foreach (var (tx, tz) in new[] { (-32f, 32f), (32f, 32f), (-32f, -32f), (32f, -32f) })
                Box("CornerTower", _background, new Vector3(tx, 5f, tz), new Vector3(5f, 10f, 5f), _m.corrugated, LR2Layers.Default, SurfaceType.Metal);
            AddPlayerClipBox(pMin, pMax, 10f);

            // ---- LANE 1 (Center): Symmetrical low crouch cover + tall LOS pillars
            var center = Child(_structures, "CenterLane");
            foreach (var (x, z, w, h, d) in new[]
            {
                (0f, 0f, 3.2f, 1.08f, 0.85f),
                (-5.5f, 6.5f, 2.4f, 1.08f, 0.85f), (5.5f, -6.5f, 2.4f, 1.08f, 0.85f),
                (-5.5f, -6.5f, 2.4f, 1.08f, 0.85f), (5.5f, 6.5f, 2.4f, 1.08f, 0.85f),
                (-4.5f, 15.5f, 2.8f, 2.3f, 0.95f), (4.5f, -15.5f, 2.8f, 2.3f, 0.95f),
                (4.5f, 15.5f, 2.8f, 2.3f, 0.95f), (-4.5f, -15.5f, 2.8f, 2.3f, 0.95f),
            })
            {
                Box("TacticalBlock", center, new Vector3(x, h * 0.5f, z), new Vector3(w, h, d), _m.concrete, LR2Layers.Default, SurfaceType.Concrete);
            }

            // ---- LANE 2 (West): 1.6m elevated overwatch platform + ramps + 0.85m mantle step box
            var west = Child(_structures, "WestElevationLane");
            Box("OverwatchDeck", west, new Vector3(-18f, 1.52f, 0f), new Vector3(6f, 0.16f, 12f), _m.steelGrate, LR2Layers.Default, SurfaceType.Metal);
            Box("DeckSupport", west, new Vector3(-18f, 0.72f, 0f), new Vector3(5.4f, 1.44f, 11.4f), _m.wallPlaster, LR2Layers.Default, SurfaceType.Concrete);
            Stairs("WestRampN", west, new Vector3(-18f, 0f, 10.5f), new Vector3(-18f, 1.6f, 6f), 2.8f, _m.steelGrate, false);
            Stairs("WestRampS", west, new Vector3(-18f, 0f, -10.5f), new Vector3(-18f, 1.6f, -6f), 2.8f, _m.steelGrate, false);
            Box("DeckParapet", west, new Vector3(-15.2f, 2.05f, 0f), new Vector3(0.45f, 0.9f, 5.2f), _m.concrete, LR2Layers.Default, SurfaceType.Concrete);
            Box("MantleTestBox", west, new Vector3(-12.5f, 0.425f, 0f), new Vector3(1.6f, 0.85f, 2.4f), _m.wood, LR2Layers.Default, SurfaceType.Wood);

            // ---- LANE 3 (East): Destructible cover, breakable glass & explosive barrel test lane
            Box("TestGlassPanelA", _structures, new Vector3(16f, 1.15f, 4f), new Vector3(3.2f, 2.2f, 0.06f), _m.glass, LR2Layers.Default, SurfaceType.Glass);
            Box("TestGlassPanelB", _structures, new Vector3(16f, 1.15f, -4f), new Vector3(3.2f, 2.2f, 0.06f), _m.glass, LR2Layers.Default, SurfaceType.Glass);
            Prop("SM_Jersey", _props, new Vector3(15.5f, 0f, 11f), 0f);
            Prop("SM_Jersey", _props, new Vector3(15.5f, 0f, -11f), 0f);
            Prop("SM_Crate_01", _props, new Vector3(18.5f, 0f, 7.5f), 0f);
            Prop("SM_Crate_02", _props, new Vector3(18.5f, 0f, -7.5f), 0f);
            Prop("SM_Barrel_01", _props, new Vector3(14f, 0f, 0f), 0f);
            Prop("SM_Gascylender", _props, new Vector3(18f, 0f, 0f), 0f);
            Prop("SM_Barrel_02", _props, new Vector3(22f, 0f, 0f), 0f);

            // Deterministic Spawns, TacticalPoints & AmmoCrates
            AddSpawn(new Vector3(0f, 0.05f, -25.5f), 0f, TeamId.Alpha, true, false);
            AddSpawn(new Vector3(-16f, 0.05f, -24.5f), 0f, TeamId.Alpha, true, false);
            AddSpawn(new Vector3(16f, 0.05f, -24.5f), 0f, TeamId.Alpha, true, false);
            AddSpawn(new Vector3(0f, 0.05f, 25.5f), 180f, TeamId.Bravo, true, false);
            AddSpawn(new Vector3(-16f, 0.05f, 24.5f), 180f, TeamId.Bravo, true, false);
            AddSpawn(new Vector3(16f, 0.05f, 24.5f), 180f, TeamId.Bravo, true, false);
            AddSpawn(new Vector3(-24f, 0.05f, 0f), 90f, TeamId.None, false, false);
            AddSpawn(new Vector3(24f, 0.05f, 0f), -90f, TeamId.None, false, false);

            AddTac(new Vector3(-18f, 1.65f, 0f), TacticalPointType.Overlook, 0.9f, false);
            AddTac(new Vector3(0f, 0.05f, 0f), TacticalPointType.Choke, 0.8f, false);
            AddTac(new Vector3(17f, 0.05f, 0f), TacticalPointType.Patrol, 0.7f, false);
            AddTac(new Vector3(0f, 0.05f, 14f), TacticalPointType.Patrol, 0.6f, false);
            AddTac(new Vector3(0f, 0.05f, -14f), TacticalPointType.Patrol, 0.6f, false);

            AddAmmoCrate(new Vector3(-14f, 0.01f, -22f));
            AddAmmoCrate(new Vector3(14f, 0.01f, 22f));

            // Crisp bright 6500K daylight (no fog clutter, ideal for bot & LLM visual debugging)
            BuildCustomLighting("LR2_TestRange_Profile", sunElev: 46f, sunAz: 215f, sunLux: 10000f, sunTemp: 6200f,
                fogMfp: 400f, fogColor: new Color(0.75f, 0.80f, 0.85f), exposureComp: 0.15f, contrast: 14f, saturation: -2f,
                rainDefaultPreset: -1);
            AddMatchSystems(30f);
            MarkStatic(env);

            EliminateSceneClipping("LR2_TestRange", _structures, _props, null, _spawns, _gameplay);
            SetupDestructibleProps(root);

            Physics.SyncTransforms();
            BakeNavMesh(root, "Assets/LR2/Scenes/LR2_TestRange_NavMesh.asset", false);
            GenerateCoverPoints(pMin, pMax, new[] { 0.4f, 1.85f });
            BuildMinimap("Assets/LR2/UI/Minimap/TestRange_Minimap.png", pMin, pMax);

            EditorSceneManager.SaveScene(scene, TestRangeScenePath);
            SetBuildScenes();
            Debug.Log("[LR2] Map built: " + TestRangeScenePath);
        }

        // =================================================================================================
        // Shared extra-map infrastructure helpers
        // =================================================================================================
        private static GameObject BeginExtraMap(string mapName, string configPath, int seed, out UnityEngine.SceneManagement.Scene scene)
        {
            _terrain = null;
            _cfg = LoadOrCreate<MapBuildConfig>(configPath);
            _cfg.seed = seed;
            _cfg.forest = false;
            EditorUtility.SetDirty(_cfg);
            _rnd = new System.Random(seed);
            ConfigureNavAgent();
            EnsureFolder("Assets/LR2/Scenes");
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var root = new GameObject(mapName);
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
            return root;
        }

        private static void SpawnBurningBarrel(Transform lightsParent, Transform propsParent, Vector3 pos)
        {
            var barrel = Prop("SM_Barrel_04", propsParent, pos, R(0f, 360f));
            var fireHolder = new GameObject("BurningBarrelFX");
            fireHolder.transform.SetParent(barrel != null ? barrel.transform : lightsParent, false);
            fireHolder.transform.position = (barrel != null ? barrel.transform.position : pos) + Vector3.up * 0.85f;

            void SpawnFx(string fxName)
            {
                foreach (var g in AssetDatabase.FindAssets(fxName + " t:Prefab", new[] { "Assets/LeartesStudios" }))
                {
                    var p = AssetDatabase.GUIDToAssetPath(g);
                    if (System.IO.Path.GetFileNameWithoutExtension(p) != fxName) continue;
                    var pf = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                    if (pf == null) continue;
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(pf, fireHolder.transform);
                    inst.transform.localPosition = Vector3.zero;
                    inst.transform.localScale = Vector3.one * 0.42f;
                    SanitizeParticleSystems(inst);
                    break;
                }
            }
            SpawnFx("Fire");
            SpawnFx("Smoke");

            var l = AddLight(fireHolder.transform, "FireGlow", Vector3.up * 0.35f, Quaternion.identity, LightType.Point, 2400f, 2600f, 9f, 0f, false);
            l.gameObject.AddComponent<LightFlicker>();
        }

        private static void SanitizeParticleSystems(GameObject go)
        {
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                var vel = ps.velocityOverLifetime;
                if (!vel.enabled) continue;
                var m = vel.y.mode;
                if (vel.x.mode != m || vel.z.mode != m)
                {
                    if (m == ParticleSystemCurveMode.TwoConstants)
                    {
                        vel.x = new ParticleSystem.MinMaxCurve(vel.x.constantMin, vel.x.constantMax);
                        vel.z = new ParticleSystem.MinMaxCurve(vel.z.constantMin, vel.z.constantMax);
                    }
                    else
                    {
                        vel.x = new ParticleSystem.MinMaxCurve(vel.x.constant);
                        vel.y = new ParticleSystem.MinMaxCurve(vel.y.constant);
                        vel.z = new ParticleSystem.MinMaxCurve(vel.z.constant);
                    }
                }
            }
        }

        private static void BuildCustomLighting(string profileName, float sunElev, float sunAz, float sunLux, float sunTemp,
            float fogMfp, Color fogColor, float exposureComp, float contrast, float saturation, int rainDefaultPreset)
        {
            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(_lighting, false);
            sunGo.transform.rotation = Quaternion.Euler(sunElev, sunAz, 0f);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.lightUnit = LightUnit.Lux;
            sun.intensity = sunLux;
            sun.useColorTemperature = true;
            sun.colorTemperature = sunTemp;
            sun.color = Color.white;
            sun.shadows = LightShadows.Soft;
            var hdSun = sunGo.AddComponent<HDAdditionalLightData>();
            hdSun.angularDiameter = 1.6f;
            hdSun.SetShadowResolutionOverride(true);
            hdSun.SetShadowResolution(4096);
            RenderSettings.sun = sun;

            var profile = NewProfile($"{LightDir}/{profileName}.asset");
            var ve = profile.Add<VisualEnvironment>(true);
            ve.skyType.Override((int)SkyType.PhysicallyBased);
            ve.skyAmbientMode.Override(SkyAmbientMode.Dynamic);
            var sky = profile.Add<PhysicallyBasedSky>(true);
            sky.aerosolDensity.Override(rainDefaultPreset >= 0 ? 0.28f : 0.10f);
            var fog = profile.Add<Fog>(true);
            fog.enabled.Override(rainDefaultPreset >= 0);
            fog.meanFreePath.Override(fogMfp);
            fog.baseHeight.Override(0f);
            fog.maximumHeight.Override(32f);
            fog.enableVolumetricFog.Override(rainDefaultPreset >= 0);
            fog.albedo.Override(fogColor);
            fog.anisotropy.Override(0.7f);
            fog.depthExtent.Override(90f);
            var exposure = profile.Add<Exposure>(true);
            exposure.mode.Override(ExposureMode.AutomaticHistogram);
            exposure.compensation.Override(exposureComp);
            exposure.limitMin.Override(4.0f);
            exposure.limitMax.Override(12.8f);
            exposure.adaptationMode.Override(AdaptationMode.Fixed);
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.32f);
            bloom.threshold.Override(1.0f);
            var color = profile.Add<ColorAdjustments>(true);
            color.contrast.Override(contrast);
            color.saturation.Override(saturation);
            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(rainDefaultPreset >= 0 ? 0.25f : 0.14f);
            var ao = profile.Add<ScreenSpaceAmbientOcclusion>(true);
            ao.intensity.Override(0.65f);
            ao.radius.Override(1.4f);
            profile.Add<ScreenSpaceReflection>(true).enabled.Override(true);
            profile.Add<GlobalIllumination>(true).enable.Override(false);
            profile.Add<ContactShadows>(true).enable.Override(true);
            profile.Add<MicroShadowing>(true).enable.Override(true);
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

            if (rainDefaultPreset >= 0)
            {
                var wsGo = new GameObject("Weather");
                wsGo.transform.SetParent(_systems, false);
                var ws = wsGo.AddComponent<WeatherSystem>();
                ws.sun = sun;
                ws.globalVolume = v;
                ws.startPreset = rainDefaultPreset;
                ws.randomStart = false;
                ws.dynamicCycle = true;
            }
        }

        private static void PlaceMapDecals(Vector2 playMin, Vector2 playMax, int tries)
        {
            string[] names = { "M_CrackDecal", "M_CrackDecal_2", "M_LeakDecal", "M_RustDecal", "M_GraffittiDecal", "M_Decal_Puddle_A_MIC" };
            var mats = new List<Material>();
            foreach (var n in names)
            {
                var m = FindLeartes(n);
                if (m != null) mats.Add(m);
            }
            if (mats.Count == 0) return;
            for (int i = 0; i < tries; i++)
            {
                Vector3 o = new Vector3(R(playMin.x, playMax.x), 11f, R(playMin.y, playMax.y));
                Vector3 dir = i % 3 == 0 ? Vector3.down : new Vector3(R(-1f, 1f), -0.15f, R(-1f, 1f)).normalized;
                if (i % 3 != 0) o.y = R(0.6f, 3.4f);
                if (!Physics.Raycast(o, dir, out var hit, 25f, LR2Layers.EnvironmentMask)) continue;
                var mat = mats[i % mats.Count];
                bool floorDecal = hit.normal.y > 0.7f;
                if (mat.name.Contains("Puddle") && !floorDecal) continue;
                var go = new GameObject("Decal_" + mat.name);
                go.transform.SetParent(_decals, false);
                go.transform.position = hit.point + hit.normal * 0.05f;
                go.transform.rotation = Quaternion.LookRotation(-hit.normal, floorDecal ? Vector3.forward : Vector3.up) * Quaternion.Euler(0f, 0f, R(0f, 360f));
                var dp = go.AddComponent<DecalProjector>();
                dp.material = mat;
                float s = floorDecal ? R(1.6f, 3.6f) : R(0.9f, 2.2f);
                dp.size = new Vector3(s, s, 0.35f);
                dp.drawDistance = 80f;
            }
        }
    }
}
