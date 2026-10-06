# Milestone Status: M0–M12

The status column describes repository evidence. No Unity or phone-dependent milestone is marked verified without running it on that target.

| Stage | Scope | Status |
| --- | --- | --- |
| M0 | Unity/server structure, AR Foundation stack, and architecture notes | Source and project configuration are present; Unity Editor is not installed in the current environment. |
| M1 | AR session, camera, pose tracking, and horizontal floor detection | Scene generator and runtime source are present; not run on a device. |
| M2 | Room anchor, two-point manual alignment, blocks, and 12 cm grid | Implemented in source; requires Unity and device verification. |
| M3 | Collisions, gravity, static floor, and a limit of eight dynamic blocks | Implemented in source; no CPU/GPU profile is available. |
| M4 | Rooms, snapshots, event sequences, and HTTP long-poll synchronization | Relay and client source are present; no two-device measurement is available. |
| M5 | Shared placement through repeated manual alignment | Prototype only; Cloud Anchors and ARWorldMap are outside the MVP. |
| M6 | Color selection, delete, and local JSON save/load | Implemented in source; requires a phone test. |
| M7 | Poll retries, snapshot resynchronization, and room rejoin | Partial: retry and snapshots exist; there is no offline change queue or controlled failure test. |
| M8 | FPS/RTT/tracking HUD and workload limits | Instrumentation is present; no benchmark, LOD, or device profiling. |
| M9 | Limited synchronization of physics motion | Prototype only, not deterministic simulation; client divergence has not been tested. |
| M10 | Repeatable position-error, drift, and tracking-recovery measurements | To be completed on devices using marked measurement points. |
| M11 | README, architecture, relay, and CI workflow | Source documentation is present; demo video and screenshots remain to be captured. |
| M12 | Conclusions, measured KPIs, and next-step plan | To be completed after real test results are collected. |

## Next Work

1. Open the project in Unity 6.3, resolve the pinned packages, generate the starter scene, configure ARCore/ARKit, and fix any editor compilation errors.
2. Build an Android APK and verify camera permission, plane tracking, and anchor behavior after tracking loss.
3. Deploy the relay behind HTTPS and run a shared session on two devices; measure manual-alignment error and round-trip time.
4. Add a shared-anchor provider, durable storage, network tests with latency and packet loss, and automated builds after configuring a Unity license.
5. Measure FPS, CPU, GPU, memory, and battery use. Add only measured results to the README and demo recording.
