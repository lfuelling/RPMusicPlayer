using System;
using System.Collections.Generic;

namespace RPMusicPlayer
{
    public enum RepeatMode
    {
        Off,
        One,
        All
    }

    /// <summary>
    /// Everything the player remembers about one vessel: the queue, where playback
    /// got to, and the browser settings that go with it.
    /// </summary>
    public sealed class PlayerState
    {
        internal Guid VesselId;

        /// <summary>Snapshot of the browser view at the moment a song was picked.</summary>
        internal List<MusicTrack> Queue = new List<MusicTrack>();

        internal int QueueIndex = -1;

        /// <summary>Playback position captured when we paused, in seconds.</summary>
        internal double ResumePosition;

        internal RepeatMode Repeat = RepeatMode.Off;
        internal bool Shuffle;

        /// <summary>Whether the next song starts on top of the fading out one.</summary>
        internal bool Crossfade;

        /// <summary>Set by the play/pause button.</summary>
        internal bool UserPaused;

        /// <summary>Set when we paused by ourselves because the context changed.</summary>
        internal bool ContextPaused;

        /// <summary>
        /// Whether returning to this vessel should pick playback up again. EVA is
        /// deliberately excluded: coming back from an EVA leaves the music paused.
        /// </summary>
        internal bool AutoResumeOnReturn;

        // Browser view settings.
        internal string Filter = string.Empty;
        internal SortField SortField = SortField.Artist;
        internal bool Descending;
        internal int BrowserSelection;

        internal PlayerState(Guid vesselId)
        {
            VesselId = vesselId;
        }

        internal MusicTrack Current
        {
            get
            {
                if (QueueIndex < 0 || QueueIndex >= Queue.Count)
                {
                    return null;
                }
                return Queue[QueueIndex];
            }
        }

        internal bool HasQueue
        {
            get { return Queue.Count > 0; }
        }

        internal void CapturePosition(double seconds)
        {
            ResumePosition = seconds;
        }

        internal void ResetPosition()
        {
            ResumePosition = 0;
        }

        /// <summary>Drops the queue, e.g. after the music folder was rescanned.</summary>
        internal void ClearQueue()
        {
            Queue.Clear();
            QueueIndex = -1;
            ResetPosition();
        }

        internal bool IsPaused
        {
            get { return UserPaused || ContextPaused; }
        }
    }
}