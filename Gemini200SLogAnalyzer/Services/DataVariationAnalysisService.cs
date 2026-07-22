using Gemini200SLogAnalyzer.Models;

namespace Gemini200SLogAnalyzer.Services;

public enum VariationAnalysisMode
{
    TimeInterval,
    ValueInterval
}

public static class DataVariationAnalysisService
{
    private static readonly double[] NiceSecondSteps =
    [
        1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600, 7200, 14400, 21600, 43200, 86400
    ];

    public static (int Start, int End) OrderIndices(int index1, int index2) =>
        index1 <= index2 ? (index1, index2) : (index2, index1);

    public static double? GetSeriesValue(AnalysisRow row, string seriesName) =>
        row.Values.TryGetValue(seriesName, out var value) ? value : null;

    public static TimeSpan SuggestTimeInterval(IReadOnlyList<AnalysisRow> rows, int startIndex, int endIndex)
    {
        var (lo, hi) = OrderIndices(startIndex, endIndex);
        var span = rows[hi].DateTime - rows[lo].DateTime;
        if (span <= TimeSpan.Zero)
        {
            return TimeSpan.FromMinutes(1);
        }

        var targetSamples = 30;
        var rawSeconds = span.TotalSeconds / targetSamples;
        return TimeSpan.FromSeconds(RoundToNiceSeconds(rawSeconds));
    }

    public static double SuggestValueStep(
        IReadOnlyList<AnalysisRow> rows,
        int startIndex,
        int endIndex,
        string seriesName)
    {
        var (lo, hi) = OrderIndices(startIndex, endIndex);
        var startValue = GetSeriesValue(rows[lo], seriesName);
        var endValue = GetSeriesValue(rows[hi], seriesName);
        if (!startValue.HasValue || !endValue.HasValue)
        {
            return 1;
        }

        var range = Math.Abs(endValue.Value - startValue.Value);
        if (range <= 0)
        {
            return 1;
        }

        var raw = range / 20.0;
        return RoundToNiceValue(raw);
    }

    public static IReadOnlyList<VariationAnalysisRow> AnalyzeByTimeInterval(
        IReadOnlyList<AnalysisRow> rows,
        int startIndex,
        int endIndex,
        string seriesName,
        TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero)
        {
            return Array.Empty<VariationAnalysisRow>();
        }

        var (lo, hi) = OrderIndices(startIndex, endIndex);
        var startRow = rows[lo];
        var endRow = rows[hi];
        var startValue = GetSeriesValue(startRow, seriesName);
        if (!startValue.HasValue)
        {
            return Array.Empty<VariationAnalysisRow>();
        }

        var results = new List<VariationAnalysisRow>
        {
            CreateRow(1, startRow, startValue.Value, startRow.DateTime, null, 0, null)
        };

        var sequence = 2;
        var currentTarget = startRow.DateTime.Add(interval);
        var lastAddedTime = startRow.DateTime;

        while (currentTarget <= endRow.DateTime)
        {
            var rowIndex = FindRowAtOrAfter(rows, lo, hi, currentTarget);
            if (rowIndex >= 0)
            {
                var row = rows[rowIndex];
                var value = GetSeriesValue(row, seriesName);
                if (value.HasValue && row.DateTime > lastAddedTime)
                {
                    var previous = results[^1];
                    results.Add(CreateRow(
                        sequence++,
                        row,
                        value.Value,
                        startRow.DateTime,
                        value.Value - previous.Value,
                        value.Value - startValue.Value,
                        row.DateTime - previous.DateTime));
                    lastAddedTime = row.DateTime;
                }
            }

            currentTarget = currentTarget.Add(interval);
        }

        AppendEndPointIfNeeded(results, endRow, seriesName, startRow.DateTime, startValue.Value, ref sequence);
        return results;
    }

    public static IReadOnlyList<VariationAnalysisRow> AnalyzeByValueInterval(
        IReadOnlyList<AnalysisRow> rows,
        int startIndex,
        int endIndex,
        string seriesName,
        double valueStep)
    {
        if (valueStep <= 0)
        {
            return Array.Empty<VariationAnalysisRow>();
        }

        var (lo, hi) = OrderIndices(startIndex, endIndex);
        var startRow = rows[lo];
        var endRow = rows[hi];
        var startValue = GetSeriesValue(startRow, seriesName);
        var endValue = GetSeriesValue(endRow, seriesName);
        if (!startValue.HasValue || !endValue.HasValue)
        {
            return Array.Empty<VariationAnalysisRow>();
        }

        var results = new List<VariationAnalysisRow>
        {
            CreateRow(1, startRow, startValue.Value, startRow.DateTime, null, 0, null)
        };

        var increasing = endValue.Value >= startValue.Value;
        var nextTarget = startValue.Value + (increasing ? valueStep : -valueStep);
        var sequence = 2;

        for (var i = lo + 1; i <= hi; i++)
        {
            var row = rows[i];
            var value = GetSeriesValue(row, seriesName);
            if (!value.HasValue)
            {
                continue;
            }

            var crossed = increasing
                ? value.Value >= nextTarget
                : value.Value <= nextTarget;

            if (!crossed)
            {
                continue;
            }

            var previous = results[^1];
            results.Add(CreateRow(
                sequence++,
                row,
                value.Value,
                startRow.DateTime,
                value.Value - previous.Value,
                value.Value - startValue.Value,
                row.DateTime - previous.DateTime));

            nextTarget += increasing ? valueStep : -valueStep;

            if (increasing && nextTarget > endValue.Value + valueStep)
            {
                break;
            }

            if (!increasing && nextTarget < endValue.Value - valueStep)
            {
                break;
            }
        }

        AppendEndPointIfNeeded(results, endRow, seriesName, startRow.DateTime, startValue.Value, ref sequence);
        return results;
    }

    private static void AppendEndPointIfNeeded(
        List<VariationAnalysisRow> results,
        AnalysisRow endRow,
        string seriesName,
        DateTime startTime,
        double startValue,
        ref int sequence)
    {
        if (results[^1].DateTime >= endRow.DateTime)
        {
            return;
        }

        var endValue = GetSeriesValue(endRow, seriesName);
        if (!endValue.HasValue)
        {
            return;
        }

        var previous = results[^1];
        results.Add(CreateRow(
            sequence++,
            endRow,
            endValue.Value,
            startTime,
            endValue.Value - previous.Value,
            endValue.Value - startValue,
            endRow.DateTime - previous.DateTime));
    }

    private static VariationAnalysisRow CreateRow(
        int sequence,
        AnalysisRow row,
        double value,
        DateTime startTime,
        double? deltaFromPrevious,
        double deltaFromStart,
        TimeSpan? intervalFromPrevious)
    {
        return new VariationAnalysisRow
        {
            Sequence = sequence,
            DateTime = row.DateTime,
            ElapsedFromStart = row.DateTime - startTime,
            Value = value,
            DeltaFromPrevious = deltaFromPrevious,
            DeltaFromStart = deltaFromStart,
            IntervalFromPrevious = intervalFromPrevious
        };
    }

    private static int FindRowAtOrAfter(
        IReadOnlyList<AnalysisRow> rows,
        int lo,
        int hi,
        DateTime target)
    {
        var left = lo;
        var right = hi;
        var result = -1;

        while (left <= right)
        {
            var mid = (left + right) / 2;
            if (rows[mid].DateTime >= target)
            {
                result = mid;
                right = mid - 1;
            }
            else
            {
                left = mid + 1;
            }
        }

        return result;
    }

    private static double RoundToNiceSeconds(double seconds)
    {
        if (seconds <= 1)
        {
            return 1;
        }

        foreach (var step in NiceSecondSteps)
        {
            if (seconds <= step)
            {
                return step;
            }
        }

        return NiceSecondSteps[^1];
    }

    private static double RoundToNiceValue(double value)
    {
        if (value <= 0)
        {
            return 1;
        }

        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)));
        var normalized = value / magnitude;

        double nice = normalized switch
        {
            <= 1 => 1,
            <= 2 => 2,
            <= 5 => 5,
            _ => 10
        };

        return nice * magnitude;
    }
}
