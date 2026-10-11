using System.Windows.Media;

namespace GuessMelody.Services
{
    public class ColorPickerService
    {
        /// <summary>Возвращает HEX (#RRGGBB) или null, если отменено.</summary>
        public string PickHex(string initialHex = "#FFFFFF")
        {
            using (var dlg = new System.Windows.Forms.ColorDialog())
            {
                dlg.FullOpen = true;
                dlg.AnyColor = true;

                var initial = ParseHexToDrawing(initialHex);
                if (initial.HasValue) dlg.Color = initial.Value;

                var result = dlg.ShowDialog();
                if (result != System.Windows.Forms.DialogResult.OK) return null;

                var c = dlg.Color;
                return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            }
        }

        private static System.Drawing.Color? ParseHexToDrawing(string hex)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(hex)) return null;
                var c = (Color)ColorConverter.ConvertFromString(hex);
                return System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B);
            }
            catch { return null; }
        }
    }
}