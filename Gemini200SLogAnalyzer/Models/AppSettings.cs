namespace Gemini200SLogAnalyzer.Models;

public class AppSettings
{
    public string LastInputFolder { get; set; } = string.Empty;
    public string LastOutputFolder { get; set; } = string.Empty;
    public string LastOutputFileName { get; set; } = "MergedLog.csv";
}
