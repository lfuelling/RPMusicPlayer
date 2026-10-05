using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace RPMusicPlayer
{
    /// <summary>
    /// Owns the single AudioSource the plugin plays music through.
    ///
    /// Unity decodes the audio files for us, so no codec has to be shipped with the
    /// plugin; this works the same on Windows, macOS and Linux.
    /// </summary>
    public sealed class AudioEngine : MonoBehaviour
    {
        private AudioSource source;

        /// <summary>Raised on the main thread once a track has finished playing.</summary>
        internal event Action<MusicTrack> TrackFinished;

        /// <summary>Raised once a new clip has finished loading, successfully or not.</summary>
        internal event Action<MusicTrack, AudioClip> TrackLoaded;

        internal MusicTrack Current { get; private set; }
        internal AudioClip CurrentClip { get; private set; }
        internal bool IsLoading { get; private set; }
        internal bool IsPlaying { get; private set; }
        internal bool IsPaused { get; private set; }

        /// <summary>True when something is loaded and the user has not paused it.</summary>
        internal bool IsActive
        {
            get { return Current != null && !IsPaused; }
        }

        internal double Position
        {
            get
            {
                if (source == null || CurrentClip == null || !source.isPlaying)
                {
                    return 0;
                }
                return source.time;
            }
        }

        internal double Duration
        {
            get { return CurrentClip != null ? CurrentClip.length : 0; }
        }

        internal float Volume
        {
            get { return source != null ? source.volume : 0f; }
            set
            {
                if (source != null)
                {
                    source.volume = Mathf.Clamp01(value);
                }
            }
        }

        internal void Initialize()
        {
            if (source != null)
            {
                return;
            }

            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
            source.priority = 0;
        }

        /// <summary>Re-applies the volume, e.g. after KSP's master volume changed.</summary>
        internal void RefreshVolume()
        {
            Volume = MasterVolume() * Settings.Current.Volume;
        }

        private static float MasterVolume()
        {
            try
            {
                return Mathf.Clamp01(GameSettings.MASTER_VOLUME);
            }
            catch (Exception)
            {
                return 1f;
            }
        }

        /// <summary>
        /// Starts a track. Loading happens over several frames, so the audio starts
        /// once the clip is ready rather than blocking the caller.
        /// </summary>
        internal void Play(MusicTrack track, double startAt)
        {
            if (track == null)
            {
                Stop();
                return;
            }

            Stop();
            Current = track;
            IsPaused = false;
            IsLoading = true;
            StartCoroutine(LoadAndPlay(track, startAt));
        }

        private IEnumerator LoadAndPlay(MusicTrack track, double startAt)
        {
            var url = ToFileUrl(track.Path);
            var audioType = AudioTypeFor(track.Extension);

            using (var request = UnityWebRequestMultimedia.GetAudioClip(url, audioType))
            {
                yield return request.SendWebRequest();

                AudioClip clip = null;
                if (request.isNetworkError || request.isHttpError)
                {
                    Log.Error("Could not load '{0}': {1}", track.FileName, request.error);
                }
                else
                {
                    clip = DownloadHandlerAudioClip.GetContent(request);
                }

                // The user may have skipped on while this was loading.
                if (!ReferenceEquals(Current, track))
                {
                    if (clip != null)
                    {
                        Destroy(clip);
                    }
                    yield break;
                }

                if (clip == null)
                {
                    IsLoading = false;
                    IsPlaying = false;
                    var handler = TrackLoaded;
                    if (handler != null)
                    {
                        handler(track, null);
                    }
                    yield break;
                }

                ReleaseClip();
                CurrentClip = clip;
                track.DurationSeconds = clip.length;

                source.clip = clip;
                RefreshVolume();
                source.time = Mathf.Clamp((float)startAt, 0f, Mathf.Max(0f, clip.length - 0.05f));
                source.Play();
                IsPlaying = true;

                var loaded = TrackLoaded;
                if (loaded != null)
                {
                    loaded(track, clip);
                }
            }

            IsLoading = false;
        }

        /// <summary>Pauses playback but keeps the current position.</summary>
        internal void Pause()
        {
            if (source == null || source.clip == null || !source.isPlaying)
            {
                return;
            }

            IsPlaying = false;
            IsPaused = true;
            source.Pause();
        }

        internal void Resume()
        {
            if (source == null || source.clip == null || source.isPlaying)
            {
                return;
            }

            IsPaused = false;
            IsPlaying = true;
            source.Play();
        }

        /// <summary>Stops and forgets the current track.</summary>
        internal void Stop()
        {
            if (source != null)
            {
                source.Stop();
                source.clip = null;
            }

            Current = null;
            CurrentClip = null;
            IsPlaying = false;
            IsPaused = false;
        }

        /// <summary>Stops the source but keeps the clip, so playback can resume later.</summary>
        internal void StopKeepingPosition()
        {
            if (source != null)
            {
                source.Stop();
            }
            IsPlaying = false;
            IsPaused = false;
        }

        private void Update()
        {
            if (source == null || CurrentClip == null || IsLoading)
            {
                return;
            }

            // Unity raises no event when a one shot clip finishes, and every path that
            // stops playback on purpose clears IsPlaying first, so a latched source that
            // is no longer playing means the track ran to its end.
            if (IsPlaying && !source.isPlaying)
            {
                IsPlaying = false;

                var finished = Current;
                var handler = TrackFinished;
                if (handler != null && finished != null)
                {
                    handler(finished);
                }
            }
        }

        private void ReleaseClip()
        {
            if (CurrentClip != null)
            {
                Destroy(CurrentClip);
                CurrentClip = null;
            }
        }

        private void OnDestroy()
        {
            Stop();
            ReleaseClip();
        }

        internal static AudioType AudioTypeFor(string extension)
        {
            switch (extension)
            {
                case "mp3":
                    return AudioType.MPEG;
                case "ogg":
                case "oga":
                    return AudioType.OGGVORBIS;
                case "wav":
                    return AudioType.WAV;
                case "aif":
                case "aiff":
                    return AudioType.AIFF;
                case "mod":
                    return AudioType.MOD;
                case "xm":
                    return AudioType.XM;
                case "it":
                    return AudioType.IT;
                case "s3m":
                    return AudioType.S3M;
                default:
                    return AudioType.UNKNOWN;
            }
        }

        /// <summary>
        /// Turns a local path into a file:// URL, escaping the characters that would
        /// otherwise break the URI.
        /// </summary>
        internal static string ToFileUrl(string path)
        {
            var full = Path.GetFullPath(path).Replace('\\', '/');

            // Windows drive letters need a third slash; "/home/..." already has one.
            var prefix = full.Length > 1 && full[1] == ':' ? "file:///" : "file://";

            var builder = new StringBuilder(prefix, prefix.Length + full.Length);
            foreach (var character in full)
            {
                if (IsUriSafe(character))
                {
                    builder.Append(character);
                }
                else
                {
                    builder.Append('%').Append(((int)character).ToString("X2"));
                }
            }

            return builder.ToString();
        }

        private static bool IsUriSafe(char character)
        {
            if ((character >= 'A' && character <= 'Z')
                || (character >= 'a' && character <= 'z')
                || (character >= '0' && character <= '9'))
            {
                return true;
            }

            switch (character)
            {
                case '-':
                case '_':
                case '.':
                case '~':
                case '/':
                case ':':
                    return true;
                default:
                    return false;
            }
        }
    }
}