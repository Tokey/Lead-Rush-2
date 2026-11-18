using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

namespace LR2.World
{
    /// <summary>Small runtime recorder for P3; consumers can sample it during visible map runs.</summary>
    public sealed class PerformanceFrameTelemetry : MonoBehaviour
    {
        [Serializable]
        public struct Sample { public float frameMs; public long totalAllocatedBytes; }

        [SerializeField, Min(8)] private int capacity = 3600;
        private Sample[] _samples;
        private int _count;
        private Recorder _gcRecorder;

        public int Count => _count;
        public Sample Get(int index) => _samples[index];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindAnyObjectByType<PerformanceFrameTelemetry>() != null) return;
            var go = new GameObject("[LR2 Performance Telemetry]");
            DontDestroyOnLoad(go);
            go.AddComponent<PerformanceFrameTelemetry>();
        }

        private void Awake()
        {
            _samples = new Sample[Mathf.Max(8, capacity)];
            _gcRecorder = Recorder.Get("GC.Alloc");
            if (_gcRecorder != null) _gcRecorder.enabled = true;
        }

        private void LateUpdate()
        {
            _samples[_count % _samples.Length] = new Sample
            {
                frameMs = Time.unscaledDeltaTime * 1000f,
                totalAllocatedBytes = _gcRecorder != null ? _gcRecorder.sampleBlockCount : 0
            };
            _count++;
        }

        public void ResetSamples() => _count = 0;

        private void OnApplicationQuit()
        {
            if (_count < 8) return;
            int sampleCount = Mathf.Min(_count, _samples.Length);
            var frameTimes = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++) frameTimes[i] = _samples[(_count - sampleCount + i) % _samples.Length].frameMs;
            Array.Sort(frameTimes);
            float average = frameTimes.Average();
            float median = frameTimes[frameTimes.Length / 2];
            float p99 = frameTimes[Mathf.Clamp(Mathf.CeilToInt(frameTimes.Length * 0.99f) - 1, 0, frameTimes.Length - 1)];
            string dir = Path.Combine(Application.dataPath, "..", "Logs", "LR2_P3");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, SceneManager.GetActiveScene().name + "_frame_timing.txt");
            File.WriteAllText(path, $"scene={SceneManager.GetActiveScene().name}\nsamples={sampleCount}\navgFrameMs={average:0.00}\nmedianFrameMs={median:0.00}\np99FrameMs={p99:0.00}\n");
        }
    }
}
