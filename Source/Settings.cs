using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RPMusicPlayer
{
    /// <summary>
    /// User configuration, read from the RPMUSICPLAYER node in
    /// GameData/RPMusicPlayer/RPMusicPlayer.cfg.
    /// </summary>
    internal sealed class Settings
    {
        internal const string ConfigNodeName = "RPMUSICPLAYER";

                private const string DefaultMusicPath = "GameData/RPMusicPlayer/Music";

        private static Settings current;

        internal static Settings Current
        {
            get
            {
                if (current == null)
                {
                    current = new Settings();
                    current.Load();
                }
                return current;
            }
        }

        /// <summary>Absolute path of the folder that is scanned for music.</summary>
        internal string MusicPath = string.Empty;

        /// <summary>Music volume, 0 to 1, before KSP's master volume is applied.</summary>
        internal float Volume = 0.7f;

        /// <summary>Scan sub folders of the music folder as well.</summary>
        internal bool ScanSubFolders = true;

                /// <summary>Scan the music folder when the game starts.</summary>
                internal bool ScanOnStart = true;

        /// <summary>Lower case file extensions that are considered playable, without the dot.</summary>
        internal readonly List<string> Extensions = new List<string>();

        /// <summary>
        /// Global button that opens the browser: "auto" (the default), a button name,
        /// "screen" for the monitor screen itself, or "none" to disable.
        /// </summary>
        internal string EntryButton = "auto";

        /// <summary>
        /// Name of the RasterPropMonitor page whose button should open the music player,
        /// for example "resources". The music player then becomes a second mode on that
        /// button. Takes priority over <see cref="EntryButton"/>.
        /// </summary>
        internal string EntryPage = string.Empty;

        /// <summary>
        /// Files larger than this are streamed from disk instead of being decoded into
        /// memory. Zero disables streaming, a negative value streams everything.
        /// </summary>
        internal int StreamAboveMegabytes = 24;

        /// <summary>
        /// Also pause when the player leaves the cockpit, not just when the vessel or
        /// scene changes. Off by default: stepping out to the chase camera is not the
        /// same as the kerbals being outside, and music is meant to keep playing.
        /// </summary>
        internal bool PauseWhenOutsideIva;

        private Settings()
        {
            // Wave is the only format the game's audio engine is verified to decode.
            // Others can be added with EXTENSIONS, but they are not guaranteed: a
            // format the engine refuses loads as an empty clip, which shows up as a
            // track that stops instead of playing.
            Extensions.AddRange(new[] { "wav" });
        }

        private void Load()
        {
            ConfigNode node = null;

            try
            {
                var nodes = GameDatabase.Instance.GetConfigNodes(ConfigNodeName);
                if (nodes != null && nodes.Length > 0)
                {
                    node = nodes[0];
                }
            }
            catch (Exception e)
            {
                Log.Warning("Could not read the configuration file, using defaults. {0}", e.Message);
            }

            MusicPath = ResolvePath(ReadString(node, "MUSICPATH", DefaultMusicPath));
            Volume = Mathf.Clamp01(ReadFloat(node, "VOLUME", Volume));
            ScanSubFolders = ReadBool(node, "SCANSUBFOLDERS", ScanSubFolders);
            ScanOnStart = ReadBool(node, "SCANONSTART", ScanOnStart);
            PauseWhenOutsideIva = ReadBool(node, "PAUSEWHENOUTSIDEIVA", PauseWhenOutsideIva);
            EntryButton = ReadString(node, "ENTRYBUTTON", "auto").Trim();
            EntryPage = ReadString(node, "ENTRYPAGE", string.Empty).Trim();

            int streamAbove;
            if (node != null && node.HasValue("STREAMABOVE")
                && int.TryParse(node.GetValue("STREAMABOVE"), out streamAbove))
            {
                StreamAboveMegabytes = streamAbove;
            }

            var extensions = ReadString(node, "EXTENSIONS", string.Empty);
            if (!string.IsNullOrEmpty(extensions))
            {
                Extensions.Clear();
                foreach (var token in extensions.Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmed = token.Trim().TrimStart('.').ToLowerInvariant();
                    if (trimmed.Length > 0)
                    {
                        Extensions.Add(trimmed);
                    }
                }
            }

            Log.Info("Music folder: {0}", MusicPath);
            Log.Info("Volume: {0}, extensions: {1}", Volume, string.Join(", ", Extensions.ToArray()));
            if (!Directory.Exists(MusicPath))
            {
                Log.Warning("Music folder does not exist yet: {0}", MusicPath);
            }
        }

        internal bool IsSupportedExtension(string extension)
        {
            return !string.IsNullOrEmpty(extension) && Extensions.Contains(extension.ToLowerInvariant());
        }

        private static string ReadString(ConfigNode node, string name, string fallback)
        {
            if (node != null && node.HasValue(name))
            {
                var value = node.GetValue(name);
                if (!string.IsNullOrEmpty(value))
                {
                    return value.Trim();
                }
            }
            return fallback;
        }

        private static float ReadFloat(ConfigNode node, string name, float fallback)
        {
            float parsed;
            if (node != null && node.HasValue(name) && float.TryParse(node.GetValue(name), out parsed))
            {
                return parsed;
            }
            return fallback;
        }

        private static bool ReadBool(ConfigNode node, string name, bool fallback)
        {
            if (node != null && node.HasValue(name))
            {
                var value = node.GetValue(name);
                if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            return fallback;
        }

        /// <summary>
        /// Accepts both absolute paths and paths relative to the KSP install directory,
        /// so the same configuration file works on Windows, macOS and Linux.
        /// </summary>
        private static string ResolvePath(string configured)
        {
            if (string.IsNullOrEmpty(configured))
            {
                configured = DefaultMusicPath;
            }

            configured = configured.Replace('\\', '/').TrimEnd('/');

            string root = KSPUtil.ApplicationRootPath ?? string.Empty;
            if (configured.IndexOf('/') == 0 || configured.IndexOf(':') == 1)
            {
                return Path.GetFullPath(configured);
            }

            return Path.GetFullPath(Path.Combine(root, configured));
        }
    }
}