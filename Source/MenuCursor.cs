using System.Collections.Generic;

namespace RPMusicPlayer
{
    /// <summary>
    /// Where the cursor sits in a menu, kept across rebuilds.
    ///
    /// Rebuilding a menu throws the cursor away, so a page has to put it back or
    /// the selection jumps the moment anything redraws. The page hands this the
    /// row it wants and asks for it back after every rebuild.
    ///
    /// Remembering a plain menu index is not enough: the rows below the song list
    /// move when the list changes length, so an index that pointed at the sort row
    /// can end up pointing at rescan after a filter narrows the list. The cursor
    /// therefore remembers rows by the name the page gave them and resolves that
    /// name against the rebuilt menu.
    ///
    /// Holding no Unity types keeps it testable: this is the piece that decides
    /// whether the cursor stays where the player put it.
    /// </summary>
    internal sealed class MenuCursor
    {
        /// <summary>
        /// What the cursor is on, independent of where that row ended up. -1 is
        /// "a row the page has not named", which is tracked by index alone.
        /// </summary>
        private int anchor = -1;

        private int row = -1;

        /// <summary>The row the cursor is currently on.</summary>
        internal int Row
        {
            get { return row; }
        }

        /// <summary>Records that the cursor is now on this row, with no name for it.</summary>
        internal void MoveTo(int value)
        {
            anchor = -1;
            row = value;
        }

        /// <summary>Records that the cursor is now on the row called <paramref name="name"/>.</summary>
        internal void MoveTo(int value, int name)
        {
            anchor = name;
            row = value;
        }

        /// <summary>
        /// The row to put the cursor on for a menu of <paramref name="itemCount"/>
        /// items. A named row is looked up in the new menu first; anything else
        /// falls back to the remembered index, pulled into range.
        ///
        /// The named rows are an array read by index rather than enumerated: this
        /// runs on every rebuild, and enumerating a list of structs boxes the
        /// enumerator, which is an allocation per redraw for no reason.
        /// </summary>
        internal int Restore(int itemCount, KeyValuePair<int, int>[] namedRows = null)
        {
            if (itemCount <= 0)
            {
                row = 0;
                return row;
            }

            if (anchor >= 0 && namedRows != null)
            {
                for (int i = 0; i < namedRows.Length; i++)
                {
                    var pair = namedRows[i];

                    // A page passes -1 for a row it did not draw this time, which
                    // is not somewhere the cursor can go.
                    if (pair.Key == anchor && pair.Value >= 0)
                    {
                        row = Clamp(pair.Value, 0, itemCount - 1);
                        return row;
                    }
                }

                // The row the cursor was on is gone from this build of the menu,
                // so fall through to the plain index and let it be clamped.
            }

            if (row < 0 || row > itemCount - 1)
            {
                row = Clamp(row, 0, itemCount - 1);
            }

            return row;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }
            return value > max ? max : value;
        }
    }
}
