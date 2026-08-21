#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BallisticSniper.Editor
{
    [InitializeOnLoad]
    public static class ProjectConfigurator
    {
        private const string ScenePath = "Assets/BallisticSniper/Scenes/BallisticSniper.unity";
        private const string AtlasPath = "Assets/BallisticSniper/Resources/BallisticSniper/Textures/range_material_atlas.png";

        static ProjectConfigurator()
        {
            EditorApplication.delayCall += Configure;
        }

        [MenuItem("Ballistic Sniper/Configure Project")]
        public static void Configure()
        {
            PlayerSettings.companyName = "Denis Games";
            PlayerSettings.productName = "Ballistic Sniper 3.1";
            PlayerSettings.bundleVersion = "3.1.0-unity";
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.denis.ballisticsniper.unity.v31");
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.bundleVersionCode = 7;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64 | AndroidArchitecture.ARMv7;
            PlayerSettings.Android.forceInternetPermission = false;
            PlayerSettings.Android.forceSDCardPermission = false;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetApiCompatibilityLevel(BuildTargetGroup.Android, ApiCompatibilityLevel.NET_Standard_2_0);
            EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            ConfigureAtlasImport();
        }

        private static void ConfigureAtlasImport()
        {
            TextureImporter importer = AssetImporter.GetAtPath(AtlasPath) as TextureImporter;
            if (importer == null) return;
            bool changed = importer.textureType != TextureImporterType.Default ||
                           importer.wrapMode != TextureWrapMode.Repeat ||
                           importer.filterMode != FilterMode.Trilinear ||
                           importer.anisoLevel != 8 ||
                           !importer.mipmapEnabled ||
                           importer.npotScale != TextureImporterNPOTScale.ToNearest ||
                           importer.textureCompression != TextureImporterCompression.CompressedHQ ||
                           importer.maxTextureSize != 2048;
            if (!changed) return;
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 8;
            importer.mipmapEnabled = true;
            importer.npotScale = TextureImporterNPOTScale.ToNearest;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
        }

        [MenuItem("Ballistic Sniper/Build Android APK")]
        public static void BuildAndroidApk()
        {
            Configure();
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
            {
                throw new InvalidOperationException("Android Build Support is not installed for this Unity editor.");
            }
            string outputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Builds/Android"));
            Directory.CreateDirectory(outputDirectory);
            string outputPath = Path.Combine(outputDirectory, "Ballistic-Sniper-Unity-v3.1.0.apk");
            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = outputPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.CompressWithLz4HC
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException("Android build failed: " + report.summary.result +
                                                    ". Inspect the Unity Console for the first compiler/build error.");
            }
            Debug.Log("Ballistic Sniper APK: " + outputPath);
            if (!Application.isBatchMode)
            {
                EditorUtility.RevealInFinder(outputPath);
            }
        }
    }
}
#endif
