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

        private bool     _disposing;
        private bool     _initialized;
        private bool     _customDevice;
        private MMDevice _device;

        private bool _crossFade;
        private int  _crossFadeMs;
        private int  _fadeOutMs = 2000;

        private AudioTrack() {
            IsEmpty = true;
        }

        private AudioTrack(AudioSource source)
        {
            Source = source;

            _device = GameService.GameIntegration.Audio.AudioDevice;

            var config = MusicMixer.Instance.ModuleConfig.Value;
            if (config.UseCustomOutputDevice) {
                if (!string.IsNullOrWhiteSpace(config.OutputDevice)) {
                    var customDevice = AudioUtil.GetWasApiOutputDeviceById(config.OutputDevice);
                    if (customDevice != null) {
                        _device       = customDevice;
                        _customDevice = true;
                    }
                }
            }

            _outputDevice                 =  new WasapiOut(_device, AudioClientShareMode.Shared, false, 100);
            _outputDevice.PlaybackStopped += OnPlaybackStopped;

            _mediaProvider = new MediaFoundationReader(Source.AudioUrl);

            _crossFade         =  config.CrossFade;
            _crossFadeMs       =  config.CrossFadeMs;
            _endOfStream       =  new EndOfStreamProvider(_mediaProvider, _crossFadeMs);
            _endOfStream.EndReached += OnEndOfStreamReached;

            _volumeProvider      =  new SubmergedVolumeProvider(_endOfStream);
            Source.VolumeChanged += OnVolumeChanged;
            
            _fadeInOut = new FadeInOutSampleProvider(_volumeProvider);

            // Filter is toggled when submerged.
            _lowPassFilter = new BiQuadFilterSource(_fadeInOut)
            {
                Filter = new LowPassFilter(_fadeInOut.WaveFormat.SampleRate, 400)
            };
            _equalizer = Equalizer.Create10BandEqualizer(_lowPassFilter);

            Invalidate();
        }

        private void OnPlaybackStopped(object sender, StoppedEventArgs e) {
            if (e.Exception != null) {
                this.Dispose();
            }
        }

        public static async Task<AudioTrack> TryGetStream(AudioSource source, int retries = 3, int delayMs = 500, Logger logger = null)
        {
            if (string.IsNullOrWhiteSpace(source.AudioUrl)) {
                return AudioTrack.Empty;
            }

            logger ??= MusicMixer.Logger;
            try {
                return new AudioTrack(source);
            } catch (Exception e) {
                if (retries > 0) {
                    logger.Info($"Failed to create audio output stream. Remaining retries: {retries}.");
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

        public async Task<bool> Play(int retries = 3, int delayMs = 500, Logger logger = null)
        {
            if (IsEmpty
             || _disposing
             || _outputDevice == null) {
                return false;
            }
            logger ??= Logger.GetLogger(typeof(AudioTrack));
            try {
                if (!_initialized) {
                    _initialized = true;
                    _outputDevice.Init(_equalizer);
                }
                _outputDevice.Play();
                _fadeInOut.BeginFadeIn(_crossFade ? 2000 : 500);
                this.ToggleSubmergedFx(MusicMixer.Instance.Gw2State.IsSubmerged);
                return true;
            } catch (Exception e) {

                if (retries > 0) {
                    await Task.Delay(delayMs);
                    return await Play(retries - 1, delayMs, logger);
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
                return false;
            }
        }

        public void Invalidate() {
            if (IsEmpty || _disposing || Source == null) {
                return;
            }
            if (GameService.GameIntegration.Audio.Volume == 0) {
                SetVolume(0);
                return;
            }
            SetVolume(AudioUtil.GetNormalizedVolume(Source.Volume, MusicMixer.Instance.ModuleConfig.Value.MasterVolume));
        }

        private void SetVolume(float volume) {
            if (IsEmpty) {
                return;
            }
            if (_volumeProvider != null) {
                _volumeProvider.Volume = volume;
            }
        }

        public void Seek(float seconds) {
            if (IsEmpty || _disposing) {
                return;
            }

            _mediaProvider?.SetPosition(seconds);
        }

        public void Seek(TimeSpan timespan) {
            if (IsEmpty || _disposing) {
                return;
            }

            _mediaProvider?.SetPosition(timespan);
        }

        public void Pause()
        {
            if (IsEmpty 
             || _disposing
             || _outputDevice == null 
             || _outputDevice.PlaybackState == PlaybackState.Paused) {
                return;
            }

            _outputDevice.Pause();
        }

        public void Resume()
        {
            if (IsEmpty 
             || _disposing 
             || _outputDevice == null 
             || _outputDevice.PlaybackState != PlaybackState.Paused) {
                return;
            }
            _outputDevice.Play();
        }

        public void ToggleSubmergedFx(bool enable) {
            if (IsEmpty 
             || _disposing 
             || _equalizer == null) {
                return;
            }
            _lowPassFilter.Enabled = enable;
            _volumeProvider.Enabled = enable;
            _equalizer.SampleFilters[1].AverageGainDB = enable ? 19.5f : 0; // Bass
            _equalizer.SampleFilters[9].AverageGainDB = enable ? 13.4f : 0; // Treble
        }

        public void Dispose() {
            _disposing = true;
            if (IsEmpty) {
                return;
            }
            _outputDevice.PlaybackStopped -= OnPlaybackStopped;
            _endOfStream.EndReached -= OnEndOfStreamReached;
            var fadeOutMs = _crossFade && _endOfStream.Ended ? _crossFadeMs : _fadeOutMs;
            _fadeInOut.BeginFadeOut(fadeOutMs);
            Task.Delay(fadeOutMs + 5).ContinueWith(_ => {
                _outputDevice.Stop();
                DisposeMediaInterfaces();
            });
        }

        private void DisposeMediaInterfaces() {
            Source.VolumeChanged -= OnVolumeChanged;
            try {
                _outputDevice?.Dispose();
                _mediaProvider?.Dispose();
                if (_customDevice) {
                    _device?.Dispose();
                }
            } catch (Exception e) when (e is NullReferenceException or ObjectDisposedException) {
                /* NOOP - Module was unloaded */
            }
        }

        private void OnVolumeChanged(object sender, ValueEventArgs<float> e) {
            Invalidate();
        }

        private void OnEndOfStreamReached(object o, EventArgs e) {
            _endOfStream.EndReached -= OnEndOfStreamReached;
            this.Finished?.Invoke(this, EventArgs.Empty);
        }
    }
}
