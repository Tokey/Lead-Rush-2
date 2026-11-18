using System.Collections.Generic;
using LR2.AI;
using LR2.Audio;
using LR2.Combat;
using LR2.Core;
using UnityEngine;
using UnityEngine.AI;

namespace LR2.Game
{
    /// <summary>
    /// Battlefield-6-inspired rotating strategic Capture The Flag Deathmatch controller:
    /// - Activates one strategic <see cref="FlagAnchor"/> at a time (building procedural pole, waving cloth banner,
    ///   core & outer capture rings, optical tactical beacon beam, and particle flare burst on capture).
    /// - Proximity-weighted capture rate ("closer faster, far harder"): core (<= 2.6m) captures at 2.25x speed;
    ///   outer perimeter (~11.5m) slows to 0.28x speed. Standing close to the pole actively overpowers edge campers!
    /// - Rotates automatically to a distant strategic plaza after capture or idle timeout.
    /// </summary>
    public class FlagZoneController : MonoBehaviour
    {
        public static FlagZoneController Instance { get; private set; }

        public enum ZoneState { Idle, Capturing, Losing, Contested }

        public string ActiveSectorName => _activeAnchor != null ? _activeAnchor.sectorName : "CENTRAL FLAG";
        public Vector3 ActiveFlagPosition => _activePos;
        public float ActiveRadius => _outerRadius;
        public float CoreRadius => _coreRadius;
        /// <summary>Capture progress: -1 (hostile) .. 0 (neutral) .. +1 (player).</summary>
        public float Progress { get; private set; }
        public ZoneState State { get; private set; } = ZoneState.Idle;
        public float LocalPlayerDistance { get; private set; } = 999f;
        public float LocalPlayerSpeedMult { get; private set; }
        public bool LocalPlayerInside => LocalPlayerDistance <= _outerRadius;
        public float RotationRemaining => Mathf.Max(0f, _rotateAt - Time.time);

        private readonly List<FlagAnchor> _anchors = new List<FlagAnchor>(8);
        private FlagAnchor _activeAnchor;
        private int _anchorIndex = -1;
        private Vector3 _activePos;
        private float _outerRadius = 11.5f;
        private float _coreRadius = 2.6f;
        private float _rotateAt;
        private float _nextPulseTime;
        private float _nextAutoAwardCooldown;
        private Combatant _leadingEnemy;

        // Visual rig
        private Transform _rigRoot;
        private Transform _flagBanner;
        private Material _ringMat;
        private Material _coreMat;
        private Material _bannerMat;
        private Material _beamMat;
        private MeshRenderer _bannerRenderer;
        private ParticleSystem _captureBurst;
        private Material _burstMat;

        private const float BaseCaptureSeconds = 7.5f;
        private const float StaleRotationSeconds = 55f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            GatherAnchors();
            BuildVisualRig();
            RotateToNextStrategicFlag(initial: true);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_ringMat != null) Destroy(_ringMat);
            if (_coreMat != null) Destroy(_coreMat);
            if (_bannerMat != null) Destroy(_bannerMat);
            if (_beamMat != null) Destroy(_beamMat);
            if (_burstMat != null) Destroy(_burstMat);
        }

        private void GatherAnchors()
        {
            _anchors.Clear();
            foreach (var a in FlagAnchor.All)
                if (a != null) _anchors.Add(a);

            if (_anchors.Count == 0)
            {
                // Fallback from scene TacticalPoints / SpawnPoints if map didn't embed explicit FlagAnchors
                var pts = new List<(Vector3 pos, string name)>();
                foreach (var tp in TacticalPoint.All)
                {
                    if (tp == null) continue;
                    bool far = true;
                    for (int i = 0; i < pts.Count; i++)
                        if (Vector3.Distance(pts[i].pos, tp.transform.position) < 12f) { far = false; break; }
                    if (far && NavMesh.SamplePosition(tp.transform.position, out var nh, 3f, NavMesh.AllAreas))
                        pts.Add((nh.position, $"SECTOR {(char)('A' + pts.Count)}"));
                    if (pts.Count >= 4) break;
                }
                if (pts.Count == 0) pts.Add((Vector3.zero, "CENTRAL HUB"));

                for (int i = 0; i < pts.Count; i++)
                {
                    var go = new GameObject("FlagAnchor_Auto_" + i);
                    go.transform.SetParent(transform, false);
                    go.transform.position = pts[i].pos;
                    var fa = go.AddComponent<FlagAnchor>();
                    fa.sectorName = pts[i].name;
                    _anchors.Add(fa);
                }
            }
        }

        private void RotateToNextStrategicFlag(bool initial = false)
        {
            if (_anchors.Count == 0) return;

            int nextIdx = 0;
            if (!initial && _anchors.Count > 1)
            {
                // Pick the farthest non-repeating strategic plaza so firefights sweep across the map
                float bestDist = -1f;
                for (int i = 0; i < _anchors.Count; i++)
                {
                    if (i == _anchorIndex) continue;
                    float d = Vector3.Distance(_activePos, _anchors[i].transform.position) + Random.Range(0f, 8f);
                    if (d > bestDist) { bestDist = d; nextIdx = i; }
                }
            }
            else if (_anchors.Count > 1)
            {
                // Start near centermost strategic flag on initial spawn
                float bestCenter = float.MaxValue;
                for (int i = 0; i < _anchors.Count; i++)
                {
                    float d = new Vector2(_anchors[i].transform.position.x, _anchors[i].transform.position.z).magnitude;
                    if (d < bestCenter) { bestCenter = d; nextIdx = i; }
                }
            }

            _anchorIndex = nextIdx;
            _activeAnchor = _anchors[nextIdx];
            Vector3 p = _activeAnchor.transform.position;
            if (NavMesh.SamplePosition(p, out var hit, 4f, NavMesh.AllAreas)) p = hit.position;
            _activePos = p;
            _outerRadius = Mathf.Max(5f, _activeAnchor.captureRadius);
            _coreRadius = Mathf.Clamp(_activeAnchor.coreRadius, 1.5f, _outerRadius * 0.45f);

            Progress = 0f;
            State = ZoneState.Idle;
            _rotateAt = Time.time + StaleRotationSeconds;

            if (_rigRoot != null)
            {
                _rigRoot.position = _activePos;
                _rigRoot.localScale = Vector3.one;
            }
            UpdateRingScale();
        }

        private void Update()
        {
            var mm = MatchManager.Instance;
            if (mm != null && mm.State != MatchState.Playing)
            {
                UpdateVisualRig(0f, ZoneState.Idle);
                return;
            }

            if (Time.time >= _rotateAt && Time.time > _nextAutoAwardCooldown)
            {
                _nextAutoAwardCooldown = Time.time + 1.5f;
                RotateToNextStrategicFlag();
                var s = LR2CommonSounds.Instance;
                if (s != null) LR2Audio.Play2D(s.countdownTick, 0.7f, 0.25f);
                return;
            }

            EvaluateCapture(Time.deltaTime);
            UpdateVisualRig(Progress, State);
        }

        /// <summary>
        /// Computes BF6 distance-weighted capture multiplier ("closer faster, far harder"):
        /// 2.25x inside core (&lt;= 2.6m), falling off smoothly to 0.28x near outer radius.
        /// </summary>
        public static float ComputeProximitySpeedMultiplier(float dist, float coreRadius, float outerRadius)
        {
            if (dist > outerRadius) return 0f;
            if (dist <= coreRadius) return 2.25f;
            float t = Mathf.Clamp01((dist - coreRadius) / Mathf.Max(0.1f, outerRadius - coreRadius));
            return Mathf.Lerp(2.25f, 0.28f, Mathf.Pow(t, 0.65f));
        }

        private void EvaluateCapture(float dt)
        {
            float playerForce = 0f;
            float bestEnemyForce = 0f;
            _leadingEnemy = null;

            LocalPlayerDistance = 999f;
            LocalPlayerSpeedMult = 0f;

            var list = Combatant.All;
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (c == null || !c.IsAlive || c.Team == TeamId.Hostile) continue;
                float planar = Vector2.Distance(new Vector2(c.Position.x, c.Position.z), new Vector2(_activePos.x, _activePos.z));
                float vert = Mathf.Abs(c.Position.y - _activePos.y);

                if (c.IsLocalPlayer)
                {
                    LocalPlayerDistance = planar;
                    LocalPlayerSpeedMult = vert <= 4.5f ? ComputeProximitySpeedMultiplier(planar, _coreRadius, _outerRadius) : 0f;
                }

                if (vert > 4.5f || planar > _outerRadius) continue;
                float mult = ComputeProximitySpeedMultiplier(planar, _coreRadius, _outerRadius);

                var mm = MatchManager.Instance;
                var local = mm != null ? mm.LocalPlayer : null;
                bool isFriendly = c.IsLocalPlayer || (local != null && !TeamUtil.AreHostile(local, c));
                if (isFriendly)
                {
                    playerForce += mult;
                }
                else if (mult > bestEnemyForce)
                {
                    bestEnemyForce = mult;
                    _leadingEnemy = c;
                }
            }

            float diff = playerForce - bestEnemyForce;
            bool contested = playerForce > 0.05f && bestEnemyForce > 0.05f && Mathf.Abs(diff) < 0.65f;

            if (contested)
            {
                State = ZoneState.Contested;
                return;
            }

            float baseRate = 1f / BaseCaptureSeconds;
            if (diff > 0.05f)
            {
                State = ZoneState.Capturing;
                Progress = Mathf.Clamp(Progress + diff * baseRate * dt, -1f, 1f);
                if (LocalPlayerInside && Time.time >= _nextPulseTime)
                {
                    _nextPulseTime = Time.time + Mathf.Lerp(0.75f, 0.30f, Mathf.Clamp01(LocalPlayerSpeedMult / 2.25f));
                    var s = LR2CommonSounds.Instance;
                    if (s != null) LR2Audio.Play2D(s.countdownTick, 0.42f, 0.2f);
                }
            }
            else if (diff < -0.05f)
            {
                State = ZoneState.Losing;
                Progress = Mathf.Clamp(Progress + diff * baseRate * dt, -1f, 1f);
            }
            else
            {
                // Gently decay partial capture if nobody is inside the zone
                State = ZoneState.Idle;
                Progress = Mathf.MoveTowards(Progress, 0f, baseRate * 0.22f * dt);
            }

            if (Progress >= 0.999f)
            {
                var mm = MatchManager.Instance;
                if (_captureBurst != null) _captureBurst.Play();
                if (mm != null && mm.LocalPlayer != null) mm.AwardFlagCapture(mm.LocalPlayer, ActiveSectorName);
                _nextAutoAwardCooldown = Time.time + 1.5f;
                RotateToNextStrategicFlag();
            }
            else if (Progress <= -0.999f)
            {
                var mm = MatchManager.Instance;
                if (_captureBurst != null) _captureBurst.Play();
                if (mm != null && _leadingEnemy != null) mm.AwardFlagCapture(_leadingEnemy, ActiveSectorName);
                _nextAutoAwardCooldown = Time.time + 1.5f;
                RotateToNextStrategicFlag();
            }
        }

        private void BuildVisualRig()
        {
            _rigRoot = new GameObject("ActiveFlagVisual").transform;
            _rigRoot.SetParent(transform, false);

            var pShader = Shader.Find("LR2/ParticleUnlit") ?? Shader.Find("Sprites/Default");
            var litShader = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");

            // Concrete plinth + flagpole mast
            var plinth = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            plinth.name = "FlagPlinth";
            plinth.transform.SetParent(_rigRoot, false);
            plinth.transform.localPosition = new Vector3(0f, 0.12f, 0f);
            plinth.transform.localScale = new Vector3(1.15f, 0.12f, 1.15f);
            Destroy(plinth.GetComponent<Collider>());
            var pm = new Material(litShader);
            if (pm.HasProperty("_BaseColor")) pm.SetColor("_BaseColor", new Color(0.22f, 0.24f, 0.27f));
            plinth.GetComponent<Renderer>().sharedMaterial = pm;

            var mast = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            mast.name = "FlagMast";
            mast.transform.SetParent(_rigRoot, false);
            mast.transform.localPosition = new Vector3(0f, 2.25f, 0f);
            mast.transform.localScale = new Vector3(0.08f, 2.15f, 0.08f);
            Destroy(mast.GetComponent<Collider>());
            var mm = new Material(litShader);
            if (mm.HasProperty("_BaseColor")) mm.SetColor("_BaseColor", new Color(0.75f, 0.78f, 0.82f));
            if (mm.HasProperty("_Metallic")) mm.SetFloat("_Metallic", 0.85f);
            mast.GetComponent<Renderer>().sharedMaterial = mm;

            // Flag banner hanging near top of mast
            var flagGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            flagGo.name = "FlagBanner";
            flagGo.transform.SetParent(_rigRoot, false);
            flagGo.transform.localPosition = new Vector3(0.72f, 3.7f, 0f);
            flagGo.transform.localScale = new Vector3(1.35f, 0.78f, 0.04f);
            Destroy(flagGo.GetComponent<Collider>());
            _bannerMat = new Material(litShader);
            if (_bannerMat.HasProperty("_BaseColor")) _bannerMat.SetColor("_BaseColor", new Color(0.95f, 0.65f, 0.15f));
            _bannerRenderer = flagGo.GetComponent<MeshRenderer>();
            _bannerRenderer.sharedMaterial = _bannerMat;
            _flagBanner = flagGo.transform;

            // Ground outer capture ring & inner core ring
            var ringTex = BuildRingTex(128, 0.90f, 0.06f);
            _ringMat = new Material(pShader);
            _ringMat.SetTexture("_MainTex", ringTex);
            _ringMat.SetColor("_Color", new Color(0.25f, 0.75f, 1.0f, 0.55f));
            _ringMat.SetFloat("_Intensity", 1.8f);
            _ringMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _ringMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            _ringMat.renderQueue = 3000;

            var outerQuad = CreateHorizontalQuad("OuterCaptureRing", _rigRoot, new Vector3(0f, 0.07f, 0f), 1f, _ringMat);
            _outerRingTf = outerQuad.transform;

            _coreMat = new Material(_ringMat);
            _coreMat.SetColor("_Color", new Color(1f, 0.72f, 0.18f, 0.75f));
            _coreMat.SetFloat("_Intensity", 2.4f);
            var coreQuad = CreateHorizontalQuad("CoreCaptureRing", _rigRoot, new Vector3(0f, 0.08f, 0f), 1f, _coreMat);
            _coreRingTf = coreQuad.transform;

            // Vertical subtle optical sky beacon
            var beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            beam.name = "FlagBeacon";
            beam.transform.SetParent(_rigRoot, false);
            beam.transform.localPosition = new Vector3(0f, 18f, 0f);
            beam.transform.localScale = new Vector3(0.32f, 18f, 0.32f);
            Destroy(beam.GetComponent<Collider>());
            _beamMat = new Material(pShader);
            _beamMat.SetTexture("_MainTex", Texture2D.whiteTexture);
            _beamMat.SetColor("_Color", new Color(0.95f, 0.70f, 0.22f, 0.16f));
            _beamMat.SetFloat("_Intensity", 1.6f);
            _beamMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _beamMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            _beamMat.renderQueue = 3000;
            beam.GetComponent<Renderer>().sharedMaterial = _beamMat;

            // Capture celebration spark burst
            var bGo = new GameObject("FlagCaptureBurst");
            bGo.transform.SetParent(_rigRoot, false);
            bGo.transform.localPosition = new Vector3(0f, 3.6f, 0f);
            _captureBurst = bGo.AddComponent<ParticleSystem>();
            var bm = _captureBurst.main;
            bm.loop = false;
            bm.playOnAwake = false;
            bm.duration = 0.6f;
            bm.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.9f);
            bm.startSpeed = new ParticleSystem.MinMaxCurve(4f, 11f);
            bm.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
            bm.gravityModifier = 1.6f;
            var bem = _captureBurst.emission;
            bem.enabled = true;
            bem.SetBursts(new[] { new ParticleSystem.Burst(0f, 48) });
            bem.rateOverTime = 0f;
            _burstMat = new Material(_ringMat);
            _burstMat.SetTexture("_MainTex", Texture2D.whiteTexture);
            _burstMat.SetColor("_Color", new Color(1f, 0.80f, 0.28f, 0.95f));
            _burstMat.SetFloat("_Intensity", 3.5f);
            bGo.GetComponent<ParticleSystemRenderer>().sharedMaterial = _burstMat;
        }

        private Transform _outerRingTf;
        private Transform _coreRingTf;

        private void UpdateRingScale()
        {
            if (_outerRingTf != null) _outerRingTf.localScale = new Vector3(_outerRadius * 2f, _outerRadius * 2f, 1f);
            if (_coreRingTf != null) _coreRingTf.localScale = new Vector3(_coreRadius * 2f, _coreRadius * 2f, 1f);
        }

        private void UpdateVisualRig(float prog, ZoneState state)
        {
            if (_flagBanner != null)
            {
                float wind = LR2.World.WeatherSystem.Instance != null ? LR2.World.WeatherSystem.Wind : 0.3f;
                float flutter = Mathf.Sin(Time.time * (4.5f + wind * 6f)) * (7f + wind * 12f);
                _flagBanner.localRotation = Quaternion.Euler(0f, flutter, 0f);
                float h = Mathf.Lerp(1.85f, 3.85f, Mathf.Abs(prog));
                _flagBanner.localPosition = new Vector3(0.72f, h, 0f);
            }

            Color col = state == ZoneState.Contested
                ? Color.Lerp(new Color(1f, 0.72f, 0.15f), Color.white, 0.5f + 0.5f * Mathf.Sin(Time.time * 12f))
                : prog >= 0f
                    ? Color.Lerp(new Color(0.95f, 0.72f, 0.22f), new Color(0.18f, 0.76f, 1f), prog)
                    : Color.Lerp(new Color(0.95f, 0.72f, 0.22f), new Color(0.96f, 0.24f, 0.22f), -prog);

            if (_bannerMat != null && _bannerMat.HasProperty("_BaseColor")) _bannerMat.SetColor("_BaseColor", col);
            if (_ringMat != null) _ringMat.SetColor("_Color", new Color(col.r, col.g, col.b, 0.45f));
            if (_beamMat != null) _beamMat.SetColor("_Color", new Color(col.r, col.g, col.b, 0.16f));
        }

        private static GameObject CreateHorizontalQuad(string name, Transform parent, Vector3 localPos, float scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = Vector3.one * scale;
            Destroy(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        private static Texture2D BuildRingTex(int size, float ringRadius, float thickness)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - (size - 1) * 0.5f) / (size * 0.5f);
                    float dy = (y - (size - 1) * 0.5f) / (size * 0.5f);
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float ring = Mathf.Exp(-Mathf.Pow((r - ringRadius) / thickness, 2f));
                    float fill = r < ringRadius ? 0.08f * (1f - r / ringRadius) : 0f;
                    byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(ring + fill) * 255f);
                    px[y * size + x] = new Color32(255, 255, 255, a);
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }
    }
}
