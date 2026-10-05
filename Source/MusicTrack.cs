using System;
using System.Globalization;

namespace RPMusicPlayer
{
    /// <summary>
    /// One playable file on disk, together with whatever metadata we could read for it.
    /// </summary>
    public sealed class MusicTrack
    {
        internal string Path;
        internal string FileName;

        internal string Title;
        internal string Artist;
        internal string Album;
        internal string Genre;

        /// <summary>Set once the clip has actually been decoded.</summary>
        internal double DurationSeconds;

        internal MusicTrack(string path, TrackTags tags)
        {
            Path = path;
            FileName = System.IO.Path.GetFileName(path);
            Title = tags.Title;
            Artist = tags.Artist;
            Album = tags.Album;
            Genre = tags.Genre;
        }

        /// <summary>The title shown in the browser: the tag, or the file name when there is no tag.</summary>
        internal string DisplayTitle
        {
            get { return string.IsNullOrEmpty(Title) ? NameWithoutExtension : Title; }
        }

        internal string NameWithoutExtension
        {
            get { return System.IO.Path.GetFileNameWithoutExtension(Path); }
        }

        /// <summary>"Artist - Title", with the missing half simply dropped.</summary>
        internal string ArtistAndTitle
        {
            get
            {
                bool hasArtist = !string.IsNullOrEmpty(Artist);
                bool hasTitle = !string.IsNullOrEmpty(Title);
                if (hasArtist && hasTitle)
                {
                    return Artist + " - " + Title;
                }
                return hasArtist ? Artist : DisplayTitle;
            }
        }

        internal string Extension
        {
            get { return (System.IO.Path.GetExtension(Path) ?? string.Empty).TrimStart('.').ToLowerInvariant(); }
        }

        internal bool HasTags
        {
            get
            {
                return !string.IsNullOrEmpty(Title)
                    || !string.IsNullOrEmpty(Artist)
                    || !string.IsNullOrEmpty(Album)
                    || !string.IsNullOrEmpty(Genre);
            }
        }

        internal string DurationText
        {
            get { return DurationSeconds > 0 ? FormatDuration(DurationSeconds) : "--:--"; }
        }

        internal static string FormatDuration(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
            {
                return "--:--";
            }

            var span = TimeSpan.FromSeconds(seconds);
            if (span.TotalHours >= 1)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}",
                    (int)span.TotalHours, span.Minutes, span.Seconds);
            }

            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}",
                span.Minutes, span.Seconds);
        }

        public override string ToString()
        {
            return ArtistAndTitle + " (" + System.IO.Path.GetFileName(Path) + ")";
        }
    }
}