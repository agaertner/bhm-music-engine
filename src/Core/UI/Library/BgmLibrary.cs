using Blish_HUD;
using Blish_HUD.Controls;
using Blish_HUD.Graphics.UI;
using Blish_HUD.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Nekres.Music_Mixer.Core.Services;
using Nekres.Music_Mixer.Core.Services.Data;
using Nekres.Music_Mixer.Properties;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Nekres.Music_Mixer.Core.UI.Library {
    public class BgmLibraryView : View {

        private Playlist      _playlist;
        private KeyBinding    _pasteShortcut;
        private FlowPanel     _tracksPanel;
        private string        _name;
        private bool          _checkingLink;

        public BgmLibraryView(Playlist playlist, string playlistName) {
            _name = playlistName;
            _playlist = playlist;
            _pasteShortcut = new KeyBinding(ModifierKeys.Ctrl, Keys.V) {
                Enabled = true
            };
            _pasteShortcut.Activated += OnPastePressed;
            MusicMixer.Instance.Data.SourceRemoved += OnSourceRemoved;
        }

        private void OnSourceRemoved(object sender, ValueEventArgs<AudioSource> e) {
            _playlist.Tracks.RemoveAll(x => x.Id.Equals(e.Value.Id));
            var bgmContainers = _tracksPanel.Children.Cast<ViewContainer>();
            foreach (var container in bgmContainers) {
                var bgmEntry = (BgmEntry)container.CurrentView;
                if (bgmEntry.AudioSource.Id.Equals(e.Value.Id)) {
                    container.Dispose();
                    break;
                }
            }
        }

        protected override void Unload() {
            MusicMixer.Instance.Data.SourceRemoved -= OnSourceRemoved;
            _pasteShortcut.Enabled                 =  false;
            _pasteShortcut.Activated               -= OnPastePressed;
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
                ScreenNotification.ShowNotification($"{Resources.Checking_link___} ({Resources.Please_wait_})", ScreenNotification.NotificationType.Warning);
                GameService.Content.PlaySoundEffectByName("button-click");
                return;
            }

            _checkingLink = true;

            try {
                var url = await ClipboardUtil.WindowsClipboardService.GetTextAsync();

                if (!url.IsWebLink()) {
                    ScreenNotification.ShowNotification(Resources.Your_clipboard_does_not_contain_a_valid_link_, ScreenNotification.NotificationType.Error);
                    GameService.Content.PlaySoundEffectByName("error");
                    return;
                }

                ScreenNotification.ShowNotification($"{Resources.Link_pasted__Checking___} ({Resources.Please_wait_})");

                var data = await MusicMixer.Instance.YtDlp.GetMetaData(url);

                if (data.IsError) {
                    ScreenNotification.ShowNotification(Resources.Unsupported_website_, ScreenNotification.NotificationType.Error);
                    GameService.Content.PlaySoundEffectByName("error");
                    return;
                }

                if (_playlist.Tracks.Any(track => string.Equals(track.ExternalId, data.Id))) {
                    ScreenNotification.ShowNotification(Resources.This_track_is_already_in_the_playlist_, ScreenNotification.NotificationType.Error);
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
                        DayCycles  = AudioSource.DayCycle.Day | AudioSource.DayCycle.Night
                    };

                    if (!MusicMixer.Instance.Data.Upsert(source)) {
                        ScreenNotification.ShowNotification(Resources.Something_went_wrong__Please_try_again_, ScreenNotification.NotificationType.Error);
                        GameService.Content.PlaySoundEffectByName("error");
                        return;
                    }
                }

                _playlist.Tracks.Add(source);
                MusicMixer.Instance.Data.Upsert(_playlist);

                AddBgmEntry(source, _tracksPanel);
                GameService.Content.PlaySoundEffectByName("select-skill");

            } catch (Exception e) {

                ScreenNotification.ShowNotification(Resources.Something_went_wrong__Please_try_again_, ScreenNotification.NotificationType.Error);
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
                Text = Resources.Enabled,
                BasicTooltipText = Resources.Enable_or_disable_this_playlist_,
                Checked = _playlist.Enabled
            };

            enabledCb.CheckedChanged += async (_, e) => {
                _playlist.Enabled = e.Checked;

                if (!MusicMixer.Instance.Data.Upsert(_playlist)) {
                    ScreenNotification.ShowNotification(Resources.Something_went_wrong__Please_try_again_, ScreenNotification.NotificationType.Error);
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
                Parent              = buildPanel,
                Width               = buildPanel.ContentRegion.Width,
                Top                 = title.Bottom,
                Height              = buildPanel.ContentRegion.Height - title.Height - 40,
                ShowBorder          = true,
                FlowDirection       = ControlFlowDirection.SingleTopToBottom,
                ControlPadding      = new Vector2(0,                   Panel.BOTTOM_PADDING),
                OuterControlPadding = new Vector2(Panel.RIGHT_PADDING, Panel.BOTTOM_PADDING),
                CanScroll           = true
            };

            var addBttn = new StandardButton {
                Parent           = buildPanel,
                Width            = _tracksPanel.Width,
                Height           = 32,
                Top              = _tracksPanel.Bottom + Panel.BOTTOM_PADDING,
                Left             = _tracksPanel.Left,
                Text             = $"{Resources.Paste_From_Clipboard} [{_pasteShortcut.GetBindingDisplayText()}]",
                BasicTooltipText = string.Format("{0}\n{1} {2}", Resources.Paste_a_video_or_audio_link_from_your_clipboard_to_add_it_to_the_playlist_, Resources.Recommended_platforms_, "SoundCloud, YouTube.")
            };

            foreach (var track in _playlist.Tracks) {
                AddBgmEntry(track, _tracksPanel);
            }

            addBttn.Click += async (_, _) => await FindAdd();

            base.Build(buildPanel);
        }

        private void AddBgmEntry(AudioSource source, FlowPanel parent) {
            if (source.Duration.Equals(TimeSpan.Zero)) {
                return;
            }

            source.Title ??= string.Empty;

            var bgmEntryContainer = new ViewContainer {
                Parent = parent,
                Width = parent.ContentRegion.Width,
                Height = 108
            };

            var bgmEntry = new BgmEntry(source);
            bgmEntry.OnDeleted += (_, _) => {
                _playlist.Tracks.Remove(source);

                if (!MusicMixer.Instance.Data.Upsert(_playlist)) {
                    ScreenNotification.ShowNotification(Resources.Something_went_wrong__Please_try_again_, ScreenNotification.NotificationType.Error);
                    GameService.Content.PlaySoundEffectByName("error");
                }

                MusicMixer.Instance.Data.Remove(source);
            };

            bgmEntryContainer.Show(bgmEntry);
        }

        public class BgmEntry : View {

            public event EventHandler<EventArgs> OnDeleted;

            public readonly AudioSource AudioSource;

            public BgmEntry(AudioSource audioSource) {
                AudioSource = audioSource;
            }

            protected override void Build(Container buildPanel) {

                /*var slidePanel = new SlidePanel {
                    Parent = buildPanel,
                    Width = buildPanel.ContentRegion.Width,
                    Height = buildPanel.ContentRegion.Height,
                    Left = buildPanel.ContentRegion.Width
                };*/

                var thumbnail = new RoundedImage {
                    Parent  = buildPanel,
                    Width   = 192, // 16:9
                    Height  = 108,
                    Texture = AudioSource.Thumbnail
                };

                thumbnail.Click += async (_,_) => {
                    if (string.IsNullOrWhiteSpace(AudioSource.PageUrl)) {
                        ScreenNotification.ShowNotification(Resources.Page_Not_Found_, ScreenNotification.NotificationType.Error);
                        GameService.Content.PlaySoundEffectByName("error");
                    } else {
                        //Process.Start(_audioSource.PageUrl);
                        await MusicMixer.Instance.Audio.Play(AudioSource);
                        GameService.Content.PlaySoundEffectByName("open-skill-slot");
                    }
                };

                if (AudioSource.HasError) {
                    var image = new Image {
                        Parent           = buildPanel,
                        Left             = thumbnail.Width - 32,
                        Width            = 32,
                        Height           = 32,
                        Texture          = GameService.Content.DatAssetCache.GetTextureFromAssetId(154982),
                        BasicTooltipText = AudioSource.GetErrorMessage(),
                        BackgroundColor  = Color.Black * 0.7f
                    };
                }

                var durationStr = AudioSource.Duration.ToShortForm();
                var size = LabelUtil.GetLabelSize(ContentService.FontSize.Size14, durationStr);
                var duration = new FormattedLabelBuilder().SetWidth(size.X + Panel.RIGHT_PADDING).SetHeight(size.Y + 4)
                                                          .SetHorizontalAlignment(HorizontalAlignment.Center)
                                                          .CreatePart(durationStr, o => {
                                                              o.SetFontSize(ContentService.FontSize.Size14);
                                                          }).Build();
                duration.Parent          = buildPanel;
                duration.Bottom          = thumbnail.Bottom;
                duration.Right           = thumbnail.Right;
                duration.BackgroundColor = Color.Black * 0.25f;

                var delBttn = new Image {
                    Parent           = buildPanel,
                    Width            = 32,
                    Height           = 32,
                    Right            = buildPanel.ContentRegion.Width - Panel.RIGHT_PADDING - 13 /*SCROLLBAR_WIDTH*/,
                    Top              = thumbnail.Top,
                    Texture          = GameService.Content.DatAssetCache.GetTextureFromAssetId(156012),
                    BasicTooltipText = Resources.Remove_from_Playlist
                };

                delBttn.MouseEntered += (_, _) => {
                    delBttn.Texture = GameService.Content.DatAssetCache.GetTextureFromAssetId(156011);
                };

                delBttn.MouseLeft += (_, _) => {
                    delBttn.Texture = GameService.Content.DatAssetCache.GetTextureFromAssetId(156012);
                };

                delBttn.Click += (_, _) => {
                    GameService.Content.PlaySoundEffectByName("button-click");

                    delBttn.Texture = GameService.Content.DatAssetCache.GetTextureFromAssetId(156012);

                    //slidePanel.SlideOut(buildPanel.Dispose);

                    GameService.Content.PlaySoundEffectByName("window-close");

                    OnDeleted?.Invoke(this, EventArgs.Empty);
                };

                var title = new FormattedLabelBuilder().SetWidth(buildPanel.ContentRegion.Width -
                                                                 thumbnail.Right - delBttn.Width -
                                                                 Panel.RIGHT_PADDING - 13 - Control.ControlStandard.ControlOffset.X * 3)
                                                       .SetHeight(thumbnail.Height)
                                                       .SetVerticalAlignment(VerticalAlignment.Top)
                                                       .CreatePart(AudioSource.Title, o => {
                                                           o.SetFontSize(ContentService.FontSize.Size20);
                                                           o.MakeBold();
                                                           if (string.IsNullOrWhiteSpace(AudioSource.PageUrl)) {
                                                               o.SetLink(() => ScreenNotification.ShowNotification(Resources.Page_Not_Found_, ScreenNotification.NotificationType.Error));
                                                               GameService.Content.PlaySoundEffectByName("error");
                                                           } else {
                                                               o.SetLink(() => {
                                                                   Process.Start(AudioSource.PageUrl);
                                                                   GameService.Content.PlaySoundEffectByName("open-skill-slot");
                                                               });
                                                           }}).Wrap().CreatePart($"\n{AudioSource.Uploader}", o => { 
                                                            o.SetFontSize(ContentService.FontSize.Size16);
                                                       }).Build();
                title.Parent = buildPanel;
                title.Top    = thumbnail.Top;
                title.Left   = thumbnail.Right + Control.ControlStandard.ControlOffset.X;

                var cyclesPanel = new FlowPanel {
                    Parent              = buildPanel,
                    Width               = 140,
                    Height              = 32,
                    Right               = buildPanel.ContentRegion.Width - Panel.RIGHT_PADDING,
                    Bottom              = thumbnail.Bottom,
                    FlowDirection       = ControlFlowDirection.SingleLeftToRight,
                    ControlPadding      = new Vector2(Control.ControlStandard.ControlOffset.X, 0),
                    OuterControlPadding = new Vector2(Control.ControlStandard.ControlOffset.X, 0)
                };

                foreach (var cycle in Enum.GetValues(typeof(AudioSource.DayCycle)).Cast<AudioSource.DayCycle>().Skip(1)
                                          .Except(new[] { AudioSource.DayCycle.Dawn, AudioSource.DayCycle.Dusk })) {
                    var cb = new Checkbox {
                        Parent  = cyclesPanel,
                        Width   = 100,
                        Height  = 32,
                        Text    = cycle == AudioSource.DayCycle.Day ? Resources.Day : Resources.Night,
                        Checked = AudioSource.HasDayCycle(cycle)
                    };

                    cb.CheckedChanged += (_, e) => {

                        if ((AudioSource.DayCycles & ~cycle) == AudioSource.DayCycle.None) {
                            // At least one cycle must be selected
                            cb.GetPrivateField("_checked").SetValue(cb, !e.Checked); // Skip invoking CheckedChanged
                            GameService.Content.PlaySoundEffectByName("error");
                            return;
                        }

                        var oldCycles = AudioSource.DayCycles;
                        if (e.Checked) {
                            AudioSource.DayCycles |= cycle;
                        } else {
                            AudioSource.DayCycles &= ~cycle;
                        }

                        if (!MusicMixer.Instance.Data.Upsert(AudioSource)) {
                            AudioSource.DayCycles = oldCycles;
                            cb.GetPrivateField("_checked").SetValue(cb, !e.Checked); // Skip invoking CheckedChanged
                            ScreenNotification.ShowNotification(Resources.Something_went_wrong__Please_try_again_, ScreenNotification.NotificationType.Error);
                            GameService.Content.PlaySoundEffectByName("error");
                            return;
                        }

                        GameService.Content.PlaySoundEffectByName("color-change");
                    };
                }

                //slidePanel.SlideIn();

                base.Build(buildPanel);
            }
        }
    }
}
