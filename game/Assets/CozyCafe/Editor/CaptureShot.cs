using System;
using System.IO;
using CozyCafe.Core.Gauntlet;
using CozyCafe.Unity;
using UnityEditor;
using UnityEngine;

namespace CozyCafe.Editor
{
    /// <summary>
    /// Unity capture host for visual gates. Env contract:
    ///   GAUNTLET_CAPTURE_STAGE   stage id whose live scene is captured
    ///   GAUNTLET_CAPTURE_OUTPUT  absolute PNG path to write
    /// Builds real view objects from the shared core state, renders through an
    /// actual Camera -&gt; RenderTexture -&gt; ReadPixels pipeline, and saves a PNG.
    /// No headless math substitutes for the render.
    /// </summary>
    public static class CaptureShot
    {
        public static void Run()
        {
            string stage = Environment.GetEnvironmentVariable("GAUNTLET_CAPTURE_STAGE");
            string output = Environment.GetEnvironmentVariable("GAUNTLET_CAPTURE_OUTPUT");
            int exitCode = 2;
            GameObject view = null;
            GameObject camGo = null;
            RenderTexture rt = null;
            try
            {
                if (string.IsNullOrEmpty(stage) || string.IsNullOrEmpty(output))
                {
                    Debug.LogError("CaptureShot: GAUNTLET_CAPTURE_STAGE and GAUNTLET_CAPTURE_OUTPUT are required");
                }
                else
                {
                    var scene = StageScenes.Build(stage);
                    if (scene == null || !scene.IsLoaded)
                    {
                        Debug.LogError("CaptureShot: stage '" + stage + "' produced no scene");
                    }
                    else
                    {
                        view = StageViewBuilder.Build(scene);
                        camGo = new GameObject("GauntletCaptureCamera");
                        var cam = camGo.AddComponent<Camera>();
                        cam.orthographic = true;
                        cam.clearFlags = CameraClearFlags.SolidColor;
                        cam.backgroundColor = new Color(0.106f, 0.09f, 0.122f, 1f);
                        FrameOn(cam, view, 960, 600);

                        rt = new RenderTexture(960, 600, 24);
                        cam.targetTexture = rt;
                        cam.Render();
                        RenderTexture.active = rt;
                        var tex = new Texture2D(960, 600, TextureFormat.RGB24, false);
                        tex.ReadPixels(new Rect(0, 0, 960, 600), 0, 0);
                        tex.Apply();
                        File.WriteAllBytes(output, tex.EncodeToPNG());
                        UnityEngine.Object.DestroyImmediate(tex);
                        exitCode = 0;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("CaptureShot failed: " + e);
            }
            finally
            {
                if (rt != null) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
                if (camGo != null) UnityEngine.Object.DestroyImmediate(camGo);
                if (view != null) UnityEngine.Object.DestroyImmediate(view);
            }
            EditorApplication.Exit(exitCode);
        }

        private static void FrameOn(Camera cam, GameObject root, int w, int h)
        {
            var bounds = new Bounds();
            bool any = false;
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
            if (!any) bounds = new Bounds(Vector3.zero, Vector3.one * 10f);
            Vector3 c = bounds.center;
            cam.transform.position = new Vector3(c.x, c.y, c.z - 50f);
            float halfH = Mathf.Max(bounds.extents.y + 20f, 10f);
            float halfW = bounds.extents.x + 20f;
            float need = Mathf.Max(halfH, halfW * h / (float)w);
            cam.orthographicSize = need;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;
        }
    }
}
