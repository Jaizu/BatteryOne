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
        public static readonly Drawing.Color Charging = Drawing.Color.FromArgb(0x4C, 0xC2, 0xFF);

        /// <param name="known">False before the first reading: draws "…".</param>
        /// <param name="charging">The wheel is on the charger: blue, with a bolt when there is room for it.</param>
        public static Drawing.Bitmap Render(int size, bool known, bool connected, int percent, int lowThreshold, bool charging = false)
        {
            if (!known) return Render(size, "…", Off, Drawing.Color.White);
            if (!connected) return Render(size, "OFF", Off, Drawing.Color.White);
            if (charging) return Render(size, percent.ToString(), Charging, Drawing.Color.Black, bolt: size >= 64);
            bool low = percent <= lowThreshold;
            return Render(size, percent.ToString(), low ? Low : percent <= 50 ? Medium : High,
                low ? Drawing.Color.White : Drawing.Color.Black);
        }

        public static Drawing.Bitmap Render(int size, string text, Drawing.Color back, Drawing.Color fore, bool bolt = false)
        {
            var bmp = new Drawing.Bitmap(size, size);
            using (var g = Drawing.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                using (var brush = new Drawing.SolidBrush(back)) g.FillRectangle(brush, 0, 0, size, size);

                // Same proportions as the original 32 px tray icon: 18 px text, 13 px for three characters.
                // With a bolt, the text moves to the lower part of the square and gets smaller.
                float fontSize = size * (text.Length >= 3 ? 13f : 18f) / 32f * (bolt ? 0.75f : 1f);
                var textArea = bolt ? new Drawing.RectangleF(0, size * 0.36f, size, size * 0.58f) : new Drawing.RectangleF(0, 0, size, size);
                using (var font = new Drawing.Font("Segoe UI", fontSize, Drawing.FontStyle.Bold, Drawing.GraphicsUnit.Pixel))
                using (var brush = new Drawing.SolidBrush(fore))
                using (var format = new Drawing.StringFormat { Alignment = Drawing.StringAlignment.Center, LineAlignment = Drawing.StringAlignment.Center })
                {
                    g.DrawString(text, font, brush, textArea, format);
                    if (bolt) g.FillPolygon(brush, Bolt(size / 2f, size * 0.07f, size * 0.32f));
                }
            }
            return bmp;
        }

        /// <summary>A lightning bolt <paramref name="height"/> tall, centered on <paramref name="centerX"/>.</summary>
        static Drawing.PointF[] Bolt(float centerX, float top, float height)
        {
            float u = height / 10f;
            float x = centerX - 3f * u;
            return new[]
            {
                new Drawing.PointF(x + 4f * u, top),
                new Drawing.PointF(x + 0f * u, top + 5.6f * u),
                new Drawing.PointF(x + 2.8f * u, top + 5.6f * u),
                new Drawing.PointF(x + 2f * u, top + 10f * u),
                new Drawing.PointF(x + 6f * u, top + 4.2f * u),
                new Drawing.PointF(x + 3.2f * u, top + 4.2f * u),
            };
        }
    }
}
