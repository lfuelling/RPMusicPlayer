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
    /// The songs queued up behind the one that is playing, and the rule for what
    /// comes next.
    ///
    /// This holds no Unity types and never touches the audio engine: it is pure
    /// bookkeeping, so the ordering rules (shuffle, repeat, end of queue) can be
    /// exercised without the game. <see cref="PlayerManager"/> owns one per vessel
    /// and is the only thing that turns these decisions into sound.
    /// </summary>
    public sealed class PlaybackQueue
    {
        /// <summary>
        /// Uniform random integer in [minInclusive, maxExclusive), injected so tests
        /// can make the shuffle deterministic.
        /// </summary>
        private readonly Func<int, int, int> randomRange;

        private readonly List<MusicTrack> tracks = new List<MusicTrack>();

        /// <summary>Where in <see cref="Tracks"/> playback currently is, -1 when nothing is picked.</summary>
        internal int Index = -1;

        internal RepeatMode Repeat = RepeatMode.Off;
        internal bool Shuffle;

        internal PlaybackQueue(Func<int, int, int> randomRange)
        {
            if (randomRange == null)
            {
                throw new ArgumentNullException("randomRange");
            }
            this.randomRange = randomRange;
        }

        internal int Count
        {
            get { return tracks.Count; }
        }

        internal MusicTrack Current
        {
            get
            {
                if (Index < 0 || Index >= tracks.Count)
                {
                    return null;
                }
                return tracks[Index];
            }
        }

        internal bool HasQueue
        {
            get { return tracks.Count > 0; }
        }

        /// <summary>
        /// The queued songs, in the order they will play. Read only: the queue is
        /// only ever reordered through <see cref="ShuffleRemaining"/>.
        /// </summary>
        internal IList<MusicTrack> Tracks
        {
            get { return tracks.AsReadOnly(); }
        }

        /// <summary>
        /// Takes a snapshot of the browser view as the new queue and points at the
        /// song the player picked. Songs that have not played yet are shuffled when
        /// shuffle is on; everything from the picked song onwards keeps the order
        /// the user is looking at, with the tail randomised.
        /// </summary>
        internal void Load(IEnumerable<MusicTrack> view, int index)
        {
            tracks.Clear();

            if (view != null)
            {
                foreach (var track in view)
                {
                    if (track != null)
                    {
                        tracks.Add(track);
                    }
                }
            }

            Index = index >= 0 && index < tracks.Count ? index : -1;

            if (Shuffle)
            {
                ShuffleRemaining();
            }
        }

        /// <summary>
        /// What should play when the current song ends: the next one, the same one
        /// again for repeat one, or the start of the queue for repeat all. Returns
        /// false when the queue has run out and playback should stop.
        /// </summary>
        internal bool TryAdvance()
        {
            if (!HasQueue)
            {
                return false;
            }

            if (Repeat == RepeatMode.One)
            {
                return true;
            }

            var next = Index + 1;
            if (next >= tracks.Count)
            {
                if (Repeat != RepeatMode.All)
                {
                    return false;
                }
                next = 0;
            }

            Index = next;
            return true;
        }

        /// <summary>
        /// What should play for the next and previous buttons. Direction is +1 or -1.
        /// Repeat one stays where it is rather than skipping, which is what pressing
        /// next on a repeating song is expected to do.
        /// </summary>
        internal bool TryStep(int direction)
        {
            if (!HasQueue)
            {
                return false;
            }

            var next = Index + direction;

            if (next >= tracks.Count)
            {
                if (Repeat == RepeatMode.One)
                {
                    return true;
                }
                if (Repeat != RepeatMode.All)
                {
                    return false;
                }
                next = 0;
            }
            else if (next < 0)
            {
                next = Repeat == RepeatMode.All ? tracks.Count - 1 : 0;
            }

            Index = next;
            return true;
        }

        /// <summary>
        /// Whether a crossfade has somewhere to go. Repeating a single song is
        /// excluded on purpose: it has to restart cleanly rather than fade into
        /// itself, and the end of the queue is left to finish normally.
        /// </summary>
        internal bool CanCrossfade()
        {
            if (!HasQueue || Repeat == RepeatMode.One)
            {
                return false;
            }

            return Index + 1 < tracks.Count || Repeat == RepeatMode.All;
        }

        /// <summary>
        /// Flips shuffle. Turning it on randomises the songs still to come, leaving
        /// the current one where it is so playback is not interrupted; turning it
        /// off leaves the order alone rather than re-sorting mid queue.
        /// </summary>
        internal bool ToggleShuffle()
        {
            Shuffle = !Shuffle;
            if (Shuffle)
            {
                ShuffleRemaining();
            }
            return Shuffle;
        }

        /// <summary>
        /// Fisher-Yates over the songs after the current one. The current track and
        /// anything before it are left alone, so the song that is sounding does not
        /// jump and the queue still starts where the player expects it to.
        /// </summary>
        internal void ShuffleRemaining()
        {
            var first = Index + 1;
            if (first < 0)
            {
                first = 0;
            }

            for (int i = tracks.Count - 1; i > first; i--)
            {
                var j = randomRange(first, i + 1);

                var swap = tracks[i];
                tracks[i] = tracks[j];
                tracks[j] = swap;
            }
        }

        /// <summary>Drops the queue, e.g. after the music folder was rescanned.</summary>
        internal void Clear()
        {
            tracks.Clear();
            Index = -1;
        }
    }
}
