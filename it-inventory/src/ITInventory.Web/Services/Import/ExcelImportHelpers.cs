using ClosedXML.Excel;
using ITInventory.Data.Common;

namespace ITInventory.Web.Services.Import;

public static class ExcelImportHelpers
{
    public static Dictionary<string, int> ReadHeaders(IXLWorksheet ws)
    {
        var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var headerRow = ws.Row(1);
        var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;

        for (var col = 1; col <= lastCol; col++)
        {
            var text = headerRow.Cell(col).GetString().Trim();
            if (!string.IsNullOrEmpty(text) && !headers.ContainsKey(text))
                headers[text] = col;
        }

        return headers;
    }

    public static string? GetString(IXLWorksheet ws, int row, Dictionary<string, int> headers, string header)
    {
        if (!headers.TryGetValue(header, out var col)) return null;
        var value = ws.Cell(row, col).GetString().Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    public static DateTime? GetDate(IXLWorksheet ws, int row, Dictionary<string, int> headers, string header)
    {
        if (!headers.TryGetValue(header, out var col)) return null;

        var cell = ws.Cell(row, col);
        if (cell.IsEmpty()) return null;

        if (cell.DataType == XLDataType.DateTime)
            return cell.GetDateTime();

        var text = cell.GetString().Trim();
        if (string.IsNullOrEmpty(text)) return null;

        return DateTime.TryParse(text, out var parsed) ? parsed : null;
    }

    public static int? GetInt(IXLWorksheet ws, int row, Dictionary<string, int> headers, string header)
    {
        var text = GetString(ws, row, headers, header);
        return text is not null && int.TryParse(text, out var value) ? value : null;
    }

    public static bool IsRowEmpty(IXLWorksheet ws, int row, Dictionary<string, int> headers)
    {
        foreach (var col in headers.Values)
        {
            if (!ws.Cell(row, col).IsEmpty()) return false;
        }
        return true;
    }

    public static bool TryParseApplianceType(string? raw, out ApplianceType value, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            value = ApplianceType.Physical;
            return true;
        }

        if (raw.Trim().Equals("Physical", StringComparison.OrdinalIgnoreCase))
        {
            value = ApplianceType.Physical;
            return true;
        }

        if (raw.Trim().Equals("Virtual", StringComparison.OrdinalIgnoreCase))
        {
            value = ApplianceType.Virtual;
            return true;
        }

        value = ApplianceType.Physical;
        error = $"Unrecognized Physical/Virtual value '{raw}' (expected 'Physical' or 'Virtual').";
        return false;
    }

    public static byte[] CreateTemplateBytes(string title, params string[] headers)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add(title);

        for (var i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F1F5F9");
        }

        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
