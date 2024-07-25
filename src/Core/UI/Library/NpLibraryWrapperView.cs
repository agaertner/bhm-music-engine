using Blish_HUD.Controls;
using Blish_HUD.Graphics.UI;

namespace Nekres.Music_Mixer.Core.UI.Library {

    // Wraps the now playing view element with any library body.
    internal class NpLibraryWrapperView : View {

        private ViewContainer _library;
        private ViewContainer _playing;
        private IView         _libraryView;
        public NpLibraryWrapperView(IView libraryView) {
            _libraryView = libraryView;
        }

        protected override void Build(Container buildPanel) {
            _playing = new ViewContainer {
                Parent   = buildPanel,
                Width    = buildPanel.ContentRegion.Width,
                Height   = 140,
                Top      = buildPanel.ContentRegion.Height - 140,
                ShowTint = true,
                ShowBorder = true
            };
            _playing.Show(new NowPlayingView());

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
