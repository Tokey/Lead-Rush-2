using LR2.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace LR2.World
{
    /// <summary>Blends the bodycam lens Volume (distortion, grain, aberration, grade) by the player's setting.</summary>
    [RequireComponent(typeof(Volume))]
    public class BodycamPostFX : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] private float weightWhenOn = 1f;
        private Volume _volume;

        private void Awake()
        {
            _volume = GetComponent<Volume>();
            Apply();
            LR2Settings.Changed += Apply;
        }

        private void OnDestroy() => LR2Settings.Changed -= Apply;

        private void Apply()
        {
            if (_volume != null) _volume.weight = LR2Settings.BodycamLook ? weightWhenOn : 0f;
        }
    }
}
