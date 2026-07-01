using System.IO;
using System.Globalization;
using System.Text.RegularExpressions;
using Gemini200SLogAnalyzer.Models;

namespace Gemini200SLogAnalyzer.Services;

public static class LogFileParser
{
    private static readonly Regex FileNameDatePattern = new(
        @"^(\d{4})-(\d{4})-(\d{6})",
        RegexOptions.Compiled);

    public static DateTime ExtractSortDate(string fileName)
    {
        var match = FileNameDatePattern.Match(Path.GetFileNameWithoutExtension(fileName));
        if (match.Success)
        {
            var year = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var monthDay = match.Groups[2].Value;
            var timePart = match.Groups[3].Value;
            var month = int.Parse(monthDay[..2], CultureInfo.InvariantCulture);
            var day = int.Parse(monthDay[2..], CultureInfo.InvariantCulture);
            var hour = int.Parse(timePart[..2], CultureInfo.InvariantCulture);
            var minute = int.Parse(timePart[2..4], CultureInfo.InvariantCulture);
            var second = int.Parse(timePart[4..], CultureInfo.InvariantCulture);
            return new DateTime(year, month, day, hour, minute, second);
        }

        var fileInfo = new FileInfo(fileName);
        return fileInfo.Exists ? fileInfo.LastWriteTime : DateTime.MinValue;
    }

    public static ParsedLogFile Parse(string filePath)
    {
        var lines = File.ReadAllLines(filePath);
        if (lines.Length < 15)
        {
            throw new InvalidDataException($"ログファイルの行数が不足しています: {filePath}");
        }

        var lotId = ExtractValue(lines, 1);
        var cassette = ExtractValue(lines, 2);
        var recipeId = ExtractValue(lines, 3);
        var slot = ExtractValue(lines, 8);

        var dataHeaders = CsvHelper.ParseLine(lines[12])
            .Select(h => h.Trim())
            .Where(h => !string.IsNullOrEmpty(h))
            .ToArray();

        if (dataHeaders.Length == 0)
        {
            throw new InvalidDataException($"項目名が見つかりません: {filePath}");
        }

        var dataRows = new List<string[]>();
        for (var i = 14; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                continue;
            }

            var fields = CsvHelper.ParseLine(lines[i]);
            if (fields.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            dataRows.Add(fields);
        }

        return new ParsedLogFile
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            SortDate = ExtractSortDate(filePath),
            LotId = lotId,
            Cassette = cassette,
            RecipeId = recipeId,
            Slot = slot,
            DataHeaders = dataHeaders,
            DataRows = dataRows
        };
    }

    private static string ExtractValue(string[] lines, int zeroBasedLineIndex)
    {
        if (zeroBasedLineIndex >= lines.Length)
        {
            return string.Empty;
        }

        var fields = CsvHelper.ParseLine(lines[zeroBasedLineIndex]);
        return fields.Length > 1 ? fields[1].Trim() : string.Empty;
    }
}
