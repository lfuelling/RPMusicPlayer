using System.Collections.Generic;
using System.Linq;

using RPMusicPlayer;

namespace RPMusicPlayer.Tests
{
    /// <summary>
    /// Checks the filter and the sort, in particular that the filter reads the tag
    /// it says it reads.
    ///
    /// The filter used to build its list of letters from the artist but match
    /// against every tag at once, so choosing "A" returned songs whose album merely
    /// contained an "a" somewhere. <see cref="LibraryQuery"/> is where those two
    /// halves now have to agree.
    /// </summary>
    internal static class Filter
    {
        private static void True(bool value, string what)
        {
            Program.True(value, what);
        }

        private static void False(bool value, string what)
        {
            Program.False(value, what);
        }

        private static void Equal(string expected, string actual, string what)
        {
            Program.Equal(expected, actual, what);
        }

        private static MusicTrack Track(string fileName, string title, string artist, string album, string genre)
        {
            var tags = new TrackTags
            {
                Title = title,
                Artist = artist,
                Album = album,
                Genre = genre
            };
            return new MusicTrack(fileName, tags);
        }

        /// <summary>
        /// A small library where every tag starts with a different letter, so a
        /// filter on the wrong tag is impossible to miss.
        /// </summary>
        private static List<MusicTrack> Library()
        {
            return new List<MusicTrack>
            {
                Track("one.wav", "Alpha", "Anna", "Zeta", "Rock"),
                Track("two.wav", "Bravo", "Ben", "Yankee", "Jazz"),
                Track("three.wav", "Charlie", "Cara", "Xray", "Blues"),
                Track("four.wav", "Delta", "Dan", "Whiskey", "Rock")
            };
        }

        private static string Names(IEnumerable<MusicTrack> tracks)
        {
            return string.Join(",", tracks.Select(t => t.Title));
        }

        private static string FilterNames(List<MusicTrack> tracks, string filter, SortField field)
        {
            return Names(LibraryQuery.Filter(tracks, filter, field));
        }

        // ------------------------------------------------------------ filter field

        /// <summary>The filter reads the tag it was told to read, and only that one.</summary>
        private static void FilterReadsTheChosenTag()
        {
            var tracks = Library();

            Equal("Alpha", FilterNames(tracks, "A", SortField.Artist), "filter: artist A");
            Equal("Alpha", FilterNames(tracks, "A", SortField.Title), "filter: title A");
            Equal("", FilterNames(tracks, "A", SortField.Album), "filter: no album starts with A");
            Equal("", FilterNames(tracks, "A", SortField.Genre), "filter: no genre starts with A");

            // A tag the library does have a letter for does return just that song.
            Equal("Delta", FilterNames(tracks, "W", SortField.Album), "filter: album W");
            Equal("Alpha,Delta", FilterNames(tracks, "R", SortField.Genre),
                "filter: genre R finds both songs with it");
        }

        /// <summary>
        /// The bug: filtering by artist "A" used to also return a song whose album
        /// began with A, because the match ran against every tag.
        /// </summary>
        private static void FilterDoesNotLeakAcrossTags()
        {
            var tracks = Library();

            // Anna is the only artist starting with A, even though two albums and
            // all four titles also start with letters that would have matched.
            Equal("Alpha", FilterNames(tracks, "A", SortField.Artist),
                "filter: artist A does not match on other tags");
            Equal("Alpha", FilterNames(tracks, "A", SortField.Title),
                "filter: title A does not match on the artist");
        }

        /// <summary>With no filter, everything comes through.</summary>
        private static void NoFilterKeepsEverything()
        {
            var tracks = Library();

            Equal("4", LibraryQuery.Filter(tracks, null, SortField.Artist).Count().ToString(),
                "filter: no filter keeps every song");
            Equal("4", LibraryQuery.Filter(tracks, "", SortField.Artist).Count().ToString(),
                "filter: an empty filter keeps every song");
        }

        /// <summary>
        /// The filter is a starting letter, so a song whose tag has the letter in
        /// the middle is not offered under it.
        /// </summary>
        private static void FilterMatchesTheStartOfTheTag()
        {
            var tracks = new List<MusicTrack>
            {
                Track("a.wav", "Sunrise", "Nyx", "Dawn", "Ambient"),
                Track("b.wav", "Moonset", "Nyx", "Dusk", "Ambient")
            };

            Equal("Sunrise", Names(LibraryQuery.Filter(tracks, "S", SortField.Title)),
                "filter: matches the start of the title");
            Equal("", Names(LibraryQuery.Filter(tracks, "n", SortField.Title)),
                "filter: a letter in the middle of the title does not match");
        }

        private static void FilterIgnoresCase()
        {
            var tracks = Library();

            Equal("Alpha", FilterNames(tracks, "a", SortField.Artist), "filter: lower case matches");
            Equal("Alpha", FilterNames(tracks, "A", SortField.Artist), "filter: upper case matches");
        }

        /// <summary>A track with nothing in the filtered tag cannot match any letter.</summary>
        private static void EmptyTagsDoNotMatchEverything()
        {
            var tracks = new List<MusicTrack>
            {
                Track("a.wav", "Alpha", null, null, null),
                Track("b.wav", "Bravo", "Ben", "Yankee", null)
            };

            Equal("Bravo", Names(LibraryQuery.Filter(tracks, "B", SortField.Artist)),
                "filter: a track with no artist is skipped, not matched");
        }

        // ---------------------------------------------------------- filter options

        /// <summary>
        /// The letters the filter row offers have to be the letters it can match.
        /// These come from one tag, the filter reads another, and the two used to
        /// disagree.
        /// </summary>
        private static void FilterOptionsComeFromTheFilteredTag()
        {
            var tracks = Library();

            var artistOptions = LibraryQuery.FilterOptions(tracks, SortField.Artist);
            Equal("|A|B|C|D", string.Join("|", artistOptions), "filter: artist letters");

            var titleOptions = LibraryQuery.FilterOptions(tracks, SortField.Title);
            Equal("|A|B|C|D", string.Join("|", titleOptions), "filter: title letters");

            // Every offered letter has to select at least one song.
            foreach (var option in artistOptions.Where(o => !string.IsNullOrEmpty(o)))
            {
                True(LibraryQuery.Filter(tracks, option, SortField.Artist).Any(),
                    $"filter: offered letter {option} matches something");
            }
        }

        /// <summary>
        /// Each tag offers its own letters, which is the whole point of choosing a
        /// tag to filter on.
        /// </summary>
        private static void EachTagOffersItsOwnLetters()
        {
            var tracks = Library();

            Equal("|B|J|R", string.Join("|", LibraryQuery.FilterOptions(tracks, SortField.Genre)),
                "filter: genre letters");
            Equal("|W|X|Y|Z", string.Join("|", LibraryQuery.FilterOptions(tracks, SortField.Album)),
                "filter: album letters");
            Equal("|F|O|T", string.Join("|", LibraryQuery.FilterOptions(tracks, SortField.FileName)),
                "filter: file name letters");
        }

        /// <summary>No filter is always the first choice.</summary>
        private static void NoFilterIsAlwaysOffered()
        {
            var options = LibraryQuery.FilterOptions(Library(), SortField.Artist);

            Equal("", options[0], "filter: no filter is offered first");
            Equal("5", options.Count.ToString(), "filter: four letters plus no filter");
        }

        /// <summary>A tag the library has nothing for offers no letters at all.</summary>
        private static void EmptyTagOffersNoLetters()
        {
            var tracks = new List<MusicTrack> { Track("a.wav", "Alpha", null, null, null) };

            var options = LibraryQuery.FilterOptions(tracks, SortField.Artist);
            Equal("1", options.Count.ToString(), "filter: no artist means no letters");
            Equal("", options[0], "filter: only no filter is offered");
        }

        private static void ValuesThatDoNotStartWithALetterAreSkipped()
        {
            Equal("A", LibraryQuery.FirstLetter("Anna"), "letter: from a name");
            Equal(null, LibraryQuery.FirstLetter("9Lives"), "letter: a leading digit is not a letter");
            Equal(null, LibraryQuery.FirstLetter(null), "letter: nothing has no letter");
            Equal(null, LibraryQuery.FirstLetter("   "), "letter: whitespace has no letter");
            Equal("A", LibraryQuery.FirstLetter("  Anna"), "letter: leading spaces are ignored");
            Equal("É", LibraryQuery.FirstLetter("Émile"), "letter: accented letters count");
        }

        // ----------------------------------------------------------- filter cycling

        private static void FilterCyclesThroughTheAvailableLetters()
        {
            var options = LibraryQuery.FilterOptions(Library(), SortField.Artist);

            Equal("A", LibraryQuery.StepFilter("", options, 1), "filter: first step is A");
            Equal("B", LibraryQuery.StepFilter("A", options, 1), "filter: steps forward");
            Equal("", LibraryQuery.StepFilter("D", options, 1), "filter: wraps back to none");
            Equal("D", LibraryQuery.StepFilter("", options, -1), "filter: steps backwards");
        }

        /// <summary>
        /// Changing which tag is filtered on leaves a letter from the old tag on
        /// screen, filtering nothing at all.
        /// </summary>
        private static void UnavailableFilterFallsBackToNone()
        {
            var options = LibraryQuery.FilterOptions(Library(), SortField.Artist);

            // "Q" is not offered by any of these tags.
            Equal("", LibraryQuery.StepFilter("Q", options, 1),
                "filter: a letter that is not offered falls back to none");
            Equal("", LibraryQuery.StepFilter("Q", new List<string>(), 1),
                "filter: with no options at all the filter is cleared");
        }

        // ------------------------------------------------------------------ sorting

        private static void SortingUsesTheChosenTag()
        {
            var tracks = Library();

            Equal("Alpha,Bravo,Charlie,Delta",
                Names(LibraryQuery.Sort(tracks, SortField.Title, false)), "sort: by title");
            Equal("Delta,Charlie,Bravo,Alpha",
                Names(LibraryQuery.Sort(tracks, SortField.Album, false)), "sort: by album");
            Equal("Charlie,Bravo,Alpha,Delta",
                Names(LibraryQuery.Sort(tracks, SortField.Genre, false)), "sort: by genre");
        }

        private static void DescendingIsTheReverseOrder()
        {
            var tracks = Library();

            Equal("Delta,Charlie,Bravo,Alpha",
                Names(LibraryQuery.Sort(tracks, SortField.Title, true)), "sort: title descending");
        }

        /// <summary>Sorting after filtering must give the same result as the reverse.</summary>
        private static void FilterAndSortCompose()
        {
            var tracks = Library();

            var matching = LibraryQuery.Filter(tracks, "A", SortField.Artist);
            Equal("Alpha", Names(LibraryQuery.Sort(matching, SortField.Title, false)),
                "sort: filtering then sorting keeps just the match");
        }

        internal static void Run()
        {
            FilterReadsTheChosenTag();
            FilterDoesNotLeakAcrossTags();
            NoFilterKeepsEverything();
            FilterMatchesTheStartOfTheTag();
            FilterIgnoresCase();
            EmptyTagsDoNotMatchEverything();

            FilterOptionsComeFromTheFilteredTag();
            EachTagOffersItsOwnLetters();
            NoFilterIsAlwaysOffered();
            EmptyTagOffersNoLetters();
            ValuesThatDoNotStartWithALetterAreSkipped();

            FilterCyclesThroughTheAvailableLetters();
            UnavailableFilterFallsBackToNone();

            SortingUsesTheChosenTag();
            DescendingIsTheReverseOrder();
            FilterAndSortCompose();
        }
    }
}
