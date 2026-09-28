using System;
using System.Collections.Generic;
using CozyCafe.Core.Modules;

namespace CozyCafe.Core.Tools
{
    /// Desktop window mode: the full cafe view or the small always-on-top
    /// mini window the work tools live on. Switching is a view-layer change
    /// only — it never touches the cafe's logical state.
    public enum WindowMode
    {
        Normal = 0,
        Mini = 1
    }

    /// Focus(25m) / break(5m) presets plus a free custom duration.
    public enum TimerKind
    {
        Focus = 0,
        Break = 1,
        Custom = 2
    }

    public enum TimerState
    {
        Idle = 0,
        Running = 1,
        Paused = 2,
        Completed = 3
    }

    /// A focus session's outcome. Only sessions that run to completion
    /// credit their seconds; sleep/exit/stop interruptions credit exactly 0.
    public sealed class FocusRecord
    {
        public double Seconds;
        public string EndedBy;
    }

    /// The work-timer: monotonic remaining time, an exact pause report, and
    /// preset durations for the 25/5 cycle plus arbitrary custom sessions.
    /// Tool time lives here — it never feeds the cafe settlement clock.
    public sealed class FocusTimer
    {
        public const double FocusPresetSeconds = 25.0 * 60.0; // 1500
        public const double BreakPresetSeconds = 5.0 * 60.0;  // 300

        public TimerKind Kind { get; private set; }
        public TimerState State { get; private set; }
        public double DurationSeconds { get; private set; }
        public double RemainingSeconds { get; private set; }

        /// Breaks are rest, not focus credit.
        public bool CreditsFocus
        {
            get { return Kind == TimerKind.Focus || Kind == TimerKind.Custom; }
        }

        public void StartFocus() { Start(TimerKind.Focus, FocusPresetSeconds); }
        public void StartBreak() { Start(TimerKind.Break, BreakPresetSeconds); }
        public void StartCustom(double seconds) { Start(TimerKind.Custom, seconds); }

        public void Start(TimerKind kind, double seconds)
        {
            if (seconds <= 0) seconds = 1;
            Kind = kind;
            DurationSeconds = seconds;
            RemainingSeconds = seconds;
            State = TimerState.Running;
        }

        /// Advances a running session. Returns true when this tick completed
        /// the session (remaining reached exactly 0) so the owner can file
        /// the focus record at the real completion boundary.
        public bool Tick(double dtSeconds)
        {
            if (State != TimerState.Running || dtSeconds <= 0) return false;
            RemainingSeconds -= dtSeconds;
            if (RemainingSeconds <= 0)
            {
                RemainingSeconds = 0;
                State = TimerState.Completed;
                return true;
            }
            return false;
        }

        /// Freezes the session and reports the exact remaining seconds —
        /// no rounding, no clamping against a preset.
        public double Pause()
        {
            if (State == TimerState.Running) State = TimerState.Paused;
            return RemainingSeconds;
        }

        public void Resume()
        {
            if (State == TimerState.Paused) State = TimerState.Running;
        }

        /// Drops the session without credit (manual stop or interruption).
        public void Abort()
        {
            State = TimerState.Idle;
            DurationSeconds = 0;
            RemainingSeconds = 0;
        }

        public void Restore(TimerKind kind, TimerState state,
            double duration, double remaining)
        {
            Kind = kind;
            State = state;
            DurationSeconds = duration;
            RemainingSeconds = remaining;
        }
    }

    /// Audio scope: this MVP only ever plays local files and rights-cleared
    /// material. OAuth/streaming sources are a distinct kind the deck
    /// refuses — the scope limit is enforced in code, not just documented.
    public enum MusicSourceKind
    {
        LocalFile = 0,
        RightsCleared = 1,
        ExternalOAuth = 2
    }

    public sealed class MusicTrack
    {
        public string Id;
        public string Title;
        public double DurationSeconds;
        public MusicSourceKind Source;
    }

    /// The local music deck: a catalog of local/rights-cleared tracks plus
    /// real playback controls (select/play/pause/next/previous/volume).
    /// Nothing here reaches a network or an OAuth provider.
    public sealed class LocalMusicDeck
    {
        public readonly List<MusicTrack> Catalog = new List<MusicTrack>();
        public int CurrentIndex = -1;
        public bool IsPlaying;
        public double PositionSeconds;
        public double Volume = 0.7;

        /// The shipped ambient catalog: local bundled loops only.
        public static List<MusicTrack> DefaultCatalog()
        {
            return new List<MusicTrack>
            {
                new MusicTrack { Id = "amb_rain_loop", Title = "Rain Loop",
                    DurationSeconds = 92, Source = MusicSourceKind.LocalFile },
                new MusicTrack { Id = "amb_cafe_murmur", Title = "Cafe Murmur",
                    DurationSeconds = 118, Source = MusicSourceKind.RightsCleared },
                new MusicTrack { Id = "mus_lofi_01", Title = "Lo-fi Loop 01",
                    DurationSeconds = 74, Source = MusicSourceKind.LocalFile },
                new MusicTrack { Id = "amb_piano_loop", Title = "Piano Loop",
                    DurationSeconds = 101, Source = MusicSourceKind.RightsCleared },
            };
        }

        public MusicTrack Current
        {
            get
            {
                return CurrentIndex >= 0 && CurrentIndex < Catalog.Count
                    ? Catalog[CurrentIndex] : null;
            }
        }

        /// Adds a track to the session catalog. External/OAuth sources are
        /// rejected at the deck boundary — the MVP plays local or
        /// rights-cleared audio only.
        public bool Enqueue(MusicTrack track)
        {
            if (track == null || track.Source == MusicSourceKind.ExternalOAuth)
            {
                return false;
            }
            Catalog.Add(track);
            return true;
        }

        public bool AllSourcesAllowed()
        {
            foreach (var t in Catalog)
            {
                if (t.Source == MusicSourceKind.ExternalOAuth) return false;
            }
            return true;
        }

        public bool Select(int index)
        {
            if (index < 0 || index >= Catalog.Count) return false;
            CurrentIndex = index;
            PositionSeconds = 0;
            return true;
        }

        public bool Play()
        {
            if (Catalog.Count == 0) return false;
            if (CurrentIndex < 0) Select(0);
            IsPlaying = true;
            return true;
        }

        public bool PausePlayback()
        {
            if (!IsPlaying) return false;
            IsPlaying = false;
            return true;
        }

        public bool Next()
        {
            if (Catalog.Count == 0) return false;
            CurrentIndex = CurrentIndex < 0 ? 0 : (CurrentIndex + 1) % Catalog.Count;
            PositionSeconds = 0;
            return true;
        }

        public bool Previous()
        {
            if (Catalog.Count == 0) return false;
            CurrentIndex = CurrentIndex <= 0 ? Catalog.Count - 1 : CurrentIndex - 1;
            PositionSeconds = 0;
            return true;
        }

        public double SetVolume(double v)
        {
            if (v < 0) v = 0;
            if (v > 1) v = 1;
            Volume = v;
            return Volume;
        }

        /// Advances playback; reaching a track's end auto-advances to the
        /// next local entry like a real deck.
        public void TickPlayback(double dtSeconds)
        {
            if (!IsPlaying || dtSeconds <= 0) return;
            var cur = Current;
            PositionSeconds += dtSeconds;
            if (cur != null && PositionSeconds >= cur.DurationSeconds) Next();
        }
    }

    public sealed class TodoItem
    {
        public int Id;
        public string Text;
        public bool Done;
    }

    public sealed class MemoNote
    {
        public int Id;
        public string Text = "";
        public bool Open = true;
    }

    /// View-model the renderer draws: a snapshot of real tool state, not a
    /// mock. Every field is copied from live module values by BuildPanel().
    public sealed class ToolPanel
    {
        public bool MiniMode;
        public int MemoCount;
        public readonly List<int> MemoLineChars = new List<int>();
        public readonly List<bool> TodoDone = new List<bool>();
        public readonly List<int> TodoTextChars = new List<int>();
        public double TimerRemaining;
        public double TimerDuration;
        public TimerState TimerState;
        public bool MusicPlaying;
        public int MusicTrackIndex;
        public int MusicTrackCount;
        public double MusicVolume;
        public double MusicPosition;
        public double MusicTrackDuration;
        public double FocusSecondsTotal;
    }

    /// <summary>
    /// Desktop work tools over the shared core: memo, todo, the focus
    /// timer, focus records, the local music deck, and normal/mini window
    /// mode. Text entry state suppresses game shortcuts; every mutation
    /// autosaves through the same deterministic MiniJson snapshot path the
    /// layout save uses. Tool/focus time is accounted here only — the cafe
    /// settlement modules are never touched.
    /// </summary>
    public sealed class ToolsModule : ModuleBase
    {
        public override string Name { get { return "tools"; } }

        public WindowMode Mode { get; private set; }
        public bool ToolsPanelVisible { get; private set; }

        public readonly List<MemoNote> Memos = new List<MemoNote>();
        public readonly List<TodoItem> Todos = new List<TodoItem>();
        public readonly FocusTimer Timer = new FocusTimer();
        public readonly List<FocusRecord> FocusRecords = new List<FocusRecord>();
        public readonly LocalMusicDeck Music = new LocalMusicDeck();

        /// Memo text-entry focus: while a memo is being edited the game
        /// layer's shortcuts stay suppressed so typed keys never trigger
        /// cafe actions.
        public int EditingMemoId = -1;

        /// Latest autosave snapshot — every mutation rewrites it, so a save
        /// always reflects live state with no explicit save call.
        public string AutosavedJson { get; private set; }
        public long AutosaveRevision { get; private set; }

        private int nextMemoId = 1;
        private int nextTodoId = 1;

        public ToolsModule()
        {
            foreach (var t in LocalMusicDeck.DefaultCatalog()) Music.Catalog.Add(t);
            AutosavedJson = SaveTools();
        }

        // ---- window mode -------------------------------------------------

        /// Normal <-> Mini switch. Entering mini ends any open memo edit
        /// (autosaved first, like the original's mini-mode popup close) and
        /// docks the tools onto the small window; cafe state is untouched.
        public void SetMode(WindowMode mode)
        {
            if (Mode == mode) return;
            if (mode == WindowMode.Mini && EditingMemoId >= 0) EndMemoEdit();
            Mode = mode;
            ToolsPanelVisible = mode == WindowMode.Mini || ToolsPanelVisible;
            Autosave();
        }

        public void SetToolsPanelVisible(bool visible)
        {
            ToolsPanelVisible = visible;
            Autosave();
        }

        // ---- memo --------------------------------------------------------

        public int CreateMemo(string text)
        {
            var m = new MemoNote { Id = nextMemoId++, Text = text ?? "" };
            Memos.Add(m);
            Autosave();
            return m.Id;
        }

        public bool SetMemoText(int id, string text)
        {
            var m = FindMemo(id);
            if (m == null) return false;
            m.Text = text ?? "";
            Autosave();
            return true;
        }

        public bool DeleteMemo(int id)
        {
            var m = FindMemo(id);
            if (m == null) return false;
            if (EditingMemoId == id) EditingMemoId = -1;
            Memos.Remove(m);
            Autosave();
            return true;
        }

        public MemoNote FindMemo(int id)
        {
            foreach (var m in Memos) if (m.Id == id) return m;
            return null;
        }

        /// Opening a memo for text input focuses the entry field — game
        /// shortcuts are suppressed until the edit ends.
        public bool BeginMemoEdit(int id)
        {
            if (FindMemo(id) == null) return false;
            EditingMemoId = id;
            return true;
        }

        /// Ends the edit; the final text is already autosaved on each
        /// SetMemoText call, and closing persists once more.
        public void EndMemoEdit()
        {
            EditingMemoId = -1;
            Autosave();
        }

        public bool GameShortcutsSuppressed
        {
            get { return EditingMemoId >= 0; }
        }

        /// Whether a game-layer shortcut may fire right now. Text entry
        /// swallows every key, so shortcuts only route when no memo is
        /// being edited.
        public bool RouteGameShortcut(string key)
        {
            return !GameShortcutsSuppressed;
        }

        // ---- todo --------------------------------------------------------

        public int AddTodo(string text)
        {
            var t = new TodoItem { Id = nextTodoId++, Text = text ?? "" };
            Todos.Add(t);
            Autosave();
            return t.Id;
        }

        public bool SetTodoDone(int id, bool done)
        {
            var t = FindTodo(id);
            if (t == null) return false;
            t.Done = done;
            Autosave();
            return true;
        }

        public bool CompleteTodo(int id) { return SetTodoDone(id, true); }

        /// Reorders the list in place: the item is removed and reinserted
        /// at the clamped target index.
        public bool MoveTodo(int id, int index)
        {
            var t = FindTodo(id);
            if (t == null) return false;
            Todos.Remove(t);
            if (index < 0) index = 0;
            if (index > Todos.Count) index = Todos.Count;
            Todos.Insert(index, t);
            Autosave();
            return true;
        }

        public bool RemoveTodo(int id)
        {
            var t = FindTodo(id);
            if (t == null) return false;
            Todos.Remove(t);
            Autosave();
            return true;
        }

        public TodoItem FindTodo(int id)
        {
            foreach (var t in Todos) if (t.Id == id) return t;
            return null;
        }

        // ---- timer + focus records ----------------------------------------

        /// Forwards a clock tick to the timer; a natural completion files a
        /// focus record crediting the full session duration.
        public void TickTimer(double dtSeconds)
        {
            if (Timer.Tick(dtSeconds))
            {
                FocusRecords.Add(new FocusRecord
                {
                    Seconds = Timer.CreditsFocus ? Timer.DurationSeconds : 0,
                    EndedBy = "completed"
                });
            }
        }

        /// The device slept: any live session is discarded and credits
        /// exactly 0 seconds — no partial focus is banked.
        public void OnSystemSleep() { InterruptSession("sleep"); }

        /// The app exited mid-session: same zero-credit rule.
        public void OnSystemExit() { InterruptSession("exit"); }

        /// Manual stop also banks nothing.
        public void StopTimer() { InterruptSession("stopped"); }

        private void InterruptSession(string endedBy)
        {
            if (Timer.State == TimerState.Running
                || Timer.State == TimerState.Paused)
            {
                FocusRecords.Add(new FocusRecord { Seconds = 0, EndedBy = endedBy });
            }
            Timer.Abort();
        }

        public double TotalFocusSeconds
        {
            get
            {
                double s = 0;
                foreach (var r in FocusRecords) s += r.Seconds;
                return s;
            }
        }

        // ---- save / load ---------------------------------------------------

        /// Deterministic snapshot of all tool state through MiniJson — the
        /// same serialization path the layout save uses.
        public string SaveTools()
        {
            var d = new Dictionary<string, object>();
            d["v"] = 1;
            d["mode"] = (int)Mode;
            d["panel"] = ToolsPanelVisible ? 1 : 0;
            var memos = new List<object>();
            var sortedMemos = new List<MemoNote>(Memos);
            sortedMemos.Sort(delegate (MemoNote a, MemoNote b)
            {
                return a.Id.CompareTo(b.Id);
            });
            foreach (var m in sortedMemos)
            {
                var r = new Dictionary<string, object>();
                r["id"] = m.Id;
                r["text"] = m.Text;
                r["open"] = m.Open ? 1 : 0;
                memos.Add(r);
            }
            d["memos"] = memos;
            var todos = new List<object>();
            foreach (var t in Todos)
            {
                var r = new Dictionary<string, object>();
                r["id"] = t.Id;
                r["text"] = t.Text;
                r["done"] = t.Done ? 1 : 0;
                todos.Add(r);
            }
            d["todos"] = todos;
            d["next_memo"] = nextMemoId;
            d["next_todo"] = nextTodoId;
            var timer = new Dictionary<string, object>();
            timer["kind"] = (int)Timer.Kind;
            timer["state"] = (int)Timer.State;
            timer["duration"] = Timer.DurationSeconds;
            timer["remaining"] = Timer.RemainingSeconds;
            d["timer"] = timer;
            var focus = new List<object>();
            foreach (var r in FocusRecords)
            {
                var fr = new Dictionary<string, object>();
                fr["seconds"] = r.Seconds;
                fr["ended_by"] = r.EndedBy;
                focus.Add(fr);
            }
            d["focus_records"] = focus;
            var music = new Dictionary<string, object>();
            music["index"] = Music.CurrentIndex;
            music["playing"] = Music.IsPlaying ? 1 : 0;
            music["volume"] = Music.Volume;
            music["position"] = Music.PositionSeconds;
            var tracks = new List<object>();
            foreach (var t in Music.Catalog)
            {
                var tr = new Dictionary<string, object>();
                tr["id"] = t.Id;
                tr["title"] = t.Title;
                tr["duration"] = t.DurationSeconds;
                tr["source"] = (int)t.Source;
                tracks.Add(tr);
            }
            music["tracks"] = tracks;
            d["music"] = music;
            return MiniJson.ToJson(d);
        }

        public void LoadTools(string json)
        {
            var d = (Dictionary<string, object>)MiniJson.Parse(json);
            Mode = (WindowMode)(int)(long)d["mode"];
            ToolsPanelVisible = (long)d["panel"] != 0;
            Memos.Clear();
            foreach (var o in (List<object>)d["memos"])
            {
                var r = (Dictionary<string, object>)o;
                Memos.Add(new MemoNote
                {
                    Id = (int)(long)r["id"],
                    Text = (string)r["text"],
                    Open = (long)r["open"] != 0
                });
            }
            Todos.Clear();
            foreach (var o in (List<object>)d["todos"])
            {
                var r = (Dictionary<string, object>)o;
                Todos.Add(new TodoItem
                {
                    Id = (int)(long)r["id"],
                    Text = (string)r["text"],
                    Done = (long)r["done"] != 0
                });
            }
            nextMemoId = (int)(long)d["next_memo"];
            nextTodoId = (int)(long)d["next_todo"];
            var timer = (Dictionary<string, object>)d["timer"];
            Timer.Restore(
                (TimerKind)(int)(long)timer["kind"],
                (TimerState)(int)(long)timer["state"],
                ToSeconds(timer["duration"]),
                ToSeconds(timer["remaining"]));
            FocusRecords.Clear();
            foreach (var o in (List<object>)d["focus_records"])
            {
                var r = (Dictionary<string, object>)o;
                FocusRecords.Add(new FocusRecord
                {
                    Seconds = ToSeconds(r["seconds"]),
                    EndedBy = (string)r["ended_by"]
                });
            }
            var music = (Dictionary<string, object>)d["music"];
            Music.Catalog.Clear();
            foreach (var o in (List<object>)music["tracks"])
            {
                var r = (Dictionary<string, object>)o;
                Music.Catalog.Add(new MusicTrack
                {
                    Id = (string)r["id"],
                    Title = (string)r["title"],
                    DurationSeconds = ToSeconds(r["duration"]),
                    Source = (MusicSourceKind)(int)(long)r["source"]
                });
            }
            Music.CurrentIndex = (int)(long)music["index"];
            Music.IsPlaying = (long)music["playing"] != 0;
            Music.Volume = ToSeconds(music["volume"]);
            Music.PositionSeconds = ToSeconds(music["position"]);
            EditingMemoId = -1;
            Autosave();
        }

        private static double ToSeconds(object v)
        {
            var l = v as long?;
            if (l.HasValue) return l.Value;
            return (double)v;
        }

        private void Autosave()
        {
            AutosavedJson = SaveTools();
            AutosaveRevision++;
        }

        /// Snapshot of live state the renderer paints — real values only.
        public ToolPanel BuildPanel()
        {
            var p = new ToolPanel();
            p.MiniMode = Mode == WindowMode.Mini;
            p.MemoCount = Memos.Count;
            if (Memos.Count > 0)
            {
                var lines = Memos[0].Text.Split('\n');
                foreach (var ln in lines) p.MemoLineChars.Add(ln.Length);
            }
            foreach (var t in Todos)
            {
                p.TodoDone.Add(t.Done);
                p.TodoTextChars.Add(t.Text.Length);
            }
            p.TimerRemaining = Timer.RemainingSeconds;
            p.TimerDuration = Timer.DurationSeconds;
            p.TimerState = Timer.State;
            p.MusicPlaying = Music.IsPlaying;
            p.MusicTrackIndex = Music.CurrentIndex;
            p.MusicTrackCount = Music.Catalog.Count;
            p.MusicVolume = Music.Volume;
            p.MusicPosition = Music.PositionSeconds;
            var cur = Music.Current;
            p.MusicTrackDuration = cur != null ? cur.DurationSeconds : 0;
            p.FocusSecondsTotal = TotalFocusSeconds;
            return p;
        }

        /// Probe does real tool work on a throwaway instance: memo write,
        /// todo add/complete/reorder, a timer pause, and a full save/load
        /// round-trip — the live module is never mutated by a health check.
        protected override bool OnProbe()
        {
            var p = new ToolsModule();
            int m = p.CreateMemo("점검");
            p.SetMemoText(m, "점검 완료");
            int a = p.AddTodo("알파");
            int b = p.AddTodo("베타");
            if (!p.CompleteTodo(b) || !p.MoveTodo(b, 0)) return false;
            p.Timer.StartCustom(60);
            p.TickTimer(10);
            double rem = p.Timer.Pause();
            var q = new ToolsModule();
            q.LoadTools(p.SaveTools());
            return rem == 50
                && q.Todos.Count == 2 && q.Todos[0].Id == b && q.Todos[0].Done
                && q.Todos[1].Id == a && !q.Todos[1].Done
                && q.Memos.Count == 1 && q.Memos[0].Text == "점검 완료"
                && q.Timer.RemainingSeconds == 50;
        }
    }
}
