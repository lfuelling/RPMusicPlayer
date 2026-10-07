using System;

namespace RPMusicPlayer
{
    /// <summary>
    /// Everything the player remembers about one vessel: the queue, where playback
    /// got to, and the browser settings that go with it.
    /// </summary>
    public sealed class PlayerState
    {
        internal Guid VesselId;

        /// <summary>
        /// Snapshot of the browser view at the moment a song was picked, plus the
        /// rules for what plays next. The ordering lives in
        /// <see cref="PlaybackQueue"/> rather than here so it can be tested on its own.
        /// </summary>
        internal PlaybackQueue Playback;

        /// <summary>Playback position captured when we paused, in seconds.</summary>
        internal double ResumePosition;

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

        internal PlayerState(Guid vesselId, PlaybackQueue playback)
        {
            VesselId = vesselId;
            Playback = playback;
        }

        internal MusicTrack Current
        {
            get { return Playback.Current; }
        }

        internal bool HasQueue
        {
            get { return Playback.HasQueue; }
        }

        internal RepeatMode Repeat
        {
            get { return Playback.Repeat; }
            set { Playback.Repeat = value; }
        }

        internal bool Shuffle
        {
            get { return Playback.Shuffle; }
        }

        internal bool Crossfade;

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
            Playback.Clear();
            ResetPosition();
        }

        internal bool IsPaused
        {
            get { return UserPaused || ContextPaused; }
        }
    }
}
