using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Controls;
using Blish_HUD.Graphics.UI;
using Blish_HUD.Input;
using Glide;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Nekres.Music_Mixer.Core.Services;
using Nekres.Music_Mixer.Core.Services.Data;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Nekres.Music_Mixer.Core.UI.Library {
    public class BgmLibraryView : View {

        private Playlist _playlist;
        private KeyBinding _pasteShortcut;
        private FlowPanel _tracksPanel;
        private string _name;
        private bool _checkingLink;

        public BgmLibraryView(Playlist playlist, string playlistName) {
            _name = playlistName;
            _playlist = playlist;
            _pasteShortcut = new KeyBinding(ModifierKeys.Ctrl, Keys.V) {
                Enabled = true
            };

            _pasteShortcut.Activated += OnPastePressed;
        }

        protected override void Unload() {
            _pasteShortcut.Enabled = false;
            _pasteShortcut.Activated -= OnPastePressed;
            base.Unload();
        }

        private async void OnPastePressed(object sender, EventArgs e) {
            if (this.ViewTarget?.Parent == null || _tracksPanel == null) {
                return;
            }

            // Only allow paste via shortcut if the mouse is hovering the tracks list.
            if (this.ViewTarget.Parent.Visible && _tracksPanel.ContentRegion.Contains(_tracksPanel.RelativeMousePosition)) {
                await FindAdd();
            };
        }

        private async Task FindAdd() {
            if (_checkingLink) {
                ScreenNotification.ShowNotification("Checking link… (Please wait.)", ScreenNotification.NotificationType.Warning);
                GameService.Content.PlaySoundEffectByName("button-click");
                return;
            }

            _checkingLink = true;

            try {
                var url = await ClipboardUtil.WindowsClipboardService.GetTextAsync();

                if (!url.IsWebLink()) {
                    ScreenNotification.ShowNotification("Your clipboard does not contain a valid link.", ScreenNotification.NotificationType.Error);
                    GameService.Content.PlaySoundEffectByName("error");
                    return;
                }

                ScreenNotification.ShowNotification("Link pasted. Checking… (Please wait.)");

                var data = await MusicMixer.Instance.YtDlp.GetMetaData(url);

                if (data.IsError) {
                    ScreenNotification.ShowNotification("Unsupported website.", ScreenNotification.NotificationType.Error);
                    GameService.Content.PlaySoundEffectByName("error");
                    return;
                }

                if (_playlist.Tracks.Any(track => string.Equals(track.ExternalId, data.Id))) {
                    ScreenNotification.ShowNotification("This track is already in the playlist.", ScreenNotification.NotificationType.Error);
                    GameService.Content.PlaySoundEffectByName("error");
                    return;
                }

                if (!MusicMixer.Instance.Data.GetTrackByMediaId(data.Id, out var source)) {
                    source = new AudioSource {
                        ExternalId = data.Id,
                        Uploader   = data.Uploader,
                        Title      = data.Title,
                        PageUrl    = data.Url,
                        Duration   = data.Duration,
                        Volume     = 1,
                        DayCycles = AudioSource.DayCycle.Day |
                                    AudioSource.DayCycle.Night
                    };

                    if (!MusicMixer.Instance.Data.Upsert(source)) {
                        ScreenNotification.ShowNotification("Something went wrong. Please try again.", ScreenNotification.NotificationType.Error);
                        GameService.Content.PlaySoundEffectByName("error");
                        return;
                    }
                }

                _playlist.Tracks.Add(source);
                MusicMixer.Instance.Data.Upsert(_playlist);

                AddBgmEntry(source, _tracksPanel);

            } catch (Exception e) {

                ScreenNotification.ShowNotification("Something went wrong. Please try again.", ScreenNotification.NotificationType.Error);
                GameService.Content.PlaySoundEffectByName("error");
                MusicMixer.Logger.Info(e, e.Message);

            } finally {
                _checkingLink = false;
            }
        }

        protected override void Build(Container buildPanel) {

            var title = new Label {
                Parent = buildPanel,
                Width = buildPanel.ContentRegion.Width,
                Height = 40,
                Text = _name,
                Font = GameService.Content.GetFont(ContentService.FontFace.Menomonia, ContentService.FontSize.Size18, ContentService.FontStyle.Regular),
                TextColor = Color.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Middle
            };

            var enabledCb = new Checkbox {
                Parent = buildPanel,
                Width = 100,
                Top = 20,
                Left = Panel.LEFT_PADDING,
                Text = "Enabled",
                BasicTooltipText = "Enable or disable this playlist.",
                Checked = _playlist.Enabled
            };

            enabledCb.CheckedChanged += async (_, e) => {
                _playlist.Enabled = e.Checked;

                if (!MusicMixer.Instance.Data.Upsert(_playlist)) {
                    ScreenNotification.ShowNotification("Something went wrong. Please try again.", ScreenNotification.NotificationType.Error);
                    GameService.Content.PlaySoundEffectByName("error");
                    return;
                }

                if (_playlist.ExternalId.Equals("Defeated")) {
                    if (e.Checked) {
                        await MusicMixer.Instance.Gw2State.SetupLockFiles(Gw2StateService.State.Defeated);
                    } else {
                        MusicMixer.Instance.Gw2State.RevertLockFiles(Gw2StateService.State.Defeated);
                    }
                }

                if (e.Checked) {
                    GameService.Content.PlaySoundEffectByName("color-change");
                }
            };

            _tracksPanel = new FlowPanel {
                Parent = buildPanel,
                Width = buildPanel.ContentRegion.Width,
                Top = title.Bottom,
                Height = buildPanel.ContentRegion.Height - title.Bottom - 32 - Panel.BOTTOM_PADDING,
                ShowBorder = true,
                FlowDirection = ControlFlowDirection.SingleTopToBottom,
                ControlPadding = new Vector2(0, Panel.BOTTOM_PADDING),
                OuterControlPadding = new Vector2(Panel.RIGHT_PADDING, Panel.BOTTOM_PADDING),
                CanScroll = true
            };

            var addBttn = new StandardButton {
                Parent = buildPanel,
                Width = _tracksPanel.Width,
                Height = 32,
                Top = _tracksPanel.Bottom + Panel.BOTTOM_PADDING,
                Left = _tracksPanel.Left,
                Text = $"Paste From Clipboard [{_pasteShortcut.GetBindingDisplayText()}]",
                BasicTooltipText = "Paste a video or audio link from your clipboard to add it to the playlist.\nRecommended platforms: SoundCloud, YouTube.",
            };

            foreach (var track in _playlist.Tracks) {
                AddBgmEntry(track, _tracksPanel);
            }

            addBttn.Click += async (_, _) => await FindAdd();

            base.Build(buildPanel);
        }

        private void AddBgmEntry(AudioSource source, FlowPanel parent) {
            var bgmEntryContainer = new ViewContainer {
                Parent = parent,
                Width = parent.ContentRegion.Width,
                Height = 108
            };

            var bgmEntry = new BgmEntry(source);
            bgmEntry.OnDeleted += (_, _) => {
                _playlist.Tracks.Remove(source);

                if (!MusicMixer.Instance.Data.Upsert(_playlist)) {
                    ScreenNotification.ShowNotification("Something went wrong. Please try again.", ScreenNotification.NotificationType.Error);
                    GameService.Content.PlaySoundEffectByName("error");
                };
            };

            bgmEntryContainer.Show(bgmEntry);
        }

        public class BgmEntry : View {

            public event EventHandler<EventArgs> OnDeleted;

            private AudioSource _audioSource;

            public BgmEntry(AudioSource audioSource) {
                _audioSource = audioSource;
            }
            protected override void Build(Container buildPanel) {

                var slidePanel = new SlidePanel {
                    Parent = buildPanel,
                    Width = buildPanel.ContentRegion.Width,
                    Height = buildPanel.ContentRegion.Height,
                    Left = buildPanel.ContentRegion.Width
                };

                var thumbnail = new RoundedImage(_audioSource.Thumbnail) {
                    Parent = slidePanel,
                    Width = 192, // 16:9
                    Height = 108
                };

                var durationStr = _audioSource.Duration.ToShortForm();
                var size = LabelUtil.GetLabelSize(ContentService.FontSize.Size14, durationStr);
                var duration = new FormattedLabelBuilder().SetWidth(size.X + Panel.RIGHT_PADDING).SetHeight(size.Y + 4)
                                                          .SetHorizontalAlignment(HorizontalAlignment.Center)
                                                          .CreatePart(durationStr, o => {
                                                              o.SetFontSize(ContentService.FontSize.Size14);
                                                          }).Build();
                duration.Parent = slidePanel;
                duration.Bottom = thumbnail.Bottom;
                duration.Right = thumbnail.Right;
                duration.BackgroundColor = Color.Black * 0.25f;

                var delBttn = new Image {
                    Parent = slidePanel,
                    Width = 32,
                    Height = 32,
                    Right = slidePanel.ContentRegion.Width - Panel.RIGHT_PADDING,
                    Top = thumbnail.Top,
                    Texture = GameService.Content.DatAssetCache.GetTextureFromAssetId(156012),
                    BasicTooltipText = "Remove from Playlist"
                };

                delBttn.MouseEntered += (_, _) => {
                    delBttn.Texture = GameService.Content.DatAssetCache.GetTextureFromAssetId(156011);
                };

                delBttn.MouseLeft += (_, _) => {
                    delBttn.Texture = GameService.Content.DatAssetCache.GetTextureFromAssetId(156012);
                };

                delBttn.Click += (_, _) => {
                    delBttn.Texture = GameService.Content.DatAssetCache.GetTextureFromAssetId(156012);

                    slidePanel.SlideOut(buildPanel.Dispose);

                    OnDeleted?.Invoke(this, EventArgs.Empty);
                };

                var title = new FormattedLabelBuilder().SetWidth(slidePanel.ContentRegion.Width -
                                                                 thumbnail.Right - delBttn.Width -
                                                                 Panel.RIGHT_PADDING - Control.ControlStandard.ControlOffset.X * 3)
                                                       .SetHeight(thumbnail.Height)
                                                       .SetVerticalAlignment(VerticalAlignment.Top)
                                                       .CreatePart(_audioSource.Title, o => {
                                                           o.SetFontSize(ContentService.FontSize.Size20);
                                                           o.MakeBold();
                                                           if (string.IsNullOrWhiteSpace(_audioSource.PageUrl)) {
                                                               o.SetLink(() => ScreenNotification.ShowNotification("Page Not Found.", ScreenNotification.NotificationType.Error));
                                                               GameService.Content.PlaySoundEffectByName("error");
                                                           } else {
                                                               o.SetHyperLink(_audioSource.PageUrl);
                                                           }}).Wrap().CreatePart($"\n{_audioSource.Uploader}", o => { 
                                                            o.SetFontSize(ContentService.FontSize.Size16);
                                                       }).Build();
                title.Parent = slidePanel;
                title.Top = thumbnail.Top;
                title.Left = thumbnail.Right + Control.ControlStandard.ControlOffset.X;

                var cyclesPanel = new FlowPanel {
                    Parent              = slidePanel,
                    Width               = 140,
                    Height              = 32,
                    Right               = slidePanel.ContentRegion.Width - Panel.RIGHT_PADDING,
                    Bottom              = thumbnail.Bottom,
                    FlowDirection       = ControlFlowDirection.SingleLeftToRight,
                    ControlPadding      = new Vector2(Control.ControlStandard.ControlOffset.X, 0),
                    OuterControlPadding = new Vector2(Control.ControlStandard.ControlOffset.X, 0)
                };

                foreach (var cycle in Enum.GetValues(typeof(AudioSource.DayCycle)).Cast<AudioSource.DayCycle>().Skip(1)
                                          .Except(new[] { AudioSource.DayCycle.Dawn, AudioSource.DayCycle.Dusk })) {
                    var cb = new Checkbox {
                        Parent = cyclesPanel,
                        Width = 100,
                        Height = 32,
                        Text = cycle.ToString(),
                        Checked = _audioSource.HasDayCycle(cycle)
                    };

                    cb.CheckedChanged += (_, e) => {

                        if ((_audioSource.DayCycles & ~cycle) == AudioSource.DayCycle.None) {
                            // At least one cycle must be selected
                            cb.GetPrivateField("_checked").SetValue(cb, !e.Checked); // Skip invoking CheckedChanged
                            GameService.Content.PlaySoundEffectByName("error");
                            return;
                        }

                        var oldCycles = _audioSource.DayCycles;
                        if (e.Checked) {
                            _audioSource.DayCycles |= cycle;
                        } else {
                            _audioSource.DayCycles &= ~cycle;
                        }

                        if (!MusicMixer.Instance.Data.Upsert(_audioSource)) {
                            _audioSource.DayCycles = oldCycles;
                            cb.GetPrivateField("_checked").SetValue(cb, !e.Checked); // Skip invoking CheckedChanged
                            ScreenNotification.ShowNotification("Something went wrong. Please try again.", ScreenNotification.NotificationType.Error);
                            GameService.Content.PlaySoundEffectByName("error");
                            return;
                        }

                        GameService.Content.PlaySoundEffectByName("color-change");
                    };
                }

                slidePanel.SlideIn();

                base.Build(buildPanel);
            }
        }

        public class SlidePanel : Panel {

            private Tween _tween;

            public void SlideIn() {
                _tween?.Cancel();
                _tween = Animation.Tweener.Tween(this, new { Left = 0, Opacity = 1f }, 0.3f).Ease(Ease.CubeOut);
            }

            public void SlideOut(Action onComplete = null) {
                _tween?.Cancel();
                _tween = Animation.Tweener.Tween(this, new { Left = Parent.ContentRegion.Width, Opacity = 0f }, 0.3f)
                                  .Ease(Ease.CubeIn)
                                  .OnComplete(onComplete);
            }

            protected override void DisposeControl() {
                SlideOut(base.DisposeControl);
            }
        }

        public class RoundedImage : Control {

            private Effect _curvedBorder;

            private AsyncTexture2D _texture;

            private SpriteBatchParameters _defaultParams;
            private SpriteBatchParameters _curvedBorderParams;

            private float _radius = 0.215f;
            private Tween _tween;

            public RoundedImage(AsyncTexture2D texture) {
                _defaultParams = new();
                _curvedBorder = MusicMixer.Instance.ContentsManager.GetEffect<Effect>(@"effects\curvedborder.mgfx");
                _curvedBorderParams = new() {
                    Effect = _curvedBorder
                };
                _texture = texture;

                //_curvedBorder.Parameters["Smooth"].SetValue(false); // Disable anti-aliasing
            }

            protected override void DisposeControl() {
                _curvedBorder.Dispose();
                base.DisposeControl();
            }

            protected override void OnMouseEntered(MouseEventArgs e) {
                _tween?.Cancel();
                _tween = Animation.Tweener.Tween(this, new { _radius = 0.315f }, 0.1f);
                base.OnMouseEntered(e);
            }

            protected override void OnMouseLeft(MouseEventArgs e) {
                _tween?.Cancel();
                _tween = Animation.Tweener.Tween(this, new { _radius = 0.215f }, 0.1f);
                base.OnMouseLeft(e);
            }

            protected override void Paint(SpriteBatch spriteBatch, Rectangle bounds) {
                if (!_texture.HasTexture || !_texture.HasSwapped) {
                    LoadingSpinnerUtil.DrawLoadingSpinner(this, spriteBatch, new Rectangle((bounds.Width - 32) / 2, (bounds.Height - 32) / 2, 32, 32));
                    return;
                }

                _curvedBorder.Parameters["Radius"].SetValue(_radius);
                _curvedBorder.Parameters["Opacity"].SetValue(this.Opacity);

                spriteBatch.End();
                spriteBatch.Begin(_curvedBorderParams);
                spriteBatch.DrawOnCtrl(this, _texture, new Rectangle(0, 0, this.Width, this.Height));
                spriteBatch.End();
                spriteBatch.Begin(_defaultParams);
            }

        }
    }
}
