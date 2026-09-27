using System.Collections.Generic;
using CozyCafe.Core.Iso;
using CozyCafe.Core.Layout;
using CozyCafe.Core.Scene;
using UnityEngine;

namespace CozyCafe.Unity
{
    /// <summary>
    /// Builds live render objects from the shared core scene state so editor
    /// captures and runtime views draw the actual game, not a mock. One pixel
    /// maps to one world unit; screen Y is flipped into Unity's up axis.
    /// Render offsets come from the core contract and apply to the sprite
    /// transform only — logical state is untouched.
    /// </summary>
    public static class StageViewBuilder
    {
        private static readonly Dictionary<string, Sprite> spriteCache =
            new Dictionary<string, Sprite>();

        public static GameObject Build(GameScene scene)
        {
            var root = new GameObject("CozyCafeStageView");
            if (scene == null || scene.Room == null) return root;
            var room = scene.Room;

            for (int y = 0; y < room.Height; y++)
            {
                for (int x = 0; x < room.Width; x++)
                {
                    if (!room.HasCell(x, y)) continue;
                    var go = new GameObject("tile_" + x + "_" + y);
                    go.transform.SetParent(root.transform, false);
                    var sr = go.AddComponent<SpriteRenderer>();
                    bool odd = ((x + y) & 1) == 1;
                    sr.sprite = DiamondSprite(
                        IsoMath.TileTopWidthPx, IsoMath.TileTopHeightPx,
                        odd ? new Color(0.737f, 0.545f, 0.337f) : new Color(0.792f, 0.596f, 0.376f));
                    double sx, sy;
                    IsoMath.Project(x + 0.5, y + 0.5, out sx, out sy);
                    go.transform.position = new Vector3((float)sx, (float)-sy, 0f);
                    sr.sortingOrder = 0;
                    AddSideFaces(root, room, x, y);
                }
            }

            var furn = new List<Furniture>(scene.Furniture);
            furn.Sort(delegate (Furniture a, Furniture b)
            {
                int c = a.DepthKey.CompareTo(b.DepthKey);
                return c != 0 ? c : a.HostId.CompareTo(b.HostId);
            });
            var byId = new Dictionary<int, Furniture>();
            foreach (var f in furn) if (f.Id != 0) byId[f.Id] = f;
            foreach (var f in furn) AddFurniture(root, f, byId);
            foreach (var a in scene.Agents) AddAgent(root, a);
            return root;
        }

        private static void AddSideFaces(GameObject root, RoomGrid room, int x, int y)
        {
            // 4 px visual skirts on the outer boundary only; never colliders.
            if (!room.HasCell(x + 1, y))
            {
                CreateSkirt(root, x, y, true);
            }
            if (!room.HasCell(x, y + 1))
            {
                CreateSkirt(root, x, y, false);
            }
        }

        private static void CreateSkirt(GameObject root, int x, int y, bool rightEdge)
        {
            var go = new GameObject("side_" + x + "_" + y + (rightEdge ? "_r" : "_l"));
            go.transform.SetParent(root.transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = DiamondSprite(32, IsoMath.VisualThicknessPx + 16,
                rightEdge ? new Color(0.478f, 0.322f, 0.204f) : new Color(0.4f, 0.267f, 0.173f));
            double ex, ey;
            if (rightEdge) IsoMath.Project(x + 1, y + 0.5, out ex, out ey);
            else IsoMath.Project(x + 0.5, y + 1, out ex, out ey);
            go.transform.position = new Vector3((float)ex, (float)-(ey + 8), 0f);
            sr.sortingOrder = 1;
        }

        private static void AddFurniture(GameObject root, Furniture f,
            Dictionary<int, Furniture> byId)
        {
            var go = new GameObject("furn_" + f.Kind + "_" + f.CellX + "_" + f.CellY);
            go.transform.SetParent(root.transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = DiamondSprite(40, 19, FurnitureColor(f.Kind));
            // Same render contract as the core rasterizer: (ground + target)
            // once for floor pieces, host mount resolution for children.
            double dx, dy;
            RenderContract.DrawAnchorResolved(f,
                delegate (int id)
                {
                    Furniture h;
                    return byId.TryGetValue(id, out h) ? h : null;
                },
                1.0, out dx, out dy);
            go.transform.position = new Vector3((float)dx, (float)-dy, 0f);
            sr.sortingOrder = 100 + f.DepthKey + (f.HostId != 0 ? 1 : 0);
        }

        private static void AddAgent(GameObject root, Agent a)
        {
            var go = new GameObject("agent_" + a.Name);
            go.transform.SetParent(root.transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = DiamondSprite(12, 18,
                a.IsStaff ? new Color(0.36f, 0.56f, 0.88f) : new Color(0.89f, 0.57f, 0.36f));
            double gx, gy;
            IsoMath.Project(a.GridX, a.GridY, out gx, out gy);
            go.transform.position = new Vector3((float)gx, (float)-(gy - 9), 0f);
            sr.sortingOrder = 200;
        }

        private static Color FurnitureColor(FurnitureKind kind)
        {
            switch (kind)
            {
                case FurnitureKind.Table: return new Color(0.63f, 0.4f, 0.25f);
                case FurnitureKind.Chair:
                case FurnitureKind.Stool: return new Color(0.69f, 0.45f, 0.29f);
                case FurnitureKind.Counter: return new Color(0.77f, 0.58f, 0.39f);
                case FurnitureKind.Door: return new Color(0.35f, 0.5f, 0.36f);
                default: return new Color(0.33f, 0.34f, 0.38f);
            }
        }

        /// Procedural diamond sprite (real generated texture, not a bundled asset).
        private static Sprite DiamondSprite(int w, int h, Color color)
        {
            string key = w + "x" + h + ":" + color;
            Sprite cached;
            if (spriteCache.TryGetValue(key, out cached) && cached != null) return cached;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            float hw = w / 2f, hh = h / 2f;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float nx = Mathf.Abs(x + 0.5f - hw) / hw;
                    float ny = Mathf.Abs(y + 0.5f - hh) / hh;
                    tex.SetPixel(x, y, nx + ny <= 1f ? color : Color.clear);
                }
            }
            tex.Apply();
            var sp = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 1f);
            spriteCache[key] = sp;
            return sp;
        }
    }
}
