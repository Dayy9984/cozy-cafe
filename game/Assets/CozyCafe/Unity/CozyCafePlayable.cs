using System;
using System.Collections.Generic;
using System.IO;
using CozyCafe.Core;
using CozyCafe.Core.Layout;
using CozyCafe.Core.Economy;
using CozyCafe.Core.Iso;
using CozyCafe.Core.Modules;
using CozyCafe.Core.Scene;
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
        private Camera cam;

        private int pendingKind = -1;   // >=0: FurnitureKind to place; -1: select/move; -2: remove
        private int selectedId = -1;
        private int hoverX = -1, hoverY = -1;
        private string status = "";
        private bool toolsOpen;

        private readonly List<Vector3> agentBase = new List<Vector3>();
        private readonly List<Transform> agentNodes = new List<Transform>();
        private GameObject ghost;
        private float clock;

        private string memoText = "";
        private string todoDraft = "";
        private Vector2 toolsScroll;

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
            }
            LoadState();
            CollectAgents();
            MakeGhost();
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
            if (tools != null)
            {
                tools.TickTimer(dt);
                tools.Music.TickPlayback(dt);
            }

            UpdateHover();
            HandleInput();
            BobAgents();
            ZoomCamera();
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
            var view = transform.Find("CozyCafeStageView");
            if (view == null) return;
            foreach (Transform child in view)
            {
                if (child.name.StartsWith("agent_", StringComparison.Ordinal))
                {
                    agentNodes.Add(child);
                    agentBase.Add(child.position);
                }
            }
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
    }
}
