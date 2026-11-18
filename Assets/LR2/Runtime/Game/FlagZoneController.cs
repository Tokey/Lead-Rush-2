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
        private readonly Dictionary<Combatant, float> _ffaProgress = new Dictionary<Combatant, float>(8);
        public Combatant LeadingCombatant { get; private set; }

        // Visual rig
        private Transform _rigRoot;
        private Transform _flagBanner;
        private Material _ringMat;
        private Material _coreMat;
        private Material _bannerMat;
        private Material _beamMat;
        private MeshRenderer _bannerRenderer;
        private ParticleSystem _captureBurst;
        private ParticleSystem _ambientSparks;
        private Material _burstMat;
        private Material _sparkMat;
        private Light _zoneLight;

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
            if (_sparkMat != null) Destroy(_sparkMat);
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
            _ffaProgress.Clear();
            LeadingCombatant = null;
            _leadingEnemy = null;
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

        /// <summary>Counts friendly and hostile combatants inside the outer zone vs inner 2.25x core for tactical bot planning.</summary>
        public void CountZoneOccupants(Combatant observer, out int alliesInZone, out int alliesInCore, out int hostilesInZone, out Combatant closestHostileInZone)
        {
            alliesInZone = 0;
            alliesInCore = 0;
            hostilesInZone = 0;
            closestHostileInZone = null;
            float bestDist = float.MaxValue;

            var list = Combatant.All;
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (c == null || c == observer || !c.IsAlive || c.Team == TeamId.Hostile) continue;
                float planar = Vector2.Distance(new Vector2(c.Position.x, c.Position.z), new Vector2(_activePos.x, _activePos.z));
                float vert = Mathf.Abs(c.Position.y - _activePos.y);
                if (vert > 4.5f || planar > _outerRadius) continue;

                bool hostile = observer == null || TeamUtil.AreHostile(observer, c);
                if (hostile)
                {
                    hostilesInZone++;
                    if (planar < bestDist)
                    {
                        bestDist = planar;
                        closestHostileInZone = c;
                    }
                }
                else
                {
                    alliesInZone++;
                    if (planar <= _coreRadius) alliesInCore++;
                }
            }
        }

        /// <summary>True when a hostile is actively capturing or contesting the flag (returns normalized 0..1 capture urgency).</summary>
        public bool IsHostileCapturingOrContesting(Combatant who, out float urgency01)
        {
            urgency01 = 0f;
            if (who == null) return false;
            var mm = MatchManager.Instance;
            bool isTeamMode = mm != null && mm.IsTeamMode;
            if (isTeamMode)
            {
                var local = mm != null ? mm.LocalPlayer : null;
                bool sameAsPlayer = local != null && !TeamUtil.AreHostile(local, who);
                if (State == ZoneState.Contested)
                {
                    urgency01 = Mathf.Max(0.45f, Mathf.Abs(Progress));
                    return true;
                }
                if (sameAsPlayer && (Progress < -0.15f || State == ZoneState.Losing))
                {
                    urgency01 = Mathf.Clamp01(Mathf.Abs(Progress));
                    return true;
                }
                if (!sameAsPlayer && Progress > 0.15f)
                {
                    urgency01 = Mathf.Clamp01(Progress);
                    return true;
                }
                return false;
            }

            float maxEnemyProg = 0f;
            bool enemyOccupant = State == ZoneState.Contested || (LeadingCombatant != null && LeadingCombatant != who);
            foreach (var kv in _ffaProgress)
            {
                if (kv.Key == null || kv.Key == who || !kv.Key.IsAlive) continue;
                if (kv.Value > maxEnemyProg) maxEnemyProg = kv.Value;
            }
            if (!enemyOccupant && maxEnemyProg < 0.2f) return false;
            urgency01 = Mathf.Clamp01(Mathf.Max(maxEnemyProg, State == ZoneState.Contested ? 0.5f : 0.25f));
            return true;
        }

        private void EvaluateCapture(float dt)
        {
            var mm = MatchManager.Instance;
            bool isTeamMode = mm != null && mm.IsTeamMode;
            if (!isTeamMode)
            {
                EvaluateFreeForAllCapture(dt, mm);
                return;
            }

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
                if (_captureBurst != null) _captureBurst.Play();
                if (mm != null && mm.LocalPlayer != null) mm.AwardFlagCapture(mm.LocalPlayer, ActiveSectorName);
                _nextAutoAwardCooldown = Time.time + 1.5f;
                RotateToNextStrategicFlag();
            }
            else if (Progress <= -0.999f)
            {
                if (_captureBurst != null) _captureBurst.Play();
                if (mm != null && _leadingEnemy != null) mm.AwardFlagCapture(_leadingEnemy, ActiveSectorName);
                _nextAutoAwardCooldown = Time.time + 1.5f;
                RotateToNextStrategicFlag();
            }
        }

        /// <summary>B51: Free-For-All Capture the Flag tracks individual progress for every participant in the zone.</summary>
        private void EvaluateFreeForAllCapture(float dt, MatchManager mm)
        {
            LocalPlayerDistance = 999f;
            LocalPlayerSpeedMult = 0f;

            Combatant top1 = null, top2 = null;
            float f1 = 0f, f2 = 0f;
            Combatant local = mm != null ? mm.LocalPlayer : null;

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
                    local = c;
                }
                if (vert > 4.5f || planar > _outerRadius) continue;

                float mult = ComputeProximitySpeedMultiplier(planar, _coreRadius, _outerRadius);
                if (mult > f1) { f2 = f1; top2 = top1; f1 = mult; top1 = c; }
                else if (mult > f2) { f2 = mult; top2 = c; }
            }

            bool contested = top1 != null && top2 != null && f2 > 0.05f && (f1 - f2) < 0.65f;
            float baseRate = 1f / BaseCaptureSeconds;
            Combatant capturer = null;

            if (!contested)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var c = list[i];
                    if (c == null || c.Team == TeamId.Hostile) continue;
                    float cur = _ffaProgress.TryGetValue(c, out float v) ? v : 0f;
                    if (c == top1 && f1 > 0.05f)
                        cur = Mathf.Clamp01(cur + (f1 - f2) * baseRate * dt);
                    else
                        cur = Mathf.MoveTowards(cur, 0f, baseRate * (top1 != null ? 0.45f : 0.22f) * dt);
                    _ffaProgress[c] = cur;
                    if (cur >= 0.999f) capturer = c;
                }
            }

            float localProg = local != null && _ffaProgress.TryGetValue(local, out float lp) ? lp : 0f;
            float bestBotProg = 0f;
            _leadingEnemy = null;
            foreach (var kv in _ffaProgress)
            {
                if (kv.Key == null || kv.Key == local || !kv.Key.IsAlive) continue;
                if (kv.Value > bestBotProg) { bestBotProg = kv.Value; _leadingEnemy = kv.Key; }
            }

            LeadingCombatant = top1;
            Progress = localProg >= bestBotProg ? localProg : -bestBotProg;

            if (contested) State = ZoneState.Contested;
            else if (top1 == local && local != null && f1 > 0.05f)
            {
                State = ZoneState.Capturing;
                if (LocalPlayerInside && Time.time >= _nextPulseTime)
                {
                    _nextPulseTime = Time.time + Mathf.Lerp(0.75f, 0.30f, Mathf.Clamp01(LocalPlayerSpeedMult / 2.25f));
                    var s = LR2CommonSounds.Instance;
                    if (s != null) LR2Audio.Play2D(s.countdownTick, 0.42f, 0.2f);
                }
            }
            else if (top1 != null && f1 > 0.05f) State = ZoneState.Losing;
            else State = ZoneState.Idle;

            if (capturer != null)
            {
                if (_captureBurst != null) _captureBurst.Play();
                if (mm != null) mm.AwardFlagCapture(capturer, ActiveSectorName);
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

            // Soft ground perimeter projection ring & inner proximity core ring (feathered alpha, no hard edges)
            var outerTex = BuildRingTex(256, 0.88f, 0.045f, dashed: true);
            _ringMat = new Material(pShader);
            _ringMat.SetTexture("_MainTex", outerTex);
            _ringMat.SetColor("_Color", new Color(0.25f, 0.75f, 1.0f, 0.34f));
            _ringMat.SetFloat("_Intensity", 1.35f);
            _ringMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _ringMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            _ringMat.renderQueue = 3000;

            var outerQuad = CreateHorizontalQuad("OuterCaptureRing", _rigRoot, new Vector3(0f, 0.05f, 0f), 1f, _ringMat);
            _outerRingTf = outerQuad.transform;

            var coreTex = BuildRingTex(256, 0.86f, 0.065f, dashed: false);
            _coreMat = new Material(_ringMat);
            _coreMat.SetTexture("_MainTex", coreTex);
            _coreMat.SetColor("_Color", new Color(1f, 0.72f, 0.18f, 0.48f));
            _coreMat.SetFloat("_Intensity", 1.85f);
            var coreQuad = CreateHorizontalQuad("CoreCaptureRing", _rigRoot, new Vector3(0f, 0.06f, 0f), 1f, _coreMat);
            _coreRingTf = coreQuad.transform;

            // Faint soft-gradient vertical light column (crossed billboards so no hard cylinder silhouette)
            var beamTex = BuildSoftBeamTex(64, 128);
            _beamMat = new Material(pShader);
            _beamMat.SetTexture("_MainTex", beamTex);
            _beamMat.SetColor("_Color", new Color(0.95f, 0.70f, 0.22f, 0.16f));
            _beamMat.SetFloat("_Intensity", 1.25f);
            _beamMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _beamMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            _beamMat.SetFloat("_Cull", 0f);
            _beamMat.renderQueue = 3000;

            for (int bi = 0; bi < 2; bi++)
            {
                var beam = GameObject.CreatePrimitive(PrimitiveType.Quad);
                beam.name = "FlagBeacon_" + bi;
                beam.transform.SetParent(_rigRoot, false);
                beam.transform.localPosition = new Vector3(0f, 7.5f, 0f);
                beam.transform.localRotation = Quaternion.Euler(0f, bi * 90f, 0f);
                beam.transform.localScale = new Vector3(1.55f, 15f, 1f);
                Destroy(beam.GetComponent<Collider>());
                beam.GetComponent<Renderer>().sharedMaterial = _beamMat;
            }

            // Warm/team-tinted ground point light + gentle rising glowing zone motes (B41)
            var lightGo = new GameObject("FlagZoneLight");
            lightGo.transform.SetParent(_rigRoot, false);
            lightGo.transform.localPosition = new Vector3(0f, 2.2f, 0f);
            _zoneLight = lightGo.AddComponent<Light>();
            _zoneLight.type = LightType.Point;
            _zoneLight.range = 10f;
            _zoneLight.shadows = LightShadows.None;
            _zoneLight.intensity = 1600f;
            lightGo.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>();

            var sparksGo = new GameObject("FlagZoneParticles");
            sparksGo.transform.SetParent(_rigRoot, false);
            sparksGo.transform.localPosition = new Vector3(0f, 0.25f, 0f);
            _ambientSparks = sparksGo.AddComponent<ParticleSystem>();
            var sm = _ambientSparks.main;
            sm.loop = true;
            sm.playOnAwake = true;
            sm.simulationSpace = ParticleSystemSimulationSpace.World;
            sm.maxParticles = 72;
            sm.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 3.2f);
            sm.startSpeed = new ParticleSystem.MinMaxCurve(0.45f, 1.05f);
            sm.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
            sm.gravityModifier = -0.05f;
            var sem = _ambientSparks.emission;
            sem.rateOverTime = 18f;
            var ssh = _ambientSparks.shape;
            ssh.shapeType = ParticleSystemShapeType.Circle;
            ssh.radius = _coreRadius * 0.9f;
            var svel = _ambientSparks.velocityOverLifetime;
            svel.enabled = true;
            svel.y = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
            _sparkMat = new Material(pShader);
            _sparkMat.SetTexture("_MainTex", BuildSoftDiscTex(64));
            _sparkMat.SetColor("_Color", new Color(1f, 0.78f, 0.25f, 0.75f));
            _sparkMat.SetFloat("_Intensity", 2.2f);
            _sparkMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _sparkMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            sparksGo.GetComponent<ParticleSystemRenderer>().sharedMaterial = _sparkMat;
            _ambientSparks.Play();

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

            float pulse = state == ZoneState.Contested ? 0.75f + 0.25f * Mathf.Sin(Time.time * 10f) : 1f;
            if (_bannerMat != null && _bannerMat.HasProperty("_BaseColor")) _bannerMat.SetColor("_BaseColor", col);
            if (_ringMat != null) _ringMat.SetColor("_Color", new Color(col.r, col.g, col.b, 0.34f * pulse));
            if (_coreMat != null) _coreMat.SetColor("_Color", new Color(col.r, col.g, col.b, (0.42f + 0.32f * Mathf.Abs(prog)) * pulse));
            if (_beamMat != null) _beamMat.SetColor("_Color", new Color(col.r, col.g, col.b, 0.15f * pulse));
            if (_sparkMat != null) _sparkMat.SetColor("_Color", new Color(col.r, col.g, col.b, 0.78f));
            if (_zoneLight != null)
            {
                _zoneLight.color = col;
                _zoneLight.intensity = 1500f * pulse;
            }
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

        private static Texture2D BuildRingTex(int size, float ringRadius, float thickness, bool dashed = false)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - (size - 1) * 0.5f) / (size * 0.5f);
                    float dy = (y - (size - 1) * 0.5f) / (size * 0.5f);
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float ang = Mathf.Atan2(dy, dx);
                    float dash = dashed ? Mathf.SmoothStep(0.15f, 0.65f, 0.5f + 0.5f * Mathf.Sin(ang * 24f)) : 1f;
                    float ring = Mathf.Exp(-Mathf.Pow((r - ringRadius) / thickness, 2f)) * (dashed ? Mathf.Lerp(0.35f, 1f, dash) : 1f);
                    float halo = Mathf.Exp(-Mathf.Pow((r - ringRadius) / (thickness * 3.2f), 2f)) * 0.35f;
                    float fill = r < ringRadius ? 0.055f * Mathf.Pow(r / Mathf.Max(0.01f, ringRadius), 1.6f) : 0f;
                    byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(ring + halo + fill) * 255f);
                    px[y * size + x] = new Color32(255, 255, 255, a);
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        private static Texture2D BuildSoftBeamTex(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                float v = y / (float)(h - 1);
                float vFade = Mathf.Pow(1f - v, 2.2f) * Mathf.SmoothStep(0f, 0.08f, v);
                for (int x = 0; x < w; x++)
                {
                    float u = Mathf.Abs(x / (float)(w - 1) * 2f - 1f);
                    float hFade = Mathf.Pow(Mathf.Clamp01(1f - u), 2.4f);
                    byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(vFade * hFade) * 255f);
                    px[y * w + x] = new Color32(255, 255, 255, a);
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        private static Texture2D BuildSoftDiscTex(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(u * u + v * v));
                px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }
    }
}
