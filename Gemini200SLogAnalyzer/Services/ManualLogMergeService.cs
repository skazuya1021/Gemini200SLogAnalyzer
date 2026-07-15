using Gemini200SLogAnalyzer.Models;

namespace Gemini200SLogAnalyzer.Services;

public sealed class ManualLogMergeService
{
    public async Task<MergedLogData> MergeAsync(
        IEnumerable<string> filePaths,
        IProgress<(int current, int total, string message)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var sortedPaths = filePaths
            .Where(ManualLogFileParser.IsManualLogFile)
            .Select(p => new { Path = p, Date = ManualLogFileParser.ExtractSortDate(p) })
            .OrderBy(x => x.Date)
            .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Path)
            .ToList();

        if (sortedPaths.Count == 0)
        {
            throw new InvalidOperationException("処理対象のManualLogファイルがありません。");
        }

        var total = sortedPaths.Count;
        var parsedFiles = new ParsedManualLogFile[total];
        var processed = 0;

        await Task.Run(() =>
        {
            Parallel.ForEach(
                sortedPaths.Select((path, index) => (path, index)),
                new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = Environment.ProcessorCount
                },
                item =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    parsedFiles[item.index] = ManualLogFileParser.Parse(item.path);
                    var count = Interlocked.Increment(ref processed);
                    progress?.Report((count, total, $"ManualLog読込中: {Path.GetFileName(item.path)}"));
                });
        }, cancellationToken);

        var orderedParsed = parsedFiles.OrderBy(f => f.SortDate)
            .ThenBy(f => f.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var first = orderedParsed[0];
        var dataHeaders = first.DataHeaders;
        var allHeaders = MergedLogData.MetaHeaders.Concat(dataHeaders).ToArray();
        var rows = new List<string[]>();

        for (var fileIndex = 0; fileIndex < orderedParsed.Count; fileIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parsed = orderedParsed[fileIndex];
            progress?.Report((fileIndex + 1, total, $"ManualLog合体中: {parsed.FileName}"));

            foreach (var dataRow in parsed.DataRows)
            {
                var row = new string[allHeaders.Length];
                row[0] = parsed.FileName;
                row[1] = parsed.LotId;
                row[2] = parsed.Cassette;
                row[3] = parsed.RecipeId;
                row[4] = parsed.Slot;

                for (var col = 0; col < dataHeaders.Length; col++)
                {
                    var value = col < dataRow.Length ? dataRow[col] : string.Empty;
                    row[5 + col] = value;
                }

                rows.Add(row);
            }
        }

        return new MergedLogData
        {
            AllHeaders = allHeaders,
            Rows = rows
        };
    }
}
