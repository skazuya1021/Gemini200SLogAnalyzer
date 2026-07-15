using System.Globalization;
using System.Text.RegularExpressions;
using Gemini200SLogAnalyzer.Models;

namespace Gemini200SLogAnalyzer.Services;

public static class ManualLogFileParser
{
    private static readonly Regex CreatedLinePattern = new(
        @"Log file created\s*:\s*(.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex FileNamePattern = new(
        @"^(\d{4})(\d{2})(\d{2})(\d{2})_([Ll]\d+)",
        RegexOptions.Compiled);

    public static bool IsManualLogFile(string filePath)
    {
        if (!filePath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            using var reader = new StreamReader(filePath);
            var firstLine = reader.ReadLine();
            return firstLine is not null &&
                   firstLine.StartsWith("Log file created", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static DateTime ExtractSortDate(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var match = FileNamePattern.Match(fileName);
        if (match.Success)
        {
            var year = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var month = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            var day = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
            var hour = int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture);
            return new DateTime(year, month, day, hour, 0, 0);
        }

        try
        {
            return ExtractCreatedDate(File.ReadLines(filePath).First());
        }
        catch
        {
            var fileInfo = new FileInfo(filePath);
            return fileInfo.Exists ? fileInfo.LastWriteTime : DateTime.MinValue;
        }
    }

    public static ParsedManualLogFile Parse(string filePath)
    {
        var lines = File.ReadAllLines(filePath);
        if (lines.Length < 4)
        {
            throw new InvalidDataException($"ManualLogファイルの行数が不足しています: {filePath}");
        }

        if (!CreatedLinePattern.IsMatch(lines[0]))
        {
            throw new InvalidDataException($"ManualLog形式ではありません: {filePath}");
        }

        var createdDate = ExtractCreatedDate(lines[0]);
        var cassette = ExtractCassette(filePath);
        var headers = CsvHelper.ParseLine(lines[1])
            .Select(h => h.Trim())
            .Where(h => !string.IsNullOrEmpty(h))
            .Select(h => h.Equals("Time", StringComparison.OrdinalIgnoreCase) ? "DateTime" : h)
            .ToArray();

        if (headers.Length == 0)
        {
            throw new InvalidDataException($"項目名が見つかりません: {filePath}");
        }

        var dataRows = new List<string[]>();
        for (var i = 3; i < lines.Length; i++)
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

            if (fields.Length > 0)
            {
                fields[0] = BuildDateTimeString(createdDate, fields[0]);
            }

            dataRows.Add(fields);
        }

        return new ParsedManualLogFile
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            SortDate = ExtractSortDate(filePath),
            LotId = "ManualLog",
            Cassette = cassette,
            RecipeId = string.Empty,
            Slot = string.Empty,
            DataHeaders = headers,
            DataRows = dataRows
        };
    }

    private static DateTime ExtractCreatedDate(string createdLine)
    {
        var match = CreatedLinePattern.Match(createdLine);
        if (!match.Success)
        {
            throw new InvalidDataException("Log file created 行を解析できません。");
        }

        var value = match.Groups[1].Value.Trim();
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ||
            DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out parsed))
        {
            return parsed;
        }

        throw new InvalidDataException($"作成日時を解析できません: {value}");
    }

    private static string ExtractCassette(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var match = FileNamePattern.Match(fileName);
        if (match.Success)
        {
            return match.Groups[5].Value.ToUpperInvariant();
        }

        var parent = Path.GetFileName(Path.GetDirectoryName(filePath));
        if (!string.IsNullOrWhiteSpace(parent) &&
            parent.StartsWith('L') &&
            parent.Length >= 2 &&
            char.IsDigit(parent[1]))
        {
            return parent.ToUpperInvariant();
        }

        return string.Empty;
    }

    private static string BuildDateTimeString(DateTime baseDate, string timeValue)
    {
        if (TimeSpan.TryParse(timeValue.Trim(), CultureInfo.InvariantCulture, out var time) ||
            TimeSpan.TryParse(timeValue.Trim(), CultureInfo.CurrentCulture, out time))
        {
            var dateTime = baseDate.Date + time;
            return dateTime.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        return timeValue;
    }
}
