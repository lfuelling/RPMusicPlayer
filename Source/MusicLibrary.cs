using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RPMusicPlayer
{
    /// <summary>
    /// The set of songs found in the music folder. The filtering and sorting that
    /// turn it into a browsable list live in <see cref="LibraryQuery"/>, so they can
    /// be tested without the game's configuration.
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
        /// The songs the browser shows: filtered by <paramref name="filter"/> on the
        /// given tag, then sorted. This is also the queue that gets played once a
        /// song is picked.
        /// </summary>
        internal List<MusicTrack> View(string filter, SortField filterField, SortField sortField, bool descending)
        {
            var matching = LibraryQuery.Filter(tracks, filter, filterField);
            var sorted = LibraryQuery.Sort(matching, sortField, descending);
            return new List<MusicTrack>(sorted);
        }
    }
}