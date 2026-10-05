using System;
using System.Collections.Generic;

namespace RPMusicPlayer
{
    public enum MonitorButton
    {
        None,
        Up,
        Down,
        Left,
        Right,
        Select,
        Back
    }

    /// <summary>
    /// Maps a monitor's own global buttons onto the actions the music pages need.
    ///
    /// RasterPropMonitor IVAs almost all name their global buttons the same way
    /// (button_UP, button_DOWN, button_ENTER, ...) but not all of them, so the button
    /// name is matched against a set of patterns. Anything the player has an opinion
    /// about can be pinned down in the configuration file instead.
    /// </summary>
    public sealed class ButtonMap
    {
        private static readonly Dictionary<MonitorButton, string[]> Patterns = new Dictionary<MonitorButton, string[]>
        {
            { MonitorButton.Up, new[] { "UP" } },
            { MonitorButton.Down, new[] { "DOWN" } },
            { MonitorButton.Left, new[] { "LEFT", "PREV", "BACKWARD" } },
            { MonitorButton.Right, new[] { "RIGHT", "NEXT", "FORWARD" } },
            { MonitorButton.Select, new[] { "ENTER", "SELECT", "FIRE", "OK", "EXEC" } },
            { MonitorButton.Back, new[] { "ESC", "BACK", "CANCEL", "MENU", "HOME", "EXIT" } }
        };

        private readonly Dictionary<MonitorButton, int> ids = new Dictionary<MonitorButton, int>();
        private readonly List<string> globalButtons = new List<string>();

        internal ButtonMap(IEnumerable<string> configuredGlobalButtons)
        {
            if (configuredGlobalButtons == null)
            {
                return;
            }

            foreach (var name in configuredGlobalButtons)
            {
                var trimmed = (name ?? string.Empty).Trim();
                if (trimmed.Length > 0)
                {
                    globalButtons.Add(trimmed);
                }
            }

            BuildDefaults();
        }

        private void BuildDefaults()
        {
            foreach (var action in new[] { MonitorButton.Up, MonitorButton.Down, MonitorButton.Left, MonitorButton.Right, MonitorButton.Select, MonitorButton.Back })
            {
                var id = MatchPatterns(Patterns[action]);
                if (id >= 0)
                {
                    ids[action] = id;
                }
            }
        }

        private int MatchPatterns(string[] patterns)
        {
            foreach (var pattern in patterns)
            {
                // A button may only claim one action, so skip ones already taken.
                for (int i = 0; i < globalButtons.Count; i++)
                {
                    if (ids.ContainsValue(i))
                    {
                        continue;
                    }

                    if (globalButtons[i].IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        /// <summary>The global button id for an action, or -1 when this monitor has no such button.</summary>
        internal int IdFor(MonitorButton action)
        {
            int id;
            return ids.TryGetValue(action, out id) ? id : -1;
        }

        internal void Set(MonitorButton action, int id)
        {
            if (id < 0)
            {
                ids.Remove(action);
            }
            else
            {
                ids[action] = id;
            }
        }

        internal bool Knows(MonitorButton action)
        {
            return ids.ContainsKey(action);
        }

        /// <summary>
        /// The monitor's own name for an action, used to build the on screen legend.
        /// </summary>
        internal string LabelFor(MonitorButton action)
        {
            int id = IdFor(action);
            if (id >= 0 && id < globalButtons.Count)
            {
                return globalButtons[id];
            }
            return null;
        }

        internal string LabelFor(MonitorButton action, string fallback)
        {
            return LabelFor(action) ?? fallback;
        }

        /// <summary>Pins an action to a named global button, e.g. from the configuration file.</summary>
        internal void Bind(MonitorButton action, string buttonName)
        {
            if (string.IsNullOrEmpty(buttonName))
            {
                return;
            }

            for (int i = 0; i < globalButtons.Count; i++)
            {
                if (string.Equals(globalButtons[i], buttonName.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    ids[action] = i;
                    return;
                }
            }
        }

        /// <summary>Splits a monitor's globalButtons config value into individual names.</summary>
        internal static IEnumerable<string> Split(string globalButtons)
        {
            if (string.IsNullOrEmpty(globalButtons))
            {
                yield break;
            }

            foreach (var token in globalButtons.Split(','))
            {
                var trimmed = token.Trim();
                if (trimmed.Length > 0)
                {
                    yield return trimmed;
                }
            }
        }
    }
}