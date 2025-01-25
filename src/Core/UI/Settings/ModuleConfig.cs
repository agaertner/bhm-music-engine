using Nekres.Music_Mixer.Core.Services;
using Newtonsoft.Json;

namespace Nekres.Music_Mixer.Core.UI.Settings {

    internal class ModuleConfig : ConfigBase {

        public static ModuleConfig Default = new() {
            _outputDevice          = string.Empty,
            _useCustomOutputDevice = false,
            _masterVolume          = 0.25f,
            _gameVolume            = 0.3f,
            _averageBitrate        = YtDlpService.AudioBitrate.B320,
            _muteWhenInBackground  = true,
            _defaultUpdates        = true,
            _playToCompletion      = false,
            _crossFade             = true,
            _crossFadeMs           = 5000,
        };

        private bool _playToCompletion;
        [JsonProperty("play_to_completion")]
        public bool PlayToCompletion {
            get => _playToCompletion;
            set {
                if (SetProperty(ref _playToCompletion, value)) {
                    this.SaveConfig(MusicMixer.Instance.ModuleConfig);
                }
            }
        }

        private bool _crossFade;
        [JsonProperty("crossfade")]
        public bool CrossFade {
            get => _crossFade;
            set {
                if (SetProperty(ref _crossFade, value)) {
                    this.SaveConfig(MusicMixer.Instance.ModuleConfig);
                }
            }
        }

        private int _crossFadeMs = 5000;
        [JsonProperty("crossfade_ms")]
        public int CrossFadeMs {
            get => _crossFadeMs;
            set {
                if (SetProperty(ref _crossFadeMs, value)) {
                    this.SaveConfig(MusicMixer.Instance.ModuleConfig);
                }
            }
        }

        private bool _useCustomOutputDevice;
        [JsonProperty("use_output_device")]
        public bool UseCustomOutputDevice {
            get => _useCustomOutputDevice;
            set {
                if (SetProperty(ref _useCustomOutputDevice, value)) {
                    this.SaveConfig(MusicMixer.Instance.ModuleConfig);
                }
            }
        }

        private string _outputDevice;
        [JsonProperty("output_device")]
        public string OutputDevice {
            get => _outputDevice;
            set {
                if (SetProperty(ref _outputDevice, value)) {
                    this.SaveConfig(MusicMixer.Instance.ModuleConfig);
                }
            }
        }

        private float _masterVolume;
        [JsonProperty("master_volume")]
        public float MasterVolume {
            get => _masterVolume;
            set {
                if (SetProperty(ref _masterVolume, value)) {
                    this.SaveConfig(MusicMixer.Instance.ModuleConfig);
                }
            }
        }

        private float _gameVolume = 0.1f;
        [JsonProperty("game_volume")]
        public float GameVolume {
            get => _gameVolume;
            set {
                if (SetProperty(ref _gameVolume, value)) {
                    this.SaveConfig(MusicMixer.Instance.ModuleConfig);
                }
            }
        }

        private YtDlpService.AudioBitrate _averageBitrate;
        [JsonProperty("average_bitrate")]
        public YtDlpService.AudioBitrate AverageBitrate {
            get => _averageBitrate;
            set {
                if (SetProperty(ref _averageBitrate, value)) {
                    this.SaveConfig(MusicMixer.Instance.ModuleConfig);
                }
            }
        }

        private bool _muteWhenInBackground;
        [JsonProperty("mute_in_background")]
        public bool MuteWhenInBackground {
            get => _muteWhenInBackground;
            set {
                if (SetProperty(ref _muteWhenInBackground, value)) {
                    this.SaveConfig(MusicMixer.Instance.ModuleConfig);
                }
            }
        }

        private bool _defaultUpdates;
        [JsonProperty("default_updates")]
        public bool DefaultUpdates {
            get => _defaultUpdates;
            set {
                if (SetProperty(ref _defaultUpdates, value)) {
                    this.SaveConfig(MusicMixer.Instance.ModuleConfig);
                }
            }
        }

        private bool _paused;
        [JsonProperty("paused")]
        public bool Paused {
            get => _paused;
            set {
                if (SetProperty(ref _paused, value)) {
                    this.SaveConfig(MusicMixer.Instance.ModuleConfig);
                }
            }
        }

        protected override void BindingChanged() {
            SaveConfig(MusicMixer.Instance.ModuleConfig);
        }
    }
}
