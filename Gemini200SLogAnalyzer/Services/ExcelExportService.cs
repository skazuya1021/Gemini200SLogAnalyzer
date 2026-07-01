using System.Globalization;
using ClosedXML.Excel;
using Gemini200SLogAnalyzer.Models;

namespace Gemini200SLogAnalyzer.Services;

public static class ExcelExportService
{
    public static void ExportAnalysis(IReadOnlyList<AnalysisRow> rows, IReadOnlyList<string> columnNames, string filePath)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Analysis");

        var headers = new List<string> { "DateTime", "Cassette", "LotID", "RecipeID" };
        headers.AddRange(columnNames);

        for (var c = 0; c < headers.Count; c++)
        {
            worksheet.Cell(1, c + 1).Value = headers[c];
            worksheet.Cell(1, c + 1).Style.Font.Bold = true;
        }

        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            worksheet.Cell(r + 2, 1).Value = row.DateTime;
            worksheet.Cell(r + 2, 1).Style.DateFormat.Format = "yyyy/mm/dd hh:mm:ss";
            worksheet.Cell(r + 2, 2).Value = row.Cassette;
            worksheet.Cell(r + 2, 3).Value = row.LotId;
            worksheet.Cell(r + 2, 4).Value = row.RecipeId;

            for (var c = 0; c < columnNames.Count; c++)
            {
                var columnName = columnNames[c];
                if (row.Values.TryGetValue(columnName, out var value) && value.HasValue)
                {
                    worksheet.Cell(r + 2, c + 5).Value = value.Value;
                }
            }
        }

        worksheet.Columns().AdjustToContents();
        workbook.SaveAs(filePath);
    }
}
