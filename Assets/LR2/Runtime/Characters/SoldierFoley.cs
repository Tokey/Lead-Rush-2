using System.Collections;
using LR2.Audio;
using LR2.Combat;
using LR2.Core;
using LR2.Weapons;
using UnityEngine;

namespace LR2.Characters
{
    /// <summary>
    /// Body + weapon foley for every soldier: kit and rifle rattle on each footstep (louder when sprinting), cloth on
    /// crouch/stand, effort breaths on jump / landing / mantle, sprint breathing with recovery afterwards, pain grunts,
    /// trigger-reset clicks after each shot (own gun) and the empty magazine clattering on the ground on empty reloads.
    /// Bots use the same sounds in 3D, so an enemy's gear can be heard around a corner.
    /// </summary>
    public class SoldierFoley : MonoBehaviour
    {
        [SerializeField, Range(0f, 2f)] private float volume = 1f;
        [Tooltip("Seconds of sprinting before heavy breathing starts.")]
        [SerializeField] private float breathAfterSprint = 1.2f;

        private SoldierController _s;
        private SoldierMovement _m;
        private Health _h;
        private FoleyLibrary _lib;
        private LR2Firearm _gun;
        private SoldierStance _lastStance;
        private float _sprintTime, _breathAt, _recoverUntil, _nextPain;
        private bool _inhale = true;

        private void Start()
        {
            _s = GetComponent<SoldierController>();
            _m = _s != null ? _s.Movement : null;
            _h = _s != null ? _s.Health : null;
            _lib = FoleyLibrary.Instance;
            if (_lib == null || _m == null) { enabled = false; return; }
            _m.Footstep += OnStep;
            _m.Jumped += OnJump;
            _m.Landed += OnLand;
            _m.MantleStarted += OnMantle;
            if (_h != null) _h.Damaged += OnDamaged;
            GameEvents.ShotFired += OnShot;
            _lastStance = _m.Stance;
        }

        private void OnDestroy()
        {
            if (_m != null)
            {
                _m.Footstep -= OnStep;
                _m.Jumped -= OnJump;
                _m.Landed -= OnLand;
                _m.MantleStarted -= OnMantle;
            }
            if (_h != null) _h.Damaged -= OnDamaged;
            GameEvents.ShotFired -= OnShot;
            if (_gun != null) _gun.ReloadStarted -= OnReload;
        }

        private float Vol => volume * (_s != null && _s.IsLocalPlayer ? 1f : 0.9f);
        private Vector3 Chest => transform.position + Vector3.up * 1.2f;
        private Vector3 Head => transform.position + Vector3.up * 1.65f;

        private void Play(SoundSet set, Vector3 pos, float scale = 1f, float delay = 0f)
        {
            if (set == null || !set.IsValid) return;
            LR2Audio.PlayAt(set, pos, Vol * scale, delay);
        }

        private void OnStep(float speed)
        {
            bool sprint = _s.IsSprinting;
            float k = Mathf.Lerp(0.4f, 1f, Mathf.Clamp01(speed / 6f));
            Play(sprint ? _lib.gearSprint : _lib.gearWalk, Chest, k);
            Play(_lib.weaponRattle, Chest, sprint ? 0.9f : 0.45f, 0.03f);
            if (_m.Stance == SoldierStance.Crouching || Random.value < 0.35f) Play(_lib.clothWalk, Chest, 0.6f);
            if (Random.value < 0.15f) Play(_lib.accent, Chest, 0.55f, 0.06f);
        }

        private void OnJump()
        {
            Play(_lib.breathJump, Head, 0.9f);
            Play(_lib.clothWalk, Chest, 0.8f);
        }

        private void OnLand(float speed)
        {
            bool hard = speed > 7f;
            Play(hard ? _lib.breathLandHard : _lib.breathLandSoft, Head, hard ? 1f : 0.6f, 0.05f);
            Play(_lib.weaponLand, Chest, Mathf.Clamp01(speed / 8f) + 0.35f);
            bool dirt = Physics.Raycast(transform.position + Vector3.up * 0.3f, Vector3.down, out var hit, 0.8f, LR2Layers.EnvironmentMask)
                        && hit.collider is TerrainCollider;
            Play(dirt ? _lib.landDirt : _lib.landConcrete, transform.position, Mathf.Clamp01(speed / 9f) + 0.3f);
        }

        private void OnMantle()
        {
            Play(_lib.breathMantle, Head, 1f);
            Play(_lib.mantle, Chest, 1f);
        }

        private void OnDamaged(DamageInfo d)
        {
            if (Time.time < _nextPain || _h == null || !_h.IsAlive) return;
            _nextPain = Time.time + 0.7f;
            Play(_lib.pain, Head, 1f, 0.04f);
        }

        private void OnShot(ShotEvent e)
        {
            if (_s == null || e.shooter != _s.Combatant || !_s.IsLocalPlayer) return;
            Play(_lib.triggerReset, Chest, 1f, 0.055f); // disconnector click as the trigger resets
        }

        private void OnReload()
        {
            if (_gun == null || _gun.AmmoInMagazine > 0) return;
            StartCoroutine(MagDrop(0.75f)); // empty mag hits the ground shortly after the mag-out
        }

        private IEnumerator MagDrop(float delay)
        {
            yield return new WaitForSeconds(delay);
            Vector3 p = transform.position + transform.right * 0.25f + transform.forward * 0.3f;
            Play(_lib.magDrop, p, 1f);
        }

        private void Update()
        {
            if (_s == null || _s.IsDead) return;
            // follow weapon switches for reload events
            var gun = _s.ActiveFirearm;
            if (gun != _gun)
            {
                if (_gun != null) _gun.ReloadStarted -= OnReload;
                _gun = gun;
                if (_gun != null) _gun.ReloadStarted += OnReload;
            }
            // cloth on stance changes
            var stance = _m.Stance;
            if (stance != _lastStance)
            {
                Play(stance == SoldierStance.Crouching ? _lib.stanceDown : _lib.stanceUp, Chest, 0.8f);
                _lastStance = stance;
            }
            // sprint breathing, then recovery breaths afterwards
            if (_s.IsSprinting)
            {
                _sprintTime += Time.deltaTime;
                if (_sprintTime > breathAfterSprint && Time.time >= _breathAt)
                {
                    Play(_inhale ? _lib.breathSprintIn : _lib.breathSprintOut, Head, Mathf.Clamp01(0.5f + _sprintTime * 0.08f));
                    _breathAt = Time.time + (_inhale ? 0.38f : 0.52f);
                    _inhale = !_inhale;
                }
            }
            else if (_sprintTime > 0f)
            {
                if (_sprintTime > breathAfterSprint) _recoverUntil = Time.time + Mathf.Min(4.5f, _sprintTime * 0.7f);
                _sprintTime = 0f;
            }
            else if (Time.time < _recoverUntil && Time.time >= _breathAt)
            {
                Play(_lib.breathRecover, Head, 0.5f);
                _breathAt = Time.time + Random.Range(0.9f, 1.3f);
            }
        }
    }
}
