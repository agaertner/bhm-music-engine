using Blish_HUD.Controls;
using Glide;
using System;

namespace Nekres.Music_Mixer.Core.UI {
    public class SlidePanel : Panel {

        private Tween _tween;

        public void SlideIn() {
            _tween?.CancelAndComplete();
            _tween = Animation.Tweener.Tween(this, new { Left = 0, Opacity = 1f }, 0.3f)
                              .Ease(Ease.CubeOut)
                              .OnComplete(Invalidate);
        }

        public void SlideOut(Action onComplete = null) {
            _tween?.CancelAndComplete();
            _tween = Animation.Tweener.Tween(this, new { Left = Width, Opacity = 0f }, 0.3f)
                              .Ease(Ease.CubeIn)
                              .OnComplete(onComplete);
        }
    }
}
