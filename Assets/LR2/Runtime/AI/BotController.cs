using LR2.Characters;
using LR2.Combat;
using LR2.Core;
using LR2.Game;
using LR2.Weapons;
using UnityEngine;
using UnityEngine.AI;

namespace LR2.AI
{
    /// <summary>
    /// Drives a SoldierController exactly like a human would: it produces SoldierInput (move stick, look angles,
    /// trigger, ADS, reload, crouch, jump). Perception -> brain (intent) -> motor (navigation) + aim + trigger discipline.
    /// </summary>
    [RequireComponent(typeof(SoldierController))]
    [DisallowMultipleComponent]
    public class BotController : MonoBehaviour, ISoldierInputSource
    {
        [Header("Setup")]
        [SerializeField] private BotDifficulty difficulty;
        [Tooltip("Optional MonoBehaviour implementing IBotDirectiveProvider (e.g. a future LLM commander).")]
        [SerializeField] private MonoBehaviour directiveProvider;
        [SerializeField] private float directivePollInterval = 1.5f;
        [SerializeField] private float thinkInterval = 0.25f;

        [Header("Debug")]
        [SerializeField] private bool showDebugLabel;
        [SerializeField] private bool drawGizmos = true;

        public BotDifficulty Difficulty { get => difficulty; set => difficulty = value; }
        public SoldierController Soldier { get; private set; }
        public Combatant Combatant { get; private set; }
        public NavMeshAgent Agent { get; private set; }
        public BotPerception Perception { get; private set; }
        public IBotBrain Brain { get; set; }
        public BotBlackboard Blackboard { get; } = new BotBlackboard();
        public BotIntent Intent => _intent;
        public Combatant CurrentTarget => Blackboard.target;
        public string CurrentGoalName => _intent.goal.ToString();

        private Combatant _leader;
        private BotIntent _intent;
        private float _nextThink;
        private float _lastThink;
        private float _nextDirectivePoll;

        private BotDifficulty _runtimeAdaptiveDiff;
        private bool _isAdaptiveMode;
        /// <summary>Skirmish lobby per-slot difficulty: true/false beats the global Adaptive setting; null follows it.</summary>
        public bool? AdaptiveOverride { get; set; }
        private float _nextAdaptiveRefresh;

        public bool IsAdaptiveMode => _isAdaptiveMode;
        public BotDifficulty ActiveDifficulty => _isAdaptiveMode && _runtimeAdaptiveDiff != null ? _runtimeAdaptiveDiff : difficulty;

        // aim, biomechanical mouse kinematics (Fitts's law / minimum-jerk / Meyer submovements / Harris-Wolpert SDN) & recoil pull-down
        private float _aimYaw, _aimPitch, _yawVel, _pitchVel;
        private float _aimError;
        private float _noiseSeed;
        private bool _useHead;
        private Combatant _lastTarget;
        private Vector3 _perceivedTargetVel;
        private bool _wasVisible;
        private float _lostSightTime;
        private float _reactionReadyAt;
        private float _recoilReactAt;
        private float _sprayHeat;
        private float _combatStress;
        private float _glanceOffsetYaw;

        // B118 (user 11-19: bots in AI vs AI "turn very roboticly ... look at human mouse moving data, fit a curve through it").
        // Fitted to the Balabit Mouse Dynamics Challenge raw logs (9 sessions, 4,636 movements; Tools-free fit in the commit):
        // speed peaks at 34 % of a movement (IQR 19-56 %) -> beta(2.0, 3.3) speed profile; duration T = 0.167 * A^0.198 with
        // A in px (0.13 deg per px: a median 258 px movement ~ a 34 deg glance in 0.5 s); the path bows a median 8 % of its
        // length (p90 25 %, either side); the primary movement covers a median 92 % and 71 % end with a correction after a
        // short dwell; pauses between movements are log-normal (mu -0.373, sigma 1.036: median 0.72 s).
        private const float HumanBetaA = 2.0f, HumanBetaB = 3.3f;
        /// <summary>A/B switch for QA (<c>-lr2LegacyAim</c>): the pre-B118 aim (minimum-jerk flicks, no bow, Fitts timing for every look).</summary>
        public static readonly bool LegacyAim = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-lr2LegacyAim") >= 0;
        private static float[] _humanCdf;
        private float _flickCurve, _flickPerpYaw, _flickPerpPitch, _lookHoldUntil;
        private bool _flickCasual;

        private static float HumanProfile(float u)
        {
            if (_humanCdf == null)
            {
                const int n = 128;
                var c = new float[n + 1];
                float acc = 0f;
                for (int i = 1; i <= n; i++)
                {
                    float x = (i - 0.5f) / n;
                    acc += Mathf.Pow(x, HumanBetaA - 1f) * Mathf.Pow(1f - x, HumanBetaB - 1f);
                    c[i] = acc;
                }
                for (int i = 1; i <= n; i++) c[i] /= acc;
                _humanCdf = c;
            }
            float f = Mathf.Clamp01(u) * (_humanCdf.Length - 1);
            int k = Mathf.Min((int)f, _humanCdf.Length - 2);
            return Mathf.Lerp(_humanCdf[k], _humanCdf[k + 1], f - k);
        }

        private static float Gauss()
        {
            float u1 = Mathf.Max(1e-6f, Random.value), u2 = Random.value;
            return Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.PI * u2);
        }

        /// <summary>Pause before the next look-around, from the fitted log-normal (seconds).</summary>
        private static float HumanPause(float shift = 0f) => Mathf.Clamp(Mathf.Exp(-0.373f + shift + 1.036f * Gauss()), 0.15f, 3.5f);

        /// <summary>Duration of a casual look of <paramref name="deg"/> degrees: the fitted power law with its spread, a bit quicker for better aimers.</summary>
        private static float HumanLookDuration(float deg, float aimSpeed)
        {
            float t = 0.1669f * Mathf.Pow(Mathf.Max(1f, deg / 0.13f), 0.198f) * Mathf.Exp(0.22f * Gauss());
            t *= Mathf.Lerp(1.15f, 0.82f, Mathf.InverseLerp(4.2f, 10.5f, aimSpeed));
            return Mathf.Clamp(t, 0.22f, 1.1f);
        }

        /// <summary>Next scan offset: sweeps back and forth, mostly small steps, sometimes a wide check (half-normal, sigma 38 deg).</summary>
        private float NextScanOffset(float limit)
        {
            float step = Mathf.Clamp(Mathf.Abs(Gauss()) * 38f + 6f, 6f, limit * 1.6f);
            float sign = Random.value < 0.7f ? -Mathf.Sign(_glanceOffsetYaw == 0f ? (Random.value - 0.5f) : _glanceOffsetYaw) : Mathf.Sign(Random.value - 0.5f);
            return Mathf.Clamp(_glanceOffsetYaw + sign * step, -limit, limit);
        }
        private float _nextGlanceAt;

        // Primary ballistic saccade + secondary closed-loop correction state (Meyer 1988; Flash & Hogan 1985)
        private bool _flickActive;
        private float _flickStartTime;
        private float _flickDuration;
        private float _flickStartYaw, _flickStartPitch;
        private float _flickGoalYaw, _flickGoalPitch;
        private float _nextFlickAllowedAt;
        // B89 (user 11-19 00:13: "human players can sometimes overshoot, and some pros start pre shooting even, add those")
        private bool _flickOvershoot, _flickPreFire;
        private float _flickProgress;
        private bool _expectedSpotWasHidden;
        private float _preFireUntil;
        /// <summary>QA/docs: flicks started, overshoots, flick pre-fires and corner pre-fires since spawn.</summary>
        public Vector4 FlickStats => new Vector4(_statFlicks, _statOvershoots, _statFlickPreFires, _statCornerPreFires);
        private int _statFlicks, _statOvershoots, _statFlickPreFires, _statCornerPreFires;
        private float _sdnPhase;

        // trigger discipline
        private int _burstShotsLeft;
        private float _burstPauseUntil;
        private LR2Firearm _subscribedGun;

        // resupply (B74b: disengaged-only AmmoCrate seeking on broken armour or low reserve ammo)
        private Health _subscribedHealth;
        private bool _armorBroken;
        private AmmoCrate _resupplyCrate;
        private Vector3 _resupplyDestination;
        private float _nextCrateScanAt;

        public bool NeedsResupply => Blackboard.armorBroken || Blackboard.lowAmmo;
        public AmmoCrate TargetResupplyCrate => _resupplyCrate;

        // movement
        private Vector3 _lastDestination;
        private Vector3 _lastSteer;
        private float _nextRepath;
        private float _strafeDir;
        private float _nextStrafeSwitch;
        private float _stuckTimer;
        private float _lastCrouchToggle;
        private bool _slideUsed;
        private bool _slideFrame;
        private bool _peekStanding;
        private float _nextPeekToggle;

        private void Awake()
        {
            Soldier = GetComponent<SoldierController>();
            Combatant = GetComponent<Combatant>();
            Agent = GetComponent<NavMeshAgent>();
            if (Agent != null)
            {
                Agent.updatePosition = false;
                Agent.updateRotation = false;
                Agent.autoTraverseOffMeshLink = false;
            }
            Brain ??= new TacticalBotBrain();
            _noiseSeed = Random.value * 100f;
        }

        private void RefreshAdaptiveProfile(float now)
        {
            _isAdaptiveMode = AdaptiveOverride ?? (LR2Settings.BotDifficulty == 4);
            if (!_isAdaptiveMode) return;
            if (_runtimeAdaptiveDiff == null)
            {
                _runtimeAdaptiveDiff = ScriptableObject.CreateInstance<BotDifficulty>();
                _runtimeAdaptiveDiff.hideFlags = HideFlags.HideAndDontSave;
            }
            if (now >= _nextAdaptiveRefresh)
            {
                _nextAdaptiveRefresh = now + 0.75f;
                AdaptiveDifficultyTracker.FillAdaptiveProfile(_runtimeAdaptiveDiff, difficulty);
            }
        }

        private void OnDestroy()
        {
            if (_runtimeAdaptiveDiff != null) Destroy(_runtimeAdaptiveDiff);
        }

        private void OnEnable()
        {
            _lastSteer = Vector3.zero;
            _stuckTimer = 0f;
            _slideFrame = false;
            if (Soldier != null) Soldier.InputSource = this;
            Perception ??= new BotPerception(this);
            Brain ??= new TacticalBotBrain();
            Brain.Initialize(this);
            EnsureHealthSubscription();
            if (Soldier != null) Soldier.Respawned += OnRespawned;
        }

        private void OnDisable()
        {
            if (Soldier != null) Soldier.Respawned -= OnRespawned;
            Perception?.Dispose();
            Perception = null;
            if (_subscribedGun != null) _subscribedGun.Fired -= OnGunFired;
            _subscribedGun = null;
            if (_subscribedHealth != null)
            {
                _subscribedHealth.ArmorBroken -= OnArmorBroken;
                _subscribedHealth.ArmorRestored -= OnArmorRestored;
            }
            _subscribedHealth = null;
            _resupplyCrate = null;
        }

        private void OnRespawned(SoldierController s)
        {
            Perception?.Clear();
            Brain?.Reset();
            _intent = default;
            _armorBroken = s != null && s.Health != null && s.Health.HasArmor && s.Health.Armor <= 0.001f;
            _resupplyCrate = null;
            _resupplyDestination = Vector3.zero;
            _nextCrateScanAt = 0f;
            _lastTarget = null;
            _perceivedTargetVel = Vector3.zero;
            _wasVisible = false;
            _aimYaw = s.Yaw;
            _aimPitch = 0f;
            _yawVel = _pitchVel = 0f;
            _flickActive = false;
            _nextFlickAllowedAt = 0f;
            _sprayHeat = 0f;
            _combatStress = 0f;
            _lastSteer = Vector3.zero;
            _stuckTimer = 0f;
            _slideUsed = false;
            _slideFrame = false;
            if (Agent != null && Agent.isActiveAndEnabled)
            {
                if (NavMesh.SamplePosition(transform.position, out var hit, 3f, NavMesh.AllAreas)) Agent.Warp(hit.position);
                if (Agent.isOnNavMesh) Agent.ResetPath();
            }
        }

        // ------------------------------------------------------------------ public API
        public void SetSquadLeader(Combatant leader) => _leader = leader;

        public void IssueDirective(BotDirective directive)
        {
            Blackboard.directive = directive;
            Blackboard.hasDirective = directive.order != BotOrder.None;
            Blackboard.directiveUntil = directive.duration > 0f ? Time.time + directive.duration : float.MaxValue;
            _nextThink = 0f;
        }

        public string DescribeState()
        {
            var bb = Blackboard;
            string target = bb.target != null ? $"{bb.target.DisplayName} ({(bb.targetVisible ? "visible" : $"lost {bb.TimeSinceSeen:0.0}s")}, {bb.targetDistance:0}m)" : "none";
            return $"{(Combatant != null ? Combatant.DisplayName : name)} | goal {_intent.goal} | hp {bb.health01 * 100f:0} | ammo {bb.ammoInMagazine} | target {target}" +
                   (bb.hasDirective ? $" | order {bb.directive.order}" : "");
        }

        // ------------------------------------------------------------------ ISoldierInputSource
        public void GetInput(SoldierController soldier, ref SoldierInput input)
        {
            input.ClearAll();
            if (soldier.IsDead || !soldier.ControlsEnabled || Perception == null)
            {
                _lastSteer = Vector3.zero;
                _slideFrame = false;
                return;
            }

            float dt = Time.deltaTime;
            float now = Time.time;
            RefreshAdaptiveProfile(now);
            EnsureGunSubscription();
            EnsureHealthSubscription();
            Perception.Tick(dt);
            UpdateBlackboard(now);
            PollDirective(now);

            // Abort resupply immediately on the same frame if combat resumes or the targeted crate goes on cooldown.
            if (_intent.goal == BotGoal.Resupply &&
                (Blackboard.targetVisible || Blackboard.underFire || Blackboard.recentlyDamaged ||
                 _resupplyCrate == null || !_resupplyCrate.isActiveAndEnabled || !_resupplyCrate.IsReady ||
                 (!Blackboard.armorBroken && !Blackboard.lowAmmo)))
            {
                _resupplyCrate = null;
                _nextThink = 0f;
            }

            if (now >= _nextThink)
            {
                _intent = Brain.Think(Blackboard, now - _lastThink);
                TryApplyDisengagedResupplyGoal(ref _intent, now);
                _lastThink = now;
                _nextThink = now + thinkInterval * Random.Range(0.85f, 1.15f);
            }

            _slideFrame = false;
            Vector3 worldMove = UpdateNavigation(now, dt);
            UpdateAim(ref input, worldMove, now, dt);
            UpdateCombatInputs(ref input, ref worldMove, now);

            Vector3 fwd = transform.forward, right = transform.right;
            input.move = Vector2.ClampMagnitude(new Vector2(Vector3.Dot(worldMove, right), Vector3.Dot(worldMove, fwd)), 1f);
            bool threatClose = (Blackboard.target != null && Blackboard.targetDistance < 16f) ||
                               (Blackboard.hasNoise && now - Blackboard.noiseTime < 3f && Vector3.Distance(transform.position, Blackboard.noisePosition) < 16f);
            input.sprint = _slideFrame || (_intent.allowSprint && !Blackboard.targetVisible && !threatClose && input.move.y > 0.75f && Mathf.Abs(input.move.x) < 0.4f);
            UpdateStuck(ref input, now, dt);
        }

        // ------------------------------------------------------------------ blackboard / directives
        private void UpdateBlackboard(float now)
        {
            var bb = Blackboard;
            var gun = Soldier.ActiveFirearm;
            var health = Soldier.Health;
            bb.time = now;
            bb.position = transform.position;
            bb.yaw = Soldier.Yaw;
            bb.difficulty = ActiveDifficulty;
            bb.health01 = health != null ? health.Normalized : 1f;
            bool hasArmor = health != null && health.HasArmor;
            if (hasArmor && health.Armor > 0.001f) _armorBroken = false;
            else if (hasArmor && health.Armor <= 0.001f) _armorBroken = true;
            bb.hasArmor = hasArmor;
            bb.armor01 = health != null ? health.ArmorNormalized : 0f;
            bb.armorBroken = hasArmor && _armorBroken;
            bb.ammoInMagazine = gun != null ? gun.AmmoInMagazine : 0;
            bb.ammo01 = gun != null ? (float)gun.AmmoInMagazine / Mathf.Max(1, gun.MagazineSize) : 0f;
            bb.reserveAmmo = gun != null ? gun.ReserveAmmo : 0;
            int lowAmmoCutoff = gun != null ? Mathf.Max(12, gun.MagazineSize) : 0;
            int maxCarry = gun != null && gun.Definition != null ? gun.MagazineSize + gun.Definition.startingReserve : 0;
            bb.lowAmmo = gun != null && gun.ReserveAmmo <= lowAmmoCutoff && (gun.AmmoInMagazine + gun.ReserveAmmo < maxCarry);
            bb.isReloading = gun != null && gun.IsReloading;
            bb.effectiveRange = gun != null && gun.Definition != null ? gun.Definition.effectiveRange : 30f;
            bb.lastDamageTime = health != null ? health.LastDamageTime : -999f;
            bb.recentlyDamaged = now - bb.lastDamageTime < 1.5f;
            bb.lastDamageDirection = Perception.LastDamageDirection;
            bb.underFire = bb.recentlyDamaged || now - Perception.LastNearMissTime < 1.2f;
            bb.knownEnemies = Perception.KnownEnemyCount;
            bb.squadLeader = _leader;

            var best = Perception.BestThreat;
            if (best != null && best.combatant != null)
            {
                bb.target = best.combatant;
                bb.targetVisible = best.visible;
                bb.targetLastKnown = best.visible ? best.combatant.Position : best.lastKnownPosition;
                bb.targetLastSeenTime = best.visible ? now : best.LastInfoTime;
                bb.targetDistance = Vector3.Distance(transform.position, bb.targetLastKnown);
            }
            else
            {
                bb.target = null;
                bb.targetVisible = false;
            }

            bb.hasNoise = Perception.HasNoise;
            bb.noisePosition = Perception.NoisePosition;
            bb.noiseTime = Perception.NoiseTime;
            if (bb.hasDirective && now > bb.directiveUntil) bb.hasDirective = false;
        }

        private void PollDirective(float now)
        {
            if (directiveProvider == null || now < _nextDirectivePoll) return;
            _nextDirectivePoll = now + directivePollInterval;
            if (directiveProvider is IBotDirectiveProvider provider && provider.TryGetDirective(this, Blackboard, out var d))
                IssueDirective(d);
        }

        // ------------------------------------------------------------------ navigation
        private Vector3 UpdateNavigation(float now, float dt)
        {
            if (Agent == null || !Agent.isActiveAndEnabled) return Vector3.zero;

            if (!Agent.isOnNavMesh)
            {
                if (NavMesh.SamplePosition(transform.position, out var hit, 6f, NavMesh.AllAreas))
                {
                    Vector3 toNav = hit.position - transform.position;
                    float vertDist = Mathf.Abs(toNav.y);
                    toNav.y = 0f;
                    if (Soldier.Movement.IsGrounded && toNav.magnitude < 0.35f && vertDist < 2.2f)
                    {
                        Agent.Warp(hit.position);
                        _lastSteer = Vector3.zero;
                        return Vector3.zero;
                    }
                    _lastSteer = toNav.sqrMagnitude > 0.001f ? toNav.normalized : Vector3.zero;
                    return _lastSteer;
                }
                _lastSteer = Vector3.zero;
                return Vector3.zero;
            }

            Agent.nextPosition = transform.position;
            if ((Agent.nextPosition - transform.position).sqrMagnitude > 1f) Agent.Warp(transform.position);

            if (!_intent.hasDestination)
            {
                if (Agent.hasPath) Agent.ResetPath();
                _lastSteer = Vector3.zero;
                return Vector3.zero;
            }

            if ((_intent.destination - _lastDestination).sqrMagnitude > 1f || now >= _nextRepath)
            {
                _lastDestination = _intent.destination;
                _nextRepath = now + 0.6f;
                if (NavMesh.SamplePosition(_intent.destination, out var dest, 3f, NavMesh.AllAreas))
                    Agent.SetDestination(dest.position);
            }

            if (Agent.isOnOffMeshLink)
            {
                var link = Agent.currentOffMeshLinkData;
                Vector3 toEnd = link.endPos - transform.position;
                toEnd.y = 0f;
                if (toEnd.magnitude < 0.6f) Agent.CompleteOffMeshLink();
                _lastSteer = toEnd.sqrMagnitude > 0.001f ? toEnd.normalized : Vector3.zero;
                return _lastSteer;
            }

            if (Agent.pathPending) return _lastSteer;
            if (!Agent.hasPath || Agent.remainingDistance <= Mathf.Max(Agent.stoppingDistance, 0.6f))
            {
                _lastSteer = Vector3.zero;
                return Vector3.zero;
            }
            Vector3 steer = Agent.steeringTarget - transform.position;
            steer.y = 0f;
            _lastSteer = steer.sqrMagnitude > 0.001f ? steer.normalized : Vector3.zero;
            return _lastSteer;
        }

        private void UpdateStuck(ref SoldierInput input, float now, float dt)
        {
            bool wantsMove = input.move.sqrMagnitude > 0.25f;
            Vector3 vel = Soldier.Movement != null ? Soldier.Movement.Velocity : Vector3.zero;
            vel.y = 0f;
            float speed = vel.magnitude;
            if (wantsMove && speed < 0.4f && Soldier.Movement.IsGrounded) _stuckTimer += dt;
            else _stuckTimer = Mathf.Max(0f, _stuckTimer - dt * 2f);

            if (_stuckTimer > 1.1f && _stuckTimer < 1.2f) input.jump = true; // hop / mantle over the obstacle
            if (_stuckTimer > 2.4f)
            {
                _stuckTimer = 0f;
                _nextRepath = 0f;
                if (Agent != null && Agent.isOnNavMesh) Agent.ResetPath();
            }
        }

        // ------------------------------------------------------------------ aim (human mouse deltas + realistic recoil pull-down)
        private void UpdateAim(ref SoldierInput input, Vector3 worldMove, float now, float dt)
        {
            var bb = Blackboard;
            var d = ActiveDifficulty;
            Vector3 eye = Soldier.AimOrigin;
            Combatant target = _intent.target != null ? _intent.target : bb.target;
            bool engaging = _intent.goal == BotGoal.Engage || (bb.targetVisible && target != null && _intent.allowFire);

            // Decay burst fatigue and combat stress smoothly; rise when firing or under incoming bullets
            _sprayHeat = Mathf.MoveTowards(_sprayHeat, 0f, dt * 0.95f);
            float stressTarget = bb.recentlyDamaged ? 1f : (bb.underFire ? 0.65f : 0f);
            _combatStress = Mathf.MoveTowards(_combatStress, stressTarget, dt * (stressTarget > _combatStress ? 5.0f : 0.7f));

            // Context-aware perceptual-cognitive reaction latency & 125 ms visuomotor target velocity lag (CSKnow / Aim Lab).
            if (bb.targetVisible && target != null)
            {
                if (target != _lastTarget || (!_wasVisible && now - _lostSightTime > 1.5f))
                {
                    float baseReact = d != null ? d.RandomReaction() : 0.4f;
                    float eccDeg = Vector3.Angle(Soldier.AimDirection, (target.ChestPosition - eye));
                    float eccFactor = Mathf.Lerp(0.76f, 1.58f, Mathf.Clamp01(eccDeg / 115f));
                    _reactionReadyAt = now + baseReact * eccFactor;
                    _aimError = d != null ? d.initialAimError : 4f;
                    _useHead = d != null && Random.value < d.headshotChance;
                    _flickActive = false; // Allow immediate ballistic saccade toward the newly acquired contact
                    if (target != _lastTarget) _perceivedTargetVel = Vector3.zero;
                }
                _perceivedTargetVel = Vector3.Lerp(_perceivedTargetVel, target.Velocity, dt / (0.125f + dt));
                _lastTarget = target;
            }
            else
            {
                _perceivedTargetVel = Vector3.Lerp(_perceivedTargetVel, Vector3.zero, dt / (0.125f + dt));
            }
            if (_wasVisible && !bb.targetVisible) _lostSightTime = now;
            _wasVisible = bb.targetVisible;

            Vector3 lookPoint;
            bool hasLook = true;
            if (engaging && target != null && bb.targetVisible)
            {
                Vector3 aimPoint = _useHead ? target.HeadPosition : target.ChestPosition;
                aimPoint += _perceivedTargetVel * 0.055f; // human lagged velocity lead (trailing-edge misses on counter-strafe)
                lookPoint = aimPoint;
            }
            else if (_intent.hasLookPoint)
            {
                lookPoint = _intent.lookPoint;
                // D61: search gaze stays near the horizon. A look point a few metres away (or one the bot has reached) is only
                // a direction to scan; aiming at its height made a bot standing on the spot look straight up or down
                // ("sometimes looks up instead of searching me", user 11-18).
                Vector3 flat = lookPoint - eye;
                flat.y = 0f;
                float horiz = flat.magnitude;
                if (!engaging && horiz < 3f)
                {
                    if (now >= _nextGlanceAt)
                    {
                        _nextGlanceAt = now + 0.45f + HumanPause();  // B118: human rhythm, not a metronome
                        _glanceOffsetYaw = NextScanOffset(78f);      // clear the angles around the spot, sweeping
                    }
                    Vector3 dir = horiz > 0.3f ? flat / horiz : transform.forward;
                    dir = Quaternion.AngleAxis(_glanceOffsetYaw, Vector3.up) * dir;
                    lookPoint = eye + dir * 8f;
                    lookPoint.y = eye.y - 0.12f;
                }
                else if (!engaging)
                {
                    float rise = Mathf.Min(horiz * 0.18f, 1.0f);  // about 10 deg up at most
                    float drop = Mathf.Min(horiz * 0.40f, 2.5f);  // about 22 deg down
                    lookPoint.y = Mathf.Clamp(lookPoint.y, eye.y - drop, eye.y + rise);
                }
            }
            else if (Agent != null && Agent.hasPath && Agent.path != null && Agent.path.corners != null && Agent.path.corners.Length >= 2)
            {
                // Slice corners & periodically scan open flanks instead of staring blindly at feet
                if (now >= _nextGlanceAt)
                {
                    _nextGlanceAt = now + 0.6f + HumanPause(0.5f);
                    _glanceOffsetYaw = Random.value < 0.55f ? Mathf.Clamp(Gauss() * 16f, -32f, 32f) : 0f;
                }
                Vector3 corner = Agent.path.corners[1];
                Vector3 lookDir = corner - eye;
                lookDir.y = 0f;
                if (lookDir.sqrMagnitude < 0.25f && worldMove.sqrMagnitude > 0.01f) lookDir = worldMove;
                if (lookDir.sqrMagnitude > 0.01f)
                    lookDir = Quaternion.AngleAxis(_glanceOffsetYaw, Vector3.up) * lookDir.normalized;
                else
                    lookDir = transform.forward;
                lookPoint = eye + lookDir * 8f;
                lookPoint.y = eye.y - 0.12f; // natural head-height horizon gaze
            }
            else if (worldMove.sqrMagnitude > 0.01f)
            {
                lookPoint = eye + worldMove * 6f;
                lookPoint.y = eye.y - 0.10f;
            }
            else
            {
                lookPoint = eye + Soldier.AimDirection * 6f;
                hasLook = false;
            }

            Vector2 desired = LR2Math.DirectionToYawPitch(lookPoint - eye);
            if (!hasLook) desired.y = Mathf.Lerp(Soldier.Pitch, 0f, 0.05f);

            // Dynamic human accuracy & intensity modulation: weapon spread, spray heat, movement, stance, and stress
            if (d != null)
            {
                var gun = Soldier.ActiveFirearm;
                float gunSpread = gun != null ? gun.CurrentSpread : 0.8f;
                float settle = d.settledAimError + _sprayHeat * 1.35f + Mathf.Max(0f, gunSpread - 0.8f) * 0.45f;
                if (target != null)
                {
                    Vector3 lateral = Vector3.ProjectOnPlane(_perceivedTargetVel, (lookPoint - eye).normalized);
                    settle += Mathf.Clamp(lateral.magnitude * 0.25f, 0f, 2f) * (1f - 0.6f * d.composure);
                }
                // D61: composure damps the stress and self-movement terms (Veteran 0.7)
                settle += _combatStress * 1.15f * (1f - d.composure);
                if (Soldier.Movement != null && Soldier.Movement.Speed > 2f) settle += 0.45f * (1f - 0.5f * d.composure);
                if (Soldier.IsCrouching || Soldier.IsAiming) settle *= 0.80f;
                _aimError = Mathf.MoveTowards(_aimError, settle, d.errorDecay * dt);

                // ESTA / Aim Lab 1.55:1 wrist-yaw vs vertical-arm error anisotropy + late-spray horizontal weave
                float swayFreq = 1.65f + _combatStress * 1.4f + _sprayHeat * 0.9f;
                float t = now * swayFreq;
                _sdnPhase += dt * (7.5f + _combatStress * 4f);
                desired.x += (Mathf.PerlinNoise(t, _noiseSeed) - 0.5f) * 2f * _aimError * 1.15f;
                desired.y += (Mathf.PerlinNoise(_noiseSeed, t * 1.13f) - 0.5f) * 2f * _aimError * 0.74f;
                if (_sprayHeat > 0.25f)
                    desired.x += Mathf.Sin(_sdnPhase * 2.2f + _noiseSeed) * 0.65f * (_sprayHeat - 0.25f);
            }
            else
            {
                _sdnPhase += dt * (7.5f + _combatStress * 4f);
            }

            // Human biomechanical aiming (pure input.look mouse deltas, NEVER absoluteLook teleport):
            // 1. Meyer Optimized Submovement + Flash-Hogan Minimum Jerk + Fitts's Law for large saccadic flicks (> 10.5 deg)
            // 2. Empirical ESTA/CSKnow/Aim Lab anisotropic endpoint covariance (sigma_along ~ 2.27 * sigma_perp, primary undershoot bias)
            // 3. Harris-Wolpert Signal-Dependent Noise (variance proportional to motor angular speed, 1.55:1 yaw/pitch wrist/arm ratio)
            // 4. Human recoil pull-down after 65-135ms cognitive latency
            _aimYaw = Soldier.Yaw;
            _aimPitch = Soldier.Pitch;
            float w = d != null ? d.aimSpeed : 6f;
            float rawYawErr = Mathf.DeltaAngle(_aimYaw, desired.x);
            float rawPitchErr = desired.y - _aimPitch;
            float angularDist = Mathf.Sqrt(rawYawErr * rawYawErr + rawPitchErr * rawPitchErr);

            // Start a ballistic primary submovement when snapping onto a distant angle (refractory cooldown 110-165ms prevents robot jitter)
            if (!_flickActive && angularDist > 10.5f && now >= _nextFlickAllowedAt)
            {
                float targetDist = target != null ? Mathf.Max(1.5f, bb.targetDistance) : 12f;
                float halfWidthDeg = Mathf.Atan2(_useHead ? 0.16f : 0.34f, targetDist) * Mathf.Rad2Deg;
                float id = Mathf.Log(1f + angularDist / Mathf.Max(0.4f, halfWidthDeg), 2f); // Fitts's ID = log2(D/W + 1)
                float skillScale = Mathf.Clamp(7.5f / Mathf.Max(2.5f, w), 0.62f, 1.55f);
                _flickCasual = !LegacyAim && !(engaging && bb.targetVisible && target != null); // looking around, not acquiring someone
                _flickDuration = _flickCasual ? HumanLookDuration(angularDist, w)
                                              : Mathf.Clamp((0.065f + 0.042f * id) * skillScale, 0.085f, 0.38f);
                _flickStartTime = now;
                _flickStartYaw = _aimYaw;
                _flickStartPitch = _aimPitch;

                // Meyer (1988) / van Beers (2004) / Aim Lab anisotropic flick endpoint ellipse aligned to flick direction (ux, uy)
                float invDist = 1f / Mathf.Max(0.001f, angularDist);
                float ux = rawYawErr * invDist;
                float uy = rawPitchErr * invDist;
                float nx = -uy;
                float ny = ux;
                float composure = d != null ? d.composure : 0.25f;
                float sdnSigma = Mathf.Clamp(angularDist * (0.045f + 0.045f * _combatStress * (1f - composure)) * skillScale, 0.35f, 6.5f);
                float muAlong = Mathf.Lerp(-0.075f, 0.035f, _combatStress) * angularDist;
                // B89: now and then a fast flick sails past the target and corrects back (more often under stress)
                float overChance = (d != null ? d.overshootChance : 0.16f) * Mathf.Lerp(1f, 1.4f, _combatStress);
                _flickOvershoot = angularDist > 14f && Random.value < overChance;
                if (_flickOvershoot)
                {
                    muAlong = Random.Range(0.05f, 0.14f) * Mathf.Lerp(1f, 1.25f, _combatStress) * angularDist;
                    _statOvershoots++;
                }
                // B89: a pro may already pull the trigger as the flick arrives (only onto a visible target)
                _flickPreFire = engaging && bb.targetVisible && target != null && d != null && Random.value < d.preFireChance;
                _statFlicks++;
                float epsAlong = muAlong + ((Random.value + Random.value) - 1f) * sdnSigma;
                float epsPerp = ((Random.value + Random.value) - 1f) * (0.44f * sdnSigma);
                float endErrYaw = (ux * epsAlong + nx * epsPerp) * 1.08f;
                float endErrPitch = (uy * epsAlong + ny * epsPerp) * 0.70f;
                _flickGoalYaw = _aimYaw + rawYawErr + endErrYaw;
                _flickGoalPitch = Mathf.Clamp(_aimPitch + rawPitchErr + endErrPitch, -82f, 82f);
                if (_flickCasual)
                {
                    // B118: the primary movement covers ~92 % of the way (the rest is a correction after a short dwell)
                    float cover = Mathf.Clamp(0.92f + 0.09f * Gauss(), 0.75f, 1.02f);
                    _flickGoalYaw = _aimYaw + rawYawErr * cover + nx * 0.02f * angularDist * Gauss();
                    _flickGoalPitch = Mathf.Clamp(_aimPitch + rawPitchErr * cover + ny * 0.02f * angularDist * Gauss(), -82f, 82f);
                    _flickOvershoot = false;
                    _flickPreFire = false;
                }
                // B118: the path bows sideways (half-normal, median ~8 % of the distance for casual looks, half that in a fight)
                _flickCurve = LegacyAim ? 0f : angularDist * Mathf.Min(Mathf.Abs(Gauss()) * (_flickCasual ? 0.1f : 0.05f), _flickCasual ? 0.25f : 0.12f)
                              * (Random.value < 0.5f ? -1f : 1f);
                _flickPerpYaw = nx;
                _flickPerpPitch = ny;
                _flickActive = true;
                _nextFlickAllowedAt = now + _flickDuration + Random.Range(0.11f, 0.165f); // human visual feedback refractory period
            }

            float yawStepDeg, pitchStepDeg;
            // B118: a casual look never delays a reaction: an enemy in sight ends it at once (a combat flick takes over)
            if (_flickActive && _flickCasual && engaging && bb.targetVisible && target != null) { _flickActive = false; _nextFlickAllowedAt = now; }
            if (engaging) _lookHoldUntil = 0f;
            if (_flickActive)
            {
                float u = Mathf.Clamp01((now - _flickStartTime) / Mathf.Max(0.05f, _flickDuration));
                // B118: human speed profile fitted to real mouse movements (peak at ~1/3, long deceleration), replacing the
                // symmetric Flash & Hogan minimum-jerk quintic; the path bows sideways like a wrist arc
                float s = LegacyAim ? u * u * u * (10f + u * (-15f + 6f * u)) : HumanProfile(u);
                float bow = _flickCurve * Mathf.Sin(Mathf.PI * s);
                float subYaw = Mathf.LerpAngle(_flickStartYaw, _flickGoalYaw, s) + _flickPerpYaw * bow;
                float subPitch = Mathf.Lerp(_flickStartPitch, _flickGoalPitch, s) + _flickPerpPitch * bow * 0.7f;
                yawStepDeg = Mathf.DeltaAngle(_aimYaw, subYaw);
                pitchStepDeg = subPitch - _aimPitch;

                // Synchronize spring velocity so handoff from ballistic flick into closed-loop tracking has zero jerk spike
                _yawVel = yawStepDeg / Mathf.Max(0.001f, dt);
                _pitchVel = pitchStepDeg / Mathf.Max(0.001f, dt);

                _flickProgress = u;
                // an overshooting flick runs to its end point (passing through the target on the way)
                if (u >= 1f || (!_flickOvershoot && angularDist < 2.2f))
                {
                    _flickActive = false;
                    // B118: 71 % of human movements pause 60-140 ms before the corrective submovement
                    if (_flickCasual && Random.value < 0.71f) _lookHoldUntil = now + Random.Range(0.06f, 0.14f);
                }
            }
            else
            {
                // Closed-loop foveal tracking + corrective submovement + Harris-Wolpert Signal-Dependent Noise
                float zeta = engaging ? 0.80f : 1f;
                float yawErr = rawYawErr;
                float pitchErr = rawPitchErr;

                // Human recoil pull-down delay & continuous-spray compensation fatigue
                if (pitchErr > 0.2f && now < _recoilReactAt)
                    pitchErr *= 0.12f;
                else if (pitchErr > 0.3f && _sprayHeat > 0.1f)
                {
                    float compEfficiency = d != null ? Mathf.Clamp(d.aimSpeed / 10.5f, 0.55f, 0.92f) : 0.74f;
                    pitchErr *= Mathf.Clamp(compEfficiency * (1f - _sprayHeat * 0.22f), 0.45f, 0.94f);
                }

                // Harris-Wolpert SDN: high motor command magnitude amplifies execution tremor with 1.55:1 wrist/arm ratio
                float cmdMag = Mathf.Sqrt(yawErr * yawErr + pitchErr * pitchErr);
                float sdnAmp = Mathf.Min(1.6f, cmdMag * 0.032f * (0.55f + _combatStress * 0.65f));
                yawErr += Mathf.Sin(_sdnPhase * 1.37f + _noiseSeed) * sdnAmp * 1.15f;
                pitchErr += Mathf.Cos(_sdnPhase * 1.71f + _noiseSeed) * sdnAmp * 0.74f;

                if (now < _lookHoldUntil && !engaging) { yawErr = 0f; pitchErr = 0f; _yawVel = 0f; _pitchVel = 0f; } // the dwell
                _yawVel += (w * w * yawErr - 2f * zeta * w * _yawVel) * dt;
                _pitchVel += (w * w * pitchErr - 2f * zeta * w * _pitchVel) * dt;
                _yawVel = Mathf.Clamp(_yawVel, -900f, 900f);
                _pitchVel = Mathf.Clamp(_pitchVel, -600f, 600f);
                yawStepDeg = _yawVel * dt;
                pitchStepDeg = _pitchVel * dt;
            }

            float maxStep = Soldier.maxBotTurnSpeed * dt;
            input.absoluteLook = false;
            input.look = new Vector2(
                Mathf.Clamp(yawStepDeg, -maxStep, maxStep),
                Mathf.Clamp(pitchStepDeg, -maxStep, maxStep));
        }

        // ------------------------------------------------------------------ trigger, ADS, stance
        private void UpdateCombatInputs(ref SoldierInput input, ref Vector3 worldMove, float now)
        {
            var bb = Blackboard;
            var d = ActiveDifficulty;
            var gun = Soldier.ActiveFirearm;
            Combatant target = bb.target;
            float dist = bb.targetDistance;
            bool visible = bb.targetVisible && target != null;

            // Reload discipline (top off bot reserve if completely dry so long matches stay active).
            if (gun != null && !gun.IsReloading)
            {
                if (gun.AmmoInMagazine <= 0 && gun.ReserveAmmo <= 0 && gun.Definition != null)
                {
                    gun.AddReserveAmmo(gun.Definition.startingReserve);
                    input.reload = true;
                }
                else if (gun.AmmoInMagazine <= 0 && gun.ReserveAmmo > 0) input.reload = true;
                else if (!visible && bb.ammo01 < 0.35f && bb.TimeSinceSeen > 1.5f && gun.ReserveAmmo > 0) input.reload = true;
            }

            bool fire = false;
            if (visible && _intent.allowFire && gun != null && !gun.IsReloading && now >= _reactionReadyAt)
            {
                Vector3 toTarget = (target.ChestPosition - Soldier.AimOrigin);
                float angle = Vector3.Angle(Soldier.AimDirection, toTarget);
                float tolerance = Mathf.Max(d != null ? d.fireTolerance : 3.5f, Mathf.Atan2(0.45f, Mathf.Max(dist, 0.5f)) * Mathf.Rad2Deg);
                // B89 flick pre-fire: the shot is timed to the arrival of the flick, before the aim has settled on target
                bool flickShot = _flickActive && _flickPreFire && _flickProgress >= 0.72f
                    && Vector3.Angle(Quaternion.Euler(_flickGoalPitch, _flickGoalYaw, 0f) * Vector3.forward, toTarget) <= tolerance * 1.5f;
                if (flickShot && _burstShotsLeft <= 0) _statFlickPreFires++;
                if (angle <= tolerance || flickShot)
                {
                    if (dist < 13f) fire = true; // full auto up close
                    else if (now >= _burstPauseUntil)
                    {
                        if (_burstShotsLeft <= 0)
                            _burstShotsLeft = d != null ? Random.Range(d.burstSize.x, d.burstSize.y + 1) : 4;
                        fire = true;
                    }
                }
            }
            else if (!visible && _intent.allowFire && target != null && gun != null && !gun.IsReloading &&
                     bb.TimeSinceSeen < 0.85f && bb.ammo01 > 0.35f && d != null && d.aggression > 0.45f &&
                     now >= _burstPauseUntil && Vector3.Angle(Soldier.AimDirection, (bb.targetLastKnown + Vector3.up * 1.3f) - Soldier.AimOrigin) < 8f)
            {
                // Brief suppressive burst on the corner where the target just ducked into cover
                if (_burstShotsLeft <= 0) _burstShotsLeft = 2;
                fire = true;
            }
            else if (!visible && _intent.allowFire && gun != null && !gun.IsReloading && d != null && d.preFireChance > 0f
                     && bb.ammo01 > 0.3f && now >= _burstPauseUntil && PreFireExpectedSpot(bb, d, now))
            {
                // B89 corner pre-fire: a short burst at the spot where the target is expected, as that spot comes into view
                if (_burstShotsLeft <= 0) _burstShotsLeft = Random.Range(2, 4);
                fire = true;
            }
            input.fire = fire;

            // ADS beyond close range, or when pre-aiming a corner during a hunt or recently lost cover edge.
            bool wantAds = (visible && dist > 9f) ||
                           (_intent.goal == BotGoal.Hunt && dist < 25f && dist > 6f) ||
                           (!visible && target != null && bb.TimeSinceSeen < 1.8f && dist < 28f);
            input.aim = wantAds && !input.sprint;

            // Combat strafing (ADAD) and range keeping.
            if (_intent.goal == BotGoal.Engage && visible)
            {
                if (now >= _nextStrafeSwitch)
                {
                    float chance = d != null ? d.strafeChance : 0.4f;
                    _strafeDir = Random.value < chance ? (Random.value < 0.5f ? -1f : 1f) : 0f;
                    _nextStrafeSwitch = now + (d != null ? Random.Range(d.strafeInterval.x, d.strafeInterval.y) : 0.8f);
                }
                Vector3 right = transform.right;
                worldMove = worldMove * 0.65f + right * _strafeDir * 0.85f;

                // Slide into close fights (veterans).
                if (!_slideUsed && d != null && dist < 14f && dist > 6f && Soldier.IsSprinting && Random.value < d.slideChance)
                {
                    input.crouch = true;
                    input.fire = false;
                    input.aim = false;
                    _slideFrame = true;
                    _slideUsed = true;
                }
            }
            else
            {
                _strafeDir = 0f;
                if (!visible) _slideUsed = false;
            }

            // Crouch behind waist-high cover, stand up to peek/shoot.
            bool wantCrouch = _intent.crouch;
            if (wantCrouch && visible)
            {
                if (now >= _nextPeekToggle)
                {
                    _peekStanding = !_peekStanding;
                    _nextPeekToggle = now + (_peekStanding ? Random.Range(1.2f, 2.4f) : Random.Range(0.6f, 1.2f));
                }
                wantCrouch = !_peekStanding;
            }
            else if (!wantCrouch && visible && d != null && dist > 15f && Random.value < d.crouchPeekChance * Time.deltaTime)
            {
                wantCrouch = true; // occasional crouch-spot while trading at range
            }
            if (wantCrouch != Soldier.IsCrouching && now - _lastCrouchToggle > 0.45f && !Soldier.IsSprinting && !input.crouch)
            {
                input.crouch = true;
                _lastCrouchToggle = now;
            }
        }

        private void EnsureGunSubscription()
        {
            var gun = Soldier.ActiveFirearm;
            if (gun == _subscribedGun) return;
            if (_subscribedGun != null) _subscribedGun.Fired -= OnGunFired;
            _subscribedGun = gun;
            if (gun != null) gun.Fired += OnGunFired;
        }

        private void EnsureHealthSubscription()
        {
            var health = Soldier != null ? Soldier.Health : null;
            if (health == _subscribedHealth) return;
            if (_subscribedHealth != null)
            {
                _subscribedHealth.ArmorBroken -= OnArmorBroken;
                _subscribedHealth.ArmorRestored -= OnArmorRestored;
            }
            _subscribedHealth = health;
            if (health != null)
            {
                health.ArmorBroken += OnArmorBroken;
                health.ArmorRestored += OnArmorRestored;
                _armorBroken = health.HasArmor && health.Armor <= 0.001f;
            }
            else
            {
                _armorBroken = false;
            }
        }

        private void OnArmorBroken(DamageInfo _) => _armorBroken = true;

        private void OnArmorRestored(float _)
        {
            if (_subscribedHealth != null && _subscribedHealth.Armor > 0.001f)
                _armorBroken = false;
        }

        /// <summary>True only when the bot is completely out of combat and not handling an urgent objective.</summary>
        /// <summary>
        /// B89 corner pre-fire. A skilled player rounding a corner onto the spot where the enemy was last seen fires a short
        /// burst the moment that spot comes into view, before seeing anyone there. Rolled once per corner (when the line to the
        /// expected spot goes from blocked to clear), only when the bot is already pre-aimed there and the contact is fresh.
        /// </summary>
        private bool PreFireExpectedSpot(BotBlackboard bb, BotDifficulty d, float now)
        {
            if (now < _preFireUntil) return true;
            if (bb.target == null || bb.TimeSinceSeen > 5f || bb.TimeSinceSeen < 0.85f) { _expectedSpotWasHidden = false; return false; }
            Vector3 eye = Soldier.AimOrigin;
            Vector3 spot = bb.targetLastKnown + Vector3.up * 1.3f;
            float dist = Vector3.Distance(eye, spot);
            if (dist < 3f || dist > 30f) { _expectedSpotWasHidden = false; return false; }
            bool clear = !Physics.Linecast(eye, spot, LR2Layers.VisionMask, QueryTriggerInteraction.Ignore);
            bool justOpened = clear && _expectedSpotWasHidden;
            _expectedSpotWasHidden = !clear;
            if (!justOpened) return false;
            if (Vector3.Angle(Soldier.AimDirection, spot - eye) > 7f) return false;
            if (Random.value >= d.preFireChance) return false;
            _preFireUntil = now + Random.Range(0.18f, 0.32f);
            _statCornerPreFires++;
            return true;
        }

        public static bool IsDisengagedForResupply(BotBlackboard bb, BotIntent brainIntent, Combatant self = null)
        {
            if (bb == null) return false;
            if (bb.targetVisible || bb.underFire || bb.recentlyDamaged) return false;
            if (bb.target != null && bb.TimeSinceSeen <= 2.5f) return false;
            if (bb.hasDirective && bb.directive.order != BotOrder.None && bb.directive.order != BotOrder.FreeRoam) return false;
            switch (brainIntent.goal)
            {
                case BotGoal.Idle:
                case BotGoal.Patrol:
                case BotGoal.FollowLeader:
                    return true;
                case BotGoal.MoveTo:
                    var flag = FlagZoneController.Instance;
                    if (flag != null && (flag.State == FlagZoneController.ZoneState.Contested ||
                        (self != null && flag.IsHostileCapturingOrContesting(self, out float urgency) && urgency >= 0.35f)))
                        return false;
                    return true;
                default:
                    return false;
            }
        }

        private void TryApplyDisengagedResupplyGoal(ref BotIntent intent, float now)
        {
            var bb = Blackboard;
            if ((!bb.armorBroken && !bb.lowAmmo) || !IsDisengagedForResupply(bb, intent, Combatant))
            {
                _resupplyCrate = null;
                return;
            }

            if (_resupplyCrate != null && (!_resupplyCrate.isActiveAndEnabled || !_resupplyCrate.IsReady))
                _resupplyCrate = null;

            if (_resupplyCrate == null && now >= _nextCrateScanAt)
            {
                _nextCrateScanAt = now + 0.8f;
                if (!TryPickReadyAmmoCrate(out _resupplyCrate, out _resupplyDestination))
                    return;
            }

            if (_resupplyCrate == null) return;

            intent.goal = BotGoal.Resupply;
            intent.hasDestination = true;
            intent.destination = _resupplyDestination;
            intent.allowSprint = Vector3.Distance(bb.position, _resupplyDestination) > 6f;
            intent.crouch = false;
            intent.allowFire = true;
            intent.target = null;
        }

        private bool TryPickReadyAmmoCrate(out AmmoCrate chosen, out Vector3 destination)
        {
            chosen = null;
            destination = Vector3.zero;
            float bestDist = 85f;
            Vector3 myPos = transform.position;

            for (int i = 0; i < AmmoCrate.All.Count; i++)
            {
                var crate = AmmoCrate.All[i];
                if (crate == null || !crate.isActiveAndEnabled || !crate.IsReady) continue;

                Vector3 cratePos = crate.transform.position;
                float dist = Vector3.Distance(myPos, cratePos);
                if (dist > bestDist) continue;

                Vector3 sampled = cratePos;
                if (NavMesh.SamplePosition(cratePos, out var centerHit, 0.85f, NavMesh.AllAreas))
                {
                    sampled = centerHit.position;
                }
                else
                {
                    Vector3 toBot = myPos - cratePos;
                    toBot.y = 0f;
                    Vector3 side = toBot.sqrMagnitude > 0.01f ? toBot.normalized * 0.6f : Vector3.forward * 0.6f;
                    if (!NavMesh.SamplePosition(cratePos + side, out var sideHit, 1.0f, NavMesh.AllAreas))
                        continue;
                    sampled = sideHit.position;
                }

                bestDist = dist;
                chosen = crate;
                destination = sampled;
            }

            return chosen != null;
        }

        private void OnGunFired()
        {
            var d = ActiveDifficulty;
            // Track sustained fire recoil stress & reaction lag (~65-135ms) before human hand starts pulling mouse down
            bool firstShotOfBurst = _sprayHeat < 0.08f;
            _sprayHeat = Mathf.Clamp01(_sprayHeat + (Soldier.IsAiming ? 0.13f : 0.19f));
            if (firstShotOfBurst)
                _recoilReactAt = Time.time + Random.Range(0.065f, 0.135f);

            if (Blackboard.targetDistance < 13f && _sprayHeat < 0.72f) return;
            _burstShotsLeft--;
            if (_burstShotsLeft <= 0)
            {
                _burstPauseUntil = Time.time + (d != null ? Random.Range(d.burstPause.x, d.burstPause.y) : 0.3f);
            }
        }

        // ------------------------------------------------------------------ debug
        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos) return;
            Vector3 eye = transform.position + Vector3.up * 1.6f;
            float half = (difficulty != null ? difficulty.fieldOfView : 110f) * 0.5f;
            float range = difficulty != null ? difficulty.visionRange : 70f;
            Vector3 fwd = Application.isPlaying && Combatant != null ? Combatant.ViewDirection : transform.forward;
            Gizmos.color = new Color(1f, 1f, 0f, 0.6f);
            Gizmos.DrawLine(eye, eye + Quaternion.AngleAxis(-half, Vector3.up) * fwd * Mathf.Min(range, 25f));
            Gizmos.DrawLine(eye, eye + Quaternion.AngleAxis(half, Vector3.up) * fwd * Mathf.Min(range, 25f));
            if (!Application.isPlaying) return;
            if (Blackboard.target != null)
            {
                Gizmos.color = Blackboard.targetVisible ? Color.red : new Color(1f, 0.5f, 0f);
                Gizmos.DrawWireSphere(Blackboard.targetLastKnown + Vector3.up, 0.5f);
                Gizmos.DrawLine(eye, Blackboard.targetLastKnown + Vector3.up);
            }
            if (Agent != null && Agent.hasPath)
            {
                Gizmos.color = Color.cyan;
                var corners = Agent.path.corners;
                for (int i = 1; i < corners.Length; i++) Gizmos.DrawLine(corners[i - 1], corners[i]);
            }
            if (_intent.hasDestination)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawWireCube(_intent.destination, Vector3.one * 0.4f);
            }
        }

        private void OnGUI()
        {
            if (!showDebugLabel) return;
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 sp = cam.WorldToScreenPoint(transform.position + Vector3.up * 2.3f);
            if (sp.z < 0f) return;
            GUI.Label(new Rect(sp.x - 150f, Screen.height - sp.y - 20f, 300f, 40f), DescribeState());
        }
    }
}
