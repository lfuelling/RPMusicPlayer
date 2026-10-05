using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RPMusicPlayer
{
    /// <summary>
    /// The handful of tag fields the browser can sort and display.
    /// </summary>
    internal sealed class TrackTags
    {
        internal string Title;
        internal string Artist;
        internal string Album;
        internal string Genre;

        /// <summary>
        /// Set when the file carries a video stream as well as audio, which the
        /// audio engine may refuse to decode. Worth knowing before the track is
        /// selected, but not certain enough to stop it being selected: whether
        /// the audio really decodes is only settled by trying.
        /// </summary>
        internal bool HasVideoStream;

        internal bool HasAnything
        {
            get
            {
                return !string.IsNullOrEmpty(Title)
                    || !string.IsNullOrEmpty(Artist)
                    || !string.IsNullOrEmpty(Album)
                    || !string.IsNullOrEmpty(Genre);
            }
        }

        internal static TrackTags Empty()
        {
            return new TrackTags();
        }
    }

    /// <summary>
    /// Reads ID3v2.2/2.3/2.4 and ID3v1 tags straight from the file.
    ///
    /// This is deliberately hand written rather than pulling in a tagging library:
    /// KSP plugins are loaded by Unity's Mono runtime, and shipping a second
    /// assembly for four string fields is not worth the compatibility risk.
    /// </summary>
    internal static class TagReader
    {
        private static readonly string[] GenreNames =
        {
            "Blues", "Classic Rock", "Country", "Dance", "Disco", "Funk", "Grunge",
            "Hip-Hop", "Jazz", "Metal", "New Age", "Oldies", "Other", "Pop", "R&B",
            "Rap", "Reggae", "Rock", "Techno", "Industrial", "Alternative", "Ska",
            "Death Metal", "Pranks", "Soundtrack", "Euro-Techno", "Ambient",
            "Trip-Hop", "Vocal", "Jazz+Funk", "Fusion", "Trance", "Classical",
            "Instrumental", "Acid", "House", "Game", "Sound Clip", "Gospel",
            "Noise", "AlternRock", "Bass", "Soul", "Punk", "Space", "Meditative",
            "Instrumental Pop", "Instrumental Rock", "Ethnic", "Gothic",
            "Darkwave", "Techno-Industrial", "Electronic", "Pop-Folk",
            "Eurodance", "Dream", "Southern Rock", "Comedy", "Cult", "Gangsta",
            "Top 40", "Christian Rap", "Pop/Funk", "Jungle", "Native American",
            "Cabaret", "New Wave", "Psychadelic", "Rave", "Showtunes", "Trailer",
            "Lo-Fi", "Tribal", "Acid Punk", "Acid Jazz", "Polka", "Retro", "Musical",
            "Rock & Roll", "Hard Rock"
        };

        internal static TrackTags Read(string path)
        {
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var signature = new byte[4];
                    if (!ReadFully(stream, signature))
                    {
                        return TrackTags.Empty();
                    }

                    stream.Position = 0;

                    // Each container keeps its tags in a different place.
                    if (IsMagic(signature, 'R', 'I', 'F', 'F'))
                    {
                        return Merge(ReadRiff(stream), TrackTags.Empty());
                    }

                    if (IsMagic(signature, 'O', 'g', 'g', 'S'))
                    {
                        return ReadOgg(stream);
                    }

                    if (IsMagic(signature, 'f', 'L', 'a', 'C'))
                    {
                        return ReadFlac(stream);
                    }

                    var v2 = ReadId3v2(stream);
                    if (v2 != null && v2.HasAnything)
                    {
                        return v2;
                    }

                    var v1 = ReadId3v1(stream);
                    if (v1 != null && v1.HasAnything)
                    {
                        return v1;
                    }
                }
            }
            catch (Exception e)
            {
                Log.Info("Could not read tags from '{0}': {1}", Path.GetFileName(path), e.Message);
            }

            return TrackTags.Empty();
        }

        private static bool IsMagic(byte[] buffer, char a, char b, char c, char d)
        {
            return buffer[0] == (byte)a && buffer[1] == (byte)b
                && buffer[2] == (byte)c && buffer[3] == (byte)d;
        }

        // ------------------------------------------------------------------ ogg

        /// <summary>
        /// Reads the Vorbis comments out of an ogg stream.
        ///
        /// An ogg file is a series of pages of segments, and the tags live in the
        /// comment header packet of the audio stream, which is the second packet after
        /// that stream's identification header. Files can carry both video and audio
        /// streams, so the packets are matched to the right stream by serial number.
        /// </summary>
        private static TrackTags ReadOgg(FileStream stream)
        {
            var tags = new TrackTags();
            var vorbisStreams = new HashSet<uint>();

            var packet = new List<byte>();
            uint packetSerial = 0;
            bool packetOpen = false;

            long position = 0;
            int pagesRead = 0;

            // The comment header is always at the very start, so a small page cap is
            // plenty and keeps this cheap.
            while (position + 27 <= stream.Length && pagesRead < 64)
            {
                stream.Position = position;
                var header = new byte[27];
                if (!ReadFully(stream, header) || !IsMagic(header, 'O', 'g', 'g', 'S'))
                {
                    break;
                }

                uint serial = (uint)ReadLittleEndianInt32(header, 14);
                int segmentCount = header[26];

                var segments = new byte[segmentCount];
                if (!ReadFully(stream, segments))
                {
                    break;
                }

                long bodyLength = 0;
                for (int i = 0; i < segmentCount; i++)
                {
                    bodyLength += segments[i];
                }

                var body = new byte[bodyLength];
                if (!ReadFully(stream, body))
                {
                    break;
                }

                position = position + 27 + segmentCount + bodyLength;
                pagesRead++;

                int offset = 0;
                foreach (var segment in segments)
                {
                    if (!packetOpen)
                    {
                        packet.Clear();
                        packetSerial = serial;
                        packetOpen = true;
                    }

                    for (int i = 0; i < segment; i++)
                    {
                        packet.Add(body[offset + i]);
                    }
                    offset += segment;

                    // A segment shorter than 255 ends the packet.
                    if (segment != 255)
                    {
                        ReadOggPacket(packet, packetSerial, vorbisStreams, tags);
                        packet.Clear();
                        packetOpen = false;
                    }
                }
            }

            return tags;
        }

        private static void ReadOggPacket(List<byte> packet, uint serial, HashSet<uint> vorbisStreams, TrackTags tags)
        {
            var buffer = packet.ToArray();

            if (StartsWith(buffer, 0x01, (byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s'))
            {
                // Vorbis identification header: the comments come in the next packet.
                vorbisStreams.Add(serial);
                return;
            }

            // A file can hold Theora video alongside the Vorbis audio.
            if (StartsWith(buffer, 0x80, (byte)'t', (byte)'h', (byte)'e', (byte)'o', (byte)'r', (byte)'a')
                || StartsWith(buffer, 0x81, (byte)'t', (byte)'h', (byte)'e', (byte)'o', (byte)'r', (byte)'a'))
            {
                tags.HasVideoStream = true;
                return;
            }

            if (StartsWith(buffer, 0x03, (byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s')
                && vorbisStreams.Contains(serial))
            {
                ReadVorbisComments(buffer, 7, tags);
                return;
            }

            if (StartsWith(buffer, (byte)'O', (byte)'p', (byte)'u', (byte)'s', (byte)'T', (byte)'a', (byte)'g', (byte)'s'))
            {
                ReadVorbisComments(buffer, 8, tags);
            }
        }

        private static bool StartsWith(byte[] buffer, params byte[] signature)
        {
            if (buffer.Length < signature.Length)
            {
                return false;
            }
            for (int i = 0; i < signature.Length; i++)
            {
                if (buffer[i] != signature[i])
                {
                    return false;
                }
            }
            return true;
        }

        // ----------------------------------------------------------------- flac

        /// <summary>
        /// Reads the Vorbis comments out of a flac metadata block.
        /// </summary>
        private static TrackTags ReadFlac(FileStream stream)
        {
            var tags = new TrackTags();

            var magic = new byte[4];
            if (!ReadFully(stream, magic))
            {
                return tags;
            }

            for (int block = 0; block < 128; block++)
            {
                var header = new byte[4];
                if (!ReadFully(stream, header))
                {
                    break;
                }

                bool last = (header[0] & 0x80) != 0;
                int type = header[0] & 0x7F;
                long length = ((long)header[1] << 16) | ((long)header[2] << 8) | header[3];

                if (length < 0 || stream.Position + length > stream.Length)
                {
                    break;
                }

                if (type == 4)
                {
                    var body = new byte[length];
                    if (ReadFully(stream, body))
                    {
                        ReadVorbisComments(body, 0, tags);
                    }
                }
                else
                {
                    stream.Position += length;
                }

                if (last)
                {
                    break;
                }
            }

            return tags;
        }

        // ------------------------------------------------------- vorbis comments

        /// <summary>
        /// Reads a Vorbis comment block: a vendor string, a count, then that many
        /// length prefixed "KEY=value" strings.
        /// </summary>
        private static void ReadVorbisComments(byte[] buffer, int offset, TrackTags tags)
        {
            try
            {
                if (offset + 4 > buffer.Length)
                {
                    return;
                }

                var vendorLength = (int)ReadLittleEndianInt32(buffer, offset);
                offset += 4 + vendorLength;

                if (offset + 4 > buffer.Length)
                {
                    return;
                }

                int count = (int)ReadLittleEndianInt32(buffer, offset);
                offset += 4;

                // A corrupt file could claim an absurd number of comments.
                count = Math.Min(count, 4096);

                for (int i = 0; i < count; i++)
                {
                    if (offset + 4 > buffer.Length)
                    {
                        return;
                    }

                    int length = (int)ReadLittleEndianInt32(buffer, offset);
                    offset += 4;

                    if (length < 0 || offset + length > buffer.Length)
                    {
                        return;
                    }

                    var comment = new UTF8Encoding(false, false).GetString(buffer, offset, length);
                    offset += length;

                    ApplyVorbisComment(comment, tags);
                }
            }
            catch (Exception)
            {
                // A malformed comment block should not stop the rest of the scan.
            }
        }

        private static void ApplyVorbisComment(string comment, TrackTags tags)
        {
            var separator = comment.IndexOf('=');
            if (separator <= 0)
            {
                return;
            }

            var key = comment.Substring(0, separator).Trim().ToUpperInvariant();
            var value = Clean(comment.Substring(separator + 1));

            if (value == null)
            {
                return;
            }

            switch (key)
            {
                case "TITLE":
                    tags.Title = tags.Title ?? value;
                    break;
                case "ARTIST":
                    tags.Artist = tags.Artist ?? value;
                    break;
                case "ALBUM":
                    tags.Album = tags.Album ?? value;
                    break;
                case "GENRE":
                    tags.Genre = tags.Genre ?? value;
                    break;
            }
        }

        /// <summary>
        /// Fills the buffer, looping as needed: a single FileStream.Read is allowed to
        /// return fewer bytes than asked for, which would silently truncate a tag.
        /// </summary>
        private static bool ReadFully(Stream stream, byte[] buffer)
        {
            int filled = 0;
            while (filled < buffer.Length)
            {
                int read = stream.Read(buffer, filled, buffer.Length - filled);
                if (read <= 0)
                {
                    return false;
                }
                filled += read;
            }
            return true;
        }

        /// <summary>
                /// Fills in any field that is still empty from <paramref name="fallback"/>.
                /// </summary>
                private static TrackTags Merge(TrackTags tags, TrackTags fallback)
                {
                    if (tags == null)
                    {
                        return fallback;
                    }
                    if (fallback == null)
                    {
                        return tags;
                    }

                    if (string.IsNullOrEmpty(tags.Title)) { tags.Title = fallback.Title; }
                    if (string.IsNullOrEmpty(tags.Artist)) { tags.Artist = fallback.Artist; }
                    if (string.IsNullOrEmpty(tags.Album)) { tags.Album = fallback.Album; }
                    if (string.IsNullOrEmpty(tags.Genre)) { tags.Genre = fallback.Genre; }

                    return tags;
                }

                /// <summary>
                /// Reads the tags of a RIFF/WAVE file.
                ///
                /// Wave files do not use ID3 at the start of the file; they carry a LIST/INFO
                /// chunk, and often an "ID3 " chunk holding a normal ID3v2 tag (usually mostly
                /// cover art) alongside the audio. Both are read, with the ID3 tag winning
                /// because it carries more.
                /// </summary>
                private static TrackTags ReadRiff(FileStream stream)
                {
                    var tags = new TrackTags();
                    var info = new TrackTags();

                    long position = 12;
                    while (position + 8 <= stream.Length)
                    {
                        stream.Position = position;
                        var chunkHeader = new byte[8];
                        if (!ReadFully(stream, chunkHeader))
                        {
                            break;
                        }

                        var id = Encoding.ASCII.GetString(chunkHeader, 0, 4);
                        long size = ReadLittleEndianInt32(chunkHeader, 4);
                        long dataStart = position + 8;

                        // Guard against a truncated or nonsensical chunk size.
                        if (size < 0 || dataStart + size > stream.Length)
                        {
                            break;
                        }

                        if (string.Equals(id, "LIST", StringComparison.OrdinalIgnoreCase))
                        {
                            ReadInfoChunk(stream, dataStart, size, info);
                        }
                        else if (id.StartsWith("id3", StringComparison.OrdinalIgnoreCase))
                        {
                            stream.Position = dataStart;
                            Merge(tags, ReadId3v2(stream));
                        }

                        // Chunks are padded to an even size.
                        position = dataStart + size + (size & 1);
                    }

                    return Merge(tags, info);
                }

                /// <summary>Reads the sub chunks of a LIST/INFO chunk.</summary>
                private static void ReadInfoChunk(FileStream stream, long start, long size, TrackTags tags)
                {
                    // The first four bytes are the list type, which has to be "INFO".
                    var type = new byte[4];
                    stream.Position = start;
                    if (!ReadFully(stream, type) || type[0] != 'I' || type[1] != 'N' || type[2] != 'F' || type[3] != 'O')
                    {
                        return;
                    }

                    long position = start + 4;
                    long end = start + size;

                    while (position + 8 <= end)
                    {
                        stream.Position = position;
                        var subHeader = new byte[8];
                        if (!ReadFully(stream, subHeader))
                        {
                            return;
                        }

                        var id = Encoding.ASCII.GetString(subHeader, 0, 4);
                        int subSize = (int)Math.Min(ReadLittleEndianInt32(subHeader, 4), end - position - 8);
                        if (subSize < 0)
                        {
                            return;
                        }

                        var value = new byte[subSize];
                        if (!ReadFully(stream, value))
                        {
                            return;
                        }

                        var text = Clean(DecodeLatin1(value, 0, value.Length));
                        if (text != null)
                        {
                            switch (id.ToUpperInvariant())
                            {
                                case "INAM":
                                    tags.Title = tags.Title ?? text;
                                    break;
                                case "IART":
                                    tags.Artist = tags.Artist ?? text;
                                    break;
                                case "IPRD":
                                    tags.Album = tags.Album ?? text;
                                    break;
                                case "IGNR":
                                    tags.Genre = tags.Genre ?? text;
                                    break;
                            }
                        }

                        position += 8 + subSize + (subSize & 1);
                    }
                }

                private static TrackTags ReadId3v2(FileStream stream)
        {
            var header = new byte[10];
            if (!ReadFully(stream, header))
            {
                return null;
            }

            if (header[0] != 'I' || header[1] != 'D' || header[2] != '3')
            {
                return null;
            }

            int majorVersion = header[3];
            int flags = header[5];
            long tagSize = ReadSyncSafeInt32(header, 6);

            if (tagSize <= 0 || tagSize > stream.Length)
            {
                return null;
            }

            long bodyStart = stream.Position;
            long bodyEnd = Math.Min(bodyStart + tagSize, stream.Length);

            // Skip the extended header if one is present.
            if ((flags & 0x40) != 0)
            {
                var extendedHeader = new byte[4];
                if (stream.Position + 4 > bodyEnd || !ReadFully(stream, extendedHeader))
                {
                    return null;
                }
                long extendedSize = majorVersion >= 4
                    ? ReadSyncSafeInt32(extendedHeader, 0)
                    : ReadBigEndianInt32(extendedHeader, 0) + 4;
                stream.Position += Math.Max(0, extendedSize - extendedHeader.Length);
            }

            var tags = new TrackTags();
            bool id3v24 = majorVersion >= 4;

            while (stream.Position + (id3v24 ? 10 : 6) <= bodyEnd)
            {
                string frameId;
                long frameSize;

                if (majorVersion == 2)
                {
                    // ID3v2.2 uses three character frame names and three byte sizes.
                    var frameHeader = new byte[6];
                    if (!ReadFully(stream, frameHeader))
                    {
                        break;
                    }
                    if (frameHeader[0] == 0)
                    {
                        break;
                    }
                    frameId = Encoding.ASCII.GetString(frameHeader, 0, 3);
                    frameSize = ((long)frameHeader[3] << 16) | ((long)frameHeader[4] << 8) | frameHeader[5];
                }
                else
                {
                    var frameHeader = new byte[10];
                    if (!ReadFully(stream, frameHeader))
                    {
                        break;
                    }
                    if (frameHeader[0] == 0)
                    {
                        break;
                    }
                    frameId = Encoding.ASCII.GetString(frameHeader, 0, 4);
                    frameSize = id3v24
                        ? ReadSyncSafeInt32(frameHeader, 4)
                        : ReadBigEndianInt32(frameHeader, 4);
                }

                if (frameSize <= 0 || stream.Position + frameSize > bodyEnd)
                {
                    break;
                }

                if (IsTextFrame(frameId))
                {
                    var body = new byte[frameSize];
                    if (!ReadFully(stream, body))
                    {
                        break;
                    }

                    var value = DecodeTextFrame(body);
                    switch (FrameName(frameId))
                    {
                        case "TIT2":
                            tags.Title = tags.Title ?? value;
                            break;
                        case "TPE1":
                            tags.Artist = tags.Artist ?? value;
                            break;
                        case "TALB":
                            tags.Album = tags.Album ?? value;
                            break;
                        case "TCON":
                            tags.Genre = tags.Genre ?? NormalizeGenre(value);
                            break;
                    }
                }
                else
                {
                    stream.Position += frameSize;
                }
            }

            return tags;
        }

        private static TrackTags ReadId3v1(FileStream stream)
        {
            if (stream.Length < 128)
            {
                return null;
            }

            stream.Position = stream.Length - 128;
            var block = new byte[128];
            if (!ReadFully(stream, block))
            {
                return null;
            }

            if (block[0] != 'T' || block[1] != 'A' || block[2] != 'G')
            {
                return null;
            }

            var tags = new TrackTags();
            tags.Title = Clean(DecodeLatin1(block, 3, 30));
            tags.Artist = Clean(DecodeLatin1(block, 33, 30));
            tags.Album = Clean(DecodeLatin1(block, 63, 30));
            var genreIndex = block[127];
            tags.Genre = genreIndex < GenreNames.Length ? GenreNames[genreIndex] : null;
            return tags;
        }

        private static bool IsTextFrame(string frameId)
        {
            return frameId[0] == 'T' && frameId != "TXXX";
        }

        /// <summary>
        /// ID3v2.2 uses different frame names for the four fields we care about.
        /// </summary>
        private static string FrameName(string frameId)
        {
            switch (frameId)
            {
                case "TT2":
                    return "TIT2";
                case "TP1":
                    return "TPE1";
                case "TAL":
                    return "TALB";
                case "TCO":
                    return "TCON";
                default:
                    return frameId;
            }
        }

        private static string DecodeTextFrame(byte[] body)
        {
            if (body.Length < 1)
            {
                return null;
            }

            int encoding = body[0];
            var content = new byte[body.Length - 1];
            Array.Copy(body, 1, content, 0, content.Length);

            string value;
            switch (encoding)
            {
                case 0:
                    value = DecodeLatin1(content, 0, content.Length);
                    break;
                case 1:
                    value = DecodeUtf16WithBom(content);
                    break;
                case 2:
                    value = Encoding.BigEndianUnicode.GetString(content);
                    break;
                case 3:
                    value = new UTF8Encoding(false, true).GetString(content);
                    break;
                default:
                    value = DecodeLatin1(content, 0, content.Length);
                    break;
            }

            // ID3v2.4 separates multiple values with a null byte.
            if (value != null)
            {
                var separator = value.IndexOf('\0');
                if (separator >= 0)
                {
                    value = value.Substring(0, separator);
                }
            }

            return Clean(value);
        }

        private static string DecodeUtf16WithBom(byte[] content)
        {
            if (content.Length < 2)
            {
                return null;
            }

            if (content[0] == 0xFF && content[1] == 0xFE)
            {
                return Encoding.Unicode.GetString(content, 2, content.Length - 2);
            }
            if (content[0] == 0xFE && content[1] == 0xFF)
            {
                return Encoding.BigEndianUnicode.GetString(content, 2, content.Length - 2);
            }

            return Encoding.Unicode.GetString(content);
        }

        private static string DecodeLatin1(byte[] buffer, int offset, int length)
        {
            var limited = Math.Min(length, buffer.Length - offset);
            if (limited <= 0)
            {
                return null;
            }
            return Encoding.GetEncoding(28591).GetString(buffer, offset, limited);
        }

        private static string NormalizeGenre(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            // ID3v1 genre numbers may appear as "17" or "(17)".
            var trimmed = value.Trim();
            if (trimmed.Length > 0 && trimmed[0] == '(')
            {
                trimmed = trimmed.Substring(1);
            }
            if (trimmed.IndexOf(')') >= 0)
            {
                trimmed = trimmed.Substring(0, trimmed.IndexOf(')'));
            }

            int index;
            if (int.TryParse(trimmed, out index) && index >= 0 && index < GenreNames.Length)
            {
                return GenreNames[index];
            }

            return Clean(value);
        }

        private static string Clean(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            var trimmed = value.Trim('\0', ' ', '\t', '\r', '\n');
            return trimmed.Length == 0 ? null : trimmed;
        }

        private static long ReadSyncSafeInt32(byte[] buffer, int offset)
        {
            return ((long)(uint)(buffer[offset] & 0x7F) << 21)
                 | ((long)(uint)(buffer[offset + 1] & 0x7F) << 14)
                 | ((long)(uint)(buffer[offset + 2] & 0x7F) << 7)
                 | (long)(uint)(buffer[offset + 3] & 0x7F);
        }

        /// <summary>
                /// RIFF stores its sizes little endian, unlike ID3 which is big endian.
                /// </summary>
                private static long ReadLittleEndianInt32(byte[] buffer, int offset)
                {
                    return (long)(uint)(buffer[offset]
                         | ((long)(uint)buffer[offset + 1] << 8)
                         | ((long)(uint)buffer[offset + 2] << 16)
                         | ((long)(uint)buffer[offset + 3] << 24));
                }

                private static long ReadBigEndianInt32(byte[] buffer, int offset)
        {
            return ((long)(uint)buffer[offset] << 24)
                 | ((long)(uint)buffer[offset + 1] << 16)
                 | ((long)(uint)buffer[offset + 2] << 8)
                 | (long)(uint)buffer[offset + 3];
        }
    }
}