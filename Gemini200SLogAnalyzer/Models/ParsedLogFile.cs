namespace Gemini200SLogAnalyzer.Models;

public sealed class ParsedLogFile
{
    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public required DateTime SortDate { get; init; }
    public required string LotId { get; init; }
    public required string Cassette { get; init; }
    public required string RecipeId { get; init; }
    public required string Slot { get; init; }
    public required string[] DataHeaders { get; init; }
    public required List<string[]> DataRows { get; init; }
}
