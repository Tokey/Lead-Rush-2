using LR2.Characters;
using LR2.Combat;
using LR2.Core;
using LR2.FX;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace LR2.World
{
    /// <summary>
    /// Trigger-volume fire hazard. Applies damage through the normal Health path and creates a
    /// self-contained flame, smoke, flickering-light, and optional crackle-loop presentation.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class FireHazard : MonoBehaviour
    {
        [Header("Damage")]
        [SerializeField, Min(0f)] private float damagePerSecond = 12f;
        [SerializeField, Min(0.05f)] private float damageInterval = 0.25f;
        [SerializeField, Min(0f)] private float damageRadius = 1.8f;

        [Header("Presentation")]
        [SerializeField, Min(0f)] private float lightIntensity = 1000f;
        [SerializeField, Min(0f)] private float lightRange = 5f;
        [SerializeField, Range(0f, 1f)] private float flickerAmount = 0.18f;
        [SerializeField, Range(0f, 1f)] private float smokeAmount = 0.7f;
        [SerializeField] private AudioClip crackleLoop;

        private readonly Collider[] _overlaps = new Collider[16];
        private float _nextDamageAt;
        private float _lightBaseIntensity;
        private float _seed;
        private Light _flameLight;
        private ParticleSystem _flameParticles;
        private ParticleSystem _smokeParticles;
        private AudioSource _crackle;

        private void Reset()
        {
            var collider = GetComponent<Collider>();
            collider.isTrigger = true;
            if (collider is SphereCollider sphere)
                sphere.radius = damageRadius;
        }

        private void Awake()
        {
            var trigger = GetComponent<Collider>();
            trigger.isTrigger = true;
            _seed = Random.value * 100f;
            BuildPresentation();
        }

        private void OnEnable()
        {
            _nextDamageAt = Time.time;
            if (_flameParticles != null) _flameParticles.Play();
            if (_smokeParticles != null) _smokeParticles.Play();
            if (_crackle != null) _crackle.Play();
        }

        private void OnDisable()
        {
            if (_flameParticles != null) _flameParticles.Stop();
            if (_smokeParticles != null) _smokeParticles.Stop();
            if (_crackle != null) _crackle.Stop();
        }

        private void Update()
        {
            if (_flameLight != null)
            {
                float noise = Mathf.PerlinNoise(Time.time * 8f, _seed);
                float second = Mathf.PerlinNoise(Time.time * 13f + 17f, _seed);
                _flameLight.intensity = _lightBaseIntensity *
                    Mathf.Clamp01(1f - flickerAmount + flickerAmount * (noise * 0.65f + second * 0.35f));
            }

            if (Time.time < _nextDamageAt) return;
            _nextDamageAt = Time.time + damageInterval;
            ApplyDamageTick();
        }

        private void ApplyDamageTick()
        {
            int count = Physics.OverlapSphereNonAlloc(
                transform.position, damageRadius, _overlaps, LR2Layers.CharacterMask, QueryTriggerInteraction.Ignore);

            float amount = damagePerSecond * damageInterval;
            var damaged = new System.Collections.Generic.HashSet<SoldierController>();
            for (int i = 0; i < count; i++)
            {
                if (_overlaps[i] == null) continue;
                var soldier = _overlaps[i].GetComponentInParent<SoldierController>();
                if (soldier == null || soldier.IsDead || soldier.Health == null || !damaged.Add(soldier)) continue;

                var combatant = soldier.Combatant;
                Vector3 point = combatant != null ? combatant.ChestPosition : soldier.transform.position + Vector3.up * 1.2f;
                var info = new DamageInfo
                {
                    amount = amount,
                    type = DamageType.Environment,
                    zone = HitZone.LowerTorso,
                    attacker = null,
                    victim = combatant,
                    source = gameObject,
                    weaponName = "Fire",
                    point = point,
                    normal = (point - transform.position).normalized,
                    direction = Vector3.up,
                    force = 0f
                };
                soldier.Health.ApplyDamage(ref info);
            }
        }

        private void BuildPresentation()
        {
            var lightGo = new GameObject("Fire Light");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.localPosition = Vector3.up * Mathf.Max(0.35f, damageRadius * 0.45f);
            _flameLight = lightGo.AddComponent<Light>();
            _flameLight.type = LightType.Point;
            _flameLight.color = new Color(1f, 0.42f, 0.12f);
            _flameLight.range = lightRange;
            _lightBaseIntensity = lightIntensity;
            _flameLight.intensity = _lightBaseIntensity;
            _flameLight.shadows = LightShadows.None;
            _flameLight.lightUnit = LightUnit.Lumen;
            _ = lightGo.GetComponent<HDAdditionalLightData>() ?? lightGo.AddComponent<HDAdditionalLightData>();

            var lib = LR2FxLibrary.Instance;
            if (lib != null && lib.fire != null && lib.smoke != null)
            {
                BuildParticlePackFire(lib);
            }
            else
            {
                _flameParticles = BuildParticles("Fire Flame", new Color(1f, 0.32f, 0.03f, 0.9f),
                    new Color(1f, 0.85f, 0.18f, 0.1f), 0.16f, 0.55f, 46f);
                _smokeParticles = BuildParticles("Fire Smoke", new Color(0.12f, 0.12f, 0.12f, 0.48f),
                    new Color(0.04f, 0.04f, 0.04f, 0f), 0.5f, 1.8f, 18f * smokeAmount);
                var hazeGo = new GameObject("Fire Heat Haze");
                hazeGo.transform.SetParent(transform, false);
                hazeGo.transform.localPosition = Vector3.up * Mathf.Max(0.25f, damageRadius * 0.28f);
                hazeGo.AddComponent<HeatHazeEmitter>().Configure(
                    new Vector2(Mathf.Max(0.35f, damageRadius * 0.38f), Mathf.Max(0.8f, damageRadius * 1.5f)), 0.22f);
            }

            if (crackleLoop == null)
            {
                crackleLoop = BuildFallbackCrackleClip();
            }
            if (crackleLoop != null)
            {
                var audioGo = new GameObject("Fire Crackle");
                audioGo.transform.SetParent(transform, false);
                _crackle = audioGo.AddComponent<AudioSource>();
                _crackle.clip = crackleLoop;
                _crackle.loop = true;
                _crackle.spatialBlend = 1f;
                _crackle.minDistance = 2f;
                _crackle.maxDistance = Mathf.Max(12f, lightRange * 2f);
                _crackle.volume = 0.45f;
            }
        }

        // ------------------------------------------------------------------ B87 Particle Pack style fire
        // User 11-19 00:13: "please fix the fire, use unitys fire particle effects instead. the heat effect should also be particles
        // ... the unity one has the heat effect already use that". The project has the Unity Particle Pack's fire textures but not
        // its fire prefab, so this rebuilds that layered fire from the pack's own flipbooks: rolling flipbook flames with noise,
        // a hot base glow, turbulent embers, a rising smoke column, and the heat shimmer as distortion particles (the old single
        // distortion billboard read as a blurry blob). Everything scales with the hazard radius.

        private ParticleSystem NewSystem(string name, Vector3 localPos, Material mat, float sortFudge)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortingFudge = sortFudge;
            r.maxParticleSize = 1.5f;
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            return ps;
        }

        private static ParticleSystem.MinMaxCurve Curve(params (float t, float v)[] keys)
        {
            var c = new AnimationCurve();
            foreach (var k in keys) c.AddKey(k.t, k.v);
            return new ParticleSystem.MinMaxCurve(1f, c);
        }

        private static Gradient Grad((float t, Color c)[] colors, (float t, float a)[] alphas)
        {
            var g = new Gradient();
            var ck = new GradientColorKey[colors.Length];
            for (int i = 0; i < colors.Length; i++) ck[i] = new GradientColorKey(colors[i].c, colors[i].t);
            var ak = new GradientAlphaKey[alphas.Length];
            for (int i = 0; i < alphas.Length; i++) ak[i] = new GradientAlphaKey(alphas[i].a, alphas[i].t);
            g.SetKeys(ck, ak);
            return g;
        }

        private static void UpCone(ParticleSystem ps, float angle, float radius)
        {
            var sh = ps.shape;
            sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = angle;
            sh.radius = radius;
            sh.rotation = new Vector3(-90f, 0f, 0f); // the cone points along +Z; tilt it to point up
        }

        private void BuildParticlePackFire(LR2FxLibrary lib)
        {
            float s = Mathf.Clamp(damageRadius * 0.55f, 0.35f, 1.6f); // flame scale (m)

            // 1. flames: the pack's fire flipbook, rising, licking with noise, white-yellow -> orange -> deep red
            var fl = NewSystem("Fire Flames", new Vector3(0f, 0.04f * s, 0f), lib.fire, -1f);
            var m = fl.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 0.95f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.25f + 0.35f * s, 0.45f + 0.8f * s);
            m.startSize = new ParticleSystem.MinMaxCurve(0.55f * s, 1.05f * s);
            m.startRotation = new ParticleSystem.MinMaxCurve(-0.35f, 0.35f);
            m.startColor = Color.white;
            m.gravityModifier = -0.22f;
            m.maxParticles = 140;
            var em = fl.emission;
            em.rateOverTime = 28f;
            UpCone(fl, 6f, 0.32f * s);
            var col = fl.colorOverLifetime;
            col.enabled = true;
            col.color = Grad(new[] { (0f, new Color(1f, 0.95f, 0.8f)), (0.25f, new Color(1f, 0.78f, 0.42f)), (0.55f, new Color(1f, 0.5f, 0.14f)), (1f, new Color(0.55f, 0.13f, 0.03f)) },
                             new[] { (0f, 0f), (0.08f, 1f), (0.5f, 0.85f), (0.8f, 0.4f), (1f, 0f) });
            var sz = fl.sizeOverLifetime;
            sz.enabled = true;
            sz.size = Curve((0f, 0.55f), (0.35f, 1f), (1f, 0.45f));
            var rot = fl.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-0.7f, 0.7f);
            var nz = fl.noise;
            nz.enabled = true;
            nz.strength = 0.35f * s;
            nz.frequency = 1.1f;
            nz.scrollSpeed = 1.2f;
            nz.damping = true;
            nz.octaveCount = 2;
            nz.quality = ParticleSystemNoiseQuality.Medium;
            LR2FxLibrary.ApplyFlipbook(fl, lib.fireTiles);
            _flameParticles = fl;

            // 2. hot base glow (soft additive disc that flickers with the light)
            var glowMat = lib.fireGlow != null ? lib.fireGlow : lib.embers;
            if (glowMat != null)
            {
                var gl = NewSystem("Fire Glow", new Vector3(0f, 0.28f * s, 0f), glowMat, -2f);
                var gm = gl.main;
                gm.startLifetime = new ParticleSystem.MinMaxCurve(0.22f, 0.4f);
                gm.startSpeed = 0f;
                gm.startSize = new ParticleSystem.MinMaxCurve(1.5f * s, 2.2f * s);
                gm.startColor = new Color(1f, 0.46f, 0.12f, 0.32f);
                gm.maxParticles = 16;
                var ge = gl.emission;
                ge.rateOverTime = 16f;
                var gsh = gl.shape;
                gsh.enabled = true;
                gsh.shapeType = ParticleSystemShapeType.Sphere;
                gsh.radius = 0.08f * s;
                var gc = gl.colorOverLifetime;
                gc.enabled = true;
                gc.color = Grad(new[] { (0f, Color.white), (1f, Color.white) }, new[] { (0f, 0f), (0.4f, 1f), (1f, 0f) });
            }

            // 3. embers: tiny sparks thrown up with strong turbulence, drawn as short streaks
            if (lib.embers != null)
            {
                var eb = NewSystem("Fire Embers", new Vector3(0f, 0.2f * s, 0f), lib.embers, -3f);
                var me = eb.main;
                me.startLifetime = new ParticleSystem.MinMaxCurve(1.0f, 2.4f);
                me.startSpeed = new ParticleSystem.MinMaxCurve(1.0f + 0.6f * s, 2.4f + 1.2f * s);
                me.startSize = new ParticleSystem.MinMaxCurve(0.018f, 0.045f);
                me.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.45f), new Color(1f, 0.5f, 0.1f));
                me.gravityModifier = -0.12f;
                me.maxParticles = 70;
                var ee = eb.emission;
                ee.rateOverTime = 8f * s + 3f;
                ee.SetBursts(new[] { new ParticleSystem.Burst(0.4f, 3, 6, -1, 1.3f) });
                UpCone(eb, 18f, 0.35f * s);
                var en = eb.noise;
                en.enabled = true;
                en.strength = 1.1f;
                en.frequency = 2.2f;
                en.scrollSpeed = 1.5f;
                en.quality = ParticleSystemNoiseQuality.Medium;
                var ec = eb.colorOverLifetime;
                ec.enabled = true;
                ec.color = Grad(new[] { (0f, Color.white), (0.6f, new Color(1f, 0.6f, 0.25f)), (1f, new Color(0.8f, 0.2f, 0.05f)) },
                                new[] { (0f, 1f), (0.7f, 0.8f), (1f, 0f) });
                var er = eb.GetComponent<ParticleSystemRenderer>();
                er.renderMode = ParticleSystemRenderMode.Stretch;
                er.velocityScale = 0.025f;
                er.lengthScale = 1.6f;
            }

            // 4. smoke column: the pack's steam flipbook, dark grey, growing and drifting
            var sm = NewSystem("Fire Smoke", new Vector3(0f, 0.75f * s + 0.15f, 0f), lib.smoke, 2f);
            var ms = sm.main;
            ms.startLifetime = new ParticleSystem.MinMaxCurve(2.8f, 4.6f);
            ms.startSpeed = new ParticleSystem.MinMaxCurve(0.45f, 1.05f);
            ms.startSize = new ParticleSystem.MinMaxCurve(0.9f * s, 1.5f * s);
            ms.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            ms.startColor = new ParticleSystem.MinMaxGradient(new Color(0.14f, 0.13f, 0.12f, 0.6f), new Color(0.07f, 0.07f, 0.07f, 0.5f));
            ms.gravityModifier = -0.05f;
            ms.maxParticles = 90;
            var se = sm.emission;
            se.rateOverTime = 7f * Mathf.Max(0.2f, smokeAmount);
            UpCone(sm, 12f, 0.35f * s);
            var ssz = sm.sizeOverLifetime;
            ssz.enabled = true;
            ssz.size = Curve((0f, 0.6f), (1f, 2.3f));
            var sc = sm.colorOverLifetime;
            sc.enabled = true;
            sc.color = Grad(new[] { (0f, Color.white), (1f, Color.white) }, new[] { (0f, 0f), (0.12f, 1f), (0.6f, 0.6f), (1f, 0f) });
            var sr = sm.rotationOverLifetime;
            sr.enabled = true;
            sr.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
            var sn = sm.noise;
            sn.enabled = true;
            sn.strength = 0.4f;
            sn.frequency = 0.6f;
            sn.scrollSpeed = 0.4f;
            var sx = sm.externalForces;
            sx.enabled = true;
            LR2FxLibrary.ApplyFlipbook(sm, lib.smokeTiles);
            _smokeParticles = sm;

            // 5. heat shimmer as particles: the distortion material on rising quads that grow in and out (no fade per particle in
            //    HDRP/Unlit distortion, so size does it), much subtler than one big billboard
            if (lib.heatHaze != null)
            {
                var hazeMat = new Material(lib.heatHaze) { name = "LR2 Fire Heat Shimmer", hideFlags = HideFlags.HideAndDontSave };
                var vmap = HeatHazeEmitter.ParticleVectorMap; // radial falloff: no quad edges in the shimmer
                if (hazeMat.HasProperty("_DistortionVectorMap")) hazeMat.SetTexture("_DistortionVectorMap", vmap);
                if (hazeMat.HasProperty("_UnlitColorMap")) hazeMat.SetTexture("_UnlitColorMap", vmap);
                if (hazeMat.HasProperty("_DistortionScale")) hazeMat.SetFloat("_DistortionScale", 0.016f);
                if (hazeMat.HasProperty("_UnlitColor")) hazeMat.SetColor("_UnlitColor", new Color(1f, 1f, 1f, 0.25f));
                var hz = NewSystem("Fire Heat Shimmer", new Vector3(0f, 0.7f * s, 0f), hazeMat, 0f);
                var mh = hz.main;
                mh.startLifetime = new ParticleSystem.MinMaxCurve(1.0f, 1.6f);
                mh.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
                mh.startSize = new ParticleSystem.MinMaxCurve(0.7f * s, 1.2f * s);
                mh.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                mh.gravityModifier = -0.08f;
                mh.maxParticles = 24;
                var he = hz.emission;
                he.rateOverTime = 9f;
                UpCone(hz, 6f, 0.25f * s);
                var hsz = hz.sizeOverLifetime;
                hsz.enabled = true;
                hsz.size = Curve((0f, 0f), (0.3f, 1f), (0.75f, 0.7f), (1f, 0f));
                var hn = hz.noise;
                hn.enabled = true;
                hn.strength = 0.2f;
                hn.frequency = 0.8f;
            }

            foreach (var ps in GetComponentsInChildren<ParticleSystem>(true)) ps.Play();
        }

        private ParticleSystem BuildParticles(string name, Color startColor, Color endColor,
            float minSize, float maxSize, float emissionRate)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.up * 0.05f;
            var particles = go.AddComponent<ParticleSystem>();
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                bool isFlame = !name.Contains("Smoke");
                // D60: library materials (built into the player); the old Resources path never existed, so every fire
                // fell through to a runtime HDRP/Unlit material, which a player build renders as an opaque box
                var lib = LR2.FX.LR2FxLibrary.Instance;
                Material libMat = lib != null ? (isFlame ? lib.fire : lib.smoke) : null;
                if (libMat != null) LR2.FX.LR2FxLibrary.ApplyFlipbook(particles, isFlame ? lib.fireTiles : lib.smokeTiles);
                Material assetMat = libMat != null ? libMat
                    : UnityEngine.Resources.Load<Material>(isFlame ? "LR2/FX/M_FX_Spark" : "LR2/FX/M_FX_Smoke");
                Shader shader = Shader.Find("LR2/ParticleSoft") ?? Shader.Find("HDRP/Unlit") ?? Shader.Find("Unlit/Color");
                if (assetMat != null)
                {
                    renderer.sharedMaterial = assetMat;
                }
                else if (shader != null)
                {
                    var material = new Material(shader) { name = name + " HDRP Particle" };
                    var tex = CreateSoftCircleTexture();
                    if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", tex);
                    if (material.HasProperty("_BaseColorMap")) material.SetTexture("_BaseColorMap", tex);
                    if (material.HasProperty("_UnlitColorMap")) material.SetTexture("_UnlitColorMap", tex);
                    if (material.HasProperty("_UnlitColor")) material.SetColor("_UnlitColor", startColor);
                    if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", startColor);
                    if (material.HasProperty("_Color")) material.SetColor("_Color", startColor);
                    if (material.HasProperty("_SurfaceType")) material.SetFloat("_SurfaceType", 1f);
                    if (material.HasProperty("_BlendMode")) material.SetFloat("_BlendMode", isFlame ? 1f : 0f);
                    if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", isFlame ? 1f : 10f);
                    if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", 5f);
                    HDMaterial.ValidateMaterial(material);
                    renderer.sharedMaterial = material;
                }
            }
            var main = particles.main;
            main.startLifetime = name.Contains("Smoke") ? 1.8f : 0.5f;
            main.startSpeed = name.Contains("Smoke") ? 0.8f : 1.1f;
            main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
            main.startColor = new ParticleSystem.MinMaxGradient(startColor, endColor);
            main.gravityModifier = name.Contains("Smoke") ? -0.02f : 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 256;

            var emission = particles.emission;
            emission.rateOverTime = emissionRate;

            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = Mathf.Max(0.15f, damageRadius * 0.38f);

            var color = particles.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(startColor, endColor);
            return particles;
        }

        private static Texture2D _softCircle;
        private static Texture2D CreateSoftCircleTexture()
        {
            if (_softCircle != null) return _softCircle;
            const int S = 64;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = (x + 0.5f) / S * 2f - 1f, v = (y + 0.5f) / S * 2f - 1f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(u * u + v * v));
                a *= a;
                px[y * S + x] = new Color(1f, 1f, 1f, a);
            }
            tex.SetPixels(px);
            tex.Apply(false, true);
            _softCircle = tex;
            return _softCircle;
        }

        private static AudioClip _fallbackCrackle;
        private static AudioClip BuildFallbackCrackleClip()
        {
            if (_fallbackCrackle != null) return _fallbackCrackle;
            const int sr = 22050;
            int n = sr * 2;
            var data = new float[n];
            var rng = new System.Random(4207);
            float lp = 0f, pop = 0f;
            for (int i = 0; i < n; i++)
            {
                float w = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp = Mathf.Lerp(lp, w, 0.08f);
                if (rng.NextDouble() < 0.0045) pop = (float)(rng.NextDouble() * 0.9 + 0.3) * (rng.Next(0, 2) == 0 ? 1f : -1f);
                pop *= 0.92f;
                data[i] = Mathf.Clamp(lp * 0.28f + pop * 0.45f, -0.8f, 0.8f);
            }
            var clip = AudioClip.Create("LR2_FireCrackle", n, 1, sr, false);
            clip.SetData(data, 0);
            _fallbackCrackle = clip;
            return _fallbackCrackle;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.35f, 0.05f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, damageRadius);
        }
    }
}
