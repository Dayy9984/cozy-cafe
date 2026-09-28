using System;
using System.Collections.Generic;
using System.IO;
using CozyCafe.Core;
using CozyCafe.Core.Gauntlet;
using CozyCafe.Core.Render;

namespace CozyCafe.Cli
{
    /// <summary>
    /// Gate host over the shared core (no engine dependency).
    ///
    ///   GameCli case  &lt;stage&gt;            prints CASE\tkey\tjson lines whose
    ///                                        values are computed by real module
    ///                                        calls; exit 0 on success.
    ///   GameCli render &lt;stage&gt; &lt;abs.png&gt;  software-rasterizes the stage's
    ///                                        real game state to a PNG file.
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "case")
            {
                return RunCase(args[1]);
            }
            if (args.Length == 3 && args[0] == "render")
            {
                return RunRender(args[1], args[2]);
            }
            Console.Error.WriteLine("usage: GameCli case <stage> | render <stage> <abs.png>");
            return 2;
        }

        private static int RunCase(string stage)
        {
            try
            {
                List<CaseResult> cases = StageCases.Run(stage);
                if (cases == null)
                {
                    Console.Error.WriteLine("NOT_IMPLEMENTED: stage '" + stage + "' has no case runner");
                    return 2;
                }
                foreach (var c in cases)
                {
                    Console.Out.Write("CASE\t" + c.Key + "\t" + MiniJson.ToJson(c.Value) + "\n");
                }
                Console.Out.Flush();
                return 0;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("GameCli case '" + stage + "' failed: " + e);
                return 2;
            }
        }

        private static int RunRender(string stage, string outputPath)
        {
            try
            {
                if (!Path.IsPathRooted(outputPath))
                {
                    Console.Error.WriteLine("render output path must be absolute: " + outputPath);
                    return 2;
                }
                var scene = StageScenes.Build(stage);
                if (scene == null || !scene.IsLoaded)
                {
                    Console.Error.WriteLine("NOT_IMPLEMENTED: stage '" + stage + "' produced no scene");
                    return 2;
                }
                byte[] png = SceneRenderer.RenderPng(scene, scene.Zoom);
                File.WriteAllBytes(outputPath, png);
                Console.Out.Write("RENDER\t" + stage + "\t" + outputPath + "\n");
                Console.Out.Flush();
                return 0;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("GameCli render '" + stage + "' failed: " + e);
                return 2;
            }
        }
    }
}
