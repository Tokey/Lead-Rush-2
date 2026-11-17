using System.Collections.Generic;
using LR2.Audio;
using LR2.Combat;
using LR2.Core;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace LR2.FX
{
    /// <summary>Bullet impacts, bullet-hole decals (HDRP DecalProjector ring buffer), blood and footsteps.</summary>
    public static class ImpactSystem
    {
        private static readonly List<DecalProjector> Decals = new List<DecalProjector>(128);
        private static readonly List<float> DecalTimes = new List<float>(128);
        private static int _nextDecal;
        private static Transform _decalRoot;
        private static float _lastImpactSoundTime;

        public static void SpawnBulletImpact(Vector3 point, Vector3 normal, Vector3 direction, SurfaceType surface, bool exitHole)
        {
            var lib = SurfaceLibrary.Instance;
            if (lib == null) return;
            var fx = lib.Get(surface);
            var rot = Quaternion.LookRotation(normal.sqrMagnitude > 0.01f ? normal : -direction);

            if (fx.impactPrefab != null) LR2Pool.Spawn(fx.impactPrefab, point + normal * 0.01f, rot, 2.5f);
            if (fx.decalMaterial != null) PlaceDecal(fx.decalMaterial, point, normal, Random.Range(fx.decalSize.x, fx.decalSize.y) * (exitHole ? 1.3f : 1f), lib);

            // Avoid machine-gun audio spam: at most ~30 impact sounds per second.
            if (Time.time - _lastImpactSoundTime > 0.033f)
            {
                _lastImpactSoundTime = Time.time;
                LR2Audio.PlayAt(fx.impactSound, point, exitHole ? 0.6f : 1f);
            }
        }

        public static void SpawnBlood(Vector3 point, Vector3 normal, Vector3 direction, bool headshot)
        {
            var lib = SurfaceLibrary.Instance;
            if (lib == null) return;
            var prefab = headshot && lib.bloodHeadshotPrefab != null ? lib.bloodHeadshotPrefab : lib.bloodPrefab;
            if (prefab != null)
            {
                Vector3 spawnPos = point + (normal.sqrMagnitude > 0.01f ? normal : -direction) * 0.05f;
                LR2Pool.Spawn(prefab, spawnPos, Quaternion.LookRotation(-direction), 2.2f);
            }
            var set = headshot && lib.headshotImpact.IsValid ? lib.headshotImpact : lib.fleshImpact;
            LR2Audio.PlayAt(set, point, 1.35f);

            // Blood spatter on the wall behind the victim.
            if (lib.bloodDecal != null && Physics.Raycast(point, direction, out var wall, 2.5f, LR2Layers.EnvironmentMask, QueryTriggerInteraction.Ignore))
                PlaceDecal(lib.bloodDecal, wall.point, wall.normal, Random.Range(0.45f, 0.85f), lib);
        }

        public static void PlayFootstep(Vector3 position, SurfaceType surface, float intensity, bool localPlayer)
        {
            var lib = SurfaceLibrary.Instance;
            if (lib == null) return;
            var fx = lib.Get(surface);
            var set = fx.footstepSound.IsValid ? fx.footstepSound : lib.fallback.footstepSound;
            float vol = Mathf.Clamp01(intensity) * (localPlayer ? lib.localFootstepVolume : 1f);
            LR2Audio.PlayAt(set, position, vol);
        }

        private static void PlaceDecal(Material material, Vector3 point, Vector3 normal, float size, SurfaceLibrary lib)
        {
            if (_decalRoot == null)
            {
                _decalRoot = new GameObject("[LR2 Decals]").transform;
                Decals.Clear();
                DecalTimes.Clear();
                _nextDecal = 0;
            }

            DecalProjector projector;
            if (Decals.Count < lib.maxDecals)
            {
                var go = new GameObject("Decal");
                go.transform.SetParent(_decalRoot, false);
                projector = go.AddComponent<DecalProjector>();
                Decals.Add(projector);
                DecalTimes.Add(0f);
                _nextDecal = Decals.Count - 1;
            }
            else
            {
                _nextDecal = (_nextDecal + 1) % Decals.Count;
                projector = Decals[_nextDecal];
                if (projector == null)
                {
                    var go = new GameObject("Decal");
                    go.transform.SetParent(_decalRoot, false);
                    projector = go.AddComponent<DecalProjector>();
                    Decals[_nextDecal] = projector;
                }
            }

            var t = projector.transform;
            t.position = point + normal * 0.02f;
            t.rotation = Quaternion.LookRotation(-normal, Mathf.Abs(normal.y) > 0.9f ? Vector3.forward : Vector3.up) *
                         Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            projector.material = material;
            projector.size = new Vector3(size, size, 0.12f);
            projector.pivot = Vector3.zero;
            projector.drawDistance = 60f;
            projector.fadeScale = 0.9f;
            projector.fadeFactor = 1f;
            projector.gameObject.SetActive(true);
            DecalTimes[_nextDecal] = Time.time;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Decals.Clear();
            DecalTimes.Clear();
            _nextDecal = 0;
            _decalRoot = null;
            _lastImpactSoundTime = 0f;
        }
    }
}
