using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace SharedWorkshop.Runtime
{
    [DisallowMultipleComponent]
    public sealed class SharedWorkshopController : MonoBehaviour
    {
        public const float BlockSize = 0.12f;
        public const int MaxBlocks = 200;
        private const int MaxDynamicBlocks = 8;

        private static readonly Color[] Palette =
        {
            new Color(0.10f, 0.72f, 0.68f),
            new Color(0.25f, 0.48f, 0.96f),
            new Color(0.98f, 0.55f, 0.22f),
            new Color(0.93f, 0.30f, 0.43f),
            new Color(0.56f, 0.37f, 0.87f),
            new Color(0.95f, 0.78f, 0.24f)
        };

        private readonly List<ARRaycastHit> _raycastHits = new List<ARRaycastHit>(8);
        private readonly Dictionary<string, WorkshopBlock> _blocks = new Dictionary<string, WorkshopBlock>();
        private WorkshopObjectState[] _pendingSnapshot = Array.Empty<WorkshopObjectState>();
        private ARRaycastManager _raycastManager;
        private ARPlaneManager _planeManager;
        private ARAnchor _roomAnchor;
        private WorkshopNetworkClient _network;
        private WorkshopHud _hud;
        private Vector3 _alignmentOrigin;
        private Vector3 _alignmentUp = Vector3.up;
        private int _alignmentStep;
        private int _selectedColor;
        private bool _deleteMode;
        private bool _rotateMode;
        private float _fps = 60f;
        private float _trackingLossSeconds;
        private float _nextDynamicSync;
        private int _dynamicCursor;
        private string _status = "Point the camera at a well-lit floor.";

        public bool IsAligned { get { return _roomAnchor != null; } }
        public bool IsDeleteMode { get { return _deleteMode; } }
        public bool IsRotateMode { get { return _rotateMode; } }
        public int BlockCount { get { return _blocks.Count; } }
        public string Status { get { return _status; } }
        public static Color ColorAt(int index) { return Palette[Mathf.Clamp(index, 0, Palette.Length - 1)]; }
        public static int PaletteSize { get { return Palette.Length; } }

        private void Start()
        {
            _raycastManager = FindFirstObjectByType<ARRaycastManager>();
            _planeManager = FindFirstObjectByType<ARPlaneManager>();
            _network = GetComponent<WorkshopNetworkClient>();
            if (_network == null) _network = gameObject.AddComponent<WorkshopNetworkClient>();
            _network.Joined += OnJoined;
            _network.EventReceived += OnRemoteEvent;
            _network.SnapshotRequired += OnSnapshotRequired;
            _network.StatusChanged += SetStatus;

            _hud = GetComponent<WorkshopHud>();
            if (_hud == null) _hud = gameObject.AddComponent<WorkshopHud>();
            _hud.Initialize(this, _network);

            if (_raycastManager == null || _planeManager == null)
                SetStatus("Scena AR nie jest skonfigurowana. Uruchom Tools/Shared Workshop/Create starter scene.");
        }

        private void Update()
        {
            var delta = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
            _fps = Mathf.Lerp(_fps, 1f / delta, 0.08f);
            if (ARSession.state != ARSessionState.SessionTracking) _trackingLossSeconds += delta;

            ReadPointer();
            if (Time.unscaledTime >= _nextDynamicSync)
            {
                _nextDynamicSync = Time.unscaledTime + 0.2f;
                SyncMovingBlocks();
            }
        }

        private void ReadPointer()
        {
            var touchscreen = Touchscreen.current;
            if (touchscreen != null)
            {
                foreach (var touch in touchscreen.touches)
                {
                    if (!touch.press.wasPressedThisFrame) continue;
                    var pointerId = touch.touchId.ReadValue();
                    if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(pointerId)) continue;
                    HandleScreenTap(touch.position.ReadValue());
                }
            }

#if UNITY_EDITOR
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
                HandleScreenTap(Mouse.current.position.ReadValue());
            }
#endif
        }

        private void HandleScreenTap(Vector2 screenPosition)
        {
            if (_raycastManager == null || _planeManager == null || Camera.main == null) return;
            if (!TryGetFloorHit(screenPosition, out var hit, out var plane))
            {
                if (_alignmentStep > 0) SetStatus("No floor detected. Move the camera until an AR plane is visible.");
                return;
            }

            if (_alignmentStep == 1)
            {
                _alignmentOrigin = hit.pose.position;
                _alignmentUp = plane.transform.up.normalized;
                _alignmentStep = 2;
                SetStatus("Origin set. Point to a second point in the chosen direction at least 25 cm away.");
                return;
            }

            if (_alignmentStep == 2)
            {
                var axis = Vector3.ProjectOnPlane(hit.pose.position - _alignmentOrigin, _alignmentUp);
                if (axis.magnitude < 0.25f)
                {
                    SetStatus("The second point must be at least 25 cm from the origin.");
                    return;
                }
                CompleteAlignment(axis.normalized);
                return;
            }

            if (!IsAligned)
            {
                SetStatus("Join a room and align the shared coordinate system first.");
                return;
            }

            if (_deleteMode) TryDeleteBlock(screenPosition);
            else if (_rotateMode) TryRotateBlock(screenPosition);
            else PlaceBlock(hit.pose.position);
        }

        private bool TryGetFloorHit(Vector2 screenPosition, out ARRaycastHit result, out ARPlane plane)
        {
            _raycastHits.Clear();
            if (_raycastManager.Raycast(screenPosition, _raycastHits, TrackableType.PlaneWithinPolygon))
            {
                foreach (var candidate in _raycastHits)
                {
                    var foundPlane = candidate.trackable as ARPlane;
                    if (foundPlane == null || foundPlane.alignment != PlaneAlignment.HorizontalUp || foundPlane.trackingState != TrackingState.Tracking) continue;
                    result = candidate;
                    plane = foundPlane;
                    return true;
                }
            }
            result = default;
            plane = null;
            return false;
        }

        public void JoinRoom(string serverUrl, string roomCode, string displayName)
        {
            _network.Join(serverUrl, roomCode, displayName);
        }

        public void BeginAlignment()
        {
            if (_network == null || !_network.IsConnected)
            {
                SetStatus("Join a room before aligning the space.");
                return;
            }
            _alignmentStep = 1;
            SetStatus("Point to a shared origin on the floor. Every device must use the same physical point.");
        }

        private void CompleteAlignment(Vector3 forward)
        {
            var localStates = _blocks.Values.Select(CaptureState).Where(state => state != null).ToArray();
            ClearLocalBlocks(false);
            if (_roomAnchor != null) Destroy(_roomAnchor.gameObject);

            var rotation = Quaternion.LookRotation(forward, _alignmentUp);
            var root = new GameObject("Shared Room Anchor");
            root.transform.SetPositionAndRotation(_alignmentOrigin, rotation);
            _roomAnchor = root.AddComponent<ARAnchor>();

            var floor = new GameObject("Physics Floor");
            floor.transform.SetParent(root.transform, false);
            floor.transform.localPosition = new Vector3(0f, -0.02f, 0f);
            var floorCollider = floor.AddComponent<BoxCollider>();
            floorCollider.size = new Vector3(20f, 0.04f, 20f);

            _alignmentStep = 0;
            var allStates = new Dictionary<string, WorkshopObjectState>();
            foreach (var state in _pendingSnapshot) if (ValidState(state)) allStates[state.id] = state;
            foreach (var state in localStates) if (ValidState(state)) allStates[state.id] = state;
            _pendingSnapshot = Array.Empty<WorkshopObjectState>();
            foreach (var state in allStates.Values) CreateBlock(state, false);
            foreach (var state in localStates) _network.PublishUpsert(state);
            SetStatus("Alignment complete. Every participant must use the same origin and direction.");
        }

        private void PlaceBlock(Vector3 worldPosition)
        {
            if (_blocks.Count >= MaxBlocks)
            {
                SetStatus("The 200-block limit has been reached on this device.");
                return;
            }
            var local = _roomAnchor.transform.InverseTransformPoint(worldPosition);
            var grid = BlockSize;
            local.x = Mathf.Round(local.x / grid) * grid;
            local.z = Mathf.Round(local.z / grid) * grid;
            local.y = Mathf.Max(BlockSize * 0.5f, Mathf.Round(local.y / grid) * grid + BlockSize * 0.5f);
            var state = new WorkshopObjectState
            {
                id = Guid.NewGuid().ToString("N"),
                position = new WorkshopVector3(local),
                rotation = new WorkshopQuaternion(Quaternion.identity),
                colorIndex = _selectedColor,
                physicsEnabled = false
            };
            var block = CreateBlock(state, false);
            if (block != null) _network.PublishUpsert(state);
        }

        private void TryDeleteBlock(Vector2 screenPosition)
        {
            if (!Physics.Raycast(Camera.main.ScreenPointToRay(screenPosition), out var hit, 25f)) return;
            var block = hit.collider.GetComponentInParent<WorkshopBlock>();
            if (block == null) return;
            var id = block.Id;
            RemoveBlock(id);
            _network.PublishDelete(id);
        }

        private void TryRotateBlock(Vector2 screenPosition)
        {
            if (!Physics.Raycast(Camera.main.ScreenPointToRay(screenPosition), out var hit, 25f)) return;
            var block = hit.collider.GetComponentInParent<WorkshopBlock>();
            if (block == null) return;
            block.transform.localRotation = Quaternion.AngleAxis(90f, Vector3.up) * block.transform.localRotation;
            _network.PublishUpsert(CaptureState(block));
        }

        public void SetSelectedColor(int index)
        {
            _selectedColor = Mathf.Clamp(index, 0, Palette.Length - 1);
        }

        public void ToggleDeleteMode()
        {
            _deleteMode = !_deleteMode;
            if (_deleteMode) _rotateMode = false;
            SetStatus(_deleteMode ? "Delete mode: tap a block." : "Placement mode.");
        }

        public void ToggleRotateMode()
        {
            _rotateMode = !_rotateMode;
            if (_rotateMode) _deleteMode = false;
            SetStatus(_rotateMode ? "Rotate mode: tap a block to rotate it by 90 degrees." : "Placement mode.");
        }

        public void DropAllBlocks()
        {
            if (!IsAligned) return;
            var activated = 0;
            foreach (var block in _blocks.Values)
            {
                var activate = activated < MaxDynamicBlocks;
                block.SetPhysicsEnabled(activate);
                if (activate)
                {
                    activated++;
                    _network.PublishUpsert(CaptureState(block));
                }
            }
            SetStatus("Gravity enabled for " + activated + " blocks. The MVP network-physics limit is " + MaxDynamicBlocks + ".");
        }

        public void SaveProject()
        {
            if (!IsAligned)
            {
                SetStatus("Align the room before saving a project.");
                return;
            }
            var save = new WorkshopSaveFile
            {
                roomCode = _network.RoomCode,
                objects = _blocks.Values.Select(CaptureState).Where(state => state != null).ToArray()
            };
            var path = SavePath();
            try
            {
                File.WriteAllText(path, JsonUtility.ToJson(save, true));
                SetStatus("Zapisano projekt lokalnie: " + Path.GetFileName(path));
            }
            catch (Exception exception) { SetStatus("Could not save the project: " + exception.Message); }
        }

        public void LoadProject()
        {
            var path = SavePath();
            if (!File.Exists(path))
            {
                SetStatus("Brak lokalnego zapisu projektu.");
                return;
            }
            try
            {
                if (new FileInfo(path).Length > 512 * 1024)
                {
                    SetStatus("The project file exceeds the 512 KB limit.");
                    return;
                }
                var save = JsonUtility.FromJson<WorkshopSaveFile>(File.ReadAllText(path));
                if (save == null || save.version != 1 || save.objects == null || save.objects.Length > MaxBlocks || save.objects.Any(state => !ValidState(state)))
                {
                    SetStatus("The project file has an unsupported or invalid format.");
                    return;
                }
                foreach (var id in _blocks.Keys.ToArray())
                {
                    RemoveBlock(id);
                    _network.PublishDelete(id);
                }
                foreach (var state in save.objects)
                {
                    if (CreateBlock(state, false) != null) _network.PublishUpsert(state);
                }
                SetStatus("Loaded the local project and sent its changes to the room.");
            }
            catch (Exception exception) { SetStatus("Could not load the project: " + exception.Message); }
        }

        private WorkshopBlock CreateBlock(WorkshopObjectState state, bool remote)
        {
            if (!IsAligned || !ValidState(state)) return null;
            if (_blocks.TryGetValue(state.id, out var existing))
            {
                if (remote) existing.ApplyRemoteState(state, ColorAt(state.colorIndex));
                else
                {
                    existing.transform.localPosition = state.position.ToVector3();
                    existing.transform.localRotation = state.rotation.ToQuaternion();
                    existing.SetColor(state.colorIndex, ColorAt(state.colorIndex));
                    existing.SetPhysicsEnabled(state.physicsEnabled);
                }
                return existing;
            }
            if (_blocks.Count >= MaxBlocks) return null;

            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = (remote ? "Remote Brick " : "Brick ") + state.id;
            cube.transform.SetParent(_roomAnchor.transform, false);
            cube.transform.localPosition = state.position.ToVector3();
            cube.transform.localRotation = state.rotation.ToQuaternion();
            cube.transform.localScale = Vector3.one * BlockSize;
            var component = cube.AddComponent<WorkshopBlock>();
            component.Initialize(state.id, state.colorIndex, ColorAt(state.colorIndex), state.physicsEnabled, remote);
            _blocks[state.id] = component;
            return component;
        }

        private WorkshopObjectState CaptureState(WorkshopBlock block)
        {
            if (block == null) return null;
            return new WorkshopObjectState
            {
                id = block.Id,
                position = new WorkshopVector3(block.transform.localPosition),
                rotation = new WorkshopQuaternion(block.transform.localRotation),
                colorIndex = block.ColorIndex,
                physicsEnabled = block.PhysicsEnabled
            };
        }

        private void SyncMovingBlocks()
        {
            if (!_network.IsConnected || _blocks.Count == 0) return;
            var blocks = _blocks.Values.ToArray();
            var sent = 0;
            var inspected = 0;
            while (sent < 4 && inspected < blocks.Length)
            {
                var block = blocks[_dynamicCursor % blocks.Length];
                _dynamicCursor = (_dynamicCursor + 1) % blocks.Length;
                inspected++;
                if (block == null || !block.PhysicsEnabled) continue;
                _network.PublishUpsert(CaptureState(block));
                sent++;
            }
        }

        private void RemoveBlock(string id)
        {
            if (!_blocks.TryGetValue(id, out var block)) return;
            _blocks.Remove(id);
            if (block != null) Destroy(block.gameObject);
        }

        private void ClearLocalBlocks(bool notifyServer)
        {
            foreach (var id in _blocks.Keys.ToArray())
            {
                RemoveBlock(id);
                if (notifyServer && _network != null) _network.PublishDelete(id);
            }
        }

        private void OnJoined(WorkshopJoinResponse response)
        {
            _pendingSnapshot = response.objects ?? Array.Empty<WorkshopObjectState>();
            if (IsAligned) ApplySnapshot(_pendingSnapshot);
        }

        private void OnSnapshotRequired(WorkshopEventBatch batch)
        {
            _pendingSnapshot = batch.objects ?? Array.Empty<WorkshopObjectState>();
            if (IsAligned) ApplySnapshot(_pendingSnapshot);
        }

        private void ApplySnapshot(WorkshopObjectState[] states)
        {
            ClearLocalBlocks(false);
            _pendingSnapshot = states ?? Array.Empty<WorkshopObjectState>();
            foreach (var state in _pendingSnapshot) CreateBlock(state, true);
        }

        private void OnRemoteEvent(WorkshopRoomEvent roomEvent)
        {
            if (roomEvent.clientId == _network.ClientId) return;
            if (roomEvent.kind == "delete")
            {
                RemoveBlock(roomEvent.objectId);
                _pendingSnapshot = _pendingSnapshot.Where(state => state != null && state.id != roomEvent.objectId).ToArray();
            }
            else if (roomEvent.kind == "upsert" && roomEvent.@object != null)
            {
                if (IsAligned) CreateBlock(roomEvent.@object, true);
                else _pendingSnapshot = _pendingSnapshot.Concat(new[] { roomEvent.@object }).ToArray();
            }
        }

        private static bool ValidState(WorkshopObjectState state)
        {
            if (state == null || string.IsNullOrEmpty(state.id) || state.id.Length > 64 || state.position == null || state.rotation == null) return false;
            var p = state.position;
            var r = state.rotation;
            return IsFinite(p.x) && IsFinite(p.y) && IsFinite(p.z) &&
                   Mathf.Abs(p.x) <= 50f && p.y >= -10f && p.y <= 20f && Mathf.Abs(p.z) <= 50f &&
                   IsFinite(r.x) && IsFinite(r.y) && IsFinite(r.z) && IsFinite(r.w) &&
                   Mathf.Abs(r.x) <= 1.01f && Mathf.Abs(r.y) <= 1.01f && Mathf.Abs(r.z) <= 1.01f && Mathf.Abs(r.w) <= 1.01f &&
                   Mathf.Sqrt(r.x * r.x + r.y * r.y + r.z * r.z + r.w * r.w) > 0.5f &&
                   state.colorIndex >= 0 && state.colorIndex < Palette.Length;
        }

        private static bool IsFinite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        private static string SavePath() { return Path.Combine(Application.persistentDataPath, "shared-workshop-save.json"); }
        private void SetStatus(string value) { _status = value; }

        public string Diagnostics()
        {
            var tracking = ARSession.state.ToString();
            var latency = _network != null && _network.LatencyMilliseconds >= 0 ? _network.LatencyMilliseconds + " ms" : "—";
            var planes = _planeManager == null ? 0 : _planeManager.trackables.Count();
            return string.Format("{0:0} FPS  ·  tracking: {1}  ·  planes: {2}  ·  blocks: {3}  ·  RTT: {4}  ·  tracking lost: {5:0.0}s",
                _fps, tracking, planes, _blocks.Count, latency, _trackingLossSeconds);
        }
    }
}
