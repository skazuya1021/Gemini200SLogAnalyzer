namespace Gemini200SLogAnalyzer.Models;

public sealed class VariationAnalysisRow
{
    public int Sequence { get; init; }
    public DateTime DateTime { get; init; }
    public TimeSpan ElapsedFromStart { get; init; }
    public double Value { get; init; }
    public double? DeltaFromPrevious { get; init; }
    public double? DeltaFromStart { get; init; }
    public TimeSpan? IntervalFromPrevious { get; init; }
}
