using System;
using System.Collections.Generic;

using RPMusicPlayer;

namespace RPMusicPlayer.Tests
{
    /// <summary>
    /// Checks that the library cursor stays where the player put it.
    ///
    /// The browser page rebuilds its menu whenever a setting changes, and a rebuild
    /// used to reset the cursor to the top of the song list. <see cref="MenuCursor"/>
    /// is the piece that carries the row across those rebuilds, so it is checked
    /// here against the menu shapes the browser actually builds.
    /// </summary>
    internal static class Cursor
    {
        /// <summary>The spacer and the header the browser puts above the songs.</summary>
        private const int LeadingItems = 2;

        /// <summary>Mirrors the browser's names for its setting rows.</summary>
        private const int Sort = 1;
        private const int Order = 2;
        private const int Filter = 3;

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

        /// <summary>
        /// The row layout Rebuild builds: two leading items, the songs, a spacer,
        /// then sort, order, filter, a spacer and rescan. The returned array is the
        /// item count followed by the sort, order and filter rows.
        /// </summary>
        private static int[] Rows(int songCount)
        {
            var count = LeadingItems + songCount + 6;
            var sort = LeadingItems + songCount + 1;
            return new[] { count, sort, sort + 1, sort + 2 };
        }

        private static KeyValuePair<int, int>[] Names(int[] rows)
        {
            return new[]
            {
                new KeyValuePair<int, int>(Sort, rows[1]),
                new KeyValuePair<int, int>(Order, rows[2]),
                new KeyValuePair<int, int>(Filter, rows[3])
            };
        }

        private static void FirstBuildStartsOnTheFirstSong()
        {
            var rows = Rows(10);
            var cursor = new MenuCursor();

            cursor.MoveTo(LeadingItems);
            Equal("2", cursor.Restore(rows[0], Names(rows)).ToString(), "cursor: starts on the first song");
        }

        private static void CursorSurvivesARebuild()
        {
            var rows = Rows(10);
            var cursor = new MenuCursor();

            cursor.MoveTo(rows[1], Sort);
            Equal(rows[1].ToString(), cursor.Restore(rows[0], Names(rows)).ToString(),
                "cursor: the sort row is still the sort row after a rebuild");

            cursor.MoveTo(rows[2], Order);
            Equal(rows[2].ToString(), cursor.Restore(rows[0], Names(rows)).ToString(),
                "cursor: the order row survives a rebuild");

            cursor.MoveTo(rows[3], Filter);
            Equal(rows[3].ToString(), cursor.Restore(rows[0], Names(rows)).ToString(),
                "cursor: the filter row survives a rebuild");
        }

        /// <summary>
        /// The reported bug: changing the sort, the order or the filter rebuilt the
        /// menu and threw the cursor back to the top of the song list.
        ///
        /// The second half of this is the subtle one. Those rows sit below the
        /// songs, so they move when the list changes length. Tracking a plain menu
        /// index and clamping it lands on whatever row happens to occupy that
        /// index now - which is how "sort" turned into "filter" or "rescan".
        /// </summary>
        private static void SettingRowsFollowTheirOwnRow()
        {
            var pairs = new[]
            {
                new { Name = "sort", Id = Sort },
                new { Name = "order", Id = Order },
                new { Name = "filter", Id = Filter }
            };

            foreach (var pair in pairs)
            {
                var before = Rows(10);
                var after = Rows(3);

                var cursor = new MenuCursor();
                var start = pair.Id == Sort ? before[1] : pair.Id == Order ? before[2] : before[3];
                cursor.MoveTo(start, pair.Id);

                var restored = cursor.Restore(after[0], Names(after));
                var expected = pair.Id == Sort ? after[1] : pair.Id == Order ? after[2] : after[3];

                Equal(expected.ToString(), restored.ToString(),
                    $"cursor: changing {pair.Name} keeps the cursor on its own row");
                True(restored >= LeadingItems,
                    $"cursor: changing {pair.Name} does not send the cursor above the songs");
                True(restored != start,
                    $"cursor: changing {pair.Name} moves the row rather than freezing the index");
            }
        }

        /// <summary>
        /// A filter that matches nothing leaves fewer rows than the cursor
        /// remembers, so the row has to be pulled into range rather than used
        /// as is or left pointing past the end of the menu.
        /// </summary>
        private static void ShortMenuPullsTheCursorBack()
        {
            var rows = Rows(40);
            var cursor = new MenuCursor();
            cursor.MoveTo(rows[1]);

            var tiny = Rows(1);
            var restored = cursor.Restore(tiny[0], Names(tiny));

            True(restored >= 0, "cursor: a shorter menu still yields a valid row");
            True(restored < tiny[0], "cursor: the row is inside the shorter menu");
        }

        /// <summary>
        /// A named row that is not in this build of the menu has to fall back to
        /// the plain index rather than being remembered forever.
        /// </summary>
        private static void MissingNamedRowFallsBackToItsIndex()
        {
            var cursor = new MenuCursor();

            // A name that is not in the menu at all, as happens when the row the
            // cursor was on is not drawn in this build.
            cursor.MoveTo(6, 99);

            var rows = Rows(10);
            Equal("6", cursor.Restore(rows[0], Names(rows)).ToString(),
                "cursor: a named row that is gone falls back to its index");
        }

        /// <summary>
        /// The browser hands over the names every rebuild, so a cursor sitting on
        /// a row that just acquired a name picks that name up on the next pass.
        /// </summary>
        private static void NamingARowLaterStillTracks()
        {
            var before = Rows(10);
            var cursor = new MenuCursor();
            cursor.MoveTo(before[1], Sort);

            var after = Rows(3);
            Equal(after[1].ToString(), cursor.Restore(after[0], Names(after)).ToString(),
                "cursor: the named row follows the menu as it changes shape");
            Equal(after[1].ToString(), cursor.Restore(after[0], Names(after)).ToString(),
                "cursor: restoring twice is stable");
        }

        private static void CursorIsClampedBothWays()
        {
            var cursor = new MenuCursor();

            cursor.MoveTo(999);
            Equal("9", cursor.Restore(10).ToString(), "cursor: a row past the end is pulled back");

            cursor.MoveTo(-5);
            Equal("0", cursor.Restore(10).ToString(), "cursor: a row before the start is pulled in");
        }

        private static void EmptyMenuIsHandled()
        {
            var cursor = new MenuCursor();

            cursor.MoveTo(7);
            Equal("0", cursor.Restore(0).ToString(), "cursor: an empty menu puts the cursor at zero");
            Equal("0", cursor.Restore(0).ToString(), "cursor: an empty menu stays at zero");
        }
        /// <summary>
        /// The browser passes -1 for a setting row that is not on the page in this
        /// build, such as the filter on a library too small for one. That must not
        /// be taken as a row the cursor can sit on.
        /// </summary>
        private static void UndrawnNamedRowIsNotUsed()
        {
            var rows = Rows(10);
            var cursor = new MenuCursor();
            cursor.MoveTo(rows[1], Sort);

            var names = new[]
            {
                new KeyValuePair<int, int>(Sort, -1),
                new KeyValuePair<int, int>(Order, rows[2]),
                new KeyValuePair<int, int>(Filter, rows[3])
            };

            Equal(rows[1].ToString(), cursor.Restore(rows[0], names).ToString(),
                "cursor: a setting row that is not drawn does not pull the cursor to the top");
        }

        /// <summary>An unplaced cursor is reported as such rather than guessed at.</summary>
        private static void UnplacedCursorIsVisible()
        {
            var cursor = new MenuCursor();
            Equal("-1", cursor.Row.ToString(), "cursor: starts unplaced");

            var rows = Rows(10);
            Equal("0", cursor.Restore(rows[0], Names(rows)).ToString(),
                "cursor: an unplaced cursor starts at the top");
        }

        /// <summary>
        /// Moving onto a song row and back out to the settings has to keep the
        /// place, which is the whole reason the cursor is tracked separately from
        /// the highlighted song.
        /// </summary>
        private static void MovingBetweenSongsAndSettingsKeepsTheRow()
        {
            var rows = Rows(10);
            var cursor = new MenuCursor();

            cursor.MoveTo(LeadingItems + 4);
            Equal((LeadingItems + 4).ToString(), cursor.Restore(rows[0], Names(rows)).ToString(),
                "cursor: a song row is remembered");

            cursor.MoveTo(rows[1], Sort);
            Equal(rows[1].ToString(), cursor.Restore(rows[0], Names(rows)).ToString(),
                "cursor: moving from a song to the sort row sticks");
        }

        /// <summary>
        /// A rebuild throws away every TextMenu.Item and rebuilds the song list, so
        /// the cursor must not add to that. <see cref="MenuCursor"/> reads the named
        /// rows it is handed without allocating, and the page refills one reused
        /// list rather than building a new one per rebuild.
        ///
        /// This pins that down: a regression here would be invisible in the UI but
        /// would show up as GC pressure in the IVA.
        /// </summary>
        private static void RestoreDoesNotAllocate()
        {
            var rows = Rows(10);
            var names = Names(rows);
            var cursor = new MenuCursor();
            cursor.MoveTo(rows[1], Sort);

            // Warm up, so JIT compilation is not counted.
            cursor.Restore(rows[0], names);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            const int iterations = 20000;
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < iterations; i++)
            {
                cursor.Restore(rows[0], names);
            }
            var after = GC.GetAllocatedBytesForCurrentThread();

            Equal("0", (after - before).ToString(),
                "cursor: restoring the cursor allocates nothing per rebuild");
        }

        /// <summary>The named rows are read, never kept, so the caller's list is reusable.</summary>
        private static void NamedRowsAreNotRetained()
        {
            var rows = Rows(10);
            var names = Names(rows);

            var cursor = new MenuCursor();
            cursor.MoveTo(rows[1], Sort);
            cursor.Restore(rows[0], names);

            // Reusing the same list for a differently shaped menu must give the
            // new answer, which it only can if nothing was copied out of it.
            var after = Rows(3);
            names[0] = new KeyValuePair<int, int>(Sort, after[1]);

            Equal(after[1].ToString(), cursor.Restore(after[0], names).ToString(),
                "cursor: a rebuilt list of names is read on every restore");
        }

        internal static void Run()
        {
            FirstBuildStartsOnTheFirstSong();
            CursorSurvivesARebuild();
            SettingRowsFollowTheirOwnRow();
            ShortMenuPullsTheCursorBack();
            MissingNamedRowFallsBackToItsIndex();
            UndrawnNamedRowIsNotUsed();
            NamingARowLaterStillTracks();
            CursorIsClampedBothWays();
            EmptyMenuIsHandled();
            UnplacedCursorIsVisible();
            MovingBetweenSongsAndSettingsKeepsTheRow();
            RestoreDoesNotAllocate();
            NamedRowsAreNotRetained();
        }
    }
}
