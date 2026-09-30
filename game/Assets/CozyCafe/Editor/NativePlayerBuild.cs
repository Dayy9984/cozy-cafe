using System;
using System.IO;
using CozyCafe.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CozyCafe.Editor
{
    /// <summary>
    /// Builds the real standalone players carrying the
    /// NativeOverlayScenario. Invoked as:
    ///   unity -batchmode -projectPath &lt;staged game&gt; -executeMethod
    ///         CozyCafe.Editor.NativePlayerBuild.BuildWindows -quit
    ///   unity -batchmode -projectPath &lt;staged game&gt; -executeMethod
    ///         CozyCafe.Editor.NativePlayerBuild.BuildMacOS -quit
    /// Env contract: GAUNTLET_NATIVE_BUILD_EXE = absolute .exe output path
    /// (Windows), GAUNTLET_NATIVE_BUILD_APP = absolute .app output path
    /// (macOS). The scene is generated, built, then deleted from the staged
    /// copy — the tracked tree is never touched (the adapter stages game/
    /// first).
    /// </summary>
    public static class NativePlayerBuild
    {
        public static void BuildWindows()
        {
            BuildPlayer("GAUNTLET_NATIVE_BUILD_EXE",
                BuildTarget.StandaloneWindows64);
        }

        public static void BuildMacOS()
        {
            BuildPlayer("GAUNTLET_NATIVE_BUILD_APP",
                BuildTarget.StandaloneOSX);
        }

        /// Playable build: the real game scene (CozyCafeBootstrap view +
        /// framing camera) instead of the native-verification scenario.
        public static void BuildMacOSGame()
        {
            BuildPlayer("GAUNTLET_NATIVE_BUILD_APP",
                BuildTarget.StandaloneOSX, playable: true);
        }

        public static void BuildWindowsGame()
        {
            BuildPlayer("GAUNTLET_NATIVE_BUILD_EXE",
                BuildTarget.StandaloneWindows64, playable: true);
        }

        private static void BuildPlayer(string outEnvVar,
            BuildTarget target, bool playable = false)
        {
            int exitCode = 2;
            string scenePath = null;
            try
            {
                string exe = Environment.GetEnvironmentVariable(outEnvVar);
                if (string.IsNullOrEmpty(exe))
                {
                    Debug.LogError("NativePlayerBuild: "
                        + outEnvVar + " is required");
                    EditorApplication.Exit(2);
                    return;
                }
                string dir = Path.GetDirectoryName(
                    exe.TrimEnd('/', Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var scene = EditorSceneManager.NewScene(
                    NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var go = new GameObject(
                    playable ? "CozyCafePlayable" : "NativeOverlay");
                if (playable)
                    go.AddComponent<CozyCafePlayable>();
                else
                    go.AddComponent<NativeOverlayScenario>();
                scenePath = playable
                    ? "Assets/__playable.unity" : "Assets/__native_overlay.unity";
                EditorSceneManager.SaveScene(scene, scenePath);
                AssetDatabase.SaveAssets();

                var result = BuildPipeline.BuildPlayer(
                    new[] { scenePath }, exe, target, BuildOptions.None);
                bool ok = result != null
                    && result.summary.result ==
                        UnityEditor.Build.Reporting.BuildResult.Succeeded
                    && (File.Exists(exe) || Directory.Exists(exe));
                Debug.Log("NativePlayerBuild result=" +
                    (result == null ? "null"
                        : result.summary.result.ToString())
                    + " target=" + target + " out=" + exe
                    + " exists=" + ok);
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
