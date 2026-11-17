using System.Collections.Generic;
using LR2.Characters;
using LR2.Combat;
using LR2.Core;
using UnityEngine;
using UnityEngine.UI;

namespace LR2.FX
{
    /// <summary>
    /// Visceral wet crimson blood droplets on both the player's held weapon (3D glistening beads &amp; splatters)
    /// and screen/bodycam lens (viscoelastic gravity-dripping droplets) after taking damage or scoring CQB kills.
    /// Auto-bootstraps cleanly in gameplay scenes without requiring prefab modifications.
    /// </summary>
    public class GunBloodFX : MonoBehaviour
    {
        public static GunBloodFX Instance { get; private set; }

        private const int MaxScreenDroplets = 28;
        private const int MaxGunDroplets = 32;

        private struct ScreenDroplet
        {
            public RectTransform rt;
            public RawImage img;
            public float age;
            public float lifetime;
            public float baseWidth;
            public float baseHeight;
            public float dripSpeed;
            public float maxAlpha;
            public bool active;
        }

        private struct GunDroplet
        {
            public GameObject go;
            public Transform host;
            public Vector3 baseLocalPos;
            public Vector3 localSlipDir;
            public float baseScale;
            public float slideSpeed;
            public float maxSlide;
            public float age;
            public float lifetime;
        }

        private Combatant _player;
        private SoldierController _soldier;
        private Object _lastItem;
        private float _nextLowHealthDrip;
        private bool _wasDead;

        private Canvas _screenCanvas;
        private readonly ScreenDroplet[] _screenPool = new ScreenDroplet[MaxScreenDroplets];
        private readonly List<GunDroplet> _gunDrops = new List<GunDroplet>(MaxGunDroplets);

        private Texture2D[] _screenTexs;
        private Texture2D _gunSplatTex;
        private Material _gunBeadMat;
        private Material _gunSplatMat;
        private Mesh _quadMesh;
        private Mesh _sphereMesh;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBoot()
        {
            // Only activate automatically when a player combatant or gameplay HUD is likely present;
            // menu scenes have no Combatant and stay completely untouched.
            if (Combatant.FindLocalPlayer() != null) EnsureInstance();
        }

        public static GunBloodFX EnsureInstance()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("[LR2 GunBloodFX]");
            Instance = go.AddComponent<GunBloodFX>();
            return Instance;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            BuildTexturesAndMaterials();
            BuildScreenCanvas();
            GameEvents.Hit += OnHit;
            GameEvents.Killed += OnKilled;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            GameEvents.Hit -= OnHit;
            GameEvents.Killed -= OnKilled;

            ClearGunDroplets();
            if (_screenCanvas != null) Destroy(_screenCanvas.gameObject);
            if (_gunBeadMat != null) Destroy(_gunBeadMat);
            if (_gunSplatMat != null) Destroy(_gunSplatMat);
            if (_gunSplatTex != null) Destroy(_gunSplatTex);
            if (_screenTexs != null)
            {
                foreach (var t in _screenTexs)
                    if (t != null) Destroy(t);
            }
        }

        private void Update()
        {
            BindPlayer();
            float dt = Time.unscaledDeltaTime;

            if (_player != null)
            {
                bool dead = !_player.IsAlive;
                if (dead && !_wasDead)
                {
                    ClearGunDroplets();
                }
                else if (!dead && _wasDead)
                {
                    ClearGunDroplets();
                    ClearScreenDroplets();
                }
                _wasDead = dead;

                // Weapon changed -> hide/clear stale drops from old weapon transform
                var curItem = _soldier != null ? _soldier.ActiveItem : null;
                if (curItem != _lastItem)
                {
                    ClearGunDroplets();
                    _lastItem = curItem;
                }

                // Subtle persistent bleeding droplets while critically wounded
                if (!dead && _player.Health != null && _player.Health.Normalized < 0.4f && Time.unscaledTime >= _nextLowHealthDrip)
                {
                    _nextLowHealthDrip = Time.unscaledTime + Random.Range(0.9f, 1.7f);
                    float edgeX = Random.value < 0.5f ? -1f : 1f;
                    SplashScreenDroplets(1, 0.35f, edgeX, Random.Range(-0.8f, 0.8f));
                }
            }

            UpdateScreenDroplets(dt);
            UpdateGunDroplets(Time.deltaTime);
        }

        private void BindPlayer()
        {
            if (_player == null) _player = Combatant.FindLocalPlayer();
            if (_soldier == null && _player != null) _soldier = _player.GetComponent<SoldierController>();
        }

        private void OnHit(HitEvent e)
        {
            if (_player == null) BindPlayer();
            if (_player == null || e.damage <= 0f) return;

            if (e.victim == _player)
            {
                float sev = Mathf.Clamp01(e.damage / 32f);
                ComputeHitScreenBias(e.point, e.direction, out float biasX, out float biasY);

                int screenCount = Mathf.RoundToInt(Mathf.Lerp(4f, 11f, sev));
                SplashScreenDroplets(screenCount, sev, biasX, biasY);

                int gunCount = Mathf.RoundToInt(Mathf.Lerp(5f, 12f, sev));
                SplashGunDroplets(gunCount, sev);
            }
            else if (e.attacker == _player && e.victim != _player)
            {
                // Point-blank close-quarters arterial mist on gun/visor when blasting someone right in front of you
                float dist = Vector3.Distance(_player.Position, e.point);
                if (dist < 3.6f)
                {
                    float closeFactor = Mathf.Clamp01(1f - dist / 3.6f);
                    SplashGunDroplets(Mathf.RoundToInt(Mathf.Lerp(1f, 5f, closeFactor)), closeFactor * 0.7f);
                    if (dist < 2.2f)
                        SplashScreenDroplets(Mathf.RoundToInt(Mathf.Lerp(1f, 4f, closeFactor)), closeFactor * 0.55f, 0f, -0.35f);
                }
            }
        }

        private void OnKilled(KillEvent e)
        {
            if (_player == null) return;
            if (e.victim == _player)
            {
                SplashScreenDroplets(8, 1f, 0f, 0f);
                return;
            }
            if (e.killer == _player && e.victim != null)
            {
                float dist = Vector3.Distance(_player.Position, e.victim.Position);
                if (dist < 4f) SplashGunDroplets(e.headshot ? 5 : 3, 0.85f);
            }
        }

        private void ComputeHitScreenBias(Vector3 hitPoint, Vector3 dir, out float biasX, out float biasY)
        {
            biasX = 0f;
            biasY = 0f;
            if (_soldier == null || _soldier.ViewCamera == null) return;
            Transform camT = _soldier.ViewCamera.transform;
            Vector3 toSource = -dir;
            if (toSource.sqrMagnitude < 0.01f && _player != null)
                toSource = hitPoint - _player.ChestPosition;
            if (toSource.sqrMagnitude < 0.001f) return;
            Vector3 local = camT.InverseTransformDirection(toSource.normalized);
            biasX = Mathf.Clamp(local.x, -1f, 1f);
            biasY = Mathf.Clamp(local.y, -1f, 1f);
        }

        // ------------------------------------------------------------------ screen blood drops
        private void BuildScreenCanvas()
        {
            var go = new GameObject("[LR2 ScreenBloodCanvas]", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            _screenCanvas = go.AddComponent<Canvas>();
            _screenCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _screenCanvas.sortingOrder = 6; // Behind LR2_GameUI (order 10) so reticle & HUD stay readable

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            for (int i = 0; i < MaxScreenDroplets; i++)
            {
                var dGo = new GameObject($"BloodDrop_{i}", typeof(RectTransform));
                dGo.transform.SetParent(go.transform, false);
                var rt = (RectTransform)dGo.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.72f);

                var img = dGo.AddComponent<RawImage>();
                img.texture = _screenTexs[i % _screenTexs.Length];
                img.raycastTarget = false;
                img.color = Color.clear;
                dGo.SetActive(false);

                _screenPool[i] = new ScreenDroplet { rt = rt, img = img, active = false };
            }
        }

        private void SplashScreenDroplets(int count, float severity, float biasX, float biasY)
        {
            for (int n = 0; n < count; n++)
            {
                int slot = PickScreenSlot();
                ref var d = ref _screenPool[slot];

                // Place around edges/quadrants leaving central sightline open for aiming
                float angle = Random.Range(0f, Mathf.PI * 2f);
                if (Mathf.Abs(biasX) + Mathf.Abs(biasY) > 0.25f && Random.value < 0.72f)
                {
                    float baseAng = Mathf.Atan2(biasY, biasX);
                    angle = baseAng + Random.Range(-0.95f, 0.95f);
                }

                float radiusX = Random.Range(340f, 890f);
                float radiusY = Random.Range(210f, 495f);
                float x = Mathf.Clamp(Mathf.Cos(angle) * radiusX, -900f, 900f);
                float y = Mathf.Clamp(Mathf.Sin(angle) * radiusY, -490f, 490f);

                // Push slightly outward if somehow inside center aim cone
                if (Mathf.Abs(x) < 230f && Mathf.Abs(y) < 170f)
                {
                    if (Mathf.Abs(x) > Mathf.Abs(y)) x = (x >= 0f ? 1f : -1f) * Random.Range(260f, 520f);
                    else y = (y >= 0f ? 1f : -1f) * Random.Range(210f, 380f);
                }

                float size = Random.Range(26f, 84f) * Mathf.Lerp(0.7f, 1.35f, severity);
                float aspect = Random.Range(0.75f, 1.25f);
                d.baseWidth = size * aspect;
                d.baseHeight = size / aspect;
                d.rt.anchoredPosition = new Vector2(x, y);
                d.rt.sizeDelta = new Vector2(d.baseWidth, d.baseHeight);
                d.rt.localEulerAngles = new Vector3(0f, 0f, Random.Range(-18f, 18f));
                d.img.texture = _screenTexs[Random.Range(0, _screenTexs.Length)];

                d.age = 0f;
                d.lifetime = Random.Range(4.8f, 8.6f) * Mathf.Lerp(0.85f, 1.25f, severity);
                d.dripSpeed = Random.Range(6f, 24f) * (size > 48f ? 1.35f : 0.75f);
                d.maxAlpha = Random.Range(0.62f, 0.9f);
                d.active = true;
                d.rt.gameObject.SetActive(true);
            }
        }

        private int PickScreenSlot()
        {
            int oldest = 0;
            float oldestRatio = -1f;
            for (int i = 0; i < _screenPool.Length; i++)
            {
                if (!_screenPool[i].active) return i;
                float ratio = _screenPool[i].age / Mathf.Max(0.1f, _screenPool[i].lifetime);
                if (ratio > oldestRatio)
                {
                    oldestRatio = ratio;
                    oldest = i;
                }
            }
            return oldest;
        }

        private void UpdateScreenDroplets(float dt)
        {
            float healMult = (_player != null && _player.Health != null && _player.Health.Normalized > 0.88f) ? 1.55f : 1f;
            for (int i = 0; i < _screenPool.Length; i++)
            {
                ref var d = ref _screenPool[i];
                if (!d.active) continue;
                d.age += dt * healMult;
                if (d.age >= d.lifetime)
                {
                    d.active = false;
                    d.rt.gameObject.SetActive(false);
                    continue;
                }

                float t = d.age / d.lifetime;
                float pop = Mathf.Clamp01(d.age / 0.07f);
                float popScale = Mathf.Lerp(1.38f, 1f, pop * pop);
                float fade = t < 0.55f ? 1f : Mathf.Clamp01((1f - t) / 0.45f);

                // Gravity viscoelastic drip downwards & slight vertical teardrop stretch
                float drip = Mathf.Max(0f, d.age - 0.35f);
                float stretchY = 1f + Mathf.Clamp01(drip * 0.14f) * 0.55f;
                d.rt.anchoredPosition -= new Vector2(0f, d.dripSpeed * dt * Mathf.Clamp01(drip * 0.9f));
                d.rt.sizeDelta = new Vector2(d.baseWidth * popScale, d.baseHeight * popScale * stretchY);

                // Fresh bright crimson darkens slightly as it dries on the lens
                Color c = Color.Lerp(new Color(0.88f, 0.08f, 0.08f, 1f), new Color(0.36f, 0.02f, 0.03f, 1f), t);
                c.a = d.maxAlpha * pop * fade;
                d.img.color = c;
            }
        }

        private void ClearScreenDroplets()
        {
            for (int i = 0; i < _screenPool.Length; i++)
            {
                _screenPool[i].active = false;
                if (_screenPool[i].rt != null) _screenPool[i].rt.gameObject.SetActive(false);
            }
        }

        public int ActiveGunDropletCount => _gunDrops.Count;

        // ------------------------------------------------------------------ gun blood drops
        private void SplashGunDroplets(int count, float severity)
        {
            if (_soldier == null || _soldier.IsDead) return;
            Camera viewCam = _soldier.ViewCamera;
            Transform camT = viewCam != null ? viewCam.transform : _soldier.transform;
            int viewMask = viewCam != null ? viewCam.cullingMask : ~0;

            var validFilters = new List<MeshFilter>();
            var visibleRenderers = new List<Renderer>();

            void CollectFrom(Transform root, bool requireWeaponNameFallback)
            {
                if (root == null) return;
                foreach (var r in root.GetComponentsInChildren<Renderer>(false))
                {
                    if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                    if (r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
                    if (r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly) continue;
                    if ((viewMask & (1 << r.gameObject.layer)) == 0) continue;
                    if (r.gameObject.name.Contains("[LR2_Blood")) continue;
                    if (requireWeaponNameFallback && !IsLikelyWeaponRenderer(r)) continue;

                    visibleRenderers.Add(r);
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null) validFilters.Add(mf);
                }
            }

            if (_soldier.ActiveItem != null) CollectFrom(_soldier.ActiveItem.transform, false);
            if (visibleRenderers.Count == 0) CollectFrom(_soldier.transform, true);
            if (visibleRenderers.Count == 0) return;

            Vector3 worldDown = Vector3.down;

            for (int n = 0; n < count; n++)
            {
                while (_gunDrops.Count >= MaxGunDroplets)
                {
                    if (_gunDrops[0].go != null) Destroy(_gunDrops[0].go);
                    _gunDrops.RemoveAt(0);
                }

                bool placed = false;
                Vector3 localPos = Vector3.zero;
                Vector3 localNormal = Vector3.up;
                Transform host = visibleRenderers[0].transform;

                if (validFilters.Count > 0)
                {
                    var mf = validFilters[Random.Range(0, validFilters.Count)];
                    host = mf.transform;
                    placed = TrySampleMeshSurface(mf, camT, out localPos, out localNormal);
                }

                if (!placed)
                {
                    var r = visibleRenderers[Random.Range(0, visibleRenderers.Count)];
                    host = r.transform;
                    var mfFallback = r.GetComponent<MeshFilter>();
                    Bounds b = (r is MeshRenderer && mfFallback != null && mfFallback.sharedMesh != null)
                        ? mfFallback.sharedMesh.bounds
                        : r.localBounds;
                    if (b.size.sqrMagnitude < 1e-5f) b = new Bounds(Vector3.zero, Vector3.one * 0.12f);

                    // Project toward camera/upward-left side of weapon receiver/handguard rather than outer bounding corners
                    Vector3 localToCam = host.InverseTransformDirection(-camT.forward);
                    Vector3 localUp = host.InverseTransformDirection(Vector3.up);
                    Vector3 sideDir = Vector3.Cross(localUp, localToCam).normalized;
                    if (sideDir.sqrMagnitude < 0.01f) sideDir = Vector3.right;
                    Vector3 faceDir = (localToCam * 0.65f + localUp * 0.85f).normalized;

                    localPos = b.center
                        + Vector3.Scale(faceDir, b.extents * 0.36f)
                        + Vector3.Scale(localToCam, b.extents * Random.Range(-0.22f, 0.28f))
                        + Vector3.Scale(sideDir, b.extents * Random.Range(-0.22f, 0.22f));
                    localNormal = faceDir.sqrMagnitude > 0.01f ? faceDir : Vector3.up;
                }

                bool bead = Random.value < 0.52f;
                var go = new GameObject(bead ? "[LR2_BloodBead]" : "[LR2_BloodSplat]");
                go.layer = host.gameObject.layer;
                go.transform.SetParent(host, false);
                go.transform.localPosition = localPos + localNormal * (bead ? 0.0015f : 0.002f);

                // Unity PrimitiveType.Quad fronts point toward -Z, so align -forward to localNormal
                Vector3 lookDir = bead ? localNormal : -localNormal;
                Vector3 upHint = Mathf.Abs(Vector3.Dot(lookDir.normalized, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up;
                Quaternion rot = Quaternion.LookRotation(lookDir, upHint) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
                go.transform.localRotation = rot;

                var mfDrop = go.AddComponent<MeshFilter>();
                mfDrop.sharedMesh = bead ? _sphereMesh : _quadMesh;
                var mrDrop = go.AddComponent<MeshRenderer>();
                mrDrop.sharedMaterial = bead ? _gunBeadMat : _gunSplatMat;
                mrDrop.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mrDrop.receiveShadows = true;

                Vector3 lossy = host.lossyScale;
                float invScale = 1f / Mathf.Max(0.05f, (Mathf.Abs(lossy.x) + Mathf.Abs(lossy.y) + Mathf.Abs(lossy.z)) / 3f);

                float worldRadius = bead
                    ? Random.Range(0.007f, 0.014f) * Mathf.Lerp(0.85f, 1.35f, severity)
                    : Random.Range(0.018f, 0.044f) * Mathf.Lerp(0.85f, 1.4f, severity);

                float localBase = worldRadius * invScale;
                go.transform.localScale = bead
                    ? new Vector3(localBase, localBase * Random.Range(0.85f, 1.25f), localBase * 0.45f)
                    : new Vector3(localBase, localBase * Random.Range(0.85f, 1.25f), localBase);

                Vector3 hostGravity = host.InverseTransformDirection(worldDown).normalized;
                Vector3 slip = Vector3.ProjectOnPlane(hostGravity, localNormal);
                if (slip.sqrMagnitude > 0.01f) slip.Normalize();
                else slip = Vector3.zero;

                _gunDrops.Add(new GunDroplet
                {
                    go = go,
                    host = host,
                    baseLocalPos = go.transform.localPosition,
                    localSlipDir = slip,
                    baseScale = localBase,
                    slideSpeed = bead ? Random.Range(0.0007f, 0.002f) * invScale : 0f,
                    maxSlide = Random.Range(0.008f, 0.022f) * invScale,
                    age = 0f,
                    lifetime = Random.Range(14f, 24f),
                });
            }
        }

        private static bool IsLikelyWeaponRenderer(Renderer r)
        {
            string n = r.gameObject.name.ToLowerInvariant();
            if (n.Contains("swat") || n.Contains("head") || n.Contains("body") || n.Contains("helmet") || n.Contains("vest"))
                return false;
            return n.Contains("ak") || n.Contains("mk") || n.Contains("fal") || n.Contains("gun") || n.Contains("rifle")
                || n.Contains("receiver") || n.Contains("barrel") || n.Contains("mag") || n.Contains("weapon");
        }

        private static bool TrySampleMeshSurface(MeshFilter mf, Transform camT, out Vector3 localPos, out Vector3 localNormal)
        {
            localPos = Vector3.zero;
            localNormal = Vector3.up;
            var mesh = mf.sharedMesh;
            if (mesh == null || !mesh.isReadable) return false;
            var tris = mesh.triangles;
            var verts = mesh.vertices;
            if (tris == null || tris.Length < 3 || verts == null || verts.Length < 3) return false;

            int triCount = tris.Length / 3;
            Vector3 toCamWorld = camT != null ? -camT.forward : Vector3.up;

            // Sample up to 10 candidate triangles and keep one facing camera/upward so droplets appear visibly on top/sides
            float bestScore = float.MinValue;
            for (int attempt = 0; attempt < 10; attempt++)
            {
                int tIdx = Random.Range(0, triCount) * 3;
                Vector3 v0 = verts[tris[tIdx]];
                Vector3 v1 = verts[tris[tIdx + 1]];
                Vector3 v2 = verts[tris[tIdx + 2]];
                Vector3 n = Vector3.Cross(v1 - v0, v2 - v0);
                float area = n.magnitude;
                if (area < 1e-6f) continue;
                n /= area;

                Vector3 worldN = mf.transform.TransformDirection(n).normalized;
                float facing = Vector3.Dot(worldN, toCamWorld) * 0.65f + Vector3.Dot(worldN, Vector3.up) * 0.55f;
                if (facing > bestScore)
                {
                    bestScore = facing;
                    float a = Random.value;
                    float b = Random.value;
                    if (a + b > 1f) { a = 1f - a; b = 1f - b; }
                    float c = 1f - a - b;
                    localPos = v0 * a + v1 * b + v2 * c;
                    localNormal = n;
                }
            }
            return bestScore > -0.25f;
        }

        private void UpdateGunDroplets(float dt)
        {
            for (int i = _gunDrops.Count - 1; i >= 0; i--)
            {
                var d = _gunDrops[i];
                if (d.go == null || d.host == null)
                {
                    _gunDrops.RemoveAt(i);
                    continue;
                }

                d.age += dt;
                if (d.age >= d.lifetime)
                {
                    Destroy(d.go);
                    _gunDrops.RemoveAt(i);
                    continue;
                }

                if (d.slideSpeed > 0f && d.localSlipDir.sqrMagnitude > 0.01f)
                {
                    float dist = Mathf.Min(d.maxSlide, d.age * d.slideSpeed);
                    d.go.transform.localPosition = d.baseLocalPos + d.localSlipDir * dist;
                }

                float shrinkStart = d.lifetime * 0.65f;
                if (d.age > shrinkStart)
                {
                    float remain = Mathf.Clamp01(1f - (d.age - shrinkStart) / (d.lifetime - shrinkStart));
                    bool bead = d.go.name.Contains("Bead");
                    float s = d.baseScale * remain;
                    d.go.transform.localScale = bead ? new Vector3(s, s, s * 0.42f) : new Vector3(s, s, s);
                }

                _gunDrops[i] = d;
            }
        }

        private void ClearGunDroplets()
        {
            foreach (var d in _gunDrops)
                if (d.go != null) Destroy(d.go);
            _gunDrops.Clear();
        }

        // ------------------------------------------------------------------ procedural textures & meshes
        private void BuildTexturesAndMaterials()
        {
            _screenTexs = new[]
            {
                CreateDropletTexture(128, 11),
                CreateDropletTexture(128, 37),
                CreateDropletTexture(128, 73),
            };
            _gunSplatTex = CreateDropletTexture(128, 109);

            // Built-in primitives safely yield Unity's shared Quad and Sphere meshes
            var tmpQ = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _quadMesh = tmpQ.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tmpQ);

            var tmpS = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _sphereMesh = tmpS.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tmpS);

            Shader lit = Shader.Find("HDRP/Lit") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            _gunBeadMat = new Material(lit);
            _gunBeadMat.hideFlags = HideFlags.HideAndDontSave;
            if (_gunBeadMat.HasProperty("_BaseColor")) _gunBeadMat.SetColor("_BaseColor", new Color(0.65f, 0.02f, 0.02f, 1f));
            if (_gunBeadMat.HasProperty("_Color")) _gunBeadMat.SetColor("_Color", new Color(0.65f, 0.02f, 0.02f, 1f));
            if (_gunBeadMat.HasProperty("_Smoothness")) _gunBeadMat.SetFloat("_Smoothness", 0.95f);
            if (_gunBeadMat.HasProperty("_Metallic")) _gunBeadMat.SetFloat("_Metallic", 0.04f);
            if (_gunBeadMat.HasProperty("_EmissiveColor")) _gunBeadMat.SetColor("_EmissiveColor", new Color(0.14f, 0.004f, 0.004f, 1f));

            _gunSplatMat = new Material(lit);
            _gunSplatMat.hideFlags = HideFlags.HideAndDontSave;
            if (_gunSplatMat.HasProperty("_BaseColor")) _gunSplatMat.SetColor("_BaseColor", new Color(0.62f, 0.02f, 0.02f, 1f));
            if (_gunSplatMat.HasProperty("_Color")) _gunSplatMat.SetColor("_Color", new Color(0.62f, 0.02f, 0.02f, 1f));
            if (_gunSplatMat.HasProperty("_BaseColorMap")) _gunSplatMat.SetTexture("_BaseColorMap", _gunSplatTex);
            if (_gunSplatMat.HasProperty("_MainTex")) _gunSplatMat.SetTexture("_MainTex", _gunSplatTex);
            if (_gunSplatMat.HasProperty("_Smoothness")) _gunSplatMat.SetFloat("_Smoothness", 0.92f);
            if (_gunSplatMat.HasProperty("_Metallic")) _gunSplatMat.SetFloat("_Metallic", 0.02f);
            if (_gunSplatMat.HasProperty("_EmissiveColor")) _gunSplatMat.SetColor("_EmissiveColor", new Color(0.12f, 0.003f, 0.003f, 1f));
            if (_gunSplatMat.HasProperty("_AlphaCutoffEnable")) _gunSplatMat.SetFloat("_AlphaCutoffEnable", 1f);
            if (_gunSplatMat.HasProperty("_AlphaCutoff")) _gunSplatMat.SetFloat("_AlphaCutoff", 0.28f);
            if (_gunSplatMat.HasProperty("_DoubleSidedEnable")) _gunSplatMat.SetFloat("_DoubleSidedEnable", 1f);
            _gunSplatMat.EnableKeyword("_ALPHATEST_ON");
            _gunSplatMat.EnableKeyword("_DOUBLESIDED_ON");
        }

        private static float SmoothStep01(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / Mathf.Max(0.0001f, edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        private static Texture2D CreateDropletTexture(int size, int seed)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
            tex.hideFlags = HideFlags.HideAndDontSave;
            tex.wrapMode = TextureWrapMode.Clamp;

            var rng = new System.Random(seed);
            int lobeCount = 9;
            var lobes = new Vector3[lobeCount]; // x, y, radius
            for (int i = 0; i < lobeCount; i++)
            {
                float a = (float)i / lobeCount * Mathf.PI * 2f + (float)rng.NextDouble() * 0.55f;
                float d = (0.14f + (float)rng.NextDouble() * 0.28f);
                lobes[i] = new Vector3(Mathf.Cos(a) * d, Mathf.Sin(a) * d, 0.10f + (float)rng.NextDouble() * 0.16f);
            }

            var pixels = new Color32[size * size];
            for (int py = 0; py < size; py++)
            {
                float v = (py + 0.5f) / size * 2f - 1f;
                for (int px = 0; px < size; px++)
                {
                    float u = (px + 0.5f) / size * 2f - 1f;

                    float field = Mathf.Clamp01(1f - Mathf.Sqrt(u * u + v * v) / 0.44f);
                    for (int i = 0; i < lobeCount; i++)
                    {
                        float dx = u - lobes[i].x;
                        float dy = v - lobes[i].y;
                        float r = Mathf.Sqrt(dx * dx + dy * dy);
                        field = Mathf.Max(field, Mathf.Clamp01(1f - r / lobes[i].z));
                    }

                    // Strict zero outside droplet silhouette so screen overlays never show rectangular edges
                    float alpha = SmoothStep01(0.14f, 0.34f, field);
                    float rim = Mathf.Clamp01(1f - Mathf.Abs(field - 0.28f) * 4f) * alpha;
                    float specDx = u + 0.13f;
                    float specDy = v - 0.14f;
                    float spec = Mathf.Clamp01(1f - Mathf.Sqrt(specDx * specDx + specDy * specDy) / 0.16f);
                    spec = Mathf.Pow(spec, 3f) * alpha;

                    float rCol = Mathf.Clamp01(0.68f + spec * 0.30f + rim * 0.16f);
                    float gCol = Mathf.Clamp01(0.03f + spec * 0.45f);
                    float bCol = Mathf.Clamp01(0.035f + spec * 0.45f);

                    pixels[py * size + px] = new Color(rCol, gCol, bCol, alpha);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }
    }
}
