namespace Gemini200SLogAnalyzer.Models;

public sealed class AnalysisRow
{
    public DateTime DateTime { get; init; }
    public string Cassette { get; init; } = string.Empty;
    public string LotId { get; init; } = string.Empty;
    public string RecipeId { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string Slot { get; init; } = string.Empty;
    public Dictionary<string, double?> Values { get; init; } = new();
}
