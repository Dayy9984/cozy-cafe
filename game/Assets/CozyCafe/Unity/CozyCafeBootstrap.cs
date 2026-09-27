using CozyCafe.Core;
using UnityEngine;

namespace CozyCafe.Unity
{
    /// <summary>
    /// Scene entry point: boots the shared core (the same code the .NET gate
    /// host runs) and builds the live view from real state.
    /// </summary>
    public sealed class CozyCafeBootstrap : MonoBehaviour
    {
        public GameBootstrap Boot { get; private set; }

        private void Awake()
        {
            Boot = GameBootstrap.Create();
            Boot.LoadDefaultScene();
            Boot.Registry.ProbeAll();
            StageViewBuilder.Build(Boot.Scene).transform.SetParent(transform, false);
        }
    }
}
