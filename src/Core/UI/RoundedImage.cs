using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Controls;
using Blish_HUD.Input;
using Glide;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Nekres.Music_Mixer.Core.UI {
    public class RoundedImage : Control {

        private Effect _curvedBorder;

        public AsyncTexture2D Texture;
        public bool           ShowLoadingSpinner;

        private SpriteBatchParameters _defaultParams;
        private SpriteBatchParameters _curvedBorderParams;

        private float _radius = 0.215f;
        private Tween _tween;
        
        public RoundedImage() {
            _defaultParams = new();
            _curvedBorder  = MusicMixer.Instance.ContentsManager.GetEffect<Effect>(@"effects\curvedborder.mgfx");
            _curvedBorderParams = new() {
                Effect = _curvedBorder
            };
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

        protected override void OnLeftMouseButtonPressed(MouseEventArgs e) {
            _tween?.Cancel();
            _tween = Animation.Tweener.Tween(this, new {_radius = 0.415f}, 0.03f);
            base.OnLeftMouseButtonPressed(e);
        }

        protected override void OnLeftMouseButtonReleased(MouseEventArgs e) {
            _tween?.Cancel();
            _tween = Animation.Tweener.Tween(this, new { _radius = 0.315f }, 0.05f);
            base.OnLeftMouseButtonReleased(e);
        }

        protected override void Paint(SpriteBatch spriteBatch, Rectangle bounds) {
            if (Texture == null || !Texture.HasTexture || !Texture.HasSwapped) {
                LoadingSpinnerUtil.DrawLoadingSpinner(this, spriteBatch, new Rectangle((bounds.Width - 32) / 2, (bounds.Height - 32) / 2, 32, 32));
                return;
            }

            _curvedBorder.Parameters["Radius"].SetValue(_radius);
            _curvedBorder.Parameters["Opacity"].SetValue(this.Opacity);

            spriteBatch.End();
            spriteBatch.Begin(_curvedBorderParams);
            spriteBatch.DrawOnCtrl(this, Texture, new Rectangle(0, 0, this.Width, this.Height));
            spriteBatch.End();
            spriteBatch.Begin(_defaultParams);

            if (ShowLoadingSpinner) {
                LoadingSpinnerUtil.DrawLoadingSpinner(this, spriteBatch, new Rectangle((bounds.Width - 64) / 2, (bounds.Height - 64) / 2, 64, 64));
            }
        }

    }
}
