using Gemini200SLogAnalyzer.Models;

namespace Gemini200SLogAnalyzer.Services;

public static class CsvExportService
{
    public static void Export(MergedLogData data, string filePath)
    {
        using var writer = new StreamWriter(filePath, false, System.Text.Encoding.UTF8);
        writer.WriteLine(CsvHelper.BuildLine(data.AllHeaders));
        foreach (var row in data.Rows)
        {
            writer.WriteLine(CsvHelper.BuildLine(row));
        }
    }
}
