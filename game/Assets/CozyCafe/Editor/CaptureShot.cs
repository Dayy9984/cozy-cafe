using System;
using System.IO;
using CozyCafe.Core.Gauntlet;
using CozyCafe.Core.Render;
using CozyCafe.Core.Scene;
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
                    else if (scene.TileCanvasView || scene.FixedViewport)
                    {
                        exitCode = CaptureCoreRaster(scene, output,
                            ref view, ref camGo, ref rt);
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

        /// Stages with an explicit view contract (fixed viewport, tile canvas)
        /// are drawn by the shared core rasterizer; the editor presents those
        /// exact pixels through the real Camera -> RenderTexture -> ReadPixels
        /// pipeline at native resolution, so both hosts emit the same render.
        private static int CaptureCoreRaster(GameScene scene, string output,
            ref GameObject view, ref GameObject camGo, ref RenderTexture rt)
        {
            int pw, ph;
            byte[] rgba = SceneRenderer.RenderPixels(scene, scene.Zoom, out pw, out ph);
            var src = new Texture2D(pw, ph, TextureFormat.RGBA32, false);
            src.filterMode = FilterMode.Point;
            int stride = pw * 4;
            var flipped = new byte[rgba.Length];
            for (int r = 0; r < ph; r++)
            {
                Buffer.BlockCopy(rgba, (ph - 1 - r) * stride, flipped, r * stride, stride);
            }
            src.LoadRawTextureData(flipped);
            src.Apply();

            view = new GameObject("SharedCoreRaster");
            var sr = view.AddComponent<SpriteRenderer>();
            var sp = Sprite.Create(src, new Rect(0, 0, pw, ph), new Vector2(0.5f, 0.5f), 1f);
            sr.sprite = sp;

            camGo = new GameObject("GauntletCaptureCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.078f, 0.078f, 0.094f, 1f);
            cam.transform.position = new Vector3(0f, 0f, -10f);
            cam.orthographicSize = ph * 0.5f;
            cam.aspect = pw / (float)ph;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;

            rt = new RenderTexture(pw, ph, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(pw, ph, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, pw, ph), 0, 0);
            tex.Apply();
            File.WriteAllBytes(output, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            UnityEngine.Object.DestroyImmediate(sp);
            UnityEngine.Object.DestroyImmediate(src);
            return 0;
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
