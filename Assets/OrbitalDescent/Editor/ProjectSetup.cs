using System.IO;
using UnityEditor;
#if UNITY_2021_2_OR_NEWER
using UnityEditor.Build;
#endif
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace OrbitalDescent.EditorTools
{
    /// <summary>
    /// One-click project configuration for the Android build: player settings, colour space, input
    /// handling, orientation and the (empty) main scene. Runs automatically the first time the project
    /// is opened (when no build scene exists yet) and is available from the "Orbital Descent" menu.
    /// </summary>
    [InitializeOnLoad]
    public static class ProjectSetup
    {
        public const string ScenePath = "Assets/Scenes/Main.unity";
        public const string BundleId = "com.hawnji.orbitaldescent";

        static ProjectSetup()
        {
            // Defer until the editor has finished loading.
            EditorApplication.delayCall += () =>
            {
                if (Application.isBatchMode) return;
                if (EditorBuildSettings.scenes.Length == 0 && !SessionState.GetBool("od.setupRan", false))
                {
                    SessionState.SetBool("od.setupRan", true);
                    Configure();
                }
            };
        }

        [MenuItem("Orbital Descent/Configure Project for Android")]
        public static void Configure()
        {
            PlayerSettings.companyName = "Hawnji";
            PlayerSettings.productName = "Orbital Descent";
            PlayerSettings.bundleVersion = "0.1.0";
#if UNITY_2021_2_OR_NEWER
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, BundleId);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
#else
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, BundleId);
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
#endif
            PlayerSettings.colorSpace = ColorSpace.Linear;

            // Landscape only, either way up.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;

            // Android: 64-bit ARM (IL2CPP, set above; required by Google Play), Vulkan with GLES3 fallback.
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.Android.renderOutsideSafeArea = true;   // the HUD handles the safe area itself
            PlayerSettings.Android.startInFullscreen = true;

            SetInputManagerOnly();

            // Built-in render pipeline with the depth texture used by the atmosphere pass.
            GraphicsSettings.defaultRenderPipeline = null;
            QualitySettings.renderPipeline = null;

            EnsureScene();
            AssetDatabase.SaveAssets();
            Debug.Log("Orbital Descent: project configured for Android (IL2CPP ARM64, Linear, landscape).");
        }

        /// <summary>The game reads touches through the legacy Input Manager (no Input System package).</summary>
        static void SetInputManagerOnly()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (assets == null || assets.Length == 0) return;
            var so = new SerializedObject(assets[0]);
            var p = so.FindProperty("activeInputHandler");
            if (p != null && p.intValue != 0)
            {
                p.intValue = 0;   // 0 = Input Manager (old), 1 = Input System, 2 = both
                so.ApplyModifiedProperties();
                Debug.Log("Orbital Descent: active input handling set to Input Manager. Restart the editor if prompted.");
            }
        }

        static void EnsureScene()
        {
            if (!File.Exists(ScenePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
                // The game builds its whole scene from code (see Bootstrap), so the scene stays empty.
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }
    }
}
