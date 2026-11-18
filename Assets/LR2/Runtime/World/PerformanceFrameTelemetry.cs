using System;
using System.IO;
using LR2.Core;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

namespace LR2.World
{
    /// <summary>
    /// Runtime frame telemetry recorder for P3 &amp; B48: tracks frame times, FrameTimingManager CPU/GPU durations,
    /// active resolution, quality preset, and HDRP upscaler scale, distinguishing steady gameplay from warmup/readback stalls.
    /// </summary>
    public sealed class PerformanceFrameTelemetry : MonoBehaviour
    {
        [Serializable]
        public struct Sample
        {
            public float frameMs;
            public float cpuMs;
            public float gpuMs;
            public long totalAllocatedBytes;
        }

        private const int WarmupFrames = 20;

        private static int _pauseDepth;

        [SerializeField, Min(8)] private int capacity = 3600;
        private Sample[] _rawSamples;
        private Sample[] _steadySamples;
        private int _rawCount;
        private int _steadyCount;
        private int _framesSinceReset;
        private int _suppressSteadyFrames;
        private Recorder _gcRecorder;
        private readonly FrameTiming[] _timing = new FrameTiming[1];

        public int Count => _steadyCount > 0 ? _steadyCount : _rawCount;
        public int RawCount => _rawCount;
        public int SteadyCount => _steadyCount;

        public Sample Get(int index) =>
            _steadyCount > 0 ? _steadySamples[index % _steadySamples.Length] : _rawSamples[index % _rawSamples.Length];

        public static void MarkExternalStall(int frames = 2)
        {
            var inst = FindAnyObjectByType<PerformanceFrameTelemetry>();
            if (inst != null) inst._suppressSteadyFrames = Mathf.Max(inst._suppressSteadyFrames, frames);
            else _pauseDepth = Mathf.Max(_pauseDepth, frames);
        }

        public static void PushCapturePause() => _pauseDepth++;
        public static void PopCapturePause() => _pauseDepth = Mathf.Max(0, _pauseDepth - 1);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _pauseDepth = 0;

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
            int cap = Mathf.Max(8, capacity);
            _rawSamples = new Sample[cap];
            _steadySamples = new Sample[cap];
            _gcRecorder = Recorder.Get("GC.Alloc");
            if (_gcRecorder != null) _gcRecorder.enabled = true;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy() => SceneManager.sceneLoaded -= OnSceneLoaded;

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ResetSamples();

        private void LateUpdate()
        {
            float frameMs = Time.unscaledDeltaTime * 1000f;
            float cpuMs = 0f;
            float gpuMs = 0f;
            if (FrameTimingManager.IsFeatureEnabled())
            {
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, _timing) > 0)
                {
                    cpuMs = (float)_timing[0].cpuFrameTime;
                    gpuMs = (float)_timing[0].gpuFrameTime;
                }
            }

            long gc = _gcRecorder != null ? _gcRecorder.sampleBlockCount : 0;
            var sample = new Sample
            {
                frameMs = frameMs,
                cpuMs = cpuMs,
                gpuMs = gpuMs,
                totalAllocatedBytes = gc
            };

            _rawSamples[_rawCount % _rawSamples.Length] = sample;
            _rawCount++;
            _framesSinceReset++;

            bool suppressed = _pauseDepth > 0 || _suppressSteadyFrames > 0;
            if (_suppressSteadyFrames > 0) _suppressSteadyFrames--;
            if (!suppressed && _framesSinceReset > WarmupFrames)
            {
                _steadySamples[_steadyCount % _steadySamples.Length] = sample;
                _steadyCount++;
            }
        }

        public void ResetSamples()
        {
            _rawCount = 0;
            _steadyCount = 0;
            _framesSinceReset = 0;
        }

        public bool TryGetSummary(out int count, out float avgMs, out float medMs, out float p99Ms,
            out float avgCpuMs, out float avgGpuMs, out float p99GpuMs, out float rawP99Ms)
        {
            bool useSteady = _steadyCount >= 8;
            Sample[] buf = useSteady ? _steadySamples : _rawSamples;
            int total = useSteady ? _steadyCount : _rawCount;
            if (total < 8)
            {
                count = 0;
                avgMs = medMs = p99Ms = avgCpuMs = avgGpuMs = p99GpuMs = rawP99Ms = 0f;
                return false;
            }

            count = Mathf.Min(total, buf.Length);
            var frameTimes = new float[count];
            var gpuTimes = new float[count];
            int gpuValid = 0;
            double frameSum = 0.0, cpuSum = 0.0, gpuSum = 0.0;
            int cpuValid = 0;
            for (int i = 0; i < count; i++)
            {
                var s = buf[(total - count + i) % buf.Length];
                frameTimes[i] = s.frameMs;
                frameSum += s.frameMs;
                if (s.cpuMs > 0.01f) { cpuSum += s.cpuMs; cpuValid++; }
                if (s.gpuMs > 0.01f) { gpuTimes[gpuValid++] = s.gpuMs; gpuSum += s.gpuMs; }
            }

            Array.Sort(frameTimes);
            avgMs = (float)(frameSum / count);
            medMs = frameTimes[count / 2];
            p99Ms = frameTimes[Mathf.Clamp(Mathf.CeilToInt(count * 0.99f) - 1, 0, count - 1)];
            avgCpuMs = cpuValid > 0 ? (float)(cpuSum / cpuValid) : 0f;
            avgGpuMs = gpuValid > 0 ? (float)(gpuSum / gpuValid) : 0f;

            if (gpuValid > 0)
            {
                Array.Sort(gpuTimes, 0, gpuValid);
                p99GpuMs = gpuTimes[Mathf.Clamp(Mathf.CeilToInt(gpuValid * 0.99f) - 1, 0, gpuValid - 1)];
            }
            else
            {
                p99GpuMs = 0f;
            }

            int rCount = Mathf.Min(_rawCount, _rawSamples.Length);
            if (rCount >= 8)
            {
                var raw = new float[rCount];
                for (int i = 0; i < rCount; i++) raw[i] = _rawSamples[(_rawCount - rCount + i) % _rawSamples.Length].frameMs;
                Array.Sort(raw);
                rawP99Ms = raw[Mathf.Clamp(Mathf.CeilToInt(rCount * 0.99f) - 1, 0, rCount - 1)];
            }
            else
            {
                rawP99Ms = p99Ms;
            }

            return true;
        }

        public string FlushReport(string sceneName = null, string suffix = null)
        {
            if (!TryGetSummary(out int count, out float avgMs, out float medMs, out float p99Ms,
                out float avgCpuMs, out float avgGpuMs, out float p99GpuMs, out float rawP99Ms))
                return null;

            string activeScene = string.IsNullOrEmpty(sceneName) ? SceneManager.GetActiveScene().name : sceneName;
            string qName = QualitySettings.names != null && QualitySettings.GetQualityLevel() < QualitySettings.names.Length
                ? QualitySettings.names[QualitySettings.GetQualityLevel()]
                : "Default";
            string upscaler = UpscalerBackend.ActiveUpscalerLabel();
            float scalePct = UpscalerBackend.CurrentScalePercent;

            string dir = null;
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], "-lr2P3Dir", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(args[i + 1]))
                    dir = Path.GetFullPath(args[i + 1]);
            if (string.IsNullOrEmpty(dir))
                dir = Path.Combine(Application.dataPath, "..", "Logs", "LR2_P3");
            Directory.CreateDirectory(dir);
            string file = string.IsNullOrEmpty(suffix)
                ? $"{activeScene}_frame_timing.txt"
                : $"{activeScene}_{suffix}_frame_timing.txt";
            string path = Path.Combine(dir, file);

            File.WriteAllText(path,
                $"scene={activeScene}\n" +
                $"resolution={Screen.width}x{Screen.height}\n" +
                $"quality={qName}\n" +
                $"upscaler={upscaler}\n" +
                $"renderScalePercent={scalePct:0.0}\n" +
                $"samples={count}\n" +
                $"avgFrameMs={avgMs:0.00}\n" +
                $"medianFrameMs={medMs:0.00}\n" +
                $"p99FrameMs={p99Ms:0.00}\n" +
                $"avgCpuMs={avgCpuMs:0.00}\n" +
                $"avgGpuMs={avgGpuMs:0.00}\n" +
                $"p99GpuMs={p99GpuMs:0.00}\n" +
                $"rawSamples={Mathf.Min(_rawCount, _rawSamples.Length)}\n" +
                $"rawP99FrameMs={rawP99Ms:0.00}\n");
            return path;
        }

        private void OnApplicationQuit() => FlushReport();
    }
}
