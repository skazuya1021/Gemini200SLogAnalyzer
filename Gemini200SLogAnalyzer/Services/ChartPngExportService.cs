using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScottPlot;

namespace Gemini200SLogAnalyzer.Services;

public static class ChartPngExportService
{
    private const int DefaultWidth = 1200;
    private const int DefaultHeight = 700;
    private const double Padding = 16;
    private const double LineHeight = 16;
    private const double FontSize = 12;

    public static void Save(Plot plot, string filePath, string? annotationText, Visual dpiVisual)
    {
        if (string.IsNullOrWhiteSpace(annotationText))
        {
            plot.SavePng(filePath, DefaultWidth, DefaultHeight);
            return;
        }

        var tempChartPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        try
        {
            plot.SavePng(tempChartPath, DefaultWidth, DefaultHeight);

            var chartBitmap = LoadBitmap(tempChartPath);
            var lines = annotationText.Replace("\r\n", "\n").Split('\n');
            var textHeight = Padding * 2 + lines.Length * LineHeight;
            var totalHeight = DefaultHeight + textHeight;
            var dpi = VisualTreeHelper.GetDpi(dpiVisual);

            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.DrawRectangle(Brushes.White, null, new Rect(0, 0, DefaultWidth, totalHeight));
                context.DrawImage(chartBitmap, new Rect(0, 0, DefaultWidth, DefaultHeight));

                var typeface = new Typeface(new FontFamily("Yu Gothic UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                var y = DefaultHeight + Padding;

                foreach (var line in lines)
                {
                    var formattedText = new FormattedText(
                        line,
                        CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        typeface,
                        FontSize,
                        Brushes.Black,
                        dpi.PixelsPerDip);

                    context.DrawText(formattedText, new Point(Padding, y));
                    y += LineHeight;
                }
            }

            var renderTarget = new RenderTargetBitmap(
                DefaultWidth,
                (int)Math.Ceiling(totalHeight),
                dpi.PixelsPerDip * 96,
                dpi.PixelsPerDip * 96,
                PixelFormats.Pbgra32);
            renderTarget.Render(visual);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(renderTarget));

            using var stream = File.Open(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            encoder.Save(stream);
        }
        finally
        {
            if (File.Exists(tempChartPath))
            {
                File.Delete(tempChartPath);
            }
        }
    }

    private static BitmapImage LoadBitmap(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
