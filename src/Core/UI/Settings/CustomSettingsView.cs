using Blish_HUD.Controls;
using Blish_HUD.Graphics.UI;
using Nekres.Music_Mixer.Properties;

namespace Nekres.Music_Mixer.Core.UI.Settings {
    internal class CustomSettingsView : View {

        private StandardButton _settingsBttn;

        protected override void Build(Container buildPanel) {
            _settingsBttn = new StandardButton {
                Parent = buildPanel,
                Width  = 200,
                Height = 40,
                Left   = (buildPanel.ContentRegion.Width - 200) / 2,
                Top    = buildPanel.ContentRegion.Height / 2 - 40, // Purposefully a bit higher than centered.
                Text   = Resources.Settings
            };

            _settingsBttn.Click += (_, _) => {
                MusicMixer.Instance.ShowSettings();
            };

            base.Build(buildPanel);
        }
    }
}
