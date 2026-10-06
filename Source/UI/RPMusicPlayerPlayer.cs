using System.Collections.Generic;
using System.Text;
using JSI;
using UnityEngine;

namespace RPMusicPlayer
{
    /// <summary>
    /// The player page: what is playing, how far through it is, and the transport
    /// controls.
    ///
    /// The controls are a selectable list rather than a read only readout, so shuffle,
    /// repeat and volume can all be reached with the same up/down/select buttons the
    /// browser uses.
    /// </summary>
    public class RPMusicPlayerPlayer : MusicPageHandler
    {
        private readonly TextMenu menu = new TextMenu();
        private int selection;

        /// <summary>Menu index of the volume row, so left and right can adjust it.</summary>
        private int volumeRow = -1;

        public override void OnAwake()
        {
            base.OnAwake();

            menu.labelColor = ColorTag(Color.white);
            menu.selectedColor = ColorTag(Color.green);
            menu.disabledColor = ColorTag(Color.gray);
            menu.menuTitle = string.Empty;
        }

        /// <summary>Draws the page. Called by RasterPropMonitor on every text refresh.</summary>
        public string ShowMenu(int width, int height)
        {
            EnsureSubscribed();

            var player = Player;
            var state = State;

            if (player == null)
            {
                return "           == NOW PLAYING ==\n\n         Not flying a vessel.";
            }

            if (IsDirty)
            {
                Rebuild(state, player);
                ClearDirty();
            }

            var header = BuildHeader(state, player);

            // Leave a blank line between the track details and the controls.
            var menuHeight = Mathf.Max(3, height - header.Count - 1);

            var text = new StringBuilder();
            foreach (var line in header)
            {
                text.AppendLine(Truncate(line, width));
            }
            text.Append(menu.ShowMenu(width, menuHeight));

            return text.ToString();
        }

        /// <summary>The track details above the controls.</summary>
        private List<string> BuildHeader(PlayerState state, PlayerManager player)
        {
            var lines = new List<string> { "          == NOW PLAYING ==" };

            var current = state == null ? null : state.Current;
            if (current == null)
            {
                lines.Add(string.Empty);
                lines.Add("      Nothing selected.");
                lines.Add("   Open the library to pick a song.");
                return lines;
            }

            lines.Add("Title:  " + current.DisplayTitle);
            lines.Add("Artist: " + Fallback(current.Artist, "unknown"));
            lines.Add("Album:  " + Fallback(current.Album, "unknown"));
            lines.Add("Time:   " + PositionText(player.Audio));
            lines.Add("");

            if (player.Audio.IsLoading)
            {
                lines.Add("Loading...");
            } else
            {
                lines.Add("");
            }
            lines.Add("");

            return lines;
        }

        private void Rebuild(PlayerState state, PlayerManager player)
        {
            menu.Clear();

            if (state == null)
            {
                menu.Add(new TextMenu.Item("         Not flying a vessel.") { isDisabled = true });
                menu.currentSelection = 0;
                return;
            }

            var hasTrack = state.Current != null;

            menu.Add(new TextMenu.Item(PlayPauseLabel(state), (i, item) => player.TogglePause(), 0)
            {
                isDisabled = !hasTrack
            });

            menu.Add(new TextMenu.Item("Next Track", (i, item) => player.Next(), 0)
            {
                isDisabled = !hasTrack
            });

            menu.Add(new TextMenu.Item("Previous Track", (i, item) => player.Previous(), 0)
            {
                isDisabled = !hasTrack
            });

            menu.Add(new TextMenu.Item("Shuffle: " + OnOff(state.Shuffle), (i, item) => player.ToggleShuffle(), 0)
            {
                isSelected = state.Shuffle
            });

            menu.Add(new TextMenu.Item("Crossfade: " + OnOff(state.Crossfade), (i, item) => player.ToggleCrossfade(), 0)
            {
                isSelected = state.Crossfade
            });

            menu.Add(new TextMenu.Item("Repeat:  " + RepeatLabel(state), (i, item) => player.CycleRepeat(), 0)
            {
                isSelected = state.Repeat != RepeatMode.Off
            });

            menu.Add(new TextMenu.Item("Volume:  " + Settings.Current.Volume.ToString("P0"),
                (i, item) => player.AdjustVolume(0.05f), 0));
            volumeRow = menu.Count - 1;

            if (state.HasQueue)
            {
                menu.Add(new TextMenu.Item(
                    "Queue:   " + (state.QueueIndex + 1) + " of " + state.Queue.Count,
                    null, 0) { isDisabled = true });
            }

            menu.currentSelection = Mathf.Clamp(selection, 0, menu.Count - 1);
            selection = menu.currentSelection;
        }

        private static string PlayPauseLabel(PlayerState state)
        {
            if (state.Current == null)
            {
                return "Play";
            }
            if (state.IsPaused)
            {
                return "Play";
            }
            return "Pause";
        }

        private static string OnOff(bool value)
        {
            return value ? "ON" : "off";
        }

        private static string RepeatLabel(PlayerState state)
        {
            switch (state.Repeat)
            {
                case RepeatMode.All: return "ALL";
                case RepeatMode.One: return "ONE";
                default: return "off";
            }
        }

        protected override void SwitchView()
        {
            GoToBrowserPage();
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
                selection = menu.currentSelection;
            }
            else if (Matches(map, MonitorButton.Down, buttonID))
            {
                menu.NextItem();
                selection = menu.currentSelection;
            }
            else if (Matches(map, MonitorButton.Select, buttonID))
            {
                menu.SelectItem();
                MarkDirty();
            }
            else if (Matches(map, MonitorButton.Back, buttonID))
            {
                GoToBrowserPage();
            }
            else if (Matches(map, MonitorButton.Left, buttonID))
            {
                AdjustVolumeIfSelected(-0.05f);
            }
            else if (Matches(map, MonitorButton.Right, buttonID))
            {
                AdjustVolumeIfSelected(0.05f);
            }
        }

        /// <summary>
        /// Left and right change the volume when the volume row is the one under
        /// the cursor. On any other row they do nothing, so they never move
        /// between the pages: that is what the next and previous buttons are for.
        /// </summary>
        private void AdjustVolumeIfSelected(float delta)
        {
            if (menu.currentSelection != volumeRow)
            {
                return;
            }

            var player = Player;
            if (player != null)
            {
                player.AdjustVolume(delta);
                MarkDirty();
            }
        }

        private static string Fallback(string value, string fallback)
        {
            return string.IsNullOrEmpty(value) ? fallback : value;
        }

        private static string Truncate(string text, int width)
        {
            if (width <= 1 || text.Length <= width)
            {
                return text;
            }
            return text.Substring(0, width);
        }
    }
}
