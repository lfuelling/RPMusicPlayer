using System;

namespace RPMusicPlayer
{
    /// <summary>
    /// Stands in for the real logger, which pulls in UnityEngine and therefore cannot
    /// be loaded outside the game.
    /// </summary>
    internal static class Log
    {
        internal static void Info(string message)
        {
        }

        internal static void Info(string format, params object[] args)
        {
        }

        internal static void Warning(string message)
        {
        }

        internal static void Warning(string format, params object[] args)
        {
        }

        internal static void Error(string message)
        {
        }

        internal static void Error(string format, params object[] args)
        {
        }
    }
}