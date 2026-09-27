using System;
using System.IO;
using CozyCafe.Core;
using CozyCafe.Core.Gauntlet;
using UnityEditor;
using UnityEngine;

namespace CozyCafe.Editor
{
    /// <summary>
    /// Unity CASE host, invoked as:
    ///   unity -batchmode -projectPath game -executeMethod
    ///         CozyCafe.Editor.GauntletEntry.Run -quit
    /// Reads GAUNTLET_STAGE, writes CASE&lt;TAB&gt;key&lt;TAB&gt;json lines to
    /// GAUNTLET_RESULTS (absolute path). Every value is computed by the shared
    /// core modules — this class only transports them. Internal failure exits
    /// the editor with code 2.
    /// </summary>
    public static class GauntletEntry
    {
        public static void Run()
        {
            string stage = Environment.GetEnvironmentVariable("GAUNTLET_STAGE");
            string results = Environment.GetEnvironmentVariable("GAUNTLET_RESULTS");
            int exitCode = 2;
            try
            {
                if (string.IsNullOrEmpty(stage) || string.IsNullOrEmpty(results))
                {
                    Debug.LogError("GauntletEntry: GAUNTLET_STAGE and GAUNTLET_RESULTS env vars are required");
                }
                else
                {
                    var cases = StageCases.Run(stage);
                    if (cases == null)
                    {
                        File.WriteAllText(results, "NOT_IMPLEMENTED\t" + stage + "\n");
                        Debug.LogWarning("GauntletEntry: stage '" + stage + "' has no case runner");
                    }
                    else
                    {
                        using (var w = new StreamWriter(results, false))
                        {
                            foreach (var c in cases)
                            {
                                w.WriteLine("CASE\t" + c.Key + "\t" + MiniJson.ToJson(c.Value));
                            }
                        }
                        exitCode = 0;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("GauntletEntry failed: " + e);
                try
                {
                    if (!string.IsNullOrEmpty(results))
                    {
                        File.WriteAllText(results, "ERROR\t" + stage + "\t" + e.Message + "\n");
                    }
                }
                catch (Exception) { /* results path may be unwritable */ }
            }
            EditorApplication.Exit(exitCode);
        }
    }
}
