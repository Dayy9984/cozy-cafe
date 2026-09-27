using CozyCafe.Core.Scene;

namespace CozyCafe.Core.Gauntlet
{
    /// <summary>
    /// Builds the live game state a stage renders or captures. Both capture
    /// hosts (editor camera path and the CLI rasterizer) draw this same state.
    /// </summary>
    public static class StageScenes
    {
        public static GameScene Build(string stage)
        {
            var boot = GameBootstrap.Create();
            boot.LoadDefaultScene();
            boot.Registry.ProbeAll();
            return boot.Scene;
        }
    }
}
