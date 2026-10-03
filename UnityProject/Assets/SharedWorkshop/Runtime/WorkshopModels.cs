using System;
using UnityEngine;

namespace SharedWorkshop.Runtime
{
    [Serializable]
    public sealed class WorkshopVector3
    {
        public float x;
        public float y;
        public float z;

        public WorkshopVector3() { }
        public WorkshopVector3(Vector3 value) { x = value.x; y = value.y; z = value.z; }
        public Vector3 ToVector3() { return new Vector3(x, y, z); }
    }

    [Serializable]
    public sealed class WorkshopQuaternion
    {
        public float x;
        public float y;
        public float z;
        public float w = 1f;

        public WorkshopQuaternion() { }
        public WorkshopQuaternion(Quaternion value) { x = value.x; y = value.y; z = value.z; w = value.w; }
        public Quaternion ToQuaternion() { return new Quaternion(x, y, z, w); }
    }

    [Serializable]
    public sealed class WorkshopObjectState
    {
        public string id;
        public WorkshopVector3 position;
        public WorkshopQuaternion rotation;
        public int colorIndex;
        public bool physicsEnabled;
    }

    [Serializable]
    public sealed class WorkshopJoinResponse
    {
        public string roomCode;
        public string clientId;
        public long sequence;
        public WorkshopObjectState[] objects;
    }

    [Serializable]
    public sealed class WorkshopRoomEvent
    {
        public long sequence;
        public string kind;
        public string clientId;
        public WorkshopObjectState @object;
        public string objectId;
    }

    [Serializable]
    public sealed class WorkshopEventBatch
    {
        public long sequence;
        public bool resyncRequired;
        public WorkshopObjectState[] objects;
        public WorkshopRoomEvent[] events;
    }

    [Serializable]
    public sealed class WorkshopSaveFile
    {
        public int version = 1;
        public string roomCode;
        public WorkshopObjectState[] objects;
    }

    [Serializable]
    internal sealed class JoinRequest
    {
        public string displayName;
    }

    [Serializable]
    internal sealed class UpsertRequest
    {
        public string clientId;
        public WorkshopObjectState @object;
    }

    [Serializable]
    internal sealed class DeleteRequest
    {
        public string clientId;
        public string objectId;
    }
}
