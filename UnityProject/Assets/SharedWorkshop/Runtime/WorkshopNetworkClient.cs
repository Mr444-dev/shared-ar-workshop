using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace SharedWorkshop.Runtime
{
    [DisallowMultipleComponent]
    public sealed class WorkshopNetworkClient : MonoBehaviour
    {
        public event Action<WorkshopJoinResponse> Joined;
        public event Action<WorkshopRoomEvent> EventReceived;
        public event Action<WorkshopEventBatch> SnapshotRequired;
        public event Action<string> StatusChanged;

        private string _baseUrl;
        private string _roomCode;
        private string _displayName;
        private string _clientId;
        private long _sequence;
        private bool _connected;
        private Coroutine _polling;
        private Coroutine _pinging;

        public bool IsConnected { get { return _connected; } }
        public string ClientId { get { return _clientId; } }
        public string RoomCode { get { return _roomCode; } }
        public int LatencyMilliseconds { get; private set; } = -1;

        public void Join(string baseUrl, string roomCode, string displayName)
        {
            if (_connected) return;
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                SetStatus("The server URL must start with http:// or https://.");
                return;
            }
            if (string.IsNullOrWhiteSpace(roomCode) || string.IsNullOrWhiteSpace(displayName))
            {
                SetStatus("Enter a room code and display name.");
                return;
            }

            _baseUrl = baseUrl.Trim().TrimEnd('/');
            _roomCode = roomCode.Trim().ToUpperInvariant();
            _displayName = displayName.Trim();
            StartCoroutine(JoinRoutine());
        }

        public void PublishUpsert(WorkshopObjectState state)
        {
            if (!_connected || state == null) return;
            var payload = new UpsertRequest { clientId = _clientId, @object = state };
            StartCoroutine(PostRoutine("/objects/upsert", JsonUtility.ToJson(payload)));
        }

        public void PublishDelete(string objectId)
        {
            if (!_connected || string.IsNullOrEmpty(objectId)) return;
            var payload = new DeleteRequest { clientId = _clientId, objectId = objectId };
            StartCoroutine(PostRoutine("/objects/delete", JsonUtility.ToJson(payload)));
        }

        private IEnumerator JoinRoutine()
        {
            SetStatus("Connecting to room " + _roomCode + "…");
            var payload = JsonUtility.ToJson(new JoinRequest { displayName = _displayName });
            using (var request = CreatePost("/api/rooms/" + _roomCode + "/join", payload))
            {
                var started = Time.realtimeSinceStartup;
                yield return request.SendWebRequest();
                RecordLatency(started);
                if (request.result != UnityWebRequest.Result.Success)
                {
                    SetStatus("Connection failed: " + request.error);
                    yield break;
                }
                WorkshopJoinResponse response;
                try { response = JsonUtility.FromJson<WorkshopJoinResponse>(request.downloadHandler.text); }
                catch (Exception exception)
                {
                    SetStatus("Invalid server response: " + exception.Message);
                    yield break;
                }
                if (response == null || string.IsNullOrEmpty(response.clientId))
                {
                    SetStatus("The server did not return a participant ID.");
                    yield break;
                }

                _clientId = response.clientId;
                _sequence = response.sequence;
                _connected = true;
                Joined?.Invoke(response);
                SetStatus("Connected · room " + _roomCode);
                _polling = StartCoroutine(PollRoutine());
                _pinging = StartCoroutine(PingRoutine());
            }
        }

        private IEnumerator PollRoutine()
        {
            var backoff = 0.5f;
            while (_connected)
            {
                var url = "/api/rooms/" + _roomCode + "/events?clientId=" + UnityWebRequest.EscapeURL(_clientId) + "&since=" + _sequence;
                using (var request = UnityWebRequest.Get(_baseUrl + url))
                {
                    request.timeout = 30;
                    yield return request.SendWebRequest();
                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        if (request.responseCode == 401 || request.responseCode == 404)
                        {
                            _connected = false;
                            SetStatus("The room session expired. Rejoining…");
                            yield return new WaitForSecondsRealtime(0.5f);
                            yield return JoinRoutine();
                            yield break;
                        }
                        SetStatus("Retrying the connection… " + request.error);
                        yield return new WaitForSecondsRealtime(backoff);
                        backoff = Mathf.Min(backoff * 1.8f, 5f);
                        continue;
                    }

                    backoff = 0.5f;
                    WorkshopEventBatch batch;
                    try { batch = JsonUtility.FromJson<WorkshopEventBatch>(request.downloadHandler.text); }
                    catch (Exception exception)
                    {
                        SetStatus("Could not read room changes: " + exception.Message);
                        yield return new WaitForSecondsRealtime(0.5f);
                        continue;
                    }
                    if (batch == null)
                    {
                        yield return new WaitForSecondsRealtime(0.5f);
                        continue;
                    }
                    if (batch.resyncRequired)
                    {
                        _sequence = batch.sequence;
                        SnapshotRequired?.Invoke(batch);
                        continue;
                    }
                    if (batch.events != null)
                    {
                        foreach (var roomEvent in batch.events)
                        {
                            if (roomEvent == null || roomEvent.sequence <= _sequence) continue;
                            _sequence = roomEvent.sequence;
                            EventReceived?.Invoke(roomEvent);
                        }
                    }
                    else
                    {
                        _sequence = Math.Max(_sequence, batch.sequence);
                    }
                }
            }
        }

        private IEnumerator PingRoutine()
        {
            while (_connected)
            {
                using (var request = UnityWebRequest.Get(_baseUrl + "/health"))
                {
                    request.timeout = 5;
                    var started = Time.realtimeSinceStartup;
                    yield return request.SendWebRequest();
                    if (request.result == UnityWebRequest.Result.Success) RecordLatency(started);
                }
                yield return new WaitForSecondsRealtime(2f);
            }
        }

        private IEnumerator PostRoutine(string operation, string payload)
        {
            using (var request = CreatePost("/api/rooms/" + _roomCode + operation, payload))
            {
                var started = Time.realtimeSinceStartup;
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success) RecordLatency(started);
                else SetStatus("Could not send the change: " + request.error);
            }
        }

        private UnityWebRequest CreatePost(string path, string json)
        {
            var request = new UnityWebRequest(_baseUrl + path, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
            request.timeout = 10;
            return request;
        }

        private void RecordLatency(float started)
        {
            var sample = Mathf.RoundToInt((Time.realtimeSinceStartup - started) * 1000f);
            LatencyMilliseconds = LatencyMilliseconds < 0 ? sample : Mathf.RoundToInt(Mathf.Lerp(LatencyMilliseconds, sample, 0.35f));
        }

        private void SetStatus(string value) { StatusChanged?.Invoke(value); }
    }
}
