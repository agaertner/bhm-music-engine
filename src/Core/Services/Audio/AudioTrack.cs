using Blish_HUD;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Nekres.Music_Mixer.Core.Services.Audio.Source;
using Nekres.Music_Mixer.Core.Services.Audio.Source.DSP;
using Nekres.Music_Mixer.Core.Services.Audio.Source.Equalizer;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Nekres.Music_Mixer.Core.Services.Data;

namespace Nekres.Music_Mixer.Core.Services.Audio {
    internal class AudioTrack : IDisposable {

        public static   AudioTrack Empty = new();
        public readonly bool       IsEmpty;

        public event EventHandler<EventArgs> Finished;

        private bool _muted;
        public bool Muted
        {
            get => _muted;
            set {
                _muted = value;

                if (_volumeProvider != null) {
                    _volumeProvider.Volume = value ? 0 : Source.Volume;
                }
            }
        }

        public readonly AudioSource Source;
        public          TimeSpan    CurrentTime => _mediaProvider.CurrentTime;
        public          TimeSpan    TotalTime   => _mediaProvider.TotalTime;
        public          bool        IsBuffering => _endOfStream.IsBuffering;

        private readonly WasapiOut               _outputDevice;
        private readonly MediaFoundationReader   _mediaProvider;  // Web stream
        private readonly EndOfStreamProvider     _endOfStream;    // Finish event
        private readonly SubmergedVolumeProvider _volumeProvider; // Volume control
        private readonly FadeInOutSampleProvider _fadeInOut;
        private readonly BiQuadFilterSource      _lowPassFilter; // Submerged SFX
        private readonly Equalizer               _equalizer;

        private bool _initialized;

        private AudioTrack() {
            IsEmpty = true;
        }

        private AudioTrack(AudioSource source)
        {
            Source = source;

            _outputDevice  = new WasapiOut(GameService.GameIntegration.Audio.AudioDevice, AudioClientShareMode.Shared, false, 100);
            _mediaProvider = new MediaFoundationReader(Source.AudioUrl);
            

            _endOfStream = new EndOfStreamProvider(_mediaProvider);
            _endOfStream.Ended += OnEndOfStreamReached;

            _volumeProvider = new SubmergedVolumeProvider(_endOfStream) {
                Volume = Source.Volume
            };
            Source.VolumeChanged += OnVolumeChanged;

            _fadeInOut = new FadeInOutSampleProvider(_volumeProvider);

            // Filter is toggled when submerged.
            _lowPassFilter = new BiQuadFilterSource(_fadeInOut)
            {
                Filter = new LowPassFilter(_fadeInOut.WaveFormat.SampleRate, 400)
            };
            _equalizer = Equalizer.Create10BandEqualizer(_lowPassFilter);
        }

        public static bool TryGetStream(AudioSource source, out AudioTrack soundTrack, int retries = 3, Logger logger = null)
        {
            if (string.IsNullOrWhiteSpace(source.AudioUrl)) {
                soundTrack = AudioTrack.Empty;
                return false;
            }

            logger ??= Logger.GetLogger(typeof(AudioTrack));
            try {
                soundTrack = new AudioTrack(source);
                return true;
            } catch (Exception e) {
                if (retries > 0) {
                    logger.Warn(e, $"Failed to create audio output stream. Remaining retries: {retries}.");
                    return TryGetStream(source, out soundTrack, --retries, logger);
                }

                switch (e) {
                    case InvalidCastException:
                        break;
                    case UnauthorizedAccessException:
                        break;
                    case COMException:
                        break;
                    default: break;
                }

                logger.Warn(e, e.Message);
            }
            soundTrack = AudioTrack.Empty;
            return false;
        }

        public void Play(int fadeInDuration = 500, int retries = 3, Logger logger = null)
        {
            logger ??= Logger.GetLogger(typeof(AudioTrack));
            try {

                if (!_initialized) {
                    _initialized = true;
                    _outputDevice.Init(_equalizer);
                }
                _outputDevice.Play();
                _fadeInOut.BeginFadeIn(fadeInDuration);
                this.ToggleSubmergedFx(MusicMixer.Instance.Gw2State.IsSubmerged);

            } catch (Exception e) {

                if (retries > 0) {
                    Play(fadeInDuration, --retries, logger);
                }

                switch (e) {
                    case InvalidCastException:
                        break;
                    case UnauthorizedAccessException:
                        break;
                    case COMException:
                        break;
                    default: break;
                }

                logger.Warn(e, e.Message);
            }
        }

        public void Seek(float seconds)
        {
            if (IsEmpty) {
                return;
            }

            _mediaProvider?.SetPosition(seconds);
        }

        public void Seek(TimeSpan timespan)
        {
            if (IsEmpty) {
                return;
            }

            _mediaProvider?.SetPosition(timespan);
        }

        public void Pause()
        {
            if (IsEmpty) {
                return;
            }

            if (_outputDevice == null || _outputDevice.PlaybackState == PlaybackState.Paused) {
                return;
            }

            _outputDevice.Pause();
        }

        public void Resume()
        {
            if (IsEmpty) {
                return;
            }

            if (_outputDevice == null || _outputDevice.PlaybackState != PlaybackState.Paused) {
                return;
            }

            _outputDevice.Play();
        }

        public void ToggleSubmergedFx(bool enable)
        {
            if (IsEmpty) {
                return;
            }

            if (_equalizer == null) {
                return;
            }

            _lowPassFilter.Enabled = enable;
            _volumeProvider.Enabled = enable;
            _equalizer.SampleFilters[1].AverageGainDB = enable ? 19.5f : 0; // Bass
            _equalizer.SampleFilters[9].AverageGainDB = enable ? 13.4f : 0; // Treble
        }

        public void Dispose()
        {
            if (IsEmpty) {
                return;
            }

            _endOfStream.Ended -= OnEndOfStreamReached;
            DisposeMediaInterfaces();
        }

        public async Task DisposeAsync() {
            if (IsEmpty) {
                return;
            }

            _endOfStream.Ended -= OnEndOfStreamReached;
            _fadeInOut.BeginFadeOut(2000);
            await Task.Delay(2005).ContinueWith(_ => {
                DisposeMediaInterfaces();
            });
        }

        private void DisposeMediaInterfaces() {
            Source.VolumeChanged -= OnVolumeChanged;

            try {
                _outputDevice?.Dispose();
                _mediaProvider?.Dispose();
            } catch (Exception e) when (e is NullReferenceException or ObjectDisposedException) {
                /* NOOP - Module was unloaded */
            }
        }

        private void OnVolumeChanged(object sender, ValueEventArgs<float> e) {
            _volumeProvider.Volume = e.Value;
        }

        private void OnEndOfStreamReached(object o, EventArgs e) {
            _endOfStream.Ended -= OnEndOfStreamReached;
            this.Finished?.Invoke(this, EventArgs.Empty);
        }
    }
}
