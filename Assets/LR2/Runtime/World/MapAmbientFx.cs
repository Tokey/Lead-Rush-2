using System.Collections.Generic;
using LR2.Core;
using LR2.FX;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LR2.World
{
    /// <summary>
    /// B112 (user 11-19: "use more of the unity particle effects throuout the entire map ... dont spam them, let them be
    /// tasteful choices and serve purpose"). Per map, chosen the same way every time (seeded by the scene name):
    /// up to three standing steam leaks on pipe runs near eye level (15 m apart), up to two shorting light fittings that
    /// spark every 5-14 s and dip their light, and a short steam jet wherever a bullet punctures a pipe.
    /// </summary>
    public class MapAmbientFx : MonoBehaviour
    {
        public static MapAmbientFx Instance { get; private set; }

        private class Fixture
        {
            public Light light;
            public float baseIntensity, nextSpark, dipUntil;
        }

        private readonly List<Fixture> _fixtures = new List<Fixture>();
        /// <summary>Where the standing leaks are (point, surface normal) and the sparking fittings (media capture).</summary>
        public readonly List<(Vector3 point, Vector3 normal)> SteamSpots = new List<(Vector3, Vector3)>();
        public readonly List<Vector3> SparkSpots = new List<Vector3>();

        /// <summary>Every sparking fitting shorts out now.</summary>
        public void SparkNow() { foreach (var f in _fixtures) f.nextSpark = Time.time; }
        private readonly List<GameObject> _jets = new List<GameObject>();
        private float _nextJetAt;
        private System.Random _rng;

        public static void Ensure()
        {
            if (Instance == null) new GameObject("[LR2 AmbientFx]").AddComponent<MapAmbientFx>();
        }

        private void Awake() => Instance = this;
        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void Start()
        {
            var lib = LR2FxLibrary.Instance;
            if (lib == null) return;
            _rng = new System.Random(SceneManager.GetActiveScene().name.GetHashCode());
            if (lib.packSteamStatic != null) PlaceSteamLeaks(lib.packSteamStatic);
            if (lib.packSparks != null) PickFixtures();
        }

        private static bool IsPipe(Transform t)
        {
            for (int i = 0; i < 3 && t != null; i++, t = t.parent)
                if (t.name.ToLowerInvariant().Contains("pipe")) return true;
            return false;
        }

        private void Shuffle<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        private void PlaceSteamLeaks(GameObject prefab)
        {
            var pipes = new List<Renderer>();
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.InstanceID))
                if (r.enabled && IsPipe(r.transform) && r.bounds.size.magnitude > 1.2f) pipes.Add(r);
            Shuffle(pipes);
            var placed = new List<Vector3>();
            foreach (var r in pipes)
            {
                if (placed.Count >= 3) break;
                Bounds b = r.bounds;
                Vector3 axis = b.size.x >= b.size.z ? Vector3.right : Vector3.forward;
                if (b.size.y > Mathf.Max(b.size.x, b.size.z)) axis = Vector3.up;
                float along = (float)_rng.NextDouble() * 1.2f - 0.6f;
                Vector3 p = b.center + axis * (Vector3.Dot(b.extents, axis) * along);
                Vector3 side = Vector3.Cross(axis, Vector3.up);
                if (side.sqrMagnitude < 0.01f) side = Vector3.right;
                side = side.normalized * (_rng.NextDouble() < 0.5 ? -1f : 1f);
                float reach = b.extents.magnitude + 0.5f;
                if (!Physics.Raycast(p + side * reach, -side, out var hit, reach + 0.5f, LR2Layers.EnvironmentMask, QueryTriggerInteraction.Ignore)) continue;
                if (!IsPipe(hit.collider.transform)) continue;
                if (hit.point.y < 0.3f || hit.point.y > 4.5f) continue; // where a soldier sees it
                bool crowded = false;
                foreach (var q in placed) if ((q - hit.point).sqrMagnitude < 15f * 15f) crowded = true;
                if (crowded) continue;
                placed.Add(hit.point);
                SteamSpots.Add((hit.point, hit.normal));
                var go = Instantiate(prefab, hit.point + hit.normal * 0.03f, Quaternion.LookRotation((hit.normal + Vector3.up * 0.4f).normalized), transform);
                go.name = "SteamLeak";
                PackFx.Prepare(go, 0.55f);
            }
        }

        private void PickFixtures()
        {
            var lights = new List<Light>();
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.InstanceID))
            {
                if (l.type == LightType.Directional || !l.enabled) continue;
                string n = (l.name + " " + (l.transform.parent != null ? l.transform.parent.name : "")).ToLowerInvariant();
                if (!(n.Contains("lamp") || n.Contains("bulb") || n.Contains("fixture") || n.Contains("practical") || n.Contains("light"))) continue;
                if (n.Contains("fire") || n.Contains("zone") || n.Contains("muzzle") || n.Contains("laser") || n.Contains("lightning") || n.Contains("flash")) continue;
                lights.Add(l);
            }
            Shuffle(lights);
            foreach (var l in lights)
            {
                if (_fixtures.Count >= 2) break;
                bool crowded = false;
                foreach (var f in _fixtures) if ((f.light.transform.position - l.transform.position).sqrMagnitude < 20f * 20f) crowded = true;
                if (crowded) continue;
                _fixtures.Add(new Fixture { light = l, baseIntensity = l.intensity, nextSpark = Time.time + 3f + (float)_rng.NextDouble() * 8f });
                SparkSpots.Add(l.transform.position);
            }
        }

        private void Update()
        {
            var lib = LR2FxLibrary.Instance;
            float now = Time.time;
            foreach (var f in _fixtures)
            {
                if (f.light == null) continue;
                if (now >= f.nextSpark && lib != null && lib.packSparks != null)
                {
                    PackFx.Spawn(lib.packSparks, f.light.transform.position - Vector3.up * 0.12f, Quaternion.LookRotation(Vector3.down), 0.5f, 2.5f);
                    f.dipUntil = now + 0.28f;
                    f.nextSpark = now + 5f + Random.value * 9f;
                }
                // the fitting browns out while it shorts, with a stutter
                f.light.intensity = now < f.dipUntil ? f.baseIntensity * (Random.value < 0.5f ? 0.15f : 0.6f) : f.baseIntensity;
            }
            _jets.RemoveAll(j => j == null || !j.activeInHierarchy);
        }

        /// <summary>From ImpactSystem: a round hit metal. If it was a pipe, it vents a short jet of steam along the normal.</summary>
        public void OnMetalHit(Vector3 point, Vector3 normal)
        {
            var lib = LR2FxLibrary.Instance;
            if (lib == null || lib.packSteam == null || Time.time < _nextJetAt || _jets.Count >= 4) return;
            var cols = Physics.OverlapSphere(point, 0.06f, LR2Layers.EnvironmentMask, QueryTriggerInteraction.Ignore);
            bool pipe = false;
            foreach (var c in cols) if (c != null && IsPipe(c.transform)) pipe = true;
            if (!pipe) return;
            _nextJetAt = Time.time + 0.35f;
            var go = PackFx.Spawn(lib.packSteam, point + normal * 0.02f, Quaternion.LookRotation(normal), 0.45f, 2.6f);
            if (go != null) _jets.Add(go);
        }
    }
}
