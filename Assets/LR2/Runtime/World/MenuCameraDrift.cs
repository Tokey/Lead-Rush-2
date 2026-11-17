using UnityEngine;

namespace LR2.World
{
    /// <summary>Slow handheld drift for the main-menu camera.</summary>
    public class MenuCameraDrift : MonoBehaviour
    {
        [SerializeField] private Vector3 positionAmplitude = new Vector3(0.06f, 0.04f, 0.03f);
        [SerializeField] private Vector3 rotationAmplitude = new Vector3(0.6f, 0.9f, 0.3f);
        [SerializeField] private float speed = 0.18f;
        private Vector3 _pos;
        private Quaternion _rot;

        private void Awake()
        {
            _pos = transform.localPosition;
            _rot = transform.localRotation;
        }

        private void LateUpdate()
        {
            float t = Time.unscaledTime * speed;
            Vector3 p = new Vector3(Mathf.PerlinNoise(t, 1.3f) - 0.5f, Mathf.PerlinNoise(2.7f, t) - 0.5f, Mathf.PerlinNoise(t, 5.1f) - 0.5f) * 2f;
            Vector3 r = new Vector3(Mathf.PerlinNoise(t * 0.8f, 9.1f) - 0.5f, Mathf.PerlinNoise(4.4f, t * 0.8f) - 0.5f, Mathf.PerlinNoise(t, 7.7f) - 0.5f) * 2f;
            transform.localPosition = _pos + Vector3.Scale(p, positionAmplitude);
            transform.localRotation = _rot * Quaternion.Euler(Vector3.Scale(r, rotationAmplitude));
        }
    }
}
