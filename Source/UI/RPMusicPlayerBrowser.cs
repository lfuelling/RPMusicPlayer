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
        /// Survives the rebuilds, so changing a setting leaves the cursor on the
        /// row that was changed instead of throwing it back to the top of the list.
        /// </summary>
        private readonly MenuCursor cursor = new MenuCursor();

        /// <summary>
        /// Menu indices of the rows left and right can step through, so those
        /// buttons act on whichever of them is under the cursor. -1 when the row
        /// is not on the page, which stops a stale index from firing.
        /// </summary>
        private int sortRow = -1;
        private int orderRow = -1;
        private int filterFieldRow = -1;
        private int filterRow = -1;

        /// <summary>
        /// Names for the setting rows, so the cursor can follow the one the player
        /// is on when the song list above them changes length.
        ///
        /// Held as a field and refilled in place rather than built per rebuild:
        /// Rebuild throws away a whole menu full of TextMenu.Items anyway, so there
        /// is no reason to add a list on top of that.
        /// </summary>
        private readonly KeyValuePair<int, int>[] namedRows = new KeyValuePair<int, int>[4];

        private enum Row
        {
            Sort = 1,
            Order,
            FilterField,
            Filter
        }

        private void FillNamedRows()
        {
            namedRows[0] = new KeyValuePair<int, int>((int)Row.Sort, sortRow);
            namedRows[1] = new KeyValuePair<int, int>((int)Row.Order, orderRow);
            namedRows[2] = new KeyValuePair<int, int>((int)Row.FilterField, filterFieldRow);
            namedRows[3] = new KeyValuePair<int, int>((int)Row.Filter, filterRow);
        }

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
            filterFieldRow = -1;
            filterRow = -1;

            if (state == null)
            {
                menu.Add(new TextMenu.Item("          Not flying a vessel.") { isDisabled = true });
                RestoreCursor();
                return;
            }

            menu.Add(new TextMenu.Item());
            menu.Add(new TextMenu.Item("         == MUSIC LIBRARY =="));

            if (player.Library.IsScanning)
            {
                menu.Add(new TextMenu.Item("       Scanning music folder...") { isDisabled = true });
                RestoreCursor();
                return;
            }

            view = player.Library.View(state.Filter, state.FilterField, state.SortField, state.Descending);

            if (view.Count == 0)
            {
                var message = string.IsNullOrEmpty(state.Filter)
                    ? "No music found!"
                    : "Nothing matches the filter.";
                menu.Add(new TextMenu.Item(message) { isDisabled = true });
                AddControls(state);
                RestoreCursor();
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

            AddControls(state);
            RestoreCursor();
        }

        /// <summary>
        /// Puts the cursor back where the player left it. The setting rows sit
        /// below the songs, so they move when the list changes length; naming
        /// them is what keeps the cursor on the sort, order or filter row rather
        /// than on whatever row now occupies that position.
        /// </summary>
        private void RestoreCursor()
        {
            if (cursor.Row < 0)
            {
                // First time round, or after a reset: start on the first song.
                cursor.MoveTo(LeadingItems + ClampSelection());
            }

            FillNamedRows();
            menu.currentSelection = cursor.Restore(menu.Count, namedRows);
        }

        /// <summary>
        /// Records where the cursor is, naming it when it is on one of the setting
        /// rows so the cursor is found again on the right row after a rebuild.
        /// </summary>
        private void RememberCursorRow()
        {
            var selection = menu.currentSelection;

            if (selection == sortRow)
            {
                cursor.MoveTo(selection, (int)Row.Sort);
            }
            else if (selection == orderRow)
            {
                cursor.MoveTo(selection, (int)Row.Order);
            }
            else if (selection == filterFieldRow)
            {
                cursor.MoveTo(selection, (int)Row.FilterField);
            }
            else if (selection == filterRow)
            {
                cursor.MoveTo(selection, (int)Row.Filter);
            }
            else
            {
                cursor.MoveTo(selection);
            }
        }

        private int ClampSelection()
        {
            var state = State;
            var selection = state == null ? 0 : state.BrowserSelection;

            if (view.Count == 0)
            {
                return 0;
            }
            return selection < 0 || selection >= view.Count ? 0 : selection;
        }

        private void AddControls(PlayerState state)
        {
            menu.Add(new TextMenu.Item());

            var sortLabel = state.SortField + (state.Descending ? " v" : " ^");
            menu.Add(new TextMenu.Item("Sort by: " + sortLabel, OnCycleSort, 0));
            sortRow = menu.Count - 1;

            menu.Add(new TextMenu.Item("Order: " + (state.Descending ? "descending" : "ascending"), OnToggleDirection, 0));
            orderRow = menu.Count - 1;

            var filterEnabled = FilterEnabled();

            // Which tag the filter reads. Without this the filter can only ever
            // look at the one the list happens to be sorted by.
            menu.Add(new TextMenu.Item("Filter by: " + state.FilterField, OnCycleFilterField, 0)
            {
                isDisabled = !filterEnabled
            });
            filterFieldRow = menu.Count - 1;

            var filterLabel = string.IsNullOrEmpty(state.Filter) ? "none" : state.Filter;
            menu.Add(new TextMenu.Item("Filter: " + filterLabel, OnCycleFilter, 0)
            {
                isDisabled = !filterEnabled
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
                // Activating a setting row rebuilds the menu, so the cursor is
                // recorded before the callback rather than after.
                RememberCursorRow();
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
        /// order, filter by and filter rows is under the cursor, so select is not the
        /// only way to reach them. On a song row or on rescan they do nothing, so they
        /// never move between the pages: that is what the next and previous buttons
        /// are for.
        /// </summary>
        private void StepRow(int direction)
        {
            var selection = menu.currentSelection;

            // Left and right on a setting row change it and rebuild, so the cursor
            // has to be recorded before the rebuild throws it away.
            if (selection == sortRow || selection == orderRow
                || selection == filterRow || selection == filterFieldRow)
            {
                RememberCursorRow();
            }

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
            else if (selection == filterFieldRow && FilterEnabled())
            {
                CycleFilterField(direction);
            }
        }

        /// <summary>
        /// The filter row is greyed out along with the row above it when the library
        /// is too small for a filter to mean anything, so left and right have to
        /// honour the same rule that select does. Shared with the rows themselves so
        /// the two cannot drift apart.
        /// </summary>
        private bool FilterEnabled()
        {
            var player = Player;
            return player != null && player.Library.Count >= 2;
        }

        private void RememberSelection()
        {
            var state = State;

            // The cursor row is remembered whether or not it is a song row, so
            // moving off the songs and back does not lose the place. A setting row
            // is remembered by name, so it is found again after a rebuild.
            RememberCursorRow();

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

            // The song selection is deliberately left alone: the cursor stays on
            // the sort row, so resetting the highlighted song would only move the
            // highlight out from under the player for no reason.
            state.SortField = Step(state.SortField, direction);
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

            // As with the sort field, the highlighted song stays put.
            var options = LibraryQuery.FilterOptions(player.Library.All, state.FilterField);
            state.Filter = LibraryQuery.StepFilter(state.Filter, options, direction);
            MarkDirty();
        }

        private void OnCycleFilterField(int index, TextMenu.Item item)
        {
            CycleFilterField(1);
        }

        /// <summary>
        /// Changes which tag the filter reads. The letters offered follow the new
        /// tag, and a letter that only existed for the old one is dropped rather
        /// than left on, filtering nothing.
        /// </summary>
        private void CycleFilterField(int direction)
        {
            var state = State;
            var player = Player;
            if (state == null || player == null)
            {
                return;
            }

            state.FilterField = StepFilterField(state.FilterField, direction);

            var options = LibraryQuery.FilterOptions(player.Library.All, state.FilterField);
            state.Filter = LibraryQuery.StepFilter(state.Filter, options, 0);
            MarkDirty();
        }

        /// <summary>
        /// The next or previous tag to filter on, wrapping around. File name is left
        /// out: a filter over file names is the same as searching the library, and
        /// the letters it offers are rarely what anyone wants to pick from.
        /// </summary>
        private static SortField StepFilterField(SortField current, int direction)
        {
            var fields = new[]
            {
                SortField.Artist, SortField.Album, SortField.Genre, SortField.Title
            };

            var index = Array.IndexOf(fields, current);
            if (index < 0)
            {
                return fields[0];
            }

            return fields[(index + direction + fields.Length) % fields.Length];
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
