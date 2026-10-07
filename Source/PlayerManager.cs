using System;
using System.Collections.Generic;
using UnityEngine;

namespace RPMusicPlayer
{
    /// <summary>
    /// The heart of the plugin: owns the library and the audio engine, keeps one
    /// <see cref="PlayerState"/> per vessel, and pauses playback whenever the player
    /// is not actually flying the vessel the queue belongs to.
    /// </summary>
    public sealed class PlayerManager : MonoBehaviour
    {
        private static PlayerManager instance;

        internal static PlayerManager Instance
        {
            get { return instance; }
        }

        internal MusicLibrary Library { get; private set; }
        internal AudioEngine Audio { get; private set; }

        private readonly Dictionary<Guid, PlayerState> states = new Dictionary<Guid, PlayerState>();

        private bool scanRequested = true;
        private bool scanRunning;

        // Page wiring: which vessel we are patching, and how often to retry.
        private float nextInjectAttempt;
        private Guid knownVessel = Guid.Empty;
        private float inVesselSince = -1f;
        private bool warnedNoMonitors;

        // Context tracking: the vessel and pod state playback was last settled for.
        private Guid lastVesselId = Guid.Empty;
        private bool lastInsidePodForPause;

        /// <summary>Raised when the track list changes, so open pages can redraw.</summary>
        internal event Action LibraryChanged;

        /// <summary>Raised when playback state changes.</summary>
        internal event Action PlaybackChanged;

        /// <summary>The vessel the player is currently controlling, if any.</summary>
        internal PlayerState CurrentState { get; private set; }

        internal void Initialize()
        {
            Library = new MusicLibrary();
            Audio = gameObject.AddComponent<AudioEngine>();
            Audio.Initialize();
            Audio.RefreshVolume();
            Audio.TrackFinished += OnTrackFinished;
            Audio.TrackLoaded += OnTrackLoaded;
            Audio.TrackNearingEnd += OnTrackNearingEnd;
        }

        internal void Tick()
        {
            if (scanRequested && Settings.Current.ScanOnStart)
            {
                scanRequested = false;
                Library.BeginScan();
                scanRunning = Library.IsScanning;
            }

            if (scanRunning)
            {
                // Spread the file reads over several frames so loading does not hitch the game.
                scanRunning = !Library.ScanStep(24);
                if (!scanRunning)
                {
                    RaiseLibraryChanged();
                }
            }

            UpdateContext();
            UpdatePages();
        }

        // ---------------------------------------------------------------- page wiring

        /// <summary>
        /// Keeps the music pages attached to the monitors of whatever vessel is loaded.
        /// Runs on the persistent host, so it does not matter which scenes have addons.
        /// </summary>
        private void UpdatePages()
        {
            if (!FlightContext.HasActiveVessel)
            {
                inVesselSince = -1f;
                return;
            }

            var vessel = FlightContext.Vessel;

            // Unity reuses instance ids, so forget the old monitors when the vessel changes.
            if (vessel.id != knownVessel)
            {
                knownVessel = vessel.id;
                PageInjector.Reset();
                warnedNoMonitors = false;
            }

            if (inVesselSince < 0f)
            {
                inVesselSince = Time.realtimeSinceStartup;
                Log.Info("Vessel '{0}' available, looking for RasterPropMonitor screens.", vessel.vesselName);
            }

            if (Time.realtimeSinceStartup >= nextInjectAttempt)
            {
                nextInjectAttempt = Time.realtimeSinceStartup + 0.5f;
                var patched = PageInjector.InjectAll();

                // Having an internal model but no usable monitor usually means the IVA
                // is from a mod whose RasterPropMonitor has not started up yet.
                if (patched == 0 && !warnedNoMonitors && Time.realtimeSinceStartup - inVesselSince > 5f)
                {
                    warnedNoMonitors = true;
                    Log.Warning("In a vessel but no RasterPropMonitor pages could be added. Check that the IVA has a monitor screen.");
                }
            }
        }

        /// <summary>
        /// Creates the object that survives scene changes, holding the library and the
        /// audio engine. Called from every addon instance, but only the first one does
        /// any work.
        /// </summary>
        internal static PlayerManager EnsureHost()
        {
            if (instance != null)
            {
                return instance;
            }

            var host = new GameObject("RPMusicPlayer");
            UnityEngine.Object.DontDestroyOnLoad(host);

            instance = host.AddComponent<PlayerManager>();
            instance.Initialize();

            Log.Info("Started. Music folder: {0}", Settings.Current.MusicPath);
            return instance;
        }

        // ---------------------------------------------------------------- context

        private void UpdateContext()
        {
            var vessel = FlightContext.Vessel;
            var inFlight = FlightContext.HasActiveVessel;
            var vesselId = inFlight ? vessel.id : Guid.Empty;

            var podChanged = Settings.Current.PauseWhenOutsideIva
                && FlightContext.InsidePod != lastInsidePodForPause;
            lastInsidePodForPause = FlightContext.InsidePod;

            if (vesselId == lastVesselId && !podChanged)
            {
                return;
            }

            var vesselChanged = vesselId != lastVesselId;
            lastVesselId = vesselId;

            if (vesselChanged)
            {
                if (vesselId != Guid.Empty)
                {
                    var state = StateFor(vesselId);
                    var returning = CurrentState != null && CurrentState.VesselId == vesselId;
                    CurrentState = state;

                    if (!returning)
                    {
                        RestoreContext();
                    }
                }
                else
                {
                    // Ground, map view or no vessel under control: stop, but remember where.
                    SuspendCurrent(true);
                    CurrentState = null;
                }
            }

            // Optional: treat stepping out of the cockpit as a pause too. This is the
            // closest stand in for "the kerbals are outside", see the README.
            if (podChanged && !FlightContext.InsidePod)
            {
                SuspendCurrent(false);
                if (CurrentState != null)
                {
                    CurrentState.UserPaused = true;
                    CurrentState.AutoResumeOnReturn = false;
                }
            }

            RaisePlaybackChanged();
        }

        /// <summary>
        /// Stops playback for the current vessel, remembering the position so that it
        /// can pick up again later.
        /// </summary>
        private void SuspendCurrent(bool autoResumeOnReturn)
        {
            var state = CurrentState;
            if (state == null || state.Current == null)
            {
                return;
            }

            state.CapturePosition(Audio.Position);
            Audio.StopKeepingPosition();
            state.ContextPaused = true;
            state.AutoResumeOnReturn = autoResumeOnReturn;
        }

        /// <summary>
        /// Picks playback back up when the player returns to a vessel that was playing.
        /// A vessel that was paused by going on EVA stays paused.
        /// </summary>
        private void RestoreContext()
        {
            var state = CurrentState;
            if (state == null || !state.ContextPaused)
            {
                return;
            }

            state.ContextPaused = false;

            if (state.Current != null && !state.UserPaused && state.AutoResumeOnReturn)
            {
                var resumeAt = state.ResumePosition;
                state.ResetPosition();
                Audio.Play(state.Current, resumeAt);
            }

            state.AutoResumeOnReturn = false;
        }

        private void RaisePlaybackChanged()
        {
            var handler = PlaybackChanged;
            if (handler != null)
            {
                handler();
            }
        }

        private void RaiseLibraryChanged()
        {
            var handler = LibraryChanged;
            if (handler != null)
            {
                handler();
            }
        }

        internal PlayerState StateFor(Guid vesselId)
        {
            PlayerState state;
            if (!states.TryGetValue(vesselId, out state))
            {
                // The queue draws its randomness from here so it can be handed a
                // deterministic source in tests instead of the game's.
                var playback = new PlaybackQueue(ShuffledIndex);
                state = new PlayerState(vesselId, playback);
                states[vesselId] = state;
            }
            return state;
        }

        /// <summary>A uniform random integer in [min, max), the way shuffle needs it.</summary>
        private static int ShuffledIndex(int min, int max)
        {
            return UnityEngine.Random.Range(min, max);
        }

        /// <summary>
        /// The state of the vessel being flown right now, or null on the ground.
        /// </summary>
        internal PlayerState StateForActiveVessel()
        {
            if (!FlightContext.HasActiveVessel)
            {
                return null;
            }
            return StateFor(FlightContext.Vessel.id);
        }

        // ---------------------------------------------------------------- playback

        /// <summary>
        /// Plays a song and queues up the rest of the view it was picked from.
        /// </summary>
        internal void PlayFromView(IList<MusicTrack> view, int index)
        {
            var state = StateForActiveVessel();
            if (state == null || view == null || index < 0 || index >= view.Count)
            {
                return;
            }

            state.Playback.Load(view, index);
            state.UserPaused = false;
            state.ContextPaused = false;
            state.AutoResumeOnReturn = false;
            state.ResetPosition();
            CurrentState = state;

            Audio.Play(state.Current, 0);
            RaisePlaybackChanged();
        }

        internal void TogglePause()
        {
            var state = CurrentState;
            if (state == null || state.Current == null || Audio.IsLoading)
            {
                return;
            }

            if (Audio.IsPlaying)
            {
                state.CapturePosition(Audio.Position);
                state.UserPaused = true;
                Audio.Pause();
            }
            else
            {
                state.UserPaused = false;
                state.ContextPaused = false;
                state.AutoResumeOnReturn = false;

                // Reuse the clip we already decoded unless the source was torn down.
                if (Audio.CurrentClip != null && ReferenceEquals(Audio.Current, state.Current) && Audio.IsPaused)
                {
                    state.ResetPosition();
                    Audio.Resume();
                }
                else
                {
                    Audio.Play(state.Current, state.ResumePosition);
                }
            }

            RaisePlaybackChanged();
        }

        internal void Next()
        {
            Step(1);
        }

        internal void Previous()
        {
            Step(-1);
        }

        private void Step(int direction)
        {
            var state = CurrentState;
            if (state == null)
            {
                return;
            }

            if (!state.Playback.TryStep(direction))
            {
                StopAtEndOfQueue(state);
                return;
            }

            StartCurrent(state);
        }

        private void StopAtEndOfQueue(PlayerState state)
        {
            state.ResetPosition();
            state.UserPaused = true;
            Audio.StopKeepingPosition();
            RaisePlaybackChanged();
        }

        private void OnTrackFinished(MusicTrack finished)
        {
            var state = CurrentState;
            if (state == null)
            {
                return;
            }

            if (!state.Playback.TryAdvance())
            {
                // Queue finished: stay on the last track instead of looping.
                StopAtEndOfQueue(state);
                return;
            }

            StartCurrent(state);
        }

        /// <summary>
        /// Crossfade: the current track is inside its last few seconds, so start the
        /// next one now, on top of the one that is fading out. Repeating one song and
        /// a queue that has ended still finish normally.
        /// </summary>
        private void OnTrackNearingEnd()
        {
            var state = CurrentState;
            if (state == null || !state.Crossfade || Audio.IsLoading)
            {
                return;
            }

            if (!state.Playback.CanCrossfade())
            {
                return;
            }

            // The queue only moves once the new track is actually playing, so the
            // crossfade is started here and the index is committed by the load.
            state.Playback.TryAdvance();
            StartCurrent(state);
        }

        /// <summary>
        /// A song that will not load is skipped rather than left in the queue: the
        /// player used to sit on it in silence, and with repeat on it could come
        /// back round and fail again. Consecutive failures are counted so a library
        /// the engine cannot decode at all stops instead of looping.
        /// </summary>
        private int loadFailures;

        private void OnTrackLoaded(MusicTrack track, AudioClip clip)
        {
            if (clip == null)
            {
                // SkipFailedTrack reports whether it already redrew the page.
                if (SkipFailedTrack(track))
                {
                    return;
                }
            }
            else
            {
                loadFailures = 0;
            }

            RaisePlaybackChanged();
        }

        /// <summary>
        /// Steps past a song that failed to load. Returns true when the next song
        /// was started, false when there is nowhere left to go.
        ///
        /// If the crossfade put the previous song back instead, the queue is moved
        /// back to match what is actually playing: advancing would start the next
        /// song on top of it, and the queue would disagree with the audio.
        /// </summary>
        private bool SkipFailedTrack(MusicTrack failed)
        {
            var state = CurrentState;
            if (state == null || !ReferenceEquals(state.Current, failed))
            {
                return false;
            }

            // A queue full of undecodable files would otherwise cycle forever.
            const int GiveUpAfter = 3;
            if (++loadFailures >= GiveUpAfter)
            {
                Log.Error("'{0}' would not load and the last few songs did not either; stopping.",
                    failed.FileName);
                StopAtEndOfQueue(state);
                return false;
            }

            Log.Warning("Skipping '{0}'.", failed.FileName);

            if (Audio.ResumedAfterFailure)
            {
                // Step back onto the song that is still sounding so the queue and
                // the audio agree; it advances on its own when it finishes.
                state.Playback.TryStep(-1);
                return false;
            }

            if (!state.Playback.TryAdvance())
            {
                StopAtEndOfQueue(state);
                return false;
            }

            StartCurrent(state);
            return true;
        }

        /// <summary>
        /// Starts whatever the queue points at now. With crossfade on, what is still
        /// playing is handed to a second source and fades out underneath; Audio.Play
        /// falls back to a plain stop when nothing is sounding.
        /// </summary>
        private void StartCurrent(PlayerState state)
        {
            state.UserPaused = false;
            state.ContextPaused = false;
            state.AutoResumeOnReturn = false;
            state.ResetPosition();

            Audio.Play(state.Current, 0, state.Crossfade && Audio.IsPlaying);
            RaisePlaybackChanged();
        }

        internal void ToggleShuffle()
        {
            var state = StateForActiveVessel();
            if (state == null)
            {
                return;
            }

            state.Playback.ToggleShuffle();
            RaisePlaybackChanged();
        }

        internal void ToggleCrossfade()
        {
            var state = StateForActiveVessel();
            if (state == null)
            {
                return;
            }

            state.Crossfade = !state.Crossfade;
            RaisePlaybackChanged();
        }

        internal void CycleRepeat()
        {
            var state = StateForActiveVessel();
            if (state == null)
            {
                return;
            }

            state.Repeat = state.Repeat == RepeatMode.Off
                ? RepeatMode.All
                : state.Repeat == RepeatMode.All ? RepeatMode.One : RepeatMode.Off;

            RaisePlaybackChanged();
        }

        internal void AdjustVolume(float delta)
        {
            var settings = Settings.Current;
            settings.Volume = Mathf.Clamp01(settings.Volume + delta);
            Audio.RefreshVolume();
            RaisePlaybackChanged();
        }

        internal void RescanLibrary()
        {
            Library.BeginScan();
            scanRequested = false;
            scanRunning = Library.IsScanning;
            RaiseLibraryChanged();
        }

        private void Update()
        {
            Tick();
        }

        private void OnDestroy()
        {
            if (Audio != null)
            {
                Audio.TrackFinished -= OnTrackFinished;
                Audio.TrackLoaded -= OnTrackLoaded;
                Audio.TrackNearingEnd -= OnTrackNearingEnd;
            }
        }
    }
}