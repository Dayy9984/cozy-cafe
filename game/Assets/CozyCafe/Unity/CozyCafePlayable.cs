using UnityEngine;

namespace CozyCafe.Unity
{
    /// <summary>
    /// Playable-scene bootstrap: builds the live cafe view via
    /// CozyCafeBootstrap (Awake), then frames an orthographic camera on the
    /// real sprite bounds in Start so the player build shows the actual
    /// cafe instead of an empty window.
    /// </summary>
    public sealed class CozyCafePlayable : MonoBehaviour
    {
        private void Awake()
        {
            gameObject.AddComponent<CozyCafeBootstrap>();
        }

        private void Start()
        {
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.13f, 0.11f, 0.14f);

            var renderers = FindObjectsOfType<SpriteRenderer>();
            Vector3 center = Vector3.zero;
            float need = 320f;
            if (renderers.Length > 0)
            {
                var b = renderers[0].bounds;
                foreach (var r in renderers) b.Encapsulate(r.bounds);
                center = b.center;
                need = Mathf.Max(b.extents.y,
                    b.extents.x / Mathf.Max(0.1f, cam.aspect)) + 40f;
            }
            cam.transform.position = new Vector3(center.x, center.y, -50f);
            cam.orthographicSize = need;
        }
    }
}
