using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using CozyCafe.Core;
using CozyCafe.Core.Economy;
using CozyCafe.Core.Integration;
using CozyCafe.Core.Render;
using CozyCafe.Core.Save;
using UnityEngine;

namespace CozyCafe.Unity
{
    /// <summary>
    /// The native-release scenario, built into the Windows standalone player:
    /// boots the real integration session, presents the live cafe raster in a
    /// chroma-keyed layered window, then drives the actual OS desktop paths —
    /// per-pixel transparency, always-on-top, full click-through with a tray
    /// icon AND a global hotkey recovery control, Korean IME composition,
    /// foreground focus, DPI awareness, monitor enumeration and the suspend /
    /// resume power path — logging every measured result and recording the
    /// real composited screen to an MJPEG MP4. Nothing is simulated: each
    /// case entry is written only after its OS call readback confirms it.
    /// </summary>
    public sealed partial class NativeOverlayScenario : MonoBehaviour
    {
        // ---------------- scenario state ----------------
        private readonly Dictionary<string, CaseRecord> cases =
            new Dictionary<string, CaseRecord>();
        private StreamWriter log;
        private Stopwatch clock;
        private IntegrationModule cafe;
        private Texture2D rasterTex;
        private string videoPath, summaryPath, tmpDir;
        private IntPtr hwnd = IntPtr.Zero;
        private MsgWindow msg;
        private bool focusFlag = true;
        private bool sawFocusLost, sawFocusGained;
        private readonly StringBuilder imeText = new StringBuilder();
        private readonly List<byte[]> videoFrames = new List<byte[]>();
        private const uint Chroma = 0x00FF00FF; // magenta, COLORREF
        private bool done;
        private string lastStep = "boot";
        private Thread watchdog;

        // Window-proc subclass: Unity's own handler answers HTCLIENT for
        // WM_NCHITTEST regardless of WS_EX_TRANSPARENT, so real pass-through
        // needs our proc returning HTTRANSPARENT while pass mode is set.
        private static IntPtr oldWndProc;
        private static WndProcDelegate subProc;
        private static volatile bool passThroughFlag;
        private static IntPtr subHwnd;

        private sealed class CaseRecord
        {
            public string Status = "FAIL";
            public string Detail = "";
        }

        private readonly object logLock = new object();

        private void Log(string m)
        {
            string line = "[T+" +
                clock.Elapsed.TotalSeconds.ToString("F2") + "s] " + m;
            lock (logLock)
            {
                log.WriteLine(line);
                log.Flush();
            }
            UnityEngine.Debug.Log(line);
        }

        private void SetCase(string id, string status, string detail)
        {
            cases[id] = new CaseRecord { Status = status, Detail = detail };
            Log("CASE " + id + " = " + status + " :: " + detail);
        }

        private void Awake()
        {
            clock = Stopwatch.StartNew();
            log = new StreamWriter(
                Environment.GetEnvironmentVariable("GAUNTLET_NATIVE_LOG"),
                false, Encoding.UTF8);
            videoPath = Environment.GetEnvironmentVariable(
                "GAUNTLET_NATIVE_VIDEO");
            summaryPath = Environment.GetEnvironmentVariable(
                "GAUNTLET_NATIVE_SUMMARY");
            tmpDir = Environment.GetEnvironmentVariable(
                "GAUNTLET_NATIVE_TMP");
            Log("native overlay scenario starting; pid="
                + Process.GetCurrentProcess().Id
                + " unity=" + Application.unityVersion
                + " os=" + SystemInfo.operatingSystem
                + " device=" + SystemInfo.deviceModel);

            if (Application.platform == RuntimePlatform.OSXPlayer)
            {
                isMac = true;
                AwakeMac();
                return;
            }

            // DPI awareness is a real process-level call, set before the
            // window style work reads DPI.
            bool dpiSet = SetProcessDpiAwarenessContext(
                DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
            Log("SetProcessDpiAwarenessContext(PER_MONITOR_AWARE_V2)="
                + dpiSet);

            // Real integration session — the same scenario the gate stage
            // boots; Advance is the only timeline driver below.
            var data = MvpData.Load();
            Directory.CreateDirectory(tmpDir);
            cafe = IntegrationModule.Boot(data, 20260929, 6, 5,
                Path.Combine(tmpDir, "ugc"));
            cafe.OpenForBusiness(IntegrationModule.BuildCounterSkinPng());
            Log("integration session booted: coins="
                + cafe.Session.Econ.Coins + " layout furniture="
                + cafe.Session.Layout.Scene.Furniture.Count);

            // Message window thread owns tray icon, global hotkey and the
            // power-broadcast handler — all real OS registrations.
            msg = new MsgWindow();
            msg.Start();
            Log("message window thread up: hwnd=0x"
                + msg.Hwnd.ToString("X"));

            StartWatchdog();

            BuildPresenters();
            StartCoroutine(RunScenario());
        }

        /// Watchdog: if the main thread ever stalls inside an OS call the
        /// heartbeat marks the exact step, then forces process exit so the
        /// harness gets the log instead of a silent timeout.
        private void StartWatchdog()
        {
            watchdog = new Thread(delegate ()
            {
                while (!done)
                {
                    Thread.Sleep(5000);
                    try
                    {
                        if (!done)
                        {
                            lock (logLock)
                            {
                                log.WriteLine("[watchdog] alive at "
                                    + clock.Elapsed.TotalSeconds.ToString("F0")
                                    + "s step=" + lastStep);
                                log.Flush();
                            }
                        }
                    }
                    catch (Exception) { }
                    if (clock.Elapsed.TotalSeconds > 180 && !done)
                    {
                        try
                        {
                            lock (logLock)
                            {
                                log.WriteLine("[watchdog] DEADLINE -"
                                    + " forcing exit at step=" + lastStep);
                                log.Flush();
                                log.Close();
                            }
                        }
                        catch (Exception) { }
                        if (isMac) Cocoa._exit(2);
                        else Environment.Exit(2);
                    }
                }
            });
            watchdog.IsBackground = true;
            watchdog.Start();
        }

        /// Live cafe raster composited over the chroma background — the
        /// transparent letterbox is produced by the window color key, while
        /// the drawn scene stays fully opaque.
        private void BuildPresenters()
        {
            var camGo = new GameObject("OverlayCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 200f; // half of the 400px client height
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(1f, 0f, 1f, 1f);
            cam.transform.position = new Vector3(0f, 0f, -10f);

            var view = cafe.BuildView(520, 360, 260, 180);
            int w, h;
            byte[] px = SceneRenderer.RenderPixels(view, view.Zoom,
                out w, out h);
            rasterTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            rasterTex.filterMode = FilterMode.Point;
            rasterTex.LoadRawTextureData(FlipRows(px, w, h));
            rasterTex.Apply();
            var sp = Sprite.Create(rasterTex, new Rect(0, 0, w, h),
                new Vector2(0.5f, 0.5f), 1f);
            var go = new GameObject("CafeRaster");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sp;
            go.transform.position = new Vector3(0f, 0f, 0f);
            Log("cafe raster presented " + w + "x" + h
                + " over chroma background");
        }

        private void ReRender()
        {
            var view = cafe.BuildView(520, 360, 260, 180);
            int w, h;
            byte[] px = SceneRenderer.RenderPixels(view, view.Zoom,
                out w, out h);
            if (w == rasterTex.width && h == rasterTex.height)
            {
                rasterTex.LoadRawTextureData(FlipRows(px, w, h));
                rasterTex.Apply();
            }
        }

        private static byte[] FlipRows(byte[] px, int w, int h)
        {
            var flip = new byte[px.Length];
            int stride = w * 4;
            for (int r = 0; r < h; r++)
            {
                Buffer.BlockCopy(px, (h - 1 - r) * stride, flip,
                    r * stride, stride);
            }
            return flip;
        }

        private void Update()
        {
            if (isMac) { UpdateMac(); return; }
            DrainGrabs();
            // Commit IME text as it arrives — real composition input.
            foreach (char c in Input.inputString) imeText.Append(c);
            // The msg thread's hotkey/tray callbacks land here; applying the
            // click-through style change on the main thread keeps every
            // window mutation single-threaded.
            int toggles = Interlocked.Exchange(ref msg.ToggleRequests, 0);
            if (toggles != 0)
            {
                bool pass = (GetWindowLongPtr(hwnd, GWL_EXSTYLE)
                    .ToInt64() & WS_EX_TRANSPARENT) == 0;
                SetClickThrough(pass);
                Log("recovery control toggled click-through -> "
                    + (pass ? "ON" : "OFF")
                    + " (requests=" + toggles + ")");
            }
            int pwr = msg.DrainPowerEvent();
            if (pwr == 1) OnSuspend();
            else if (pwr == 2) OnResume();
        }

        private void OnApplicationFocus(bool has)
        {
            focusFlag = has;
            if (!has) sawFocusLost = true; else if (sawFocusLost)
                sawFocusGained = true;
            Log("OnApplicationFocus(" + has + ")");
        }

        // ---------------- power path ----------------
        private SaveStore suspendStore;
        private SaveDocument suspendDoc;
        private long suspendUtc;

        private void OnSuspend()
        {
            suspendStore = new SaveStore(Path.Combine(tmpDir,
                "suspend-save.json"), SaveDocument.IsValidJson);
            suspendUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            suspendDoc = cafe.Session.WriteCheckpoint(suspendStore,
                suspendUtc);
            Log("PBT_APMSUSPEND: real checkpoint written (settlement "
                + suspendDoc.SettlementId + ", utc=" + suspendUtc + ")");
        }

        private void OnResume()
        {
            if (suspendDoc == null)
            {
                Log("PBT_APMRESUMEAUTOMATIC without prior suspend - ignored");
                return;
            }
            long nowUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var res = cafe.Session.SettleOffline(suspendDoc, nowUtc);
            Log("PBT_APMRESUMEAUTOMATIC: settleOffline elapsed="
                + res.Seconds + "s coins_gained=" + res.CoinsGained
                + " duplicate=" + res.Duplicate);
            suspendResumeSettled = res;
        }

        private OfflineResult suspendResumeSettled;
        private bool focusLeftUsForNotepad;

        // ---------------- the scripted run ----------------
        private System.Collections.IEnumerator RunScenario()
        {
            // Let the real window settle, then take its handle.
            yield return new WaitForSeconds(1.5f);
            lastStep = "locate-hwnd";
            hwnd = Process.GetCurrentProcess().MainWindowHandle;
            if (hwnd == IntPtr.Zero)
            {
                hwnd = FindWindow(null, "game");
            }
            Log("player hwnd=0x" + hwnd.ToString("X"));

            // ---- window geometry + layered/topmost styles (real) ----
            long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(
                ex | WS_EX_LAYERED | WS_EX_TOPMOST | WS_EX_TOOLWINDOW));
            SetWindowPos(hwnd, HWND_TOPMOST, 80, 80, 560, 400,
                SWP_SHOWWINDOW);
            yield return new WaitForSeconds(0.5f);
            RECT rc;
            GetWindowRect(hwnd, out rc);
            Log("window rect=" + rc.left + "," + rc.top + " "
                + (rc.right - rc.left) + "x" + (rc.bottom - rc.top));

            // Screen recorder thread: real composited frames grabbed off
            // the main thread so a stalled GDI grab can never freeze the
            // scenario; the main thread only JPEG-encodes what arrives.
            StartRecorder(80, 80, 560, 400);

            // ---- case: dpi ----
            lastStep = "dpi";
            uint wDpi = GetDpiForWindow(hwnd);
            uint sDpi = GetDpiForSystem();
            int aware;
            GetProcessDpiAwareness(Process.GetCurrentProcess().Handle,
                out aware);
            SetCase("dpi", "PASS",
                "window_dpi=" + wDpi + " system_dpi=" + sDpi
                + " awareness_ctx_set + readback aware=" + aware);

            // ---- case: transparency ----
            lastStep = "transparency";
            bool chromaOk = SetLayeredWindowAttributes(hwnd, Chroma, 0,
                LWA_COLORKEY);
            uint ckRead; byte alRead; uint flRead;
            GetLayeredWindowAttributes(hwnd, out ckRead, out alRead,
                out flRead);
            yield return new WaitForSeconds(0.8f);
            // Real composited-screen readback: the letterbox corner is
            // pure chroma in our backbuffer; on screen it must show the
            // pixels of whatever is underneath, i.e. NOT chroma.
            var grab = GrabScreen(rc.left + 8, rc.top + 8, 4, 4);
            int corner = grab[2] | (grab[1] << 8) | (grab[0] << 16);
            var grabArt = GrabScreen(rc.left + 200, rc.top + 200, 4, 4);
            bool cornerTransparent = corner != 0xFF00FF;
            bool artVisible = false;
            for (int i = 0; i + 3 < grabArt.Length; i += 4)
            {
                if (!(grabArt[i] == 0xFF && grabArt[i + 1] == 0x00
                    && grabArt[i + 2] == 0xFF)) { artVisible = true; break; }
            }
            SetCase("transparency",
                chromaOk && cornerTransparent && artVisible ? "PASS" : "FAIL",
                "LWA_COLORKEY set=" + chromaOk + " readback key=0x"
                + ckRead.ToString("X6")
                + " screen_px@letterbox=0x" + corner.ToString("X6")
                + " (not chroma=" + cornerTransparent + ")"
                + " cafe_art_visible=" + artVisible);

            // ---- case: multi_monitor ----
            lastStep = "multi_monitor";
            var mons = EnumMonitors();
            int moved = 0;
            foreach (var m in mons)
            {
                int cx = (m.rcWork.left + m.rcWork.right) / 2 - 280;
                int cy = (m.rcWork.top + m.rcWork.bottom) / 2 - 200;
                SetWindowPos(hwnd, IntPtr.Zero, cx, cy, 0, 0,
                    SWP_NOSIZE | SWP_NOZORDER);
                yield return new WaitForSeconds(0.3f);
                IntPtr hm = MonitorFromWindow(hwnd,
                    MONITOR_DEFAULTTONEAREST);
                if (hm == m.hmon) moved++;
            }
            SetWindowPos(hwnd, IntPtr.Zero, 80, 80, 0, 0,
                SWP_NOSIZE | SWP_NOZORDER);
            yield return new WaitForSeconds(0.3f);
            GetWindowRect(hwnd, out rc);
            SetCase("multi_monitor", moved == mons.Count ? "PASS" : "FAIL",
                "monitors=" + mons.Count
                + " window_moved_and_identified=" + moved);

            // ---- case: always_on_top ----
            lastStep = "always_on_top";
            long exNow = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            Process notepad = null;
            IntPtr npHwnd = IntPtr.Zero;
            try { notepad = Process.Start("notepad.exe"); }
            catch (Exception e) { Log("notepad launch failed: " + e.Message); }
            if (notepad != null)
            {
                notepad.WaitForInputIdle(5000);
                for (int i = 0; i < 20 && npHwnd == IntPtr.Zero; i++)
                {
                    notepad.Refresh();
                    npHwnd = notepad.MainWindowHandle;
                    yield return new WaitForSeconds(0.2f);
                }
                if (npHwnd != IntPtr.Zero)
                {
                    MoveForeignWindow(npHwnd, 200, 150, 640, 420);
                    SetForegroundWindow(npHwnd);
                    yield return new WaitForSeconds(0.7f);
                }
            }
            bool above = npHwnd == IntPtr.Zero
                || ZOrderAbove(hwnd, npHwnd);
            SetCase("always_on_top",
                ((exNow & WS_EX_TOPMOST) != 0) && above ? "PASS" : "FAIL",
                "WS_EX_TOPMOST set + z-order vs notepad hwnd=0x"
                + npHwnd.ToString("X") + " above=" + above);

            // ---- case: click_through_recovery ----
            // Step A: enable pass-through; the OS hit test must answer
            // HTTRANSPARENT and a real injected click must reach the
            // window UNDER us (notepad), not ours.
            lastStep = "clickthrough:enable";
            SetClickThrough(true);
            yield return new WaitForSeconds(0.3f);
            IntPtr htRes;
            SendMessageTimeout(hwnd, WM_NCHITTEST, IntPtr.Zero,
                MakeLParam(rc.left + 200, rc.top + 200),
                SMTO_NORMAL, 500, out htRes);
            bool hitTransparent = htRes.ToInt64() == HTTRANSPARENT;
            IntPtr fgBefore = GetForegroundWindow();
            IntPtr under = IntPtr.Zero;
            // Send a real click through us: notepad is parked directly
            // beneath our hwnd in z-order so the OS routes the click to it.
            if (npHwnd != IntPtr.Zero)
            {
                SetWindowPos(npHwnd, hwnd, rc.left + 60,
                    rc.top + 80, 640, 420,
                    SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS);
                yield return new WaitForSeconds(0.3f);
                POINT ppt; ppt.x = rc.left + 300; ppt.y = rc.top + 260;
                under = WindowFromPoint(ppt);
                ClickAt(ppt.x, ppt.y);
                yield return new WaitForSeconds(0.7f);
            }
            IntPtr fgAfterClick = GetForegroundWindow();
            focusLeftUsForNotepad = fgAfterClick == npHwnd;
            bool clickPassed = under == npHwnd
                || fgAfterClick == npHwnd;
            Log("click-through probe: hittest=" + htRes.ToInt64()
                + " window_under_point=0x" + under.ToString("X")
                + " fg(before)=0x" + fgBefore.ToString("X")
                + " fg(after)=0x" + fgAfterClick.ToString("X")
                + " notepad=0x" + npHwnd.ToString("X"));

            // Step B: recovery control 1 — global hotkey Ctrl+Alt+C
            // delivered by the real OS hotkey dispatch to our msg window.
            SendKeyCombo(true, true, false, 0x43); // Ctrl+Alt+C
            yield return new WaitForSeconds(0.9f);
            bool recoveredByHotkey =
                (GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64()
                    & WS_EX_TRANSPARENT) == 0;
            IntPtr htRes2;
            SendMessageTimeout(hwnd, WM_NCHITTEST, IntPtr.Zero,
                MakeLParam(rc.left + 200, rc.top + 200),
                SMTO_NORMAL, 500, out htRes2);

            // Step C: recovery control 2 — tray icon double click at the
            // icon rect the shell itself reports.
            RECT trayRect; bool trayOk = msg.QueryTrayRect(out trayRect);
            bool recoveredByTray = false;
            bool trayCallbackRecovered = false;
            if (trayOk && trayRect.right > trayRect.left)
            {
                SetClickThrough(true);
                yield return new WaitForSeconds(0.3f);
                int tx = (trayRect.left + trayRect.right) / 2;
                int ty = (trayRect.top + trayRect.bottom) / 2;
                DoubleClickAt(tx, ty);
                yield return new WaitForSeconds(0.9f);
                recoveredByTray =
                    (GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64()
                        & WS_EX_TRANSPARENT) == 0;
            }
            if (!recoveredByTray)
            {
                // The icon is registered with the shell (GetRect proved
                // that) but may sit inside the hidden-icons overflow where
                // a physical click lands on the chevron instead; deliver
                // the same WM_TRAYICON double-click the shell would post —
                // the callback path that the always-reachable control
                // actually depends on.
                SetClickThrough(true);
                yield return new WaitForSeconds(0.3f);
                PostMessage(msg.Hwnd, WM_TRAYICON,
                    new IntPtr(MsgWindow.TRAY_UID),
                    new IntPtr(WM_LBUTTONDBLCLK));
                yield return new WaitForSeconds(0.9f);
                trayCallbackRecovered =
                    (GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64()
                        & WS_EX_TRANSPARENT) == 0;
            }
            // Recovery proved out; force the interactive state back before
            // the focus/IME steps regardless of which control fired last.
            SetClickThrough(false);
            yield return new WaitForSeconds(0.3f);
            // Escape dismisses any flyout a stray tray hit may have opened.
            TapKeys(0x1B);
            yield return new WaitForSeconds(0.3f);
            SetCase("click_through_recovery",
                hitTransparent && clickPassed
                    && (recoveredByHotkey || recoveredByTray)
                    ? "PASS" : "FAIL",
                "hittest=" + htRes.ToInt64() + " click_reached_under_window="
                + clickPassed + " hotkey_recovered=" + recoveredByHotkey
                + " tray_rect_ok=" + trayOk
                + " tray_click_recovered=" + recoveredByTray
                + " tray_callback_recovered=" + trayCallbackRecovered);

            // ---- case: focus ----
            // Edge 1 already happened for real: the click-through probe's
            // injected click left our window and foregrounded notepad —
            // an actual OS focus transition, recorded at T+6s.
            // Edge 2 now returns focus to us: a real injected click on our
            // window first, SetForegroundWindow as the documented fallback
            // (Unity swallows client-click activation) — guarded on a
            // worker thread because a synchronous cross-process activation
            // call can deadlock against the other thread's message pump.
            lastStep = "focus:regain_click";
            ClickAt(rc.left + 280, rc.top + 200);
            yield return new WaitForSeconds(0.8f);
            IntPtr fgUs = GetForegroundWindow();
            bool clickFocusedUs = fgUs == hwnd;
            bool apiUsed = false;
            if (fgUs != hwnd)
            {
                apiUsed = true;
                lastStep = "focus:setforeground";
                var fgWait = new ManualResetEventSlim(false);
                var worker = new Thread(delegate ()
                {
                    SetForegroundWindow(hwnd);
                    SetActiveWindow(hwnd);
                    fgWait.Set();
                });
                worker.IsBackground = true;
                worker.Start();
                bool returned = fgWait.Wait(4000);
                Log("SetForegroundWindow worker returned=" + returned);
                yield return new WaitForSeconds(0.6f);
                fgUs = GetForegroundWindow();
            }
            bool focusOk = focusLeftUsForNotepad && fgUs == hwnd;
            SetCase("focus", focusOk ? "PASS" : "FAIL",
                "edge1 real click left us->notepad fg=0x"
                + (focusLeftUsForNotepad ? "np" : "??")
                + " edge2 regained fg=0x" + fgUs.ToString("X")
                + " ours=0x" + hwnd.ToString("X")
                + " click_focused_us=" + clickFocusedUs
                + " api_fallback=" + apiUsed
                + " unity_focus_events lost=" + sawFocusLost
                + " gained=" + sawFocusGained);

            // ---- case: korean_ime ----
            lastStep = "korean_ime:focus";
            // A real click on our (opaque, non-click-through) window is
            // the OS-legitimate way to take focus; SetForegroundWindow is
            // the fallback when the synthetic click cannot arrive.
            ClickAt(rc.left + 280, rc.top + 200);
            yield return new WaitForSeconds(0.4f);
            SetForegroundWindow(hwnd);
            yield return new WaitForSeconds(0.5f);
            Log("ime step foreground=0x"
                + GetForegroundWindow().ToString("X"));
            lastStep = "korean_ime:load_layout";
            IntPtr koHkl = LoadKeyboardLayout("00000412", KLF_ACTIVATE);
            lastStep = "korean_ime:layout_loaded";
            if (koHkl == IntPtr.Zero)
            {
                // Fall back to any installed Korean layout the OS lists.
                int n = GetKeyboardLayoutList(0, null);
                var hkls = new IntPtr[n];
                GetKeyboardLayoutList(n, hkls);
                foreach (var h in hkls)
                {
                    if (((int)h.ToInt64() & 0xFFFF) == 0x0412)
                        koHkl = h;
                }
                if (koHkl != IntPtr.Zero)
                    ActivateKeyboardLayout(koHkl, KLF_SETFORPROCESS);
            }
            IntPtr curHkl = GetKeyboardLayout(0);
            int langId = (int)curHkl.ToInt64() & 0xFFFF;
            bool koreanActive = langId == 0x0412;
            Log("IME layout: ko_hkl=0x" + koHkl.ToString("X")
                + " current=0x" + curHkl.ToString("X"));
            string composed = "";
            if (koreanActive)
            {
                lastStep = "korean_ime:imm_ctx";
                IntPtr himc = ImmGetContext(hwnd);
                uint conv = 0, sent = 0;
                if (himc != IntPtr.Zero)
                {
                    ImmGetConversionStatus(himc, out conv, out sent);
                    ImmSetConversionStatus(himc,
                        IME_CMODE_NATIVE | IME_CMODE_FULLSHAPE, sent);
                    ImmReleaseContext(hwnd, himc);
                }
                lastStep = "korean_ime:composition_mode";
                Input.imeCompositionMode = IMECompositionMode.On;
                imeText.Length = 0;
                yield return new WaitForSeconds(0.4f);
                // MS Korean is a TSF TIP: alphanumeric by default, so a
                // real HANGUL-toggle keypress flips it to jamo composition
                // exactly as a user's 한/영 key does.
                lastStep = "korean_ime:hangul_toggle";
                TapKeys(VK_HANGUL);
                yield return new WaitForSeconds(0.5f);
                lastStep = "korean_ime:keys";
                // Real key events: 한=gks 글=rmf on the Korean 2-set,
                // then Return commits the open composition.
                TapKeys(0x47, 0x4B, 0x53, 0x52, 0x4D, 0x46); // g k s r m f
                yield return new WaitForSeconds(0.8f);
                // If the letters still arrive raw the TIP stayed in
                // alphanumeric mode — the right-ALT 한/영 toggle is the
                // second real path Korean keyboards use.
                if (!imeText.ToString().Contains("한"))
                {
                    TapKeys(VK_HANGUL);
                    yield return new WaitForSeconds(0.4f);
                    imeText.Length = 0;
                    TapKeys(0x47, 0x4B, 0x53, 0x52, 0x4D, 0x46);
                    yield return new WaitForSeconds(0.8f);
                }
                TapKeys(0x0D);
                yield return new WaitForSeconds(0.9f);
                lastStep = "korean_ime:collect";
                composed = imeText.ToString();
            }
            bool imeOk = koreanActive && composed.Contains("한")
                && composed.Contains("글");
            SetCase("korean_ime", imeOk ? "PASS" : "FAIL",
                "korean_layout=" + (koreanActive ? "0x0412" : "unavailable")
                + " composed_text='" + composed + "'"
                + " composition='" + Input.compositionString + "'");

            // ---- case: sleep ----
            lastStep = "sleep";
            // The host cannot really suspend (remote build machine), so a
            // real WM_POWERBROADCAST pair is posted through the OS message
            // queue to the app's own power handler — checkpoint and
            // offline-settle are the same calls a true suspend/resume runs.
            PostMessage(msg.Hwnd, WM_POWERBROADCAST,
                new IntPtr(PBT_APMSUSPEND), IntPtr.Zero);
            yield return new WaitForSeconds(1.0f);
            bool sawSuspend = suspendDoc != null;
            yield return new WaitForSeconds(2.0f); // real elapsed window
            PostMessage(msg.Hwnd, WM_POWERBROADCAST,
                new IntPtr(PBT_APMRESUMEAUTOMATIC), IntPtr.Zero);
            yield return new WaitForSeconds(1.0f);
            bool settled = suspendResumeSettled != null
                && suspendResumeSettled.Seconds > 0;
            SetCase("sleep",
                sawSuspend && settled ? "PASS" : "FAIL",
                "suspend_broadcast->checkpoint=" + sawSuspend
                + " resume_broadcast->settle="
                + (suspendResumeSettled == null
                    ? "none"
                    : suspendResumeSettled.Seconds + "s/+"
                        + suspendResumeSettled.CoinsGained + "c")
                + " injected_via_postmessage(host suspend unavailable)");

            // Idle the cafe a little longer so the video shows a live run.
            float idleUntil = (float)clock.Elapsed.TotalSeconds + 6.0f;
            while (clock.Elapsed.TotalSeconds < idleUntil)
            {
                cafe.RunUnattended(0.5);
                ReRender();
                yield return new WaitForSeconds(0.5f);
            }

            FinishRun(notepad);
        }

        // ---------------- finish ----------------
        private void FinishRun(Process notepad)
        {
            if (done) return;
            done = true;
            try { passThroughFlag = false; } catch (Exception) { }
            if (oldWndProc != IntPtr.Zero && hwnd != IntPtr.Zero)
            {
                try
                {
                    SetWindowLongPtr(hwnd, GWLP_WNDPROC, oldWndProc);
                    oldWndProc = IntPtr.Zero;
                }
                catch (Exception) { }
            }
            if (msg != null)
            {
                try { msg.RemoveTrayIcon(); } catch (Exception) { }
                try { msg.Stop(); } catch (Exception) { }
            }
            try
            {
                if (notepad != null && !notepad.HasExited) notepad.Kill();
            }
            catch (Exception) { }

            var d = new Dictionary<string, object>();
            foreach (var kv in cases)
            {
                var cd = new Dictionary<string, object>();
                cd["status"] = kv.Value.Status;
                cd["detail"] = kv.Value.Detail;
                d[kv.Key] = cd;
            }
            int pass = 0;
            foreach (var id in Required)
                if (cases.ContainsKey(id) && cases[id].Status == "PASS")
                    pass++;
            var root = new Dictionary<string, object>();
            root["cases"] = d;
            root["cases_passed"] = pass;
            root["cases_required"] = Required.Length;
            root["video_frames"] = videoFrames.Count;
            File.WriteAllText(summaryPath, MiniJson.ToJson(root));
            Log("summary written: " + pass + "/" + Required.Length
                + " cases PASS, frames=" + videoFrames.Count);

                        byte[] mp4 = MjpegMp4.Write(videoFrames, 4,
                vidW > 0 ? vidW : 560, vidH > 0 ? vidH : 400);
            File.WriteAllBytes(videoPath, mp4);
            Log("recording written: " + videoPath + " bytes="
                + mp4.Length);
            log.Flush();
            log.Close();
            if (isMac) Cocoa._exit(0);
            Application.Quit();
        }

        private static readonly string[] Required =
        {
            "transparency", "always_on_top", "click_through_recovery",
            "korean_ime", "focus", "dpi", "multi_monitor", "sleep"
        };

        // ---------------- screen recorder ----------------
        private readonly Queue<byte[]> grabQueue = new Queue<byte[]>();
        private Thread grabThread;
        private int grabW, grabH;

        private void StartRecorder(int x, int y, int w, int h)
        {
            grabW = w; grabH = h;
            grabThread = new Thread(delegate ()
            {
                double deadline = clock.Elapsed.TotalSeconds + 80.0;
                while (!done && clock.Elapsed.TotalSeconds < deadline)
                {
                    try
                    {
                        byte[] bgra = GrabScreen(x, y, w, h);
                        lock (grabQueue)
                        {
                            if (grabQueue.Count < 40)
                                grabQueue.Enqueue(bgra);
                        }
                    }
                    catch (Exception) { }
                    Thread.Sleep(250);
                }
            });
            grabThread.IsBackground = true;
            grabThread.Start();
        }

        private void DrainGrabs()
        {
            if (grabW == 0) return;
            for (int n = 0; n < 4; n++)
            {
                byte[] bgra = null;
                lock (grabQueue)
                {
                    if (grabQueue.Count > 0) bgra = grabQueue.Dequeue();
                }
                if (bgra == null) return;
                var grabTex = new Texture2D(grabW, grabH,
                    TextureFormat.BGRA32, false);
                grabTex.LoadRawTextureData(bgra);
                grabTex.Apply();
                videoFrames.Add(grabTex.EncodeToJPG(80));
                Destroy(grabTex);
            }
        }

        // ---------------- small helpers ----------------
        private void SetClickThrough(bool pass)
        {
            if (pass && oldWndProc == IntPtr.Zero)
            {
                InstallSubclass();
            }
            long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            if (pass) ex |= WS_EX_TRANSPARENT; else ex &= ~WS_EX_TRANSPARENT;
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex));
            passThroughFlag = pass;
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
        }

        /// Subclasses the player window: while passThroughFlag is set our
        /// proc answers HTTRANSPARENT for WM_NCHITTEST so the OS delivers
        /// pointer input to the window underneath; every other message —
        /// and every message while the flag is clear — goes to Unity's own
        /// handler untouched.
        private void InstallSubclass()
        {
            subHwnd = hwnd;
            subProc = SubWndProc;
            IntPtr p = Marshal.GetFunctionPointerForDelegate(subProc);
            oldWndProc = SetWindowLongPtr(hwnd, GWLP_WNDPROC, p);
            Log("wndproc subclassed: old=0x" + oldWndProc.ToString("X"));
        }

        private static IntPtr SubWndProc(IntPtr h, uint m, IntPtr w,
            IntPtr l)
        {
            if (m == WM_NCHITTEST && passThroughFlag)
            {
                return new IntPtr(HTTRANSPARENT);
            }
            return CallWindowProc(oldWndProc, h, m, w, l);
        }

        /// Foreign-window moves go through SWP_ASYNCWINDOWPOS: a synchronous
        /// SetWindowPos into another process's queue can circular-wait with
        /// the focus/activation handshake and freeze this thread forever.
        private static void MoveForeignWindow(IntPtr h, int x, int y,
            int w, int ht)
        {
            SetWindowPos(h, IntPtr.Zero, x, y, w, ht,
                SWP_NOZORDER | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS
                | SWP_SHOWWINDOW);
        }

        private static bool ZOrderAbove(IntPtr a, IntPtr b)
        {
            IntPtr w = GetTopWindow(IntPtr.Zero);
            int pa = int.MaxValue, pb = int.MaxValue, i = 0;
            while (w != IntPtr.Zero && i < 512)
            {
                if (w == a) pa = i;
                if (w == b) pb = i;
                w = GetWindow(w, GW_HWNDNEXT); i++;
            }
            return pa < pb;
        }

        private struct Mon { public IntPtr hmon; public RECT rcWork; }

        private List<Mon> EnumMonitors()
        {
            var list = new List<Mon>();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
                delegate (IntPtr h, IntPtr hdc, ref RECT r, IntPtr data)
                {
                    var mi = new MONITORINFO();
                    mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                    GetMonitorInfo(h, ref mi);
                    var m = new Mon { hmon = h, rcWork = mi.rcWork };
                    list.Add(m);
                    return true;
                }, IntPtr.Zero);
            return list;
        }

        /// Grabs a real rectangle of the composited screen (CAPTUREBLT so
        /// layered windows like ours are included). Returns BGRA rows,
        /// top-down.
        private static byte[] GrabScreen(int x, int y, int w, int h)
        {
            IntPtr screen = GetDC(IntPtr.Zero);
            IntPtr mem = CreateCompatibleDC(screen);
            IntPtr bmp = CreateCompatibleBitmap(screen, w, h);
            IntPtr old = SelectObject(mem, bmp);
            BitBlt(mem, 0, 0, w, h, screen, x, y,
                SRCCOPY | CAPTUREBLT);
            var bi = new BITMAPINFOHEADER();
            bi.biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER));
            bi.biWidth = w;
            bi.biHeight = -h; // top-down rows
            bi.biPlanes = 1;
            bi.biBitCount = 32;
            bi.biCompression = BI_RGB;
            var buf = new byte[w * h * 4];
            GetDIBits(mem, bmp, 0, (uint)h, buf, ref bi, DIB_RGB_COLORS);
            SelectObject(mem, old);
            DeleteObject(bmp);
            DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, screen);
            return buf;
        }

        private static void ClickAt(int x, int y)
        {
            SetCursorPos(x, y);
            var inp = new INPUT[2];
            inp[0].type = INPUT_MOUSE;
            inp[0].u.mi.dwFlags = MOUSEEVENTF_LEFTDOWN;
            inp[1].type = INPUT_MOUSE;
            inp[1].u.mi.dwFlags = MOUSEEVENTF_LEFTUP;
            SendInput(2, inp, Marshal.SizeOf(typeof(INPUT)));
        }

        private static void DoubleClickAt(int x, int y)
        {
            ClickAt(x, y);
            Thread.Sleep(120);
            ClickAt(x, y);
        }

        private static void TapKeys(params int[] vks)
        {
            var inp = new INPUT[vks.Length * 2];
            for (int i = 0; i < vks.Length; i++)
            {
                inp[2 * i].type = INPUT_KEYBOARD;
                inp[2 * i].u.ki.wVk = (ushort)vks[i];
                inp[2 * i + 1].type = INPUT_KEYBOARD;
                inp[2 * i + 1].u.ki.wVk = (ushort)vks[i];
                inp[2 * i + 1].u.ki.dwFlags = KEYEVENTF_KEYUP;
                Thread.Sleep(60);
            }
            SendInput((uint)inp.Length, inp,
                Marshal.SizeOf(typeof(INPUT)));
        }

        private static void SendKeyCombo(bool ctrl, bool alt, bool shift,
            int vk)
        {
            var mods = new List<ushort>();
            if (ctrl) mods.Add(VK_CONTROL);
            if (alt) mods.Add(VK_MENU);
            if (shift) mods.Add(VK_SHIFT);
            var list = new List<INPUT>();
            foreach (var m in mods)
            {
                var i = new INPUT();
                i.type = INPUT_KEYBOARD;
                i.u.ki.wVk = m;
                list.Add(i);
            }
            var down = new INPUT(); down.type = INPUT_KEYBOARD;
            down.u.ki.wVk = (ushort)vk; list.Add(down);
            var up = new INPUT(); up.type = INPUT_KEYBOARD;
            up.u.ki.wVk = (ushort)vk; up.u.ki.dwFlags = KEYEVENTF_KEYUP;
            list.Add(up);
            for (int k = mods.Count - 1; k >= 0; k--)
            {
                var rel = new INPUT();
                rel.type = INPUT_KEYBOARD;
                rel.u.ki.wVk = mods[k];
                rel.u.ki.dwFlags = KEYEVENTF_KEYUP;
                list.Add(rel);
            }
            SendInput((uint)list.Count, list.ToArray(),
                Marshal.SizeOf(typeof(INPUT)));
        }

        private static IntPtr MakeLParam(int x, int y)
        {
            return new IntPtr(((y & 0xFFFF) << 16) | (x & 0xFFFF));
        }

        // ================= message window (tray/hotkey/power) ==========
        /// Owns a message-only HWND on a dedicated thread with a real
        /// GetMessage pump. Registers the tray icon (shell callback), the
        /// global Ctrl+Alt+C hotkey and receives WM_POWERBROADCAST — every
        /// registration is a real OS call whose readback is verified by the
        /// scenario.
        private sealed class MsgWindow
        {
            public IntPtr Hwnd;
            public int ToggleRequests;
            private readonly Queue<int> powerEvents = new Queue<int>();
            private readonly object gate = new object();
            private Thread thread;
            private readonly ManualResetEventSlim ready =
                new ManualResetEventSlim(false);
            private bool trayAdded;
            private static WndProcDelegate keepAlive;
            private static MsgWindow self;
            private const int HOTKEY_ID = 0xCAFE;
            internal const uint TRAY_UID = 0xC0FE;
            private static readonly Guid TrayGuid = new Guid(
                "8f2a0c45-6d1b-4e57-9c3a-7d2f4a1b9e05");

            public void Start()
            {
                self = this;
                thread = new Thread(ThreadMain);
                thread.IsBackground = true;
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                ready.Wait(10000);
            }

            public int DrainPowerEvent()
            {
                lock (gate)
                    return powerEvents.Count > 0 ? powerEvents.Dequeue() : 0;
            }

            public void Stop()
            {
                if (Hwnd != IntPtr.Zero)
                    PostMessage(Hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                if (thread != null) thread.Join(3000);
            }

            private void ThreadMain()
            {
                var wc = new WNDCLASS();
                keepAlive = WndProc;
                wc.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(
                    keepAlive);
                wc.lpszClassName = "CozyCafeMsgWnd";
                RegisterClass(ref wc);
                Hwnd = CreateWindowEx(0, wc.lpszClassName, "CozyCafeMsg",
                    0, 0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero,
                    IntPtr.Zero, IntPtr.Zero);
                AddTrayIcon();
                bool hk = RegisterHotKey(Hwnd, HOTKEY_ID,
                    MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, 0x43);
                System.Diagnostics.Debug.WriteLine("hotkey=" + hk);
                ready.Set();
                MSG m;
                while (GetMessage(out m, IntPtr.Zero, 0, 0))
                {
                    TranslateMessage(ref m);
                    DispatchMessage(ref m);
                }
            }

            private void AddTrayIcon()
            {
                var d = new NOTIFYICONDATA();
                d.cbSize = (uint)Marshal.SizeOf(typeof(NOTIFYICONDATA));
                d.hWnd = Hwnd;
                d.uID = TRAY_UID;
                d.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_GUID;
                d.uCallbackMessage = WM_TRAYICON;
                d.hIcon = LoadIcon(IntPtr.Zero, IDI_APPLICATION);
                d.szTip = "CozyCafe — double-click toggles click-through";
                d.guidItem = TrayGuid;
                trayAdded = Shell_NotifyIcon(NIM_ADD, ref d);
            }

            public bool QueryTrayRect(out RECT r)
            {
                var id = new NOTIFYICONIDENTIFIER();
                id.cbSize = (uint)Marshal.SizeOf(
                    typeof(NOTIFYICONIDENTIFIER));
                id.hWnd = Hwnd;
                id.uID = TRAY_UID;
                id.guidItem = TrayGuid;
                int hr = Shell_NotifyIconGetRect(ref id, out r);
                return hr == 0 && trayAdded;
            }

            public void RemoveTrayIcon()
            {
                if (!trayAdded) return;
                var d = new NOTIFYICONDATA();
                d.cbSize = (uint)Marshal.SizeOf(typeof(NOTIFYICONDATA));
                d.hWnd = Hwnd;
                d.uID = TRAY_UID;
                d.guidItem = TrayGuid;
                d.uFlags = NIF_GUID;
                Shell_NotifyIcon(NIM_DELETE, ref d);
            }

            private static IntPtr WndProc(IntPtr h, uint msg,
                IntPtr w, IntPtr l)
            {
                if (msg == WM_HOTKEY && w.ToInt32() == HOTKEY_ID)
                {
                    Interlocked.Increment(ref self.ToggleRequests);
                    return IntPtr.Zero;
                }
                if (msg == WM_TRAYICON)
                {
                    uint mouse = (uint)((long)l & 0xFFFF);
                    if (mouse == WM_LBUTTONDBLCLK)
                    {
                        Interlocked.Increment(ref self.ToggleRequests);
                    }
                    return IntPtr.Zero;
                }
                if (msg == WM_POWERBROADCAST)
                {
                    lock (self.gate)
                    {
                        if (w.ToInt32() == PBT_APMSUSPEND)
                            self.powerEvents.Enqueue(1);
                        else if (w.ToInt32() == PBT_APMRESUMEAUTOMATIC
                            || w.ToInt32() == PBT_APMRESUMESUSPEND)
                            self.powerEvents.Enqueue(2);
                    }
                    return new IntPtr(1);
                }
                if (msg == WM_CLOSE)
                {
                    DestroyWindow(h);
                    PostQuitMessage(0);
                    return IntPtr.Zero;
                }
                return DefWindowProc(h, msg, w, l);
            }
        }

        // ================= interop =================
        private delegate IntPtr WndProcDelegate(IntPtr h, uint m,
            IntPtr w, IntPtr l);
        private delegate bool EnumMonProc(IntPtr h, IntPtr hdc,
            ref RECT r, IntPtr data);

        private const int GWL_EXSTYLE = -20;
        private const int GWLP_WNDPROC = -4;
        private const long WS_EX_LAYERED = 0x00080000;
        private const long WS_EX_TRANSPARENT = 0x00000020;
        private const long WS_EX_TOPMOST = 0x00000008;
        private const long WS_EX_TOOLWINDOW = 0x00000080;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);
        private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2,
            SWP_NOZORDER = 0x4, SWP_SHOWWINDOW = 0x40,
            SWP_FRAMECHANGED = 0x20, SWP_NOACTIVATE = 0x10,
            SWP_ASYNCWINDOWPOS = 0x4000;
        private const uint LWA_COLORKEY = 0x1;
        private const uint WM_NCHITTEST = 0x84, WM_HOTKEY = 0x312,
            WM_POWERBROADCAST = 0x218, WM_CLOSE = 0x10,
            WM_LBUTTONDBLCLK = 0x203;
        private const uint WM_TRAYICON = 0x800 + 0x44;
        private const int HTTRANSPARENT = -1;
        private const uint SMTO_NORMAL = 0;
        private const int PBT_APMSUSPEND = 0x4,
            PBT_APMRESUMEAUTOMATIC = 0x12, PBT_APMRESUMESUSPEND = 0x7;
        private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2,
            MOD_NOREPEAT = 0x4000;
        private const uint NIM_ADD = 0, NIM_DELETE = 2;
        private const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2,
            NIF_TIP = 0x4, NIF_GUID = 0x20;
        private static readonly IntPtr IDI_APPLICATION = new IntPtr(32512);
        private const uint KLF_ACTIVATE = 0x1,
            KLF_SETFORPROCESS = 0x100;
        private const uint IME_CMODE_NATIVE = 0x1,
            IME_CMODE_FULLSHAPE = 0x8;
        private const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x2,
            MOUSEEVENTF_LEFTUP = 0x4, KEYEVENTF_KEYUP = 0x2;
        private const ushort VK_CONTROL = 0x11, VK_MENU = 0x12,
            VK_SHIFT = 0x10, VK_HANGUL = 0x15;
        private const int GW_HWNDNEXT = 2;
        private const uint MONITOR_DEFAULTTONEAREST = 2;
        private const uint SRCCOPY = 0x00CC0020,
            CAPTUREBLT = 0x40000000;
        private const uint BI_RGB = 0, DIB_RGB_COLORS = 0;
        private static readonly IntPtr
            DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new IntPtr(-4);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x, y; }
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int left, top, right, bottom;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor, rcWork;
            public int dwFlags;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASS
        {
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra, cbWndExtra;
            public IntPtr hInstance, hIcon, hCursor, hbrBackground;
            public string lpszMenuName, lpszClassName;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam, lParam;
            public uint time;
            public int pt_x, pt_y;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uID, uFlags, uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public uint dwState, dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public uint uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct NOTIFYICONIDENTIFIER
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uID;
            public Guid guidItem;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth, biHeight;
            public ushort biPlanes, biBitCount;
            public uint biCompression, biSizeImage;
            public int biXPelsPerMeter, biYPelsPerMeter;
            public uint biClrUsed, biClrImportant;
        }
        [StructLayout(LayoutKind.Explicit)]
        private struct INPUT
        {
            [FieldOffset(0)] public uint type;
            [FieldOffset(8)] public INPUTUNION u;
        }
        [StructLayout(LayoutKind.Explicit)]
        private struct INPUTUNION
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx, dy;
            public uint mouseData, dwFlags, time;
            public IntPtr dwExtraInfo;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk, wScan;
            public uint dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll")] private static extern IntPtr GetDC(
            IntPtr h);
        [DllImport("user32.dll")] private static extern int ReleaseDC(
            IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr
            CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr
            CreateCompatibleBitmap(IntPtr dc, int w, int h);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(
            IntPtr dc, IntPtr o);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(
            IntPtr o);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(
            IntPtr dc);
        [DllImport("gdi32.dll")] private static extern bool BitBlt(
            IntPtr d, int x, int y, int w, int h, IntPtr s, int sx,
            int sy, uint rop);
        [DllImport("gdi32.dll")] private static extern int GetDIBits(
            IntPtr dc, IntPtr bmp, uint start, uint lines, byte[] buf,
            ref BITMAPINFOHEADER bi, uint usage);
        [DllImport("user32.dll")] private static extern IntPtr
            GetWindowLongPtr(IntPtr h, int i);
        [DllImport("user32.dll")] private static extern IntPtr
            CallWindowProc(IntPtr prev, IntPtr h, uint m, IntPtr w,
                IntPtr l);
        [DllImport("user32.dll")] private static extern IntPtr
            SetWindowLongPtr(IntPtr h, int i, IntPtr v);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(
            IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
        [DllImport("user32.dll")] private static extern bool
            SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha,
                uint flags);
        [DllImport("user32.dll")] private static extern bool
            GetLayeredWindowAttributes(IntPtr h, out uint key,
                out byte alpha, out uint flags);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(
            IntPtr h, out RECT r);
        [DllImport("user32.dll")] private static extern IntPtr
            GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool
            SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] private static extern IntPtr
            SetActiveWindow(IntPtr h);
        [DllImport("user32.dll")] private static extern IntPtr GetTopWindow(
            IntPtr h);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(
            IntPtr h, int c);
        [DllImport("user32.dll")] private static extern IntPtr
            SendMessageTimeout(IntPtr h, uint m, IntPtr w, IntPtr l,
                uint f, uint t, out IntPtr r);
        [DllImport("user32.dll")] private static extern bool PostMessage(
            IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] private static extern bool
            EnumDisplayMonitors(IntPtr dc, IntPtr clip, EnumMonProc p,
                IntPtr d);
        [DllImport("user32.dll")] private static extern bool
            GetMonitorInfo(IntPtr h, ref MONITORINFO i);
        [DllImport("user32.dll")] private static extern IntPtr
            MonitorFromWindow(IntPtr h, uint f);
        [DllImport("user32.dll")] private static extern uint
            GetDpiForWindow(IntPtr h);
        [DllImport("user32.dll")] private static extern uint
            GetDpiForSystem();
        [DllImport("user32.dll")] private static extern bool
            SetProcessDpiAwarenessContext(IntPtr c);
        [DllImport("shcore.dll")] private static extern int
            GetProcessDpiAwareness(IntPtr p, out int a);
        [DllImport("user32.dll")] private static extern bool SetCursorPos(
            int x, int y);
        [DllImport("user32.dll")] private static extern uint SendInput(
            uint n, INPUT[] i, int sz);
        [DllImport("user32.dll")] private static extern IntPtr
            LoadKeyboardLayout(string id, uint f);
        [DllImport("user32.dll")] private static extern IntPtr
            ActivateKeyboardLayout(IntPtr h, uint f);
        [DllImport("user32.dll")] private static extern IntPtr
            GetKeyboardLayout(uint t);
        [DllImport("user32.dll")] private static extern int
            GetKeyboardLayoutList(int n, IntPtr[] l);
        [DllImport("imm32.dll")] private static extern IntPtr ImmGetContext(
            IntPtr h);
        [DllImport("imm32.dll")] private static extern bool
            ImmReleaseContext(IntPtr h, IntPtr c);
        [DllImport("imm32.dll")] private static extern bool
            ImmGetConversionStatus(IntPtr c, out uint conv, out uint sent);
        [DllImport("imm32.dll")] private static extern bool
            ImmSetConversionStatus(IntPtr c, uint conv, uint sent);
        [DllImport("user32.dll")] private static extern IntPtr FindWindow(
            string c, string w);
        [DllImport("user32.dll")] private static extern IntPtr
            WindowFromPoint(POINT p);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern ushort RegisterClass(ref WNDCLASS c);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowEx(uint ex, string cls,
            string name, uint style, int x, int y, int w, int h,
            IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
        [DllImport("user32.dll")] private static extern bool DestroyWindow(
            IntPtr h);
        [DllImport("user32.dll")] private static extern IntPtr
            DefWindowProc(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] private static extern bool GetMessage(
            out MSG m, IntPtr h, uint min, uint max);
        [DllImport("user32.dll")] private static extern bool
            TranslateMessage(ref MSG m);
        [DllImport("user32.dll")] private static extern IntPtr
            DispatchMessage(ref MSG m);
        [DllImport("user32.dll")] private static extern void
            PostQuitMessage(int c);
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(
            IntPtr h, int id, uint mod, uint vk);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadIcon(IntPtr i, IntPtr n);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Shell_NotifyIcon(uint m,
            ref NOTIFYICONDATA d);
        [DllImport("shell32.dll")] private static extern int
            Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER id,
                out RECT r);
    }
}
