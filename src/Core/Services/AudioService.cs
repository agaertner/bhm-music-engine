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

            this.Loading = true;

            if (string.IsNullOrEmpty(source.AudioUrl))
            {
                if (string.IsNullOrEmpty(source.Uri)) {
                    return;
                }

                MusicMixer.Instance.YtDlp.GetAudioOnlyUrl(source.Uri, AudioUrlReceived, source);
                return;
            }

            _currentSource = source;

            await TryPlay(source);
        }

        private async Task AudioUrlReceived(string url, AudioSource source)
        {
            try
            {
                source.AudioUrl = url;
                _currentSource = source;
                MusicMixer.Instance.Data.Upsert(source);
                await TryPlay(source);
            } catch (Exception e) when (e is NullReferenceException or ObjectDisposedException) {
                /* NOOP - Module was being unloaded. */
            }
        }

        private async Task<bool> TryPlay(AudioSource source, TimeSpan startTime = default) {
            // Making sure WasApiOut is initialized in main synchronization context. Otherwise it will fail.
            // https://github.com/naudio/NAudio/issues/425
            return await Task.Factory.StartNew(async () => {

                if (!AudioTrack.TryGetStream(source, out var newTrack)) {
                    this.Loading = false;
                    return false;
                }

                if (!this.AudioTrack.IsEmpty) {
                    this.AudioTrack.Finished -= OnSoundtrackFinished;
                    await this.AudioTrack.DisposeAsync();
                }

                if (GameService.GameIntegration.Gw2Instance.Gw2IsRunning) {
                    AudioUtil.SetVolume(GameService.GameIntegration.Gw2Instance.Gw2Process.Id, 0.1f);
                }

                this.AudioTrack          =  newTrack;
                this.AudioTrack.Muted    =  this.Muted;
                this.AudioTrack.Finished += OnSoundtrackFinished;
                this.AudioTrack.Play();

                MusicChanged?.Invoke(this, new ValueEventArgs<AudioSource>(_currentSource));

                this.Loading = false;
                return true;
            }, CancellationToken.None, TaskCreationOptions.None, _scheduler).Unwrap();
        }

        public void Stop()
        {
            if (GameService.GameIntegration.Gw2Instance.Gw2IsRunning) {
                AudioUtil.SetVolume(GameService.GameIntegration.Gw2Instance.Gw2Process.Id, 1);
            }

            this.AudioTrack?.Dispose();
        }

        private async void OnSoundtrackFinished(object o, EventArgs e)
        {
            _currentSource = null;
            if (this.Loading) {
                return;
            }

            if (MusicMixer.Instance.Gw2State.CurrentState == Gw2StateService.State.Mounted) {

                var mountType = (int)GameService.Gw2Mumble.PlayerCharacter.CurrentMount;
                var dayCycle  = (int)MusicMixer.Instance.Gw2State.TyrianTime;

                var context = MusicMixer.Instance.Data.GetContextMount(mountType, dayCycle);

                await Play(context.GetRandom());
                return;
            }

            if (MusicMixer.Instance.Gw2State.CurrentState == Gw2StateService.State.Ambient) {
                var dayCycle = (int)MusicMixer.Instance.Gw2State.TyrianTime;
                var context  = MusicMixer.Instance.Data.GetContextLocation(GameService.Gw2Mumble.CurrentMap.Id, dayCycle);

                await Play(context.GetRandom());
            }
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
            this.AudioTrack?.Dispose();

            MusicMixer.Instance.Gw2State.IsSubmergedChanged -= OnIsSubmergedChanged;
            MusicMixer.Instance.Gw2State.StateChanged       -= OnStateChanged;
        }

        private void OnGw2LostFocus(object o, EventArgs e) {
            if (!MusicMixer.Instance.MuteWhenInBackgroundSetting.Value) {
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
            switch (e.PreviousValue) {
                case Gw2StateService.State.Mounted:
                case Gw2StateService.State.Battle:
                case Gw2StateService.State.Submerged:
                case Gw2StateService.State.Victory:
                    Stop();
                    break;
                case Gw2StateService.State.Ambient:
                    SaveContext();
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
                    var mountType = (int)GameService.Gw2Mumble.PlayerCharacter.CurrentMount;

                    var context = MusicMixer.Instance.Data.GetContextMount(mountType, dayCycle);
                    await Play(context.GetRandom());
                    break;
                case Gw2StateService.State.Ambient:
                    var context2 = MusicMixer.Instance.Data.GetContextLocation(GameService.Gw2Mumble.CurrentMap.Id, dayCycle);
                    await Play(context2.GetRandom());
                    break;
            }
        }

        private void OnIsSubmergedChanged(object o, ValueEventArgs<bool> e) {
            this.AudioTrack?.ToggleSubmergedFx(e.Value);
        }
    }
}
