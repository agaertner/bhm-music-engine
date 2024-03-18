using Blish_HUD;
using Nekres.Music_Mixer.Core.Services.Data;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Nekres.Music_Mixer.Core.Services.Audio {
    internal class AudioService : IDisposable
    {
        public event EventHandler<ValueEventArgs<AudioSource>> MusicChanged;

        private bool _muted;
        public bool Muted
        {
            get => _muted;
            set {
                _muted = value;

                if (!this.AudioTrack.IsEmpty) {
                    this.AudioTrack.Muted = value;
                }
            }
        }

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
            if (this.Loading || source == null) {
                return;
            }

            if (string.IsNullOrEmpty(source.PageUrl)) {
                return;
            }

            if (string.IsNullOrEmpty(source.AudioUrl)) {
                source.AudioUrl = await MusicMixer.Instance.YtDlp.GetAudioOnlyUrl(source.PageUrl);
                MusicMixer.Instance.Data.Upsert(source);
            }
            
            _currentSource = source;
            await TryPlay(source);
        }

        private async Task<bool> TryPlay(AudioSource source) {
            this.Loading = true;

            try {
                // Making sure WasApiOut is initialized in main synchronization context. Otherwise it will fail.
                // https://github.com/naudio/NAudio/issues/425
                return await Task.Factory.StartNew(async () => {

                    var track = await AudioTrack.TryGetStream(source);
                    if (track.IsEmpty || (MusicMixer.Instance?.Gw2State?.CurrentState ?? 0) == Gw2StateService.State.StandBy) {
                        track.Dispose();
                        return false;
                    }

                    if (!this.AudioTrack.IsEmpty) {
                        this.AudioTrack.Finished -= OnSoundtrackFinished;
                        await this.AudioTrack.DisposeAsync();
                    }

                    if (GameService.GameIntegration.Gw2Instance.Gw2IsRunning) {
                        AudioUtil.SetVolume(GameService.GameIntegration.Gw2Instance.Gw2Process.Id, 0.1f);
                    }

                    this.AudioTrack          =  track;
                    this.AudioTrack.Muted    =  this.Muted;
                    this.AudioTrack.Finished += OnSoundtrackFinished;

                    await this.AudioTrack.Play();

                    MusicChanged?.Invoke(this, new ValueEventArgs<AudioSource>(_currentSource));

                    return true;
                }, CancellationToken.None, TaskCreationOptions.None, _scheduler).Unwrap();
            } finally {
                this.Loading = false;
            }
        }

        public void Stop()
        {
            if (GameService.GameIntegration.Gw2Instance.Gw2IsRunning) {
                AudioUtil.SetVolume(GameService.GameIntegration.Gw2Instance.Gw2Process.Id, 1);
            }

            this.AudioTrack?.Dispose();
            this.AudioTrack = AudioTrack.Empty;
        }

        private async void OnSoundtrackFinished(object o, EventArgs e)
        {
            _currentSource = null;
            await ChangeContext(MusicMixer.Instance.Gw2State.CurrentState);
        }

        public void Pause()
        {
            this.AudioTrack?.Pause();
        }

        public void Resume()
        {
            this.AudioTrack?.Resume();
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

            if (await TryPlay(_previousSource)) {
                this.AudioTrack?.Seek(_interuptedAt);
                return true;
            };

            return false;
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
            if (!MusicMixer.Instance?.MuteWhenInBackground.Value ?? true) {
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
            if (e.NewValue == Gw2StateService.State.StandBy) {
                Stop();
            }

            switch (e.PreviousValue) {
                case Gw2StateService.State.Mounted:
                case Gw2StateService.State.Battle:
                    Stop(); 
                    break;
            }

            if (await PlayFromSave()) {
                return;
            }

            // Select new song if nothing is playing.
            await ChangeContext(e.NewValue);
        }

        private async Task ChangeContext(Gw2StateService.State state) {
            var dayCycle = (int)MusicMixer.Instance.Gw2State.TyrianTime;
            switch (state) {
                case Gw2StateService.State.Mounted:

                    if (!MusicMixer.Instance.Data.GetMountPlaylist(GameService.Gw2Mumble.PlayerCharacter.CurrentMount, out var context)) {
                        return;
                    }

                    if (context.IsEmpty || !context.Enabled) {
                        return;
                    }

                    await Play(context.GetRandom(dayCycle));
                    break;
                case Gw2StateService.State.Defeated:
                    if (!MusicMixer.Instance.Data.GetDefeatedPlaylist(out var context2)) {
                        return;
                    }

                    if (context2.IsEmpty || !context2.Enabled) {
                        return;
                    }

                    await Play(context2.GetRandom(dayCycle));
                    break;
                default: break;
            }
        }

        private void OnIsSubmergedChanged(object o, ValueEventArgs<bool> e) {
            this.AudioTrack?.ToggleSubmergedFx(e.Value);
        }
    }
}
