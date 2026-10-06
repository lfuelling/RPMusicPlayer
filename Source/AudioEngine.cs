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
        /// <summary>How long a crossfade lasts, in seconds.</summary>
        internal const float CrossfadeSeconds = 3f;

        private AudioSource source;

        // Crossfade: while the new track plays on the main source, the previous one
        // keeps sounding on this second source and fades out.
        private AudioSource fadeSource;
        private AudioClip fadingClip;
        private float fadeStartedAt;
        private float fadeFromVolume;

        // The freshly started track of a crossfade ramps up from silence instead of
        // starting at full volume.
        private bool fadeInProgress;
        private float fadeInStartedAt;

        private float wantedVolume = 1f;

        private bool nearingEndRaised;

        /// <summary>Raised on the main thread once a track has finished playing.</summary>
        internal event Action<MusicTrack> TrackFinished;

        /// <summary>Raised once a new clip has finished loading, successfully or not.</summary>
        internal event Action<MusicTrack, AudioClip> TrackLoaded;

        /// <summary>
        /// Raised on the main thread when the current track is within the crossfade
        /// window of its end, while it is still playing. The listener decides whether
        /// to start the next song now or let it finish normally.
        /// </summary>
        internal event Action TrackNearingEnd;

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
            get { return wantedVolume; }
            set
            {
                wantedVolume = Mathf.Clamp01(value);
                ApplyVolume();
            }
        }

        /// <summary>The gain the playing source currently carries, so a crossfade starts quiet.</summary>
        private float FadeInGain
        {
            get
            {
                if (!fadeInProgress)
                {
                    return 1f;
                }
                return Mathf.Clamp01((Time.realtimeSinceStartup - fadeInStartedAt) / CrossfadeSeconds);
            }
        }

        private void ApplyVolume()
        {
            if (source != null)
            {
                source.volume = Mathf.Clamp01(wantedVolume * FadeInGain);
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

            fadeSource = gameObject.AddComponent<AudioSource>();
            fadeSource.playOnAwake = false;
            fadeSource.loop = false;
            fadeSource.spatialBlend = 0f;
            fadeSource.dopplerLevel = 0f;
            fadeSource.priority = 0;
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
            Play(track, startAt, false);
        }

        /// <summary>
        /// Starts a track, optionally crossfading: what is playing is handed to a
        /// second source and fades out while the new one ramps up underneath it.
        /// </summary>
        internal void Play(MusicTrack track, double startAt, bool fadeOutCurrent)
        {
            if (track == null)
            {
                Stop();
                return;
            }

            if (fadeOutCurrent && TryBeginFadeOut())
            {
                // The old track is fading on the second source; the main source is idle.
            }
            else
            {
                Stop();
            }

            Current = track;
            IsPaused = false;
            IsLoading = true;
            nearingEndRaised = false;
            fadeInProgress = fadeOutCurrent;
            StartCoroutine(LoadAndPlay(track, startAt));
        }

        private IEnumerator LoadAndPlay(MusicTrack track, double startAt)
        {
            var url = ToFileUrl(track.Path);
            var audioType = AudioTypeFor(track.Extension);
            var stream = ShouldStream(track);

            Log.Info("Loading '{0}' ({1:F1} MB, {2}).", track.FileName, SizeInMegabytes(track), stream ? "streaming" : "into memory");

            using (var request = UnityWebRequestMultimedia.GetAudioClip(url, audioType))
            {
                // This Unity build has no streaming overload of GetAudioClip, but the
                // download handler exposes the flag and it has to be set before sending.
                if (stream)
                {
                    var handler = request.downloadHandler as DownloadHandlerAudioClip;
                    if (handler != null)
                    {
                        handler.streamAudio = true;
                    }
                }

                yield return request.SendWebRequest();

                AudioClip clip = null;
                if (request.isNetworkError || request.isHttpError)
                {
                    Log.Error("Could not load '{0}': {1}", track.FileName, request.error);
                }
                else
                {
                    clip = DownloadHandlerAudioClip.GetContent(request);

                    // Unity reports success for files it cannot actually turn into audio,
                    // handing back an empty clip. Treating that as a playable track makes
                    // the queue jump straight to the next song.
                    if (clip != null && !IsUsable(clip))
                    {
                        Log.Error("'{0}' could not be turned into audio: {1}. " +
                            "Unity cannot decode every container; ogg files that also carry a " +
                            "video stream (Ogg Theora) are the usual cause, so try a plain " +
                            "audio only ogg.", track.FileName, Describe(clip));
                        Destroy(clip);
                        clip = null;
                    }
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

                // Only seek when resuming part way in. Setting time to zero is still a
                // seek, and asking a clip that cannot be seeked to do one makes FMOD
                // refuse to create a sound for it at all, which is silent failure for
                // some compressed formats.
                if (startAt > 0.01)
                {
                    Seek(source, clip, startAt);
                }
                source.Play();
                if (fadeInProgress)
                {
                    // The fade in is measured against the moment the music actually
                    // starts, not the moment Play was called.
                    fadeInStartedAt = Time.realtimeSinceStartup;
                }
                IsPlaying = true;

                Log.Info("Loaded '{0}': {1}s, {2} channel(s), {3} Hz, load type {4}, state {5}.",
                    track.FileName,
                    clip.length.ToString("F1"),
                    clip.channels,
                    clip.frequency,
                    clip.loadType,
                    clip.loadState);

                var loaded = TrackLoaded;
                if (loaded != null)
                {
                    loaded(track, clip);
                }
            }

            IsLoading = false;
        }

        /// <summary>
        /// A clip Unity reports as loaded but which has no audio in it. Seeking one of
        /// these throws, and playing one finishes immediately.
        /// </summary>
        private static bool IsUsable(AudioClip clip)
        {
            return clip != null
                && clip.channels > 0
                && clip.frequency > 0
                && clip.length > 0f
                && clip.loadState != AudioDataLoadState.Unloaded;
        }

        private static string Describe(AudioClip clip)
        {
            return string.Format(
                "{0:F1}s, {1} channel(s), {2} Hz, load type {3}, state {4}",
                clip.length, clip.channels, clip.frequency, clip.loadType, clip.loadState);
        }

        /// <summary>
        /// Big files are streamed from disk rather than decompressed into memory: a
        /// long wave file can decode to several hundred megabytes, and only one clip
        /// is held at a time, so the whole library never has to fit in RAM but a single
        /// track still has to. Streaming keeps the footprint small at the cost of not
        /// being able to resume from a position.
        /// </summary>
        private static bool ShouldStream(MusicTrack track)
        {
            var threshold = Settings.Current.StreamAboveMegabytes;
            if (threshold < 0)
            {
                return true;
            }
            if (threshold == 0)
            {
                return false;
            }

            return SizeInMegabytes(track) > threshold;
        }

        private static double SizeInMegabytes(MusicTrack track)
        {
            try
            {
                var info = new FileInfo(track.Path);
                return info.Length / (1024.0 * 1024.0);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>
        /// Moves the playhead, tolerating formats that cannot be seeked rather than
        /// letting the exception escape into the audio thread.
        /// </summary>
        private static void Seek(AudioSource audio, AudioClip clip, double position)
        {
            var target = Mathf.Clamp((float)position, 0f, Mathf.Max(0f, clip.length - 0.05f));

            try
            {
                audio.time = target;
            }
            catch (Exception e)
            {
                Log.Info("Could not seek '{0}' to {1:F1}s, starting from the beginning. {2}",
                    clip.name, target, e.Message);
                audio.time = 0f;
            }
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

            if (fadeSource != null && fadeSource.isPlaying)
            {
                fadeSource.Pause();
            }
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

            if (fadeSource != null && fadingClip != null && !fadeSource.isPlaying)
            {
                fadeSource.UnPause();
            }
        }

        /// <summary>Stops and forgets the current track.</summary>
        internal void Stop()
        {
            EndFadeOut();

            if (source != null)
            {
                source.Stop();
                source.clip = null;
            }

            Current = null;
            CurrentClip = null;
            IsPlaying = false;
            IsPaused = false;
            fadeInProgress = false;
        }

        /// <summary>Stops the source but keeps the clip, so playback can resume later.</summary>
        internal void StopKeepingPosition()
        {
            EndFadeOut();

            if (source != null)
            {
                source.Stop();
            }
            IsPlaying = false;
            IsPaused = false;
        }

        private void Update()
        {
            UpdateFadeOut();

            if (source == null || CurrentClip == null || IsLoading)
            {
                return;
            }

            if (fadeInProgress)
            {
                ApplyVolume();
                if (FadeInGain >= 1f)
                {
                    fadeInProgress = false;
                }
            }

            // Give the player a chance to crossfade while the tail of the track is
            // still sounding. A repeat of the current song and queues that end are
            // still handled by TrackFinished, whatever the listener decides here.
            if (IsPlaying && !nearingEndRaised
                && CurrentClip.length - source.time <= CrossfadeSeconds)
            {
                nearingEndRaised = true;
                var nearEnd = TrackNearingEnd;
                if (nearEnd != null)
                {
                    nearEnd();
                }
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

        private void UpdateFadeOut()
        {
            if (fadingClip == null)
            {
                return;
            }

            var elapsed = Time.realtimeSinceStartup - fadeStartedAt;
            var progress = elapsed / CrossfadeSeconds;

            if (progress >= 1f || (elapsed > 0.5f && fadeSource != null && !fadeSource.isPlaying))
            {
                EndFadeOut();
                return;
            }

            if (fadeSource != null)
            {
                fadeSource.volume = Mathf.Clamp01(fadeFromVolume * (1f - progress));
            }
        }

        /// <summary>
        /// Hands the track that is playing to the second source, where it fades out
        /// over the crossfade window, and frees the main source for the next song.
        /// Returns false when there is nothing to fade, in which case a plain stop is
        /// just as good.
        /// </summary>
        private bool TryBeginFadeOut()
        {
            if (fadeSource == null || source == null || CurrentClip == null || !IsPlaying)
            {
                return false;
            }

            var fromVolume = source.volume;

            var position = Mathf.Clamp((float)source.time, 0f, Mathf.Max(0f, CurrentClip.length - 0.05f));

            fadeSource.clip = CurrentClip;
            fadeSource.volume = fromVolume;
            fadeSource.Play();
            try
            {
                fadeSource.time = position;
            }
            catch (Exception)
            {
                // Formats that cannot be seeked would replay from the beginning, which
                // is worse than a plain stop, so give up on the fade entirely.
                EndFadeOut();
                return false;
            }

            fadingClip = CurrentClip;
            fadeFromVolume = fromVolume;
            fadeStartedAt = Time.realtimeSinceStartup;

            // The main source forgets the track; the clip now belongs to the fade and
            // is only released once it has ended.
            fadeInProgress = false;
            source.Stop();
            source.clip = null;
            Current = null;
            CurrentClip = null;
            IsPlaying = false;
            IsPaused = false;
            return true;
        }

        /// <summary>Stops the fade out and releases the clip it was playing.</summary>
        private void EndFadeOut()
        {
            if (fadeSource != null && fadeSource.isPlaying)
            {
                fadeSource.Stop();
            }
            if (fadeSource != null)
            {
                fadeSource.clip = null;
            }
            if (fadingClip != null)
            {
                Destroy(fadingClip);
                fadingClip = null;
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
            EndFadeOut();
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