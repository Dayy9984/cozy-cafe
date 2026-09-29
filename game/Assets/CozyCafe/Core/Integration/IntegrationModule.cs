using System;
using System.Collections.Generic;
using CozyCafe.Core.Character;
using CozyCafe.Core.Economy;
using CozyCafe.Core.Layout;
using CozyCafe.Core.Modules;
using CozyCafe.Core.Render;
using CozyCafe.Core.Save;
using CozyCafe.Core.Scene;
using CozyCafe.Core.Tools;
using CozyCafe.Core.Ugc;

namespace CozyCafe.Core.Integration
{
    /// <summary>
    /// The unattended cafe wired end-to-end as one real session: boot ->
    /// layout -> staff -> research -> tools -> UGC -> idle run -> save /
    /// offline. Every step goes through the real public module calls the
    /// game itself makes; CafeSession.Advance is the only time source, so a
    /// scripted first session replays bit-exact. BuildView() exposes the
    /// running state as the renderable GameScene both capture hosts draw.
    /// </summary>
    public sealed class IntegrationModule : ModuleBase
    {
        public override string Name { get { return "integration"; } }

        /// Frame quantum used while driving the cafe unattended — the same
        /// shape as the game's per-frame tick. During a no-input run Advance
        /// is the only call ever issued against the session.
        public const double UnattendedStepSeconds = 0.5;

        public CafeSession Session { get; private set; }
        public ToolsModule Tools { get; private set; }
        public UgcModule Ugc { get; private set; }
        public CharacterModule Characters { get; private set; }

        /// The renderable view of the live session — the very GameScene the
        /// layout editor mutates; the capture path draws this same object.
        public GameScene View
        {
            get { return Session != null ? Session.Layout.Scene : null; }
        }

        /// <summary>
        /// Boots a real cafe: the shared session timeline (economy, research
        /// lab, hire office, layout) plus the desktop tools, the UGC creator
        /// and the character module. The authored opening layout is placed
        /// through the real editor rules — a rejected placement fails the
        /// boot loudly instead of shipping a broken room.
        /// </summary>
        public static IntegrationModule Boot(MvpData data, long staffSeed,
            int roomW, int roomH, string ugcStoreDir)
        {
            if (data == null) throw new ArgumentNullException("data");
            var m = new IntegrationModule();
            m.Session = CafeSession.CreateStartup(data, staffSeed, roomW, roomH);
            m.Tools = new ToolsModule();
            m.Ugc = new UgcModule { StoreDir = ugcStoreDir };
            m.Characters = new CharacterModule();
            if (!m.PlaceOpeningLayout())
            {
                throw new InvalidOperationException(
                    "opening layout rejected by the real placement rules");
            }
            return m;
        }

        /// <summary>
        /// Opening-day layout through the real editor: a wall-slot door, one
        /// table/seat pair, and a two-counter bar actually hosting the
        /// espresso machine and the grinder. Every call is validated; a
        /// grouped commit rolls back whole on any violation.
        /// </summary>
        public bool PlaceOpeningLayout()
        {
            var ed = Session.Layout;
            if (!ed.TryPlace(FurnitureKind.Door, 0, 2, 0).Ok) return false;
            ed.BeginCommand();
            bool pair = ed.TryPlace(FurnitureKind.Table, 2, 1, 0).Ok
                && ed.TryPlace(FurnitureKind.Chair, 1, 1, 0).Ok;
            if (ed.EndCommand() != PlacementReject.None || !pair) return false;
            ed.BeginCommand();
            bool bar = ed.TryPlace(FurnitureKind.Counter, 4, 1, 0).Ok
                && ed.TryPlace(FurnitureKind.Counter, 4, 3, 0).Ok;
            if (ed.EndCommand() != PlacementReject.None || !bar) return false;
            return ed.TryPlace(FurnitureKind.EspressoMachine, 4, 1, 0).Ok
                && ed.TryPlace(FurnitureKind.Grinder, 4, 3, 0).Ok;
        }

        /// <summary>
        /// Opening actions across every wired module: hire the first panel
        /// candidate (a real staff boundary on the timeline), queue the
        /// ice-machine and milk research, put live memo/todo/timer/music
        /// state on the tools, and run a UGC counter skin through the real
        /// import -> assign -> preview -> validate -> save -> apply chain.
        /// Returns false unless every leg genuinely succeeded.
        /// </summary>
        public bool OpenForBusiness(byte[] ugcSkinPng)
        {
            var panel = Session.Staff.OpenHirePanel();
            bool hired = panel.Count > 0 && Session.Hire(0);
            bool queued = Session.Lab.Reserve("R01") && Session.Lab.Reserve("R02");

            int memo = Tools.CreateMemo("");
            Tools.SetMemoText(memo, "오늘 매출 확인\n내일 원두 주문");
            Tools.AddTodo("개업 준비");
            int sweep = Tools.AddTodo("테이블 닦기");
            Tools.CompleteTodo(sweep);
            Tools.Timer.StartFocus();
            Tools.TickTimer(45);
            Tools.Music.Play();
            bool toolsLive = Tools.Memos.Count == 1 && Tools.Todos.Count == 2
                && Tools.Timer.State == TimerState.Running
                && Tools.Music.IsPlaying;

            return hired && queued && toolsLive && InstallCounterSkin(ugcSkinPng);
        }

        private bool InstallCounterSkin(byte[] png)
        {
            if (png == null || Ugc.StoreDir == null) return false;
            if (Ugc.BeginImport(png, "counter-skin") == null) return false;
            if (!Ugc.Assign("furniture_skin", 0.5, 0.5, "")) return false;
            Ugc.AddAppearance("furniture:counter");
            if (Ugc.BuildPreview(64, 48) == null) return false;
            if (Ugc.Validate().Count != 0) return false;
            if (Ugc.Save() == null) return false;
            var inst = Ugc.Apply(Session);
            return inst.SkinRefs.Count == 1 && inst.FallbackAssets.Count == 0
                && Ugc.AppearanceOf("furniture:counter") != null;
        }

        /// <summary>
        /// The no-input run: Advance() — and nothing else — moves the
        /// timeline. Returns the runtime seconds actually applied.
        /// </summary>
        public double RunUnattended(double seconds)
        {
            double applied = 0;
            while (applied < seconds)
            {
                double step = Math.Min(UnattendedStepSeconds, seconds - applied);
                Session.Advance(step);
                applied += step;
            }
            return applied;
        }

        /// <summary>
        /// Keeps the unattended Advance loop going until the condition
        /// holds — the funds-gated research queue, a menu opening, whatever
        /// the caller measures — or the runtime cap expires. Returns elapsed
        /// runtime seconds, or -1 when the condition never arrived.
        /// </summary>
        public double RunUnattendedUntil(Func<bool> cond, double capSeconds)
        {
            double elapsed = 0;
            while (!cond())
            {
                if (elapsed >= capSeconds) return -1;
                Session.Advance(UnattendedStepSeconds);
                elapsed += UnattendedStepSeconds;
            }
            return elapsed;
        }

        /// <summary>
        /// Byte-deterministic fingerprint of the entire wired state: the
        /// versioned save document (wallet, event ledger, lab, staff,
        /// layout, runtime clock) plus tools state and the UGC appearance
        /// bindings — everything a real replay must reproduce.
        /// </summary>
        public string StateFingerprint(long nowUtc)
        {
            var doc = Session.BuildDocument(nowUtc);
            var d = new Dictionary<string, object>();
            d["save"] = MiniJson.Parse(doc.ToJson());
            d["tools"] = MiniJson.Parse(Tools.SaveTools());
            var ap = new List<object>();
            var keys = new List<string>(Ugc.AppearanceByTarget.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (var k in keys)
            {
                ap.Add(k + "=" + (Ugc.AppearanceByTarget[k] ?? ""));
            }
            d["ugc_appearances"] = ap;
            return MiniJson.ToJson(d);
        }

        /// <summary>
        /// The running cafe as a renderable scene: the session's own layout
        /// scene plus composited characters standing on their logical ground
        /// anchors (a staff member in the apron preset, a seeded customer)
        /// and the docked tools panel snapshot — all real module state at
        /// capture time inside a fixed viewport.
        /// </summary>
        public GameScene BuildView(int viewportW, int viewportH,
            double anchorX, double anchorY)
        {
            var s = Session.Layout.Scene;
            s.Characters.Clear();
            // Staff (apron preset) works the floor; a deterministic seeded
            // customer waits at the bar — both composited by the real rig.
            AddCharacter(s, Characters.ComboForPreset(2), Facing.SW, 0.6, 4.6);
            AddCharacter(s,
                Characters.RollAppearance(CustomerSeed(), false),
                Facing.NE, 4.8, 1.0);
            s.ToolsPanel = Tools.BuildPanel();
            s.FixedViewport = true;
            s.ViewportW = viewportW;
            s.ViewportH = viewportH;
            s.AnchorX = anchorX;
            s.AnchorY = anchorY;
            s.IsLoaded = s.Validate();
            return s;
        }

        /// The customer roll is seeded by the hired staff member's
        /// appearance stream — deterministic per session, different per
        /// roster, never a fixed pick.
        private int CustomerSeed()
        {
            int id = Session.Staff.Roster.Count > 0
                ? Session.Staff.Roster[0].AppearanceSeed : 0;
            return 9000 + id % 500;
        }

        private void AddCharacter(GameScene s, AppearanceCombo combo,
            Facing dir, double gx, double gy)
        {
            int vis;
            var sprite = Characters.Composite(combo, dir, "idle", 0, out vis);
            s.Characters.Add(new CharacterPlacement
            {
                Direction = dir.ToString(),
                GridX = gx,
                GridY = gy,
                Sprite = sprite,
                AnchorX = Characters.Rig.FootAnchorX,
                AnchorY = Characters.Rig.FootAnchorY
            });
        }

        /// <summary>
        /// The authored 24x24 counter skin the wired session imports — a
        /// real PNG encoded in memory, then taken through the creator's
        /// decode/validate/store/apply path like a user drop.
        /// </summary>
        public static byte[] BuildCounterSkinPng()
        {
            var c = new SoftwareCanvas(24, 24);
            c.Clear(new Rgba(0, 0, 0, 0));
            for (int y = 0; y < 24; y++)
            {
                for (int x = 0; x < 24; x++)
                {
                    bool border = x < 2 || y < 2 || x >= 22 || y >= 22;
                    c.SetPixel(x, y, border
                        ? Rgba.Opaque(120, 200, 180)
                        : Rgba.Opaque(58, 96, 88));
                }
            }
            c.FillRect(4, 4, 6, 6, Rgba.Opaque(240, 224, 170));
            c.FillRect(14, 14, 6, 6, Rgba.Opaque(220, 190, 140));
            c.FillRect(4, 14, 6, 6, Rgba.Opaque(190, 150, 110));
            c.FillRect(14, 4, 6, 6, Rgba.Opaque(170, 140, 100));
            return PngWriter.Encode(24, 24, c.Pixels);
        }

        /// Settled sale-event count read out of the economy's own save
        /// record — the real ledger position, not a counter we track here.
        public long SettledSaleCount()
        {
            return SaveDoc.Long(SaveDoc.Get(Session.Econ.SaveState(), "sale_seq"));
        }

        /// Probe exercises the real boot path on a throwaway session:
        /// layout through the editor, a short unattended run, and a real
        /// render of the live view. Never touches the caller's session.
        protected override bool OnProbe()
        {
            try
            {
                var data = MvpData.TryLoad();
                if (data == null) return false;
                var m = Boot(data, 1, 6, 5, null);
                long c0 = m.Session.Econ.Coins;
                m.RunUnattended(60.0);
                var v = m.BuildView(520, 460, 260, 130);
                int w, h;
                var px = SceneRenderer.RenderPixels(v, v.Zoom, out w, out h);
                return m.Session.Econ.Coins > c0 && v.IsLoaded
                    && px != null && px.Length == w * h * 4;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
