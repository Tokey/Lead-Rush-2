# P3 visible performance pass - 2026-11-18

The pass was run in a visible 1120x630 Unity window, sequentially on
TestRange, Saltworks, Foundry and Deadsector, with the QA bot and the new
`PerformanceFrameTelemetry` bootstrap active. No Unity processes overlapped.

| Map | Capture | Average frame ms | Median frame ms | 99th percentile | Result |
|---|---|---:|---:|---:|---|
| TestRange | QA completed; telemetry file was not flushed | not captured | not captured | not captured | INCOMPLETE |
| Saltworks | QA completed; telemetry file was not flushed | not captured | not captured | not captured | INCOMPLETE |
| Foundry | 1,314 samples | 37.45 | 10.16 | 73.16 | FAIL target |
| Deadsector | QA completed; telemetry file was not flushed | not captured | not captured | not captured | INCOMPLETE |

Foundry exceeds the provisional 33.3 ms sustained and 50 ms p99 targets.
The median/p99 spread indicates intermittent spikes rather than a uniformly
slow frame. The first follow-up should capture CPU/GPU timings with Unity's
FrameTiming API or Profiler window and isolate HDRP weather/volume, shadows,
decals, and animated Kinemation rigs in that order.

This is deliberately marked incomplete: the visible run is real, but missing
flushes are not treated as passing evidence. The telemetry component now writes
per-map summaries on application quit; rerun the three incomplete maps after
the next harness refresh.
