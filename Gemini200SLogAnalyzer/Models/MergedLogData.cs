namespace Gemini200SLogAnalyzer.Models;

public sealed class MergedLogData
{
    public const int MetaColumnCount = 5;

    public static readonly string[] MetaHeaders =
    [
        "FileName",
        "Lot ID",
        "Cassette",
        "RecipeID",
        "Slot"
    ];

    public required string[] AllHeaders { get; init; }
    public required List<string[]> Rows { get; init; }

    public string[] DataColumnNames => AllHeaders.Skip(MetaColumnCount).ToArray();
}
