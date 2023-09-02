using Blish_HUD;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Nekres.Music_Mixer.Core.Services.Audio.Source;
using Nekres.Music_Mixer.Core.Services.Audio.Source.DSP;
using Nekres.Music_Mixer.Core.Services.Audio.Source.Equalizer;
using Nekres.Music_Mixer.Core.Services.Data;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Nekres.Music_Mixer.Core.Services.Audio {
    internal class AudioTrack : IDisposable {

        public static AudioTrack Empty = new();
        public readonly bool IsEmpty;

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

        public static async Task<AudioTrack> TryGetStream(AudioSource source, int retries = 3, int delayMs = 500, Logger logger = null)
        {
            if (string.IsNullOrWhiteSpace(source.AudioUrl)) {
                return AudioTrack.Empty;
            }

            logger ??= Logger.GetLogger(typeof(AudioTrack));
            try {
                return new AudioTrack(source);
            } catch (Exception e) {
                if (retries > 0) {
                    logger.Info(e, $"Failed to create audio output stream. Remaining retries: {retries}.");
                    await Task.Delay(delayMs);
                    return await TryGetStream(source, retries - 1, delayMs, logger);
                }

                switch (e) {
                    case InvalidCastException:
                        logger.Warn(e, e.Message);
                        break;
                    case UnauthorizedAccessException:
                        logger.Info(e, "Output device unavailable. Access denied.");
                        break;
                    case COMException when (uint)e.HResult == 0x88890008:
                        logger.Warn(e, "Output device does not support shared mode.");
                        break;
                    case COMException when (uint)e.HResult == 0x80040154:
                        logger.Warn(e, "Output device unsupported. Component class not registered.");
                        break;
                    case COMException when (uint)e.HResult == 0x80070490:
                        logger.Warn(e, "Output device is not supported or was not found.");
                        break;
                    case COMException when (uint)e.HResult == 0x80070005:
                        logger.Warn(e, "Output device unavailable. Access denied.");
                        break;
                    case COMException when (uint)e.HResult == 0x8889000A:
                        logger.Warn(e, "Output device unavailable. Device is being used in exclusive mode.");
                        break;
                    case COMException:
                        logger.Warn(e, $"Output device unavailable. HRESULT: {e.HResult}");
                        break;
                    default:
                        logger.Warn(e, e.Message);
                        break;
                }
            }
            return AudioTrack.Empty;
        }

        public async Task Play(int fadeInDuration = 500, int retries = 3, int delayMs = 500, Logger logger = null)
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
                    await Task.Delay(delayMs);
                    await Play(fadeInDuration, retries - 1, delayMs,logger);
                    return;
                }

                switch (e) {
                    case InvalidCastException:
                        logger.Warn(e, e.Message);
                        break;
                    case UnauthorizedAccessException:
                        logger.Info(e, "Access to output device denied.");
                        break;
                    case COMException: // HRESULT: 0x80070005
                        logger.Warn(e, $"Output device unavailable. HRESULT: {e.HResult}");
                        break;
                    default:
                        logger.Warn(e, e.Message);
                        break;
                }
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
