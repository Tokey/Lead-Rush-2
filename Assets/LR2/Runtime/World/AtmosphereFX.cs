using UnityEngine;

namespace LR2.World
{
    /// <summary>
    /// Lightweight, map-placeable ground atmosphere. The low layer is particle-based so it remains
    /// readable at gameplay distance; force-field particles give soldiers a visible wake without
    /// changing the global HDRP fog budget.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AtmosphereFX : MonoBehaviour
    {
        [Header("Ground layer")]
        [SerializeField, Min(0.3f)] private float layerHeight = 0.55f;
        [SerializeField, Min(0.1f)] private float radius = 18f;
        [SerializeField, Range(0f, 1f)] private float mistOpacity = 0.16f;
        [SerializeField, Min(0f)] private float mistRate = 28f;

        [Header("Ambient motes")]
        [SerializeField, Range(0f, 1f)] private float dustOpacity = 0.3f;
        [SerializeField, Min(0f)] private float dustRate = 16f;
        [SerializeField] private Color mistColor = new Color(0.7f, 0.75f, 0.76f, 1f);
        [SerializeField] private Color dustColor = new Color(0.48f, 0.43f, 0.36f, 1f);

        private ParticleSystem _mist;
        private ParticleSystem _dust;
        private ParticleSystemForceField _forceField;

        private void Awake()
        {
            _mist = CreateLayer("Ground Mist", mistColor, mistOpacity, mistRate, layerHeight, 0.5f);
            _dust = CreateLayer("Dust Ash Embers", dustColor, dustOpacity, dustRate, Mathf.Max(1f, layerHeight + 1.2f), 1.4f);
            var forceObject = new GameObject("Soldier Reactive Air");
            forceObject.transform.SetParent(transform, false);
            _forceField = forceObject.AddComponent<ParticleSystemForceField>();
            _forceField.enabled = true;
        }

        private ParticleSystem CreateLayer(string name, Color color, float opacity, float rate, float height, float lifetime)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.7f, lifetime * 1.3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.03f, 0.12f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.65f);
            main.startColor = new Color(color.r, color.g, color.b, Mathf.Clamp01(opacity));
            main.maxParticles = 256;
            var emission = ps.emission;
            emission.rateOverTime = Mathf.Max(0f, rate);
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius;
            go.transform.localPosition = Vector3.up * Mathf.Clamp(height * 0.5f, 0.3f, 1.5f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.25f;
            ps.Play();
            return ps;
        }

        private void OnDestroy()
        {
            if (_mist != null) Destroy(_mist.gameObject);
            if (_dust != null) Destroy(_dust.gameObject);
            if (_forceField != null) Destroy(_forceField.gameObject);
        }
    }
}
