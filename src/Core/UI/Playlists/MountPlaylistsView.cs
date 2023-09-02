using Blish_HUD.Controls;
using Blish_HUD.Graphics.UI;
using Microsoft.Xna.Framework.Graphics;
using Nekres.Music_Mixer.Core.Services.Data;
using Nekres.Music_Mixer.Core.UI.Library;
using System;
using System.Collections.Generic;
using System.Linq;
using MountType = Gw2Sharp.Models.MountType;

namespace Nekres.Music_Mixer.Core.UI.Playlists {

    public class MountPlaylistsView : View {

        private Dictionary<MountType, Texture2D> _icons;

        public MountPlaylistsView() {
            _icons = Enum.GetValues(typeof(MountType))
                         .Cast<MountType>()
                         .Skip(1)
                         .ToDictionary(x => x,
                                       x => MusicMixer.Instance.ContentsManager
                                                      .GetTexture($"skills/{x.ToString().ToLowerInvariant()}_skill.png"));
        }

        protected override void Unload() {
            foreach (var texture in _icons.Values) {
                texture?.Dispose();
            }

            base.Unload();
        }

        protected override void Build(Container buildPanel) {

            var playlistMenuPanel = new Panel {
                Parent = buildPanel,
                Width  = 150,
                Height = buildPanel.ContentRegion.Height,
                Title  = "Playlists"
            };

            var menu = new Menu {
                Parent = playlistMenuPanel,
                Width  = playlistMenuPanel.ContentRegion.Width,
                Height = playlistMenuPanel.ContentRegion.Height
            };

            var bgmLibraryContainer = new ViewContainer {
                Parent = buildPanel,
                Left   = menu.Right                     + Panel.RIGHT_PADDING,
                Width  = buildPanel.ContentRegion.Width - menu.Width - Panel.RIGHT_PADDING,
                Height = buildPanel.ContentRegion.Height
            };

            var mountTypes = Enum.GetValues(typeof(MountType)).Cast<MountType>().Skip(1);

            foreach (var mountType in mountTypes) {

                var name = mountType.ToString().SplitCamelCase();

                var mountItem = new MenuItem {
                    Text   = name,
                    Parent = menu,
                    Icon   = _icons[mountType]
                };

                mountItem.Click += (_, _) => {

                    MusicMixer.Instance.Resources.PlayMenuItemClick();

                    if (!MusicMixer.Instance.Data.GetMountPlaylist(mountType, out var context)) {
                        context = new Playlist {
                            ExternalId = mountType.ToString(),
                            Tracks     = new List<AudioSource>()
                        };
                    }

                    bgmLibraryContainer.Show(new BgmLibraryView(context, name));
                };
            }

            base.Build(buildPanel);
        }
    }
}
