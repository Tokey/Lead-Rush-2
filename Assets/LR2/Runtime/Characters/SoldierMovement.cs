// Based on KINEMATION FPS Animation Framework demo FPSMovement (2025), adapted for LR2:
// input is pushed by SoldierController (human or bot), plus mantling, CoD-style speed modifiers, footsteps and noise.
using System;
using Demo.Scripts.Runtime.Character;
using KINEMATION.FPSAnimationFramework.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Input;
using LR2.Combat;
using LR2.Core;
using LR2.FX;
using UnityEngine;

namespace LR2.Characters
{
    [RequireComponent(typeof(CharacterController))]
    public class SoldierMovement : MonoBehaviour
    {
        public delegate bool ActionConditionDelegate();
        public delegate void OnActionCallback();

        [SerializeField] private FPSMovementSettings movementSettings;

        [Header("LR2 Speed Modifiers")]
        [Tooltip("Speed multiplier while aiming down sights.")]
        [SerializeField, Range(0.2f, 1f)] private float adsSpeedMultiplier = 0.58f;
        [Tooltip("Speed multiplier when moving backwards.")]
        [SerializeField, Range(0.4f, 1f)] private float backwardsMultiplier = 0.82f;
        [Tooltip("Global multiplier applied to all gaits (tune overall pace).")]
        [SerializeField, Range(0.5f, 1.5f)] private float paceMultiplier = 1f;

        [Header("Mantle")]
        [SerializeField] private bool enableMantle = true;
        [SerializeField] private float mantleMinHeight = 0.45f;
        [SerializeField] private float mantleMaxHeight = 1.85f;
        [SerializeField] private float mantleReach = 0.85f;
        [SerializeField] private float mantleDuration = 0.42f;

        [Header("Footsteps")]
        [SerializeField] private float strideWalk = 1.75f;
        [SerializeField] private float strideSprint = 2.5f;
        [SerializeField] private float strideCrouch = 1.25f;
        [SerializeField] private float noiseWalk = 9f;
        [SerializeField] private float noiseSprint = 18f;
        [SerializeField] private float noiseCrouch = 3f;

        [Header("Falling")]
        [Tooltip("Landing speed (m/s) above which fall damage starts.")]
        [SerializeField] private float fallDamageSpeed = 13f;
        [SerializeField] private float fallDamagePerMs = 9f;

        public OnActionCallback onStartMoving;
        public OnActionCallback onStopMoving;
        public OnActionCallback onSprintStarted;
        public OnActionCallback onSprintEnded;
        public OnActionCallback onCrouch;
        public OnActionCallback onUncrouch;
        public OnActionCallback onJump;
        public OnActionCallback onLanded;
        public OnActionCallback onSlideStarted;
        public OnActionCallback onSlideEnded;

        public ActionConditionDelegate _slideActionCondition;
        public ActionConditionDelegate _sprintActionCondition;

        public event Action Jumped;
        public event Action<float> Landed;
        public event Action SlideStarted;
        public event Action MantleStarted;
        /// <summary>Raised on every footfall with the planar speed (foley, AI hearing).</summary>
        public event Action<float> Footstep;

        public SoldierMoveState State { get; private set; }
        public SoldierStance Stance { get; private set; }
        public Vector2 AnimatorVelocity { get; private set; }
        public Vector3 MoveVector { get; private set; }
        public Vector3 Velocity => _controller != null && _controller.enabled ? _controller.velocity : _velocity;
        public bool IsGrounded => _controller != null && _controller.isGrounded;
        public float Speed => new Vector3(_velocity.x, 0f, _velocity.z).magnitude;
        public float SpeedMultiplier { get; set; } = 1f;
        public bool IsAimingSlowdown { get; set; }
        public bool IsMoving => _inputDirection.sqrMagnitude > 0.01f;
        public FPSMovementSettings Settings => movementSettings;

        private CharacterController _controller;
        private UserInputController _inputController;
        private Animator _animator;
        private Combatant _combatant;
        private Health _health;

        private Vector2 _inputDirection;
        private bool _sprintRequested;
        private SoldierMoveState _cachedState;
        private Vector3 _velocity;
        private float _originalHeight;
        private Vector3 _originalCenter;
        private GaitSettings _desiredGait;
        private GaitSettings _cachedGait;
        private float _slideProgress;
        private Vector3 _slideVector;
        private float _gaitProgress;
        private bool _wasMoving;
        private float _strideAccum;
        private float _airborneTime;
        private float _lastFallSpeed;
        private bool _initialized;

        // Mantle
        private Vector3 _mantleStart, _mantleEnd;
        private float _mantleT;

        private static readonly int InAirHash = Animator.StringToHash("InAir");
        private static readonly int MoveXHash = Animator.StringToHash("MoveX");
        private static readonly int MoveYHash = Animator.StringToHash("MoveY");
        private static readonly int VelocityHash = Animator.StringToHash("Velocity");
        private static readonly int MovingHash = Animator.StringToHash("Moving");
        private static readonly int CrouchingHash = Animator.StringToHash("Crouching");
        private static readonly int SprintingHash = Animator.StringToHash("Sprinting");
        private static readonly int LocoSpeedHash = Animator.StringToHash("LocoSpeed");

        [Header("Animation cadence")]
        [Tooltip("Ground speed (m/s) the walk cycle was authored for. Lower walk speeds slow the cycle down instead of skating.")]
        [SerializeField] private float walkAnimReferenceSpeed = 4.6f;
        [SerializeField] private float sprintAnimReferenceSpeed = 6.2f;
        [SerializeField] private float crouchAnimReferenceSpeed = 2.6f;
        private float _locoSpeed = 1f;

        // ------------------------------------------------------------------ API used by SoldierController
        public void SetMoveInput(Vector2 move) => _inputDirection = Vector2.ClampMagnitude(move, 1f);

        public void SetSprintRequested(bool sprint)
        {
            _sprintRequested = sprint;
            if (State is SoldierMoveState.InAir or SoldierMoveState.Sliding or SoldierMoveState.Mantling) return;

            bool canSprint = sprint && CanSprint() && _inputDirection.y > 0.35f;
            if (canSprint && State != SoldierMoveState.Sprinting) State = SoldierMoveState.Sprinting;
            else if (!canSprint && State == SoldierMoveState.Sprinting) State = SoldierMoveState.Walking;
        }

        public void StopSprint()
        {
            _sprintRequested = false;
            if (State == SoldierMoveState.Sprinting) State = SoldierMoveState.Walking;
        }

        public bool RequestJump()
        {
            if (State is SoldierMoveState.Mantling or SoldierMoveState.Sliding) return false;
            if (enableMantle && TryStartMantle()) return true;
            if (State == SoldierMoveState.InAir) return false;
            if (Stance == SoldierStance.Crouching)
            {
                if (CanUnCrouch()) UnCrouch();
                return false;
            }

            State = SoldierMoveState.InAir;
            _velocity.y = movementSettings != null ? movementSettings.jumpHeight : 5.5f;
            Jumped?.Invoke();
            return true;
        }

        /// <summary>Crouch toggle; while sprinting this becomes a slide (CoD style).</summary>
        public void RequestCrouchToggle()
        {
            if (State == SoldierMoveState.Sprinting && CanSlide())
            {
                _slideProgress = 0f;
                State = SoldierMoveState.Sliding;
                return;
            }

            if (State is not (SoldierMoveState.Idle or SoldierMoveState.Walking)) return;

            if (Stance == SoldierStance.Standing)
            {
                Crouch();
                _desiredGait = Gait(GaitKind.Crouch);
                return;
            }

            if (!CanUnCrouch()) return;
            UnCrouch();
            _desiredGait = Gait(GaitKind.Walk);
        }

        public void ForceStand()
        {
            if (Stance == SoldierStance.Crouching && CanUnCrouch()) UnCrouch();
        }

        public void Teleport(Vector3 position)
        {
            bool wasEnabled = _controller.enabled;
            _controller.enabled = false;
            transform.position = position;
            _controller.enabled = wasEnabled;
            ResetState();
        }

        public void ResetState()
        {
            _velocity = Vector3.zero;
            MoveVector = Vector3.zero;
            _inputDirection = Vector2.zero;
            _sprintRequested = false;
            _airborneTime = 0f;
            if (_initialized && Stance == SoldierStance.Crouching) UnCrouch();
            _cachedState = State = SoldierMoveState.Idle;
            _desiredGait = _cachedGait = Gait(GaitKind.Idle);
        }

        // ------------------------------------------------------------------ internals
        private enum GaitKind { Idle, Walk, Sprint, Crouch }

        private GaitSettings Gait(GaitKind kind)
        {
            if (movementSettings == null) return new GaitSettings { velocity = kind == GaitKind.Sprint ? 7f : 4.5f, velocitySmoothing = 10f };
            switch (kind)
            {
                case GaitKind.Walk: return movementSettings.walking;
                case GaitKind.Sprint: return movementSettings.sprinting;
                case GaitKind.Crouch: return movementSettings.crouching;
                default: return movementSettings.idle;
            }
        }

        private bool CanSlide()
        {
            return State == SoldierMoveState.Sprinting && Stance == SoldierStance.Standing
                && (_slideActionCondition == null || _slideActionCondition.Invoke());
        }

        private bool CanSprint()
        {
            bool condition = _sprintActionCondition == null || _sprintActionCondition.Invoke();
            return Stance == SoldierStance.Standing && condition && !IsAimingSlowdown;
        }

        private bool CanUnCrouch()
        {
            float height = _originalHeight - _controller.radius * 2f;
            Vector3 position = transform.TransformPoint(_originalCenter + Vector3.up * height / 2f);
            return !Physics.CheckSphere(position, _controller.radius * 0.95f, GroundMask, QueryTriggerInteraction.Ignore);
        }

        private int GroundMask => movementSettings != null && movementSettings.groundMask.value != 0
            ? movementSettings.groundMask.value | LR2Layers.EnvironmentMask
            : LR2Layers.EnvironmentMask;

        private void Crouch()
        {
            float crouchedHeight = _originalHeight * (movementSettings != null ? movementSettings.crouchRatio : 0.6f);
            float diff = _originalHeight - crouchedHeight;
            _controller.height = crouchedHeight;
            Vector3 center = _originalCenter;
            center.y -= diff / 2f;
            _controller.center = center;
            Stance = SoldierStance.Crouching;
            if (_animator != null) _animator.SetBool(CrouchingHash, true);
            onCrouch?.Invoke();
        }

        private void UnCrouch()
        {
            _controller.height = _originalHeight;
            _controller.center = _originalCenter;
            Stance = SoldierStance.Standing;
            if (_animator != null) _animator.SetBool(CrouchingHash, false);
            onUncrouch?.Invoke();
        }

        private void UpdateMovementState()
        {
            if (State == SoldierMoveState.Mantling) return;
            if (State == SoldierMoveState.Sliding && !Mathf.Approximately(_slideProgress, 1f)) return;
            if (State == SoldierMoveState.InAir) return;

            if (State == SoldierMoveState.Sprinting && _inputDirection.y > 0.35f && Mathf.Abs(_inputDirection.x) < 0.75f
                && _sprintRequested && CanSprint())
                return;

            if (_sprintRequested && CanSprint() && _inputDirection.y > 0.35f)
            {
                State = SoldierMoveState.Sprinting;
                return;
            }

            State = IsMoving ? SoldierMoveState.Walking : SoldierMoveState.Idle;
        }

        private void OnMovementStateChanged()
        {
            if (_cachedState == SoldierMoveState.InAir) onLanded?.Invoke();
            if (_cachedState == SoldierMoveState.Sprinting) onSprintEnded?.Invoke();
            if (_cachedState == SoldierMoveState.Sliding)
            {
                onSlideEnded?.Invoke();
                if (CanUnCrouch()) UnCrouch();
            }

            switch (State)
            {
                case SoldierMoveState.Idle:
                    _desiredGait = Gait(GaitKind.Idle);
                    return;
                case SoldierMoveState.InAir:
                    onJump?.Invoke();
                    return;
                case SoldierMoveState.Mantling:
                    return;
                case SoldierMoveState.Sprinting:
                    _gaitProgress = 0f;
                    _cachedGait = _desiredGait;
                    onSprintStarted?.Invoke();
                    _desiredGait = Gait(GaitKind.Sprint);
                    return;
                case SoldierMoveState.Sliding:
                    _desiredGait.velocitySmoothing = movementSettings != null ? movementSettings.slideDirectionSmoothing : 2f;
                    _slideVector = _velocity.sqrMagnitude > 0.1f ? _velocity : transform.forward * Gait(GaitKind.Sprint).velocity;
                    _slideProgress = 0f;
                    onSlideStarted?.Invoke();
                    SlideStarted?.Invoke();
                    Crouch();
                    return;
            }

            if (Stance == SoldierStance.Crouching)
            {
                _desiredGait = Gait(GaitKind.Crouch);
                return;
            }

            if (_cachedState == SoldierMoveState.Idle)
            {
                _gaitProgress = 0f;
                _cachedGait = _desiredGait;
            }
            _desiredGait = Gait(GaitKind.Walk);
        }

        private float SpeedScale()
        {
            float scale = paceMultiplier * SpeedMultiplier;
            if (IsAimingSlowdown) scale *= adsSpeedMultiplier;
            if (_inputDirection.y < -0.1f) scale *= backwardsMultiplier;
            return scale;
        }

        private void UpdateSliding()
        {
            float curve = movementSettings != null ? movementSettings.slideCurve.Evaluate(_slideProgress) : 1f - _slideProgress;
            float speed = curve * (movementSettings != null ? movementSettings.slideSpeed : 9f);
            _velocity = _slideVector.normalized * speed;
            Vector3 desired = _velocity;
            desired.y = -2f;
            MoveVector = desired;
            _slideProgress = Mathf.Clamp01(_slideProgress + Time.deltaTime);
        }

        private void UpdateGrounded()
        {
            var normInput = _inputDirection;
            var targetDirection = transform.right * normInput.x + transform.forward * normInput.y;

            float maxAccelTime = movementSettings != null ? movementSettings.accelerationCurve.keys[^1].time : 0.2f;
            _gaitProgress = Mathf.Min(_gaitProgress + Time.deltaTime, Mathf.Max(maxAccelTime, 0.0001f));
            float t = movementSettings != null ? movementSettings.accelerationCurve.Evaluate(_gaitProgress) : 1f;
            targetDirection *= Mathf.Lerp(_cachedGait.velocity, _desiredGait.velocity, t) * SpeedScale();

            targetDirection = Vector3.Lerp(_velocity, targetDirection, KMath.ExpDecayAlpha(_desiredGait.velocitySmoothing, Time.deltaTime));
            _velocity = targetDirection;
            targetDirection.y = -2f;
            MoveVector = targetDirection;
        }

        private void UpdateInAir()
        {
            float gravity = movementSettings != null ? movementSettings.gravity : 18f;
            float maxFall = movementSettings != null && movementSettings.maxFallVelocity > 0f ? movementSettings.maxFallVelocity : 40f;
            _velocity.y -= gravity * Time.deltaTime;
            _velocity.y = Mathf.Max(-maxFall, _velocity.y);
            _lastFallSpeed = Mathf.Max(_lastFallSpeed, -_velocity.y);

            var desired = transform.right * _inputDirection.x + transform.forward * _inputDirection.y;
            desired *= _desiredGait.velocity * SpeedScale();
            float airFriction = movementSettings != null ? movementSettings.airFriction : 1f;
            float airVelocity = movementSettings != null ? movementSettings.airVelocity : 2f;
            desired = Vector3.Lerp(_velocity, desired * airFriction, KMath.ExpDecayAlpha(airVelocity, Time.deltaTime));
            desired.y = _velocity.y;
            _velocity = desired;
            MoveVector = desired;
        }

        private bool TryStartMantle()
        {
            if (Stance == SoldierStance.Crouching) return false;
            Vector3 fwd = transform.forward;
            Vector3 feet = transform.position;
            int mask = LR2Layers.EnvironmentMask;

            // 1) Something solid in front at knee/waist height?
            bool blocked = false;
            for (float h = mantleMinHeight; h <= mantleMaxHeight; h += 0.35f)
            {
                if (Physics.Raycast(feet + Vector3.up * h, fwd, mantleReach, mask, QueryTriggerInteraction.Ignore))
                {
                    blocked = true;
                    break;
                }
            }
            if (!blocked) return false;

            // 2) Find the ledge top.
            Vector3 probe = feet + fwd * (mantleReach * 0.9f) + Vector3.up * (mantleMaxHeight + 0.4f);
            if (!Physics.Raycast(probe, Vector3.down, out var top, mantleMaxHeight + 0.4f, mask, QueryTriggerInteraction.Ignore))
                return false;
            float ledge = top.point.y - feet.y;
            if (ledge < mantleMinHeight || ledge > mantleMaxHeight || top.normal.y < 0.7f) return false;

            // 3) Room to stand on top?
            Vector3 target = top.point + Vector3.up * 0.05f;
            float r = _controller.radius;
            Vector3 p1 = target + Vector3.up * (r + 0.05f);
            Vector3 p2 = target + Vector3.up * (_originalHeight - r);
            if (Physics.CheckCapsule(p1, p2, r * 0.9f, mask | (1 << LR2Layers.PlayerClip), QueryTriggerInteraction.Ignore))
                return false;

            _mantleStart = feet;
            _mantleEnd = target;
            _mantleT = 0f;
            _velocity = Vector3.zero;
            State = SoldierMoveState.Mantling;
            _controller.enabled = false;
            MantleStarted?.Invoke();
            onJump?.Invoke();
            return true;
        }

        private void UpdateMantle()
        {
            _mantleT += Time.deltaTime / Mathf.Max(0.05f, mantleDuration);
            float t = Mathf.Clamp01(_mantleT);
            // Rise first, then move over the ledge.
            float up = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t * 1.6f));
            float fwd = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.35f) / 0.65f));
            Vector3 pos = new Vector3(
                Mathf.Lerp(_mantleStart.x, _mantleEnd.x, fwd),
                Mathf.Lerp(_mantleStart.y, _mantleEnd.y + 0.02f, up),
                Mathf.Lerp(_mantleStart.z, _mantleEnd.z, fwd));
            transform.position = pos;
            MoveVector = Vector3.zero;
            if (t >= 1f)
            {
                _controller.enabled = true;
                State = SoldierMoveState.Idle;
                onLanded?.Invoke();
            }
        }

        private void UpdateAnimatorParams()
        {
            if (_animator == null) return;
            var animatorVelocity = _inputDirection;
            animatorVelocity *= State is SoldierMoveState.InAir or SoldierMoveState.Mantling ? 0f : 1f;
            AnimatorVelocity = Vector2.Lerp(AnimatorVelocity, animatorVelocity, KMath.ExpDecayAlpha(_desiredGait.velocitySmoothing, Time.deltaTime));

            _animator.SetFloat(MoveXHash, AnimatorVelocity.x);
            _animator.SetFloat(MoveYHash, AnimatorVelocity.y);
            _animator.SetFloat(VelocityHash, AnimatorVelocity.magnitude);
            _animator.SetBool(InAirHash, State == SoldierMoveState.InAir);
            _animator.SetBool(MovingHash, IsMoving);

            float sprintWeight = _animator.GetFloat(SprintingHash);
            float t = KMath.ExpDecayAlpha(_desiredGait.velocitySmoothing, Time.deltaTime);
            sprintWeight = Mathf.Lerp(sprintWeight, State == SoldierMoveState.Sprinting ? 1f : 0f, t);
            _animator.SetFloat(SprintingHash, sprintWeight);

            float planar = new Vector3(_velocity.x, 0f, _velocity.z).magnitude;
            float reference = State == SoldierMoveState.Sprinting ? sprintAnimReferenceSpeed : Stance == SoldierStance.Crouching ? crouchAnimReferenceSpeed : walkAnimReferenceSpeed;
            float targetLoco = planar > 0.3f ? Mathf.Clamp(planar / Mathf.Max(0.1f, reference), 0.55f, 1.15f) : 1f;
            _locoSpeed = Mathf.Lerp(_locoSpeed, targetLoco, KMath.ExpDecayAlpha(8f, Time.deltaTime));
            _animator.SetFloat(LocoSpeedHash, _locoSpeed);

            if (_inputController != null)
                _inputController.SetValue(FPSANames.MoveInput, new Vector4(AnimatorVelocity.x, AnimatorVelocity.y));
        }

        private void UpdateFootsteps()
        {
            if (State is SoldierMoveState.InAir or SoldierMoveState.Mantling) return;
            float planar = new Vector3(_velocity.x, 0f, _velocity.z).magnitude;
            if (planar < 0.6f) return;
            _strideAccum += planar * Time.deltaTime;

            float stride = State == SoldierMoveState.Sprinting ? strideSprint : Stance == SoldierStance.Crouching ? strideCrouch : strideWalk;
            if (State == SoldierMoveState.Sliding) return;
            if (_strideAccum < stride) return;
            _strideAccum = 0f;
            Footstep?.Invoke(planar);

            float noise = State == SoldierMoveState.Sprinting ? noiseSprint : Stance == SoldierStance.Crouching ? noiseCrouch : noiseWalk;
            if (IsAimingSlowdown) noise *= 0.6f;
            float intensity = State == SoldierMoveState.Sprinting ? 1f : Stance == SoldierStance.Crouching ? 0.35f : 0.7f;
            PlayFootstep(intensity);
            GameEvents.RaiseNoise(_combatant, transform.position, noise, NoiseType.Footstep);
        }

        private void PlayFootstep(float intensity)
        {
            Vector3 origin = transform.position + Vector3.up * 0.3f;
            SurfaceType surface = SurfaceType.Concrete;
            if (Physics.Raycast(origin, Vector3.down, out var hit, 1.2f, LR2Layers.GroundMask, QueryTriggerInteraction.Ignore))
                surface = SurfaceDetector.Detect(hit.collider);
            bool local = _combatant != null && _combatant.IsLocalPlayer;
            ImpactSystem.PlayFootstep(transform.position, surface, intensity, local);
        }

        private void HandleLanding()
        {
            float impact = _lastFallSpeed;
            _lastFallSpeed = 0f;
            Landed?.Invoke(impact);
            if (_airborneTime > 0.25f)
            {
                PlayFootstep(Mathf.Clamp01(impact / 10f) + 0.4f);
                GameEvents.RaiseNoise(_combatant, transform.position, Mathf.Lerp(8f, 22f, impact / 15f), NoiseType.Footstep);
            }

            if (impact > fallDamageSpeed && _health != null)
            {
                var info = new DamageInfo
                {
                    amount = (impact - fallDamageSpeed) * fallDamagePerMs,
                    type = DamageType.Fall,
                    point = transform.position,
                    direction = Vector3.down,
                    weaponName = "Fall"
                };
                _health.ApplyDamage(ref info);
            }
        }

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _inputController = GetComponent<UserInputController>();
            _animator = GetComponent<Animator>();
            _combatant = GetComponent<Combatant>();
            _health = GetComponent<Health>();
        }

        private void Start()
        {
            _originalHeight = _controller.height;
            _originalCenter = _controller.center;
            _cachedState = State = SoldierMoveState.Idle;
            Stance = SoldierStance.Standing;
            _desiredGait = _cachedGait = Gait(GaitKind.Idle);
            _initialized = true;
        }

        private void Update()
        {
            if (!_initialized) return;
            if (State == SoldierMoveState.Mantling)
            {
                UpdateMantle();
                UpdateAnimatorParams();
                _cachedState = State;
                return;
            }

            UpdateMovementState();
            if (_cachedState != State) OnMovementStateChanged();

            bool moving = IsMoving;
            if (_wasMoving != moving)
            {
                if (moving) onStartMoving?.Invoke();
                else onStopMoving?.Invoke();
            }
            _wasMoving = moving;

            if (State == SoldierMoveState.InAir) UpdateInAir();
            else if (State == SoldierMoveState.Sliding) UpdateSliding();
            else UpdateGrounded();

            if (_controller.enabled) _controller.Move(MoveVector * Time.deltaTime);
            UpdateAnimatorParams();
            UpdateFootsteps();

            if (_combatant != null) _combatant.Velocity = Velocity;
            _cachedState = State;
        }

        private void LateUpdate()
        {
            if (!_initialized || State == SoldierMoveState.Mantling || !_controller.enabled) return;
            bool grounded = _controller.isGrounded;
            if (!grounded) _airborneTime += Time.deltaTime;

            if (State != SoldierMoveState.InAir && !grounded && _airborneTime > 0.12f)
            {
                State = SoldierMoveState.InAir;
                if (_velocity.y > 0f) _velocity.y = 0f;
            }
            else if (State == SoldierMoveState.InAir && grounded && _velocity.y <= 0.01f)
            {
                State = SoldierMoveState.Idle;
                HandleLanding();
                _airborneTime = 0f;
            }

            if (grounded && State != SoldierMoveState.InAir) _airborneTime = 0f;
        }
    }
}
