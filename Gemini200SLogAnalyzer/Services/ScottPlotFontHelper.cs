using ScottPlot;

namespace Gemini200SLogAnalyzer.Services;

public static class ScottPlotFontHelper
{
    private const string JapaneseSample = "分析サンプル";
    private const string FallbackFont = "Yu Gothic UI";

    public static void ApplyJapaneseFont(Plot plot)
    {
        var fontName = Fonts.Detect(JapaneseSample);
        if (string.IsNullOrWhiteSpace(fontName))
        {
            fontName = FallbackFont;
        }

        plot.Font.Set(fontName);
        plot.Font.Automatic();
    }
}
