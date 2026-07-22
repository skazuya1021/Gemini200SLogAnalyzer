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
    public static int GetDateTimeColumnIndex(MergedLogData data) =>
        Array.FindIndex(data.AllHeaders, h => h.Equals("DateTime", StringComparison.OrdinalIgnoreCase));

    public static DateTime ResolveGroupDateTime(MergedLogData data, IReadOnlyList<string[]> groupRows)
    {
        var dateTimeIndex = GetDateTimeColumnIndex(data);
        if (dateTimeIndex >= 0)
        {
            foreach (var row in groupRows)
            {
                if (dateTimeIndex < row.Length &&
                    TryParseDateTime(row[dateTimeIndex], out var parsed))
                {
                    return parsed;
                }
            }
        }

        var fileName = groupRows[0][0];
        if (ManualLogFileParser.TryExtractSortDateFromFileName(fileName, out var manualDate))
        {
            return manualDate;
        }

        return LogFileParser.ExtractSortDate(fileName);
    }

    public static (DateTime Min, DateTime Max)? GetDateRange(MergedLogData data)
    {
        var dateTimeIndex = GetDateTimeColumnIndex(data);
        if (dateTimeIndex < 0)
        {
            var groupDates = data.Rows
                .GroupBy(row => (FileName: row[0], Slot: row[4]))
                .Select(group => ResolveGroupDateTime(data, group.ToList()))
                .Where(d => d != DateTime.MinValue)
                .ToList();

            return groupDates.Count == 0 ? null : (groupDates.Min(), groupDates.Max());
        }

        var dates = data.Rows
            .Select(row => dateTimeIndex < row.Length && TryParseDateTime(row[dateTimeIndex], out var parsed)
                ? parsed
                : DateTime.MinValue)
            .Where(d => d != DateTime.MinValue)
            .ToList();

        return dates.Count == 0 ? null : (dates.Min(), dates.Max());
    }

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

        var dateTimeIndex = GetDateTimeColumnIndex(data);

        var columnIndexMap = selected.ToDictionary(
            name => name,
            name => Array.IndexOf(data.AllHeaders, name));

        var results = dateTimeIndex >= 0
            ? AnalyzeWithDateTimeColumn(
                data, selected, statTypes, columnIndexMap, dateTimeIndex,
                dateFrom, dateTo, lotIdFilter, recipeIdFilter)
            : AnalyzeWithoutDateTimeColumn(
                data, selected, statTypes, columnIndexMap,
                dateFrom, dateTo, lotIdFilter, recipeIdFilter);

        return results.OrderBy(r => r.DateTime).ToList();
    }

    private static List<AnalysisRow> AnalyzeWithDateTimeColumn(
        MergedLogData data,
        IReadOnlyList<string> selected,
        IReadOnlyList<StatisticType> statTypes,
        Dictionary<string, int> columnIndexMap,
        int dateTimeIndex,
        DateTime? dateFrom,
        DateTime? dateTo,
        string? lotIdFilter,
        string? recipeIdFilter)
    {
        var groups = data.Rows
            .Select(row => new
            {
                Row = row,
                DateTime = dateTimeIndex < row.Length &&
                           TryParseDateTime(row[dateTimeIndex], out var parsed)
                    ? parsed
                    : (DateTime?)null
            })
            .Where(x => x.DateTime.HasValue)
            .Where(x => PassesMetadataFilters(x.Row, lotIdFilter, recipeIdFilter))
            .Where(x => PassesDateFilters(x.DateTime!.Value, dateFrom, dateTo))
            .GroupBy(x => (FileName: x.Row[0], Slot: x.Row[4], DateTime: x.DateTime!.Value));

        var results = new List<AnalysisRow>();

        foreach (var group in groups)
        {
            var groupRows = group.Select(x => x.Row).ToList();
            var first = groupRows[0];

            results.Add(BuildAnalysisRow(
                group.Key.FileName,
                group.Key.Slot,
                group.Key.DateTime,
                first,
                selected,
                statTypes,
                columnIndexMap,
                groupRows));
        }

        return results;
    }

    private static List<AnalysisRow> AnalyzeWithoutDateTimeColumn(
        MergedLogData data,
        IReadOnlyList<string> selected,
        IReadOnlyList<StatisticType> statTypes,
        Dictionary<string, int> columnIndexMap,
        DateTime? dateFrom,
        DateTime? dateTo,
        string? lotIdFilter,
        string? recipeIdFilter)
    {
        var groups = data.Rows.GroupBy(row => (FileName: row[0], Slot: row[4]));
        var results = new List<AnalysisRow>();

        foreach (var group in groups)
        {
            var groupRows = group.ToList();
            if (groupRows.Count == 0)
            {
                continue;
            }

            var first = groupRows[0];
            if (!PassesMetadataFilters(first, lotIdFilter, recipeIdFilter))
            {
                continue;
            }

            var dateTime = ResolveGroupDateTime(data, groupRows);
            if (!PassesDateFilters(dateTime, dateFrom, dateTo))
            {
                continue;
            }

            results.Add(BuildAnalysisRow(
                group.Key.FileName,
                group.Key.Slot,
                dateTime,
                first,
                selected,
                statTypes,
                columnIndexMap,
                groupRows));
        }

        return results;
    }

    private static bool PassesMetadataFilters(
        string[] row,
        string? lotIdFilter,
        string? recipeIdFilter)
    {
        var lotId = row[1];
        var recipeId = row[3];

        if (!string.IsNullOrWhiteSpace(lotIdFilter) && lotIdFilter != "(すべて)" &&
            !lotId.Equals(lotIdFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(recipeIdFilter) && recipeIdFilter != "(すべて)" &&
            !recipeId.Equals(recipeIdFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static bool PassesDateFilters(DateTime dateTime, DateTime? dateFrom, DateTime? dateTo)
    {
        if (dateFrom.HasValue && dateTime < dateFrom.Value.Date)
        {
            return false;
        }

        if (dateTo.HasValue && dateTime > dateTo.Value.Date.AddDays(1).AddTicks(-1))
        {
            return false;
        }

        return true;
    }

    private static AnalysisRow BuildAnalysisRow(
        string fileName,
        string slot,
        DateTime dateTime,
        string[] first,
        IReadOnlyList<string> selected,
        IReadOnlyList<StatisticType> statTypes,
        Dictionary<string, int> columnIndexMap,
        IReadOnlyList<string[]> groupRows)
    {
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

        return new AnalysisRow
        {
            DateTime = dateTime,
            Cassette = first[2],
            LotId = first[1],
            RecipeId = first[3],
            FileName = fileName,
            Slot = slot,
            Values = values
        };
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
        var dateTimeIndex = GetDateTimeColumnIndex(data);
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (dateTimeIndex >= 0)
        {
            foreach (var row in data.Rows)
            {
                if (dateTimeIndex >= row.Length ||
                    !TryParseDateTime(row[dateTimeIndex], out var dateTime))
                {
                    continue;
                }

                if (!PassesDateFilters(dateTime, dateFrom, dateTo))
                {
                    continue;
                }

                var value = columnIndex < row.Length ? row[columnIndex] : string.Empty;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    values.Add(value);
                }
            }

            return values.OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToList();
        }

        var groups = data.Rows.GroupBy(row => (FileName: row[0], Slot: row[4]));

        foreach (var group in groups)
        {
            var groupRows = group.ToList();
            var first = groupRows[0];
            var dateTime = ResolveGroupDateTime(data, groupRows);

            if (!PassesDateFilters(dateTime, dateFrom, dateTo))
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
