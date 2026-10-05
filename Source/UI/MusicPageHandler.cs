using JSI;
using UnityEngine;

namespace RPMusicPlayer
{
    /// <summary>
    /// Shared plumbing for the music pages: finds the monitor this handler belongs to,
    /// works out which of its buttons does what, and switches between our own pages.
    /// </summary>
    public abstract class MusicPageHandler : InternalModule
    {
        private RasterPropMonitor monitor;
        private ButtonMap buttons;
        private bool dirty = true;
        private PlayerManager subscribed;

        protected RasterPropMonitor Monitor
        {
            get
            {
                if (monitor == null && internalProp != null)
                {
                    monitor = internalProp.FindModelComponent<RasterPropMonitor>();
                    if (monitor != null)
                    {
                        buttons = new ButtonMap(ButtonMap.Split(monitor.globalButtons));

                        // Some pods name their page switching buttons after their
                        // position on the panel, so the pod's own page settings are the
                        // only thing that can say which buttons they are.
                        PageInjector.BindPageSwitchButtons(monitor, buttons);
                    }
                }
                return monitor;
            }
        }

        protected ButtonMap Buttons
        {
            get
            {
                // Touch Monitor first, because that is what builds the map.
                if (Monitor == null)
                {
                    return null;
                }
                return buttons;
            }
        }

        protected PlayerManager Player
        {
            get { return PlayerManager.Instance; }
        }

        /// <summary>The state of the vessel being flown, or null when not flying one.</summary>
        protected PlayerState State
        {
            get
            {
                var manager = Player;
                return manager == null ? null : manager.StateForActiveVessel();
            }
        }

        public override void OnAwake()
        {
            base.OnAwake();
            EnsureSubscribed();
            MarkDirty();
        }

        private void OnDestroy()
        {
            if (subscribed != null)
            {
                subscribed.PlaybackChanged -= OnRedraw;
                subscribed.LibraryChanged -= OnRedraw;
                subscribed = null;
            }
        }

        /// <summary>
        /// Listens for changes that happen while the page is open: the music folder
        /// finishing its scan, a track loading so its duration shows up, or the queue
        /// advancing. Without this the page would sit on whatever it drew first.
        /// </summary>
        protected void EnsureSubscribed()
        {
            var manager = PlayerManager.Instance;
            if (manager == null || manager == subscribed)
            {
                return;
            }

            if (subscribed != null)
            {
                subscribed.PlaybackChanged -= OnRedraw;
                subscribed.LibraryChanged -= OnRedraw;
            }

            manager.PlaybackChanged += OnRedraw;
            manager.LibraryChanged += OnRedraw;
            subscribed = manager;
        }

        private void OnRedraw()
        {
            MarkDirty();
        }

        /// <summary>Forces the next render to rebuild the menu.</summary>
        protected void MarkDirty()
        {
            dirty = true;
        }

        protected bool IsDirty
        {
            get { return dirty; }
        }

        protected void ClearDirty()
        {
            dirty = false;
        }

        /// <summary>Called by RasterPropMonitor when the page becomes visible.</summary>
        public void PageActive(bool active, int pageNumber)
        {
            if (active)
            {
                MarkDirty();
            }
        }

        /// <summary>
        /// Called for every button press on the monitor, whatever page is showing.
        /// </summary>
        public void ClickProcessor(int buttonID)
        {
            var map = Buttons;

            // The next and previous buttons are the way between the two pages,
            // whichever one is showing, so they are handled here rather than in
            // the pages themselves.
            if (Matches(map, MonitorButton.Next, buttonID) || Matches(map, MonitorButton.Prev, buttonID))
            {
                SwitchView();
                return;
            }

            OnButtonPressed(buttonID);
        }

        protected virtual void OnButtonPressed(int buttonID)
        {
        }

        /// <summary>Switches to the other one of the two music pages.</summary>
        protected abstract void SwitchView();

        protected bool Matches(ButtonMap map, MonitorButton action, int buttonID)
        {
            return map != null && buttonID >= 0 && map.IdFor(action) == buttonID;
        }

        /// <summary>Switches the monitor to one of the pages this plugin added.</summary>
        protected void GoToPlayerPage()
        {
            PageInjector.SwitchTo(Monitor, PageInjector.FindPage(Monitor, PageInjector.PlayerPageName));
        }

        protected void GoToBrowserPage()
        {
            PageInjector.SwitchTo(Monitor, PageInjector.FindPage(Monitor, PageInjector.BrowserPageName));
        }

        /// <summary>Back out to whatever page the pod was showing before.</summary>
        protected void LeavePages()
        {
            PageInjector.SwitchTo(Monitor, PageInjector.FindExitPage(Monitor));
        }

        protected static string ColorTag(Color color)
        {
            return JUtil.ColorToColorTag(color);
        }

        protected static string PositionText(AudioEngine audio)
        {
            if (audio == null)
            {
                return "--:--";
            }

            return MusicTrack.FormatDuration(audio.Position) + " / " + MusicTrack.FormatDuration(audio.Duration);
        }
    }
}