// Based on KINEMATION FPS Animation Framework demo FPSController (2025). LR2 changes: input comes from an
// ISoldierInputSource (human or AI) so bots and the player share one character; CoD-style sprint-out, ADS,
// flinch, view kick, death (ragdoll + weapon drop + death cam) and respawn; first/third person setup by role.
using System;
using System.Collections.Generic;
using System.Reflection;
using Demo.Scripts.Runtime.Character;
using KINEMATION.FPSAnimationFramework.Runtime.Camera;
using KINEMATION.FPSAnimationFramework.Runtime.Core;
using KINEMATION.FPSAnimationFramework.Runtime.Playables;
using KINEMATION.FPSAnimationFramework.Runtime.Recoil;
using KINEMATION.ProceduralRecoilAnimationSystem.Runtime;
using KINEMATION.Shared.KAnimationCore.Runtime.Input;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using LR2.Audio;
using LR2.Combat;
using LR2.Core;
using LR2.Weapons;
using UnityEngine;
using UnityEngine.Rendering;

namespace LR2.Characters
{
    [RequireComponent(typeof(CharacterController), typeof(SoldierMovement))]
    [DefaultExecutionOrder(-50)]
    public class SoldierController : MonoBehaviour
    {
        [SerializeField] private FPSControllerSettings settings;

        [Header("Look")]
        [SerializeField] private float minPitch = -80f;
        [SerializeField] private float maxPitch = 80f;
        [Tooltip("Max turn rate (deg/s) when a bot steers with absolute aim angles.")]
        public float maxBotTurnSpeed = 720f;

        [Header("Feel")]
        [Tooltip("Aim punch when hit (degrees per 25 damage).")]
        [SerializeField] private float flinchStrength = 1.4f;
        [SerializeField] private float flinchRecovery = 10f;
        [Tooltip("View kick recovery speed (camera returns after shots).")]
        [SerializeField] private float kickRecovery = 9f;
        [SerializeField] private float weaponSwapCooldown = 0.35f;

        [Header("View")]
        [Tooltip("Head renderers hidden (shadows only) in first person.")]
        [SerializeField] private Renderer[] firstPersonHidden = Array.Empty<Renderer>();
        [Tooltip("Eye socket used as aim origin for bots (falls back to the camera transform / head).")]
        [SerializeField] private Transform eyeSocket;

        [Header("Death")]
        [SerializeField] private float deathCamDistance = 3.4f;
        [SerializeField] private float deathCamHeight = 1.6f;
        [SerializeField] private float droppedWeaponLifetime = 12f;

        public event Action<LR2Item> ItemEquipped;
        public event Action<SoldierController> Respawned;
        public event Action<SoldierController, DamageInfo> Died;

        // ------------------------------------------------------------------ components
        public Combatant Combatant { get; private set; }
        public Health Health { get; private set; }
        public SoldierMovement Movement { get; private set; }
        public FPSAnimator FPSAnimator { get; private set; }
        public Animator Animator { get; private set; }
        public UserInputController UserInput { get; private set; }
        public IPlayablesController Playables { get; private set; }
        public FPSCameraController CameraController { get; private set; }
        public RecoilAnimation RecoilAnimation { get; private set; }
        public RecoilPattern RecoilPattern { get; private set; }
        public Camera ViewCamera { get; private set; }
        public FPSControllerSettings Settings => settings;

        public ISoldierInputSource InputSource { get; set; }
        public bool ControlsEnabled { get; set; } = true;

        // ------------------------------------------------------------------ state
        public float Yaw => transform.eulerAngles.y;
        public float Pitch => _pitch;
        public Transform ViewTransform => ViewCamera != null && ViewCamera.isActiveAndEnabled ? ViewCamera.transform : EyeTransform;
        public Transform EyeTransform => eyeSocket != null ? eyeSocket : transform;
        public Vector3 AimOrigin { get; private set; }
        public Vector3 AimDirection { get; private set; }
        public bool IsAiming => _isAiming;
        public float AimBlend { get; private set; }
        public bool IsSprinting => Movement != null && Movement.State == SoldierMoveState.Sprinting;
        public bool IsCrouching => Movement != null && Movement.Stance == SoldierStance.Crouching;
        public bool IsProning => Movement != null && Movement.Stance == SoldierStance.Prone;
        public bool IsReloading => ActiveFirearm != null && ActiveFirearm.IsReloading;
        public bool IsDead => Health != null && !Health.IsAlive;
        public bool IsLocalPlayer => Combatant != null && Combatant.IsLocalPlayer;
        public bool IsSwitchingWeapon => _switching;
        public LR2Item ActiveItem => _items.Count > 0 ? _items[_activeIndex] : null;
        public LR2Firearm ActiveFirearm => ActiveItem as LR2Firearm;
        public IReadOnlyList<LR2Item> Items => _items;
        public int ActiveSlot => _activeIndex;
        public float ScopeSensitivity => UserInput != null && _sensIndex >= 0 ? Mathf.Max(0.05f, UserInput.GetValue<float>(_sensIndex)) : 1f;

        /// <summary>False while sprinting or during the sprint-out / swap window.</summary>
        public bool CanShootNow => !IsDead && !IsSprinting && Time.time >= _sprintOutUntil && !_switching;

        private readonly List<LR2Item> _items = new List<LR2Item>();
        private CharacterController _controller;
        private SoldierRagdoll _ragdoll;
        private Transform _weaponBone;
        private SoldierInput _input = SoldierInput.Empty;
        private int _activeIndex;
        private bool _isAiming;
        private bool _firePressed;
        private bool _prevSprintInput;
        private bool _switching;
        private int _pendingIndex = -1;
        private float _switchAt;
        private float _lastSwitchTime = -10f;
        private float _sprintOutUntil;
        private float _yawAccum;
        private float _pitch;
        private float _lastLean;
        private Vector2 _flinch, _flinchVel;
        private Vector2 _kick, _kickApplied;
        private int _sensIndex = -1;
        private bool _initialized;
        private float _nextHurtVoiceAt;

        // death cam / weapon drop
        private Transform _camParent;
        private Vector3 _camLocalPos;
        private Quaternion _camLocalRot;
        private Combatant _killer;
        private Vector3 _deathPos;
        private GameObject _droppedWeapon;
        public GameObject DroppedWeapon => _droppedWeapon;

        private static readonly int FullBodyWeightHash = Animator.StringToHash("FullBodyWeight");
        private static readonly int SlideHash = Animator.StringToHash("Sliding");

        // ------------------------------------------------------------------ lifecycle
        private void Awake()
        {
            Combatant = GetComponent<Combatant>();
            Health = GetComponent<Health>();
            Movement = GetComponent<SoldierMovement>();
            FPSAnimator = GetComponent<FPSAnimator>();
            Animator = GetComponent<Animator>();
            UserInput = GetComponent<UserInputController>();
            Playables = GetComponent<IPlayablesController>();
            RecoilAnimation = GetComponent<RecoilAnimation>();
            RecoilPattern = GetComponent<RecoilPattern>();
            _controller = GetComponent<CharacterController>();
            _ragdoll = GetComponent<SoldierRagdoll>();
            ViewCamera = GetComponentInChildren<Camera>(true);
            if (InputSource == null) InputSource = GetComponent<ISoldierInputSource>();

            if (Health != null)
            {
                Health.Damaged += OnDamaged;
                Health.Died += OnDied;
            }
        }

        private void OnDestroy()
        {
            if (Health != null)
            {
                Health.Damaged -= OnDamaged;
                Health.Died -= OnDied;
            }
            LR2Settings.Changed -= OnSettingsChanged;
            if (_droppedWeapon != null) Destroy(_droppedWeapon);
        }

        private void OnSettingsChanged()
        {
            if (!_initialized || !IsLocalPlayer) return;
            if (ActiveItem != null) ActiveItem.UpdateFieldOfView(_isAiming);
        }

        private void Start()
        {
            ConfigureRole();
            if (ViewCamera != null && IsLocalPlayer) ViewCamera.fieldOfView = LR2Settings.VerticalFov(LR2Settings.FieldOfView, ViewCamera.aspect);
            if (FPSAnimator != null) FPSAnimator.Initialize();
            CameraController = ViewCamera != null && ViewCamera.gameObject.activeInHierarchy ? ViewCamera.GetComponent<FPSCameraController>() : null;
            if (CameraController != null)
            {
                var fovField = typeof(FPSCameraController).GetField("fovSpeed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (fovField != null && fovField.FieldType == typeof(float))
                {
                    float cur = (float)fovField.GetValue(CameraController);
                    if (cur < 8f) fovField.SetValue(CameraController, 8f);
                }
            }
            if (GetComponent<WeaponAntiClip>() == null) gameObject.AddComponent<WeaponAntiClip>();
            if (IsLocalPlayer) LR2Settings.Changed += OnSettingsChanged;

            var rig = GetComponentInChildren<KRigComponent>();
            if (rig != null && settings != null) _weaponBone = rig.GetRigTransform(settings.weaponBone);
            if (_weaponBone == null) _weaponBone = transform;
            if (eyeSocket == null)
            {
                var head = Animator != null && Animator.isHuman ? Animator.GetBoneTransform(HumanBodyBones.Head) : null;
                eyeSocket = head != null ? head : (ViewCamera != null ? ViewCamera.transform : transform);
            }
            if (Combatant != null && Combatant.EyePoint == null)
            {
                Transform head = Animator != null && Animator.isHuman ? Animator.GetBoneTransform(HumanBodyBones.Head) : null;
                Transform chest = Animator != null && Animator.isHuman ? Animator.GetBoneTransform(HumanBodyBones.Chest) : null;
                Combatant.SetAimPoints(ViewCamera != null && IsLocalPlayer ? ViewCamera.transform : eyeSocket, head, chest);
            }

            InitializeMovement();
            InitializeWeapons();
            _sensIndex = UserInput != null ? UserInput.GetPropertyIndex("SensitivityMultiplier") : -1;
            _pitch = 0f;
            _yawAccum = transform.eulerAngles.y;
            EquipImmediate(0);
            _initialized = true;
            GameEvents.RaiseSpawned(Combatant);
        }

        /// <summary>Hides the head/helmet meshes (shadows stay) - used by the killcam when looking through this soldier's eyes.</summary>
        public void SetHeadHidden(bool hidden)
        {
            foreach (var r in firstPersonHidden)
                if (r != null) r.shadowCastingMode = hidden ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
        }

        /// <summary>First person (local player) vs third person (bots): camera, listener, head visibility.</summary>
        private void ConfigureRole()
        {
            bool local = IsLocalPlayer;
            if (ViewCamera != null)
            {
                ViewCamera.gameObject.SetActive(local);
                var listener = ViewCamera.GetComponent<AudioListener>();
                if (listener != null) listener.enabled = local;
            }

            foreach (var r in firstPersonHidden)
                if (r != null) r.shadowCastingMode = local ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;

            if (local)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private void InitializeMovement()
        {
            Movement.onJump = () => PlayTransitionMotion(settings != null ? settings.jumpingMotion : null);
            Movement.onLanded = () => PlayTransitionMotion(settings != null ? settings.jumpingMotion : null);
            Movement.onCrouch = () => PlayTransitionMotion(settings != null ? settings.crouchingMotion : null);
            Movement.onUncrouch = () => PlayTransitionMotion(settings != null ? settings.crouchingMotion : null);
            Movement.onProneStarted = () => PlayTransitionMotion(settings != null ? settings.crouchingMotion : null);
            Movement.onProneEnded = () => PlayTransitionMotion(settings != null ? settings.crouchingMotion : null);
            Movement._proneActionCondition = () => !_switching && !IsDead;
            Movement.onSprintStarted = OnSprintStarted;
            Movement.onSprintEnded = OnSprintEnded;
            Movement.onSlideStarted = () =>
            {
                if (Animator != null) Animator.CrossFade(SlideHash, 0.2f);
                var s = LR2CommonSounds.Instance;
                if (s != null) LR2Audio.PlayAt(s.slide, transform.position);
            };
            Movement.onStopMoving = () => PlayTransitionMotion(settings != null ? settings.stopMotion : null);
            Movement._sprintActionCondition = () => !_switching && !IsDead;
            Movement._slideActionCondition = () => !_switching && !IsDead;
            Movement.Jumped += () =>
            {
                var s = LR2CommonSounds.Instance;
                if (s != null) LR2Audio.PlayAt(s.jump, transform.position);
            };
            Movement.MantleStarted += () =>
            {
                var s = LR2CommonSounds.Instance;
                if (s != null) LR2Audio.PlayAt(s.mantle, transform.position);
            };
            Movement.Landed += speed =>
            {
                var s = LR2CommonSounds.Instance;
                if (s != null && speed > 4f) LR2Audio.PlayAt(s.land, transform.position, Mathf.Clamp01(speed / 12f));
                if (IsLocalPlayer && speed > 6f) AddViewKick(new Vector2(0f, Mathf.Min(speed * 0.25f, 3f)));
            };
        }

        private void InitializeWeapons()
        {
            _items.Clear();
            if (settings == null || settings.weaponPrefabs == null) return;

            // Loadout slots: weaponPrefabs holds [0:Mk18, 1:Mk23, 2:AK12, 3:AK74N, 4:FAL].
            // Equip the player's chosen primary rifle cleanly in slot 0 (default = MK18 Mod 1) + Mk23 suppressed sidearm in slot 1.
            var spawnList = new List<GameObject>(2);
            if (settings.weaponPrefabs.Count >= 2)
            {
                int[] rifleSlotMap = { 0, 2, 3, 4 };
                int choice = IsLocalPlayer ? LR2.Game.LR2Session.SelectedPrimaryWeapon : 0;
                int primaryIdx = choice >= 0 && choice < rifleSlotMap.Length && rifleSlotMap[choice] < settings.weaponPrefabs.Count
                    ? rifleSlotMap[choice]
                    : 0;
                if (settings.weaponPrefabs[primaryIdx] != null) spawnList.Add(settings.weaponPrefabs[primaryIdx]);
                if (settings.weaponPrefabs[1] != null) spawnList.Add(settings.weaponPrefabs[1]);
            }
            else
            {
                foreach (var p in settings.weaponPrefabs)
                    if (p != null) spawnList.Add(p);
            }

            foreach (var prefab in spawnList)
            {
                if (prefab == null) continue;
                var go = Instantiate(prefab, transform.position, Quaternion.identity);
                var t = go.transform;
                t.SetParent(_weaponBone, false);
                t.localPosition = Vector3.zero;
                t.localRotation = Quaternion.identity;
                var item = go.GetComponent<LR2Item>();
                if (item == null)
                {
                    Debug.LogWarning($"[LR2] Weapon prefab {prefab.name} has no LR2Item component.");
                    Destroy(go);
                    continue;
                }
                _items.Add(item);
                go.SetActive(false);
            }
        }

        // ------------------------------------------------------------------ update
        private void Update()
        {
            if (!_initialized) return;

            if (IsDead)
            {
                if (IsLocalPlayer) UpdateDeathCam();
                return;
            }

            if (ControlsEnabled && InputSource != null) InputSource.GetInput(this, ref _input);
            else
            {
                bool keepLook = InputSource != null && IsLocalPlayer;
                Vector2 look = Vector2.zero;
                if (keepLook)
                {
                    InputSource.GetInput(this, ref _input);
                    look = _input.look;
                }
                _input.ClearAll();
                _input.look = look;
            }

            UpdateLook();
            UpdateActions();
            UpdateWeaponSwitch();
            if (ActiveItem != null) ActiveItem.Tick(Time.deltaTime);

            float fullBody = Animator != null ? Animator.GetFloat(FullBodyWeightHash) : 0f;
            float playablesWeight = IsSprinting ? Mathf.Max(0.78f, 1f - fullBody * 0.22f) : (1f - fullBody);
            if (UserInput != null) UserInput.SetValue(FPSANames.PlayablesWeight, playablesWeight);

            float adsTime = ActiveFirearm != null && ActiveFirearm.Definition != null ? ActiveFirearm.Definition.adsTime : 0.25f;
            AimBlend = Mathf.MoveTowards(AimBlend, _isAiming ? 1f : 0f, Time.deltaTime / Mathf.Max(0.05f, adsTime));
            Movement.IsAimingSlowdown = _isAiming;

            Quaternion view = Quaternion.Euler(_pitch, transform.eulerAngles.y, 0f);
            AimDirection = view * Vector3.forward;
            AimOrigin = IsLocalPlayer && ViewCamera != null && ViewCamera.isActiveAndEnabled ? ViewCamera.transform.position : EyeTransform.position;
            if (Combatant != null) Combatant.ViewDirection = AimDirection;

            _input.ResetOneShots();
        }

        private void UpdateLook()
        {
            float dt = Time.deltaTime;
            float deltaYaw, deltaPitch;
            if (_input.absoluteLook)
            {
                float maxStep = maxBotTurnSpeed * dt;
                deltaYaw = Mathf.Clamp(Mathf.DeltaAngle(transform.eulerAngles.y, _input.aimYaw), -maxStep, maxStep);
                deltaPitch = Mathf.Clamp(Mathf.Clamp(_input.aimPitch, minPitch, maxPitch) - _pitch, -maxStep, maxStep);
            }
            else
            {
                deltaYaw = _input.look.x;
                deltaPitch = _input.look.y;
            }

            // Kinemation recoil pattern (camera climb).
            if (RecoilPattern != null)
            {
                Vector2 recoil = RecoilPattern.GetRecoilDelta();
                deltaYaw += recoil.x;
                deltaPitch += recoil.y;
            }

            // Flinch (aim punch) spring and per-shot view kick that recovers.
            _flinch = Vector2.SmoothDamp(_flinch, Vector2.zero, ref _flinchVel, 1f / Mathf.Max(0.1f, flinchRecovery));
            Vector2 kickTarget = _kick;
            _kick = Vector2.Lerp(_kick, Vector2.zero, LR2Math.Damp(kickRecovery, dt));
            Vector2 kickDelta = kickTarget - _kickApplied;
            _kickApplied = kickTarget;
            deltaYaw += kickDelta.x + _flinch.x * dt * flinchRecovery;
            deltaPitch += kickDelta.y + _flinch.y * dt * flinchRecovery;

            _pitch = Mathf.Clamp(_pitch + deltaPitch, minPitch, maxPitch);
            _yawAccum += deltaYaw;
            transform.rotation *= Quaternion.Euler(0f, deltaYaw, 0f);

            if (UserInput != null)
            {
                UserInput.SetValue(FPSANames.MouseDeltaInput, new Vector4(deltaYaw, deltaPitch));
                UserInput.SetValue(FPSANames.MouseInput, new Vector4(_yawAccum, _pitch));
            }
        }

        private void UpdateActions()
        {
            var item = ActiveItem;
            bool busySwitch = _switching;

            // Firing or aiming cancels sprint, then a short sprint-out delay before the gun is up (CoD feel).
            if ((_input.fire || _input.aim) && IsSprinting)
            {
                Movement.StopSprint();
                float sprintOut = ActiveFirearm != null && ActiveFirearm.Definition != null ? ActiveFirearm.Definition.sprintToFireTime : 0.2f;
                _sprintOutUntil = Time.time + sprintOut;
            }

            Movement.SetMoveInput(_input.move);
            // B16/D40 (COD): the last press wins. Pressing sprint during a reload interrupts it (the player; bots finish
            // their reloads, as a human would); a reload started while sprinting drops the sprint until it is done.
            bool sprintPressed = _input.sprint && !_prevSprintInput;
            _prevSprintInput = _input.sprint;
            if (sprintPressed && IsReloading && IsLocalPlayer) ActiveFirearm.InterruptReload();
            Movement.SetSprintRequested(_input.sprint && !_input.fire && !_input.aim && !busySwitch && !IsReloading);
            if (_input.jump) Movement.RequestJump();
            if (_input.crouch) Movement.RequestCrouchToggle();
            if (_input.prone) Movement.RequestProneToggle();

            // Lean (Kinemation lean layer)
            if (UserInput != null && settings != null && !Mathf.Approximately(_input.lean, _lastLean))
            {
                UserInput.SetValue(FPSANames.LeanInput, _input.lean * settings.leanAngle);
                PlayTransitionMotion(settings.leanMotion);
                _lastLean = _input.lean;
            }

            if (item == null) return;

            // Fire
            bool canFire = CanShootNow && !busySwitch;
            if (_input.fire && canFire)
            {
                if (!_firePressed)
                {
                    _firePressed = true;
                    item.OnFirePressed();
                }
            }
            else if (_firePressed)
            {
                _firePressed = false;
                item.OnFireReleased();
            }

            // Aim (ADS)
            bool wantAim = _input.aim && !IsSprinting && !busySwitch && !IsReloading;
            if (wantAim && !_isAiming) SetAim(true);
            else if (!wantAim && _isAiming) SetAim(false);

            if (_input.reload) RequestReload();

            if (_input.weaponSlot >= 0) BeginWeaponSwitch(_input.weaponSlot);
            else if (_input.nextWeapon && _items.Count > 1) BeginWeaponSwitch((_activeIndex + 1) % _items.Count);
        }

        private void SetAim(bool aim)
        {
            var item = ActiveItem;
            if (item == null) return;
            if (aim)
            {
                if (!item.OnAimPressed()) return;
                _isAiming = true;
            }
            else
            {
                item.OnAimReleased();
                _isAiming = false;
            }
            PlayTransitionMotion(settings != null ? settings.aimingMotion : null);
            var s = LR2CommonSounds.Instance;
            if (s != null) LR2Audio.PlayAt(aim ? s.adsIn : s.adsOut, transform.position, IsLocalPlayer ? 1f : 0.6f);
        }

        public bool RequestReload()
        {
            var item = ActiveItem;
            if (item == null || _switching || IsDead) return false;
            if (_isAiming) SetAim(false);
            return item.OnReload();
        }

        // ------------------------------------------------------------------ weapons
        private void BeginWeaponSwitch(int index)
        {
            if (index == _activeIndex || index < 0 || index >= _items.Count || _switching) return;
            if (Time.time - _lastSwitchTime < weaponSwapCooldown) return;
            _lastSwitchTime = Time.time;
            if (_isAiming) SetAim(false);
            if (_firePressed)
            {
                _firePressed = false;
                ActiveItem.OnFireReleased();
            }
            ActiveItem.OnUnEquip();
            _switching = true;
            _pendingIndex = index;
            _switchAt = Time.time + (settings != null ? settings.equipDelay : 0.3f);
            var s = LR2CommonSounds.Instance;
            if (s != null) LR2Audio.PlayAt(s.weaponSwap, transform.position, IsLocalPlayer ? 1f : 0.6f);
        }

        private void UpdateWeaponSwitch()
        {
            if (!_switching || Time.time < _switchAt) return;
            _switching = false;
            EquipImmediate(_pendingIndex);
        }

        private void EquipImmediate(int index)
        {
            if (_items.Count == 0) return;
            index = Mathf.Clamp(index, 0, _items.Count - 1);
            for (int i = 0; i < _items.Count; i++)
                if (i != index && _items[i] != null) _items[i].gameObject.SetActive(false);
            _activeIndex = index;
            var item = _items[index];
            item.gameObject.SetActive(true);
            item.OnEquip(this);
            ItemEquipped?.Invoke(item);
        }

        public void RefillAmmo()
        {
            foreach (var item in _items) item.RefillAmmo();
        }

        public int AddReserveAmmo(int rounds)
        {
            int accepted = 0;
            if (ActiveItem != null) accepted += ActiveItem.AddReserveAmmo(rounds);
            foreach (var item in _items)
                if (item != ActiveItem) accepted += item.AddReserveAmmo(rounds / 2);
            return accepted;
        }

        /// <summary>Adds a per-shot camera kick (x = yaw, y = pitch; negative pitch = up).</summary>
        public void AddViewKick(Vector2 kick) => _kick += kick;

        private void PlayTransitionMotion(FPSAnimatorLayerSettings layerSettings)
        {
            if (layerSettings == null || FPSAnimator == null) return;
            FPSAnimator.LinkAnimatorLayer(layerSettings);
        }

        private void OnSprintStarted()
        {
            // B16/D40: sprint interrupts the reload (COD); before this the reload kept running hidden in the sprint carry.
            if (ActiveFirearm != null && ActiveFirearm.IsReloading) ActiveFirearm.InterruptReload();
            if (_firePressed && ActiveItem != null)
            {
                _firePressed = false;
                ActiveItem.OnFireReleased();
            }
            if (_isAiming) SetAim(false);
            if (UserInput != null)
            {
                UserInput.SetValue(FPSANames.StabilizationWeight, 0.78f);
                UserInput.SetValue("LookLayerWeight", 0.8f);
            }
        }

        private void OnSprintEnded()
        {
            if (UserInput == null) return;
            UserInput.SetValue(FPSANames.StabilizationWeight, 1f);
            UserInput.SetValue("LookLayerWeight", 1f);
        }

        // ------------------------------------------------------------------ damage / death / respawn
        private void OnDamaged(DamageInfo info)
        {
            if (IsDead) return;
            float scale = Mathf.Clamp(info.amount / 25f, 0.3f, 2f) * flinchStrength * (_isAiming ? 0.7f : 1f);
            _flinch += new Vector2(UnityEngine.Random.Range(-1f, 1f), -UnityEngine.Random.Range(0.4f, 1f)) * scale;

        }

        private void ForceResetAimState()
        {
            _isAiming = false;
            AimBlend = 0f;
            if (ActiveItem != null) ActiveItem.OnAimReleased();
            if (UserInput != null)
            {
                UserInput.SetValue(FPSANames.IsAiming, false);
                UserInput.SetValue("AimingWeight", 0f);
            }
            if (RecoilAnimation != null)
            {
                RecoilAnimation.isAiming = false;
                RecoilAnimation.Stop();
            }
            if (ViewCamera != null && Movement != null)
            {
                float hipFovH = IsLocalPlayer ? LR2Settings.FieldOfView : 90f;
                ViewCamera.fieldOfView = LR2Settings.VerticalFov(hipFovH, ViewCamera.aspect);
            }
        }

        private void OnDied(DamageInfo info)
        {
            _killer = info.attacker;
            _deathPos = transform.position;
            if (_firePressed && ActiveItem != null) ActiveItem.OnFireReleased();
            _firePressed = false;
            if (ActiveItem != null) ActiveItem.CancelActions();
            ForceResetAimState();
            _switching = false;
            _input.ClearAll();
            Movement.ResetState();
            Movement.enabled = false;
            _controller.enabled = false;

            var s = LR2CommonSounds.Instance;
            if (s != null) LR2Audio.PlayAt(s.bodyFall, transform.position, 1f, 0.45f);

            if (CameraController != null) CameraController.enabled = false;
            if (IsLocalPlayer)
            {
                SetHeadHidden(false);
                BeginDeathCam();
            }
            if (FPSAnimator != null) FPSAnimator.enabled = false;
            if (_ragdoll != null) _ragdoll.Activate(info);
            else if (Animator != null) Animator.enabled = false;

            DropWeapon(info);
            Died?.Invoke(this, info);
        }

        private void DropWeapon(DamageInfo info)
        {
            var item = ActiveItem;
            if (item == null) return;
            // B59b: Spawn slightly forward/right of chest & drop forward/downward so close-range attacker fire
            // never drives the dropped weapon backwards through the victim's torso.
            Vector3 dropPos = transform.position + Vector3.up * 1.12f + transform.forward * 0.28f + transform.right * 0.16f;
            _droppedWeapon = Instantiate(item.gameObject, dropPos, item.transform.rotation);
            foreach (var b in _droppedWeapon.GetComponentsInChildren<MonoBehaviour>(true)) Destroy(b);
            foreach (var a in _droppedWeapon.GetComponentsInChildren<Animator>(true)) Destroy(a);
            foreach (var l in _droppedWeapon.GetComponentsInChildren<Light>(true)) Destroy(l);
            foreach (var p in _droppedWeapon.GetComponentsInChildren<ParticleSystem>(true)) Destroy(p.gameObject);
            SetLayerRecursive(_droppedWeapon.transform, LR2Layers.Debris);
            var bounds = new Bounds(_droppedWeapon.transform.position, Vector3.zero);
            foreach (var r in _droppedWeapon.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
            var box = _droppedWeapon.AddComponent<BoxCollider>();
            box.center = _droppedWeapon.transform.InverseTransformPoint(bounds.center);
            box.size = new Vector3(0.08f, 0.2f, 0.8f);
            var rb = _droppedWeapon.AddComponent<Rigidbody>();
            rb.mass = 3.5f;
            rb.linearVelocity = transform.forward * 0.9f + transform.right * 0.55f + Vector3.up * 0.85f;
            rb.angularVelocity = UnityEngine.Random.insideUnitSphere * 3.5f;
            Destroy(_droppedWeapon, droppedWeaponLifetime);
            item.gameObject.SetActive(false);
        }

        private static void SetLayerRecursive(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayerRecursive(t.GetChild(i), layer);
        }

        private void BeginDeathCam()
        {
            if (ViewCamera == null) return;
            var cam = ViewCamera.transform;
            _camParent = cam.parent;
            _camLocalPos = cam.localPosition;
            _camLocalRot = cam.localRotation;
            cam.SetParent(null, true);
        }

        private void UpdateDeathCam()
        {
            if (ViewCamera == null || ViewCamera.transform.parent != null) return;
            var cam = ViewCamera.transform;
            Vector3 focus = _deathPos + Vector3.up * 0.6f;
            Vector3 lookTarget = _killer != null && _killer.IsAlive ? _killer.ChestPosition : focus;
            Vector3 away = (focus - lookTarget);
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = -transform.forward;
            Vector3 desired = focus + away.normalized * deathCamDistance + Vector3.up * deathCamHeight;
            if (Physics.Linecast(focus, desired, out var block, LR2Layers.EnvironmentMask, QueryTriggerInteraction.Ignore))
                desired = block.point + block.normal * 0.25f;
            cam.position = Vector3.Lerp(cam.position, desired, LR2Math.Damp(3f, Time.deltaTime));
            Quaternion look = Quaternion.LookRotation((lookTarget - cam.position).normalized);
            cam.rotation = Quaternion.Slerp(cam.rotation, look, LR2Math.Damp(4f, Time.deltaTime));
        }

        private void EndDeathCam()
        {
            if (ViewCamera == null || _camParent == null) return;
            var cam = ViewCamera.transform;
            cam.SetParent(_camParent, false);
            cam.localPosition = _camLocalPos;
            cam.localRotation = _camLocalRot;
            _camParent = null;
        }

        public void Teleport(Vector3 position, float yaw)
        {
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            _yawAccum = yaw;
            _pitch = 0f;
            Movement.Teleport(position);
        }

        /// <summary>Revives the soldier at a spawn: health, ammo, pose, controls, camera.</summary>
        public void Respawn(Vector3 position, float yaw)
        {
            if (_droppedWeapon != null) Destroy(_droppedWeapon);
            bool wasDead = IsDead;
            if (_ragdoll != null) _ragdoll.Deactivate();
            if (Animator != null) Animator.enabled = true;
            if (FPSAnimator != null)
            {
                FPSAnimator.enabled = true;
                // Kinemation only rebuilds when it *sees* the Animator re-enable; it was disabled with us, so force it.
                if (wasDead) FPSAnimator.RebuildPlayables();
            }
            EndDeathCam();
            if (CameraController != null) CameraController.enabled = true;

            _controller.enabled = true;
            Movement.enabled = true;
            Teleport(position, yaw);
            _flinch = _flinchVel = _kick = _kickApplied = Vector2.zero;
            ForceResetAimState();
            _firePressed = false;
            _switching = false;
            _sprintOutUntil = 0f;
            _input.ClearAll();

            if (Health != null) Health.Revive();
            RefillAmmo();
            EquipImmediate(0);
            ForceResetAimState();
            if (Combatant != null) Combatant.SpawnTime = Time.time;
            ConfigureRole();
            Respawned?.Invoke(this);
            GameEvents.RaiseSpawned(Combatant);
        }
    }
}
