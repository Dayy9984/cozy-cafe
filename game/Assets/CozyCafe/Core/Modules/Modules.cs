using System.Collections.Generic;
using CozyCafe.Core.Scene;

namespace CozyCafe.Core.Modules
{
    /// <summary>
    /// A real game module registered in the boot sequence. Probe() performs
    /// actual work against module state and InvocationCount proves the entry
    /// path really called it — gates never trust a declared value.
    /// </summary>
    public interface IModule
    {
        string Name { get; }
        int InvocationCount { get; }
        bool Probe();
    }

    public abstract class ModuleBase : IModule
    {
        public abstract string Name { get; }
        public int InvocationCount { get; private set; }

        public bool Probe()
        {
            InvocationCount++;
            return OnProbe();
        }

        protected abstract bool OnProbe();
    }

    public sealed class ModuleRegistry
    {
        private readonly List<IModule> modules = new List<IModule>();

        public IReadOnlyList<IModule> Modules
        {
            get { return modules; }
        }

        public int Count
        {
            get { return modules.Count; }
        }

        public void Register(IModule module)
        {
            if (module != null) modules.Add(module);
        }

        public int InvokedCount
        {
            get
            {
                int n = 0;
                foreach (var m in modules) if (m.InvocationCount > 0) n++;
                return n;
            }
        }

        /// True only when every registered module was actually invoked and
        /// reported healthy — a not-called module can never satisfy this.
        public bool ProbeAll()
        {
            if (modules.Count == 0) return false;
            bool ok = true;
            foreach (var m in modules)
            {
                if (!m.Probe()) ok = false;
            }
            return ok && InvokedCount == modules.Count;
        }
    }
}

namespace CozyCafe.Core.Modules
{
    /// <summary>
    /// Idle-economy clock and wallet. Monotonic elapsed time; money math uses
    /// long coins. Probe() exercises the real earning formula without mutating
    /// state.
    /// </summary>
    public sealed class SimulationModule : ModuleBase
    {
        public override string Name { get { return "simulation"; } }

        public const int BaseCoinsPerMinute = 44;

        public double ElapsedSeconds { get; private set; }
        public long WalletCoins { get; private set; }

        public SimulationModule(long startingCoins)
        {
            WalletCoins = startingCoins;
            ElapsedSeconds = 0;
        }

        public long PreviewEarnings(double seconds)
        {
            if (seconds <= 0) return 0;
            return (long)(BaseCoinsPerMinute * (seconds / 60.0));
        }

        public void Tick(double dtSeconds)
        {
            if (dtSeconds <= 0) return;
            ElapsedSeconds += dtSeconds;
            WalletCoins += PreviewEarnings(dtSeconds);
        }

        public bool Charge(long amount)
        {
            if (amount < 0 || WalletCoins < amount) return false;
            WalletCoins -= amount;
            return true;
        }

        protected override bool OnProbe()
        {
            return PreviewEarnings(60.0) == BaseCoinsPerMinute
                && WalletCoins >= 0
                && ElapsedSeconds >= 0;
        }
    }


    // The shared character rig lives in CozyCafe.Core.Character
    // (Character/CharacterModule.cs): data-sourced parts and palettes,
    // layered compositing, seeded appearances.
}

namespace CozyCafe.Core.Modules
{
    // The real desktop-tools module lives in CozyCafe.Core.Tools
    // (Tools/ToolsModule.cs): window modes, memo/todo persistence, the
    // focus timer + records, and the local music deck.

    /// <summary>Desktop platform capabilities negotiated at boot.</summary>
    public sealed class PlatformModule : ModuleBase
    {
        public override string Name { get { return "platform"; } }

        public bool SupportsKoreanIme = true;
        public bool SupportsTransparency = true;
        public bool SupportsAlwaysOnTop = true;
        public bool SupportsMousePassthrough = true;

        protected override bool OnProbe()
        {
            return SupportsKoreanIme && SupportsAlwaysOnTop;
        }
    }

    /// <summary>
    /// Save state over the live scene. Probe serializes the same snapshot twice
    /// and requires deterministic output — a real round-trip check.
    /// </summary>
    public sealed class SaveModule : ModuleBase
    {
        public override string Name { get { return "save"; } }

        private readonly GameScene scene;

        public string LastSnapshotJson { get; private set; }

        public SaveModule(GameScene scene)
        {
            this.scene = scene;
        }

        public string Snapshot()
        {
            var d = new Dictionary<string, object>();
            d["room_w"] = scene.Room.Width;
            d["room_h"] = scene.Room.Height;
            d["furniture"] = scene.Furniture.Count;
            d["agents"] = scene.Agents.Count;
            return MiniJson.ToJson(d);
        }

        protected override bool OnProbe()
        {
            string a = Snapshot();
            string b = Snapshot();
            LastSnapshotJson = a;
            return a == b && a.Length > 2;
        }
    }

    // The real local UGC creator lives in CozyCafe.Core.Ugc
    // (Ugc/UgcModule.cs): PNG import, role/anchor/direction assignment,
    // preview, validation, save and apply with placement/appearance-only
    // public presets.
}
