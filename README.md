# Shared AR Workshop

Shared AR Workshop is a Unity mobile AR prototype for placing and editing virtual blocks in a room with another participant. AR Foundation handles plane detection and local tracking; a small Node.js relay broadcasts room state over HTTP long polling.

> **Status: prototype; Unity builds and device behavior are not verified yet.** The repository contains the application and relay source, but no generated scene, APK, or iOS build. The Unity Editor is not installed in the current verification environment. Server behavior can be checked independently from the AR client.

## What is implemented in source

- AR Foundation scene bootstrap for horizontal-plane detection and camera tracking.
- Manual two-point alignment for approximate shared room coordinates.
- Grid-snapped block placement, color selection, rotation, deletion, and a limited local-physics demo.
- Local JSON save/load.
- Room join, state snapshots, long-poll event delivery, and block upsert/delete endpoints.
- Request size, room/object/member limits, coordinate validation, per-member operation throttling, and bounded pending long polls on the relay.

These are code paths, not a claim that the complete AR flow has been built or verified on a phone. In particular, shared alignment accuracy, reconnect behavior, and two-device synchronization still need a hardware demonstration.

## Requirements

- Unity **6000.3.0f1**, with Android Build Support, Android SDK/NDK, and OpenJDK for Android builds.
- An ARCore-supported Android phone. iOS builds require macOS, Xcode, and an ARKit-capable device.
- Node.js 20 or later for the room relay.
- Two compatible AR devices in the same well-lit room to demonstrate shared placement.

The Unity project manifest pins the AR Foundation, ARCore, ARKit, XR Plug-in Management, Input System, and uGUI packages. On first open, allow Unity Package Manager to resolve them. The relay's `server/package-lock.json` is committed. Unity's generated `Packages/packages-lock.json` should be committed after a successful Package Manager resolution so subsequent clones use the same transitive package graph.

## Run the room relay

From the repository root:

```powershell
node server/server.js --host 127.0.0.1 --port 8787
```

The relay listens on loopback by default and keeps rooms in process memory. Restarting it clears all rooms. For a phone demo, deploy the relay behind HTTPS and enter its HTTPS URL in the app. Do not expose this prototype directly to the public internet: room codes act as the only invitation, there are no user accounts or authorization roles, and state is not persisted.

For local LAN-only testing, bind to the machine's LAN interface and restrict access with the host firewall. A mobile device must be able to reach the host; do not assume `localhost` on the phone refers to the development computer.

## Open and configure the Unity project

1. Open `UnityProject` with Unity 6000.3.0f1 and wait for Package Manager resolution to finish.
2. Select **Tools → Shared Workshop → Create starter scene**. The generated scene is saved at `Assets/Scenes/SharedWorkshop.unity` and added to Build Settings.
3. Open **Project Settings → XR Plug-in Management**. Initialize the platform settings if Unity asks, then select **Tools → Shared Workshop → Configure XR providers** to assign ARCore for Android and ARKit for iOS.
4. Select Android or iOS in Build Profiles, build the app, and run it on an AR-capable device.
5. Enter the HTTPS relay URL, a display name, and a room code. One participant can generate an eight-character code; the other participant enters the same code.
6. Each participant selects **Align**, then points at the same physical origin and a second point in the same direction. This is manual alignment, not cloud-anchor or shared-world-map localization.
7. Place blocks and verify that both devices show the same objects. Record the devices, Unity version, network, lighting, frame rate, tracking loss, and observed sync delay for the portfolio demo.

The relay receives room codes, display names, block identifiers, transforms, colors, and physics flags. It does not receive camera frames or room scans.

## Relay API

| Method | Endpoint | Purpose |
| --- | --- | --- |
| `GET` | `/health` | Process health and in-memory room counts |
| `POST` | `/api/rooms/:code/join` | Join a room and receive its current object snapshot |
| `GET` | `/api/rooms/:code/events?clientId=:id&since=:seq` | Long-poll for changes after a sequence cursor |
| `POST` | `/api/rooms/:code/objects/upsert` | Create or update a block |
| `POST` | `/api/rooms/:code/objects/delete` | Delete a block |

The server validates room codes, room membership tokens, payload sizes, object transforms, object counts, and update rate. The `clientId` returned at join is a bearer token, not account authentication. Anyone who obtains it can act as that room member until the room expires or the server restarts.

## Verification and known limitations

- `npm run check` checks JavaScript syntax. `npm test` starts the relay on loopback and covers room membership, input limits, event delivery, and duplicate long-poll rejection.
- The manual Android build workflow requires Unity licensing secrets (`UNITY_LICENSE`, `UNITY_EMAIL`, and `UNITY_PASSWORD`) in GitHub Actions. It has not been run for this repository.
- No APK/IPA, device recording, performance result, or two-device test result is included.
- The relay is in-memory and unauthenticated. It is a portfolio prototype, not a production collaboration backend.
- Dynamic block motion is a small demonstration; remote transforms are relayed as updates rather than using a dedicated authoritative physics simulation.
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
