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

            long bodyStart = header.Length;
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

        private static long ReadBigEndianInt32(byte[] buffer, int offset)
        {
            return ((long)(uint)buffer[offset] << 24)
                 | ((long)(uint)buffer[offset + 1] << 16)
                 | ((long)(uint)buffer[offset + 2] << 8)
                 | (long)(uint)buffer[offset + 3];
        }
    }
}