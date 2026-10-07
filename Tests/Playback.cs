using System;
using System.Collections.Generic;
using System.Linq;

using RPMusicPlayer;

namespace RPMusicPlayer.Tests
{
    /// <summary>
    /// Checks for the queue ordering rules: what plays next, and in what order
    /// when shuffle is on.
    ///
    /// <see cref="PlaybackQueue"/> takes its randomness as an argument, so these
    /// run against a seeded generator and assert on exact outcomes rather than
    /// hoping a random draw happens to look right.
    /// </summary>
    internal static class Playback
    {
        private const int Seed = 20240101;

        // The shared check helpers live on Program; these keep the calls in this
        // file reading like the rest of the suite.
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

        /// <summary>A fixed sequence so a failing check always fails the same way.</summary>
        private sealed class ScriptedRandom
        {
            private readonly int[] script;
            private int position;

            internal ScriptedRandom(params int[] script)
            {
                this.script = script;
            }

            internal int Next(int minInclusive, int maxExclusive)
            {
                var value = position < script.Length ? script[position] : minInclusive;
                position++;
                return Math.Max(minInclusive, Math.Min(maxExclusive - 1, value));
            }

            /// <summary>
            /// Always picks the first candidate. In a Fisher-Yates pass that walks
            /// the tail right to left this produces a fixed, clearly non alphabetical
            /// order, which is what the deterministic checks below assert on.
            /// </summary>
            internal static int AlwaysFirst(int minInclusive, int maxExclusive)
            {
                return minInclusive;
            }
        }

        private static PlaybackQueue Queue(Func<int, int, int> random)
        {
            return new PlaybackQueue(random);
        }

        private static PlaybackQueue Queue()
        {
            return new PlaybackQueue(new Random(Seed).Next);
        }

        /// <summary>Songs named A, B, C ... so orderings read as sequences.</summary>
        private static List<MusicTrack> Songs(int count)
        {
            var songs = new List<MusicTrack>();
            for (int i = 0; i < count; i++)
            {
                songs.Add(new MusicTrack("song" + (char)('A' + i) + ".wav", new TrackTags()));
            }
            return songs;
        }

        /// <summary>The letter of each song, from the current one onwards.</summary>
        private static string Order(PlaybackQueue queue)
        {
            return string.Join("", queue.Tracks.Skip(queue.Index).Select(Letter));
        }

        /// <summary>The letter of every song in the queue, current one included.</summary>
        private static string FullOrder(PlaybackQueue queue)
        {
            return string.Join("", queue.Tracks.Select(Letter));
        }

        private static string Letter(MusicTrack track)
        {
            return track.FileName.Substring(4, 1);
        }

        // ------------------------------------------------------------------ order

        private static void QueueOrderIsTheViewOrder()
        {
            var queue = Queue();
            queue.Load(Songs(5), 2);

            Equal("C", queue.Current.FileName.Substring(4, 1), "queue: picked song is current");
            True(queue.HasQueue, "queue: has a queue after loading a view");
            Equal("ABCDE", FullOrder(queue), "queue: the view order is kept when shuffle is off");
            Equal("CDE", Order(queue), "queue: playback starts at the picked song");
        }

        private static void AdvanceWalksTheViewInOrder()
        {
            var queue = Queue();
            queue.Load(Songs(4), 0);

            var played = new List<string>();
            played.Add(queue.Current.FileName.Substring(4, 1));

            for (int i = 0; i < 3; i++)
            {
                True(queue.TryAdvance(), "advance: queue continues");
                played.Add(queue.Current.FileName.Substring(4, 1));
            }

            Equal("ABCD", string.Join("", played), "advance: plays the view in order");
            False(queue.TryAdvance(), "advance: stops at the end with repeat off");
        }

        private static void RepeatOffStopsAtTheEnd()
        {
            var queue = Queue();
            queue.Load(Songs(3), 0);

            queue.TryAdvance();
            queue.TryAdvance();
            True(queue.Index == 2, "repeat off: last song is still selected when the queue ends");
            False(queue.TryAdvance(), "repeat off: no song after the last one");
        }

        private static void RepeatAllWrapsAround()
        {
            var queue = Queue();
            queue.Load(Songs(3), 2);
            queue.Repeat = RepeatMode.All;

            True(queue.TryAdvance(), "repeat all: continues past the last song");
            Equal("A", queue.Current.FileName.Substring(4, 1), "repeat all: wraps to the first song");
        }

        private static void RepeatOneStaysPut()
        {
            var queue = Queue();
            queue.Load(Songs(4), 2);
            queue.Repeat = RepeatMode.One;

            True(queue.TryAdvance(), "repeat one: plays again");
            Equal("C", queue.Current.FileName.Substring(4, 1), "repeat one: repeats the same song");
        }

        private static void StepMovesBothWays()
        {
            var queue = Queue();
            queue.Load(Songs(4), 1);

            True(queue.TryStep(1), "step: next");
            Equal("C", queue.Current.FileName.Substring(4, 1), "step: moves forward");

            True(queue.TryStep(-1), "step: previous");
            Equal("B", queue.Current.FileName.Substring(4, 1), "step: moves back");

            True(queue.TryStep(-1), "step: previous at the start");
            Equal("A", queue.Current.FileName.Substring(4, 1), "step: previous stops at the first song");
        }

        private static void StepAtTheEndRespectsRepeat()
        {
            var queue = Queue();
            queue.Load(Songs(3), 2);

            False(queue.TryStep(1), "step: no next song with repeat off");
            Equal("C", queue.Current.FileName.Substring(4, 1), "step: a refused step leaves the song alone");

            queue.Repeat = RepeatMode.All;
            True(queue.TryStep(1), "step: next with repeat all");
            Equal("A", queue.Current.FileName.Substring(4, 1), "step: repeat all wraps to the first song");
        }

        // ---------------------------------------------------------------- shuffle

        /// <summary>
        /// The reported bug: shuffle was on, yet songs came out alphabetically.
        /// This pins the two halves of that - the flag being honoured when the queue
        /// is built, and the tail genuinely being reordered.
        /// </summary>
        private static void ShuffleReordersWhatIsLeftToPlay()
        {
            // A generator that always takes the first candidate, which moves every
            // song in the tail. A queue that ignored shuffle would keep ABCDEFGH
            // and fail this.
            var queue = Queue(ScriptedRandom.AlwaysFirst);
            queue.Load(Songs(8), 0);
            queue.ToggleShuffle();

            Equal("ACDEFGHB", Order(queue), "shuffle: unplayed songs come out reordered, not alphabetical");
        }

        private static void ShuffleOnLoadAppliesToTheNewQueue()
        {
            // Shuffle already on when a song is picked: the queue has to be shuffled
            // as it is built, not only when the setting is toggled afterwards.
            var queue = Queue(ScriptedRandom.AlwaysFirst);
            queue.ToggleShuffle();
            queue.Load(Songs(8), 0);

            Equal("ACDEFGHB", Order(queue), "shuffle: applies when a song is picked with shuffle already on");
        }

        private static void ShuffleKeepsTheCurrentSongInPlace()
        {
            var queue = Queue(ScriptedRandom.AlwaysFirst);
            queue.Load(Songs(6), 2);
            queue.ToggleShuffle();

            // A B C D E F, picked C. Only D, E and F are free to move, and the
            // pass swaps F then E past D, giving C E F D.
            Equal("C", Letter(queue.Current), "shuffle: the song that is playing does not move");
            Equal("CEFD", Order(queue), "shuffle: the tail is reordered after the current song");
        }

        private static void ShuffleOffLeavesTheOrderAlone()
        {
            var queue = Queue(ScriptedRandom.AlwaysFirst);
            queue.Load(Songs(5), 0);
            queue.ToggleShuffle();
            var shuffled = Order(queue);

            False(queue.ToggleShuffle(), "shuffle: toggling off reports off");
            Equal(shuffled, Order(queue), "shuffle: turning it off does not reorder the queue");
        }

        private static void ShuffleFlagRoundTrips()
        {
            var queue = Queue();
            True(queue.ToggleShuffle(), "shuffle: toggling on reports on");
            False(queue.ToggleShuffle(), "shuffle: toggling again reports off");
        }

        private static void ShuffleIsAFairPermutationOfTheTail()
        {
            const int trials = 4000;
            const int tail = 6;

            var random = new Random(Seed);
            var orders = new HashSet<string>();

            // slotCounts[slot][song] counts how often a song landed in a tail slot.
            var slotCounts = new int[tail, tail];
            var songCounts = new int[tail];

            for (int trial = 0; trial < trials; trial++)
            {
                var queue = Queue(random.Next);
                queue.Load(Songs(tail + 1), 0);
                queue.ToggleShuffle();

                var order = Order(queue);
                orders.Add(order);

                // The current song is fixed, so the rest is the permutation.
                for (int slot = 0; slot < tail; slot++)
                {
                    var song = order[slot + 1] - 'B';
                    slotCounts[slot, song]++;
                    songCounts[song]++;
                }
            }

            // There are 720 orderings of six songs; most should show up over 4000 draws.
            True(orders.Count > 600,
                $"shuffle: produces many different orders (saw {orders.Count} of 720 possible)");

            // Every song should reach every slot about equally often.
            for (int slot = 0; slot < tail; slot++)
            {
                for (int song = 0; song < tail; song++)
                {
                    var share = slotCounts[slot, song] / (double)songCounts[song];
                    True(share > 0.10 && share < 0.24,
                        $"shuffle: song {(char)('B' + song)} reaches slot {slot} evenly (share {share:P0})");
                }
            }
        }

        private static void ShuffleKeepsEverySongExactlyOnce()
        {
            var random = new Random(Seed);
            for (int trial = 0; trial < 500; trial++)
            {
                var queue = Queue(random.Next);
                queue.Load(Songs(9), 4);
                queue.ToggleShuffle();

                var order = FullOrder(queue);
                Equal("9", order.Length.ToString(), "shuffle: no song is lost");
                Equal("9", order.Distinct().Count().ToString(), "shuffle: no song is duplicated");
                Equal("E", order.Substring(4, 1), "shuffle: the picked song stays where it was picked");
            }
        }

        private static void ShuffleOnAnEmptyQueueIsHarmless()
        {
            var queue = Queue(ScriptedRandom.AlwaysFirst);
            True(queue.ToggleShuffle(), "shuffle: turning it on with nothing queued works");
            queue.Load(Songs(3), 0);
            True(queue.Current != null, "shuffle: the first pick still plays");

            // The picked song is the one already playing, so it stays put and the
            // two after it are the ones that get shuffled.
            Equal("A", Letter(queue.Current), "shuffle: the first pick stays where it was picked");
            Equal("CB", Order(queue).Substring(1), "shuffle: the songs after the first pick are shuffled");
        }

        // -------------------------------------------------------------- crossfade

        private static void CrossfadeNeedsSomewhereToGo()
        {
            var queue = Queue();
            queue.Load(Songs(3), 2);
            False(queue.CanCrossfade(), "crossfade: the last song of the queue is not crossfaded");

            queue.Load(Songs(3), 1);
            True(queue.CanCrossfade(), "crossfade: a song with a successor can crossfade");
        }

        private static void CrossfadeNeverRepeatsOneSong()
        {
            var queue = Queue();
            queue.Load(Songs(3), 1);
            queue.Repeat = RepeatMode.One;

            False(queue.CanCrossfade(), "crossfade: repeat one restarts cleanly instead of crossfading");
        }

        private static void CrossfadeWrapsWithRepeatAll()
        {
            var queue = Queue();
            queue.Load(Songs(3), 2);
            queue.Repeat = RepeatMode.All;

            True(queue.CanCrossfade(), "crossfade: repeat all crossfades from the last song back to the first");
        }

        private static void CrossfadeLeavesAOneSongQueueAlone()
        {
            var queue = Queue();
            queue.Load(Songs(1), 0);

            False(queue.CanCrossfade(), "crossfade: a queue of one has nothing to fade to");
            False(queue.CanCrossfade(), "crossfade: still nothing to fade to on a second look");
        }

        // ------------------------------------------------- shuffle plus crossfade

        /// <summary>
        /// The two settings were reported together: crossfade on, shuffle on, and
        /// the queue never got past the song that was picked. This walks the whole
        /// queue the way the player does - crossfading on every hand over - and
        /// checks every song is reached exactly once, in a non alphabetical order.
        /// </summary>
        private static void ShuffleAndCrossfadeTogetherPlayEverySong()
        {
            var random = new Random(Seed);

            for (int trial = 0; trial < 200; trial++)
            {
                var queue = Queue(random.Next);
                queue.Load(Songs(8), 0);
                queue.ToggleShuffle();

                var played = new List<string> { Letter(queue.Current) };
                while (queue.CanCrossfade())
                {
                    True(queue.TryAdvance(), "shuffle + crossfade: the queue keeps going");
                    played.Add(Letter(queue.Current));
                }

                var order = string.Join("", played);

                // Reaching the end of the queue is the stop condition, so the song
                // picked last is the one the final crossfade led to.
                Equal("8", played.Count.ToString(), "shuffle + crossfade: every song is reached");
                Equal("8", played.Distinct().Count().ToString(),
                    "shuffle + crossfade: no song plays twice");
                False(order == "ABCDEFGH",
                    "shuffle + crossfade: the order is not the alphabetical one");
            }
        }

        /// <summary>
        /// A crossfade must never move the queue on without a song to move to, and
        /// must never skip one either. The song after the current one is always the
        /// one that plays next.
        /// </summary>
        private static void CrossfadeMovesExactlyOneSong()
        {
            var queue = Queue();
            queue.Load(Songs(5), 1);

            var before = Letter(queue.Current);
            queue.TryAdvance();
            Equal("C", Letter(queue.Current), "crossfade: moves on by exactly one song");
            True(before != Letter(queue.Current), "crossfade: the queue actually moved");
        }

        /// <summary>
        /// With repeat on, a crossfade from the last song wraps to the first. If it
        /// did not, the queue would stall on the final song with nothing to fade to.
        /// </summary>
        private static void CrossfadeWrapsWithoutLosingASong()
        {
            var queue = Queue();
            queue.Load(Songs(4), 3);
            queue.Repeat = RepeatMode.All;

            True(queue.CanCrossfade(), "crossfade: repeat all can crossfade off the end");
            queue.TryAdvance();
            Equal("A", Letter(queue.Current), "crossfade: repeat all wraps to the first song");
        }

        // ------------------------------------------------------------------ edges

        private static void EmptyQueueIsSafe()
        {
            var queue = Queue();

            False(queue.HasQueue, "empty queue: reports no queue");
            True(queue.Current == null, "empty queue: has no current song");
            False(queue.TryAdvance(), "empty queue: cannot advance");
            False(queue.TryStep(1), "empty queue: cannot step");
            False(queue.CanCrossfade(), "empty queue: cannot crossfade");
        }

        private static void LoadingAViewReplacesTheQueue()
        {
            var queue = Queue();
            queue.Load(Songs(5), 4);
            queue.Load(Songs(3), 1);

            Equal("3", queue.Count.ToString(), "load: a new view replaces the old queue");
            Equal("B", Letter(queue.Current), "load: points at the newly picked song");
        }

        private static void OutOfRangePickIsIgnored()
        {
            var queue = Queue();
            queue.Load(Songs(3), 99);

            True(queue.Current == null, "load: a pick outside the view selects nothing");
        }

        private static void ClearEmptiesTheQueue()
        {
            var queue = Queue();
            queue.Load(Songs(4), 1);
            queue.Clear();

            False(queue.HasQueue, "clear: drops the queue");
            True(queue.Current == null, "clear: has no current song");
        }

        internal static void Run()
        {
            QueueOrderIsTheViewOrder();
            AdvanceWalksTheViewInOrder();
            RepeatOffStopsAtTheEnd();
            RepeatAllWrapsAround();
            RepeatOneStaysPut();
            StepMovesBothWays();
            StepAtTheEndRespectsRepeat();

            ShuffleReordersWhatIsLeftToPlay();
            ShuffleOnLoadAppliesToTheNewQueue();
            ShuffleKeepsTheCurrentSongInPlace();
            ShuffleOffLeavesTheOrderAlone();
            ShuffleFlagRoundTrips();
            ShuffleIsAFairPermutationOfTheTail();
            ShuffleKeepsEverySongExactlyOnce();
            ShuffleOnAnEmptyQueueIsHarmless();

            CrossfadeNeedsSomewhereToGo();
            CrossfadeNeverRepeatsOneSong();
            CrossfadeWrapsWithRepeatAll();
            CrossfadeLeavesAOneSongQueueAlone();

            ShuffleAndCrossfadeTogetherPlayEverySong();
            CrossfadeMovesExactlyOneSong();
            CrossfadeWrapsWithoutLosingASong();

            EmptyQueueIsSafe();
            LoadingAViewReplacesTheQueue();
            OutOfRangePickIsIgnored();
            ClearEmptiesTheQueue();
        }
    }
}
