using System;
using System.Collections.Generic;
using System.Linq;

namespace RPMusicPlayer
{
    /// <summary>
    /// Which tag the list is ordered by, and which tag the filter applies to.
    /// </summary>
    public enum SortField
    {
        Artist,
        Album,
        Genre,
        Title,
        FileName
    }

    /// <summary>
    /// Filtering and sorting of the song list.
    ///
    /// This is deliberately separate from <see cref="MusicLibrary"/>, which walks
    /// the music folder and therefore needs the game's configuration. Everything
    /// here is pure text handling over a set of tracks, so the rules can be tested
    /// without KSP: in particular which tag the filter reads, and that the letters
    /// offered by the filter row are the ones it actually matches.
    ///
    /// Held as statics because there is no state to keep between calls; the library
    /// owns the tracks, this only reads them.
    /// </summary>
    internal static class LibraryQuery
    {
        /// <summary>
        /// The value of one tag on one track, which is what both the sort order and
        /// the filter are expressed in. Never null, so callers do not have to guard.
        /// </summary>
        internal static string Value(MusicTrack track, SortField field)
        {
            switch (field)
            {
                case SortField.Album: return track.Album ?? string.Empty;
                case SortField.Genre: return track.Genre ?? string.Empty;
                case SortField.Title: return track.DisplayTitle;
                case SortField.FileName: return track.FileName ?? string.Empty;
                default: return track.Artist ?? string.Empty;
            }
        }

        /// <summary>
        /// The songs that match the filter, read from the given tag only.
        ///
        /// The filter is a starting letter, so it matches the start of the value
        /// rather than anywhere in it. It used to match any tag anywhere in the
        /// song, which meant a track could be offered under the letter "A" because
        /// its album had an "a" in it while the list was sorted by artist.
        /// </summary>
        internal static IEnumerable<MusicTrack> Filter(IEnumerable<MusicTrack> query, string filter, SortField field)
        {
            if (string.IsNullOrEmpty(filter))
            {
                return query;
            }

            var needle = filter.Trim();

            // A track with nothing in the filtered tag cannot start with any
            // letter, so it drops out rather than matching everything.
            return query.Where(t => Value(t, field).StartsWith(needle, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// The songs in display order. Sorted in a stable, culture independent way
        /// so the order does not change with the player's regional settings.
        /// </summary>
        internal static IEnumerable<MusicTrack> Sort(IEnumerable<MusicTrack> query, SortField field, bool descending)
        {
            var buffer = query.ToList();
            var indexed = buffer.Select((track, index) => new { track, index });

            var sorted = field == SortField.FileName
                ? indexed.OrderBy(x => x.track.FileName, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.index)
                : indexed.OrderBy(x => Value(x.track, field), StringComparer.OrdinalIgnoreCase)
                         .ThenBy(x => x.track.DisplayTitle, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(x => x.index);

            return descending ? sorted.Reverse().Select(x => x.track) : sorted.Select(x => x.track);
        }

        /// <summary>
        /// The filter row's choices: no filter, then the starting letters that are
        /// actually present for the tag being filtered on.
        ///
        /// Built from the same tag the filter matches on, so the letters offered are
        /// exactly the letters that select something. A tag the library has nothing
        /// for yields no letters, which leaves the row on "none".
        /// </summary>
        internal static List<string> FilterOptions(IEnumerable<MusicTrack> tracks, SortField field)
        {
            var letters = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var track in tracks)
            {
                var first = FirstLetter(Value(track, field));
                if (first != null)
                {
                    letters.Add(first);
                }
            }

            var options = new List<string> { string.Empty };
            foreach (var letter in letters)
            {
                options.Add(letter);
            }

            return options;
        }

        /// <summary>
        /// The single letter a value files under, or null when it does not start
        /// with a letter at all (a file name beginning with a digit, say).
        /// </summary>
        internal static string FirstLetter(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            var trimmed = value.TrimStart();
            if (trimmed.Length == 0 || !char.IsLetter(trimmed[0]))
            {
                return null;
            }

            return trimmed.Substring(0, 1).ToUpperInvariant();
        }

        /// <summary>
        /// Steps through the available filter choices in the direction given,
        /// wrapping round the end. A letter that is no longer in the library falls
        /// back to the first one rather than leaving a filter that matches nothing.
        /// </summary>
        internal static string StepFilter(string current, List<string> options, int direction)
        {
            if (options.Count == 0)
            {
                return string.Empty;
            }

            for (int i = 0; i < options.Count; i++)
            {
                if (string.Equals(options[i], current, StringComparison.OrdinalIgnoreCase))
                {
                    return options[(i + direction + options.Count) % options.Count];
                }
            }

            // The current filter is no longer available, so start over from none.
            return options[0];
        }
    }
}
