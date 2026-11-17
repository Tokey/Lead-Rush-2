using UnityEngine;

namespace LR2.Audio
{
    /// <summary>Random spatial one-shots around the listener (crows, wind gusts, distant thunder) + water drips indoors.</summary>
    public class AmbientOneShots : MonoBehaviour
    {
        public AudioClip[] outdoorClips;
        public Vector2 outdoorInterval = new Vector2(5f, 14f);
        [Range(0f, 1f)] public float outdoorVolume = 0.55f;
        public AudioClip[] dripClips;
        public Vector3[] dripPoints;
        public Vector2 dripInterval = new Vector2(0.8f, 3f);
        [Range(0f, 1f)] public float dripVolume = 0.5f;

        private float _nextOutdoor, _nextDrip;

        private void Start()
        {
            _nextOutdoor = Time.time + Random.Range(2f, 6f);
            _nextDrip = Time.time + 1f;
        }

        private void Update()
        {
            Vector3 l = LR2Audio.ListenerPosition;
            if (outdoorClips != null && outdoorClips.Length > 0 && Time.time >= _nextOutdoor)
            {
                _nextOutdoor = Time.time + Random.Range(outdoorInterval.x, outdoorInterval.y);
                Vector2 d = Random.insideUnitCircle.normalized * Random.Range(18f, 45f);
                var clip = outdoorClips[Random.Range(0, outdoorClips.Length)];
                LR2Audio.PlayClip(clip, l + new Vector3(d.x, Random.Range(4f, 18f), d.y), outdoorVolume, Random.Range(0.92f, 1.06f), 8f, 140f, 0.85f, AudioCategory.Ambience, 205);
            }
            if (dripClips != null && dripClips.Length > 0 && dripPoints != null && dripPoints.Length > 0 && Time.time >= _nextDrip)
            {
                _nextDrip = Time.time + Random.Range(dripInterval.x, dripInterval.y);
                var p = dripPoints[Random.Range(0, dripPoints.Length)] + new Vector3(Random.Range(-2f, 2f), 0f, Random.Range(-2f, 2f));
                if ((p - l).sqrMagnitude < 18f * 18f)
                    LR2Audio.PlayClip(dripClips[Random.Range(0, dripClips.Length)], p, dripVolume, Random.Range(0.9f, 1.1f), 1f, 14f, 1f, AudioCategory.Ambience, 225);
            }
        }
    }
}
