using Drawing = System.Drawing;

namespace BatteryOne
{
    /// <summary>
    /// Draws the battery icon: the percentage on a green / orange / red square, or OFF on grey.
    /// Shared by the tray icon and the Stream Deck plugin so both look the same.
    /// </summary>
    public static class BatteryIcon
    {
        public static readonly Drawing.Color Off = Drawing.Color.FromArgb(0x6E, 0x6E, 0x6E);
        public static readonly Drawing.Color Low = Drawing.Color.FromArgb(0xE8, 0x11, 0x23);
        public static readonly Drawing.Color Medium = Drawing.Color.FromArgb(0xFC, 0xB0, 0x40);
        public static readonly Drawing.Color High = Drawing.Color.FromArgb(0x6C, 0xCB, 0x5F);

        /// <param name="known">False before the first reading: draws "…".</param>
        public static Drawing.Bitmap Render(int size, bool known, bool connected, int percent, int lowThreshold)
        {
            if (!known) return Render(size, "…", Off, Drawing.Color.White);
            if (!connected) return Render(size, "OFF", Off, Drawing.Color.White);
            bool low = percent <= lowThreshold;
            return Render(size, percent.ToString(), low ? Low : percent <= 50 ? Medium : High,
                low ? Drawing.Color.White : Drawing.Color.Black);
        }

        public static Drawing.Bitmap Render(int size, string text, Drawing.Color back, Drawing.Color fore)
        {
            var bmp = new Drawing.Bitmap(size, size);
            using (var g = Drawing.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                using (var brush = new Drawing.SolidBrush(back)) g.FillRectangle(brush, 0, 0, size, size);

                // Same proportions as the original 32 px tray icon: 18 px text, 13 px for three characters
                float fontSize = size * (text.Length >= 3 ? 13f : 18f) / 32f;
                using (var font = new Drawing.Font("Segoe UI", fontSize, Drawing.FontStyle.Bold, Drawing.GraphicsUnit.Pixel))
                using (var brush = new Drawing.SolidBrush(fore))
                using (var format = new Drawing.StringFormat { Alignment = Drawing.StringAlignment.Center, LineAlignment = Drawing.StringAlignment.Center })
                {
                    g.DrawString(text, font, brush, new Drawing.RectangleF(0, 0, size, size), format);
                }
            }
            return bmp;
        }
    }
}
