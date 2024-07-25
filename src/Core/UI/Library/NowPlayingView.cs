using Blish_HUD;
using Blish_HUD.Controls;
using Blish_HUD.Graphics.UI;
using Microsoft.Xna.Framework.Graphics;
using Nekres.Music_Mixer.Core.Services.Data;
using Nekres.Music_Mixer.Properties;
using System;
using System.Diagnostics;
using System.Threading;
using Color = Microsoft.Xna.Framework.Color;
using Container = Blish_HUD.Controls.Container;

namespace Nekres.Music_Mixer.Core.UI.Library {
    internal class NowPlayingView : View {

        private Thread    _songUpdater;
        private bool      _unloading;
        private Container _buildPanel;

        private Texture2D _playTex;
        private Texture2D _pauseTex;
        private Texture2D _stopTex;

        // Track info
        private AudioSource    _source;
        private TrackBar       _trackbar;
        private RoundedImage   _thumbnail;
        private FormattedLabel _title;
        private Label          _totalDurLbl;
        private Label          _currentTmeLbl;
        
        public NowPlayingView() {
            _songUpdater = new Thread(UpdateTrackbar) {
                IsBackground = true
            };

            MusicMixer.Instance.Audio.MusicChanged += OnMusicChanged;

            _source = MusicMixer.Instance.Audio.AudioTrack?.Source;
            
            _playTex  = MusicMixer.Instance.ContentsManager.GetTexture("play.png");
            _pauseTex = MusicMixer.Instance.ContentsManager.GetTexture("pause.png");
            _stopTex  = MusicMixer.Instance.ContentsManager.GetTexture("stop.png");
        }

        private void OnMusicChanged(object sender, ValueEventArgs<AudioSource> e) {
            _source = e.Value;
            CreateInfo();
        }

        protected override void Unload() {
            _unloading = true;
            MusicMixer.Instance.Audio.MusicChanged -= OnMusicChanged;
        }

        private void CreateInfo() {
            if (_buildPanel == null) {
                return;
            }

            _thumbnail.Texture  = _source?.Thumbnail;
            _currentTmeLbl.Text = TimeSpan.Zero.ToShortForm();
            var dur = _source?.Duration ?? TimeSpan.Zero;
            _totalDurLbl.Text   = dur.ToShortForm();
            _trackbar.Value     = 0;
            _trackbar.MaxValue  = (float)dur.TotalSeconds;
            _trackbar.RefreshValue(0);

            var title    = _source?.Title    ?? string.Empty;
            var pageUrl  = _source?.PageUrl  ?? string.Empty;
            var uploader = _source?.Uploader ?? string.Empty;
            MakeTitle(title, pageUrl, uploader);
        }

        protected override void Build(Container buildPanel) {
            _buildPanel = buildPanel;

            var playBttn = new Image {
                Texture = MusicMixer.Instance.ModuleConfig.Value.Paused ? _playTex : _pauseTex,
                Parent  = buildPanel,
                Width   = 28,
                Height  = 28,
                Left    = 192 + (_buildPanel.ContentRegion.Width - 192 - 28) / 2,
                Top     = _buildPanel.ContentRegion.Height - 28 - 24,
                BasicTooltipText = MusicMixer.Instance.ModuleConfig.Value.Paused ? Resources.Continue : Resources.Pause
            };

            playBttn.MouseEntered += (_, _) => {
                playBttn.Tint = Color.White * 0.8f;
            };

            playBttn.MouseLeft += (_, _) => {
                playBttn.Tint = Color.White;
            };

            playBttn.Click += (_, _) => {
                var config = MusicMixer.Instance.ModuleConfig.Value;
                config.Paused = !config.Paused;

                if (config.Paused) {
                    playBttn.Texture          = _playTex;
                    playBttn.BasicTooltipText = Resources.Continue;
                } else {
                    playBttn.Texture          = _pauseTex;
                    playBttn.BasicTooltipText = Resources.Pause;
                }
                
                if (config.Paused) {
                    MusicMixer.Instance.Audio.Pause();
                } else {
                    MusicMixer.Instance.Audio.Resume();
                }
            };

            _thumbnail = new RoundedImage {
                Parent = _buildPanel,
                Width = 192, // 16:9
                Height = 108,
                Top = (_buildPanel.ContentRegion.Height - 108) / 2
            };

            _thumbnail.Click += (_, _) => {
                if (string.IsNullOrWhiteSpace(_source.PageUrl)) {
                    ScreenNotification.ShowNotification(Resources.Page_Not_Found_, ScreenNotification.NotificationType.Error);
                    GameService.Content.PlaySoundEffectByName("error");
                } else {
                    Process.Start(_source.PageUrl);
                    GameService.Content.PlaySoundEffectByName("open-skill-slot");
                }
            };

            _currentTmeLbl = new Label {
                Parent              = _buildPanel,
                Width               = 50,
                HorizontalAlignment = HorizontalAlignment.Center,
                Left                = _thumbnail.Right,
                Top                 = _buildPanel.ContentRegion.Height - 20,
                Text                = TimeSpan.Zero.ToShortForm()
        };

            _totalDurLbl = new Label {
                Parent              = _buildPanel,
                Width               = 50,
                Left                = _buildPanel.ContentRegion.Width  - 50,
                Top                 = _buildPanel.ContentRegion.Height - 20,
                HorizontalAlignment = HorizontalAlignment.Center,
            };

            // Seeker
            _trackbar = new TrackBar {
                Parent   = _buildPanel,
                Width    = _buildPanel.ContentRegion.Width - 15 - _thumbnail.Width - _currentTmeLbl.Width - _totalDurLbl.Width,
                Height   = 16,
                Left     = _currentTmeLbl.Right             + Panel.RIGHT_PADDING,
                Top      = _buildPanel.ContentRegion.Height - 20,
                Value    = 0,
                MinValue = 0
            };
            _trackbar.IsDraggingChanged += OnIsDraggingChanged;

            CreateInfo();
            _songUpdater.Start();
        }

        private void MakeTitle(string title, string pageUrl, string uploader) {
            _title?.Dispose();
            _title = new FormattedLabelBuilder().SetWidth(_buildPanel.ContentRegion.Width -
                                                          _thumbnail.Right - Panel.RIGHT_PADDING - 13 - Control.ControlStandard.ControlOffset.X * 3)
                                                   .SetHeight(_buildPanel.Height - _totalDurLbl.Height - 40)
                                                   .SetVerticalAlignment(VerticalAlignment.Top)
                                                   .CreatePart(title, o => {
                                                       o.SetFontSize(ContentService.FontSize.Size20);
                                                       o.MakeBold();
                                                       o.SetLink(() => {
                                                           if (string.IsNullOrWhiteSpace(pageUrl)) {
                                                               ScreenNotification.ShowNotification(Resources.Page_Not_Found_, ScreenNotification.NotificationType.Error);
                                                               GameService.Content.PlaySoundEffectByName("error");
                                                           } else {
                                                               Process.Start(pageUrl);
                                                               GameService.Content.PlaySoundEffectByName("open-skill-slot");
                                                           }
                                                       });
                                                   }).Wrap().CreatePart($"\n{uploader}", o => o.SetFontSize(ContentService.FontSize.Size16)).Build();
            _title.Parent = _buildPanel;
            _title.Top = Panel.TOP_PADDING;
            _title.Left = _thumbnail.Right + Control.ControlStandard.ControlOffset.X;
        }

        private void UpdateTrackbar() {
            while (!_unloading && MusicMixer.Instance != null) {
                if (_source == null || _source.IsEmpty) {
                    continue;
                }

                var track = MusicMixer.Instance.Audio.AudioTrack;
                if (track == null        ||
                    track.IsEmpty        ||
                    track.Source == null ||
                    track.Source.IsEmpty ||
                    !track.Source.Id.Equals(_source.Id)) {
                    continue;
                }

                if (_trackbar.Dragging) {
                    _currentTmeLbl.Text = TimeSpan.FromSeconds(_trackbar.Value).ToShortForm();
                    continue;
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
