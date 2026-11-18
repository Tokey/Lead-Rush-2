using UnityEngine;

namespace LR2.Audio
{
    /// <summary>Shared, allocation-free policy for selecting and limiting environmental weapon tails.</summary>
    public sealed class WeaponSoundTailRouter : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] private float indoorGain = 1f;
        [SerializeField, Range(0f, 1f)] private float outdoorGain = 0.65f;
        [SerializeField, Min(1)] private int maxOverlappingTails = 4;
        [SerializeField, Min(0.01f)] private float minimumTailGap = 0.045f;
        private float _lastTailTime = -999f;
        private int _activeTails;

        public float Gain(bool indoors) => indoors ? indoorGain : outdoorGain;
        public bool CanPlay(float now)
        {
            if (_activeTails >= maxOverlappingTails || now - _lastTailTime < minimumTailGap) return false;
            _lastTailTime = now;
            _activeTails++;
            return true;
        }

        public void NotifyTailEnded()
        {
            if (_activeTails > 0) _activeTails--;
        }
    }
}
