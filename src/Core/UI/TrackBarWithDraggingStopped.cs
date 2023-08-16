using Blish_HUD;
using Blish_HUD.Controls;
using Blish_HUD.Input;
using System;

namespace Nekres.Music_Mixer.Core.UI.Controls {
    public class TrackBarWithDraggingStopped : TrackBar {

        public event EventHandler<ValueEventArgs<float>> DraggingStopped;

        private float _dragStartValue;

        public TrackBarWithDraggingStopped() : base()
        {
            /* NOOP */
        }

        protected override void OnLeftMouseButtonReleased(MouseEventArgs e) {
            base.OnLeftMouseButtonReleased(e);

            if (!Dragging && Math.Abs(_dragStartValue - this.Value) > 0.01f) {
                this.DraggingStopped?.Invoke(this, new ValueEventArgs<float>(this.Value));
            }
        }

        protected override void OnLeftMouseButtonPressed(MouseEventArgs e)
        {
            base.OnLeftMouseButtonPressed(e);

            if (Dragging) {
                _dragStartValue = this.Value;
            }
        }
    }
}
