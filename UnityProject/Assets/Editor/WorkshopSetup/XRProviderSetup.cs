using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.ARCore;
using UnityEngine.XR.ARKit;

namespace SharedWorkshop.EditorTools
{
    public static class XRProviderSetup
    {
        [MenuItem("Tools/Shared Workshop/Configure XR providers")]
        public static void ConfigureProviders()
        {
            if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget) || perTarget == null)
            {
                Debug.LogError("[Shared Workshop] XR Plug-in Management settings are missing. Open Project Settings > XR Plug-in Management to initialize them, then retry.");
                return;
            }

            var android = perTarget.SettingsForBuildTarget(BuildTargetGroup.Android);
            var ios = perTarget.SettingsForBuildTarget(BuildTargetGroup.iOS);
            var androidReady = EnsureLoader(android, typeof(ARCoreLoader).FullName, BuildTargetGroup.Android);
            var iosReady = EnsureLoader(ios, typeof(ARKitLoader).FullName, BuildTargetGroup.iOS);

            AssetDatabase.SaveAssets();
            Debug.Log("[Shared Workshop] XR providers configured: Android ARCore=" + androidReady + ", iOS ARKit=" + iosReady + ".");
        }

        private static bool EnsureLoader(XRGeneralSettings settings, string loaderType, BuildTargetGroup target)
        {
            if (settings == null || settings.AssignedSettings == null)
            {
                Debug.LogError("[Shared Workshop] XR Plug-in Management settings are missing. Open Project Settings > XR Plug-in Management and install its package, then retry.");
                return false;
            }

            var manager = settings.AssignedSettings;
            if (!XRPackageMetadataStore.AssignLoader(manager, loaderType, target))
            {
                Debug.LogError("[Shared Workshop] Could not assign XR loader " + loaderType + ". Enable it in Project Settings > XR Plug-in Management.");
                return false;
            }
            EditorUtility.SetDirty(manager);
            EditorUtility.SetDirty(settings);
            return true;
        }
    }
}
