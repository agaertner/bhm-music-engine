﻿using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Controls;
using Blish_HUD.Input;
using Glide;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Nekres.Music_Mixer.Core.UI {
    public class RoundedImage : Control {

        private Effect _curvedBorder;

        private AsyncTexture2D _texture;

        private SpriteBatchParameters _defaultParams;
        private SpriteBatchParameters _curvedBorderParams;

        private float _radius = 0.215f;
        private Tween _tween;

        public RoundedImage(AsyncTexture2D texture) {
            _defaultParams = new();
            _curvedBorder  = MusicMixer.Instance.ContentsManager.GetEffect<Effect>(@"effects\curvedborder.mgfx");
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
