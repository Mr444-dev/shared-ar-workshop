# Shared AR Workshop
![Unity](https://img.shields.io/badge/Unity-000000?logo=unity&logoColor=white) ![AR Foundation](https://img.shields.io/badge/AR%20Foundation-2D2D2D) ![MIT](https://img.shields.io/badge/License-MIT-blue.svg)

Shared AR Workshop is a Unity mobile AR prototype for placing and editing virtual blocks in a room with another participant. AR Foundation handles plane detection and local tracking; a small Node.js relay broadcasts room state over HTTP long polling.

> **Status: prototype; Unity builds and device behavior are not verified yet.** The repository contains the application and relay source, but no generated scene, APK, or iOS build. The Unity Editor is not installed in the current verification environment. Server behavior can be checked independently from the AR client.

[Run the relay](#run-the-room-relay) · [Verification](#verification-and-known-limitations) · [License](#license)
## What is implemented in source

- AR Foundation scene bootstrap for horizontal-plane detection and camera tracking.
- Manual two-point alignment for approximate shared room coordinates.
- Grid-snapped block placement, color selection, rotation, deletion, and a limited local-physics demo.
- Local JSON save/load.
- Room join, state snapshots, long-poll event delivery, individual edits, and atomic batch/replace endpoints.
- A cryptographic bearer token is separate from the public participant ID; the client sends it in the `Authorization` header, never in a URL or event.
- The client serializes and coalesces pending edits, validates server snapshots/events, prevents stale session callbacks, and bounds its pre-alignment object cache.
- The client prevents unsent edits from crossing into a different room/server; discarding queued edits requires an in-app confirmation. The queue is in memory only and is not durable offline editing.
- The relay applies room, object, member, event-history, request-size, join-rate, and pending-poll limits; inactive members expire and unauthenticated requests do not keep rooms alive.
- The HTTP server also bounds header size/count, request/header receipt time, connections, and requests per keep-alive socket.

These are code paths, not a claim that the complete AR flow has been built or verified on a phone. In particular, shared alignment accuracy, reconnect behavior, and two-device synchronization still need a hardware demonstration.

## Requirements

- Unity **6000.3.0f1**, with Android Build Support, Android SDK/NDK, and OpenJDK for Android builds.
- An ARCore-supported Android phone. iOS builds require macOS, Xcode, and an ARKit-capable device.
- Node.js 22 or later for local relay development; CI and the container use Node.js 24 LTS.
- Two compatible AR devices in the same well-lit room to demonstrate shared placement.

The Unity project manifest pins the AR Foundation, ARCore, ARKit, XR Plug-in Management, Input System, and uGUI packages. On first open, allow Unity Package Manager to resolve them. The relay's `server/package-lock.json` is committed. Unity's generated `Packages/packages-lock.json` should be committed after a successful Package Manager resolution so subsequent clones use the same transitive package graph.

## Run the room relay

From the repository root:

```powershell
node server/server.js --host 127.0.0.1 --port 8787
```

The relay listens on loopback by default and keeps rooms in process memory. Restarting it clears all rooms. Keep this prototype at one server instance: rooms, membership tokens, and rate-limit buckets are not shared across instances. The included Render blueprint uses Cloudflare's `CF-Connecting-IP` header for join throttling; other deployments should follow the proxy trust guidance in `server/README.md`. For remote use, terminate TLS at a trusted HTTPS proxy and enter its HTTPS URL in the app. Room codes are invitations, not user accounts or authorization roles; every room member can edit, delete, or replace every block in the room. The client confirms full-room replacement and binds version 2 saves to both the normalized relay URL and room code, but these checks do not replace server-side roles. Rate and capacity limits reduce abuse but do not make this an open public production service.

For local LAN-only testing, bind to the machine's LAN interface and restrict access with the host firewall. The app allows HTTP only for loopback and private IPs, which is suitable for a controlled development LAN but does not protect tokens from other devices on that network. A mobile device must be able to reach the host; do not assume `localhost` on the phone refers to the development computer.

## Open and configure the Unity project

1. Open `UnityProject` with Unity 6000.3.0f1 and wait for Package Manager resolution to finish.
2. Select **Tools → Shared Workshop → Create starter scene**. The generated scene is saved at `Assets/Scenes/SharedWorkshop.unity` and placed first in Build Settings while preserving other scenes. Existing generated scenes require explicit replacement confirmation.
3. In **Project Settings → Player → Other Settings → Configuration**, set **Active Input Handling** to **Input System Package (New)** or **Both**, then restart Unity if prompted. The AR pose driver and touch controls use the new Input System. Select **Tools → Shared Workshop → Configure XR providers** to create or load XR settings and assign ARCore for Android and ARKit for iOS. Confirm both loaders in **Project Settings → XR Plug-in Management** before building.
4. Select Android or iOS in Build Profiles, build the app, and run it on an AR-capable device.
5. Enter the relay URL and display name. Generate a 12-character room code and share it only with the participant you invite.
6. Each participant selects **Align**, then points at the same physical origin and a second point in the same direction. This is manual alignment, not cloud-anchor or shared-world-map localization.
7. Place blocks and verify that both devices show the same objects. Record the devices, Unity version, network, lighting, frame rate, tracking loss, and observed sync delay for the portfolio demo.

The relay receives room codes, display names, block identifiers, transforms, colors, and physics flags. It does not receive camera frames or room scans.

## Relay API

| Method | Endpoint | Purpose |
| --- | --- | --- |
| `GET` | `/health` | Process health status only |
| `POST` | `/api/rooms/:code/join` | Join a room and receive its current object snapshot |
| `GET` | `/api/rooms/:code/events?since=:seq` | Long-poll with a bearer token in the `Authorization` header |
| `POST` | `/api/rooms/:code/objects/upsert` | Create or update a block |
| `POST` | `/api/rooms/:code/objects/delete` | Delete a block |
| `POST` | `/api/rooms/:code/objects/upsert-batch` | Atomically publish alignment updates |
| `POST` | `/api/rooms/:code/objects/replace` | Atomically replace the room with a loaded project |

The join response contains a random `accessToken` and a separate public `clientId`. The relay authorizes edits using the bearer token and writes the participant identity into events itself. Anyone who obtains a bearer token can act as that member until its 15-minute idle expiry, room expiry, or server restart.

## Verification and known limitations

- `npm run check` checks JavaScript syntax. `npm test` starts the relay on loopback and covers authorization, caller-identity spoofing, batch replacement/resync, object validation, join throttling, and long-poll cleanup.
- Last recorded relay verification in this workspace used Node.js `v24.12.0`: `npm run check` passed and `npm test` passed all 11 tests. This verifies the Node relay only, not the Unity client or AR behavior.
- The manual Android build workflow uses `SharedWorkshop.EditorTools.PortfolioBuild.BuildAndroid`, creates the starter scene only when it is missing, requires it to be the first build scene, validates its AR components and all enabled scenes, configures XR providers, and writes to GameCI's complete APK output path. It requires Unity licensing secrets (`UNITY_LICENSE`, `UNITY_EMAIL`, and `UNITY_PASSWORD`) in GitHub Actions. The workflow has not been run for this repository.
- GitHub Actions are pinned to reviewed commit SHAs and `.github/dependabot.yml` checks for updates. Relay CI checks JavaScript, API behavior, dependency advisories, Unity package pins, and static wiring of the Android build entry point; it does not compile C#.
- Unity-side changes have not been compiled or exercised in the Editor on this checkout. No APK/IPA, device recording, performance result, or two-device result is included.
- Version 2 local saves are written through a temporary file with a recovery backup, validated for unique object IDs, and bound to the relay URL and room code before a shared replacement. Legacy version 1 saves can only be loaded locally while disconnected because their relay destination was not recorded.
- The relay is in-memory and has no person-level authentication or room roles. It is a portfolio prototype, not a production collaboration backend.
- Dynamic block motion is a small demonstration; remote transforms are relayed as updates rather than using a dedicated authoritative physics simulation.
- The HUD's health-check duration measures the `/health` request only; it is not a block synchronization or end-to-end latency measurement.
- Measured goals such as 30 FPS or sub-250 ms synchronization must not be stated as achieved until recorded on named devices and a specified network.

## Repository layout

```text
UnityProject/Assets/SharedWorkshop/Runtime/  # AR interaction, block model, relay client, HUD
UnityProject/Assets/Editor/WorkshopSetup/    # starter scene and XR provider setup
UnityProject/Packages/manifest.json          # pinned direct Unity dependencies
server/server.js                             # dependency-free Node.js room relay
docs/architecture.md                         # design and coordinate-system notes
docs/roadmap.md                              # remaining milestones
```

## License

MIT. See [LICENSE](LICENSE). Third-party dependencies and assets remain under their respective licenses.
