using System;
using System.IO;
using CozyCafe.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CozyCafe.Editor
{
    /// <summary>
    /// Builds the real Windows standalone player carrying the
    /// NativeOverlayScenario. Invoked as:
    ///   unity -batchmode -projectPath &lt;staged game&gt; -executeMethod
    ///         CozyCafe.Editor.NativePlayerBuild.BuildWindows -quit
    /// Env contract: GAUNTLET_NATIVE_BUILD_EXE = absolute .exe output path.
    /// The scene is generated, built, then deleted from the staged copy —
    /// the tracked tree is never touched (the adapter stages game/ first).
    /// </summary>
    public static class NativePlayerBuild
    {
        public static void BuildWindows()
        {
            int exitCode = 2;
            string scenePath = null;
            try
            {
                string exe = Environment.GetEnvironmentVariable(
                    "GAUNTLET_NATIVE_BUILD_EXE");
                if (string.IsNullOrEmpty(exe))
                {
                    Debug.LogError("NativePlayerBuild: "
                        + "GAUNTLET_NATIVE_BUILD_EXE is required");
                    EditorApplication.Exit(2);
                    return;
                }
                string dir = Path.GetDirectoryName(exe);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var scene = EditorSceneManager.NewScene(
                    NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var go = new GameObject("NativeOverlay");
                go.AddComponent<NativeOverlayScenario>();
                scenePath = "Assets/__native_overlay.unity";
                EditorSceneManager.SaveScene(scene, scenePath);
                AssetDatabase.SaveAssets();

                var result = BuildPipeline.BuildPlayer(
                    new[] { scenePath }, exe,
                    BuildTarget.StandaloneWindows64, BuildOptions.None);
                bool ok = result != null
                    && result.summary.result ==
                        UnityEditor.Build.Reporting.BuildResult.Succeeded
                    && File.Exists(exe);
                Debug.Log("NativePlayerBuild result=" +
                    (result == null ? "null"
                        : result.summary.result.ToString())
                    + " exe=" + exe + " exists=" + File.Exists(exe));
                exitCode = ok ? 0 : 2;
            }
            catch (Exception e)
            {
                Debug.LogError("NativePlayerBuild failed: " + e);
                exitCode = 2;
            }
            finally
            {
                if (scenePath != null)
                {
                    AssetDatabase.DeleteAsset(scenePath);
                    AssetDatabase.SaveAssets();
                }
                EditorApplication.Exit(exitCode);
            }
        }
    }
}
