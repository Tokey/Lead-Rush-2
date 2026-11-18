using LR2.Combat;
using LR2.Core;
using UnityEngine;

namespace LR2.Weapons
{
    /// <summary>Low-intensity weapon laser shared by player and bot firearms.</summary>
    [DisallowMultipleComponent]
    public sealed class WeaponLaser : MonoBehaviour
    {
        private const float MaxDistance = 120f;
        private const float BeamWidth = 0.0015f;
        private static readonly RaycastHit[] Hits = new RaycastHit[16];

        private LR2Firearm _firearm;
        private LineRenderer _beam;
        private Transform _dot;
        private Renderer _dotRenderer;
        private Material _beamMaterial;
        private Material _dotMaterial;

        private void Awake()
        {
            _firearm = GetComponent<LR2Firearm>();
            _beam = gameObject.AddComponent<LineRenderer>();
            _beam.positionCount = 2;
            _beam.startWidth = BeamWidth;
            _beam.endWidth = BeamWidth * 0.65f;
            _beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _beam.receiveShadows = false;
            _beamMaterial = CreateMaterial(new Color(1f, 0.08f, 0.025f, 0.62f));
            _beam.sharedMaterial = _beamMaterial;

            var dotObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dotObject.name = "LaserDot";
            dotObject.transform.SetParent(transform, false);
            dotObject.transform.localScale = Vector3.one * 0.018f;
            var collider = dotObject.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            _dot = dotObject.transform;
            _dotRenderer = dotObject.GetComponent<Renderer>();
            _dotMaterial = CreateMaterial(new Color(1f, 0.04f, 0.01f, 0.9f));
            _dotRenderer.sharedMaterial = _dotMaterial;
            SetVisible(false);
        }

        private void OnDestroy()
        {
            if (_beamMaterial != null) Destroy(_beamMaterial);
            if (_dotMaterial != null) Destroy(_dotMaterial);
        }

        private void LateUpdate()
        {
            if (_firearm == null || !LR2Settings.LaserEnabled || !_firearm.gameObject.activeInHierarchy)
            {
                SetVisible(false);
                return;
            }

            Transform muzzle = _firearm.Muzzle;
            if (muzzle == null)
            {
                SetVisible(false);
                return;
            }

            Vector3 start = muzzle.position + muzzle.forward * 0.04f;
            Vector3 end = start + muzzle.forward * MaxDistance;
            bool hitFound = false;
            int hitCount = Physics.RaycastNonAlloc(start, muzzle.forward, Hits, MaxDistance, LR2Layers.BulletMask,
                QueryTriggerInteraction.Ignore);
            float nearest = MaxDistance;
            for (int i = 0; i < hitCount; i++)
            {
                var hit = Hits[i];
                if (hit.collider == null || IsOwnedCollider(hit.collider.transform)) continue;
                if (hit.distance < nearest)
                {
                    nearest = hit.distance;
                    end = hit.point;
                    hitFound = true;
                }
            }

            _beam.SetPosition(0, start);
            _beam.SetPosition(1, end);
            if (hitFound)
            {
                _dot.position = end - muzzle.forward * 0.006f;
                _dot.rotation = Quaternion.LookRotation(-muzzle.forward, Vector3.up);
            }
            SetVisible(true, hitFound);
        }

        private bool IsOwnedCollider(Transform colliderTransform)
        {
            if (_firearm.Owner == null) return colliderTransform.IsChildOf(transform.root);
            return colliderTransform.IsChildOf(_firearm.Owner.transform) || colliderTransform.IsChildOf(transform.root);
        }

        private void SetVisible(bool visible, bool showDot = false)
        {
            if (_beam != null) _beam.enabled = visible;
            if (_dotRenderer != null) _dotRenderer.enabled = visible && showDot;
        }

        private static Material CreateMaterial(Color color)
        {
            Shader shader = Shader.Find("HDRP/Unlit") ?? Shader.Find("Unlit/Color");
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            if (material.HasProperty("_UnlitColor")) material.SetColor("_UnlitColor", color);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_EmissiveColor")) material.SetColor("_EmissiveColor", color * 2f);
            if (material.HasProperty("_EmissiveIntensity")) material.SetFloat("_EmissiveIntensity", 2f);
            return material;
        }
    }

    [DefaultExecutionOrder(-500)]
    internal sealed class WeaponLaserBootstrap : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (FindAnyObjectByType<WeaponLaserBootstrap>() != null) return;
            var go = new GameObject("[LR2 Weapon Lasers]");
            DontDestroyOnLoad(go);
            go.AddComponent<WeaponLaserBootstrap>();
        }

        private void OnEnable()
        {
            GameEvents.CombatantSpawned += AttachToCombatant;
            AttachToAll();
        }

        private void OnDisable()
        {
            GameEvents.CombatantSpawned -= AttachToCombatant;
        }

        private static void AttachToAll()
        {
            var firearms = FindObjectsByType<LR2Firearm>(FindObjectsInactive.Include);
            for (int i = 0; i < firearms.Length; i++) Attach(firearms[i]);
        }

        private static void AttachToCombatant(Combatant combatant)
        {
            if (combatant == null) return;
            var firearms = combatant.GetComponentsInChildren<LR2Firearm>(true);
            for (int i = 0; i < firearms.Length; i++) Attach(firearms[i]);
        }

        private static void Attach(LR2Firearm firearm)
        {
            if (firearm != null && firearm.GetComponent<WeaponLaser>() == null)
                firearm.gameObject.AddComponent<WeaponLaser>();
        }
    }
}
