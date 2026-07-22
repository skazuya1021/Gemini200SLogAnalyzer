using Gemini200SLogAnalyzer.Models;

namespace Gemini200SLogAnalyzer.Services;

public static class VariationPlotLodService
{
    public const int DefaultMaxPoints = 2000;

    public static IReadOnlyList<AnalysisRow> GetSegmentRows(
        IReadOnlyList<AnalysisRow> rows,
        int startIndex,
        int endIndex)
    {
        var (lo, hi) = DataVariationAnalysisService.OrderIndices(startIndex, endIndex);
        return rows.Skip(lo).Take(hi - lo + 1).ToList();
    }

    public static IReadOnlyList<(double X, double Y)> GetPlotPoints(
        IReadOnlyList<AnalysisRow> source,
        string seriesName,
        double xMin,
        double xMax,
        int maxPoints = DefaultMaxPoints)
    {
        if (source.Count == 0)
        {
            return Array.Empty<(double X, double Y)>();
        }

        if (xMin > xMax)
        {
            (xMin, xMax) = (xMax, xMin);
        }

        var visible = new List<(double X, double Y)>();

        foreach (var row in source)
        {
            var x = row.DateTime.ToOADate();
            if (x < xMin || x > xMax)
            {
                continue;
            }

            var value = DataVariationAnalysisService.GetSeriesValue(row, seriesName);
            if (value.HasValue)
            {
                visible.Add((x, value.Value));
            }
        }

        return DecimateIfNeeded(visible, maxPoints);
    }

    public static IReadOnlyList<(double X, double Y)> GetScatterPlotPoints(
        IReadOnlyList<AnalysisRow> source,
        string xAxisName,
        string yAxisName,
        double xMin,
        double xMax,
        int maxPoints = DefaultMaxPoints)
    {
        if (source.Count == 0)
        {
            return Array.Empty<(double X, double Y)>();
        }

        if (xMin > xMax)
        {
            (xMin, xMax) = (xMax, xMin);
        }

        var visible = new List<(double X, double Y)>();

        foreach (var row in source)
        {
            var x = GetAxisValue(row, xAxisName);
            var y = GetAxisValue(row, yAxisName);
            if (!x.HasValue || !y.HasValue || x.Value < xMin || x.Value > xMax)
            {
                continue;
            }

            visible.Add((x.Value, y.Value));
        }

        return DecimateIfNeeded(visible, maxPoints);
    }

    public static int CountPointsInTimeRange(
        IReadOnlyList<AnalysisRow> source,
        string seriesName,
        double xMin,
        double xMax)
    {
        if (xMin > xMax)
        {
            (xMin, xMax) = (xMax, xMin);
        }

        var count = 0;
        foreach (var row in source)
        {
            var x = row.DateTime.ToOADate();
            if (x < xMin || x > xMax)
            {
                continue;
            }

            if (DataVariationAnalysisService.GetSeriesValue(row, seriesName).HasValue)
            {
                count++;
            }
        }

        return count;
    }

    public static int CountScatterPointsInRange(
        IReadOnlyList<AnalysisRow> source,
        string xAxisName,
        string yAxisName,
        double xMin,
        double xMax)
    {
        if (xMin > xMax)
        {
            (xMin, xMax) = (xMax, xMin);
        }

        var count = 0;
        foreach (var row in source)
        {
            var x = GetAxisValue(row, xAxisName);
            var y = GetAxisValue(row, yAxisName);
            if (x.HasValue && y.HasValue && x.Value >= xMin && x.Value <= xMax)
            {
                count++;
            }
        }

        return count;
    }

    public static (double Min, double Max) GetDateTimeRange(IReadOnlyList<AnalysisRow> rows)
    {
        if (rows.Count == 0)
        {
            return (0, 0);
        }

        return (rows[0].DateTime.ToOADate(), rows[^1].DateTime.ToOADate());
    }

    public static (double Min, double Max) GetAxisRange(
        IReadOnlyList<AnalysisRow> rows,
        string axisName)
    {
        double? min = null;
        double? max = null;

        foreach (var row in rows)
        {
            var value = GetAxisValue(row, axisName);
            if (!value.HasValue)
            {
                continue;
            }

            min = min.HasValue ? Math.Min(min.Value, value.Value) : value.Value;
            max = max.HasValue ? Math.Max(max.Value, value.Value) : value.Value;
        }

        return min.HasValue && max.HasValue ? (min.Value, max.Value) : (0, 0);
    }

    private static double? GetAxisValue(AnalysisRow row, string axisName)
    {
        if (axisName.Equals("DateTime", StringComparison.OrdinalIgnoreCase))
        {
            return row.DateTime.ToOADate();
        }

        return DataVariationAnalysisService.GetSeriesValue(row, axisName);
    }

    private static IReadOnlyList<(double X, double Y)> DecimateIfNeeded(
        IReadOnlyList<(double X, double Y)> points,
        int maxPoints)
    {
        if (points.Count <= maxPoints)
        {
            return points;
        }

        return DecimateUniform(points, maxPoints);
    }

    private static List<(double X, double Y)> DecimateUniform(
        IReadOnlyList<(double X, double Y)> points,
        int maxPoints)
    {
        var result = new List<(double X, double Y)>(maxPoints);
        var step = (double)points.Count / maxPoints;

        for (var index = 0.0; index < points.Count; index += step)
        {
            result.Add(points[(int)index]);
        }

        var last = points[^1];
        if (result.Count == 0 || Math.Abs(result[^1].X - last.X) > double.Epsilon)
        {
            result.Add(last);
        }

        return result;
    }
}
