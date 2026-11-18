using LR2.Audio;
using LR2.Combat;
using LR2.Core;
using UnityEngine;

namespace LR2.Characters
{
    [DisallowMultipleComponent]
    public sealed class PainVoice : MonoBehaviour
    {
        private SoldierController _soldier;
        private Health _health;
        private FoleyLibrary _foley;
        private LR2CommonSounds _common;
        private float _nextVoiceAt;
        private float _nextBreathAt;

        private void Awake()
        {
            _soldier = GetComponent<SoldierController>();
            _health = _soldier != null ? _soldier.Health : null;
            _foley = FoleyLibrary.Instance;
            _common = LR2CommonSounds.Instance;
        }

        private void OnEnable()
        {
            if (_health == null) return;
            _health.Damaged += OnDamaged;
            _health.Died += OnDied;
        }

        private void OnDisable()
        {
            if (_health == null) return;
            _health.Damaged -= OnDamaged;
            _health.Died -= OnDied;
        }

        private void Update()
        {
            if (_soldier == null || _health == null || !_soldier.IsLocalPlayer || !_health.IsAlive ||
                _health.Normalized >= 0.35f || Time.time < _nextBreathAt) return;

            var set = _foley != null ? _foley.breathRecover : null;
            if (set == null || !set.IsValid) return;
            float urgency = Mathf.InverseLerp(0.35f, 0.05f, _health.Normalized);
            PlayLocal(set, Mathf.Lerp(0.12f, 0.2f, urgency), Random.Range(0.94f, 1.02f));
            _nextBreathAt = Time.time + Mathf.Lerp(1.7f, 1.05f, urgency);
        }

        private void OnDamaged(DamageInfo info)
        {
            if (!_health.IsAlive || Time.time < _nextVoiceAt) return;
            bool heavy = info.amount >= 28f;
            if (!_soldier.IsLocalPlayer && !heavy && Random.value > 0.45f) return;

            SoundSet set = _soldier.IsLocalPlayer
                ? (_common != null && _common.playerHurt.IsValid ? _common.playerHurt : _foley != null ? _foley.pain : null)
                : (_common != null && _common.painGrunt.IsValid ? _common.painGrunt : _foley != null ? _foley.pain : null);
            if (set == null || !set.IsValid) return;

            float healthFactor = 1f - _health.Normalized;
            float scale = Mathf.Lerp(0.16f, 0.24f, Mathf.Clamp01(info.amount / 45f + healthFactor * 0.35f));
            Vector3 head = transform.position + Vector3.up * 1.55f;
            if (_soldier.IsLocalPlayer)
                PlayLocal(set, scale, Random.Range(0.84f, 0.94f));
            else
                PlayRemote(set, head, scale, Random.Range(0.84f, 0.94f));
            _nextVoiceAt = Time.time + Random.Range(0.6f, 1.2f);
        }

        private void OnDied(DamageInfo info)
        {
            if (Random.value < 0.3f) return;
            SoundSet set = _soldier.IsLocalPlayer
                ? (_common != null && _common.playerDeath.IsValid ? _common.playerDeath : null)
                : (_common != null && _common.deathGrunt.IsValid ? _common.deathGrunt : null);
            if (set == null || !set.IsValid) return;

            float scale = _soldier.IsLocalPlayer ? 0.18f : 0.24f;
            if (_soldier.IsLocalPlayer)
                PlayLocal(set, scale, Random.Range(0.82f, 0.92f));
            else
                PlayRemote(set, transform.position + Vector3.up * 1.4f, scale, Random.Range(0.82f, 0.92f));
        }

        private static void PlayLocal(SoundSet set, float volume, float pitch)
        {
            LR2Audio.PlayClip(set.Pick(), Vector3.zero, set.volume * volume, pitch, 1f, 500f, 0f,
                AudioCategory.Voice, set.priority);
        }

        private static void PlayRemote(SoundSet set, Vector3 position, float volume, float pitch)
        {
            LR2Audio.PlayClip(set.Pick(), position, set.volume * volume, pitch, set.minDistance, set.maxDistance,
                set.spatialBlend, AudioCategory.Voice, set.priority);
        }
    }

    [DefaultExecutionOrder(-490)]
    internal sealed class PainVoiceBootstrap : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (FindAnyObjectByType<PainVoiceBootstrap>() != null) return;
            var go = new GameObject("[LR2 Pain Voices]");
            DontDestroyOnLoad(go);
            go.AddComponent<PainVoiceBootstrap>();
        }

        private void OnEnable()
        {
            GameEvents.CombatantSpawned += AttachCombatant;
            AttachAll();
        }

        private void OnDisable() => GameEvents.CombatantSpawned -= AttachCombatant;

        private static void AttachAll()
        {
            var soldiers = FindObjectsByType<SoldierController>(FindObjectsInactive.Include);
            for (int i = 0; i < soldiers.Length; i++)
                Attach(soldiers[i]);
        }

        private static void AttachCombatant(Combatant combatant)
        {
            if (combatant == null) return;
            Attach(combatant.GetComponent<SoldierController>());
        }

        private static void Attach(SoldierController soldier)
        {
            if (soldier != null && soldier.GetComponent<PainVoice>() == null)
                soldier.gameObject.AddComponent<PainVoice>();
        }
    }
}
