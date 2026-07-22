using System.Globalization;
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

    public static void ExportAnalysis(
        IReadOnlyList<AnalysisRow> rows,
        IReadOnlyList<string> columnNames,
        string filePath)
    {
        using var writer = new StreamWriter(filePath, false, System.Text.Encoding.UTF8);
        var headers = new List<string> { "DateTime", "Cassette", "LotID", "RecipeID" };
        headers.AddRange(columnNames);
        writer.WriteLine(CsvHelper.BuildLine(headers));

        foreach (var row in rows)
        {
            var fields = new List<string>
            {
                row.DateTime.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture),
                row.Cassette,
                row.LotId,
                row.RecipeId
            };

            foreach (var columnName in columnNames)
            {
                if (row.Values.TryGetValue(columnName, out var value) && value.HasValue)
                {
                    fields.Add(value.Value.ToString(CultureInfo.InvariantCulture));
                }
                else
                {
                    fields.Add(string.Empty);
                }
            }

            writer.WriteLine(CsvHelper.BuildLine(fields));
        }
    }

    public static void ExportVariationAnalysis(
        IReadOnlyList<VariationAnalysisRow> rows,
        string seriesName,
        string filePath)
    {
        using var writer = new StreamWriter(filePath, false, System.Text.Encoding.UTF8);
        var headers = new[]
        {
            "No",
            "DateTime",
            "Elapsed",
            seriesName,
            "DeltaFromPrevious",
            "DeltaFromStart",
            "IntervalFromPrevious"
        };
        writer.WriteLine(CsvHelper.BuildLine(headers));

        foreach (var row in rows)
        {
            var fields = new List<string>
            {
                row.Sequence.ToString(CultureInfo.InvariantCulture),
                row.DateTime.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture),
                FormatElapsed(row.ElapsedFromStart),
                row.Value.ToString(CultureInfo.InvariantCulture),
                row.DeltaFromPrevious?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                row.DeltaFromStart?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                row.IntervalFromPrevious.HasValue
                    ? FormatElapsed(row.IntervalFromPrevious.Value)
                    : string.Empty
            };
            writer.WriteLine(CsvHelper.BuildLine(fields));
        }
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed.TotalDays >= 1)
        {
            return $"{(int)elapsed.TotalDays}d {elapsed.Hours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
        }

        return elapsed.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
    }
}
