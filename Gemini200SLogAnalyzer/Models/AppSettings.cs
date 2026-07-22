namespace Gemini200SLogAnalyzer.Models;

public class AppSettings
{
    public string LastInputFolder { get; set; } = string.Empty;
    public string LastManualLogInputFolder { get; set; } = string.Empty;
    public string LastOutputFolder { get; set; } = string.Empty;
    public string LastOutputFileName { get; set; } = "MergedLog.csv";
    public string LastExcelSaveFolder { get; set; } = string.Empty;
    public string LastChartCsvSaveFolder { get; set; } = string.Empty;
    public string LastChartPngSaveFolder { get; set; } = string.Empty;
    public string LastVariationCsvSaveFolder { get; set; } = string.Empty;
    public string LastVariationPngSaveFolder { get; set; } = string.Empty;
}
