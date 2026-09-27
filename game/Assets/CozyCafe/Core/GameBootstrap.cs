using CozyCafe.Core.Modules;
using CozyCafe.Core.Scene;

namespace CozyCafe.Core
{
    /// <summary>
    /// Builds and loads the game's real default scene and wires every core
    /// module to it. Both hosts (editor entry point and the .NET CLI) call this
    /// — there is no separate mock path.
    /// </summary>
    public sealed class GameBootstrap
    {
        public GameScene Scene { get; private set; }
        public ModuleRegistry Registry { get; private set; }

        public static GameBootstrap Create()
        {
            var boot = new GameBootstrap();
            boot.Scene = BuildDefaultScene();
            boot.Registry = BuildRegistry(boot.Scene);
            return boot;
        }

        /// Validates then marks the scene loaded. Returns the outcome.
        public bool LoadDefaultScene()
        {
            if (Scene == null || !Scene.Validate())
            {
                return false;
            }
            Scene.IsLoaded = true;
            return true;
        }

        private static GameScene BuildDefaultScene()
        {
            var s = new GameScene();
            s.Room = new RoomGrid(8, 8); // MVP default room footprint

            s.Furniture.Add(new Furniture(FurnitureKind.EspressoMachine, 1, 1));
            s.Furniture.Add(new Furniture(FurnitureKind.Counter, 1, 2));
            s.Furniture.Add(new Furniture(FurnitureKind.Table, 3, 3));
            s.Furniture.Add(new Furniture(FurnitureKind.Chair, 3, 4));
            s.Furniture.Add(new Furniture(FurnitureKind.Table, 5, 2));
            s.Furniture.Add(new Furniture(FurnitureKind.Chair, 5, 1));

            s.Agents.Add(new Agent { Name = "staff_0", PresetId = 0, GridX = 2.5, GridY = 2.5, IsStaff = true });
            s.Agents.Add(new Agent { Name = "customer_0", PresetId = 2, GridX = 6.5, GridY = 5.5, IsStaff = false });
            return s;
        }

        private static ModuleRegistry BuildRegistry(GameScene scene)
        {
            var r = new ModuleRegistry();
            r.Register(new SimulationModule(0));
            r.Register(new LayoutModule(scene));
            r.Register(new CharacterModule());
            r.Register(new ToolsModule());
            r.Register(new PlatformModule());
            r.Register(new SaveModule(scene));
            r.Register(new UgcModule());
            return r;
        }
    }
}
