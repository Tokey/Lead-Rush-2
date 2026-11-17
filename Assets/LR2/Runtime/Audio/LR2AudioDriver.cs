using UnityEngine;

namespace LR2.Audio
{
    /// <summary>Hidden per-scene helper created by LR2Audio: ticks ducking recovery.</summary>
    [AddComponentMenu("")]
    public class LR2AudioDriver : MonoBehaviour
    {
        private void Update() => LR2Audio.TickDuck(Time.unscaledDeltaTime);
    }
}
