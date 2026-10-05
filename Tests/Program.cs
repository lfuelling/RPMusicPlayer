using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RPMusicPlayer.Tests
{
    internal static class Program
    {
        private static int failures;
        private static int checks;
        private static readonly List<string> TempFiles = new List<string>();

        private static int Main()
        {
            Id3v23Latin1();
            Id3v24Utf16();
            Id3v22ShortFrames();
            Id3v1Only();
            NumericGenre();
            NoTags();
            Durations();

            foreach (var file in TempFiles)
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                    // Leftover temp files are not worth failing over.
                }
            }

            Console.WriteLine();
            Console.WriteLine(failures == 0
                ? $"All {checks} checks passed."
                : $"{failures} of {checks} checks FAILED.");

            return failures == 0 ? 0 : 1;
        }

        private static void Id3v23Latin1()
        {
            var body = new List<byte>();
            AddFrame(body, "TIT2", Latin1("Kerbal Space Anthem"));
            AddFrame(body, "TPE1", Latin1("The Jebia Historical Society"));
            AddFrame(body, "TALB", Latin1("Songs of the Mun"));
            AddFrame(body, "TCON", Latin1("Space Opera"));

            var tags = Read(WriteMp3(new byte[] { 3, 0 }, body));

            Equal("Kerbal Space Anthem", tags.Title, "ID3v2.3 title");
            Equal("The Jebia Historical Society", tags.Artist, "ID3v2.3 artist");
            Equal("Songs of the Mun", tags.Album, "ID3v2.3 album");
            Equal("Space Opera", tags.Genre, "ID3v2.3 genre");
        }

        private static void Id3v24Utf16()
        {
            var body = new List<byte>();
            AddFrame(body, "TIT2", Utf16("Kerbál Song"));
            AddFrame(body, "TPE1", Utf8("Björn"));

            var tags = Read(WriteMp3(new byte[] { 4, 0 }, body));

            Equal("Kerbál Song", tags.Title, "ID3v2.4 UTF-16 title");
            Equal("Björn", tags.Artist, "ID3v2.4 UTF-8 artist");
        }

        private static void Id3v22ShortFrames()
        {
            var content = Latin1("Old Tag");
            var body = new List<byte>();
            body.AddRange(Encoding.ASCII.GetBytes("TT2"));
            body.AddRange(new byte[] { 0x00, (byte)((content.Length >> 8) & 0xFF), (byte)(content.Length & 0xFF) });
            body.AddRange(content);

            var tags = Read(WriteMp3(new byte[] { 2, 0 }, body));

            Equal("Old Tag", tags.Title, "ID3v2.2 three character frame id");
        }

        private static void Id3v1Only()
        {
            var block = new byte[128];
            Encoding.ASCII.GetBytes("TAG").CopyTo(block, 0);
            Pad(block, 3, 30, "Distant Man");
            Pad(block, 33, 30, "Bill");
            Pad(block, 63, 30, "Kerbin Sessions");
            block[127] = 17; // Rock

            var tags = Read(WriteMp3(null, null, block));

            Equal("Distant Man", tags.Title, "ID3v1 title");
            Equal("Bill", tags.Artist, "ID3v1 artist");
            Equal("Kerbin Sessions", tags.Album, "ID3v1 album");
            Equal("Rock", tags.Genre, "ID3v1 genre");
        }

        private static void NumericGenre()
        {
            var body = new List<byte>();
            AddFrame(body, "TCON", Latin1("(17)"));

            var tags = Read(WriteMp3(new byte[] { 3, 0 }, body));

            Equal("Rock", tags.Genre, "numeric genre 17");
        }

        private static void NoTags()
        {
            var tags = Read(WriteMp3(null, null, null));

            if (tags.HasAnything)
            {
                Fail("a file with no tags should report none");
            }
        }

        private static void Durations()
        {
            Equal("00:00", MusicTrack.FormatDuration(0), "zero duration");
            Equal("03:07", MusicTrack.FormatDuration(187), "minutes and seconds");
            Equal("1:01:01", MusicTrack.FormatDuration(3661), "hours");
            Equal("--:--", MusicTrack.FormatDuration(-1), "negative duration");
        }

        // ------------------------------------------------------------ test helpers

        private static byte[] Latin1(string text)
        {
            var content = Encoding.GetEncoding(28591).GetBytes(text);
            var frame = new byte[content.Length + 1];
            frame[0] = 0;
            content.CopyTo(frame, 1);
            return frame;
        }

        private static byte[] Utf8(string text)
        {
            var content = new UTF8Encoding(false).GetBytes(text);
            var frame = new byte[content.Length + 1];
            frame[0] = 3;
            content.CopyTo(frame, 1);
            return frame;
        }

        private static byte[] Utf16(string text)
        {
            var content = new UnicodeEncoding(false, true).GetBytes(text);
            var frame = new byte[content.Length + 1];
            frame[0] = 1;
            content.CopyTo(frame, 1);
            return frame;
        }

        private static void AddFrame(List<byte> body, string id, byte[] content)
        {
            body.AddRange(Encoding.ASCII.GetBytes(id));
            body.AddRange(new byte[] { 0, 0, 0, (byte)content.Length });
            body.AddRange(new byte[] { 0, 0 });
            body.AddRange(content);
        }

        private static void Pad(byte[] block, int offset, int length, string text)
        {
            var bytes = Encoding.ASCII.GetBytes(text);
            Array.Copy(bytes, 0, block, offset, Math.Min(bytes.Length, length));
        }

        /// <summary>Builds a fake mp3: an optional ID3v2 tag, a little audio, and an optional ID3v1 block.</summary>
        private static string WriteMp3(byte[] version, List<byte> body, byte[] id3v1 = null)
        {
            var file = Path.Combine(Path.GetTempPath(), "rpmp-test-" + Guid.NewGuid().ToString("N") + ".mp3");
            var contents = new List<byte>();

            if (version != null && body != null)
            {
                var size = SyncSafe(body.Count);
                contents.AddRange(Encoding.ASCII.GetBytes("ID3"));
                contents.AddRange(version);
                contents.Add(0); // flags
                contents.AddRange(size);
                contents.AddRange(body);
            }

            contents.AddRange(new byte[] { 0xFF, 0xFB, 0x90, 0x64 });

            if (id3v1 != null)
            {
                contents.AddRange(id3v1);
            }

            File.WriteAllBytes(file, contents.ToArray());
            TempFiles.Add(file);
            return file;
        }

        private static byte[] SyncSafe(int value)
        {
            return new[]
            {
                (byte)((value >> 21) & 0x7F),
                (byte)((value >> 14) & 0x7F),
                (byte)((value >> 7) & 0x7F),
                (byte)(value & 0x7F)
            };
        }

        private static TrackTags Read(string file)
        {
            return TagReader.Read(file);
        }

        private static void Equal(string expected, string actual, string what)
        {
            checks++;
            if (string.Equals(expected, actual, StringComparison.Ordinal))
            {
                Console.WriteLine("  ok    " + what);
                return;
            }
            Fail(what + ": expected \"" + expected + "\" but got \"" + (actual ?? "<null>") + "\"");
        }

        private static void Fail(string message)
        {
            failures++;
            Console.WriteLine("  FAIL  " + message);
        }
    }
}