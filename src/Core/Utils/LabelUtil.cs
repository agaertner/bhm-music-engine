using Blish_HUD;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;
using MonoGame.Extended.BitmapFonts;

namespace Nekres.Music_Mixer.Core {
    public static class LabelUtil {
        /// <summary>
        /// Workaround until <see cref="FormattedLabelBuilder.AutoSizeHeight"/> and <see cref="FormattedLabelBuilder.AutoSizeWidth"/> is fixed.
        /// </summary>
        public static Point GetLabelSize(BitmapFont font, string text, bool hasPrefix = false, bool hasSuffix = false) {
            var icon = font.MeasureString("."); // Additional measurement of a single glyph for icons since the text might contain line breaks.
            var size = font.MeasureString(text);

            float width;

            if (hasPrefix && hasSuffix) {
                width = size.Width + icon.Height * 4;
            } else if (hasPrefix || hasSuffix) {
                width = size.Width + icon.Height * 2;
            } else {
                width = size.Width;
            }

            return new Point((int)width, (int)size.Height);
        }

        public static Point GetLabelSize(ContentService.FontSize fontSize, string text, bool hasPrefix = false, bool hasSuffix = false) {
            var font = GameService.Content.GetFont(ContentService.FontFace.Menomonia, fontSize, ContentService.FontStyle.Regular);
            return GetLabelSize(font, text, hasPrefix, hasSuffix);
        }
    }
}
