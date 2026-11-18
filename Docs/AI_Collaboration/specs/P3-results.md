# P3 + B48 Visible Performance Pass — 2026-11-18

Run sequentially in visible `1120x630` Unity windows on reference workstation (`NVIDIA GeForce RTX 3080`, `Direct3D 12 [level 12.2]`, `1920x1080`, `Performant` quality, HDRP `17.5.0`), using `PerformanceFrameTelemetry` (`FrameTimingManager` CPU+GPU timings + active `UpscalerBackend` scale readout, separating steady gameplay frames from synchronous QA screenshot readbacks). No Unity processes overlapped.

## 1. Post-Optimization Four-Map Steady Frame Times

| Map | Upscaler Mode | Scale | Samples | Avg (ms) | Median (ms) | p99 (ms) | Avg CPU (ms) | Avg GPU (ms) | p99 GPU (ms) | Target (`med < 33.3 ms`, `p99 < 50 ms`) |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---|
| `LR2_TestRange` | `Native (100%)` | 100.0% | 912 | 6.79 | 6.33 | 17.25 | 6.96 | 2.59 | 3.46 | **PASS** (`0` QA failures) |
| `LR2_Deadsector` | `Native (100%)` | 100.0% | 793 | 7.74 | 7.19 | 19.05 | 7.98 | 2.85 | 4.00 | **PASS** (`p99` `89.54 -> 19.05 ms`, `0` QA failures) |
| `LR2_Deadsector` | `HDRP STP` | 85.0% | 817 | 7.47 | 7.01 | 16.43 | 7.65 | 2.81 | 4.15 | **PASS** (`-13.8%` p99 vs Native, `0` QA failures) |
| `LR2_Foundry` | `Native (100%)` | 100.0% | 765 | 7.81 | 7.00 | 18.16 | 8.08 | 3.05 | 15.00 | **PASS** (`p99` `75.17 -> 18.16 ms`, `0` QA failures) |
| `LR2_Foundry` | `HDRP STP` | 85.0% | 866 | 7.07 | 6.73 | 15.71 | 7.27 | 2.72 | 3.49 | **PASS** (`-76.7%` p99 GPU, `0` QA failures) |
| `LR2_Saltworks` | `Native (100%)` | 100.0% | 2015 | 9.60 | 8.96 | 17.78 | 9.76 | 3.24 | 4.00 | **PASS** (`med` `13.26 -> 8.96 ms`, `0` QA failures) |

## 2. `LR2_Saltworks` Multi-Upscaler Comparison (`B48`)

| Mode | Reported Scale | Samples | Avg (ms) | Median (ms) | p99 (ms) | Avg CPU (ms) | Avg GPU (ms) | p99 GPU (ms) | Status |
|---|---|---:|---:|---:|---:|---:|---:|---:|---|
| `NATIVE (100%)` | 100.0% | 2015 | 9.60 | 8.96 | 17.78 | 9.76 | 3.24 | 4.00 | Full-res TAA (`0` QA failures) |
| `HDRP STP` | 85.0% | 1994 | 9.46 | 9.23 | 16.78 | 9.64 | 3.11 | 3.65 | HDRP Spatial-Temporal Post-Processing (`0` failures) |
| `HDRP TAAU` | 85.0% | 2061 | 9.20 | **8.53** | 18.52 | 9.38 | **2.97** | 3.96 | Lowest median & avg GPU ms (`0` failures) |
| `HDRP FSR 1.0` | 85.0% | 2083 | **9.07** | 8.80 | **15.89** | **9.20** | 3.27 | 4.21 | `EdgeAdaptiveScalingUpres`; lowest p99 (`0` failures) |
| `NVIDIA DLSS` | 70.0% | 1901 | 9.77 | 9.36 | 20.23 | 10.21 | 3.63 | 4.46 | Live `DLSSDetected=True` hardware execution (`0` failures) |
| `AMD FSR 2` | `100.0% (Fallback)` | — | — | — | — | — | — | — | `FSR2Detected=False` reported honestly in Settings; Native 100% fallback |

Full architectural breakdown, root causes, and optimization details: [`P3_Report.md`](P3_Report.md).
