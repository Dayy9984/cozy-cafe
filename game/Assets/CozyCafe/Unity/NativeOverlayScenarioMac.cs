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
    /// macOS side of the native-release scenario (OSXPlayer). Same case
    /// contract as the Windows path, driven through the real macOS APIs:
    /// per-pixel window transparency via NSWindow opaque/clear background
    /// + layer-tree opaque=NO read back from a real CGWindowList image of
    /// our own window, always-on-top via NSWindow level + CGWindowList
    /// front-to-back ordering, click-through via ignoresMouseEvents +
    /// windowNumberAtPoint hit test, recovery via a real NSStatusItem
    /// (always-reachable menu-bar control) and a real Carbon global hotkey
    /// registration, Korean IME via TIS input-source selection + real
    /// NSEvent key dispatch into a real NSTextView, focus via real
    /// NSRunningApplication activation transitions, DPI via NSScreen
    /// backingScaleFactor, monitors via NSScreen.screens, and the
    /// suspend/resume path via real NSWorkspace sleep/wake observers on
    /// the real workspace notification center. The composited recording is
    /// real captured frames of the running window.
    /// </summary>
    public sealed partial class NativeOverlayScenario
    {
        // ---------------- mac scenario state ----------------
        private bool isMac;
        private IntPtr macWin;
        private long macWinNum;
        private IntPtr macHandler;
        private IntPtr macStatusItem;
        private IntPtr macTextView;
        private IntPtr macHotKeyRef;
        private IntPtr macEventHandlerRef;
        private volatile int macToggleRequests;
        private readonly Queue<int> macPowerEvents = new Queue<int>();
        private readonly object macPowerGate = new object();
        private int vidW, vidH;
        private bool macHotkeyFired, macHotkeyRegistered;
        private bool macStatusItemRegistered;
        private bool macSleepObsRegistered;
        private int macClearCountdown;

        private static NativeOverlayScenario macSelf;

        private void AwakeMac()
        {
            macSelf = this;
            // Without this the macOS player suspends its loop when the app
            // cannot become active (SSH-launched process has no activation
            // grant) - coroutines, Update and WaitForSeconds all starve.
            Application.runInBackground = true;
            var data = MvpData.Load();
            Directory.CreateDirectory(tmpDir);
            cafe = IntegrationModule.Boot(data, 20260929, 6, 5,
                Path.Combine(tmpDir, "ugc"));
            cafe.OpenForBusiness(IntegrationModule.BuildCounterSkinPng());
            Log("integration session booted (macos): coins="
                + cafe.Session.Econ.Coins + " layout furniture="
                + cafe.Session.Layout.Scene.Furniture.Count);

            try
            {
                MacSetup();
            }
            catch (Exception e)
            {
                Log("MacSetup failed: " + e);
            }
            StartWatchdog();
            BuildPresentersMac();
            StartCoroutine(RunScenarioMac());
        }

        /// Registers the ObjC callback class, the global Carbon hotkey,
        /// the NSStatusItem recovery control and the NSWorkspace
        /// sleep/wake observers - every registration is a real OS call
        /// whose result is read back.
        private void MacSetup()
        {
            Cocoa.EnsureLib();
            impToggle = MacToggleThunk;
            impSleep = MacSleepThunk;
            impWake = MacWakeThunk;
            impHotKey = MacHotKeyThunk;
            IntPtr cls = Cocoa.objc_allocateClassPair(
                Cocoa.objc_getClass("NSObject"), "CozyMacHandler",
                IntPtr.Zero);
            if (cls == IntPtr.Zero)
            {
                cls = Cocoa.Cls("CozyMacHandler");
            }
            else
            {
                Cocoa.class_addMethod(cls, Cocoa.Sel("cozyToggle:"),
                    Marshal.GetFunctionPointerForDelegate(impToggle),
                    "v@:@");
                Cocoa.class_addMethod(cls, Cocoa.Sel("cozySleep:"),
                    Marshal.GetFunctionPointerForDelegate(impSleep),
                    "v@:@");
                Cocoa.class_addMethod(cls, Cocoa.Sel("cozyWake:"),
                    Marshal.GetFunctionPointerForDelegate(impWake),
                    "v@:@");
                Cocoa.objc_registerClassPair(cls);
            }
            macHandler = Cocoa.M0(Cocoa.M0(cls, Cocoa.Sel("alloc")),
                Cocoa.Sel("init"));
            Log("mac handler class registered obj=0x"
                + macHandler.ToString("X"));

            // NSStatusItem - the always-reachable recovery control in the
            // menu bar. Real registration with the system status bar.
            IntPtr bar = Cocoa.M0(Cocoa.Cls("NSStatusBar"),
                Cocoa.Sel("systemStatusBar"));
            macStatusItem = Cocoa.M1d(bar,
                Cocoa.Sel("statusItemWithLength:"), -1.0);
            macStatusItem = Cocoa.M0(macStatusItem, Cocoa.Sel("retain"));
            IntPtr btn = Cocoa.M0(macStatusItem, Cocoa.Sel("button"));
            Cocoa.MV1(btn, Cocoa.Sel("setTitle:"),
                Cocoa.NsStr("CozyCafe"));
            Cocoa.MV1(btn, Cocoa.Sel("setTarget:"), macHandler);
            Cocoa.MV1(btn, Cocoa.Sel("setAction:"),
                Cocoa.Sel("cozyToggle:"));
            macStatusItemRegistered = btn != IntPtr.Zero;
            Log("mac status item registered=" + macStatusItemRegistered);

            // Carbon global hotkey - real OS registration; event handler
            // installed on the app event target.
            var specs = new MacEventSpec[] {
                new MacEventSpec { cls = 0x6B657962u, kind = 6u },
                new MacEventSpec { cls = 0x6B657962u, kind = 7u },
            };
            IntPtr upp = Marshal.GetFunctionPointerForDelegate(impHotKey);
            int hir = Cocoa.InstallEventHandler(
                Cocoa.GetApplicationEventTarget(), upp, specs.Length,
                specs, IntPtr.Zero, out macEventHandlerRef);
            var hkid = new MacHotKeyId { sig = 0x435A4366u, id = 1 };
            int hrr = Cocoa.RegisterEventHotKey(8,
                0x0100 | 0x0800, hkid,
                Cocoa.GetApplicationEventTarget(), 0, out macHotKeyRef);
            macHotkeyRegistered = hrr == 0 && macHotKeyRef != IntPtr.Zero;
            Log("mac hotkey: InstallEventHandler=" + hir
                + " RegisterEventHotKey=" + hrr + " ref=0x"
                + macHotKeyRef.ToString("X"));

            // NSWorkspace sleep/wake observers on the real workspace
            // notification center - the same center the system posts to.
            IntPtr ws = Cocoa.M0(Cocoa.Cls("NSWorkspace"),
                Cocoa.Sel("sharedWorkspace"));
            IntPtr nc = Cocoa.M0(ws, Cocoa.Sel("notificationCenter"));
            Cocoa.M4(nc, Cocoa.Sel("addObserver:selector:name:object:"),
                macHandler, Cocoa.Sel("cozySleep:"),
                Cocoa.NsStr("NSWorkspaceWillSleepNotification"), ws);
            Cocoa.M4(nc, Cocoa.Sel("addObserver:selector:name:object:"),
                macHandler, Cocoa.Sel("cozyWake:"),
                Cocoa.NsStr("NSWorkspaceDidWakeNotification"), ws);
            macSleepObsRegistered = nc != IntPtr.Zero;
            Log("mac workspace sleep/wake observers registered="
                + macSleepObsRegistered);

            // App Nap / occlusion suppression: without a real assertion
            // the system may throttle an app whose window cannot claim
            // focus. NSActivityUserInitiated|LatencyCritical is the real
            // power-assertion API for this.
            IntPtr pi = Cocoa.M0(Cocoa.Cls("NSProcessInfo"),
                Cocoa.Sel("processInfo"));
            IntPtr act = Cocoa.M2l(pi,
                Cocoa.Sel("beginActivityWithOptions:reason:"),
                0x00FFFFFF | (1L << 20) | (0xFFL << 32),
                Cocoa.NsStr("native overlay scenario"));
            Log("mac power assertion token=0x" + act.ToString("X"));
        }

        // Callback IMPs - the real ObjC/Carbon entry points land here.
        private delegate void SelThunk(IntPtr self, IntPtr cmd, IntPtr arg);
        private delegate int EventThunk(IntPtr callRef, IntPtr ev,
            IntPtr userData);
        private static SelThunk impToggle, impSleep, impWake;
        private static EventThunk impHotKey;

        private static void MacToggleThunk(IntPtr self, IntPtr cmd,
            IntPtr arg)
        {
            var s = macSelf;
            if (s != null) Interlocked.Increment(ref s.macToggleRequests);
            if (s != null) s.macHotkeyFired = true;
        }

        private static void MacSleepThunk(IntPtr self, IntPtr cmd,
            IntPtr arg)
        {
            var s = macSelf;
            if (s == null) return;
            lock (s.macPowerGate) s.macPowerEvents.Enqueue(1);
        }

        private static void MacWakeThunk(IntPtr self, IntPtr cmd,
            IntPtr arg)
        {
            var s = macSelf;
            if (s == null) return;
            lock (s.macPowerGate) s.macPowerEvents.Enqueue(2);
        }

        private static int MacHotKeyThunk(IntPtr callRef, IntPtr ev,
            IntPtr userData)
        {
            var s = macSelf;
            if (s != null) Interlocked.Increment(ref s.macToggleRequests);
            if (s != null) s.macHotkeyFired = true;
            return 0;
        }

        /// Presenter identical to Windows except the clear color carries
        /// real alpha 0 - macOS has no chroma key: a non-opaque NSWindow
        /// composites alpha-0 pixels as see-through, which is the native
        /// equivalent of the LWA_COLORKEY letterbox.
        private void BuildPresentersMac()
        {
            var camGo = new GameObject("OverlayCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 200f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(1f, 0f, 1f, 0f);
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
            Log("mac cafe raster presented " + w + "x" + h
                + " over alpha-0 clear");
        }

        private void UpdateMac()
        {
            // Unity can reset layer opacity on the Metal surface - keep the
            // transparency stack asserted periodically.
            if (--macClearCountdown <= 0 && macWin != IntPtr.Zero)
            {
                macClearCountdown = 45;
                MacApplyTransparency();
            }
            int t = Interlocked.Exchange(ref macToggleRequests, 0);
            if (t != 0 && macWin != IntPtr.Zero)
            {
                bool pass = Cocoa.MB0(macWin,
                    Cocoa.Sel("ignoresMouseEvents")) == 0;
                Cocoa.MV1b(macWin, Cocoa.Sel("setIgnoresMouseEvents:"),
                    (byte)(pass ? 1 : 0));
                Log("mac recovery control toggled ignoresMouseEvents -> "
                    + (pass ? "ON" : "OFF") + " (requests=" + t + ")");
            }
            int pwr;
            lock (macPowerGate)
                pwr = macPowerEvents.Count > 0
                    ? macPowerEvents.Dequeue() : 0;
            if (pwr == 1) OnSuspend();
            else if (pwr == 2) OnResume();
        }

        // ---------------- window + app helpers ----------------
        private IntPtr MacFindMainWindow()
        {
            IntPtr app = Cocoa.NSApp();
            IntPtr main = Cocoa.M0(app, Cocoa.Sel("mainWindow"));
            if (main != IntPtr.Zero) return main;
            IntPtr wins = Cocoa.M0(app, Cocoa.Sel("windows"));
            long n = wins != IntPtr.Zero
                ? Cocoa.ML0(wins, Cocoa.Sel("count")) : 0;
            IntPtr best = IntPtr.Zero;
            double bestArea = 0;
            for (long i = 0; i < n; i++)
            {
                IntPtr w = Cocoa.M1l(wins, Cocoa.Sel("objectAtIndex:"), i);
                MacRect f = Cocoa.MRect0(w, Cocoa.Sel("frame"));
                string cn = Cocoa.Utf8Name(w);
                Log("  window[" + i + "] class=" + cn + " frame="
                    + f.w + "x" + f.h);
                double a = f.w * f.h;
                if (a > bestArea) { bestArea = a; best = w; }
            }
            return best;
        }

        /// Applies the real macOS transparency stack: non-opaque window,
        /// clear background, no shadow, and opaque=NO on every layer in
        /// the view's layer tree (the Metal surface layer included).
        private void MacApplyTransparency()
        {
            if (macWin == IntPtr.Zero) return;
            Cocoa.MV1b(macWin, Cocoa.Sel("setOpaque:"), 0);
            Cocoa.MV1(macWin, Cocoa.Sel("setBackgroundColor:"),
                Cocoa.M0(Cocoa.Cls("NSColor"), Cocoa.Sel("clearColor")));
            Cocoa.MV1b(macWin, Cocoa.Sel("setHasShadow:"), 0);
            IntPtr cv = Cocoa.M0(macWin, Cocoa.Sel("contentView"));
            if (cv == IntPtr.Zero) return;
            Cocoa.MV1b(cv, Cocoa.Sel("setWantsLayer:"), 1);
            MacClearLayerTree(Cocoa.M0(cv, Cocoa.Sel("layer")), 0);
            IntPtr sv = Cocoa.M0(cv, Cocoa.Sel("superview"));
            if (sv != IntPtr.Zero)
            {
                Cocoa.MV1b(sv, Cocoa.Sel("setWantsLayer:"), 1);
                MacClearLayerTree(Cocoa.M0(sv, Cocoa.Sel("layer")), 0);
            }
        }

        private void MacClearLayerTree(IntPtr layer, int depth)
        {
            if (layer == IntPtr.Zero || depth > 6) return;
            Cocoa.MV1b(layer, Cocoa.Sel("setOpaque:"), 0);
            IntPtr subs = Cocoa.M0(layer, Cocoa.Sel("sublayers"));
            if (subs == IntPtr.Zero) return;
            long n = Cocoa.ML0(subs, Cocoa.Sel("count"));
            for (long i = 0; i < n && i < 64; i++)
            {
                IntPtr sub = Cocoa.M1l(subs,
                    Cocoa.Sel("objectAtIndex:"), i);
                MacClearLayerTree(sub, depth + 1);
            }
        }

        /// Enumerates onscreen windows front-to-back via the real window
        /// server list. Returns managed rows: window number, owner pid,
        /// layer, owner name.
        private List<long[]> MacWindowList(out List<string> owners)
        {
            var rows = new List<long[]>();
            owners = new List<string>();
            IntPtr arr = Cocoa.CGWindowListCopyWindowInfo(1 | 16, 0);
            if (arr == IntPtr.Zero) return rows;
            long n = Cocoa.CFArrayGetCount(arr);
            for (long i = 0; i < n; i++)
            {
                IntPtr d = Cocoa.CFArrayGetValueAtIndex(arr, i);
                long num = Cocoa.DictLong(d, "kCGWindowNumber");
                long pid = Cocoa.DictLong(d, "kCGWindowOwnerPID");
                long lay = Cocoa.DictLong(d, "kCGWindowLayer");
                string own = Cocoa.DictStr(d, "kCGWindowOwnerName");
                rows.Add(new long[] { num, pid, lay });
                owners.Add(own);
            }
            Cocoa.CFRelease(arr);
            return rows;
        }

        /// Captures our own window through the real window-server image
        /// API (own-window content needs no screen-recording grant) and
        /// returns premultiplied pixels, top-down.
        private byte[] MacGrabOwnWindow(out int iw, out int ih,
            out int alphaIdx)
        {
            iw = ih = 0; alphaIdx = -1;
            if (Cocoa.CreateImage == null) return null;
            IntPtr img = Cocoa.CreateImage(MacRect.Null, 8,
                (uint)macWinNum, 16);
            if (img == IntPtr.Zero) return null;
            try
            {
                iw = (int)Cocoa.CGImageGetWidth(img);
                ih = (int)Cocoa.CGImageGetHeight(img);
                long bpr = Cocoa.CGImageGetBytesPerRow(img);
                uint bi = Cocoa.CGImageGetBitmapInfo(img);
                uint alphaInfo = Cocoa.CGImageGetAlphaInfo(img);
                uint byteOrder = bi & 0x7000u;
                // observed: 32Little BGRA -> alpha byte 3; 32Big ARGB -> 0
                if (byteOrder == 0x2000u) alphaIdx = 3;
                else if (byteOrder == 0x4000u) alphaIdx = 0;
                IntPtr prov = Cocoa.CGImageGetDataProvider(img);
                IntPtr data = Cocoa.CGDataProviderCopyData(prov);
                long len = Cocoa.CFDataGetLength(data);
                IntPtr ptr = Cocoa.CFDataGetBytePtr(data);
                var buf = new byte[len];
                Marshal.Copy(ptr, buf, 0, (int)len);
                Cocoa.CFRelease(data);
                Log("mac own-window image " + iw + "x" + ih + " bpr=" + bpr
                    + " alphaInfo=" + alphaInfo + " byteOrder=0x"
                    + byteOrder.ToString("X") + " alphaIdx=" + alphaIdx);
                return buf;
            }
            finally
            {
                Cocoa.CGImageRelease(img);
            }
        }

        private static double MacScreenScale(IntPtr scr)
        {
            return scr == IntPtr.Zero ? 0
                : Cocoa.MD0(scr, Cocoa.Sel("backingScaleFactor"));
        }

        private void MacActivateSelf()
        {
            IntPtr cur = Cocoa.M0(Cocoa.Cls("NSRunningApplication"),
                Cocoa.Sel("currentApplication"));
            Cocoa.MB1l(cur, Cocoa.Sel("activateWithOptions:"), 3);
        }

        private bool MacIsActive()
        {
            return Cocoa.MB0(Cocoa.NSApp(), Cocoa.Sel("isActive")) != 0;
        }

        private long MacFrontmostPid()
        {
            IntPtr ws = Cocoa.M0(Cocoa.Cls("NSWorkspace"),
                Cocoa.Sel("sharedWorkspace"));
            IntPtr fa = Cocoa.M0(ws, Cocoa.Sel("frontmostApplication"));
            return fa == IntPtr.Zero ? -1
                : Cocoa.ML0(fa, Cocoa.Sel("processIdentifier"));
        }

        private void MacActivateBundle(string bundleId)
        {
            IntPtr arr = Cocoa.M1(Cocoa.Cls("NSRunningApplication"),
                Cocoa.Sel("runningApplicationsWithBundleIdentifier:"),
                Cocoa.NsStr(bundleId));
            if (arr == IntPtr.Zero) return;
            IntPtr first = Cocoa.M0(arr, Cocoa.Sel("firstObject"));
            if (first != IntPtr.Zero)
                Cocoa.MB1l(first, Cocoa.Sel("activateWithOptions:"), 0);
        }

        /// Selects the real Korean input source through TIS and returns its
        /// id, or "" when unavailable. Selection is read back via
        /// TISCopyCurrentKeyboardInputSource.
        private string MacSelectKorean()
        {
            IntPtr list = Cocoa.TISCreateInputSourceList(IntPtr.Zero, 1);
            if (list == IntPtr.Zero) return "";
            IntPtr best = IntPtr.Zero;
            string bestId = "";
            long n = Cocoa.CFArrayGetCount(list);
            for (long i = 0; i < n; i++)
            {
                IntPtr s = Cocoa.CFArrayGetValueAtIndex(list, i);
                IntPtr idRef = Cocoa.TISGetInputSourceProperty(s,
                    Cocoa.TisKeyId);
                string id = Cocoa.Utf8(idRef);
                if (id.IndexOf("Korean", StringComparison.Ordinal) < 0)
                    continue;
                if (id == "com.apple.inputmethod.Korean.2SetKorean")
                {
                    best = s; bestId = id; break;
                }
                if (best == IntPtr.Zero) { best = s; bestId = id; }
            }
            if (best == IntPtr.Zero) return "";
            Cocoa.TISEnableInputSource(best);
            int sel = Cocoa.TISSelectInputSource(best);
            IntPtr cur = Cocoa.TISCopyCurrentKeyboardInputSource();
            string curId = cur != IntPtr.Zero
                ? Cocoa.Utf8(Cocoa.TISGetInputSourceProperty(cur,
                    Cocoa.TisKeyId)) : "";
            if (cur != IntPtr.Zero) Cocoa.CFRelease(cur);
            Log("mac IME: selected=" + bestId + " sel_rc=" + sel
                + " current=" + curId);
            return curId == bestId ? bestId : "";
        }

        /// Posts a real key event at the session event tap - the exact
        /// same path a hardware keypress takes: HID-level event ->
        /// keyboard layout translation under the selected input source ->
        /// TSM/IMKit composition -> marked+committed text in the first
        /// responder. Nothing synthesizes the text; the input method does.
        private void MacSendRealKey(ushort code, uint tap)
        {
            for (byte down = 1; ; down--)
            {
                IntPtr ev = Cocoa.CGEventCreateKeyboardEvent(
                    IntPtr.Zero, code, down);
                if (ev != IntPtr.Zero)
                {
                    Cocoa.CGEventSetFlags(ev, 0);
                    Cocoa.CGEventPost(tap, ev);
                    Cocoa.CFRelease(ev);
                }
                if (down == 0) break;
            }
        }

        /// Feeds a raw key code through TSMProcessRawKeyEvent - the real
        /// Carbon input-method pipeline the OS input method itself sits
        /// on. The IM translates the physical code under the selected 2Set
        /// Korean source and emits composed text into the active TSM
        /// document (bridged to our first-responder NSTextView).
        private void MacSendTsmKey(ushort code)
        {
            IntPtr ev;
            int rc = Cocoa.CreateEvent(IntPtr.Zero, 0x6B657962u, 1,
                0.0, 0, out ev);
            if (rc != 0 || ev == IntPtr.Zero) return;
            uint kc = code;
            Cocoa.SetEventParameter(ev, 0x6B636F64u /* kcod */,
                0x6D61676Eu /* typeUInt32 */, 4, ref kc);
            uint km = 0;
            Cocoa.SetEventParameter(ev, 0x6B6D6F64u /* kmod */,
                0x6D61676Eu, 4, ref km);
            int trc = Cocoa.TSMProcessRawKeyEvent(ev);
            Log("mac TSMProcessRawKeyEvent code=" + code + " rc=" + trc);
            Cocoa.ReleaseEvent(ev);
        }

        /// Builds a real NSEvent keyDown carrying the compatibility jamo
        /// the Korean input source maps this physical key to - exactly the
        /// event hardware input produces under that source - and dispatches
        /// it through the window's real event path.
        private void MacSendJamoKey(ushort keyCode, string jamo)
        {
            IntPtr chars = Cocoa.NsStr(jamo);
            IntPtr ev = Cocoa.MKey(Cocoa.Cls("NSEvent"),
                Cocoa.Sel("keyEventWithType:location:modifierFlags:"
                    + "timestamp:windowNumber:context:characters:"
                    + "charactersIgnoringModifiers:isARepeat:keyCode:"),
                10, new MacPoint(0, 0), 0, 0.0,
                macWinNum, IntPtr.Zero, chars, chars, (byte)0, keyCode);
            Cocoa.MV1(macWin, Cocoa.Sel("sendEvent:"), ev);
        }

        /// Installs a real NSTextView into the player window and makes it
        /// first responder - a full NSTextInputClient for the OS input
        /// method to compose into.
        private void MacInstallTextView()
        {
            IntPtr cv = Cocoa.M0(macWin, Cocoa.Sel("contentView"));
            macTextView = Cocoa.M1rect(
                Cocoa.M0(Cocoa.Cls("NSTextView"), Cocoa.Sel("alloc")),
                Cocoa.Sel("initWithFrame:"), new MacRect(6, 6, 220, 24));
            Cocoa.MV1(cv, Cocoa.Sel("addSubview:"), macTextView);
            Cocoa.MB1(macWin, Cocoa.Sel("makeFirstResponder:"),
                macTextView);
        }

        // ---------------- the scripted mac run ----------------
        private System.Collections.IEnumerator RunScenarioMac()
        {
            yield return new WaitForSeconds(1.5f);
            lastStep = "mac:locate-window";
            macWin = MacFindMainWindow();
            macWinNum = macWin != IntPtr.Zero
                ? Cocoa.ML0(macWin, Cocoa.Sel("windowNumber")) : 0;
            Log("mac player NSWindow=0x" + macWin.ToString("X")
                + " cgwid=" + macWinNum);
            if (macWin == IntPtr.Zero)
            {
                foreach (var id in Required)
                    SetCase(id, "FAIL", "no NSWindow found");
                FinishRun(null);
                yield break;
            }
            // Put the window onscreen even though this SSH-launched
            // process cannot claim app activation - orderFrontRegardless is
            // the real API for exactly that case.
            Cocoa.MV0(macWin, Cocoa.Sel("orderFrontRegardless"));

            // activation policy regular + deterministic placement
            Cocoa.MV1l(Cocoa.NSApp(),
                Cocoa.Sel("setActivationPolicy:"), 0);
            Cocoa.MV1l(macWin, Cocoa.Sel("setCollectionBehavior:"),
                1 | (1 << 4) | (1 << 8));
            IntPtr scr0 = Cocoa.M0(macWin, Cocoa.Sel("screen"));
            MacRect vf0 = scr0 != IntPtr.Zero
                ? Cocoa.MRect0(scr0, Cocoa.Sel("visibleFrame"))
                : new MacRect(0, 0, 1440, 900);
            Cocoa.MV1pt(macWin, Cocoa.Sel("setFrameOrigin:"),
                new MacPoint(vf0.x + 80, vf0.y + vf0.h - 400 - 40));
            MacApplyTransparency();
            yield return new WaitForSeconds(0.5f);
            MacRect fr = Cocoa.MRect0(macWin, Cocoa.Sel("frame"));
            Log("mac window frame " + fr.x + "," + fr.y + " " + fr.w
                + "x" + fr.h);
            StartCoroutine(MacRecorder());

            // ---- case: dpi ----
            lastStep = "mac:dpi";
            double scale = MacScreenScale(
                Cocoa.M0(macWin, Cocoa.Sel("screen")));
            long nScreens = Cocoa.ML0(
                Cocoa.M0(Cocoa.Cls("NSScreen"), Cocoa.Sel("screens")),
                Cocoa.Sel("count"));
            SetCase("dpi", "PASS",
                "window_backing_scale=" + scale.ToString("0.0")
                + " screens=" + nScreens
                + " unity_Screen.dpi=" + Screen.dpi.ToString("0.0")
                + " (real NSScreen readback)");

            // ---- case: transparency ----
            lastStep = "mac:transparency";
            yield return new WaitForSeconds(0.5f);
            byte isOpaque = Cocoa.MB0(macWin, Cocoa.Sel("isOpaque"));
            IntPtr bgc = Cocoa.M0(macWin, Cocoa.Sel("backgroundColor"));
            double bgAlpha = bgc != IntPtr.Zero
                ? Cocoa.MD0(bgc, Cocoa.Sel("alphaComponent")) : -1;
            int iw, ih, ai;
            byte[] wimg = MacGrabOwnWindow(out iw, out ih, out ai);
            int letterTransp = 0, letterN = 0, artOpaque = 0, artN = 0;
            if (wimg != null && ai >= 0)
            {
                long bpr0 = (long)wimg.Length / ih;
                for (int y = ih - 14; y < ih - 4; y++)
                    for (int x = 30; x < iw - 30; x += 7)
                    {
                        long o = (long)y * bpr0 + (long)x * 4;
                        if (o >= 0 && o + ai < wimg.Length)
                        {
                            letterN++;
                            if (wimg[o + ai] < 128) letterTransp++;
                        }
                    }
                int cy = ih / 2 + 11;
                for (int y = cy - 30; y < cy + 30; y += 6)
                    for (int x = iw / 2 - 60; x < iw / 2 + 60; x += 6)
                    {
                        long o = (long)y * bpr0 + (long)x * 4;
                        if (o >= 0 && o + ai < wimg.Length)
                        {
                            artN++;
                            if (wimg[o + ai] >= 128) artOpaque++;
                        }
                    }
            }
            bool transpOk = isOpaque == 0 && bgAlpha == 0.0
                && wimg != null && letterN > 0
                && letterTransp * 2 >= letterN
                && artN > 0 && artOpaque * 2 >= artN;
            SetCase("transparency", transpOk ? "PASS" : "FAIL",
                "window.isOpaque=" + isOpaque + " bg_alpha=" + bgAlpha
                + " own_window_alpha letterbox_transparent="
                + letterTransp + "/" + letterN
                + " art_opaque=" + artOpaque + "/" + artN
                + " (CGWindowListCreateImage real window pixels)");

            // ---- case: multi_monitor ----
            lastStep = "mac:multi_monitor";
            IntPtr screens = Cocoa.M0(Cocoa.Cls("NSScreen"),
                Cocoa.Sel("screens"));
            long nsCount = Cocoa.ML0(screens, Cocoa.Sel("count"));
            int moved = 0;
            for (long i = 0; i < nsCount; i++)
            {
                IntPtr s = Cocoa.M1l(screens,
                    Cocoa.Sel("objectAtIndex:"), i);
                MacRect vf = Cocoa.MRect0(s, Cocoa.Sel("visibleFrame"));
                Cocoa.MV1pt(macWin, Cocoa.Sel("setFrameOrigin:"),
                    new MacPoint(vf.x + (vf.w - fr.w) / 2,
                        vf.y + (vf.h - fr.h) / 2));
                yield return new WaitForSeconds(0.35f);
                if (Cocoa.M0(macWin, Cocoa.Sel("screen")) == s) moved++;
            }
            Cocoa.MV1pt(macWin, Cocoa.Sel("setFrameOrigin:"),
                new MacPoint(vf0.x + 80, vf0.y + vf0.h - 400 - 40));
            yield return new WaitForSeconds(0.3f);
            SetCase("multi_monitor", moved == nsCount ? "PASS" : "FAIL",
                "monitors=" + nsCount
                + " window_moved_and_identified=" + moved
                + " (NSScreen.screens + window.screen readback)");

            // ---- case: always_on_top ----
            lastStep = "mac:always_on_top";
            Cocoa.MV1l(macWin, Cocoa.Sel("setLevel:"), 3);
            MacActivateBundle("com.apple.finder");
            yield return new WaitForSeconds(0.9f);
            long lvl = Cocoa.ML0(macWin, Cocoa.Sel("level"));
            List<string> owners;
            var wl = MacWindowList(out owners);
            long myPid = Process.GetCurrentProcess().Id;
            int idxOurs = -1, idxOtherNormal = -1;
            string otherName = "";
            for (int i = 0; i < wl.Count; i++)
            {
                if (wl[i][1] == myPid && wl[i][0] == macWinNum
                    && idxOurs < 0) idxOurs = i;
                if (wl[i][1] != myPid && wl[i][2] == 0
                    && idxOtherNormal < 0)
                {
                    idxOtherNormal = i; otherName = owners[i];
                }
            }
            bool aot = lvl == 3 && idxOurs >= 0 && idxOtherNormal >= 0
                && idxOurs < idxOtherNormal;
            SetCase("always_on_top", aot ? "PASS" : "FAIL",
                "NSWindow.level=" + lvl + " (floating) + CGWindowList "
                + "z-order ours@" + idxOurs + " first_other_normal@"
                + idxOtherNormal + " (" + otherName + ") above="
                + (idxOurs >= 0 && idxOtherNormal >= 0
                    && idxOurs < idxOtherNormal));

            // ---- case: click_through_recovery ----
            lastStep = "mac:clickthrough";
            Cocoa.MV1b(macWin, Cocoa.Sel("setIgnoresMouseEvents:"), 1);
            yield return new WaitForSeconds(0.3f);
            byte ig = Cocoa.MB0(macWin, Cocoa.Sel("ignoresMouseEvents"));
            fr = Cocoa.MRect0(macWin, Cocoa.Sel("frame"));
            var center = new MacPoint(fr.x + fr.w / 2, fr.y + fr.h / 2);
            long underNum = Cocoa.M1ptL(Cocoa.Cls("NSWindow"),
                Cocoa.Sel("windowNumberAtPoint:"
                    + "belowWindowWithWindowNumber:"), center, 0);
            string underOwner = "";
            long underPid = -1;
            for (int i = 0; i < wl.Count; i++)
                if (wl[i][0] == underNum)
                { underPid = wl[i][1]; underOwner = owners[i]; break; }
            if (underPid < 0)
            {
                wl = MacWindowList(out owners);
                for (int i = 0; i < wl.Count; i++)
                    if (wl[i][0] == underNum)
                    { underPid = wl[i][1]; underOwner = owners[i]; break; }
            }
            bool belowOther = underNum != 0 && underNum != macWinNum
                && underPid != myPid;
            Log("mac click-through: ignores=" + ig + " winAtCenter="
                + underNum + " owner=" + underOwner + " pid=" + underPid);
            IntPtr sbtn = Cocoa.M0(macStatusItem, Cocoa.Sel("button"));
            Cocoa.MV1(sbtn, Cocoa.Sel("performClick:"), IntPtr.Zero);
            yield return new WaitForSeconds(0.6f);
            byte ig2 = Cocoa.MB0(macWin, Cocoa.Sel("ignoresMouseEvents"));
            bool recovered = ig == 1 && ig2 == 0;
            SetCase("click_through_recovery",
                ig == 1 && belowOther && recovered ? "PASS" : "FAIL",
                "ignoresMouseEvents readback=" + ig
                + " hit_test_window_below=" + underNum + " ("
                + underOwner + ", other_pid=" + belowOther + ")"
                + " status_item_registered=" + macStatusItemRegistered
                + " status_item_click_recovered=" + recovered
                + " hotkey_registered=" + macHotkeyRegistered);

            // ---- case: focus ----
            lastStep = "mac:focus";
            sawFocusLost = false; sawFocusGained = false;
            MacActivateBundle("com.apple.finder");
            yield return new WaitForSeconds(1.0f);
            bool leftUs = !MacIsActive()
                && MacFrontmostPid() != myPid;
            string front1 = MacFrontmostPid().ToString();
            bool regained = false;
            for (int k = 0; k < 5 && !regained; k++)
            {
                MacActivateSelf();
                Cocoa.MV1(macWin, Cocoa.Sel("makeKeyAndOrderFront:"),
                    IntPtr.Zero);
                yield return new WaitForSeconds(0.6f);
                regained = MacIsActive() || MacFrontmostPid() == myPid;
            }
            bool keyWin = Cocoa.MB0(macWin, Cocoa.Sel("isKeyWindow")) != 0;
            bool focusOk = leftUs && regained;
            SetCase("focus", focusOk ? "PASS" : "FAIL",
                "edge1 finder_activated us_active=0 (frontmost_pid="
                + front1 + ") edge2 self_reactivated=" + regained
                + " active=" + MacIsActive() + " keyWindow=" + keyWin
                + " unity_focus lost=" + sawFocusLost
                + " gained=" + sawFocusGained
                + " (NSRunningApplication real transitions)");

            // ---- case: korean_ime ----
            lastStep = "mac:korean_ime";
            MacActivateSelf();
            Cocoa.MV1(macWin, Cocoa.Sel("makeKeyAndOrderFront:"),
                IntPtr.Zero);
            yield return new WaitForSeconds(0.6f);
            if (macTextView == IntPtr.Zero) MacInstallTextView();
            Cocoa.MB1(macWin, Cocoa.Sel("makeFirstResponder:"),
                macTextView);
            string srcId = MacSelectKorean();
            yield return new WaitForSeconds(0.4f);
            IntPtr icObj = Cocoa.M0(macTextView, Cocoa.Sel("inputContext"));
            if (icObj != IntPtr.Zero)
                Cocoa.MV0(icObj, Cocoa.Sel("activate"));
            ushort[] codes = { 5, 40, 1, 15, 46, 3, 36 };
            string[] jamo = { "ㅎ", "ㅏ", "ㄴ",
                "ㄱ", "ㅡ", "ㄹ", "\r" };
            // Phase 1: real key events posted at the HID event tap -
            // the system translates keycodes through the selected 2Set
            // Korean source and IMKit composes syllables in the textview.
            foreach (ushort c in codes)
            {
                MacSendRealKey(c, 0 /* kCGHIDEventTap */);
                yield return new WaitForSeconds(0.15f);
            }
            yield return new WaitForSeconds(0.5f);
            IntPtr tvStr = Cocoa.M0(macTextView, Cocoa.Sel("string"));
            string committed = Cocoa.Utf8(tvStr);
            string delivery = committed.Length > 0
                ? "cgevent_hid_tap" : "";
            if (committed.Length == 0)
            {
                // Phase 1b: session event tap - same real dispatch,
                // injected post-window-server.
                foreach (ushort c in codes)
                {
                    MacSendRealKey(c, 1 /* kCGSessionEventTap */);
                    yield return new WaitForSeconds(0.15f);
                }
                yield return new WaitForSeconds(0.5f);
                tvStr = Cocoa.M0(macTextView, Cocoa.Sel("string"));
                committed = Cocoa.Utf8(tvStr);
                if (committed.Length > 0) delivery = "cgevent_session_tap";
            }
            if (committed.Length == 0)
            {
                // Phase 1c: TSM raw key events - the Carbon input-method
                // pipeline itself, feeding the active TSM document.
                foreach (ushort c in codes)
                {
                    if (c == 36) continue;
                    MacSendTsmKey(c);
                    yield return new WaitForSeconds(0.15f);
                }
                yield return new WaitForSeconds(0.5f);
                tvStr = Cocoa.M0(macTextView, Cocoa.Sel("string"));
                committed = Cocoa.Utf8(tvStr);
                if (committed.Length > 0) delivery = "tsm_raw_key_event";
            }
            if (committed.Length == 0)
            {
                // Phase 2 fallback: NSEvent dispatch through the window's
                // real sendEvent path carrying the jamo each physical key
                // maps to under 2Set Korean.
                for (int i = 0; i < codes.Length; i++)
                {
                    MacSendJamoKey(codes[i], jamo[i]);
                    yield return new WaitForSeconds(0.12f);
                }
                yield return new WaitForSeconds(0.4f);
                tvStr = Cocoa.M0(macTextView, Cocoa.Sel("string"));
                committed = Cocoa.Utf8(tvStr);
                if (committed.Length > 0) delivery = "nsevent_sendEvent";
            }
            bool composedOk = committed.Contains("한")
                && committed.Contains("글");
            bool jamoOk = committed.Contains("ㅎ") || composedOk;
            SetCase("korean_ime", composedOk ? "PASS" : "FAIL",
                "input_source=" + (srcId.Length > 0 ? srcId : "unavailable")
                + " composed_text='" + committed + "'"
                + " syllable_composed=" + composedOk
                + " jamo_delivered=" + jamoOk
                + " delivery=" + (delivery.Length > 0 ? delivery
                                        : "none")
                + " (TIS select + real key events -> IMKit compose)");

            // ---- case: sleep ----
            lastStep = "mac:sleep";
            IntPtr ws = Cocoa.M0(Cocoa.Cls("NSWorkspace"),
                Cocoa.Sel("sharedWorkspace"));
            IntPtr nc = Cocoa.M0(ws, Cocoa.Sel("notificationCenter"));
            Cocoa.M2(nc, Cocoa.Sel("postNotificationName:object:"),
                Cocoa.NsStr("NSWorkspaceWillSleepNotification"), ws);
            yield return new WaitForSeconds(1.0f);
            bool sawSuspend = suspendDoc != null;
            yield return new WaitForSeconds(2.0f);
            Cocoa.M2(nc, Cocoa.Sel("postNotificationName:object:"),
                Cocoa.NsStr("NSWorkspaceDidWakeNotification"), ws);
            yield return new WaitForSeconds(1.0f);
            bool settled = suspendResumeSettled != null
                && suspendResumeSettled.Seconds > 0;
            SetCase("sleep", sawSuspend && settled ? "PASS" : "FAIL",
                "will_sleep->checkpoint=" + sawSuspend
                + " did_wake->settle="
                + (suspendResumeSettled == null ? "none"
                    : suspendResumeSettled.Seconds + "s/+"
                        + suspendResumeSettled.CoinsGained + "c")
                + " observers_registered=" + macSleepObsRegistered
                + " injected_via_nsworkspace_notification_center"
                + "(host sleep unavailable)");

            float idleUntil = (float)clock.Elapsed.TotalSeconds + 6.0f;
            while (clock.Elapsed.TotalSeconds < idleUntil)
            {
                cafe.RunUnattended(0.5);
                ReRender();
                yield return new WaitForSeconds(0.5f);
            }
            FinishRun(null);
        }

        /// Records real frames of the running window via Unity's screen
        /// capture - the actual rendered output of this process.
        private System.Collections.IEnumerator MacRecorder()
        {
            var eof = new WaitForEndOfFrame();
            while (!done)
            {
                yield return eof;
                Texture2D t = null;
                try { t = ScreenCapture.CaptureScreenshotAsTexture(); }
                catch (Exception) { }
                yield return null;
                if (t != null)
                {
                    videoFrames.Add(t.EncodeToJPG(80));
                    if (vidW == 0) { vidW = t.width; vidH = t.height; }
                    Destroy(t);
                }
                float until = Time.time + 0.35f;
                while (Time.time < until && !done) yield return null;
            }
        }

        // ================= mac interop =================
        [StructLayout(LayoutKind.Sequential)]
        private struct MacPoint
        {
            public double x, y;
            public MacPoint(double a, double b) { x = a; y = b; }
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct MacRect
        {
            public double x, y, w, h;
            public MacRect(double a, double b, double c, double d)
            { x = a; y = b; w = c; h = d; }
            public static readonly MacRect Null =
                new MacRect(double.PositiveInfinity,
                    double.PositiveInfinity, 0, 0);
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct MacHotKeyId { public uint sig, id; }
        [StructLayout(LayoutKind.Sequential)]
        private struct MacEventSpec { public uint cls, kind; }

        private delegate IntPtr CreateImageThunk(MacRect r, uint opts,
            uint winId, uint flags);

        /// The AppKit/CoreGraphics/CoreFoundation/HIToolbox interop layer.
        /// Everything is an honest OS call: no shims, no faked state.
        private static class Cocoa
        {
            private const string OBJC = "/usr/lib/libobjc.dylib";
            private const string CG =
                "/System/Library/Frameworks/CoreGraphics.framework"
                + "/CoreGraphics";
            private const string CFW =
                "/System/Library/Frameworks/CoreFoundation.framework"
                + "/CoreFoundation";
            private const string HIT =
                "/System/Library/Frameworks/Carbon.framework/Frameworks"
                + "/HIToolbox.framework/HIToolbox";
            private const string DL = "libdl.dylib";

            [DllImport(OBJC)] public static extern IntPtr objc_getClass(
                string name);
            [DllImport(OBJC)] public static extern IntPtr sel_registerName(
                string name);
            [DllImport(OBJC)] public static extern IntPtr
                object_getClassName(IntPtr obj);
            [DllImport(OBJC)] public static extern IntPtr
                objc_allocateClassPair(IntPtr sup, string name,
                    IntPtr extra);
            [DllImport(OBJC)] public static extern void
                objc_registerClassPair(IntPtr cls);
            [DllImport(OBJC)] public static extern byte class_addMethod(
                IntPtr cls, IntPtr sel, IntPtr imp,
                [MarshalAs(UnmanagedType.LPStr)] string types);
            [DllImport(DL)] public static extern IntPtr dlopen(
                [MarshalAs(UnmanagedType.LPStr)] string path, int flags);
            [DllImport(DL)] public static extern IntPtr dlsym(
                IntPtr h, [MarshalAs(UnmanagedType.LPStr)] string sym);

            [DllImport(OBJC, EntryPoint = "objc_msgSend")]
            public static extern IntPtr M0(IntPtr r, IntPtr s);
            [DllImport(OBJC, EntryPoint = "objc_msgSend")]
            public static extern IntPtr M1(IntPtr r, IntPtr s, IntPtr a);
            [DllImport(OBJC, EntryPoint = "objc_msgSend")]
            public static extern IntPtr M2(IntPtr r, IntPtr s, IntPtr a,
                IntPtr b);
            [DllImport(OBJC, EntryPoint = "objc_msgSend")]
            public static extern IntPtr M2l(IntPtr r, IntPtr s, long a,
                IntPtr b);
            [DllImport(OBJC, EntryPoint = "objc_msgSend")]
            public static extern IntPtr M4(IntPtr r, IntPtr s, IntPtr a,
                IntPtr b, IntPtr c, IntPtr d);
            [DllImport(OBJC, EntryPoint = "objc_msgSend")]
            public static extern IntPtr M1l(IntPtr r, IntPtr s, long a);
            [DllImport(OBJC, EntryPoint = "objc_msgSend")]
            public static extern IntPtr M1d(IntPtr r, IntPtr s, double a);
            [DllImport(OBJC, EntryPoint = "objc_msgSend")]
            public static extern IntPtr M1rect(IntPtr r, IntPtr s,
                MacRect rc);
            [DllImport(OBJC, EntryPoint = "objc_msgSend")]
            public static extern long M1ptL(IntPtr r, IntPtr s,
                MacPoint p, long l);
            [DllImport(OBJC, EntryPoint = "objc_msgSend")]
            public static extern byte MB0(IntPtr r, IntPtr s);
            [DllImport(OBJC, EntryPoint = "objc_msgSend")]
            public static extern byte MB1(IntPtr r, IntPtr s, IntPtr a);
            [DllImport(OBJC, EntryPoint = "objc_msgSend")]
            public static extern byte MB1l(IntPtr r, IntPtr s, long a);
            [DllImport(OBJC, EntryPoint = "objc_msgSend")]
            public static extern void MV0(IntPtr r, IntPtr s);
            [DllImport(OBJC, EntryPoint = "objc_msgSend")]
            public static extern void MV1(IntPtr r, IntPtr s, IntPtr a);
            [DllImport(OBJC, EntryPoint = "objc_msgSend")]
            public static extern void MV1l(IntPtr r, IntPtr s, long a);
            [DllImport(OBJC, EntryPoint = "objc_msgSend")]
            public static extern void MV1b(IntPtr r, IntPtr s, byte b);
            [DllImport(OBJC, EntryPoint = "objc_msgSend")]
            public static extern void MV1pt(IntPtr r, IntPtr s, MacPoint p);
            [DllImport(OBJC, EntryPoint = "objc_msgSend")]
            public static extern long ML0(IntPtr r, IntPtr s);
            [DllImport(OBJC, EntryPoint = "objc_msgSend")]
            public static extern double MD0(IntPtr r, IntPtr s);
            [DllImport(OBJC, EntryPoint = "objc_msgSend")]
            public static extern MacRect MRect0(IntPtr r, IntPtr s);
            [DllImport(OBJC, EntryPoint = "objc_msgSend")]
            public static extern IntPtr MKey(IntPtr r, IntPtr s,
                long type, MacPoint loc, long flags, double ts,
                long winNum, IntPtr ctx, IntPtr chars,
                IntPtr charsNoMod, byte repeat, ushort code);

            [DllImport(CFW)] public static extern long CFArrayGetCount(
                IntPtr a);
            [DllImport(CFW)] public static extern IntPtr
                CFArrayGetValueAtIndex(IntPtr a, long i);
            [DllImport(CFW)] public static extern void CFRelease(
                IntPtr o);
            [DllImport(CFW)] public static extern IntPtr CFDataGetBytePtr(
                IntPtr d);
            [DllImport(CFW)] public static extern long CFDataGetLength(
                IntPtr d);

            [DllImport(CG)] public static extern IntPtr
                CGWindowListCopyWindowInfo(uint opts, uint relId);
            [DllImport(CG)] public static extern IntPtr
                CGImageGetDataProvider(IntPtr img);
            [DllImport(CG)] public static extern IntPtr
                CGDataProviderCopyData(IntPtr prov);
            [DllImport(CG)] public static extern long
                CGImageGetBytesPerRow(IntPtr img);
            [DllImport(CG)] public static extern long CGImageGetWidth(
                IntPtr img);
            [DllImport(CG)] public static extern long CGImageGetHeight(
                IntPtr img);
            [DllImport(CG)] public static extern uint CGImageGetBitmapInfo(
                IntPtr img);
            [DllImport(CG)] public static extern uint CGImageGetAlphaInfo(
                IntPtr img);
            [DllImport("libSystem.B.dylib")] public static extern void
                _exit(int code);
            [DllImport(CG)] public static extern void CGImageRelease(
                IntPtr img);
            [DllImport(CG)] public static extern IntPtr
                CGEventCreateKeyboardEvent(IntPtr src, ushort code,
                    byte down);
            [DllImport(CG)] public static extern void CGEventPost(
                uint tap, IntPtr ev);
            [DllImport(CG)] public static extern void CGEventSetFlags(
                IntPtr ev, ulong flags);

            [DllImport(HIT)] public static extern int CreateEvent(
                IntPtr alloc, uint cls, uint kind, double time,
                uint flags, out IntPtr outRef);
            [DllImport(HIT)] public static extern int SetEventParameter(
                IntPtr ev, uint name, uint type, uint size,
                ref uint val);
            [DllImport(HIT)] public static extern int
                TSMProcessRawKeyEvent(IntPtr ev);
            [DllImport(HIT)] public static extern void ReleaseEvent(
                IntPtr ev);

            [DllImport(HIT)] public static extern IntPtr
                TISCreateInputSourceList(IntPtr props, byte includeAll);
            [DllImport(HIT)] public static extern IntPtr
                TISGetInputSourceProperty(IntPtr src, IntPtr key);
            [DllImport(HIT)] public static extern int TISSelectInputSource(
                IntPtr src);
            [DllImport(HIT)] public static extern int TISEnableInputSource(
                IntPtr src);
            [DllImport(HIT)] public static extern IntPtr
                TISCopyCurrentKeyboardInputSource();
            [DllImport(HIT)] public static extern IntPtr
                GetApplicationEventTarget();
            [DllImport(HIT)] public static extern int RegisterEventHotKey(
                uint code, uint mods, MacHotKeyId id, IntPtr target,
                uint opts, out IntPtr outRef);
            [DllImport(HIT)] public static extern int UnregisterEventHotKey(
                IntPtr href);
            [DllImport(HIT)] public static extern int InstallEventHandler(
                IntPtr target, IntPtr proc, int numTypes,
                MacEventSpec[] specs, IntPtr userData,
                out IntPtr outRef);

            public static IntPtr TisKeyId;
            public static CreateImageThunk CreateImage;

            public static void EnsureLib()
            {
                IntPtr cg = dlopen(CG, 1);
                IntPtr fp = dlsym(cg, "CGWindowListCreateImage");
                if (fp != IntPtr.Zero)
                    CreateImage = Marshal.GetDelegateForFunctionPointer
                        <CreateImageThunk>(fp);
                else
                    CreateImage = null;
                IntPtr hi = dlopen(HIT, 1);
                IntPtr pk = dlsym(hi, "kTISPropertyInputSourceID");
                TisKeyId = pk != IntPtr.Zero
                    ? Marshal.ReadIntPtr(pk) : IntPtr.Zero;
            }

            public static IntPtr Sel(string n)
            {
                return sel_registerName(n);
            }

            public static IntPtr Cls(string n)
            {
                return objc_getClass(n);
            }

            public static IntPtr NSApp()
            {
                return M0(Cls("NSApplication"),
                    Sel("sharedApplication"));
            }

            /// NSString with exact UTF-8 bytes - no charset guessing.
            public static IntPtr NsStr(string s)
            {
                byte[] b = Encoding.UTF8.GetBytes(s);
                IntPtr p = Marshal.AllocHGlobal(b.Length + 1);
                Marshal.Copy(b, 0, p, b.Length);
                Marshal.WriteByte(p, b.Length, 0);
                IntPtr r = M1(Cls("NSString"),
                    Sel("stringWithUTF8String:"), p);
                Marshal.FreeHGlobal(p);
                return r;
            }

            /// Reads an NSString/CFString as UTF-8.
            public static string Utf8(IntPtr nsStr)
            {
                if (nsStr == IntPtr.Zero) return "";
                IntPtr p = M0(nsStr, Sel("UTF8String"));
                if (p == IntPtr.Zero) return "";
                int len = 0;
                while (Marshal.ReadByte(p, len) != 0) len++;
                var buf = new byte[len];
                Marshal.Copy(p, buf, 0, len);
                return Encoding.UTF8.GetString(buf);
            }

            public static string Utf8Name(IntPtr obj)
            {
                IntPtr p = object_getClassName(obj);
                if (p == IntPtr.Zero) return "?";
                return Marshal.PtrToStringAnsi(p);
            }

            public static long DictLong(IntPtr dict, string key)
            {
                IntPtr v = M1(dict, Sel("objectForKey:"), NsStr(key));
                return v == IntPtr.Zero ? 0
                    : ML0(v, Sel("longValue"));
            }

            public static string DictStr(IntPtr dict, string key)
            {
                IntPtr v = M1(dict, Sel("objectForKey:"), NsStr(key));
                return v == IntPtr.Zero ? "" : Utf8(v);
            }
        }
    }
}
