using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace SharedWorkshop.EditorTools
{
    [InitializeOnLoad]
    public static class PackageInstaller
    {
        private static readonly string[] RequiredPackages =
        {
            "com.unity.xr.arfoundation@6.3.1",
            "com.unity.xr.arcore@6.3.1",
            "com.unity.xr.arkit@6.3.1",
            "com.unity.inputsystem@1.16.0",
            "com.unity.ugui"
        };

        private static AddAndRemoveRequest _request;
        private static double _deadline;
        private static bool _installing;
        private const double TimeoutSeconds = 600;

        static PackageInstaller()
        {
            EditorApplication.delayCall += EnsurePackages;
        }

        [MenuItem("Tools/Shared Workshop/Install AR packages")]
        public static void EnsurePackages()
        {
            if (_installing) return;

            var installed = new HashSet<string>(
                PackageInfo.GetAllRegisteredPackages().Select(package => package.name),
                StringComparer.OrdinalIgnoreCase);
            var missing = RequiredPackages.Where(package =>
            {
                var at = package.IndexOf('@');
                var packageName = at >= 0 ? package.Substring(0, at) : package;
                return !installed.Contains(packageName);
            }).ToArray();

            if (missing.Length == 0)
            {
                Debug.Log("[Shared Workshop] All AR, input and UI packages are installed.");
                return;
            }

            Debug.Log("[Shared Workshop] Installing packages with Unity Package Manager: " + string.Join(", ", missing));
            _installing = true;
            _request = Client.AddAndRemove(missing, Array.Empty<string>());
            _deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.update += Poll;
        }

        private static void Poll()
        {
            if (_request == null) return;
            if (!_request.IsCompleted)
            {
                if (EditorApplication.timeSinceStartup > _deadline)
                {
                    Finish("Package resolution timed out. Check the Unity Package Manager connection and retry from Tools/Shared Workshop.", true);
                }
                return;
            }

            if (_request.Status == StatusCode.Success)
            {
                var resolved = _request.Result.Select(package => package.name + "@" + package.version);
                EditorPrefs.SetBool("SharedWorkshop.CreateStarterSceneAfterInstall", true);
                AssetDatabase.Refresh();
                Finish("Resolved: " + string.Join(", ", resolved), false);
            }
            else
            {
                var error = _request.Error == null ? "Unknown package manager error." : _request.Error.message;
                Finish("Package installation failed: " + error, true);
            }
        }

        private static void Finish(string message, bool failed)
        {
            EditorApplication.update -= Poll;
            _request = null;
            _installing = false;
            if (failed) Debug.LogError("[Shared Workshop] " + message);
            else Debug.Log("[Shared Workshop] " + message);
        }
    }
}
