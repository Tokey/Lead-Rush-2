// Based on KINEMATION FPS Animation Framework demo Weapon (2025). Serialized field names of the demo are kept so the
// LR2 builder can swap the component on demo prefabs without losing animation/recoil setup. LR2 adds the actual
// gunplay: ammo, hitscan ballistics, spread/bloom, layered audio, muzzle flash, casings, tracers and AI noise.
using System;
using System.Collections.Generic;
using Demo.Scripts.Runtime.AttachmentSystem;
using KINEMATION.FPSAnimationFramework.Runtime.Camera;
using KINEMATION.FPSAnimationFramework.Runtime.Core;
using KINEMATION.FPSAnimationFramework.Runtime.Playables;
using KINEMATION.FPSAnimationFramework.Runtime.Recoil;
using KINEMATION.ProceduralRecoilAnimationSystem.Runtime;
using KINEMATION.Shared.KAnimationCore.Runtime.Input;
using LR2.Audio;
using LR2.Characters;
using LR2.Core;
using LR2.FX;
using UnityEngine;

namespace LR2.Weapons
{
    public class LR2Firearm : LR2Item
    {
        [Header("General")]
        [SerializeField] [Range(0f, 120f)] private float fieldOfView = 90f;
        [SerializeField] private FPSAnimationAsset reloadClip;
        [SerializeField] private LR2.Characters.ReloadMotionBridge.Rest reloadRestPose;
        [Tooltip("Reload animation play rate (1 = authored speed). Below 1 = slower, more deliberate reloads.")]
        [SerializeField, Range(0.5f, 1.5f)] private float reloadPlayRate = 0.85f;
        [Tooltip("Multiplies reloadPlayRate. 1.5 = the user's 'reload 1.5x faster' (2026-11-17).")]
        [SerializeField, Range(0.5f, 3f)] private float reloadSpeedMultiplier = 1.5f;
        [Tooltip("Tactical reload: with a round still chambered, the empty-reload clip is cut shortly after the new magazine seats (no charging handle).")]
        [SerializeField] private bool tacticalReload = true;
        [Tooltip("How long after the mag-in cue (fraction of the full clip) a tactical reload ends and blends back to ready.")]
        [SerializeField, Range(0f, 0.3f)] private float tacticalEndAfterMagIn = 0.08f;
        [Tooltip("A tactical reload keeps the chambered round, so the gun holds magazine + 1 (30+1).")]
        [SerializeField] private bool chamberedRoundBonus = true;
        private FPSAnimationAsset _reloadRuntime;
        [SerializeField] private FPSCameraAnimation cameraReloadAnimation;
        [SerializeField] private FPSAnimationAsset grenadeClip;
        [SerializeField] private FPSCameraAnimation cameraGrenadeAnimation;

        [Header("Recoil")]
        [SerializeField] private FPSAnimationAsset fireClip;
        [SerializeField] private RecoilAnimData recoilData;
        [SerializeField] private RecoilPatternSettings recoilPatternSettings;
        [SerializeField] private FPSCameraShake cameraShake;
        [Min(0f)] [SerializeField] private float fireRate = 600f;
        [SerializeField] private bool supportsAuto = true;
        [SerializeField] private bool supportsBurst;
        [SerializeField] private int burstLength = 3;

        [Header("Attachments")]
        [SerializeField] private AttachmentGroup<BaseAttachment> barrelAttachments = new AttachmentGroup<BaseAttachment>();
        [SerializeField] private AttachmentGroup<BaseAttachment> gripAttachments = new AttachmentGroup<BaseAttachment>();
        [SerializeField] private List<AttachmentGroup<ScopeAttachment>> scopeGroups = new List<AttachmentGroup<ScopeAttachment>>();

        [Header("LR2 Gunplay")]
        [SerializeField] private WeaponDefinition definition;
        [SerializeField] private Transform muzzle;
        [SerializeField] private MuzzleFlash muzzleFlash;
        [SerializeField] private ShellEjector shellEjector;
        [Tooltip("Bots see tracers more often so the player can read where fire comes from.")]
        [SerializeField] private int botTracerEvery = 1;

        public event Action Fired;
        public event Action AmmoChanged;
        public event Action ReloadStarted;
        public event Action ReloadFinished;
        public event Action DryFired;

        public WeaponDefinition Definition => definition;
        public int AmmoInMagazine { get; private set; }
        public int ReserveAmmo { get; private set; }
        public int MagazineSize => definition != null ? definition.magazineSize : 30;
        public bool IsReloading { get; private set; }
        /// <summary>Kinemation reload animation (null = timer-only reload). Read by RetargetProxy's reload bridge.</summary>
        public FPSAnimationAsset ReloadClip => reloadClip;
        /// <summary>Baked t=0 helper-bone rest pose from reloadClip curves (avoids identity fallback in SampleAnimation).</summary>
        public LR2.Characters.ReloadMotionBridge.Rest ReloadRestPose => reloadRestPose;
        public bool IsEmpty => AmmoInMagazine <= 0;
        public FireMode FireMode => _fireMode;
        public Transform Muzzle => muzzle != null ? muzzle : transform;
        public float ReloadProgress => IsReloading ? Mathf.Clamp01((Time.time - _reloadStart) / Mathf.Max(0.01f, _reloadDuration)) : 0f;
        /// <summary>0..1 over the part of the clip this reload actually plays (tactical reloads end early), for HUD bars.</summary>
        public float ReloadDisplayProgress => IsReloading ? Mathf.Clamp01(ReloadProgress / Mathf.Max(0.05f, _reloadEndAt)) : 0f;
        public override bool IsBusy => IsReloading || Time.time < _equipReadyAt;
        public override string DisplayName => definition != null ? definition.displayName : base.DisplayName;

        public bool CanFire => Owner != null && !IsReloading && AmmoInMagazine > 0 && Time.time >= _equipReadyAt && Owner.CanShootNow;

        public float Rpm => definition != null ? definition.rpm : Mathf.Max(60f, fireRate);

        /// <summary>Current cone half-angle in degrees (drives bullet spread and the dynamic crosshair).</summary>
        public float CurrentSpread
        {
            get
            {
                if (definition == null || Owner == null) return 1f;
                var move = Owner.Movement;
                float hip = definition.hipSpread + _bloom;
                if (move != null)
                {
                    hip += definition.moveSpreadAdd * Mathf.Clamp01(move.Speed / 5f);
                    if (!move.IsGrounded) hip += definition.airSpreadAdd;
                    if (move.Stance == SoldierStance.Crouching) hip *= definition.crouchSpreadMultiplier;
                }
                float ads = definition.adsSpread + _bloom * 0.12f + (move != null ? definition.moveSpreadAdd * 0.1f * Mathf.Clamp01(move.Speed / 5f) : 0f);
                return Mathf.Lerp(hip, ads, Owner.AimBlend);
            }
        }

        // Kinemation references
        private Animator _controllerAnimator;
        private UserInputController _userInput;
        private IPlayablesController _playables;
        private FPSCameraController _cameraController;
        private FPSAnimator _fpsAnimator;
        private FPSAnimatorEntity _entity;
        private RecoilAnimation _recoilAnimation;
        private RecoilPattern _recoilPattern;
        private Animator _weaponAnimator;
        private int _scopeIndex;

        // Gunplay state
        private FireMode _fireMode = FireMode.Auto;
        private bool _triggerHeld;
        private bool _shotThisPress;
        private int _burstRemaining;
        private float _nextShotTime;
        private float _lastShotTime = -10f;
        private float _bloom;
        private bool _recoilActive;
        private int _shotCounter;
        private float _equipReadyAt;
        private bool _ammoInitialized;
        private bool _dryFiredThisPress;
        private bool _indoors;

        // Reload
        private float _reloadStart;
        private float _reloadDuration;
        private bool _reloadEmpty;
        private float _reloadEndAt = 1f; // fraction of the full clip at which this reload completes (tactical < 1)
        private float ReloadRate => Mathf.Max(0.1f, reloadPlayRate * reloadSpeedMultiplier);
        /// <summary>True while a tactical reload (round chambered, no charging handle) is playing.</summary>
        public bool IsTacticalReload => IsReloading && !_reloadEmpty && _reloadEndAt < 1f;
        /// <summary>Rounds the gun holds after a reload from this state: magazine + 1 when a round stays chambered.</summary>
        private int ReloadCapacity => MagazineSize + (chamberedRoundBonus && AmmoInMagazine > 0 ? 1 : 0);
        private bool _magOutPlayed, _magInPlayed, _boltPlayed;

        public void Configure(WeaponDefinition def, Transform muzzlePoint, MuzzleFlash flash, ShellEjector ejector)
        {
            definition = def;
            muzzle = muzzlePoint;
            muzzleFlash = flash;
            shellEjector = ejector;
        }

        private void Awake() => EnsureAmmo();

        private void EnsureAmmo()
        {
            if (_ammoInitialized) return;
            _ammoInitialized = true;
            AmmoInMagazine = MagazineSize;
            ReserveAmmo = definition != null ? definition.startingReserve : 90;
            _fireMode = (definition != null ? definition.allowAuto : supportsAuto) ? FireMode.Auto : FireMode.Semi;
        }

        private Animator ResolveWeaponAnimator()
        {
            var all = GetComponentsInChildren<Animator>(true);
            Animator chosen = null;
            foreach (var cand in all)
            {
                if (cand == null) continue;
                if (cand.transform.Find("AK12Rig") != null || cand.transform.Find("FAL") != null ||
                    cand.transform.Find("Mk18") != null || cand.transform.Find("Mk23Mod0Rig") != null)
                {
                    chosen = cand;
                    break;
                }
            }
            if (chosen == null && all.Length > 0) chosen = all[all.Length - 1];
            foreach (var extra in all)
            {
                if (chosen != null && extra != null && extra != chosen && extra.gameObject == gameObject)
                    extra.enabled = false;
            }
            if (chosen != null)
            {
                chosen.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                chosen.Rebind();
                chosen.Play("Empty", 0, 0f);
                chosen.Update(0f);
            }
            return chosen;
        }

        // ------------------------------------------------------------------ equip / FOV
        public override void OnEquip(SoldierController owner)
        {
            if (owner == null) return;
            base.OnEquip(owner);
            EnsureAmmo();

            _fpsAnimator = owner.FPSAnimator;
            _entity = GetComponent<FPSAnimatorEntity>();
            _weaponAnimator = ResolveWeaponAnimator();
            _controllerAnimator = owner.Animator;
            _userInput = owner.UserInput;
            _playables = owner.Playables;
            _cameraController = owner.CameraController;
            _recoilAnimation = owner.RecoilAnimation;
            _recoilPattern = owner.RecoilPattern;

            if (_playables != null && overrideController != null && _controllerAnimator != null &&
                overrideController != _controllerAnimator.runtimeAnimatorController)
                _playables.UpdateAnimatorController(overrideController);

            InitializeAttachments();
            if (_fpsAnimator != null)
            {
                _fpsAnimator.LinkAnimatorProfile(gameObject);
                barrelAttachments.Initialize(_fpsAnimator);
                gripAttachments.Initialize(_fpsAnimator);
            }

            if (_recoilAnimation != null) _recoilAnimation.Init(recoilData, Rpm, _fireMode);
            if (_recoilPattern != null) _recoilPattern.Init(RuntimePattern());
            if (_fpsAnimator != null) _fpsAnimator.LinkAnimatorLayer(equipMotion);

            _equipReadyAt = Time.time + (definition != null ? definition.equipTime : 0.5f);
            _triggerHeld = false;
            UpdateFieldOfView(false);
            if (definition != null && definition.audio != null) LR2Audio.PlayAt(definition.audio.equip, transform.position);
        }

        public override void OnUnEquip()
        {
            CancelActions();
            if (_fpsAnimator != null) _fpsAnimator.LinkAnimatorLayer(unEquipMotion);
        }

        private void InitializeAttachments()
        {
            if (_fpsAnimator != null)
                foreach (var group in scopeGroups) group.Initialize(_fpsAnimator);
            _scopeIndex = 0;
            if (scopeGroups.Count == 0) return;
            UpdateAimPoint();
        }

        private void UpdateAimPoint()
        {
            if (scopeGroups.Count == 0 || _entity == null) return;
            var scope = scopeGroups[_scopeIndex].GetActiveAttachment();
            if (scope != null && scope.aimPoint != null) _entity.defaultAimPoint = scope.aimPoint;
        }

        public override void UpdateFieldOfView(bool aiming)
        {
            // Settings and fieldOfView are HORIZONTAL degrees (CoD/BF convention); Unity cameras take the vertical angle.
            float hipFovH = Owner != null && Owner.IsLocalPlayer ? LR2Settings.FieldOfView : fieldOfView;
            var viewCam = Owner != null ? Owner.ViewCamera : null;
            float fov = LR2Settings.VerticalFov(hipFovH, viewCam != null ? viewCam.aspect : 16f / 9f);
            float sens = 1f;
            if (aiming)
            {
                fov *= definition != null ? definition.adsFovMultiplier : 0.8f;
                if (scopeGroups.Count != 0)
                {
                    var scope = scopeGroups[_scopeIndex].GetActiveAttachment();
                    if (scope != null)
                    {
                        fov *= scope.aimFovZoom > 0.01f ? Mathf.Min(1f, scope.aimFovZoom) : 1f;
                        sens = scope.sensitivityMultiplier;
                    }
                }
            }
            if (_userInput != null) _userInput.SetValue("SensitivityMultiplier", sens);
            if (_cameraController != null) _cameraController.UpdateTargetFOV(fov);
        }

        public override bool OnAimPressed()
        {
            if (_userInput != null) _userInput.SetValue(FPSANames.IsAiming, true);
            UpdateFieldOfView(true);
            if (_recoilAnimation != null) _recoilAnimation.isAiming = true;
            return true;
        }

        public override bool OnAimReleased()
        {
            if (_userInput != null) _userInput.SetValue(FPSANames.IsAiming, false);
            UpdateFieldOfView(false);
            if (_recoilAnimation != null) _recoilAnimation.isAiming = false;
            return true;
        }

        public override void OnCycleScope()
        {
            if (scopeGroups.Count == 0) return;
            _scopeIndex = (_scopeIndex + 1) % scopeGroups.Count;
            UpdateAimPoint();
            UpdateFieldOfView(Owner != null && Owner.IsAiming);
        }

        public override void OnChangeFireMode()
        {
            bool canAuto = definition != null ? definition.allowAuto : supportsAuto;
            bool canBurst = definition != null ? definition.allowBurst : supportsBurst;
            if (_fireMode == FireMode.Semi && canBurst) _fireMode = FireMode.Burst;
            else if (_fireMode != FireMode.Auto && canAuto) _fireMode = FireMode.Auto;
            else _fireMode = FireMode.Semi;
            if (_recoilAnimation != null) _recoilAnimation.fireMode = _fireMode;
            if (definition != null && definition.audio != null) LR2Audio.PlayAt(definition.audio.fireModeSwitch, transform.position);
        }

        // ------------------------------------------------------------------ trigger
        public override bool OnFirePressed()
        {
            _triggerHeld = true;
            _shotThisPress = false;
            _dryFiredThisPress = false;
            _burstRemaining = definition != null ? definition.burstCount : Mathf.Max(1, burstLength);
            return true;
        }

        public override bool OnFireReleased()
        {
            _triggerHeld = false;
            StopRecoil();
            return true;
        }

        private RecoilPatternSettings _patternRuntime;
        private Vector2 _patternBaseV, _patternBaseH;

        /// <summary>Per-gun copy of the Kinemation recoil pattern, so per-weapon and ADS scaling never touch the shared demo asset.</summary>
        private RecoilPatternSettings RuntimePattern()
        {
            if (recoilPatternSettings == null) return null;
            if (_patternRuntime == null)
            {
                _patternRuntime = Instantiate(recoilPatternSettings);
                _patternRuntime.name = recoilPatternSettings.name + " (LR2 runtime)";
                _patternBaseV = recoilPatternSettings.verticalRecoil;
                _patternBaseH = recoilPatternSettings.horizontalRecoil;
            }
            ScalePatternForShot(false);
            return _patternRuntime;
        }

        /// <summary>Per-shot climb = demo pattern x weapon scale x (ADS multiplier while aiming). Read by RecoilPattern.OnFireStart.</summary>
        private void ScalePatternForShot(bool aiming)
        {
            if (_patternRuntime == null) return;
            float v = definition != null ? definition.recoilPatternScale : 1f;
            float h = definition != null ? definition.recoilPatternHorizontalScale : 1f;
            float ads = aiming && definition != null ? definition.adsRecoilMultiplier : 1f;
            _patternRuntime.verticalRecoil = _patternBaseV * (v * ads);
            _patternRuntime.horizontalRecoil = _patternBaseH * (h * ads);
        }

        /// <summary>Current per-shot vertical climb range in degrees (QA).</summary>
        public Vector2 PatternVerticalPerShot => _patternRuntime != null ? _patternRuntime.verticalRecoil : Vector2.zero;

        private void StopRecoil()
        {
            if (!_recoilActive) return;
            _recoilActive = false;
            if (_recoilAnimation != null) _recoilAnimation.Stop();
            if (_recoilPattern != null) _recoilPattern.OnFireEnd();
        }

        public override void Tick(float deltaTime)
        {
            if (definition != null) _bloom = Mathf.Max(0f, _bloom - definition.bloomRecovery * deltaTime);
            if (IsReloading) UpdateReload();

            bool wantsShot = _triggerHeld && (_fireMode == FireMode.Auto || (_fireMode == FireMode.Semi && !_shotThisPress) ||
                                              (_fireMode == FireMode.Burst && _burstRemaining > 0));
            if (wantsShot && Time.time >= _nextShotTime)
            {
                if (CanFire)
                {
                    Shoot();
                    _shotThisPress = true;
                    if (_fireMode == FireMode.Burst) _burstRemaining--;
                    _nextShotTime = Time.time + (definition != null ? definition.SecondsBetweenShots : 0.1f);
                }
                else if (AmmoInMagazine <= 0 && !IsReloading && !_dryFiredThisPress && Owner != null && Owner.CanShootNow)
                {
                    _dryFiredThisPress = true;
                    if (definition != null && definition.audio != null) LR2Audio.PlayAt(definition.audio.dryFire, transform.position);
                    DryFired?.Invoke();
                    StopRecoil();
                    if (ReserveAmmo > 0) Owner.RequestReload(); // CoD auto-reload on empty
                }
            }

            // Semi/burst: release recoil after one cycle like the Kinemation demo.
            if (_recoilActive && (!_triggerHeld || _fireMode != FireMode.Auto) && Time.time >= _nextShotTime) StopRecoil();
        }

        private void Shoot()
        {
            AmmoInMagazine--;
            _shotCounter++;
            var owner = Owner;
            var combatant = owner.Combatant;
            Vector3 origin = owner.AimOrigin;
            Vector3 aimDir = owner.AimDirection;
            float spread = CurrentSpread;
            int pellets = definition != null ? Mathf.Max(1, definition.pellets) : 1;
            int tracerEvery = combatant != null && combatant.IsLocalPlayer
                ? (definition != null ? definition.tracerEvery : 1)
                : botTracerEvery;
            bool tracer = tracerEvery > 0 && _shotCounter % tracerEvery == 0;

            for (int i = 0; i < pellets; i++)
            {
                Vector3 dir = LR2Math.RandomInCone(aimDir, spread);
                Ballistics.Fire(combatant, definition, origin, dir, Muzzle.position, tracer && i == 0, gameObject);
            }

            if (definition != null) _bloom = Mathf.Min(definition.maxBloom, _bloom + definition.bloomPerShot);

            // Visuals / Kinemation
            if (muzzleFlash != null) muzzleFlash.Play();
            if (shellEjector != null && definition != null && definition.audio != null)
                shellEjector.Eject(owner.Movement != null ? owner.Movement.Velocity : Vector3.zero, definition.audio.shellCasings);
            if (_weaponAnimator != null)
            {
                _weaponAnimator.speed = 1f;
                _weaponAnimator.Play("Fire", 0, 0f);
            }
            if (_cameraController != null) _cameraController.PlayCameraShake(cameraShake);
            if (fireClip != null && _playables != null) _playables.PlayAnimation(fireClip);
            if (_recoilAnimation != null && recoilData != null) _recoilAnimation.Play();
            if (_recoilPattern != null)
            {
                ScalePatternForShot(owner.IsAiming);
                _recoilPattern.OnFireStart();
            }
            _recoilActive = true;

            if (definition != null)
            {
                Vector2 kick = definition.cameraKick * (owner.IsAiming ? definition.adsKickMultiplier : 1f);
                owner.AddViewKick(new Vector2(UnityEngine.Random.Range(-kick.y, kick.y), -kick.x));
            }

            PlayShotAudio();
            _lastShotTime = Time.time;

            // Gameplay events
            bool suppressed = definition != null && definition.suppressed;
            if (combatant != null)
            {
                combatant.stats.shotsFired++;
                if (!suppressed) combatant.LastLoudShotTime = Time.time;
            }
            if (owner.Health != null) owner.Health.CancelSpawnProtection();
            float radius = definition != null ? definition.audibleRadius : 60f;
            GameEvents.RaiseShotFired(new ShotEvent
            {
                shooter = combatant,
                origin = origin,
                direction = aimDir,
                audibleRadius = radius,
                suppressed = suppressed,
                weaponName = definition != null ? definition.shortName : DisplayName
            });
            GameEvents.RaiseNoise(combatant, Muzzle.position, radius, NoiseType.Gunshot);
            Fired?.Invoke();
            AmmoChanged?.Invoke();
        }

        private void PlayShotAudio()
        {
            if (definition == null || definition.audio == null) return;
            var a = definition.audio;
            Vector3 pos = Muzzle.position;
            var combatant = Owner.Combatant;
            // Indoor check is cheap (2 rays) but only needed every few shots.
            if (_shotCounter % 4 == 1 || Time.time - _lastShotTime > 0.4f) _indoors = LR2Audio.IsIndoors(pos);
            if (combatant != null && combatant.IsLocalPlayer)
            {
                bool first = Time.time - _lastShotTime > 0.35f && a.fireFirstShot.IsValid;
                LR2Audio.PlayAt(first ? a.fireFirstShot : a.firePlayer, pos);
                LR2Audio.PlayAt(Owner.IsAiming && a.fireMechAds.IsValid ? a.fireMechAds : a.fireMech, pos);
                if (_shotCounter % 2 == 0 || !_triggerHeld) LR2Audio.PlayAt(a.TailFor(_indoors), pos);
                LR2Audio.Duck(_indoors ? 0.75f : 0.55f);
            }
            else
            {
                Vector3 listener = LR2Audio.ListenerPosition;
                float dist = Vector3.Distance(listener, pos);
                LR2Audio.PlayAtWithTravelTime(a.NpcShotForDistance(dist), pos, listener);
                if (dist < 30f && (_shotCounter % 3 == 0)) LR2Audio.PlayAt(a.TailFor(_indoors), pos, 0.6f);
                if (dist < 20f) LR2Audio.Duck(0.25f);
            }
            if (_shotCounter % 2 == 0 || !_triggerHeld) PlayEchoes(pos, a.TailFor(_indoors));
        }

        private static readonly RaycastHit[] EchoHits = new RaycastHit[1];

        /// <summary>
        /// Early reflections: probes 8 horizontal directions from the muzzle; every wall / building face between 5 and
        /// 90 m bounces a delayed (speed of sound, there and back), quieter copy of the tail back from that surface.
        /// Courtyards and long walls slap back, open forest stays soft.
        /// </summary>
        private void PlayEchoes(Vector3 origin, SoundSet tail)
        {
            if (tail == null || !tail.IsValid) return;
            int played = 0;
            float spin = (_shotCounter * 37f) % 45f;
            for (int i = 0; i < 8 && played < 3; i++)
            {
                Vector3 dir = Quaternion.Euler(UnityEngine.Random.Range(-4f, 6f), spin + i * 45f, 0f) * Vector3.forward;
                if (Physics.RaycastNonAlloc(origin, dir, EchoHits, 90f, LR2Layers.EnvironmentMask, QueryTriggerInteraction.Ignore) == 0) continue;
                var hit = EchoHits[0];
                if (hit.distance < 5f || hit.collider is TerrainCollider) continue;
                float delay = hit.distance * 2f / 343f;
                float vol = Mathf.Lerp(0.7f, 0.22f, hit.distance / 90f) * (_indoors ? 0.6f : 1f);
                LR2Audio.PlayAt(tail, hit.point + hit.normal * 0.5f, vol, delay);
                played++;
            }
        }

        // ------------------------------------------------------------------ reload
        public override bool OnReload()
        {
            if (IsReloading || AmmoInMagazine >= ReloadCapacity || ReserveAmmo <= 0 || Time.time < _equipReadyAt) return false;

            _reloadEmpty = AmmoInMagazine == 0;
            bool hasClip = FPSAnimationAsset.IsValid(reloadClip);
            if (hasClip && _reloadRuntime == null)
            {
                _reloadRuntime = Instantiate(reloadClip);
                var bt = _reloadRuntime.blendTime;
                bt.rateScale = ReloadRate;
                _reloadRuntime.blendTime = bt;
            }
            _reloadDuration = hasClip
                ? reloadClip.clip.length / ReloadRate
                : (definition != null ? (_reloadEmpty ? definition.emptyReloadTime : definition.reloadTime) / reloadSpeedMultiplier : 2.4f);
            // Only empty-reload clips exist, so a tactical reload plays the same clip and ends right after the mag seats.
            var cues = definition != null ? definition.audio : null;
            _reloadEndAt = tacticalReload && !_reloadEmpty && hasClip && cues != null
                ? Mathf.Clamp(cues.magInAt + tacticalEndAfterMagIn, 0.3f, 1f) : 1f;
            _reloadStart = Time.time;
            IsReloading = true;
            _magOutPlayed = _magInPlayed = _boltPlayed = false;
            OnFireReleased();

            if (hasClip && _playables != null) _playables.PlayAnimation(_reloadRuntime, 0f);
            if (_weaponAnimator != null)
            {
                _weaponAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                _weaponAnimator.Rebind();
                _weaponAnimator.speed = ReloadRate;
                _weaponAnimator.Play("Reload", 0, 0f);
                _weaponAnimator.Update(0f);
            }
            if (_cameraController != null) _cameraController.PlayCameraAnimation(cameraReloadAnimation);

            ReloadStarted?.Invoke();
            GameEvents.RaiseNoise(Owner != null ? Owner.Combatant : null, transform.position, 9f, NoiseType.Reload);
            return true;
        }

        /// <summary>Aggressive reload feel: camera jolts on the local player's reload cues (B27/B29).</summary>
        private void LocalReloadJolt(int cue) { if (Owner != null && Owner.IsLocalPlayer) CameraShake.ReloadCue(cue); }

        private void UpdateReload()
        {
            float t = ReloadProgress;
            var a = definition != null ? definition.audio : null;
            if (a != null)
            {
                if (!_magOutPlayed && t >= a.magOutAt) { _magOutPlayed = true; LR2Audio.PlayAt(a.magOut, transform.position); LocalReloadJolt(0); }
                if (!_magInPlayed && t >= a.magInAt) { _magInPlayed = true; LR2Audio.PlayAt(a.magIn, transform.position); LocalReloadJolt(1); }
                if (!_boltPlayed && _reloadEmpty && t >= a.boltAt) { _boltPlayed = true; LR2Audio.PlayAt(a.boltCharge, transform.position); LocalReloadJolt(2); }
            }
            if (t < _reloadEndAt) return;

            bool tactical = _reloadEndAt < 1f;
            int need = ReloadCapacity - AmmoInMagazine;
            int take = Mathf.Min(need, ReserveAmmo);
            AmmoInMagazine += take;
            ReserveAmmo -= take;
            IsReloading = false;
            if (tactical)
            {
                // Cut before the charging-handle part of the clip and blend the arms back to ready.
                if (_playables != null) _playables.StopAnimation(0.2f);
                if (_weaponAnimator != null) { _weaponAnimator.Rebind(); _weaponAnimator.Play("Empty", 0, 0f); _weaponAnimator.Update(0f); }
            }
            if (_weaponAnimator != null) _weaponAnimator.speed = 1f;
            AmmoChanged?.Invoke();
            ReloadFinished?.Invoke();
        }

        public override void CancelActions()
        {
            _triggerHeld = false;
            StopRecoil();
            if (!IsReloading) return;
            IsReloading = false;
            if (_weaponAnimator != null)
            {
                _weaponAnimator.speed = 1f;
                _weaponAnimator.Rebind();
                _weaponAnimator.Play("Empty", 0, 0f);
                _weaponAnimator.Update(0f);
            }
            if (_playables != null) _playables.StopAnimation(0.15f);
        }

        public override void RefillAmmo()
        {
            _ammoInitialized = true;
            AmmoInMagazine = MagazineSize;
            ReserveAmmo = definition != null ? definition.startingReserve : 90;
            IsReloading = false;
            _bloom = 0f;
            AmmoChanged?.Invoke();
        }

        public override int AddReserveAmmo(int rounds)
        {
            int max = definition != null ? definition.maxReserve : 210;
            int accepted = Mathf.Clamp(max - ReserveAmmo, 0, rounds);
            ReserveAmmo += accepted;
            if (accepted > 0) AmmoChanged?.Invoke();
            return accepted;
        }
    }
}
