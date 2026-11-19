using System.Collections.Generic;
using LR2.AI;
using LR2.Audio;
using LR2.Combat;
using LR2.Core;
using LR2.FX;
using UnityEngine;

namespace LR2.World
{
    /// <summary>
    /// Makes scene props, crates, barriers, furniture, glass windows, and fuel barrels destructible.
    /// Reacts to bullets with a physical jolt, shatters into dynamic debris chunks when depleted, updates CoverPoints,
    /// and optionally detonates (explosive barrels / gas cylinders) with radial splash damage and chain reactions.
    /// </summary>
    public class DestructibleProp : MonoBehaviour, IDamageable
    {
        [Header("Durability")]
        public float maxHealth = 90f;
        public SurfaceType surface = SurfaceType.Wood;

        [Header("Explosion (optional)")]
        public bool explosive;
        public float blastRadius = 5.5f;
        public float blastDamage = 85f;
        public GameObject explosionPrefab;

        private float _hp;
        private bool _destroyed;
        private Vector3 _restPos;
        private Quaternion _restRot;
        private float _joltTimer;
        private Vector3 _joltDir;

        public bool CanBeDamaged => !_destroyed;

        private void Awake()
        {
            _hp = Mathf.Max(1f, maxHealth);
            _restPos = transform.localPosition;
            _restRot = transform.localRotation;
        }

        private void Update()
        {
            if (_joltTimer <= 0f) return;
            _joltTimer -= Time.deltaTime;
            if (_joltTimer <= 0f)
            {
                transform.localPosition = _restPos;
                transform.localRotation = _restRot;
                return;
            }
            float t = _joltTimer / 0.09f;
            Vector3 localDir = transform.parent != null ? transform.parent.InverseTransformDirection(_joltDir) : _joltDir;
            localDir.y = 0f;
            if (localDir.sqrMagnitude > 0.001f) localDir.Normalize();
            transform.localPosition = _restPos + localDir * (0.025f * t);
            Vector3 tiltAxis = Vector3.Cross(Vector3.up, localDir);
            transform.localRotation = _restRot * Quaternion.AngleAxis(3.5f * t, tiltAxis.sqrMagnitude > 0.001f ? tiltAxis : Vector3.right);
        }

        public float ApplyDamage(ref DamageInfo info)
        {
            if (_destroyed) return 0f;
            float incoming = info.type == DamageType.Explosion ? info.amount * 2.4f : info.amount;
            float applied = Mathf.Min(_hp, Mathf.Max(0f, incoming));
            _hp -= applied;

            _joltDir = info.direction.sqrMagnitude > 0.001f ? info.direction.normalized : -info.normal;
            _joltTimer = 0.09f;

            if (_hp <= 0f) Shatter(info);
            return applied;
        }

        private void Shatter(DamageInfo info)
        {
            _destroyed = true;
            _joltTimer = 0f;

            Bounds b = ComputeWorldBounds();
            Vector3 center = b.center;
            Material chunkMat = ResolvePrimaryMaterial();

            ImpactSystem.SpawnBulletImpact(center, Vector3.up, info.direction, surface, true);
            SpawnDebrisChunks(b, info.direction, chunkMat);

            foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = false;
            if (explosive) TriggerExplosion(center, info);

            // Notify AI cover registry cleanly so bots vacate ruined cover.
            for (int i = CoverPoint.All.Count - 1; i >= 0; i--)
            {
                var cp = CoverPoint.All[i];
                if (cp != null && (cp.transform.position - center).sqrMagnitude < 4.5f)
                    cp.gameObject.SetActive(false);
            }

            gameObject.SetActive(false);
        }

        private Bounds ComputeWorldBounds()
        {
            var rs = GetComponentsInChildren<Renderer>(true);
            bool has = false;
            Bounds b = new Bounds(transform.position, Vector3.one * 0.6f);
            foreach (var r in rs)
            {
                if (r is ParticleSystemRenderer) continue;
                if (!has) { b = r.bounds; has = true; }
                else b.Encapsulate(r.bounds);
            }
            return b;
        }

        private Material ResolvePrimaryMaterial()
        {
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                if (r.sharedMaterial != null) return r.sharedMaterial;
            }
            return null;
        }

        private void SpawnDebrisChunks(Bounds b, Vector3 impulseDir, Material mat)
        {
            int count = surface == SurfaceType.Glass ? 6 : 5;
            Vector3 dir = impulseDir.sqrMagnitude > 0.001f ? impulseDir.normalized : Vector3.forward;
            Vector3 chunkBase = new Vector3(
                Mathf.Clamp(b.size.x * 0.28f, 0.10f, 0.40f),
                Mathf.Clamp(b.size.y * 0.22f, 0.08f, 0.34f),
                Mathf.Clamp(b.size.z * 0.28f, 0.10f, 0.40f));

            for (int i = 0; i < count; i++)
            {
                var chunk = GameObject.CreatePrimitive(PrimitiveType.Cube);
                chunk.name = "DebrisChunk";
                chunk.layer = LR2Layers.Debris;
                chunk.transform.position = b.center + new Vector3(
                    Random.Range(-b.extents.x * 0.45f, b.extents.x * 0.45f),
                    Random.Range(-b.extents.y * 0.2f, b.extents.y * 0.4f),
                    Random.Range(-b.extents.z * 0.45f, b.extents.z * 0.45f));
                chunk.transform.rotation = Random.rotation;
                chunk.transform.localScale = new Vector3(
                    chunkBase.x * Random.Range(0.55f, 1.25f),
                    chunkBase.y * Random.Range(0.45f, 1.15f),
                    chunkBase.z * Random.Range(0.55f, 1.25f));

                if (mat != null) chunk.GetComponent<Renderer>().sharedMaterial = mat;
                var rb = chunk.AddComponent<Rigidbody>();
                rb.mass = surface == SurfaceType.Concrete ? 3.5f : 1.2f;
                Vector3 v = dir * Random.Range(2.2f, 5.5f) + Vector3.up * Random.Range(1.8f, 4.4f) + Random.insideUnitSphere * 2.2f;
                rb.AddForce(v, ForceMode.Impulse);
                rb.angularVelocity = Random.insideUnitSphere * 9f;
                Destroy(chunk, 4.2f);
            }
        }

        private void TriggerExplosion(Vector3 center, DamageInfo triggerInfo)
        {
            // B112: the Particle Pack fireball (sized to the blast) and a light pulse; the old effect only without the pack
            var fxLib = LR2FxLibrary.Instance;
            if (fxLib != null && fxLib.packExplosion != null)
            {
                PackFx.Spawn(fxLib.packExplosion, center, Quaternion.identity, Mathf.Clamp(blastRadius / 2.2f, 1.4f, 3.2f), 5f);
                ExplosionFlash.Spawn(center + Vector3.up * 0.6f, new Color(1f, 0.62f, 0.32f), 60000f, blastRadius * 3f, 0.45f);
            }
            else if (explosionPrefab != null) LR2Pool.Spawn(explosionPrefab, center, Quaternion.identity, 4f);
            var s = LR2CommonSounds.Instance;
            if (s != null) LR2Audio.PlayAt(s.orbExplode, center);
            GameEvents.RaiseNoise(triggerInfo.attacker, center, 48f, NoiseType.Explosion);

            var cols = Physics.OverlapSphere(center, blastRadius, LR2Layers.BulletMask, QueryTriggerInteraction.Ignore);
            var damagedRoots = new HashSet<IDamageable>();
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] == null || cols[i].transform.IsChildOf(transform)) continue;
                var hb = cols[i].GetComponent<Hitbox>();
                IDamageable dmg = hb != null && hb.Owner != null ? hb.Owner : cols[i].GetComponentInParent<IDamageable>();
                if (dmg == null || !dmg.CanBeDamaged || !damagedRoots.Add(dmg)) continue;
                Vector3 targetPos = (hb != null && hb.Combatant != null) ? hb.Combatant.ChestPosition : cols[i].bounds.center;
                float dist = Vector3.Distance(center, targetPos);
                if (dist > blastRadius) continue;
                if (Physics.Linecast(center + Vector3.up * 0.25f, targetPos, out var wallHit, LR2Layers.EnvironmentMask, QueryTriggerInteraction.Ignore)
                    && wallHit.collider != cols[i]
                    && wallHit.collider.transform.root != cols[i].transform.root)
                    continue;
                float falloff = Mathf.Clamp01(1f - dist / Mathf.Max(0.1f, blastRadius));
                var splash = new DamageInfo
                {
                    amount = blastDamage * (0.25f + 0.75f * falloff),
                    type = DamageType.Explosion,
                    zone = HitZone.Generic,
                    attacker = triggerInfo.attacker,
                    source = gameObject,
                    weaponName = explosive ? "EXPLOSIVE" : "DEBRIS",
                    point = targetPos,
                    normal = (center - targetPos).normalized,
                    direction = (targetPos - center).normalized,
                    distance = dist,
                    force = 90f * falloff
                };
                dmg.ApplyDamage(ref splash);
            }
        }
    }
}
