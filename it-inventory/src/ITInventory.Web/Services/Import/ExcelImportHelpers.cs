using ClosedXML.Excel;
using ITInventory.Data.Common;

namespace ITInventory.Web.Services.Import;

public static class ExcelImportHelpers
{
    private const long MaxUploadBytes = 10 * 1024 * 1024; // 10 MB

    /// <summary>
    /// Rejects missing/empty files, anything that isn't a .xlsx by extension, anything over
    /// 10 MB, and anything whose bytes don't actually start like an .xlsx, before it ever
    /// reaches ClosedXML.
    ///
    /// The extension is the uploader's claim about the file; the signature is the file itself.
    /// Renaming something.exe to sheet.xlsx used to get it as far as the parser -- which is
    /// exactly the "the extension is not the content type" case the upload rules are about.
    /// The country topology upload has checked signatures since it was written
    /// (ContentMatchesExtension); this brings the Excel imports in line.
    /// </summary>
    public static bool IsValidUpload(IFormFile? file, out string error)
    {
        if (file is null || file.Length == 0)
        {
            error = "Please choose a file.";
            return false;
        }

        if (!string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            error = "Only .xlsx files are supported.";
            return false;
        }

        if (file.Length > MaxUploadBytes)
        {
            error = "File is too large (max 10 MB).";
            return false;
        }

        if (!HasXlsxSignature(file))
        {
            error = "This file is not a valid .xlsx workbook.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>
    /// .xlsx is an OOXML package, i.e. a ZIP archive, so it starts with the ZIP local-file
    /// header "PK\x03\x04" -- the same first four bytes as .docx/.pptx/.vsdx. The stream is
    /// rewound afterwards so the caller can still read the whole file.
    /// </summary>
    private static bool HasXlsxSignature(IFormFile file)
    {
        Span<byte> head = stackalloc byte[4];
        using var stream = file.OpenReadStream();
        var read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        return read == 4 && head[0] == 0x50 && head[1] == 0x4B && head[2] == 0x03 && head[3] == 0x04;
    }

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

    public static bool TryParseLocationCategory(string? raw, out LocationCategory value, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            value = LocationCategory.Local;
            return true;
        }

        var trimmed = raw.Trim();
        if (trimmed.Equals("Local", StringComparison.OrdinalIgnoreCase))
        {
            value = LocationCategory.Local;
            return true;
        }

        if (trimmed.Equals("Turkiye", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("Türkiye", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("EVM", StringComparison.OrdinalIgnoreCase))
        {
            value = LocationCategory.Turkiye;
            return true;
        }

        if (trimmed.Equals("Cloud", StringComparison.OrdinalIgnoreCase))
        {
            value = LocationCategory.Cloud;
            return true;
        }

        value = LocationCategory.Local;
        error = $"Unrecognized Location Category value '{raw}' (expected 'Local', 'Türkiye' or 'Cloud').";
        return false;
    }

    public static bool TryParseSiteRole(string? raw, out SiteRole value, out string? error)
    {
        error = null;
        value = SiteRole.Primary;

        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "Site Role is required (expected 'Primary Datacenter' or 'Disaster Recovery').";
            return false;
        }

        var trimmed = raw.Trim();
        if (trimmed.Equals("Primary", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("Primary Datacenter", StringComparison.OrdinalIgnoreCase))
        {
            value = SiteRole.Primary;
            return true;
        }

        if (trimmed.Equals("DisasterRecovery", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("Disaster Recovery", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("DR", StringComparison.OrdinalIgnoreCase))
        {
            value = SiteRole.DisasterRecovery;
            return true;
        }

        error = $"Unrecognized Site Role value '{raw}' (expected 'Primary Datacenter' or 'Disaster Recovery').";
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

    public static byte[] CreateExportBytes(string title, string[] headers, IEnumerable<object?[]> rows)
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

        var rowNum = 2;
        foreach (var row in rows)
        {
            for (var i = 0; i < row.Length; i++)
            {
                var cell = ws.Cell(rowNum, i + 1);
                switch (row[i])
                {
                    case null:
                        break;
                    case DateTime dt:
                        cell.Value = dt;
                        cell.Style.DateFormat.Format = "dd.MM.yyyy";
                        break;
                    case int iv:
                        cell.Value = iv;
                        break;
                    default:
                        cell.Value = row[i]!.ToString();
                        break;
                }
            }
            rowNum++;
        }

        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
