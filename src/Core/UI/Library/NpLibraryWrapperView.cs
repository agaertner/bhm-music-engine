using Blish_HUD;
using Blish_HUD.Controls;
using Blish_HUD.Graphics.UI;
using Nekres.Music_Mixer.Core.Services.Data;

namespace Nekres.Music_Mixer.Core.UI.Library {

    // Wraps the now playing view element with any library body.
    internal class NpLibraryWrapperView : View {

        private ViewContainer _library;
        private ViewContainer _playing;
        private IView         _libraryView;
        public NpLibraryWrapperView(IView libraryView) {
            MusicMixer.Instance.Audio.MusicChanged += OnMusicChanged;
            _libraryView = libraryView;
        }

        protected override void Unload() {
            MusicMixer.Instance.Audio.MusicChanged -= OnMusicChanged;
            base.Unload();
        }

        private void OnMusicChanged(object sender, ValueEventArgs<AudioSource> e) {
            if (e.Value.IsEmpty) {
                _playing.Clear();
                return;
            }
            _playing.Show(new NowPlayingView(e.Value));
        }

        protected override void Build(Container buildPanel) {
            _playing = new ViewContainer {
                Parent   = buildPanel,
                Width    = buildPanel.ContentRegion.Width,
                Height   = 108,
                Top      = buildPanel.ContentRegion.Height - 108,
                ShowTint = true
            };

            if (!MusicMixer.Instance.Audio.AudioTrack?.IsEmpty ?? false) {
                _playing.Show(new NowPlayingView(MusicMixer.Instance.Audio.AudioTrack.Source));
            }

            _library = new ViewContainer {
                Parent = buildPanel,
                Width  = buildPanel.ContentRegion.Width,
                Height = buildPanel.ContentRegion.Height - _playing.Height - Panel.BOTTOM_PADDING
            };
            _library.Show(_libraryView);
            base.Build(buildPanel);
        }

    }
}
