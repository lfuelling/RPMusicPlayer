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
        private readonly TextMenu menu = new TextMenu();
        private List<MusicTrack> view = new List<MusicTrack>();

        public override void OnAwake()
        {
            base.OnAwake();

            menu.labelColor = ColorTag(Color.white);
            menu.rightTextColor = ColorTag(Color.cyan);
            menu.selectedColor = ColorTag(Color.green);
            menu.disabledColor = ColorTag(Color.gray);
            menu.rightColumnWidth = 5;
        }

        /// <summary>Draws the page. Called by RasterPropMonitor on every text refresh.</summary>
        public string ShowMenu(int width, int height)
        {
            EnsureSubscribed();

            var player = Player;
            if (player == null)
            {
                return "== MUSIC LIBRARY ==\nNot flying a vessel.";
            }

            var state = State;
            if (IsDirty)
            {
                Rebuild(state, player);
                ClearDirty();
            }

            // One line for the legend and one blank line above it, plus the title that
            // TextMenu writes itself.
            var listHeight = Math.Max(4, height - 3);

            var text = new StringBuilder();
            text.Append(menu.ShowMenu(width, listHeight));

            var legend = BuildLegend();
            if (!string.IsNullOrEmpty(legend))
            {
                text.AppendLine();
                text.Append(legend);
            }

            return text.ToString();
        }

        private void Rebuild(PlayerState state, PlayerManager player)
        {
            menu.Clear();
            menu.menuTitle = "== MUSIC LIBRARY ==";

            if (state == null)
            {
                menu.Add(new TextMenu.Item("Not flying a vessel.") { isDisabled = true });
                return;
            }

            if (player.Library.IsScanning)
            {
                menu.Add(new TextMenu.Item("Scanning music folder...") { isDisabled = true });
                return;
            }

            view = player.Library.View(state.Filter, state.SortField, state.Descending);

            if (view.Count == 0)
            {
                var message = string.IsNullOrEmpty(state.Filter)
                    ? "No music found. Drop files into the music folder and rescan."
                    : "Nothing matches the filter.";
                menu.Add(new TextMenu.Item(message) { isDisabled = true });
                AddControls(state, player);
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
                menu.Add(new TextMenu.Item(view[i].ArtistAndTitle, OnPlay, index)
                {
                    rightText = view[i].DurationText
                });
            }

            menu.currentSelection = selection;

            AddControls(state, player);
        }

        private void AddControls(PlayerState state, PlayerManager player)
        {
            menu.Add(new TextMenu.Item(">> NOW PLAYING >>", OnNowPlaying, 0) { isDisabled = state.Current == null });

            var sortLabel = state.SortField + (state.Descending ? " v" : " ^");
            menu.Add(new TextMenu.Item("Sort by: " + sortLabel, OnCycleSort, 0));

            var filterLabel = string.IsNullOrEmpty(state.Filter) ? "none" : state.Filter;
            menu.Add(new TextMenu.Item("Filter: " + filterLabel, OnCycleFilter, 0)
            {
                isDisabled = player.Library.Count < 20
            });

            menu.Add(new TextMenu.Item("Rescan music folder", OnRescan, 0));
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
                CycleSort();
            }
            else if (Matches(map, MonitorButton.Right, buttonID))
            {
                ToggleDirection();
            }
        }

        private void RememberSelection()
        {
            var state = State;
            if (state != null && menu.currentSelection >= 0 && menu.currentSelection < view.Count)
            {
                state.BrowserSelection = menu.currentSelection;
            }
        }

        private void OnPlay(int index, TextMenu.Item item)
        {
            var player = Player;
            var state = State;
            if (player == null || state == null || index < 0 || index >= view.Count)
            {
                return;
            }

            state.BrowserSelection = index;
            player.PlayFromView(view, index);
            MarkDirty();
        }

        private void OnNowPlaying(int index, TextMenu.Item item)
        {
            GoToPlayerPage();
        }

        private void OnCycleSort(int index, TextMenu.Item item)
        {
            CycleSort();
        }

        private void CycleSort()
        {
            var state = State;
            if (state == null)
            {
                return;
            }

            state.SortField = Next(state.SortField);
            state.BrowserSelection = 0;
            MarkDirty();
        }

        private static SortField Next(SortField current)
        {
            switch (current)
            {
                case SortField.Artist: return SortField.Album;
                case SortField.Album: return SortField.Genre;
                case SortField.Genre: return SortField.Title;
                case SortField.Title: return SortField.FileName;
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

        private void OnCycleFilter(int index, TextMenu.Item item)
        {
            var state = State;
            if (state == null)
            {
                return;
            }

            state.Filter = NextFilter(state.Filter);
            state.BrowserSelection = 0;
            MarkDirty();
        }

        /// <summary>
        /// Cycles through a few one letter starters, which is enough to narrow a large
        /// library down without needing a text entry field on a monitor.
        /// </summary>
        private static string NextFilter(string current)
        {
            var filters = new[] { string.Empty, "a", "e", "i", "o", "u" };
            for (int i = 0; i < filters.Length; i++)
            {
                if (filters[i] == current)
                {
                    return filters[(i + 1) % filters.Length];
                }
            }
            return string.Empty;
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