using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RPMusicPlayer
{
    public enum SortField
    {
        Artist,
        Album,
        Genre,
        Title,
        FileName
    }

    /// <summary>
    /// The set of songs found in the music folder, plus the filtering and sorting
    /// used by the browser page.
    /// </summary>
    public sealed class MusicLibrary
    {
        private readonly List<MusicTrack> tracks = new List<MusicTrack>();

        internal IEnumerable<MusicTrack> All
        {
            get { return tracks; }
        }

        internal int Count
        {
            get { return tracks.Count; }
        }

        internal bool IsScanning { get; private set; }

        /// <summary>
        /// Walks the music folder in chunks so that a large library does not stall a frame.
        /// Returns false once every file has been looked at.
        /// </summary>
        internal bool ScanStep(int fileBudget)
        {
            if (!IsScanning)
            {
                return true;
            }

            var settings = Settings.Current;
            var pending = new Queue<string>(pendingFiles);
            var processed = 0;

            while (pending.Count > 0 && processed < fileBudget)
            {
                var file = pending.Dequeue();
                processed++;

                try
                {
                    var tags = TagReader.Read(file);
                    var track = new MusicTrack(file, tags);

                    if (tags.HasVideoStream)
                    {
                        track.Warning = "video stream";
                        Log.Warning("'{0}' holds a video stream as well as audio. The audio engine may " +
                            "or may not decode one of these; the track can still be selected, and if " +
                            "loading fails, that is reported in the log.", track.FileName);
                    }

                    tracks.Add(track);
                }
                catch (Exception e)
                {
                    Log.Info("Skipping '{0}': {1}", Path.GetFileName(file), e.Message);
                }
            }

            if (pending.Count > 0)
            {
                pendingFiles = pending;
                return false;
            }

            tracks.Sort((a, b) => string.Compare(a.ArtistAndTitle, b.ArtistAndTitle, StringComparison.OrdinalIgnoreCase));
            IsScanning = false;
            Log.Info("Music library ready: {0} tracks from {1}", tracks.Count, settings.MusicPath);
            return true;
        }

        private Queue<string> pendingFiles = new Queue<string>();

        /// <summary>
        /// Drops the current library and starts collecting file names again.
        /// The caller then drives <see cref="ScanStep"/> once per frame.
        /// </summary>
        internal void BeginScan()
        {
            tracks.Clear();
            pendingFiles = new Queue<string>();
            IsScanning = true;

            var settings = Settings.Current;
            if (!Directory.Exists(settings.MusicPath))
            {
                Log.Warning("Music folder not found: {0}", settings.MusicPath);
                IsScanning = false;
                return;
            }

            try
            {
                CollectFiles(settings.MusicPath, settings);
            }
            catch (Exception e)
            {
                Log.Error("Could not enumerate the music folder: {0}", e.Message);
                IsScanning = false;
            }
        }

        private void CollectFiles(string folder, Settings settings)
        {
            string[] files;
            try
            {
                files = Directory.GetFiles(folder);
            }
            catch (Exception e)
            {
                Log.Warning("Could not read folder '{0}': {1}", folder, e.Message);
                return;
            }

            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                var extension = Path.GetExtension(file).TrimStart('.').ToLowerInvariant();
                if (settings.IsSupportedExtension(extension))
                {
                    pendingFiles.Enqueue(file);
                }
            }

            if (!settings.ScanSubFolders)
            {
                return;
            }

            string[] subFolders;
            try
            {
                subFolders = Directory.GetDirectories(folder);
            }
            catch (Exception e)
            {
                Log.Warning("Could not read sub folders of '{0}': {1}", folder, e.Message);
                return;
            }

            Array.Sort(subFolders, StringComparer.OrdinalIgnoreCase);
            foreach (var subFolder in subFolders)
            {
                CollectFiles(subFolder, settings);
            }
        }

        /// <summary>
        /// The songs the browser shows: filtered by <paramref name="filter"/> and sorted.
        /// This is also the queue that gets played once a song is picked.
        /// </summary>
        internal List<MusicTrack> View(string filter, SortField sortField, bool descending)
        {
            IEnumerable<MusicTrack> query = tracks;

            if (!string.IsNullOrEmpty(filter))
            {
                var needle = filter.Trim();
                query = query.Where(t => Matches(t, needle));
            }

            var sorted = Sort(query, sortField, descending);
            return new List<MusicTrack>(sorted);
        }

        private static bool Matches(MusicTrack track, string needle)
        {
            return Contains(track.Title, needle)
                || Contains(track.Artist, needle)
                || Contains(track.Album, needle)
                || Contains(track.Genre, needle)
                || Contains(track.FileName, needle);
        }

        private static bool Contains(string value, string needle)
        {
            return !string.IsNullOrEmpty(value)
                && value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static IEnumerable<MusicTrack> Sort(IEnumerable<MusicTrack> query, SortField sortField, bool descending)
        {
            Func<MusicTrack, string> key = track =>
            {
                switch (sortField)
                {
                    case SortField.Album: return track.Album ?? string.Empty;
                    case SortField.Genre: return track.Genre ?? string.Empty;
                    case SortField.Title: return track.DisplayTitle;
                    case SortField.FileName: return track.FileName ?? string.Empty;
                    default: return track.Artist ?? string.Empty;
                }
            };

            // Sorted in a stable, culture independent way so the order does not change
            // with the player's regional settings.
            var buffer = query.ToList();
            var indexed = buffer.Select((track, index) => new { track, index });

            var sorted = sortField == SortField.FileName
                ? indexed.OrderBy(x => x.track.FileName, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.index)
                : indexed.OrderBy(x => key(x.track), StringComparer.OrdinalIgnoreCase)
                         .ThenBy(x => x.track.DisplayTitle, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(x => x.index);

            return descending ? sorted.Reverse().Select(x => x.track) : sorted.Select(x => x.track);
        }
    }
}