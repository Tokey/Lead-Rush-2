using LR2.Audio;
using LR2.Core;
using UnityEngine;

namespace LR2.FX
{
    /// <summary>Physical brass that bounces and tinkles once.</summary>

    /// <summary>Spawns pooled shell casings from the ejection port.</summary>
    public class ShellEjector : MonoBehaviour
    {
        [SerializeField] private GameObject shellPrefab;
        [Tooltip("Local-space ejection velocity (x = right, y = up, z = forward).")]
        [SerializeField] private Vector3 ejectVelocity = new Vector3(2.2f, 1.4f, 0.3f);
        [SerializeField] private float velocityRandomness = 0.35f;
        [SerializeField] private float spin = 18f;
        [SerializeField] private float lifetime = 4f;

        public GameObject ShellPrefab { get => shellPrefab; set => shellPrefab = value; }

        public void Eject(Vector3 inheritVelocity, SoundSet impactSound)
        {
            if (shellPrefab == null) return;
            var go = LR2Pool.Spawn(shellPrefab, transform.position, transform.rotation * Quaternion.Euler(0f, 90f, 0f), lifetime);
            if (go == null) return;
            var casing = go.GetComponent<ShellCasing>();
            if (casing != null) casing.impactSound = impactSound;
            var rb = go.GetComponent<Rigidbody>();
            if (rb == null) return;
            Vector3 v = ejectVelocity + Random.insideUnitSphere * velocityRandomness;
            rb.linearVelocity = transform.TransformDirection(v) + inheritVelocity;
            rb.angularVelocity = Random.insideUnitSphere * spin;
        }
    }
}
