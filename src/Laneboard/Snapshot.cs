using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Laneboard;

/// `--snapshot out.png [--demo] [--theme light|dark] [--settings]`: renders the window to a PNG without showing it.
static class Snapshot
{
    public static async Task<int> Run(string outPath, bool demo, string? theme, bool settings, string? view = null, int? solidity = null)
    {
        try
        {
            var s = demo ? new Settings { Roots = [@"C:\dev"], PinnedProjects = [@"C:\dev\tidewatch-api"] } : Settings.Load();
            if (theme is "dark" or "light") s.Theme = theme == "dark" ? "Dark" : "Light";
            if (view is "advanced" or "light") s.View = view == "advanced" ? "Advanced" : "Light";
            if (solidity is { } so) s.Solidity = so;

            var w = new MainWindow(s, demo);
            await w.RefreshAsync(true);
            if (settings) w.ShowSettings(true);

            const double W = 440, H = 760, scale = 2;
            var root = w.Root;
            root.Measure(new Size(W, H));
            root.Arrange(new Rect(0, 0, W, H));
            root.UpdateLayout();

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var bg = Application.Current.TryFindResource("SolidBackgroundFillColorBaseBrush") as Brush ?? Brushes.White;
                dc.DrawRectangle(bg, null, new Rect(0, 0, W, H));
            }
            var bmp = new RenderTargetBitmap((int)(W * scale), (int)(H * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Render(root); // drawn as laid out, never stretched
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using var f = File.Create(outPath);
            enc.Save(f);
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(outPath + ".error.txt", ex.ToString());
            return 1;
        }
    }
}
