using LR2.Audio;
using UnityEngine;

namespace LR2.FX
{
    [RequireComponent(typeof(Rigidbody))]
    public class ShellCasing : MonoBehaviour
    {
        public SoundSet impactSound;
        private bool _played;
        private Rigidbody _rb;

        public Rigidbody Body => _rb != null ? _rb : (_rb = GetComponent<Rigidbody>());

        private void OnEnable() => _played = false;

        private void OnCollisionEnter(Collision collision)
        {
            if (_played || impactSound == null || !impactSound.IsValid) return;
            _played = true;
            LR2Audio.PlayAt(impactSound, transform.position, Mathf.Clamp01(collision.relativeVelocity.magnitude / 4f));
        }
    }
}
