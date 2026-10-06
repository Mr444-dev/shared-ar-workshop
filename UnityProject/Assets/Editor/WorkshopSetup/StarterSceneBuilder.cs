using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Unity.XR.CoreUtils;
using SharedWorkshop.Runtime;

namespace SharedWorkshop.EditorTools
{
    public static class StarterSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/SharedWorkshop.unity";

        [MenuItem("Tools/Shared Workshop/Create starter scene")]
        public static void CreateStarterScene()
        {
            Directory.CreateDirectory(Path.Combine(Application.dataPath, "Scenes"));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.portfolio.sharedarworkshop");
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.iOS, "com.portfolio.sharedarworkshop");
            PlayerSettings.Android.forceInternetPermission = true;
            PlayerSettings.iOS.cameraUsageDescription = "The camera is used for on-device AR tracking and floor detection. Camera images are not uploaded.";
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;

            var sessionObject = new GameObject("AR Session");
            sessionObject.AddComponent<ARSession>();
            sessionObject.AddComponent<ARInputManager>();

            var originObject = new GameObject("XR Origin");
            var origin = originObject.AddComponent<XROrigin>();
            var planeManager = originObject.AddComponent<ARPlaneManager>();
            planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;
            planeManager.planePrefab = CreatePlanePrefab();
            originObject.AddComponent<ARRaycastManager>();
            originObject.AddComponent<ARAnchorManager>();

            var offset = new GameObject("Camera Offset");
            offset.transform.SetParent(originObject.transform, false);
            var cameraObject = new GameObject("AR Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(offset.transform, false);
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            cameraObject.AddComponent<ARCameraManager>();
            cameraObject.AddComponent<ARCameraBackground>();

            var poseDriver = cameraObject.AddComponent<TrackedPoseDriver>();
            poseDriver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            poseDriver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            poseDriver.positionInput = new InputActionProperty(new InputAction(
                "Center eye position", InputActionType.Value, "<XRHMD>/centerEyePosition"));
            poseDriver.rotationInput = new InputActionProperty(new InputAction(
                "Center eye rotation", InputActionType.Value, "<XRHMD>/centerEyeRotation"));
            origin.Camera = camera;
            origin.CameraFloorOffsetObject = offset;
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Device;

            var workshopObject = new GameObject("Shared Workshop");
            workshopObject.AddComponent<SharedWorkshopController>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Selection.activeGameObject = workshopObject;
            Debug.Log("[Shared Workshop] Starter scene created at " + ScenePath + ". Enable ARCore/ARKit in XR Plug-in Management before building.");
        }

        private static GameObject CreatePlanePrefab()
        {
            const string folder = "Assets/SharedWorkshop/Prefabs";
            const string path = folder + "/DetectedPlane.prefab";
            Directory.CreateDirectory(Path.Combine(Application.dataPath, "SharedWorkshop", "Prefabs"));
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            var plane = new GameObject("Detected Plane", typeof(ARPlane), typeof(ARPlaneMeshVisualizer), typeof(MeshFilter), typeof(MeshRenderer), typeof(LineRenderer));
            var planeRenderer = plane.GetComponent<MeshRenderer>();
            var planeMaterial = LoadOrCreateMaterial("PlaneFill", new Color(0.05f, 0.8f, 0.72f, 0.18f));
            if (planeMaterial != null) planeRenderer.sharedMaterial = planeMaterial;
            planeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            planeRenderer.receiveShadows = false;
            var outline = plane.GetComponent<LineRenderer>();
            outline.useWorldSpace = false;
            outline.loop = true;
            outline.startWidth = 0.008f;
            outline.endWidth = 0.008f;
            var outlineMaterial = LoadOrCreateMaterial("PlaneOutline", new Color(0.1f, 0.95f, 0.83f, 0.9f));
            if (outlineMaterial != null) outline.sharedMaterial = outlineMaterial;
            var prefab = PrefabUtility.SaveAsPrefabAsset(plane, path);
            Object.DestroyImmediate(plane);
            AssetDatabase.SaveAssets();
            return prefab;
        }

        private static Material LoadOrCreateMaterial(string name, Color color)
        {
            const string folder = "Assets/SharedWorkshop/Materials";
            Directory.CreateDirectory(Path.Combine(Application.dataPath, "SharedWorkshop", "Materials"));
            var path = folder + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            var shader = Shader.Find("Sprites/Default");
            if (shader == null) return null;
            var material = new Material(shader) { color = color };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
