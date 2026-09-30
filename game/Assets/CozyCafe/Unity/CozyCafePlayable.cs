using System;
using System.Collections.Generic;
using System.IO;
using CozyCafe.Core;
using CozyCafe.Core.Economy;
using CozyCafe.Core.Integration;
using CozyCafe.Core.Iso;
using CozyCafe.Core.Layout;
using CozyCafe.Core.Modules;
using CozyCafe.Core.Save;
using CozyCafe.Core.Scene;
using CozyCafe.Core.Staff;
using CozyCafe.Core.Tools;
using CozyCafe.Core.Ugc;
using UnityEngine;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System.Runtime.InteropServices;
#endif

namespace CozyCafe.Unity
{
    /// <summary>
    /// Playable cafe shell on the verified session path: IntegrationModule.Boot
    /// builds a real CafeSession (economy + research + staff + layout wired the
    /// same way the 25-minute unattended integration run was verified), and the
    /// shell adds the interactive layer the gate never covered — per-frame
    /// session.Advance ticking, mouse placement/move/rotate/remove/tile editing
    /// with undo groups, walking customers paying through real SettleSale,
    /// offline settlement via SaveDocument/SettleOffline, desktop tools
    /// (memo/todo/focus timer/local music deck), shop panels, UGC import and
    /// window controls. All state persists to persistentDataPath.
    /// </summary>
    public sealed class CozyCafePlayable : MonoBehaviour
    {
        private IntegrationModule integ;
        private CafeSession session;
        private LayoutModule layout;
        private EconomyModule econ;
        private ToolsModule tools;
        private UgcModule ugc;
        private GameScene scene;
        private Camera cam;

        private int pendingKind = -1;   // >=0 FurnitureKind | -1 select/move | -2 remove | -3 tile add | -4 tile cut
        private int selectedId = -1;
        private int hoverX = -1, hoverY = -1;
        private string status = "";
        private string offlineMsg = "";
        private bool toolsOpen, shopOpen, ugcOpen, topmost, miniMode;
        private int tileEdits, achievements;
        private long salesSeen;

        private readonly List<Vector3> agentBase = new List<Vector3>();
        private readonly List<Transform> agentNodes = new List<Transform>();
        private readonly List<SpriteRenderer> espressoSrs = new List<SpriteRenderer>();
        private GameObject ghost;
        private float clock, espressoT, autosave;

        private readonly List<Customer> customers = new List<Customer>();
        private readonly HashSet<int> takenSeats = new HashSet<int>();
        private float nextSpawn = 6f;
        private int custSeq;
        private readonly System.Random custRng = new System.Random();

        private string memoText = "";
        private string todoDraft = "";
        private string customMin = "10";
        private Vector2 toolsScroll, shopScroll;
        private string ugcPath = "";
        private Vector2 ugcScroll;

        private AudioSource audio;
        private AudioClip[] clips;
        private int audioTrack = -1;

        private string SaveDir { get { return Application.persistentDataPath; } }
        private string SaveFile { get { return Path.Combine(SaveDir, "cozycafe_save.json"); } }

        private void Awake()
        {
            var data = MvpData.Load();
            integ = IntegrationModule.Boot(
                data, 20260930L, 8, 8,
                Path.Combine(SaveDir, "ugc"));
            session = integ.Session;
            layout = session.Layout;
            econ = session.Econ;
            tools = integ.Tools;
            ugc = integ.Ugc;
            scene = layout.Scene;

            RestoreAll();
            RebuildView();
            MakeGhost();
            SetupAudio();
            SyncStaffAgents();
            if (Environment.GetEnvironmentVariable("COZY_DEMO_PANELS") == "1")
            {
                toolsOpen = true; shopOpen = true; ugcOpen = false;
                if (!session.Staff.PanelOpen) session.Staff.OpenHirePanel();
            }
            if (Environment.GetEnvironmentVariable("COZY_SELFTEST") == "1")
                selftestAt = 3f;
        }

        private void Start()
        {
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.13f, 0.11f, 0.14f);
            FrameCamera();
        }

        private void FrameCamera()
        {
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

        private void Update()
        {
            float dt = Time.deltaTime;
            clock += dt;
            session.Advance(dt);          // verified boundary path: econ+research in order
            tools.TickTimer(dt);
            tools.Music.TickPlayback(dt);

            autosave += dt;
            if (autosave > 60f) { autosave = 0; SaveAll(); }

            UpdateHover();
            HandleInput();
            BobAgents();
            ZoomCamera();
            TickCustomers(dt);
            AnimateEspresso(dt);
            SyncAudio();

            if (selftestAt > 0f)
            {
                selftestAt -= dt;
                if (selftestAt <= 0f) RunSelfTest();
            }
        }

        // Exercises the same handler methods real input calls, inside the
        // real player, and records results. Synthetic input delivery, real
        // code paths — reported honestly as such.
        private float selftestAt;
        private void RunSelfTest()
        {
            var r = new List<string>();
            // tile paint via the real stroke path
            int u0 = layout.UndoDepth;
            hoverX = 0; hoverY = scene.Room.Height - 1;
            if (!scene.Room.HasCell(hoverX, hoverY))
            {
                TryPaintTile(true);
                EndTileStroke();
            }
            else
            {
                TryPaintTile(false);
                EndTileStroke();
            }
            r.Add("tile_paint_undo_delta=" + (layout.UndoDepth - u0));
            r.Add("tile_edit_count=" + tileEdits);
            // undo restores it through the real undo stack
            bool undid = layout.Undo();
            r.Add("undo=" + undid);
            // furniture place through the real command path — table+seat
            // must commit as one group or the whole thing rolls back
            int fx = -1, fy = 1;
            for (int y = 0; y < scene.Room.Height && fx < 0; y++)
                for (int x = 1; x < scene.Room.Width - 1 && fx < 0; x++)
                    if (scene.Room.HasCell(x, y) && scene.Room.HasCell(x - 1, y)
                        && layout.OccupyingAt(x, y) == null
                        && layout.OccupyingAt(x - 1, y) == null
                        && layout.IsWalkableCell(x, y)
                        && layout.IsWalkableCell(x - 1, y))
                    { fx = x; fy = y; }
            layout.BeginCommand();
            var pr = layout.TryPlace(FurnitureKind.Table, fx, fy, 0);
            var pr2 = layout.TryPlace(FurnitureKind.Stool, fx - 1, fy, 0);
            r.Add("place_table=" + pr.Ok + " seat=" + pr2.Ok +
                " at(" + fx + "," + fy + ")");
            var pend = layout.EndCommand();
            r.Add("commit=" + pend);
            if (pr.Ok && pend == PlacementReject.None)
            {
                var f = layout.OccupyingAt(fx, fy);
                if (f != null)
                {
                    var rr = layout.TryRotate(f.Id, 1);
                    r.Add("rotate=" + rr.Ok + ":" + rr.Reason);
                }
                // seat-first removal, then table — removing the table while
                // its seat remains must be refused by the layout invariant
                var seat = layout.OccupyingAt(fx - 1, fy);
                if (seat != null)
                    r.Add("remove_seat=" + layout.TryRemove(seat.Id).Ok);
                if (f != null)
                {
                    var dr = layout.TryRemove(f.Id);
                    r.Add("remove_table=" + dr.Ok + ":" + dr.Reason);
                }
            }
            // tools module through real calls
            int mid = tools.CreateMemo("셀프테스트 한글");
            r.Add("memo=" + (mid >= 0));
            int tid = tools.AddTodo("테스트 할 일");
            r.Add("todo=" + tools.CompleteTodo(tid));
            tools.Timer.StartCustom(60);
            r.Add("timer=" + (tools.Timer.State == TimerState.Running));
            tools.Timer.Abort();
            var deck = tools.Music;
            if (deck.Catalog.Count > 0)
            {
                deck.Select(0);
                deck.Play();
                deck.TickPlayback(0.5);
                r.Add("music=" + deck.IsPlaying);
                deck.PausePlayback();
            }
            // ugc import through the real draft->validate->save->apply path
            try
            {
                var png = MakeTestPng();
                ugc.BeginImport(png, "selftest_table");
                ugc.Assign("furniture_skin", 0.5, 0.9, "SE");
                ugc.BuildPreview(64, 64);
                var probs = ugc.Validate();
                r.Add("ugc_validate=" + (probs.Count == 0));
                if (probs.Count == 0)
                {
                    ugc.Save();
                    ugc.AddAppearance("table_square");
                    var inst = ugc.Apply(session);
                    r.Add("ugc_apply_placed=" + inst.PlacedIds.Count);
                }
            }
            catch (Exception e) { r.Add("ugc_error=" + e.GetType().Name); }
            // shop paths
            var stf = session.Staff;
            if (!stf.PanelOpen) stf.OpenHirePanel();
            r.Add("staff_candidates=" + stf.Candidates.Count);
            r.Add("staff_hire_panel=" + stf.PanelOpen);
            r.Add("research_defs=" + (econ.Data != null ? econ.Data.Research.Count : 0));
            r.Add("coins=" + econ.Coins);
            r.Add("sales=" + integ.SettledSaleCount());
            File.WriteAllLines(Path.Combine(SaveDir, "selftest.json"), r);
            status = "셀프테스트 완료";
            RebuildView();
        }

        private static byte[] MakeTestPng()
        {
            // minimal valid 8x8 PNG generated in-memory
            int w = 8, h = 8;
            var raw = new byte[h * (1 + w * 4)];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int o = y * (1 + w * 4) + 1 + x * 4;
                    raw[o] = 120; raw[o + 1] = 80; raw[o + 2] = 40; raw[o + 3] = 255;
                }
            using (var ms = new MemoryStream())
            {
                ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);
                WritePngChunk(ms, "IHDR", Ihdr(w, h));
                WritePngChunk(ms, "IDAT", ZlibWrap(raw));
                WritePngChunk(ms, "IEND", new byte[0]);
                return ms.ToArray();
            }
        }

        private static byte[] Ihdr(int w, int h)
        {
            var b = new byte[13];
            b[0] = (byte)(w >> 24); b[1] = (byte)(w >> 16); b[2] = (byte)(w >> 8); b[3] = (byte)w;
            b[4] = (byte)(h >> 24); b[5] = (byte)(h >> 16); b[6] = (byte)(h >> 8); b[7] = (byte)h;
            b[8] = 8; b[9] = 6;
            return b;
        }

        private static byte[] ZlibWrap(byte[] raw)
        {
            using (var ms = new MemoryStream())
            {
                ms.WriteByte(0x78); ms.WriteByte(0x01);
                var df = new System.IO.Compression.DeflateStream(
                    ms, System.IO.Compression.CompressionLevel.NoCompression, true);
                df.Write(raw, 0, raw.Length);
                df.Close();
                var data = ms.ToArray();
                uint a = Adler32(raw);
                var outb = new byte[data.Length + 4];
                Array.Copy(data, outb, data.Length);
                outb[data.Length] = (byte)(a >> 24);
                outb[data.Length + 1] = (byte)(a >> 16);
                outb[data.Length + 2] = (byte)(a >> 8);
                outb[data.Length + 3] = (byte)a;
                return outb;
            }
        }

        private static uint Adler32(byte[] d)
        {
            uint s1 = 1, s2 = 0;
            foreach (var b in d) { s1 = (s1 + b) % 65521; s2 = (s2 + s1) % 65521; }
            return (s2 << 16) | s1;
        }

        private static void WritePngChunk(Stream s, string tag, byte[] data)
        {
            var len = BitConverter.GetBytes(data.Length);
            if (BitConverter.IsLittleEndian) Array.Reverse(len);
            s.Write(len, 0, 4);
            var tb = System.Text.Encoding.ASCII.GetBytes(tag);
            s.Write(tb, 0, 4);
            s.Write(data, 0, data.Length);
            var crc = new byte[4];
            uint c = Crc32(tb, data);
            crc[0] = (byte)(c >> 24); crc[1] = (byte)(c >> 16);
            crc[2] = (byte)(c >> 8); crc[3] = (byte)c;
            s.Write(crc, 0, 4);
        }

        private static uint Crc32(byte[] tag, byte[] data)
        {
            uint c = 0xFFFFFFFF;
            foreach (var b in tag) c = CrcStep(c, b);
            foreach (var b in data) c = CrcStep(c, b);
            return c ^ 0xFFFFFFFF;
        }

        private static uint CrcStep(uint c, byte b)
        {
            c ^= b;
            for (int i = 0; i < 8; i++)
                c = (c & 1) != 0 ? (c >> 1) ^ 0xEDB88320 : c >> 1;
            return c;
        }

        // ---- input ----------------------------------------------------

        private void UpdateHover()
        {
            hoverX = -1; hoverY = -1;
            if (cam == null || scene.Room == null) return;
            Vector3 w = cam.ScreenToWorldPoint(Input.mousePosition);
            double gx, gy;
            IsoMath.Unproject(w.x, -w.y, out gx, out gy);
            int cx = Mathf.FloorToInt((float)gx);
            int cy = Mathf.FloorToInt((float)gy);
            if (scene.Room.InBounds(cx, cy)) { hoverX = cx; hoverY = cy; }
            UpdateGhost();
        }

        private void HandleInput()
        {
            if (!UiBlocked())
            {
                if (Input.GetMouseButtonDown(0) && hoverX >= 0)
                {
                    if (pendingKind >= 0) TryPlaceHere();
                    else if (pendingKind == -2) TryRemoveHere();
                    else if (pendingKind == -3 || pendingKind == -4)
                        TryPaintTile(pendingKind == -3);
                    else SelectOrMove();
                }
                if (Input.GetMouseButton(0) &&
                    (pendingKind == -3 || pendingKind == -4))
                {
                    if (hoverX >= 0) TryPaintTile(pendingKind == -3);
                }
                if (Input.GetMouseButtonDown(1)) { pendingKind = -1; selectedId = -1; }
            }

            bool ctrl = Input.GetKey(KeyCode.LeftControl) ||
                        Input.GetKey(KeyCode.RightControl) ||
                        Input.GetKey(KeyCode.LeftCommand);
            bool typing = MemoFocused();
            if (ctrl && Input.GetKeyDown(KeyCode.Z) && !typing)
            {
                if (layout.Undo()) { RebuildView(); status = "실행 취소"; }
                else status = "되돌릴 항목 없음";
            }
            if (ctrl && Input.GetKeyDown(KeyCode.Y) && !typing)
            {
                if (layout.Redo()) { RebuildView(); status = "다시 실행"; }
                else status = "다시 실행할 항목 없음";
            }
            if (Input.GetKeyDown(KeyCode.R) && selectedId >= 0)
            {
                var r = layout.TryRotate(selectedId, 1);
                if (r.Ok) { RebuildView(); status = "회전"; } else status = RejectText(r.Reason);
            }
            if ((Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.Backspace))
                && selectedId >= 0 && !typing)
            {
                var r = layout.TryRemove(selectedId);
                if (r.Ok) { selectedId = -1; RebuildView(); status = "삭제"; }
                else status = RejectText(r.Reason);
            }
            if (Input.GetKeyDown(KeyCode.T)) toolsOpen = !toolsOpen;
            if (Input.GetKeyDown(KeyCode.B)) shopOpen = !shopOpen;
            if (Input.GetKeyDown(KeyCode.U)) ugcOpen = !ugcOpen;
            if (Input.GetKeyDown(KeyCode.Escape)) { pendingKind = -1; selectedId = -1; }
        }

        private void SelectOrMove()
        {
            var f = layout.OccupyingAt(hoverX, hoverY);
            if (selectedId >= 0 && (f == null || f.Id != selectedId))
            {
                var r = layout.TryMove(selectedId, hoverX, hoverY);
                if (r.Ok) { RebuildView(); status = "이동"; return; }
                status = RejectText(r.Reason);
            }
            selectedId = f != null ? f.Id : -1;
            if (f != null) status = "선택: " + f.Kind + " #" + f.Id;
        }

        private void TryPlaceHere()
        {
            layout.BeginCommand();
            var r = layout.TryPlace((FurnitureKind)pendingKind, hoverX, hoverY, 0);
            var end = layout.EndCommand();
            if (r.Ok && end == PlacementReject.None)
            {
                RebuildView();
                status = "배치: " + (FurnitureKind)pendingKind;
            }
            else status = RejectText(r.Ok ? end : r.Reason);
        }

        private void TryRemoveHere()
        {
            var f = layout.OccupyingAt(hoverX, hoverY);
            if (f == null) { status = "가구 없음"; return; }
            var r = layout.TryRemove(f.Id);
            if (r.Ok) { RebuildView(); status = "삭제: " + f.Kind; }
            else status = RejectText(r.Reason);
        }

        private void TryPaintTile(bool present)
        {
            if (layout.CommandOpen == false) layout.BeginCommand();
            var r = layout.PaintTile(hoverX, hoverY, present);
            if (!r.Ok) return;
            tileEdits++;
        }

        private void EndTileStroke()
        {
            if (layout.CommandOpen)
            {
                var end = layout.EndCommand();
                if (end == PlacementReject.None) { RebuildView(); status = "타일 편집"; }
            }
        }

        private static string RejectText(PlacementReject r)
        {
            return "불가 (" + r + ")";
        }

        // ---- view -----------------------------------------------------

        private void RebuildView()
        {
            var old = transform.Find("CozyCafeStageView");
            if (old != null) Destroy(old.gameObject);
            StageViewBuilder.Build(scene).transform.SetParent(transform, false);
            CollectAgents();
        }

        private void CollectAgents()
        {
            agentNodes.Clear();
            agentBase.Clear();
            espressoSrs.Clear();
            var view = transform.Find("CozyCafeStageView");
            if (view == null) return;
            foreach (Transform child in view)
            {
                if (child.name.StartsWith("agent_", StringComparison.Ordinal))
                {
                    agentNodes.Add(child);
                    agentBase.Add(child.position);
                }
                else if (child.name.StartsWith("furn_EspressoMachine"))
                {
                    var r = child.GetComponent<SpriteRenderer>();
                    if (r != null) espressoSrs.Add(r);
                }
            }
        }

        private void SyncStaffAgents()
        {
            scene.Agents.Clear();
            int i = 0;
            foreach (var c in session.Staff.Roster)
            {
                scene.Agents.Add(new Agent
                {
                    Name = "staff_" + i,
                    PresetId = c.AppearanceSeed,
                    GridX = 1.5 + (i % 3),
                    GridY = 1.5,
                    IsStaff = true
                });
                i++;
            }
            if (scene.Agents.Count == 0)
                scene.Agents.Add(new Agent
                {
                    Name = "staff_0", PresetId = 0, GridX = 2.5, GridY = 2.5, IsStaff = true
                });
            RebuildView();
        }

        private void BobAgents()
        {
            for (int i = 0; i < agentNodes.Count; i++)
            {
                if (agentNodes[i] == null) continue;
                var p = agentBase[i];
                p.y += Mathf.Sin(clock * 2.4f + i * 1.7f) * 0.035f;
                p.x += Mathf.Cos(clock * 1.1f + i * 2.3f) * 0.02f;
                agentNodes[i].position = p;
            }
        }

        private void ZoomCamera()
        {
            if (cam == null) return;
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.001f)
                cam.orthographicSize = Mathf.Clamp(
                    cam.orthographicSize * (1f - scroll * 0.25f), 40f, 900f);
        }

        private void MakeGhost()
        {
            ghost = new GameObject("ghost_cell");
            var sr = ghost.AddComponent<SpriteRenderer>();
            sr.sprite = Diamond(64, 32, new Color(1f, 1f, 1f, 0.35f));
            sr.sortingOrder = 50;
            ghost.SetActive(false);
        }

        private void UpdateGhost()
        {
            if (ghost == null) return;
            bool show = hoverX >= 0;
            ghost.SetActive(show);
            if (!show) return;
            double sx, sy;
            IsoMath.Project(hoverX + 0.5, hoverY + 0.5, out sx, out sy);
            ghost.transform.position = new Vector3((float)sx, (float)-sy, 0f);
            var sr = ghost.GetComponent<SpriteRenderer>();
            sr.color = pendingKind == -1 ? new Color(1f, 1f, 1f, 0.25f)
                     : pendingKind == -2 ? new Color(1f, 0.3f, 0.3f, 0.4f)
                     : pendingKind == -3 ? new Color(0.5f, 0.8f, 1f, 0.4f)
                     : pendingKind == -4 ? new Color(1f, 0.6f, 0.2f, 0.4f)
                     : new Color(0.4f, 1f, 0.5f, 0.4f);
        }

        private static Sprite Diamond(int w, int h, Color color)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    double nx = Math.Abs(x - w / 2.0 + 0.5) / (w / 2.0);
                    double ny = Math.Abs(y - h / 2.0 + 0.5) / (h / 2.0);
                    px[y * w + x] = (nx + ny <= 1.0)
                        ? (Color32)color : new Color32(0, 0, 0, 0);
                }
            tex.SetPixels32(px);
            tex.filterMode = FilterMode.Point;
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h),
                new Vector2(0.5f, 0.5f), 1f);
        }

        // ---- customers ------------------------------------------------

        private sealed class Customer
        {
            public GameObject Go;
            public List<int> Path;
            public int PathIdx;
            public float GX, GY;
            public int SeatId = -1;
            public int State;           // 0 walk-in, 1 sitting, 2 leaving
            public float SitLeft;
            public bool Paid;
        }

        private void TickCustomers(float dt)
        {
            nextSpawn -= dt;
            if (nextSpawn <= 0f)
            {
                nextSpawn = 7f + (custSeq % 3) * 3f;
                TrySpawnCustomer();
            }
            var room = scene.Room;
            int w = room.Width;
            for (int i = customers.Count - 1; i >= 0; i--)
            {
                var c = customers[i];
                if (c.State == 1)
                {
                    c.SitLeft -= dt;
                    if (c.SitLeft <= 0f) PayAndLeave(c);
                    continue;
                }
                if (c.Path == null || c.PathIdx >= c.Path.Count)
                {
                    if (c.State == 0) { c.State = 1; c.SitLeft = 6f + (custSeq % 4) * 2f; }
                    else Despawn(c, i);
                    continue;
                }
                int cell = c.Path[c.PathIdx];
                float tx = (cell % w) + 0.5f, ty = (cell / w) + 0.5f;
                float dx = tx - c.GX, dy = ty - c.GY;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float step = 2.2f * dt;
                if (dist <= step) { c.GX = tx; c.GY = ty; c.PathIdx++; }
                else { c.GX += dx / dist * step; c.GY += dy / dist * step; }
                double sx, sy;
                IsoMath.Project(c.GX, c.GY, out sx, out sy);
                c.Go.transform.position = new Vector3(
                    (float)sx, (float)-(sy - 9), 0f);
            }
        }

        private int[] SpawnCell()
        {
            foreach (var f in scene.Furniture)
            {
                if (f.Kind != FurnitureKind.Door) continue;
                if (layout.IsWalkableCell(f.CellX, f.CellY))
                    return new[] { f.CellX, f.CellY };
                int[] dx = { 1, -1, 0, 0 }, dy = { 0, 0, 1, -1 };
                for (int k = 0; k < 4; k++)
                    if (layout.IsWalkableCell(f.CellX + dx[k], f.CellY + dy[k]))
                        return new[] { f.CellX + dx[k], f.CellY + dy[k] };
            }
            for (int y = 0; y < scene.Room.Height; y++)
                for (int x = 0; x < scene.Room.Width; x++)
                    if (layout.IsWalkableCell(x, y)) return new[] { x, y };
            return null;
        }

        private void TrySpawnCustomer()
        {
            if (customers.Count >= 3) return;
            var s = SpawnCell();
            if (s == null) return;
            int sx = s[0], sy = s[1];

            Furniture seat = null; List<int> seatPath = null;
            int[] ndx = { 1, -1, 0, 0 }, ndy = { 0, 0, 1, -1 };
            foreach (var f in scene.Furniture)
            {
                if (!LayoutModule.IsSeat(f.Kind) || takenSeats.Contains(f.Id)) continue;
                for (int k = 0; k < 4; k++)
                {
                    int nx = f.CellX + ndx[k], ny = f.CellY + ndy[k];
                    if (!layout.IsWalkableCell(nx, ny)) continue;
                    var p = layout.FindPath(sx, sy, nx, ny);
                    if (p != null && p.Count > 0) { seat = f; seatPath = p; break; }
                }
                if (seat != null) break;
            }
            if (seat == null) return;
            takenSeats.Add(seat.Id);

            var c = new Customer();
            c.SeatId = seat.Id;
            c.GX = sx + 0.5f; c.GY = sy + 0.5f;
            c.Path = seatPath;
            var go = new GameObject("cust_" + custSeq++);
            var sr = go.AddComponent<SpriteRenderer>();
            var art = StageViewBuilder.AtlasSprite("body_anchor");
            sr.sprite = art != null ? art
                : Diamond(12, 18, new Color(0.89f, 0.57f, 0.36f));
            // per-customer palette tint — seeded variation, no re-roll on save
            float hue = (float)custRng.NextDouble();
            sr.color = art != null
                ? new Color(0.7f + hue * 0.3f, 0.6f + hue * 0.3f,
                            0.55f + hue * 0.3f)
                : Color.white;
            sr.sortingOrder = 200;
            c.Go = go;
            customers.Add(c);
        }

        private void PayAndLeave(Customer c)
        {
            if (!c.Paid)
            {
                MenuLine line = null;
                foreach (var kv in econ.Lines)
                    if (econ.Owns(kv.Value.Def)) { line = kv.Value; break; }
                if (line != null)
                {
                    var ev = econ.IssueSaleEventId(line.Def.Id);
                    if (econ.SettleSale(ev, line.Def, line.Level))
                        status = "+" + line.Def.Price + "코인: " + line.Def.Name;
                }
                c.Paid = true;
            }
            var room = scene.Room;
            int w = room.Width;
            int cx = Mathf.Clamp((int)(c.GX - 0.5f), 0, w - 1);
            int cy = Mathf.Clamp((int)(c.GY - 0.5f), 0, room.Height - 1);
            var s = SpawnCell() ?? new[] { 0, 0 };
            c.Path = layout.FindPath(cx, cy, s[0], s[1]) ?? new List<int>();
            c.PathIdx = 0;
            c.State = 2;
        }

        private void Despawn(Customer c, int i)
        {
            if (c.SeatId >= 0) takenSeats.Remove(c.SeatId);
            if (c.Go != null) Destroy(c.Go);
            customers.RemoveAt(i);
        }

        // ---- espresso frames + audio -----------------------------------

        private void AnimateEspresso(float dt)
        {
            if (espressoSrs.Count == 0) return;
            espressoT += dt;
            if (espressoT < 0.55f) return;
            espressoT = 0f;
            var a = StageViewBuilder.AtlasSprite("machine_espresso_0");
            var b = StageViewBuilder.AtlasSprite("machine_espresso_1");
            if (a == null || b == null) return;
            foreach (var sr in espressoSrs)
                if (sr != null) sr.sprite = sr.sprite == a ? b : a;
        }

        private void SetupAudio()
        {
            audio = gameObject.AddComponent<AudioSource>();
            audio.loop = true;
            audio.spatialBlend = 0f;
            clips = new AudioClip[]
            {
                MakePadClip("카페 패드", 12f),
                MakeMusicBoxClip("뮤직박스", 12f),
                MakeRainClip("빗소리", 8f)
            };
        }

        private void SyncAudio()
        {
            if (tools == null || audio == null || clips == null) return;
            var deck = tools.Music;
            audio.volume = (float)deck.Volume * 0.6f;
            int want = deck.CurrentIndex;
            if (deck.IsPlaying && want >= 0)
            {
                if (audioTrack != want || !audio.isPlaying)
                {
                    audio.clip = clips[want % clips.Length];
                    audio.Play();
                    audioTrack = want;
                }
            }
            else if (audio.isPlaying) audio.Stop();
        }

        private static AudioClip MakePadClip(string name, float seconds)
        {
            int sr = 22050, n = (int)(sr * seconds);
            var data = new float[n];
            float[][] chords = {
                new[]{261.63f,329.63f,392.00f,493.88f},
                new[]{220.00f,261.63f,329.63f,392.00f},
                new[]{174.61f,220.00f,261.63f,349.23f},
                new[]{196.00f,246.94f,293.66f,392.00f} };
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                var ch = chords[(int)(t / (seconds / 4)) % 4];
                float v = 0;
                foreach (var f in ch)
                    v += Mathf.Sin(2 * Mathf.PI * f * t) * 0.10f
                       + Mathf.Sin(2 * Mathf.PI * f * 2 * t) * 0.03f;
                v *= 0.75f + 0.25f * Mathf.Sin(2 * Mathf.PI * 0.5f * t);
                float edge = Mathf.Min(1f, Mathf.Min(i, n - i) / (sr * 0.4f));
                data[i] = v * edge;
            }
            var clip = AudioClip.Create(name, n, 1, sr, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip MakeMusicBoxClip(string name, float seconds)
        {
            int sr = 22050, n = (int)(sr * seconds);
            var data = new float[n];
            float[] notes = { 523.25f, 659.25f, 783.99f, 880.00f,
                              783.99f, 659.25f, 587.33f, 523.25f };
            int per = n / 8;
            for (int k = 0; k < 8; k++)
            {
                float f = notes[k];
                for (int i = 0; i < per && k * per + i < n; i++)
                {
                    float t = i / (float)sr;
                    data[k * per + i] +=
                        Mathf.Sin(2 * Mathf.PI * f * t) *
                        Mathf.Exp(-t * 4f) * 0.25f;
                }
            }
            var clip = AudioClip.Create(name, n, 1, sr, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip MakeRainClip(string name, float seconds)
        {
            int sr = 22050, n = (int)(sr * seconds);
            var data = new float[n];
            var rng = new System.Random(7);
            float last = 0;
            for (int i = 0; i < n; i++)
            {
                float white = (float)(rng.NextDouble() * 2 - 1);
                last = last * 0.985f + white * 0.05f;
                data[i] = last * 0.8f;
            }
            for (int i = 0; i < sr / 20; i++)
            {
                float f = i / (float)(sr / 20);
                data[i] *= f;
                data[n - 1 - i] *= f;
            }
            var clip = AudioClip.Create(name, n, 1, sr, false);
            clip.SetData(data, 0);
            return clip;
        }

        // ---- persistence + offline settlement ---------------------------

        private void SaveAll()
        {
            try
            {
                var store = new SaveStore(SaveFile);
                session.WriteCheckpoint(store, NowUtc());
                File.WriteAllText(
                    Path.Combine(SaveDir, "tools.json"), tools.SaveTools());
                File.WriteAllText(Path.Combine(SaveDir, "achievements.json"),
                    "{\"tiles\":" + tileEdits + ",\"sales\":" +
                    Math.Max(salesSeen, integ.SettledSaleCount()) +
                    ",\"ach\":" + achievements + "}");
            }
            catch (Exception e) { status = "저장 실패: " + e.Message; }
        }

        private void RestoreAll()
        {
            try
            {
                var store = new SaveStore(SaveFile);
                string text;
                if (store.TryRead(out text) == SaveStore.LoadSource.None)
                    return;
                var doc = SaveDocument.Parse(text);
                session.Restore(doc);
                var off = session.SettleOffline(doc, NowUtc());
                if (off.Seconds > 0)
                    offlineMsg = "오프라인 정산 +" + off.CoinsGained + "코인 ("
                        + TimeSpan.FromSeconds(off.Seconds).ToString(@"h\:mm") + ")";
                var tf = Path.Combine(SaveDir, "tools.json");
                if (File.Exists(tf))
                    tools.LoadTools(File.ReadAllText(tf));
                var af = Path.Combine(SaveDir, "achievements.json");
                if (File.Exists(af))
                {
                    var j = File.ReadAllText(af);
                    tileEdits = IntField(j, "tiles");
                    achievements = IntField(j, "ach");
                    salesSeen = IntField(j, "sales");
                }
            }
            catch (Exception e) { status = "복원 실패: " + e.Message; }
        }

        private static int IntField(string json, string key)
        {
            var m = System.Text.RegularExpressions.Regex.Match(
                json, "\"" + key + "\"\\s*:\\s*(\\d+)");
            return m.Success ? int.Parse(m.Groups[1].Value) : 0;
        }

        private static long NowUtc()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        private void OnApplicationQuit()
        {
            tools.OnSystemExit();
            SaveAll();
        }

        // ---- window controls (Windows) ---------------------------------

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
        [DllImport("user32.dll")] private static extern bool SetWindowPos(
            IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(
            IntPtr h, out RECT r);
        private struct RECT { public int L, T, R, B; }
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        private const uint SWP_NOSIZE = 1, SWP_NOMOVE = 2;
        private RECT prevRect;
#endif

        private void SetTopmost(bool on)
        {
            topmost = on;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            try
            {
                var h = GetActiveWindow();
                SetWindowPos(h, on ? HWND_TOPMOST : HWND_NOTOPMOST,
                    0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE);
            }
            catch { }
#endif
        }

        private void SetMini(bool on)
        {
            miniMode = on;
            if (tools != null)
                tools.SetMode(on ? WindowMode.Mini : WindowMode.Normal);
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            try
            {
                var h = GetActiveWindow();
                if (on)
                {
                    GetWindowRect(h, out prevRect);
                    SetWindowPos(h, IntPtr.Zero, 60, 60, 560, 400, 0);
                    SetTopmost(true);
                }
                else
                {
                    SetWindowPos(h, IntPtr.Zero,
                        prevRect.L, prevRect.T,
                        prevRect.R - prevRect.L, prevRect.B - prevRect.T, 0);
                    SetTopmost(false);
                }
            }
            catch { }
#endif
        }

        // ---- IMGUI overlay ---------------------------------------------

        private bool UiBlocked()
        {
            Vector2 m = new Vector2(Input.mousePosition.x,
                Screen.height - Input.mousePosition.y);
            if (m.y < 64) return true;
            if (toolsOpen && m.x > Screen.width - 280) return true;
            if (shopOpen && m.x < 300) return true;
            if (ugcOpen && m.x > Screen.width - 280) return true;
            return false;
        }

        private bool MemoFocused()
        {
            var n = GUI.GetNameOfFocusedControl();
            return n == "memo" || n == "todo" || n == "ugcpath" || n == "cmin";
        }

        private GUIStyle labelStyle, btnStyle, areaStyle;
        private void EnsureStyles()
        {
            if (labelStyle != null) return;
            Font f = null;
            var prefs = new[] { "Malgun Gothic", "Apple SD Gothic Neo",
                                "NanumGothic", "Arial Unicode MS" };
            var installed = new List<string>(Font.GetOSInstalledFontNames());
            foreach (var p in prefs)
                if (installed.Contains(p))
                { f = Font.CreateDynamicFontFromOSFont(p, 15); break; }
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 15 };
            btnStyle = new GUIStyle(GUI.skin.button) { fontSize = 14 };
            areaStyle = new GUIStyle(GUI.skin.textArea) { fontSize = 14 };
            if (f != null) { labelStyle.font = f; btnStyle.font = f; areaStyle.font = f; }
        }

        private void OnGUI()
        {
            if (Input.GetMouseButtonUp(0)) EndTileStroke();
            EnsureStyles();
            DrawToolbar();
            DrawTools();
            DrawShop();
            DrawUgc();
            if (offlineMsg.Length > 0) DrawOfflineBanner();
        }

        private void DrawOfflineBanner()
        {
            GUI.Box(new Rect(Screen.width / 2f - 190, 72, 380, 34), GUIContent.none);
            GUI.Label(new Rect(Screen.width / 2f - 180, 78, 360, 24),
                offlineMsg, labelStyle);
            if (GUI.Button(new Rect(Screen.width / 2f + 158, 76, 26, 26), "x",
                btnStyle)) offlineMsg = "";
        }

        private void DrawToolbar()
        {
            GUILayout.BeginArea(new Rect(0, 0, Screen.width, 64), GUI.skin.box);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("선택", Btn(pendingKind == -1),
                GUILayout.Width(56))) { pendingKind = -1; }
            foreach (var k in new[] { FurnitureKind.Table, FurnitureKind.Chair,
                     FurnitureKind.Stool, FurnitureKind.Counter,
                     FurnitureKind.EspressoMachine, FurnitureKind.Door })
            {
                if (GUILayout.Button(KindName(k),
                    Btn(pendingKind == (int)k), GUILayout.Width(74)))
                    pendingKind = (int)k;
            }
            if (GUILayout.Button("삭제", Btn(pendingKind == -2),
                GUILayout.Width(48))) pendingKind = -2;
            if (GUILayout.Button("타일+", Btn(pendingKind == -3),
                GUILayout.Width(52))) pendingKind = -3;
            if (GUILayout.Button("타일-", Btn(pendingKind == -4),
                GUILayout.Width(52))) pendingKind = -4;
            GUILayout.Space(8);
            if (GUILayout.Button("상점(B)", Btn(shopOpen), GUILayout.Width(60)))
                shopOpen = !shopOpen;
            if (GUILayout.Button("도구(T)", Btn(toolsOpen), GUILayout.Width(60)))
                toolsOpen = !toolsOpen;
            if (GUILayout.Button("창작(U)", Btn(ugcOpen), GUILayout.Width(60)))
                ugcOpen = !ugcOpen;
            if (GUILayout.Button("미니", Btn(miniMode), GUILayout.Width(46)))
                SetMini(!miniMode);
            if (GUILayout.Button("위", Btn(topmost), GUILayout.Width(40)))
                SetTopmost(!topmost);
            if (GUILayout.Button("저장", btnStyle, GUILayout.Width(48)))
                SaveAll();
            GUILayout.EndHorizontal();

            string sel = selectedId >= 0 ? "선택#" + selectedId : "-";
            long coins = econ != null ? econ.Coins : 0;
            GUILayout.Label(
                "코인 " + coins +
                "   시간 " + TimeSpan.FromSeconds(session.RuntimeClock)
                    .ToString(@"hh\:mm") +
                "   셀(" + hoverX + "," + hoverY + ")  가구 " + sel +
                "   undo " + layout.UndoDepth + "/" + layout.RedoDepth +
                "   판매 " + integ.SettledSaleCount() +
                "   " + status, labelStyle);
            GUILayout.EndArea();
        }

        private GUIStyle Btn(bool on)
        {
            if (!on) return btnStyle;
            var s = new GUIStyle(btnStyle);
            s.normal.textColor = new Color(0.4f, 1f, 0.6f);
            return s;
        }

        private static string KindName(FurnitureKind k)
        {
            switch (k)
            {
                case FurnitureKind.Table: return "테이블";
                case FurnitureKind.Chair: return "의자";
                case FurnitureKind.Stool: return "스툴";
                case FurnitureKind.Counter: return "카운터";
                case FurnitureKind.EspressoMachine: return "에스프레소";
                case FurnitureKind.Door: return "문";
                default: return k.ToString();
            }
        }

        private void DrawTools()
        {
            if (!toolsOpen || tools == null) return;
            GUILayout.BeginArea(
                new Rect(Screen.width - 280, 64, 280, Screen.height - 64),
                GUI.skin.box);
            toolsScroll = GUILayout.BeginScrollView(toolsScroll);

            GUILayout.Label("-- 메모 (한글) --", labelStyle);
            GUI.SetNextControlName("memo");
            string nt = GUILayout.TextArea(memoText, areaStyle,
                GUILayout.Height(64));
            if (nt != memoText)
            {
                memoText = nt;
                var m = tools.Memos.Count > 0 ? tools.Memos[0] : null;
                if (m == null) tools.CreateMemo(memoText);
                else tools.SetMemoText(m.Id, memoText);
            }

            GUILayout.Label("-- 할 일 --", labelStyle);
            var todoList = new List<TodoItem>(tools.Todos);
            for (int i = 0; i < todoList.Count; i++)
            {
                var t = todoList[i];
                GUILayout.BeginHorizontal();
                bool done = GUILayout.Toggle(t.Done, t.Text, labelStyle,
                    GUILayout.Width(150));
                if (done != t.Done) tools.SetTodoDone(t.Id, done);
                if (GUILayout.Button("↑", btnStyle, GUILayout.Width(26)) && i > 0)
                    tools.MoveTodo(t.Id, i - 1);
                if (GUILayout.Button("↓", btnStyle, GUILayout.Width(26))
                    && i < todoList.Count - 1)
                    tools.MoveTodo(t.Id, i + 1);
                if (GUILayout.Button("x", btnStyle, GUILayout.Width(24)))
                    tools.RemoveTodo(t.Id);
                GUILayout.EndHorizontal();
            }
            GUILayout.BeginHorizontal();
            GUI.SetNextControlName("todo");
            todoDraft = GUILayout.TextField(todoDraft, GUILayout.Width(200));
            if (GUILayout.Button("+", btnStyle, GUILayout.Width(28))
                && todoDraft.Length > 0)
            {
                tools.AddTodo(todoDraft);
                todoDraft = "";
            }
            GUILayout.EndHorizontal();

            GUILayout.Label("-- 집중 타이머 --", labelStyle);
            var ft = tools.Timer;
            GUILayout.Label(ft.State + "  " +
                TimeSpan.FromSeconds(ft.RemainingSeconds).ToString(@"mm\:ss") +
                "   총 집중 " +
                TimeSpan.FromSeconds(tools.TotalFocusSeconds).ToString(@"hh\:mm"),
                labelStyle);
            GUILayout.BeginHorizontal();
            if (ft.State == TimerState.Running)
            {
                if (GUILayout.Button("일시정지", btnStyle)) ft.Pause();
            }
            else if (ft.State == TimerState.Paused)
            {
                if (GUILayout.Button("재개", btnStyle)) ft.Resume();
            }
            else
            {
                if (GUILayout.Button("집중25", btnStyle)) ft.StartFocus();
                if (GUILayout.Button("휴식5", btnStyle)) ft.StartBreak();
            }
            if (GUILayout.Button("정지", btnStyle)) tools.StopTimer();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUI.SetNextControlName("cmin");
            customMin = GUILayout.TextField(customMin, GUILayout.Width(50));
            double mins;
            if (GUILayout.Button("사용자(분)", btnStyle) &&
                double.TryParse(customMin, out mins) && mins > 0)
                ft.StartCustom(mins * 60);
            GUILayout.EndHorizontal();

            GUILayout.Label("-- 음악 --", labelStyle);
            var deck = tools.Music;
            var cur = deck.Current;
            GUILayout.Label((cur != null ? cur.Title : "(트랙 없음)") +
                (deck.IsPlaying ? "  ▶" : "  ‖"), labelStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("|<", btnStyle, GUILayout.Width(34))) deck.Previous();
            if (GUILayout.Button(deck.IsPlaying ? "일시정지" : "재생",
                btnStyle, GUILayout.Width(60)))
            {
                if (deck.IsPlaying) deck.PausePlayback(); else deck.Play();
            }
            if (GUILayout.Button(">|", btnStyle, GUILayout.Width(34))) deck.Next();
            GUILayout.EndHorizontal();
            float v = GUILayout.HorizontalSlider((float)deck.Volume, 0f, 1f);
            if (Mathf.Abs(v - (float)deck.Volume) > 0.001f) deck.SetVolume(v);
            for (int i = 0; i < deck.Catalog.Count; i++)
            {
                var t = deck.Catalog[i];
                if (GUILayout.Button((i == deck.CurrentIndex ? "● " : "") +
                    t.Title, labelStyle)) deck.Select(i);
            }

            GUILayout.Label("-- 도전과제 --", labelStyle);
            long sales = Math.Max(salesSeen, integ.SettledSaleCount());
            Achievement("판매 100회", sales >= 100);
            Achievement("연구 1건", session.Lab.CompletedCount >= 1);
            Achievement("타일 편집", tileEdits >= 1);

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private readonly HashSet<string> unlockedAch = new HashSet<string>();

        private void Achievement(string name, bool done)
        {
            if (done && unlockedAch.Add(name))
            {
                achievements++;
                status = "도전과제 달성: " + name;
            }
            GUILayout.Label((done ? "★ " : "☆ ") + name, labelStyle);
        }

        private void DrawShop()
        {
            if (!shopOpen || econ == null) return;
            GUILayout.BeginArea(
                new Rect(0, 64, 300, Screen.height - 64), GUI.skin.box);
            shopScroll = GUILayout.BeginScrollView(shopScroll);

            GUILayout.Label("-- 메뉴 업그레이드 --", labelStyle);
            foreach (var kv in econ.Lines)
            {
                var l = kv.Value;
                GUILayout.BeginHorizontal();
                GUILayout.Label(l.Def.Name + " Lv" + l.Level,
                    labelStyle, GUILayout.Width(150));
                long cost = econ.UpgradeCost(l.Def, l.Level);
                if (GUILayout.Button("+" + cost, btnStyle, GUILayout.Width(90)))
                    status = econ.PurchaseUpgrade(l.Def, 1)
                        ? "업그레이드: " + l.Def.Name : "코인 부족";
                GUILayout.EndHorizontal();
            }

            GUILayout.Label("-- 연구 --", labelStyle);
            if (econ.Data != null)
            {
                var lab = session.Lab;
                if (lab.Active != null)
                    GUILayout.Label("진행: " + lab.Active.Name + " " +
                        TimeSpan.FromSeconds(lab.ActiveRemainingWork)
                            .ToString(@"mm\:ss"), labelStyle);
                foreach (var r in econ.Data.Research)
                {
                    if (lab.IsCompleted(r))
                    { GUILayout.Label("✓ " + r.Name, labelStyle); continue; }
                    bool queued = false;
                    foreach (var q in lab.Queue) if (q.Id == r.Id) queued = true;
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(r.Name + " (" + r.Cost + ")",
                        labelStyle, GUILayout.Width(170));
                    if (queued)
                        GUILayout.Label("대기", labelStyle, GUILayout.Width(60));
                    else if (GUILayout.Button("연구", btnStyle, GUILayout.Width(60)))
                        status = lab.Reserve(r.Id)
                            ? "연구 예약: " + r.Name : "예약 실패(선행/코인)";
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.Label("-- 직원 --", labelStyle);
            var stf = session.Staff;
            GUILayout.Label("고용 " + stf.Roster.Count + "명  판매+" +
                stf.TotalSalesBonusPct + "% 연구+" +
                stf.TotalResearchBonusPct + "%", labelStyle);
            if (!stf.PanelOpen)
            {
                if (GUILayout.Button("지원자 보기", btnStyle))
                    stf.OpenHirePanel();
            }
            else
            {
                for (int i = 0; i < stf.Candidates.Count; i++)
                {
                    var c = stf.Candidates[i];
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(c.Id + " 판매+" + c.SalesBonusPct +
                        "% 연구+" + c.ResearchBonusPct + "%",
                        labelStyle, GUILayout.Width(180));
                    int idx = i;
                    if (GUILayout.Button("고용", btnStyle, GUILayout.Width(56)))
                    {
                        status = stf.Hire(idx)
                            ? "고용: " + c.Id : "고용 실패(코인/정원)";
                        econ.StaffSalesBonusPct = stf.TotalSalesBonusPct;
                        session.Lab.SetWorkRate(
                            1.0 + stf.TotalResearchBonusPct / 100.0);
                        SyncStaffAgents();
                    }
                    GUILayout.EndHorizontal();
                }
                if (GUILayout.Button("닫기", btnStyle)) stf.CloseHirePanel();
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawUgc()
        {
            if (!ugcOpen || ugc == null) return;
            GUILayout.BeginArea(
                new Rect(Screen.width - 280, 64, 280, Screen.height - 64),
                GUI.skin.box);
            ugcScroll = GUILayout.BeginScrollView(ugcScroll);
            GUILayout.Label("-- 로컬 창작 (PNG 가져오기) --", labelStyle);
            GUILayout.Label("PNG 경로:", labelStyle);
            GUI.SetNextControlName("ugcpath");
            ugcPath = GUILayout.TextField(ugcPath, GUILayout.Width(250));
            GUILayout.BeginHorizontal();
            GUILayout.Label("역할 " + ugcRoles[ugcRoleIdx] + "  방향 " +
                ugcDirs[ugcDirIdx], labelStyle, GUILayout.Width(160));
            if (GUILayout.Button("역할", btnStyle, GUILayout.Width(44)))
                ugcRoleIdx = (ugcRoleIdx + 1) % ugcRoles.Length;
            if (GUILayout.Button("방향", btnStyle, GUILayout.Width(44)))
                ugcDirIdx = (ugcDirIdx + 1) % ugcDirs.Length;
            GUILayout.EndHorizontal();
            if (GUILayout.Button("가져오기 → 적용", btnStyle))
                UgcImport();
            if (ugc.Draft != null)
            {
                GUILayout.Label("초안: " + ugc.Draft.Name +
                    "  " + (ugc.Draft.Preview != null ? "미리보기 OK" : ""),
                    labelStyle);
                foreach (var p in ugc.Draft.Problems)
                    GUILayout.Label("문제: " + p, labelStyle);
            }
            foreach (var kv in ugc.AppearanceByTarget)
                GUILayout.Label("적용: " + kv.Key + " → " + kv.Value,
                    labelStyle);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private static readonly string[] ugcRoles = {
            "furniture_skin", "decor_sprite", "floor_tile_skin" };
        private static readonly string[] ugcDirs = { "", "SE", "SW", "NE", "NW" };
        private int ugcRoleIdx, ugcDirIdx;

        private void UgcImport()
        {
            try
            {
                if (!File.Exists(ugcPath)) { status = "파일 없음"; return; }
                ugc.BeginImport(File.ReadAllBytes(ugcPath),
                    Path.GetFileNameWithoutExtension(ugcPath));
                ugc.Assign(ugcRoles[ugcRoleIdx], 0.5, 0.9,
                    ugcDirs[ugcDirIdx]);
                ugc.BuildPreview(64, 64);
                var problems = ugc.Validate();
                if (problems.Count > 0)
                {
                    status = "검증 실패: " + problems[0];
                    return;
                }
                var assetId = ugc.Save();
                ugc.AddAppearance("table_square");
                var inst = ugc.Apply(session);
                status = "창작 적용: " + assetId +
                    " (적용 " + inst.PlacedIds.Count + " 배치)";
            }
            catch (Exception e) { status = "창작 실패: " + e.Message; }
        }
    }
}
