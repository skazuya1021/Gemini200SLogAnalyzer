using System.Globalization;
using System.Windows.Data;

namespace Gemini200SLogAnalyzer.Converters;

public sealed class AnalysisValueConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Dictionary<string, double?> values ||
            parameter is not string columnName ||
            !values.TryGetValue(columnName, out var numericValue) ||
            !numericValue.HasValue)
        {
            return string.Empty;
        }

        return numericValue.Value.ToString("G6", CultureInfo.InvariantCulture);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
