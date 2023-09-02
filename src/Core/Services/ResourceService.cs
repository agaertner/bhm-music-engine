using Blish_HUD;
using Microsoft.Xna.Framework.Audio;
using System;
using System.Collections.Generic;

namespace Nekres.Music_Mixer.Core.Services {
    public class ResourceService : IDisposable {

        private IReadOnlyList<SoundEffect> _menuClicks;
        private SoundEffect                _menuItemClickSfx;

        public ResourceService() {
            LoadSounds();
        }

        private void LoadSounds() {
            _menuItemClickSfx = MusicMixer.Instance.ContentsManager.GetSound(@"audio\menu-item-click.wav");
            _menuClicks = new List<SoundEffect> {
                MusicMixer.Instance.ContentsManager.GetSound(@"audio\menu-click-1.wav"),
                MusicMixer.Instance.ContentsManager.GetSound(@"audio\menu-click-2.wav"),
                MusicMixer.Instance.ContentsManager.GetSound(@"audio\menu-click-3.wav"),
                MusicMixer.Instance.ContentsManager.GetSound(@"audio\menu-click-4.wav")
            };
        }

        public void PlayMenuItemClick() {
            _menuItemClickSfx.Play(GameService.GameIntegration.Audio.Volume, 0, 0);
        }

        public void PlayMenuClick() {
            _menuClicks[RandomUtil.GetRandom(0, 3)].Play(GameService.GameIntegration.Audio.Volume, 0, 0);
        }

        public void Dispose() {
            _menuItemClickSfx.Dispose();
            foreach (var sfx in _menuClicks) {
                sfx.Dispose();
            }
        }
    }
}
