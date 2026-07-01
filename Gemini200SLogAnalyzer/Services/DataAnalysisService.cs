using System.Globalization;
using Gemini200SLogAnalyzer.Models;

namespace Gemini200SLogAnalyzer.Services;

[Flags]
public enum StatisticType
{
    None = 0,
    Median = 1,
    Average = 2,
    Max = 4,
    Min = 8
}

public static class StatisticsHelper
{
    public static double? Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var sorted = values.OrderBy(v => v).ToArray();
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 0
            ? (sorted[mid - 1] + sorted[mid]) / 2.0
            : sorted[mid];
    }

    public static double? Compute(IReadOnlyList<double> values, StatisticType type)
    {
        if (values.Count == 0)
        {
            return null;
        }

        return type switch
        {
            StatisticType.Median => Median(values),
            StatisticType.Average => values.Average(),
            StatisticType.Max => values.Max(),
            StatisticType.Min => values.Min(),
            _ => null
        };
    }

    public static string GetSuffix(StatisticType type) => type switch
    {
        StatisticType.Median => " (Median)",
        StatisticType.Average => " (Average)",
        StatisticType.Max => " (Max)",
        StatisticType.Min => " (Min)",
        _ => string.Empty
    };
}

public sealed class DataAnalysisService
{
    public IReadOnlyList<AnalysisRow> Analyze(
        MergedLogData data,
        IEnumerable<string> selectedColumns,
        StatisticType statistics,
        DateTime? dateFrom,
        DateTime? dateTo,
        string? lotIdFilter,
        string? recipeIdFilter)
    {
        var selected = selectedColumns.ToList();
        if (selected.Count == 0)
        {
            return Array.Empty<AnalysisRow>();
        }

        var statTypes = Enum.GetValues<StatisticType>()
            .Where(s => s != StatisticType.None && statistics.HasFlag(s))
            .ToList();

        if (statTypes.Count == 0)
        {
            statTypes.Add(StatisticType.Median);
        }

        var dateTimeIndex = Array.FindIndex(data.AllHeaders, h =>
            h.Equals("DateTime", StringComparison.OrdinalIgnoreCase));

        var columnIndexMap = selected.ToDictionary(
            name => name,
            name => Array.IndexOf(data.AllHeaders, name));

        var groups = data.Rows
            .GroupBy(row => (FileName: row[0], Slot: row[4]));

        var results = new List<AnalysisRow>();

        foreach (var group in groups)
        {
            var groupRows = group.ToList();
            if (groupRows.Count == 0)
            {
                continue;
            }

            var first = groupRows[0];
            var lotId = first[1];
            var cassette = first[2];
            var recipeId = first[3];

            if (!string.IsNullOrWhiteSpace(lotIdFilter) && lotIdFilter != "(すべて)" &&
                !lotId.Equals(lotIdFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(recipeIdFilter) && recipeIdFilter != "(すべて)" &&
                !recipeId.Equals(recipeIdFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var dateTime = LogFileParser.ExtractSortDate(first[0]);
            if (dateTimeIndex >= 0)
            {
                foreach (var row in groupRows)
                {
                    if (dateTimeIndex < row.Length &&
                        TryParseDateTime(row[dateTimeIndex], out var parsed))
                    {
                        dateTime = parsed;
                        break;
                    }
                }
            }

            if (dateFrom.HasValue && dateTime < dateFrom.Value)
            {
                continue;
            }

            if (dateTo.HasValue && dateTime > dateTo.Value.AddDays(1).AddTicks(-1))
            {
                continue;
            }

            var values = new Dictionary<string, double?>();

            foreach (var column in selected)
            {
                if (!columnIndexMap.TryGetValue(column, out var colIndex) || colIndex < 0)
                {
                    continue;
                }

                var numericValues = groupRows
                    .Select(row => colIndex < row.Length ? row[colIndex] : string.Empty)
                    .Select(TryParseDouble)
                    .Where(v => v.HasValue)
                    .Select(v => v!.Value)
                    .ToList();

                foreach (var stat in statTypes)
                {
                    var key = column + StatisticsHelper.GetSuffix(stat);
                    values[key] = StatisticsHelper.Compute(numericValues, stat);
                }
            }

            results.Add(new AnalysisRow
            {
                DateTime = dateTime,
                Cassette = cassette,
                LotId = lotId,
                RecipeId = recipeId,
                FileName = group.Key.FileName,
                Slot = group.Key.Slot,
                Values = values
            });
        }

        return results.OrderBy(r => r.DateTime).ToList();
    }

    public static bool TryParseDateTime(string value, out DateTime result)
    {
        var formats = new[]
        {
            "yyyy/MM/dd HH:mm:ss",
            "yyyy/M/d H:mm:ss",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy/M/d HH:mm:ss"
        };

        return DateTime.TryParseExact(value.Trim(), formats, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out result)
            || DateTime.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
    }

    public static double? TryParseDouble(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }

        if (double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out result))
        {
            return result;
        }

        return null;
    }

    public IReadOnlyList<string> GetDistinctValues(MergedLogData data, int columnIndex)
    {
        return data.Rows
            .Select(row => columnIndex < row.Length ? row[columnIndex] : string.Empty)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<string> GetDistinctValuesInDateRange(
        MergedLogData data,
        int columnIndex,
        DateTime? dateFrom,
        DateTime? dateTo)
    {
        var dateTimeIndex = Array.FindIndex(data.AllHeaders, h =>
            h.Equals("DateTime", StringComparison.OrdinalIgnoreCase));

        var groups = data.Rows.GroupBy(row => (FileName: row[0], Slot: row[4]));
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            var groupRows = group.ToList();
            var first = groupRows[0];
            var dateTime = LogFileParser.ExtractSortDate(first[0]);

            if (dateTimeIndex >= 0)
            {
                foreach (var row in groupRows)
                {
                    if (dateTimeIndex < row.Length &&
                        TryParseDateTime(row[dateTimeIndex], out var parsed))
                    {
                        dateTime = parsed;
                        break;
                    }
                }
            }

            if (dateFrom.HasValue && dateTime < dateFrom.Value)
            {
                continue;
            }

            if (dateTo.HasValue && dateTime > dateTo.Value.AddDays(1).AddTicks(-1))
            {
                continue;
            }

            var value = columnIndex < first.Length ? first[columnIndex] : string.Empty;
            if (!string.IsNullOrWhiteSpace(value))
            {
                values.Add(value);
            }
        }

        return values.OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
