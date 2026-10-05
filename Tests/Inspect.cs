using System;
using System.Collections.Generic;
using System.IO;

namespace RPMusicPlayer.Tests
{
    /// <summary>
    /// Not a test case: prints what the tag reader makes of real files, so a reported
    /// tagging problem can be reproduced outside Kerbal Space Program.
    ///
    ///   dotnet run --project Tests -- "path to music"
    /// </summary>
    internal static class Inspect
    {
        internal static int Run(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                Console.WriteLine("Usage: dotnet run --project Tests -- <music folder>");
                return 1;
            }

            var files = Directory.GetFiles(folder);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            var tagged = 0;
            foreach (var file in files)
            {
                if (Path.GetExtension(file).Equals(".txt", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var tags = TagReader.Read(file);
                if (tags.HasAnything)
                {
                    tagged++;
                }

                Console.WriteLine(Path.GetFileName(file));
                Console.WriteLine("    title  : {0}", tags.Title ?? "-");
                Console.WriteLine("    artist : {0}", tags.Artist ?? "-");
                Console.WriteLine("    album  : {0}", tags.Album ?? "-");
                Console.WriteLine("    genre  : {0}", tags.Genre ?? "-");
                Console.WriteLine("    video  : {0}", tags.HasVideoStream ? "YES - would be listed as unplayable" : "no");

                var track = new MusicTrack(file, tags);
                Console.WriteLine("    shows  : {0}", track.ArtistAndTitle);
            }

            Console.WriteLine();
            Console.WriteLine("{0} of {1} files have tags.", tagged, files.Length);
            return 0;
        }
    }
}