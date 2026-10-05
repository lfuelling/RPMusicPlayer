using UnityEngine;

namespace RPMusicPlayer
{
    /// <summary>
    /// Where the player is right now, in the terms the plugin cares about.
    ///
    /// Kerbal Space Program 1.12 has no "player is on EVA" flag, so this goes by the
    /// camera mode: while the player is looking at a pod or IVA screen the mode is IVA
    /// or Internal, and a kerbal outside the vessel (or a chase camera) is anything else.
    /// </summary>
    public static class FlightContext
    {
        public static bool InFlight
        {
            get { return HighLogic.LoadedSceneIsFlight; }
        }

        public static Vessel Vessel
        {
            get { return FlightGlobals.ActiveVessel; }
        }

        /// <summary>True while the player is inside a pod or IVA.</summary>
        public static bool InsidePod
        {
            get
            {
                var manager = CameraManager.Instance;
                if (manager == null)
                {
                    return false;
                }

                var mode = manager.currentCameraMode;
                return mode == CameraManager.CameraMode.IVA || mode == CameraManager.CameraMode.Internal;
            }
        }

        /// <summary>
        /// True while there is a vessel under control that the plugin should be managing.
        /// </summary>
        public static bool HasActiveVessel
        {
            get { return InFlight && Vessel != null; }
        }
    }
}