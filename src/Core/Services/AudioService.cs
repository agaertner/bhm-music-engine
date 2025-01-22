﻿using Blish_HUD;
using Blish_HUD.Extended;
using Nekres.Music_Mixer.Core.Services.Data;
using Nekres.Music_Mixer.Properties;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Nekres.Music_Mixer.Core.Services.Audio {
    internal class AudioService : IDisposable
    {
        public event EventHandler<ValueEventArgs<AudioSource>> MusicChanged;

        public AudioTrack AudioTrack { get; private set; }
        public bool       Loading    { get; private set; }

        private AudioSource _previousSource;
        private TimeSpan    _interuptedAt;

        private readonly TaskScheduler _scheduler;

        public AudioService() {
            AudioTrack = AudioTrack.Empty;

            _scheduler = TaskScheduler.FromCurrentSynchronizationContext();

            MusicMixer.Instance.Gw2State.IsSubmergedChanged += OnIsSubmergedChanged;
            MusicMixer.Instance.Gw2State.StateChanged       += OnStateChanged;

            GameService.GameIntegration.Gw2Instance.Gw2LostFocus     += OnGw2LostFocus;
            GameService.GameIntegration.Gw2Instance.Gw2AcquiredFocus += OnGw2AcquiredFocus;
            GameService.GameIntegration.Gw2Instance.Gw2Closed        += OnGw2Closed;
        }

        public async Task Play(AudioSource source) {
            if (this.Loading || source == null || source.IsEmpty || MusicMixer.Instance == null) {
                return;
            }
            this.Loading = true;

            if (string.IsNullOrEmpty(source.PageUrl)) {
                this.Loading = false;
                return;
            }

            YtDlpService.ErrorCode error = 0;
            if (string.IsNullOrEmpty(source.AudioUrl)) { // Direct audio url will be empty on first load every session.
                error = await MusicMixer.Instance.YtDlp.GetAudioOnlyUrl(source);
                if (error != YtDlpService.ErrorCode.Ratelimited) {
                    source.LastError = error;
                    MusicMixer.Instance.Data.Upsert(source);
                }
            }

            if (error > 0) {
                if (error == YtDlpService.ErrorCode.Unknown) {
                    this.Loading = false;
                    await NextSong(source.State);
                    return;
                }
                ErrorPrompt.Show(source.GetErrorMessage() + "\n\n" + Resources.Would_you_like_to_remove_this_track_from_all_playlists_, ErrorPrompt.DialogIcon.Exclamation, ErrorPrompt.DialogButtons.Yes | ErrorPrompt.DialogButtons.No,
                                 async bttn => {
                                     if (bttn == ErrorPrompt.DialogButtons.Yes) {
                                         MusicMixer.Instance.Data.Remove(source);
                                     } else {
                                         MusicMixer.Instance.Data.AddSourceToSkips(source.ExternalId); // Skip this session.
                                     }
                                     this.Loading = false;
                                     if (this.AudioTrack.IsEmpty) { // If this was a preview attempt there might be a track already playing. TODO: Separate load process for previews without state check.
                                         await NextSong(source.State);
                                     }
                                 });
                return;
            }

            await TryPlay(source);
            this.Loading = false;
        }

        private async Task TryPlay(AudioSource source) {
            if (source.State != MusicMixer.Instance.Gw2State.CurrentState) {
                return;
            }
            MusicMixer.Logger.Info("Playing: \"" + source.Title + "\" | " + source.PageUrl + " | Vol: " + Math.Round(source.Volume, 3) + " | " + source.State);
            // Making sure WasApiOut is initialized in main synchronization context. Otherwise it will fail.
            // https://github.com/naudio/NAudio/issues/425
            await Task.Factory.StartNew(async () => {
                var track = await AudioTrack.TryGetStream(source);
                if (track.IsEmpty) {
                    track.Dispose();
                }
                bool isPlaying = await track.Play();
                if (isPlaying) {
                    SetGameVolume(0.1f);
                    track.Finished  += OnSoundtrackFinished;
                    this.AudioTrack = track;
                    MusicChanged?.Invoke(this, new ValueEventArgs<AudioSource>(source));
                }
            }, CancellationToken.None, TaskCreationOptions.None, _scheduler).Unwrap();
        }

        public void Reset() {
            SetGameVolume(1);
            var track = this.AudioTrack;
            if (track != null) {
                track.Finished -= OnSoundtrackFinished;
                track.Dispose();
            }
            this.AudioTrack = AudioTrack.Empty;
            MusicChanged?.Invoke(this, new ValueEventArgs<AudioSource>(AudioSource.Empty));
        }

        private void SetGameVolume(float volume) {
            if (GameService.GameIntegration.Gw2Instance.Gw2IsRunning) {
                AudioUtil.SetVolume(GameService.GameIntegration.Gw2Instance.Gw2Process.Id, volume);
            }
        }

        private async void OnSoundtrackFinished(object o, EventArgs e) {
            Reset();
            await NextSong(MusicMixer.Instance.Gw2State.CurrentState);
        }

        public void Pause()
        {
            if (this.AudioTrack.IsEmpty) {
                return;
            }
            this.AudioTrack.Pause();
            SetGameVolume(1);
        }

        public void Resume()
        {
            if (this.AudioTrack.IsEmpty) {
                return;
            }
            this.AudioTrack.Resume();
            SetGameVolume(0.1f);
        }

        public void SaveContext()
        {
            if (this.AudioTrack.IsEmpty) {
                return;
            }

            _interuptedAt = this.AudioTrack.CurrentTime;
            _previousSource = this.AudioTrack.Source;
        }

        public async Task<bool> PlayFromSave()
        {
            // No save or saved time is out of duration bounds
            if (_previousSource == null || _interuptedAt > _previousSource.Duration) {
                return false;
            }

            if (!this.AudioTrack.IsEmpty && this.AudioTrack.Source.AudioUrl.Equals(_previousSource.AudioUrl)) {
                return true; // Song is already active
            }

            await Play(_previousSource);
            this.AudioTrack?.Seek(_interuptedAt);
            return true;
        }

        public void Dispose() {
            MusicMixer.Instance.Gw2State.IsSubmergedChanged          -= OnIsSubmergedChanged;
            MusicMixer.Instance.Gw2State.StateChanged                -= OnStateChanged;
            GameService.GameIntegration.Gw2Instance.Gw2LostFocus     -= OnGw2LostFocus;
            GameService.GameIntegration.Gw2Instance.Gw2AcquiredFocus -= OnGw2AcquiredFocus;
            GameService.GameIntegration.Gw2Instance.Gw2Closed        -= OnGw2Closed;

            this.AudioTrack?.Dispose();

            // Reset GW2 volume
            if (GameService.GameIntegration.Gw2Instance.Gw2IsRunning) {
                AudioUtil.SetVolume(GameService.GameIntegration.Gw2Instance.Gw2Process.Id, 1);
            }
        }

        private void OnGw2LostFocus(object o, EventArgs e) {
            if (!MusicMixer.Instance?.ModuleConfig.Value.MuteWhenInBackground ?? true) {
                return;
            }
            Pause();
        }

        private void OnGw2AcquiredFocus(object o, EventArgs e) {
            Resume();
        }

        private void OnGw2Closed(object o, EventArgs e) {
            Reset();
        }

        private async void OnStateChanged(object o, ValueChangedEventArgs<Gw2StateService.State> e) {
            if (MusicMixer.Instance.ModuleConfig.Value.PlayToCompletion && !this.AudioTrack.IsEmpty) {
                if (e.NewValue != Gw2StateService.State.Defeated) {
                    return; // Continue playing when changing states (except when defeated).
                }
                if (this.AudioTrack.Source.State == e.NewValue) {
                    return; // Continue playing when states match and play to completion is set.
                }
            }
            if (await PlayFromSave()) {
                return;
            }
            await NextSong(e.NewValue);
        }

        public async Task NextSong(Gw2StateService.State state) {
            this.Reset();
            Playlist context = state switch {
                Gw2StateService.State.Mounted  => MusicMixer.Instance.Data.GetMountPlaylist(GameService.Gw2Mumble.PlayerCharacter.CurrentMount),
                Gw2StateService.State.Defeated => MusicMixer.Instance.Data.GetDefeatedPlaylist(),
                _                              => null
            };
            if (context == null || context.IsEmpty || !context.Enabled) {
                return;
            }
            AudioSource audio = context.GetRandom((int)MusicMixer.Instance.Gw2State.TyrianTime);
            audio.State = state;
            await Play(audio);
        }

        private void OnIsSubmergedChanged(object o, ValueEventArgs<bool> e) {
            this.AudioTrack?.ToggleSubmergedFx(e.Value);
        }
    }
}
