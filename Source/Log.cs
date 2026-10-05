using UnityEngine;

namespace RPMusicPlayer
{
    /// <summary>
    /// Single place for plugin logging so that every line is easy to spot in KSP.log.
    /// </summary>
    internal static class Log
    {
        private const string Prefix = "[RPMusicPlayer] ";

        internal static void Info(string message)
        {
            Debug.Log(Prefix + message);
        }

        internal static void Info(string format, params object[] args)
        {
            Debug.Log(Prefix + SafeFormat(format, args));
        }

        internal static void Warning(string message)
        {
            Debug.LogWarning(Prefix + message);
        }

        internal static void Warning(string format, params object[] args)
        {
            Debug.LogWarning(Prefix + SafeFormat(format, args));
        }

        internal static void Error(string message)
        {
            Debug.LogError(Prefix + message);
        }

        internal static void Error(string format, params object[] args)
        {
            Debug.LogError(Prefix + SafeFormat(format, args));
        }

        private static string SafeFormat(string format, object[] args)
        {
            try
            {
                return string.Format(format, args);
            }
            catch
            {
                // A broken log message must never take the plugin down.
                return format;
            }
        }
    }
}