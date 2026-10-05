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
                state = new PlayerState(vesselId);
                states[vesselId] = state;
            }
            return state;
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

            state.Queue = new List<MusicTrack>(view);
            state.QueueIndex = index;
            state.UserPaused = false;
            state.ContextPaused = false;
            state.AutoResumeOnReturn = false;
            state.ResetPosition();
            CurrentState = state;

            if (state.Shuffle)
            {
                ShuffleRemaining(state);
            }

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
            if (state == null || !state.HasQueue)
            {
                return;
            }

            int next = state.QueueIndex + direction;

            if (next >= state.Queue.Count)
            {
                if (state.Repeat == RepeatMode.One)
                {
                    next = state.QueueIndex;
                }
                else if (state.Repeat == RepeatMode.All)
                {
                    next = 0;
                }
                else
                {
                    StopAtEndOfQueue(state);
                    return;
                }
            }
            else if (next < 0)
            {
                next = state.Repeat == RepeatMode.All ? state.Queue.Count - 1 : 0;
            }

            StartQueueIndex(state, next);
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

            if (state.Repeat == RepeatMode.One && state.Current != null)
            {
                state.ResetPosition();
                Audio.Play(state.Current, 0);
                RaisePlaybackChanged();
                return;
            }

            int next = state.QueueIndex + 1;
            if (next >= state.Queue.Count)
            {
                if (state.Repeat == RepeatMode.All && state.Queue.Count > 0)
                {
                    next = 0;
                }
                else
                {
                    // Queue finished: stay on the last track instead of looping.
                    StopAtEndOfQueue(state);
                    return;
                }
            }

            StartQueueIndex(state, next);
        }

        private void OnTrackLoaded(MusicTrack track, AudioClip clip)
        {
            RaisePlaybackChanged();
        }

        private void StartQueueIndex(PlayerState state, int index)
        {
            state.QueueIndex = index;
            state.UserPaused = false;
            state.ContextPaused = false;
            state.AutoResumeOnReturn = false;
            state.ResetPosition();
            Audio.Play(state.Current, 0);
            RaisePlaybackChanged();
        }

        /// <summary>
        /// Randomises the order of the songs still to come, leaving the current track
        /// where it is so playback is not interrupted.
        /// </summary>
        private static void ShuffleRemaining(PlayerState state)
        {
            for (int i = state.Queue.Count - 1; i > state.QueueIndex + 1; i--)
            {
                int j = UnityEngine.Random.Range(state.QueueIndex + 1, i + 1);
                var swap = state.Queue[i];
                state.Queue[i] = state.Queue[j];
                state.Queue[j] = swap;
            }
        }

        internal void ToggleShuffle()
        {
            var state = StateForActiveVessel();
            if (state == null)
            {
                return;
            }

            state.Shuffle = !state.Shuffle;
            if (state.Shuffle)
            {
                ShuffleRemaining(state);
            }
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
            }
        }
    }
}