using System;
using System.Collections.Generic;
using System.Text;
using JSI;
using UnityEngine;

namespace RPMusicPlayer
{
    /// <summary>
    /// The song browser: a scrollable, sortable and filterable list of everything in
    /// the music folder. Picking a song starts it playing, with the rest of the current
    /// view queued up behind it.
    /// </summary>
    public class RPMusicPlayerBrowser : MusicPageHandler
    {
        /// <summary>
        /// Items that sit above the songs: a spacer and the header. Keeping the header
        /// as an ordinary menu item is what lets it sit below the songs' own top line,
        /// because TextMenu can only put a fixed title above the list.
        /// </summary>
        private const int LeadingItems = 2;

        private readonly TextMenu menu = new TextMenu();
        private List<MusicTrack> view = new List<MusicTrack>();

        /// <summary>
        /// Menu indices of the rows left and right can step through, so those
        /// buttons act on whichever of them is under the cursor. -1 when the row
        /// is not on the page, which stops a stale index from firing.
        /// </summary>
        private int sortRow = -1;
        private int orderRow = -1;
        private int filterRow = -1;

        public override void OnAwake()
        {
            base.OnAwake();

            menu.labelColor = ColorTag(Color.white);
            menu.rightTextColor = ColorTag(Color.cyan);
            menu.selectedColor = ColorTag(Color.green);
            menu.disabledColor = ColorTag(Color.gray);
            menu.rightColumnWidth = 5;

            // The header is a menu item now, so TextMenu must not write a title.
            menu.menuTitle = string.Empty;
        }

        /// <summary>Draws the page. Called by RasterPropMonitor on every text refresh.</summary>
        public string ShowMenu(int width, int height)
        {
            EnsureSubscribed();

            var player = Player;
            if (player == null)
            {
                return "         == MUSIC LIBRARY ==\n         Not flying a vessel.";
            }

            var state = State;
            if (IsDirty)
            {
                Rebuild(state, player);
                ClearDirty();
            }

            return menu.ShowMenu(width, Math.Max(4, height));
        }

        private void Rebuild(PlayerState state, PlayerManager player)
        {
            menu.Clear();
            sortRow = -1;
            orderRow = -1;
            filterRow = -1;

            if (state == null)
            {
                menu.Add(new TextMenu.Item("          Not flying a vessel.") { isDisabled = true });
                return;
            }

            menu.Add(new TextMenu.Item());
            menu.Add(new TextMenu.Item("         == MUSIC LIBRARY =="));

            if (player.Library.IsScanning)
            {
                menu.Add(new TextMenu.Item("       Scanning music folder...") { isDisabled = true });
                return;
            }

            view = player.Library.View(state.Filter, state.SortField, state.Descending);

            if (view.Count == 0)
            {
                var message = string.IsNullOrEmpty(state.Filter)
                    ? "No music found!"
                    : "Nothing matches the filter.";
                menu.Add(new TextMenu.Item(message) { isDisabled = true });
                AddControls(state);
                return;
            }

            var selection = state.BrowserSelection;
            if (selection < 0 || selection >= view.Count)
            {
                selection = 0;
                state.BrowserSelection = selection;
            }

            for (int i = 0; i < view.Count; i++)
            {
                var index = i;
                var track = view[i];
                var item = new TextMenu.Item(track.ArtistAndTitle, OnPlay, index)
                {
                    rightText = track.DurationText
                };

                // A suspected undecodable track is still selectable: the suspicion is
                // not certain, and a load that fails is handled at play time.
                if (track.Warning != null)
                {
                    item.labelText = track.ArtistAndTitle + "  (" + track.Warning + ")";
                }

                menu.Add(item);
            }

            menu.currentSelection = selection + LeadingItems;

            AddControls(state);
        }

        private void AddControls(PlayerState state)
        {
            menu.Add(new TextMenu.Item());

            var sortLabel = state.SortField + (state.Descending ? " v" : " ^");
            menu.Add(new TextMenu.Item("Sort by: " + sortLabel, OnCycleSort, 0));
            sortRow = menu.Count - 1;

            menu.Add(new TextMenu.Item("Order: " + (state.Descending ? "descending" : "ascending"), OnToggleDirection, 0));
            orderRow = menu.Count - 1;

            var filterLabel = string.IsNullOrEmpty(state.Filter) ? "none" : state.Filter;
            menu.Add(new TextMenu.Item("Filter: " + filterLabel, OnCycleFilter, 0)
            {
                isDisabled = !FilterEnabled()
            });
            filterRow = menu.Count - 1;

            menu.Add(new TextMenu.Item());
            menu.Add(new TextMenu.Item("Rescan music folder", OnRescan, 0));
        }

        protected override void SwitchView()
        {
            GoToPlayerPage();
        }

        protected override void OnButtonPressed(int buttonID)
        {
            var map = Buttons;
            if (map == null)
            {
                return;
            }

            if (Matches(map, MonitorButton.Up, buttonID))
            {
                menu.PreviousItem();
                RememberSelection();
            }
            else if (Matches(map, MonitorButton.Down, buttonID))
            {
                menu.NextItem();
                RememberSelection();
            }
            else if (Matches(map, MonitorButton.Select, buttonID))
            {
                menu.SelectItem();
            }
            else if (Matches(map, MonitorButton.Back, buttonID))
            {
                LeavePages();
            }
            else if (Matches(map, MonitorButton.Left, buttonID))
            {
                StepRow(-1);
            }
            else if (Matches(map, MonitorButton.Right, buttonID))
            {
                StepRow(1);
            }
        }

        /// <summary>
        /// Left and right step backwards or forwards through whichever of the sort,
        /// order and filter rows is under the cursor, so select is not the only way
        /// to reach them. On a song row or on rescan they do nothing, so they never
        /// move between the pages: that is what the next and previous buttons are for.
        /// </summary>
        private void StepRow(int direction)
        {
            var selection = menu.currentSelection;

            if (selection == sortRow)
            {
                CycleSort(direction);
            }
            else if (selection == orderRow)
            {
                SetDirection(direction > 0);
            }
            else if (selection == filterRow && FilterEnabled())
            {
                CycleFilter(direction);
            }
        }

        /// <summary>
        /// The filter row is greyed out when the library is too small for a filter
        /// to mean anything, so left and right have to honour the same rule that
        /// select does. Shared with the row itself so the two cannot drift apart.
        /// </summary>
        private bool FilterEnabled()
        {
            var player = Player;
            return player != null && player.Library.Count >= 2;
        }

        private void RememberSelection()
        {
            var state = State;
            if (state == null)
            {
                return;
            }

            // The songs start after the spacer and the header.
            var songIndex = menu.currentSelection - LeadingItems;
            if (songIndex >= 0 && songIndex < view.Count)
            {
                state.BrowserSelection = songIndex;
            }
        }

        private void OnPlay(int index, TextMenu.Item item)
        {
            var player = Player;
            var state = State;
            if (player == null || state == null)
            {
                return;
            }

            var songIndex = index - LeadingItems;
            if (songIndex < 0 || songIndex >= view.Count)
            {
                return;
            }

            state.BrowserSelection = songIndex;
            player.PlayFromView(view, songIndex);
            MarkDirty();

            // Picking a song takes you to the player; the next and previous buttons
            // then move between the two views.
            GoToPlayerPage();
        }

        private void OnToggleDirection(int index, TextMenu.Item item)
        {
            ToggleDirection();
        }

        private void OnCycleSort(int index, TextMenu.Item item)
        {
            CycleSort(1);
        }

        private void CycleSort(int direction)
        {
            var state = State;
            if (state == null)
            {
                return;
            }

            state.SortField = Step(state.SortField, direction);
            state.BrowserSelection = 0;
            MarkDirty();
        }

        /// <summary>
        /// The next or previous sort field, wrapping around. The order of the
        /// fields is the one the label lists them in, so both directions read
        /// the same way round the cycle.
        /// </summary>
        private static SortField Step(SortField current, int direction)
        {
            var forward = direction > 0;
            switch (current)
            {
                case SortField.Artist: return forward ? SortField.Album : SortField.FileName;
                case SortField.Album: return forward ? SortField.Genre : SortField.Artist;
                case SortField.Genre: return forward ? SortField.Title : SortField.Album;
                case SortField.Title: return forward ? SortField.FileName : SortField.Genre;
                case SortField.FileName: return forward ? SortField.Artist : SortField.Title;
                default: return SortField.Artist;
            }
        }

        private void ToggleDirection()
        {
            var state = State;
            if (state != null)
            {
                state.Descending = !state.Descending;
                MarkDirty();
            }
        }

        /// <summary>
        /// Left sorts ascending and right sorts descending, rather than toggling,
        /// so the two buttons mean the same thing here as they do for the volume.
        /// </summary>
        private void SetDirection(bool descending)
        {
            var state = State;
            if (state != null && state.Descending != descending)
            {
                state.Descending = descending;
                MarkDirty();
            }
        }

        private void OnCycleFilter(int index, TextMenu.Item item)
        {
            CycleFilter(1);
        }

        private void CycleFilter(int direction)
        {
            var state = State;
            var player = Player;
            if (state == null || player == null)
            {
                return;
            }

            state.Filter = StepFilter(state.Filter, player.Library, direction);
            state.BrowserSelection = 0;
            MarkDirty();
        }

        /// <summary>
        /// Steps through the starting letters actually present in the library rather
        /// than a fixed list, so the filter is useful for any collection. A one letter
        /// filter matches the sort field the list is using, which is what a player
        /// expects from it.
        /// </summary>
        private static string StepFilter(string current, MusicLibrary library, int direction)
        {
            var options = FilterOptions(library);
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

        private static List<string> FilterOptions(MusicLibrary library)
        {
            var letters = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var track in library.All)
            {
                var first = FirstLetter(track);
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
        /// The letter the list sorts a track under: its artist when it has one, since
        /// that is the default sort, otherwise the first letter of its title.
        /// </summary>
        private static string FirstLetter(MusicTrack track)
        {
            var source = string.IsNullOrEmpty(track.Artist) ? track.DisplayTitle : track.Artist;
            if (string.IsNullOrEmpty(source))
            {
                return null;
            }

            var trimmed = source.TrimStart();
            if (trimmed.Length == 0 || !char.IsLetter(trimmed[0]))
            {
                return null;
            }

            return trimmed.Substring(0, 1).ToUpperInvariant();
        }

        private void OnRescan(int index, TextMenu.Item item)
        {
            var player = Player;
            if (player != null)
            {
                player.RescanLibrary();
                MarkDirty();
            }
        }
    }
}
