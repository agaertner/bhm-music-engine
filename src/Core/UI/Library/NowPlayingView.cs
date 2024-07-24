using Blish_HUD;
using Blish_HUD.Controls;
using Blish_HUD.Graphics.UI;
using Nekres.Music_Mixer.Core.Services.Data;
using Nekres.Music_Mixer.Properties;
using System;
using System.Diagnostics;
using System.Threading;
using Container = Blish_HUD.Controls.Container;

namespace Nekres.Music_Mixer.Core.UI.Library {
    internal class NowPlayingView : View {

        private AudioSource _source;
        private TrackBar    _trackbar;
        private Label       _currentTmeLbl;
        private Thread      _songUpdater;
        private bool        _unloading;
        private SlidePanel  _slidePanel;

        public NowPlayingView(AudioSource source) {
            _source      = source;
            _songUpdater = new Thread(UpdateTrackbar) {
                IsBackground = true
            };
        }

        protected override void Unload() {
            _unloading = true;
            _slidePanel.SlideOut(base.Unload);
        }

        protected override void Build(Container buildPanel) {

            _slidePanel = new SlidePanel {
                Parent     = buildPanel,
                Width      = buildPanel.ContentRegion.Width,
                Height     = buildPanel.ContentRegion.Height,
                Left       = buildPanel.ContentRegion.Width
            };

            var thumbnail = new RoundedImage(_source.Thumbnail) {
                Parent = _slidePanel,
                Width  = 192, // 16:9
                Height = 108
            };

            thumbnail.Click += (_, _) => {
                if (string.IsNullOrWhiteSpace(_source.PageUrl)) {
                    ScreenNotification.ShowNotification(Resources.Page_Not_Found_, ScreenNotification.NotificationType.Error);
                    GameService.Content.PlaySoundEffectByName("error");
                } else {
                    Process.Start(_source.PageUrl);
                    GameService.Content.PlaySoundEffectByName("open-skill-slot");
                }
            };

            _currentTmeLbl = new Label {
                Parent              = _slidePanel,
                Width               = 50,
                HorizontalAlignment = HorizontalAlignment.Center,
                Left                = thumbnail.Right,
                Top                 = thumbnail.Bottom - 20,
                Text                = "0:00"
            };

            var totalDurLbl = new Label {
                Parent              = _slidePanel,
                Width               = 50,
                Left                = _slidePanel.ContentRegion.Width - 50,
                Top                 = thumbnail.Bottom                - 20,
                HorizontalAlignment = HorizontalAlignment.Center,
                Text                = _source.Duration.ToShortForm()
            };

            // Seeker
            _trackbar = new TrackBar {
                Parent   = _slidePanel,
                Width    = _slidePanel.ContentRegion.Width - 15 - thumbnail.Width - _currentTmeLbl.Width - totalDurLbl.Width,
                Height   = 16,
                Left     = _currentTmeLbl.Right + Panel.RIGHT_PADDING,
                Top      = thumbnail.Bottom     - 20,
                Value    = 0,
                MinValue = 0,
                MaxValue = (float)_source.Duration.TotalSeconds
            };
            _trackbar.IsDraggingChanged += OnIsDraggingChanged;

            var title = new FormattedLabelBuilder().SetWidth(_slidePanel.ContentRegion.Width -
                                                             thumbnail.Right                 - Panel.RIGHT_PADDING - 13 - Control.ControlStandard.ControlOffset.X * 3)
                                                   .SetHeight(thumbnail.Height - totalDurLbl.Height)
                                                   .SetVerticalAlignment(VerticalAlignment.Top)
                                                   .CreatePart(_source.Title, o => {
                                                        o.SetFontSize(ContentService.FontSize.Size20);
                                                        o.MakeBold();
                                                        if (string.IsNullOrWhiteSpace(_source.PageUrl)) {
                                                            o.SetLink(() => ScreenNotification.ShowNotification(Resources.Page_Not_Found_, ScreenNotification.NotificationType.Error));
                                                            GameService.Content.PlaySoundEffectByName("error");
                                                        } else {
                                                            o.SetLink(() => {
                                                                Process.Start(_source.PageUrl);
                                                                GameService.Content.PlaySoundEffectByName("open-skill-slot");
                                                            });
                                                        }
                                                    }).Wrap().CreatePart($"\n{_source.Uploader}", o => {
                                                        o.SetFontSize(ContentService.FontSize.Size16);
                                                    }).Build();

            title.Parent = _slidePanel;
            title.Top    = thumbnail.Top;
            title.Left   = thumbnail.Right + Control.ControlStandard.ControlOffset.X;

            _songUpdater.Start();

            _slidePanel.SlideIn();
        }

        private void UpdateTrackbar() {
            while (!_unloading && MusicMixer.Instance != null) {
                if (_trackbar.Dragging) {
                    _currentTmeLbl.Text = TimeSpan.FromSeconds(_trackbar.Value).ToShortForm();
                    continue;
                }

                var track = MusicMixer.Instance.Audio.AudioTrack;
                if (track == null        ||
                    track.IsEmpty        ||
                    track.Source == null ||
                    track.Source.IsEmpty ||
                    !track.Source.Id.Equals(_source.Id)) {
                    return;
                }
                _trackbar.Value     = (float)track.CurrentTime.TotalSeconds;
                _currentTmeLbl.Text = track.CurrentTime.ToShortForm();
            }
        }

        private void OnIsDraggingChanged(object sender, ValueEventArgs<bool> e) {
            if (e.Value) {
                return;
            }

            MusicMixer.Instance.Audio.AudioTrack.Seek(((TrackBar)sender).Value);
        }

    }
}
