using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace OrbitalDescent.EditorTools
{
    /// <summary>
    /// Android build entry points. From the menu, or headless:
    ///   Unity -batchmode -quit -projectPath . -buildTarget Android
    ///         -executeMethod OrbitalDescent.EditorTools.BuildAndroid.BuildApk [-aab] [-development]
    /// Output goes to Builds/Android/. Exits with a non-zero code when the build fails.
    /// </summary>
    public static class BuildAndroid
    {
        [MenuItem("Orbital Descent/Build Android APK")]
        public static void BuildApkMenu() => Build(false, false);

        [MenuItem("Orbital Descent/Build Android App Bundle (AAB)")]
        public static void BuildAabMenu() => Build(true, false);

        /// <summary>Batch-mode entry point.</summary>
        public static void BuildApk()
        {
            var args = Environment.GetCommandLineArgs();
            bool aab = args.Contains("-aab");
            bool dev = args.Contains("-development");
            bool ok = Build(aab, dev);
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        static bool Build(bool appBundle, bool development)
        {
            ProjectSetup.Configure();
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            EditorUserBuildSettings.buildAppBundle = appBundle;

            Directory.CreateDirectory("Builds/Android");
            string path = Path.Combine("Builds/Android", appBundle ? "OrbitalDescent.aab" : "OrbitalDescent.apk");
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ProjectSetup.ScenePath },
                locationPathName = path,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = development ? BuildOptions.Development : BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            var s = report.summary;
            if (s.result == BuildResult.Succeeded)
            {
                Debug.Log($"Orbital Descent: built {path} ({s.totalSize / (1024f * 1024f):F1} MB) in {s.totalTime.TotalSeconds:F0} s");
                return true;
            }
            Debug.LogError($"Orbital Descent: Android build {s.result} with {s.totalErrors} error(s). See the console above.");
            return false;
        }
    }
}
