using UnityEngine;

namespace RPMusicPlayer
{
    /// <summary>
    /// Plugin entry point.
    ///
    /// This only has one job: make sure the persistent host object exists. All of the
    /// per frame work lives on that host instead, because it is the thing that survives
    /// scene changes. An earlier version ran the work from a scene scoped addon, which
    /// stopped as soon as the scene changed and left the plugin silently idle.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    public class RPMusicPlayerAddon : MonoBehaviour
    {
        private void Start()
        {
            PlayerManager.EnsureHost();
        }
    }
}