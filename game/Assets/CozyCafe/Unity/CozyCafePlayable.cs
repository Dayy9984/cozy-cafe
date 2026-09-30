using System;
using System.Collections.Generic;
using System.IO;
using CozyCafe.Core;
using CozyCafe.Core.Layout;
using CozyCafe.Core.Economy;
using CozyCafe.Core.Iso;
using CozyCafe.Core.Modules;
using CozyCafe.Core.Research;
using CozyCafe.Core.Scene;
using CozyCafe.Core.Staff;
using CozyCafe.Core.Tools;
using UnityEngine;

namespace CozyCafe.Unity
{
    /// <summary>
    /// Playable cafe shell: boots the shared core (same code the gate host
    /// runs), builds the live isometric view, ticks the real simulation and
    /// economy every frame, and wires mouse/keyboard input to the layout
    /// editor APIs (place / move / rotate / remove / undo / redo) plus the
    /// desktop tools (memo / todo / focus timer / music deck). Saves layout
    /// and tools state to persistentDataPath on quit and restores on boot.
    /// </summary>
    public sealed class CozyCafePlayable : MonoBehaviour
    {
        private GameBootstrap boot;
        private LayoutModule layout;
        private EconomyModule econ;
        private SimulationModule sim;
        private ToolsModule tools;
        private ResearchModule research;
        private StaffModule staff;
        private Camera cam;

        private int pendingKind = -1;   // >=0: FurnitureKind to place; -1: select/move; -2: remove
        private int selectedId = -1;
        private int hoverX = -1, hoverY = -1;
        private string status = "";
        private bool toolsOpen;
        private bool shopOpen;

        private readonly List<Vector3> agentBase = new List<Vector3>();
        private readonly List<Transform> agentNodes = new List<Transform>();
        private GameObject ghost;
        private float clock;

        private string memoText = "";
        private string todoDraft = "";
        private Vector2 toolsScroll;
        private Vector2 shopScroll;

        // customers walking the real layout paths
        private readonly List<Customer> customers = new List<Customer>();
        private readonly HashSet<int> takenSeats = new HashSet<int>();
        private float nextSpawn = 6f;
        private int custSeq;

        // espresso machine 2-frame animation + music audio
        private readonly List<SpriteRenderer> espressoSrs = new List<SpriteRenderer>();
        private float espressoT;
        private AudioSource audio;
        private AudioClip[] clips;
        private int audioTrack = -1;

        private string SaveDir
        {
            get { return Application.persistentDataPath; }
        }

        private void Awake()
        {
            var bootstrap = gameObject.AddComponent<CozyCafeBootstrap>();
            // Bootstrap.Awake ran first (script order is fine: it builds view).
            boot = bootstrap.Boot;
            foreach (var m in boot.Registry.Modules)
            {
                if (m is LayoutModule lm) layout = lm;
                else if (m is EconomyModule em) econ = em;
                else if (m is SimulationModule sm) sim = sm;
                else if (m is ToolsModule tm) tools = tm;
                else if (m is ResearchModule rm) research = rm;
                else if (m is StaffModule stm) staff = stm;
            }
            LoadState();
            CollectAgents();
            MakeGhost();
            SetupAudio();
            if (Environment.GetEnvironmentVariable("COZY_DEMO_PANELS") == "1")
            {
                toolsOpen = true;
                shopOpen = true;
                if (staff != null && !staff.PanelOpen) staff.OpenHirePanel();
            }
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
            if (sim != null) sim.Tick(dt);
            if (econ != null && econ.Data != null) econ.SimulateSeconds(dt);
            if (research != null) research.SimulateSeconds(dt);
            if (tools != null)
            {
                tools.TickTimer(dt);
                tools.Music.TickPlayback(dt);
            }

            UpdateHover();
            HandleInput();
            BobAgents();
            ZoomCamera();
            TickCustomers(dt);
            AnimateEspresso(dt);
            SyncAudio();
        }

        // ---- input ----------------------------------------------------

        private void UpdateHover()
        {
            hoverX = -1; hoverY = -1;
            if (cam == null || boot.Scene.Room == null) return;
            Vector3 w = cam.ScreenToWorldPoint(Input.mousePosition);
            double gx, gy;
            IsoMath.Unproject(w.x, -w.y, out gx, out gy);
            int cx = Mathf.FloorToInt((float)gx);
            int cy = Mathf.FloorToInt((float)gy);
            if (boot.Scene.Room.HasCell(cx, cy)) { hoverX = cx; hoverY = cy; }
            UpdateGhost();
        }

        private void HandleInput()
        {
            if (UiBlocked()) { if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)) return; }

            if (Input.GetMouseButtonDown(0) && hoverX >= 0)
            {
                if (pendingKind >= 0) TryPlaceHere();
                else if (pendingKind == -2) TryRemoveHere();
                else SelectOrMove();
            }
            if (Input.GetMouseButtonDown(1)) { pendingKind = -1; selectedId = -1; }

            bool ctrl = Input.GetKey(KeyCode.LeftControl) ||
                        Input.GetKey(KeyCode.RightControl) ||
                        Input.GetKey(KeyCode.LeftCommand);
            if (ctrl && Input.GetKeyDown(KeyCode.Z) && MemoFocused() == false)
            {
                if (layout.Undo()) { RebuildView(); status = "실행 취소"; }
                else status = "되돌릴 항목 없음";
            }
            if (ctrl && Input.GetKeyDown(KeyCode.Y) && MemoFocused() == false)
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
                && selectedId >= 0)
            {
                var r = layout.TryRemove(selectedId);
                if (r.Ok) { selectedId = -1; RebuildView(); status = "삭제"; }
                else status = RejectText(r.Reason);
            }
            if (Input.GetKeyDown(KeyCode.T)) toolsOpen = !toolsOpen;
            if (Input.GetKeyDown(KeyCode.B)) shopOpen = !shopOpen;
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
            else
            {
                status = RejectText(r.Ok ? end : r.Reason);
            }
        }

        private void TryRemoveHere()
        {
            var f = layout.OccupyingAt(hoverX, hoverY);
            if (f == null) { status = "가구 없음"; return; }
            var r = layout.TryRemove(f.Id);
            if (r.Ok) { RebuildView(); status = "삭제: " + f.Kind; }
            else status = RejectText(r.Reason);
        }

        private static string RejectText(PlacementReject r)
        {
            return "배치 불가 (" + r + ")";
        }

        // ---- view -----------------------------------------------------

        private void RebuildView()
        {
            var old = transform.Find("CozyCafeStageView");
            if (old != null) Destroy(old.gameObject);
            StageViewBuilder.Build(boot.Scene).transform.SetParent(transform, false);
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

        // ---- customers ------------------------------------------------

        private sealed class Customer
        {
            public GameObject Go;
            public List<int> Path;
            public int PathIdx;
            public float GX, GY;        // grid coords (centered)
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
            var room = boot.Scene.Room;
            int w = room.Width;
            for (int i = customers.Count - 1; i >= 0; i--)
            {
                var c = customers[i];
                if (c.State == 1)
                {
                    c.SitLeft -= dt;
                    if (c.SitLeft <= 0f) { PayAndLeave(c); }
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

        private void TrySpawnCustomer()
        {
            if (customers.Count >= 3) return;
            int sx = -1, sy = -1;
            foreach (var f in boot.Scene.Furniture)
            {
                if (f.Kind == FurnitureKind.Door) { sx = f.CellX; sy = f.CellY; break; }
            }
            if (sx < 0) { sx = 0; sy = 0; }
            if (!layout.IsWalkableCell(sx, sy))
            {
                bool found = false;
                foreach (var f in boot.Scene.Furniture)
                {
                    if (f.Kind != FurnitureKind.Door) continue;
                    int[] dx = { 1, -1, 0, 0 }, dy = { 0, 0, 1, -1 };
                    for (int k = 0; k < 4; k++)
                    {
                        if (layout.IsWalkableCell(f.CellX + dx[k], f.CellY + dy[k]))
                        { sx = f.CellX + dx[k]; sy = f.CellY + dy[k]; found = true; break; }
                    }
                    if (found) break;
                }
                if (!found && !layout.IsWalkableCell(sx, sy))
                {
                    for (int y = 0; y < boot.Scene.Room.Height && !found; y++)
                        for (int x = 0; x < boot.Scene.Room.Width && !found; x++)
                            if (layout.IsWalkableCell(x, y)) { sx = x; sy = y; found = true; }
                }
                if (!found) return;
            }
            // find a free seat and a walkable cell beside it
            Furniture seat = null; int tx = -1, ty = -1;
            int[] ndx = { 1, -1, 0, 0 }, ndy = { 0, 0, 1, -1 };
            foreach (var f in boot.Scene.Furniture)
            {
                if (!LayoutModule.IsSeat(f.Kind) || takenSeats.Contains(f.Id)) continue;
                for (int k = 0; k < 4; k++)
                {
                    int nx = f.CellX + ndx[k], ny = f.CellY + ndy[k];
                    if (layout.IsWalkableCell(nx, ny))
                    {
                        var p = layout.FindPath(sx, sy, nx, ny);
                        if (p != null && p.Count > 0)
                        { seat = f; tx = nx; ty = ny; break; }
                    }
                }
                if (seat != null) break;
            }
            if (seat == null) return;
            takenSeats.Add(seat.Id);

            var c = new Customer();
            c.SeatId = seat.Id;
            c.GX = sx + 0.5f; c.GY = sy + 0.5f;
            c.Path = layout.FindPath(sx, sy, tx, ty);
            c.PathIdx = 0;
            var go = new GameObject("cust_" + custSeq++);
            var sr = go.AddComponent<SpriteRenderer>();
            var art = StageViewBuilder.AtlasSprite("body_anchor");
            sr.sprite = art != null ? art
                : Diamond(12, 18, new Color(0.89f, 0.57f, 0.36f));
            sr.color = new Color(1f, 0.8f, 0.65f);
            sr.sortingOrder = 200;
            c.Go = go;
            customers.Add(c);
        }

        private void PayAndLeave(Customer c)
        {
            if (!c.Paid && econ != null && econ.Data != null)
            {
                MenuLine line = null;
                foreach (var kv in econ.Lines)
                    if (econ.Owns(kv.Value.Def)) { line = kv.Value; break; }
                if (line != null)
                {
                    var ev = econ.IssueSaleEventId(line.Def.Id);
                    if (econ.SettleSale(ev, line.Def, line.Level))
                        status = "+" + line.Def.Price + "코인 판매: " + line.Def.Name;
                }
                c.Paid = true;
            }
            // path back to the door/spawn edge
            var room = boot.Scene.Room;
            int w = room.Width;
            int cx = Mathf.Clamp((int)(c.GX - 0.5f), 0, w - 1);
            int cy = Mathf.Clamp((int)(c.GY - 0.5f), 0, room.Height - 1);
            int sx = 0, sy = 0;
            foreach (var f in boot.Scene.Furniture)
                if (f.Kind == FurnitureKind.Door) { sx = f.CellX; sy = f.CellY; }
            if (!layout.IsWalkableCell(sx, sy))
                for (int y = 0; y < room.Height; y++)
                    for (int x = 0; x < w; x++)
                        if (layout.IsWalkableCell(x, y)) { sx = x; sy = y; goto done; }
            done:
            c.Path = layout.FindPath(cx, cy, sx, sy);
            c.PathIdx = 0;
            c.State = 2;
            if (c.Path == null) c.Path = new List<int>();
        }

        private void Despawn(Customer c, int i)
        {
            if (c.SeatId >= 0) takenSeats.Remove(c.SeatId);
            if (c.Go != null) Destroy(c.Go);
            customers.RemoveAt(i);
        }

        // ---- espresso machine frames + music audio ---------------------

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

        // Warm pad: slow Cmaj7-Am7-Fmaj7-G7 chord loop, soft sine stack.
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

        // Music box: gentle descending arpeggio, short sine pings w/ decay.
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

        // Rain: smoothed brown noise loop.
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

        // ghost highlight diamond at the hovered cell
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
            bool show = hoverX >= 0 && pendingKind >= -1;
            ghost.SetActive(show);
            if (!show) return;
            double sx, sy;
            IsoMath.Project(hoverX + 0.5, hoverY + 0.5, out sx, out sy);
            ghost.transform.position = new Vector3((float)sx, (float)-sy, 0f);
            var sr = ghost.GetComponent<SpriteRenderer>();
            sr.color = pendingKind == -1 ? new Color(1f, 1f, 1f, 0.25f)
                     : pendingKind == -2 ? new Color(1f, 0.3f, 0.3f, 0.4f)
                     : new Color(0.4f, 1f, 0.5f, 0.4f);
        }

        private static Sprite Diamond(int w, int h, Color color)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    double nx = Math.Abs(x - w / 2.0 + 0.5) / (w / 2.0);
                    double ny = Math.Abs(y - h / 2.0 + 0.5) / (h / 2.0);
                    px[y * w + x] = (nx + ny <= 1.0)
                        ? (Color32)color : new Color32(0, 0, 0, 0);
                }
            }
            tex.SetPixels32(px);
            tex.filterMode = FilterMode.Point;
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h),
                new Vector2(0.5f, 0.5f), 1f);
        }

        // ---- persistence ----------------------------------------------

        private void SaveState()
        {
            try
            {
                File.WriteAllText(
                    Path.Combine(SaveDir, "layout.json"), layout.SaveLayout());
                File.WriteAllText(
                    Path.Combine(SaveDir, "tools.json"), tools.SaveTools());
                status = "저장됨";
            }
            catch (Exception e) { status = "저장 실패: " + e.Message; }
        }

        private void LoadState()
        {
            try
            {
                var lf = Path.Combine(SaveDir, "layout.json");
                var tf = Path.Combine(SaveDir, "tools.json");
                if (File.Exists(lf))
                {
                    layout.LoadLayout(File.ReadAllText(lf));
                    RebuildView();
                }
                if (File.Exists(tf))
                    tools.LoadTools(File.ReadAllText(tf));
            }
            catch (Exception e) { status = "불러오기 실패: " + e.Message; }
        }

        private void OnApplicationQuit() { SaveState(); }

        // ---- IMGUI overlay ---------------------------------------------

        private bool UiBlocked()
        {
            Vector2 m = new Vector2(Input.mousePosition.x,
                Screen.height - Input.mousePosition.y);
            if (m.y < 64) return true;                       // toolbar + status
            if (toolsOpen && m.x > Screen.width - 280) return true;
            if (shopOpen && m.x < 300) return true;
            return false;
        }

        private bool MemoFocused()
        {
            var n = GUI.GetNameOfFocusedControl();
            return n == "memo" || n == "todo";
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
            {
                if (installed.Contains(p))
                { f = Font.CreateDynamicFontFromOSFont(p, 15); break; }
            }
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 15 };
            btnStyle = new GUIStyle(GUI.skin.button) { fontSize = 14 };
            areaStyle = new GUIStyle(GUI.skin.textArea) { fontSize = 14 };
            if (f != null)
            {
                labelStyle.font = f; btnStyle.font = f;
                areaStyle.font = f;
            }
        }

        private void OnGUI()
        {
            EnsureStyles();
            DrawToolbar();
            DrawTools();
            DrawShop();
        }

        private void DrawToolbar()
        {
            GUILayout.BeginArea(new Rect(0, 0, Screen.width, 64), GUI.skin.box);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("선택", Btn(pendingKind == -1),
                GUILayout.Width(56))) { pendingKind = -1; }
            foreach (var k in new[] { FurnitureKind.Table, FurnitureKind.Chair,
                     FurnitureKind.Stool, FurnitureKind.Counter,
                     FurnitureKind.EspressoMachine })
            {
                bool on = pendingKind == (int)k;
                if (GUILayout.Button(KindName(k), Btn(on), GUILayout.Width(74)))
                    pendingKind = (int)k;
            }
            if (GUILayout.Button("삭제", Btn(pendingKind == -2),
                GUILayout.Width(56))) pendingKind = -2;
            GUILayout.Space(10);
            if (GUILayout.Button("상점(B)", Btn(shopOpen), GUILayout.Width(64)))
                shopOpen = !shopOpen;
            if (GUILayout.Button("도구(T)", Btn(toolsOpen), GUILayout.Width(64)))
                toolsOpen = !toolsOpen;
            if (GUILayout.Button("저장", btnStyle, GUILayout.Width(52)))
                SaveState();
            GUILayout.EndHorizontal();

            string sel = selectedId >= 0 ? "선택#" + selectedId : "-";
            GUILayout.Label(
                "코인 " + (econ != null ? econ.Coins : 0) +
                "   시간 " + TimeSpan.FromSeconds(econ != null ? econ.Clock : 0)
                    .ToString(@"hh\:mm") +
                "   셀(" + hoverX + "," + hoverY + ")  가구 " + sel +
                "   undo " + (layout != null ? layout.UndoDepth : 0) +
                "/" + (layout != null ? layout.RedoDepth : 0) +
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

            GUILayout.Label("-- 메모 --", labelStyle);
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
            foreach (var t in new List<TodoItem>(tools.Todos))
            {
                GUILayout.BeginHorizontal();
                bool done = GUILayout.Toggle(t.Done, t.Text, labelStyle);
                if (done != t.Done) tools.SetTodoDone(t.Id, done);
                if (GUILayout.Button("x", btnStyle, GUILayout.Width(24)))
                    tools.RemoveTodo(t.Id);
                GUILayout.EndHorizontal();
            }
            GUILayout.BeginHorizontal();
            GUI.SetNextControlName("todo");
            todoDraft = GUILayout.TextField(todoDraft,
                GUILayout.Width(200));
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
                TimeSpan.FromSeconds(ft.RemainingSeconds)
                    .ToString(@"mm\:ss") +
                "   총 집중 " +
                TimeSpan.FromSeconds(tools.TotalFocusSeconds)
                    .ToString(@"hh\:mm"), labelStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("집중25", btnStyle)) ft.StartFocus();
            if (GUILayout.Button("휴식5", btnStyle)) ft.StartBreak();
            if (GUILayout.Button("정지", btnStyle)) tools.StopTimer();
            GUILayout.EndHorizontal();

            GUILayout.Label("-- 음악 --", labelStyle);
            var deck = tools.Music;
            var cur = deck.Current;
            GUILayout.Label(cur != null ? cur.Title : "(트랙 없음)" +
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

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // Shop/management panel: real menu upgrades, research queue and
        // staff hiring through the verified core modules.
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
                {
                    status = econ.PurchaseUpgrade(l.Def, 1)
                        ? "업그레이드: " + l.Def.Name
                        : "코인 부족";
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Label("-- 연구 --", labelStyle);
            if (research != null && econ.Data != null)
            {
                if (research.Active != null)
                    GUILayout.Label("진행: " + research.Active.Name + " " +
                        TimeSpan.FromSeconds(research.ActiveRemainingWork)
                            .ToString(@"mm\:ss"), labelStyle);
                foreach (var r in econ.Data.Research)
                {
                    if (research.IsCompleted(r))
                    { GUILayout.Label("✓ " + r.Name, labelStyle); continue; }
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(r.Name + " (" + r.Cost + ")",
                        labelStyle, GUILayout.Width(170));
                    if (GUILayout.Button("연구", btnStyle, GUILayout.Width(60)))
                        status = research.Reserve(r.Id)
                            ? "연구 예약: " + r.Name : "예약 실패";
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.Label("-- 직원 --", labelStyle);
            if (staff != null)
            {
                GUILayout.Label("고용 " + staff.Roster.Count + "명  판매+"
                    + staff.TotalSalesBonusPct + "%", labelStyle);
                if (!staff.PanelOpen)
                {
                    if (GUILayout.Button("지원자 보기", btnStyle))
                        staff.OpenHirePanel();
                }
                else
                {
                    for (int i = 0; i < staff.Candidates.Count; i++)
                    {
                        var c = staff.Candidates[i];
                        GUILayout.BeginHorizontal();
                        GUILayout.Label(c.Id + " 판매+" + c.SalesBonusPct +
                            "% 연구+" + c.ResearchBonusPct + "%",
                            labelStyle, GUILayout.Width(180));
                        if (GUILayout.Button("고용", btnStyle,
                            GUILayout.Width(56)))
                        {
                            status = staff.Hire(i)
                                ? "고용: " + c.Id : "고용 실패(코인/정원)";
                            if (status.StartsWith("고용:"))
                                econ.StaffSalesBonusPct =
                                    staff.TotalSalesBonusPct;
                        }
                        GUILayout.EndHorizontal();
                    }
                    if (GUILayout.Button("닫기", btnStyle))
                        staff.CloseHirePanel();
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
