using Blish_HUD;
using Blish_HUD.Controls;
using Blish_HUD.Graphics.UI;
using Microsoft.Xna.Framework;
using Nekres.Music_Mixer.Core.Services;
using Nekres.Music_Mixer.Core.UI.Controls;
using Nekres.Music_Mixer.Properties;
using System;
using System.Linq;

namespace Nekres.Music_Mixer.Core.UI.Settings {
    internal class ModuleSettingsView : View {

        private ModuleConfig _config;

        public ModuleSettingsView(ModuleConfig config) {
            _config = config;
        }

        protected override void Build(Container buildPanel) {

            var flowPanel = new FlowPanel {
                Parent = buildPanel,
                Width = buildPanel.ContentRegion.Width,
                Height = buildPanel.ContentRegion.Height,
                FlowDirection = ControlFlowDirection.TopToBottom,
                ControlPadding = new Vector2(Control.ControlStandard.ControlOffset.X,Control.ControlStandard.ControlOffset.Y),
                OuterControlPadding = new Vector2(Control.ControlStandard.ControlOffset.X,Panel.TOP_PADDING),
                Padding = new Thickness(5),
                CanScroll = true
            };

            var cbx = new Checkbox {
                Parent  = flowPanel,
                Text    = Resources.Mute_when_GW2_is_in_the_background,
                Checked = _config.MuteWhenInBackground
            };

            cbx.CheckedChanged += (_, e) => {
                _config.MuteWhenInBackground = e.Checked;
            };

            var volumeContainer = new ViewContainer {
                Parent = flowPanel,
                Width  = flowPanel.ContentRegion.Width,
                Height = 30,
            };
            var volumeSetting = new NumericConfigView(_config.MasterVolume * 100, Resources.Master_Volume);
            volumeSetting.ValueChanged += (_, e) => {
                _config.MasterVolume = e.Value / 100;
            };
            volumeContainer.Show(volumeSetting);

            var avgBitrateSettingContainer = new ViewContainer {
                Parent = flowPanel,
                Width  = flowPanel.ContentRegion.Width,
                Height = 30
            };
            var avgBitrateSetting = new EnumConfigView<YtDlpService.AudioBitrate>(_config.AverageBitrate, "Audio Bitrate");
            avgBitrateSetting.ValueChanged += (_, e) => {
                _config.AverageBitrate = e.NewValue;
            };
            avgBitrateSettingContainer.Show(avgBitrateSetting);

            cbx = new Checkbox {
                Parent  = flowPanel,
                Text    = Resources.Use_a_different_Output_Device,
                Checked = _config.UseCustomOutputDevice
            };

            var inputDevice = new KeyValueDropdown<string> {
                Parent           = flowPanel,
                PlaceholderText  = Resources.Select_an_output_device___,
                BasicTooltipText = Resources.Select_an_output_device___,
                Enabled          = _config.UseCustomOutputDevice
            };

            foreach (var device in AudioUtil.WasApiOutputDevices) {
                inputDevice.AddItem(device.Id, device.FriendlyName);
            }
            inputDevice.SelectedItem = _config.OutputDevice;

            cbx.CheckedChanged += (_, e) => {
                _config.UseCustomOutputDevice = e.Checked;
                inputDevice.Enabled           = _config.UseCustomOutputDevice;
            };

            inputDevice.ValueChanged += (_, e) => {
                _config.OutputDevice = e.NewValue;
            };

            var defaultBttn = new StandardButton {
                Parent = flowPanel,
                Width  = 200,
                Height = 23,
                Text   = "Copy Playlists to Clipboard"
            };

            defaultBttn.Click += async (_,_) => {
                var json = MusicMixer.Instance.Data.ExportToJson();
                var success = !string.IsNullOrEmpty(json) && await ClipboardUtil.WindowsClipboardService.SetTextAsync(json);
                GameService.Content.PlaySoundEffectByName(success ? "color-change" : "error");
            };

            base.Build(buildPanel);
        }


        private class NumericConfigView : View {

            public event EventHandler<ValueEventArgs<float>> ValueChanged;

            private float  _value;
            private string _displayText;

            public NumericConfigView(float value, string displayText) {
                _value = value;
                _displayText = displayText;
            }

            protected override void Build(Container buildPanel) {
                var label = new Label();
                label.AutoSizeWidth = true;
                label.Left          = Panel.LEFT_PADDING;
                label.Parent        = buildPanel;
                label.Text          = _displayText;

                var trackBar = new TrackBar();
                trackBar.Size     = new Point(277, 16);
                trackBar.Left     = label.Right + Panel.LEFT_PADDING;
                trackBar.Parent   = buildPanel;
                trackBar.MaxValue = 200;
                trackBar.Value    = _value;

                trackBar.IsDraggingChanged += (_, e) => {
                    if (!e.Value) {
                        ValueChanged?.Invoke(this, new ValueEventArgs<float>(trackBar.Value));
                    }
                };

                base.Build(buildPanel);
            }
        }

        private class EnumConfigView<TEnum> : View where TEnum : struct, Enum {
            public event EventHandler<ValueChangedEventArgs<TEnum>> ValueChanged;

            private TEnum  _value;
            private string _displayText;
            public EnumConfigView(TEnum value, string displayText) {
                _value = value;
                _displayText = displayText;
            }

            protected override void Build(Container buildPanel) {
                var label = new Label();
                label.AutoSizeWidth = true;
                label.Left          = Panel.LEFT_PADDING;
                label.Parent        = buildPanel;
                label.Text          = _displayText;

                var dropdown = new KeyValueDropdown<TEnum>();
                dropdown.Left   = label.Right + Panel.LEFT_PADDING;
                dropdown.Size   = new Point(250, 27);
                dropdown.Parent = buildPanel;

                foreach (var val in Enum.GetValues(typeof(TEnum)).Cast<TEnum>()) {
                    dropdown.AddItem(val, val.ToString());
                }

                dropdown.SelectedItem = _value;

                dropdown.ValueChanged += (_,e) => {
                    ValueChanged?.Invoke(this, new ValueChangedEventArgs<TEnum>(e.PreviousValue, e.NewValue));
                };
                base.Build(buildPanel);
            }
        }
    }
}
