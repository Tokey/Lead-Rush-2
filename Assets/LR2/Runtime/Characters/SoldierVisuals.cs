using LR2.Combat;
using UnityEngine;

namespace LR2.Characters
{
    /// <summary>Friendly chevron above teammates (TDM readability). Hidden for the local player and enemies.</summary>
    public class SoldierVisuals : MonoBehaviour
    {
        [SerializeField] private Material friendlyMarkerMaterial;
        [SerializeField] private float markerHeight = 2.15f;
        [SerializeField] private float markerSize = 0.22f;
        [SerializeField] private float maxMarkerDistance = 60f;

        private Combatant _combatant;
        private Transform _marker;
        private Renderer _markerRenderer;

        [Header("Gear / uniforms")]
        [Tooltip("Uniform for the local player's side (also what you see on your own arms).")]
        [SerializeField] private Material friendlyUniform;
        [Tooltip("Uniform for the opposing side.")]
        [SerializeField] private Material enemyUniform;
        [Tooltip("Full face mask (balaclava + goggles look): no visible face on anyone.")]
        [SerializeField] private Material faceMask;
        private bool _uniformApplied;

        public Material FriendlyMarkerMaterial { get => friendlyMarkerMaterial; set => friendlyMarkerMaterial = value; }
        public void SetGear(Material friendly, Material enemy, Material mask) { friendlyUniform = friendly; enemyUniform = enemy; faceMask = mask; }

        private void Awake() => _combatant = GetComponent<Combatant>();

        private void Start()
        {
            // Apply final faction uniform immediately on spawn frame so no default model texture flashes on frame 0
            if (!_uniformApplied)
            {
                var lp = Combatant.FindLocalPlayer();
                if (_combatant != null && (_combatant.IsLocalPlayer || lp != null))
                    ApplyUniform(lp);
            }
        }

        /// <summary>Re-evaluates which uniform this soldier wears (call after team changes).</summary>
        public void RefreshUniform() => _uniformApplied = false;

        /// <summary>Applies baked uniform materials immediately (used both in prefab build and pre-reveal warmup).</summary>
        public void ApplyMaterialsNow(bool friendly)
        {
            var uniform = friendly ? friendlyUniform : enemyUniform;
            foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    string n = mats[i].name.ToLowerInvariant();
                    if (n.Contains("head") && faceMask != null) { mats[i] = faceMask; changed = true; }
                    else if ((n.Contains("body") || n.Contains("uniform")) && uniform != null) { mats[i] = uniform; changed = true; }
                }
                if (changed) r.sharedMaterials = mats;
            }
            _uniformApplied = true;
        }

        public static void ApplyAllNow()
        {
            var lp = Combatant.FindLocalPlayer();
            foreach (var sv in FindObjectsByType<SoldierVisuals>())
            {
                if (sv != null) sv.ApplyUniform(lp);
            }
        }

        private void ApplyUniform(Combatant local)
        {
            bool friendly = _combatant != null && (_combatant.IsLocalPlayer || (local != null && !TeamUtil.AreHostile(local, _combatant)));
            ApplyMaterialsNow(friendly);
        }

        private void LateUpdate()
        {
            if (!_uniformApplied)
            {
                var lp = Combatant.FindLocalPlayer();
                if (lp != null || Time.timeSinceLevelLoad > 1f) ApplyUniform(lp);
            }
            var local = Combatant.FindLocalPlayer();
            bool show = local != null && _combatant != null && local != _combatant && _combatant.IsAlive
                        && !TeamUtil.AreHostile(local, _combatant) && friendlyMarkerMaterial != null;
            if (show && Vector3.Distance(local.EyePosition, transform.position) > maxMarkerDistance) show = false;

            if (show && _marker == null) CreateMarker();
            if (_markerRenderer != null) _markerRenderer.enabled = show;
            if (!show || _marker == null) return;

            _marker.position = transform.position + Vector3.up * markerHeight;
            var cam = Camera.main;
            if (cam != null)
            {
                _marker.rotation = Quaternion.LookRotation(_marker.position - cam.transform.position);
                float d = Vector3.Distance(cam.transform.position, _marker.position);
                _marker.localScale = Vector3.one * markerSize * Mathf.Clamp(d / 12f, 0.7f, 3f);
            }
        }

        private void CreateMarker()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "FriendlyMarker";
            Destroy(go.GetComponent<Collider>());
            go.layer = Core.LR2Layers.IgnoreRaycast;
            _marker = go.transform;
            _marker.SetParent(transform, true);
            _markerRenderer = go.GetComponent<Renderer>();
            _markerRenderer.sharedMaterial = friendlyMarkerMaterial;
            _markerRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _markerRenderer.receiveShadows = false;
        }
    }
}
