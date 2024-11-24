﻿using Blish_HUD;
using Nekres.Music_Mixer.Core.Services.Data;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Nekres.Music_Mixer.Core.Services.Audio {
    internal class AudioService : IDisposable
    {
        public event EventHandler<ValueEventArgs<AudioSource>> MusicChanged;

        public AudioTrack AudioTrack { get; private set; }
        public bool       Loading    { get; private set; }

        private          AudioSource   _currentSource;
        private          AudioSource   _previousSource;
        private          TimeSpan      _interuptedAt;

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
            if (this.Loading || source == null || source.IsEmpty) {
                return;
            }
            this.Loading = true;

            if (string.IsNullOrEmpty(source.PageUrl)) {
                this.Loading = false;
                return;
            }

            if (string.IsNullOrEmpty(source.AudioUrl)) {
                source.AudioUrl = await MusicMixer.Instance.YtDlp.GetAudioOnlyUrl(source.PageUrl);
                MusicMixer.Instance?.Data.Upsert(source);
            }
            
            _currentSource = source;
            await TryPlay(source);
            this.Loading = false;
        }

        private async Task TryPlay(AudioSource source) {
            MusicMixer.Logger.Info("Playing: \"" + source.Title + "\" | " + source.PageUrl + " | Vol: " + Math.Round(source.Volume, 3) + " | " + source.State);
            // Making sure WasApiOut is initialized in main synchronization context. Otherwise it will fail.
            // https://github.com/naudio/NAudio/issues/425
            await Task.Factory.StartNew(async () => {
                var track = await AudioTrack.TryGetStream(source);

                if (track.IsEmpty || MusicMixer.Instance == null 
                                  || MusicMixer.Instance.Gw2State == null 
                                  || source.State != MusicMixer.Instance.Gw2State.CurrentState) {
                    track.Dispose();
                }

                if (!this.AudioTrack.IsEmpty) {
                    this.AudioTrack.Finished -= OnSoundtrackFinished;
                    await this.AudioTrack.DisposeAsync();
                }

                this.AudioTrack          =  track;
                this.AudioTrack.Finished += OnSoundtrackFinished;

                await this.AudioTrack.Play();

                MusicChanged?.Invoke(this, new ValueEventArgs<AudioSource>(_currentSource));
                SetGameVolume(0.1f);
            }, CancellationToken.None, TaskCreationOptions.None, _scheduler).Unwrap();
        }

        public void Stop() {
            SetGameVolume(1);

            this.AudioTrack?.Dispose();
            this.AudioTrack = AudioTrack.Empty;
            _currentSource = AudioSource.Empty;
            MusicChanged?.Invoke(this, new ValueEventArgs<AudioSource>(_currentSource));
        }

        private void SetGameVolume(float volume) {
            if (GameService.GameIntegration.Gw2Instance.Gw2IsRunning) {
                AudioUtil.SetVolume(GameService.GameIntegration.Gw2Instance.Gw2Process.Id, volume);
            }
        }

        private async void OnSoundtrackFinished(object o, EventArgs e) {
            _currentSource = AudioSource.Empty;
            MusicChanged?.Invoke(this, new ValueEventArgs<AudioSource>(_currentSource));
            await NextSong(MusicMixer.Instance.Gw2State.CurrentState);
        }

        public void Pause()
        {
            if (this.AudioTrack == null || this.AudioTrack.IsEmpty) {
                return;
            }
            this.AudioTrack.Pause();
            SetGameVolume(1);
        }

        public void Resume()
        {
            if (this.AudioTrack == null || this.AudioTrack.IsEmpty) {
                return;
            }
            this.AudioTrack.Resume();
            SetGameVolume(0.1f);
        }

        public void SaveContext()
        {
            if (this.AudioTrack.IsEmpty || _currentSource == null) {
                return;
            }

            _interuptedAt = this.AudioTrack.CurrentTime;
            _previousSource = _currentSource;
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
            Stop();
        }

        private async void OnStateChanged(object o, ValueChangedEventArgs<Gw2StateService.State> e) {
            // Don't change song if already playing one for mounted.
            if (MusicMixer.Instance.ModuleConfig.Value.PlayToCompletion 
             && e.NewValue == Gw2StateService.State.Mounted 
             && this.AudioTrack is {IsEmpty: false, IsBuffering: false} ) {
                return;
            }
            // Change song.
            if (e.NewValue is Gw2StateService.State.Mounted or Gw2StateService.State.Defeated) {
                if (await PlayFromSave()) {
                    return;
                }
                await NextSong(e.NewValue);
                return;
            }
            // Stop song if appropriate.
            if (MusicMixer.Instance.ModuleConfig.Value.PlayToCompletion 
             && e.PreviousValue == Gw2StateService.State.Mounted) {
                return; // Continue playing when dismounting and play to completion is set.
            }
            Stop(); // Stop song when in standby or other states.
        }

        public async Task NextSong(Gw2StateService.State state) {
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
