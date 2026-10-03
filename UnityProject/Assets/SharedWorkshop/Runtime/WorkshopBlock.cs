using UnityEngine;

namespace SharedWorkshop.Runtime
{
    [DisallowMultipleComponent]
    public sealed class WorkshopBlock : MonoBehaviour
    {
        private static readonly Material[] PaletteMaterials = new Material[SharedWorkshopController.PaletteSize];
        private Rigidbody _rigidbody;
        private bool _remote;

        public string Id { get; private set; }
        public int ColorIndex { get; private set; }
        public bool PhysicsEnabled { get { return _rigidbody != null && !_rigidbody.isKinematic; } }

        public void Initialize(string id, int colorIndex, Color color, bool physicsEnabled, bool remote)
        {
            Id = id;
            ColorIndex = colorIndex;
            _remote = remote;

            var renderer = GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                var shader = Shader.Find("Standard");
                if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader != null)
                {
                    if (PaletteMaterials[colorIndex] == null)
                    {
                        PaletteMaterials[colorIndex] = new Material(shader) { color = color };
                    }
                    renderer.sharedMaterial = PaletteMaterials[colorIndex];
                }
            }

            _rigidbody = GetComponent<Rigidbody>();
            if (_rigidbody == null) _rigidbody = gameObject.AddComponent<Rigidbody>();
            _rigidbody.useGravity = true;
            _rigidbody.isKinematic = remote || !physicsEnabled;
            _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            _rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }

        public void SetColor(int colorIndex, Color color)
        {
            ColorIndex = colorIndex;
            var renderer = GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                var shader = Shader.Find("Standard");
                if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader != null && PaletteMaterials[colorIndex] == null)
                    PaletteMaterials[colorIndex] = new Material(shader) { color = color };
                if (PaletteMaterials[colorIndex] != null) renderer.sharedMaterial = PaletteMaterials[colorIndex];
            }
        }

        public void SetPhysicsEnabled(bool enabled)
        {
            if (_rigidbody == null || _remote) return;
            _rigidbody.isKinematic = !enabled;
            _rigidbody.useGravity = true;
            if (enabled) _rigidbody.WakeUp();
        }

        public void ApplyRemoteState(WorkshopObjectState state, Color color)
        {
            if (!_remote || state == null || state.position == null || state.rotation == null) return;
            transform.localPosition = state.position.ToVector3();
            transform.localRotation = state.rotation.ToQuaternion();
            SetColor(state.colorIndex, color);
        }

    }
}
