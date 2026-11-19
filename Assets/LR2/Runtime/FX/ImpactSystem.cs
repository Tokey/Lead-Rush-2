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
            if (surface == SurfaceType.Metal && !exitHole) LR2.World.MapAmbientFx.Instance?.OnMetalHit(point, normal); // B112 pipe steam
            var lib = SurfaceLibrary.Instance;
            if (lib == null) return;
            var fx = lib.Get(surface);
            var rot = Quaternion.LookRotation(normal.sqrMagnitude > 0.01f ? normal : -direction);

            if (fx.impactPrefab != null) LR2Pool.Spawn(fx.impactPrefab, point + normal * 0.01f, rot, 2.5f);
            if (fx.decalMaterial != null && SurfaceDecalPolicy.ShouldProject(surface))
                PlaceDecal(fx.decalMaterial, point, normal, Random.Range(fx.decalSize.x, fx.decalSize.y) * (exitHole ? 1.3f : 1f), SurfaceDecalPolicy.Depth(surface), lib);

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
                PlaceDecal(lib.bloodDecal, wall.point, wall.normal, Random.Range(0.45f, 0.85f), 0.12f, lib);
        }

        public static void PlayFootstep(Vector3 position, SurfaceType surface, float intensity, bool localPlayer)
        {
            var lib = SurfaceLibrary.Instance;
            if (lib == null) return;
            var fx = lib.Get(surface);
            var set = fx.footstepSound.IsValid ? fx.footstepSound : lib.fallback.footstepSound;
            float vol = Mathf.Clamp01(intensity) * (localPlayer ? lib.localFootstepVolume : 1f);
            LR2Audio.PlayAt(set, position, vol);
            FootstepParticles(position, surface, intensity);
        }

        // ------------------------------------------------------------------ B97 footstep dust and splashes
        // User 11-19 00:27: "add more particles, like footstep dust, or splash while raining".
        private static ParticleSystem _footDust;
        private static ParticleSystem.EmitParams _dustEmit;

        private static void FootstepParticles(Vector3 position, SurfaceType surface, float intensity)
        {
            var cam = Camera.main;
            if (cam == null || (position - cam.transform.position).sqrMagnitude > 35f * 35f) return;
            var weather = LR2.World.WeatherSystem.Instance;
            if (weather != null && weather.Wetness > 0.25f && !LR2Audio.IsIndoors(position + Vector3.up * 0.4f))
            {
                weather.EmitFootSplash(position, intensity);
                return;
            }
            if (surface != SurfaceType.Dirt && surface != SurfaceType.Concrete && surface != SurfaceType.Default) return;
            if (_footDust == null) _footDust = CreateFootDust();
            if (_footDust == null) return;
            Color c = surface == SurfaceType.Dirt ? new Color(0.47f, 0.40f, 0.31f, 0.42f) : new Color(0.56f, 0.54f, 0.5f, 0.3f);
            int n = intensity > 0.85f ? 3 : 2;
            for (int i = 0; i < n; i++)
            {
                _dustEmit.position = position + new Vector3(Random.Range(-0.12f, 0.12f), 0.05f, Random.Range(-0.12f, 0.12f));
                _dustEmit.velocity = new Vector3(Random.Range(-0.35f, 0.35f), Random.Range(0.1f, 0.32f), Random.Range(-0.35f, 0.35f)) * (0.6f + intensity);
                _dustEmit.startSize = Random.Range(0.16f, 0.32f) * (0.7f + 0.5f * intensity);
                _dustEmit.startLifetime = Random.Range(0.7f, 1.2f);
                _dustEmit.startColor = c;
                _dustEmit.rotation = Random.Range(0f, 360f);
                _footDust.Emit(_dustEmit, 1);
            }
        }

        private static ParticleSystem CreateFootDust()
        {
            var lib = LR2FxLibrary.Instance;
            if (lib == null || lib.smoke == null) return null;
            var go = new GameObject("[LR2 Foot Dust]");
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 320;
            main.gravityModifier = -0.01f;
            var em = ps.emission;
            em.enabled = false;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 1.8f)));
            var lv = ps.limitVelocityOverLifetime;
            lv.enabled = true;
            lv.drag = 2.5f;
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
            LR2FxLibrary.ApplyFlipbook(ps, lib.smokeTiles);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = lib.smoke;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        private static void PlaceDecal(Material material, Vector3 point, Vector3 normal, float size, float depth, SurfaceLibrary lib)
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
            projector.size = new Vector3(size, size, depth);
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
