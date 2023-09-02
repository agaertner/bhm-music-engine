using Blish_HUD.Controls;
using System;

namespace Nekres.Music_Mixer {
    internal static class TrackBarExtensions
    {
        public static void RefreshValue(this TrackBar volumeTrackBar, float value)
        {
            volumeTrackBar.MinValue = Math.Min(volumeTrackBar.MinValue, value);
            volumeTrackBar.MaxValue = Math.Max(volumeTrackBar.MaxValue, value);

            volumeTrackBar.Value = value;
        }
    }
}
