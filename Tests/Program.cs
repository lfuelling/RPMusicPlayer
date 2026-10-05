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

        private static int Main(string[] args)
        {
            if (args.Length > 0)
            {
                return Inspect.Run(args[0]);
            }

            Id3v23Latin1();
            Id3v24Utf16();
            Id3v22ShortFrames();
            Id3v1Only();
            NumericGenre();
            NoTags();
            WavInfoChunks();
            WavEmbeddedId3Chunk();
            OggVorbisComments();
            FlacVorbisComments();
            OggPacketAcrossPages();
            OggWithVideoStream();
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

        /// <summary>
                /// A wave file with only LIST/INFO tags, which is how most taggers write them.
                /// </summary>
                private static void WavInfoChunks()
                {
                    var body = new List<byte>();
                    AddInfoSubChunk(body, "INAM", "Pioneer");
                    AddInfoSubChunk(body, "IART", "Lerk");
                    AddInfoSubChunk(body, "IPRD", "Spacewalk");
                    AddInfoSubChunk(body, "IGNR", "Electronic");
                    AddInfoSubChunk(body, "ICRD", "2018");

                    var list = new List<byte>();
                    list.AddRange(Encoding.ASCII.GetBytes("INFO"));
                    list.AddRange(body);

                    var tags = Read(WriteWav(list, null));

                    Equal("Pioneer", tags.Title, "wav INAM");
                    Equal("Lerk", tags.Artist, "wav IART");
                    Equal("Spacewalk", tags.Album, "wav IPRD");
                    Equal("Electronic", tags.Genre, "wav IGNR");
                }

                /// <summary>
                /// A wave file carrying a normal ID3v2 tag in an "ID3 " chunk after the audio,
                /// which is what most soundtrack rips look like.
                /// </summary>
                private static void WavEmbeddedId3Chunk()
                {
                    var frames = new List<byte>();
                    AddFrame(frames, "TIT2", Utf16("Pioneer"));
                    AddFrame(frames, "TPE1", Utf16("Lerk"));
                    AddFrame(frames, "TALB", Utf16("Spacewalk"));

                    var id3 = new List<byte>();
                    id3.AddRange(Encoding.ASCII.GetBytes("ID3"));
                    id3.AddRange(new byte[] { 3, 0, 0 });
                    id3.AddRange(SyncSafe(frames.Count));
                    id3.AddRange(frames);

                    var tags = Read(WriteWav(null, id3));

                    Equal("Pioneer", tags.Title, "wav ID3 chunk TIT2");
                    Equal("Lerk", tags.Artist, "wav ID3 chunk TPE1");
                    Equal("Spacewalk", tags.Album, "wav ID3 chunk TALB");
                }

                private static void AddInfoSubChunk(List<byte> body, string id, string value)
                {
                    // Values are NUL terminated and padded to an even length.
                    var bytes = new List<byte>(Encoding.GetEncoding(28591).GetBytes(value));
                    bytes.Add(0);
                    if (bytes.Count % 2 != 0)
                    {
                        bytes.Add(0);
                    }

                    body.AddRange(Encoding.ASCII.GetBytes(id));
                    body.AddRange(new byte[] { (byte)(bytes.Count & 0xFF), (byte)(bytes.Count >> 8), 0, 0 });
                    body.AddRange(bytes);
                }

                /// <summary>Builds a small fake wave file with the given chunks after the audio.</summary>
                private static string WriteWav(List<byte> listBody, List<byte> id3Body)
                {
                    var chunks = new List<byte>();

                    chunks.AddRange(Encoding.ASCII.GetBytes("fmt "));
                    chunks.AddRange(new byte[] { 16, 0, 0, 0 });
                    chunks.AddRange(new byte[16]);

                    chunks.AddRange(Encoding.ASCII.GetBytes("data"));
                    chunks.AddRange(new byte[] { 8, 0, 0, 0 });
                    chunks.AddRange(new byte[8]);

                    if (listBody != null)
                    {
                        chunks.AddRange(Encoding.ASCII.GetBytes("LIST"));
                        chunks.AddRange(LittleEndianInt32(listBody.Count));
                        chunks.AddRange(listBody);
                    }

                    if (id3Body != null)
                    {
                        chunks.AddRange(Encoding.ASCII.GetBytes("ID3 "));
                        chunks.AddRange(LittleEndianInt32(id3Body.Count));
                        chunks.AddRange(id3Body);
                    }

                    var file = Path.Combine(Path.GetTempPath(), "rpmp-test-" + Guid.NewGuid().ToString("N") + ".wav");

                    var contents = new List<byte>();
                    contents.AddRange(Encoding.ASCII.GetBytes("RIFF"));
                    contents.AddRange(LittleEndianInt32(4 + chunks.Count));
                    contents.AddRange(Encoding.ASCII.GetBytes("WAVE"));
                    contents.AddRange(chunks);

                    File.WriteAllBytes(file, contents.ToArray());
                    TempFiles.Add(file);
                    return file;
                }

                private static byte[] LittleEndianInt32(int value)
                {
                    return new[]
                    {
                        (byte)(value & 0xFF),
                        (byte)((value >> 8) & 0xFF),
                        (byte)((value >> 16) & 0xFF),
                        (byte)((value >> 24) & 0xFF)
                    };
                }

                /// <summary>
                        /// An ogg Vorbis file: the tags live in the second packet of the audio stream,
                        /// which comes after a different stream's packets.
                        /// </summary>
                        private static void OggVorbisComments()
                        {
                            var file = Path.Combine(Path.GetTempPath(), "rpmp-test-" + Guid.NewGuid().ToString("N") + ".ogg");

                            var comments = new List<string> { "encoder=Something", "album=Formation", "artist=Lerk", "genre=Techno", "title=Formation" };
                            var commentPacket = BuildVorbisCommentPacket(comments);

                            var identPacket = new byte[30];
                            identPacket[0] = 0x01;
                            Encoding.ASCII.GetBytes("vorbis").CopyTo(identPacket, 1);

                            var pages = new List<byte>();
                            AddOggPage(pages, 1, 0x02, 0xAA, identPacket);
                            AddOggPage(pages, 1, 0x02, 0xBB, Encoding.ASCII.GetBytes("\x81theora"));
                            AddOggPage(pages, 2, 0x00, 0xBB, new byte[] { (byte)'\x83', (byte)'t', (byte)'h', (byte)'e', (byte)'o', (byte)'r', (byte)'a' });
                            AddOggPage(pages, 2, 0x00, 0xAA, commentPacket);

                            File.WriteAllBytes(file, pages.ToArray());
                            TempFiles.Add(file);

                            var tags = Read(file);

                            Equal("Formation", tags.Title, "ogg TITLE");
                            Equal("Lerk", tags.Artist, "ogg ARTIST");
                            Equal("Formation", tags.Album, "ogg ALBUM");
                            Equal("Techno", tags.Genre, "ogg GENRE");
                        }

                        private static void FlacVorbisComments()
                        {
                            var file = Path.Combine(Path.GetTempPath(), "rpmp-test-" + Guid.NewGuid().ToString("N") + ".flac");

                            var block = BuildVorbisCommentBody(new List<string>
                            {
                                "album=Formation", "artist=Lerk", "title=Formation", "genre=Techno"
                            });

                            var blockSize = block.Length;

                            var contents = new List<byte>();
                            contents.AddRange(Encoding.ASCII.GetBytes("fLaC"));

                            // A metadata block header: last block flag, type 4 (vorbis comment), 24 bit size.
                            contents.Add(0x80 | 4);
                            contents.Add((byte)((blockSize >> 16) & 0xFF));
                            contents.Add((byte)((blockSize >> 8) & 0xFF));
                            contents.Add((byte)(blockSize & 0xFF));
                            contents.AddRange(block);

                            File.WriteAllBytes(file, contents.ToArray());
                            TempFiles.Add(file);

                            var tags = Read(file);

                            Equal("Formation", tags.Title, "flac TITLE");
                            Equal("Lerk", tags.Artist, "flac ARTIST");
                            Equal("Formation", tags.Album, "flac ALBUM");
                            Equal("Techno", tags.Genre, "flac GENRE");
                        }

                        /// <summary>A "\x03vorbis" packet wrapping a comment block.</summary>
                        private static byte[] BuildVorbisCommentPacket(List<string> comments)
                        {
                            var packet = new List<byte> { 0x03 };
                            packet.AddRange(Encoding.ASCII.GetBytes("vorbis"));
                            packet.AddRange(BuildVorbisCommentBody(comments));
                            return packet.ToArray();
                        }

                        /// <summary>A vorbis comment block: vendor string, count, then length prefixed comments.</summary>
                        private static byte[] BuildVorbisCommentBody(List<string> comments)
                        {
                            var body = new List<byte>();

                            var vendor = Encoding.ASCII.GetBytes("test");
                            body.AddRange(LittleEndianInt32(vendor.Length));
                            body.AddRange(vendor);

                            body.AddRange(LittleEndianInt32(comments.Count));
                            foreach (var comment in comments)
                            {
                                var bytes = Encoding.UTF8.GetBytes(comment);
                                body.AddRange(LittleEndianInt32(bytes.Length));
                                body.AddRange(bytes);
                            }

                            return body.ToArray();
                        }

                        /// <summary>
                        /// Writes one ogg page. A packet is split into 255 byte segments plus a remainder,
                        /// which is how real files encode anything longer than 255 bytes.
                        /// </summary>
                        private static void AddOggPage(List<byte> target, int sequence, int headerType, uint serial, byte[] packet)
                        {
                            var segments = new List<byte>();
                            int remaining = packet.Length;
                            int offset = 0;

                            while (remaining >= 255)
                            {
                                segments.Add(255);
                                offset += 255;
                                remaining -= 255;
                            }
                            segments.Add((byte)remaining);

                            target.AddRange(Encoding.ASCII.GetBytes("OggS"));
                            target.Add(0);                        // version
                            target.Add((byte)headerType);
                            target.AddRange(new byte[8]);         // granule position
                            target.AddRange(LittleEndianInt32((int)serial));
                            target.AddRange(LittleEndianInt32(sequence));
                            target.AddRange(new byte[4]);         // checksum, not validated by the reader
                            target.Add((byte)segments.Count);
                            target.AddRange(segments);
                            target.AddRange(packet);
                        }

                        /// <summary>
            /// Writes one ogg page from an explicit lacing table, for packets that span
            /// pages: a segment of exactly 255 bytes leaves the packet open and it only
            /// ends on a later page.
            /// </summary>
            private static void AddRawOggPage(List<byte> target, int sequence, int headerType, uint serial, byte[] lacing, byte[] body)
            {
                target.AddRange(Encoding.ASCII.GetBytes("OggS"));
                target.Add(0);                        // version
                target.Add((byte)headerType);
                target.AddRange(new byte[8]);         // granule position
                target.AddRange(LittleEndianInt32((int)serial));
                target.AddRange(LittleEndianInt32(sequence));
                target.AddRange(new byte[4]);         // checksum, not validated by the reader
                target.Add((byte)lacing.Length);
                target.AddRange(lacing);
                target.AddRange(body);
            }

            /// <summary>
            /// An ogg whose comment packet is split over two pages: the first page's only
            /// segment is a full 255 bytes, so the packet only ends on the next page. Real
            /// encoders write long comment packets (cover art) exactly this way, and the
            /// reassembled packet must not be mistaken for a video stream's header.
            /// </summary>
            private static void OggPacketAcrossPages()
            {
                var file = Path.Combine(Path.GetTempPath(), "rpmp-test-" + Guid.NewGuid().ToString("N") + ".ogg");

                var identPacket = new byte[30];
                identPacket[0] = 0x01;
                Encoding.ASCII.GetBytes("vorbis").CopyTo(identPacket, 1);

                var commentPacket = BuildVorbisCommentPacket(new List<string>
                {
                    "album=Across Pages", "artist=Lerk", "title=Across Pages",

                    // Long enough to span pages, the way a comment packet with cover
                    // art in it does.
                    "padding=" + new string('x', 400)
                });

                var pages = new List<byte>();
                AddOggPage(pages, 1, 0x02, 0xAA, identPacket);

                var first = new byte[255];
                Array.Copy(commentPacket, 0, first, 0, 255);
                AddRawOggPage(pages, 2, 0x00, 0xAA, new byte[] { 255 }, first);

                var rest = new byte[commentPacket.Length - 255];
                Array.Copy(commentPacket, 255, rest, 0, rest.Length);
                AddRawOggPage(pages, 3, 0x00, 0xAA, new[] { (byte)rest.Length }, rest);

                File.WriteAllBytes(file, pages.ToArray());
                TempFiles.Add(file);

                var tags = Read(file);

                Equal("Across Pages", tags.Title, "ogg packet across pages: TITLE");
                Equal("Lerk", tags.Artist, "ogg packet across pages: ARTIST");
                Equal("Across Pages", tags.Album, "ogg packet across pages: ALBUM");
                False(tags.HasVideoStream, "ogg packet across pages: not flagged as video");
            }

        /// <summary>
        /// An ogg that also holds a Theora video stream, which is the case the audio
        /// engine may refuse to turn into audio. The reader has to notice and say so.
        /// </summary>
        private static void OggWithVideoStream()
        {
            var file = Path.Combine(Path.GetTempPath(), "rpmp-test-" + Guid.NewGuid().ToString("N") + ".ogg");

            var identPacket = new byte[30];
            identPacket[0] = 0x01;
            Encoding.ASCII.GetBytes("vorbis").CopyTo(identPacket, 1);

            var pages = new List<byte>();
            AddOggPage(pages, 1, 0x02, 0xCC, new byte[] { (byte)'\x80', (byte)'t', (byte)'h', (byte)'e', (byte)'o', (byte)'r', (byte)'a' });
            AddOggPage(pages, 1, 0x02, 0xAA, identPacket);
            AddOggPage(pages, 2, 0x00, 0xAA, BuildVorbisCommentPacket(new List<string> { "title=Formation", "artist=Lerk" }));

            File.WriteAllBytes(file, pages.ToArray());
            TempFiles.Add(file);

            var tags = Read(file);

            Equal("Formation", tags.Title, "ogg with video: title still read");
            True(tags.HasVideoStream, "ogg with video: flagged as carrying a video stream");
        }

        private static void True(bool value, string what)
        {
            checks++;
            if (value)
            {
                Console.WriteLine("  ok    " + what);
                return;
            }
            Fail(what + ": expected true but got false");
        }

        private static void False(bool value, string what)
        {
            True(!value, what);
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