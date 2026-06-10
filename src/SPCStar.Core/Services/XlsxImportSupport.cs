using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SPCStar.Core.Services;

public static class XlsxImportSupport
{
    private static readonly XNamespace SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelationshipNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static string ReadImportSheetAsCsv(Stream workbookStream, string preferredSheetName = "SPC-Star Import")
    {
        using var archive = new ZipArchive(workbookStream, ZipArchiveMode.Read, leaveOpen: true);
        var sharedStrings = ReadSharedStrings(archive);
        var sheetPath = FindWorksheetPath(archive, preferredSheetName);
        var sheetEntry = archive.GetEntry(sheetPath) ?? throw new InvalidOperationException($"Worksheet '{preferredSheetName}' was not found.");

        using var sheetStream = sheetEntry.Open();
        var document = XDocument.Load(sheetStream);
        var rows = document.Descendants(SpreadsheetNs + "sheetData")
            .Elements(SpreadsheetNs + "row")
            .Select(row => ReadRow(row, sharedStrings))
            .ToArray();

        if (rows.Length == 0)
        {
            throw new InvalidOperationException("The selected workbook sheet is empty.");
        }

        var lastColumn = rows.Max(row => row.Count == 0 ? 0 : row.Keys.Max());
        var matrix = rows
            .Select(row => Enumerable.Range(1, lastColumn)
                .Select(index => row.GetValueOrDefault(index) ?? string.Empty)
                .ToArray())
            .Where(row => row.Any(value => !string.IsNullOrWhiteSpace(value)))
            .ToArray();

        if (matrix.Length == 0)
        {
            throw new InvalidOperationException("The selected workbook sheet is empty.");
        }

        var headers = matrix[0].Select(value => value.Trim()).ToArray();
        var dataRows = matrix.Skip(1)
            .Select(values =>
            {
                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < headers.Length; i++)
                {
                    row[headers[i]] = i < values.Length ? values[i].Trim() : string.Empty;
                }

                return row;
            });

        return CsvSupport.WriteRows(headers, dataRows);
    }

    private static IReadOnlyList<string> ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return [];
        }

        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        return document.Descendants(SpreadsheetNs + "si")
            .Select(item => string.Concat(item.Descendants(SpreadsheetNs + "t").Select(text => text.Value)))
            .ToArray();
    }

    private static string FindWorksheetPath(ZipArchive archive, string preferredSheetName)
    {
        var workbookEntry = archive.GetEntry("xl/workbook.xml") ?? throw new InvalidOperationException("Workbook metadata was not found.");
        using var workbookStream = workbookEntry.Open();
        var workbook = XDocument.Load(workbookStream);

        var relationshipEntry = archive.GetEntry("xl/_rels/workbook.xml.rels") ?? throw new InvalidOperationException("Workbook relationships were not found.");
        using var relationshipStream = relationshipEntry.Open();
        var relationships = XDocument.Load(relationshipStream)
            .Root?
            .Elements(PackageRelationshipNs + "Relationship")
            .ToDictionary(
                relationship => relationship.Attribute("Id")?.Value ?? string.Empty,
                relationship => relationship.Attribute("Target")?.Value ?? string.Empty,
                StringComparer.OrdinalIgnoreCase) ?? [];

        var sheets = workbook.Descendants(SpreadsheetNs + "sheet").ToArray();
        var selected = sheets.FirstOrDefault(sheet =>
            string.Equals(sheet.Attribute("name")?.Value, preferredSheetName, StringComparison.OrdinalIgnoreCase)) ?? sheets.FirstOrDefault();
        if (selected is null)
        {
            throw new InvalidOperationException("Workbook does not contain any worksheets.");
        }

        var relationshipId = selected.Attribute(RelationshipNs + "id")?.Value ?? string.Empty;
        if (!relationships.TryGetValue(relationshipId, out var target) || string.IsNullOrWhiteSpace(target))
        {
            throw new InvalidOperationException($"Worksheet '{selected.Attribute("name")?.Value}' could not be resolved.");
        }

        target = target.Replace('\\', '/');
        if (target.StartsWith('/'))
        {
            return target.TrimStart('/');
        }

        return target.StartsWith("xl/", StringComparison.OrdinalIgnoreCase) ? target : $"xl/{target}";
    }

    private static Dictionary<int, string> ReadRow(XElement row, IReadOnlyList<string> sharedStrings)
    {
        var values = new Dictionary<int, string>();
        var nextColumn = 1;

        foreach (var cell in row.Elements(SpreadsheetNs + "c"))
        {
            var column = ColumnIndex(cell.Attribute("r")?.Value) ?? nextColumn;
            values[column] = ReadCellValue(cell, sharedStrings);
            nextColumn = column + 1;
        }

        return values;
    }

    private static string ReadCellValue(XElement cell, IReadOnlyList<string> sharedStrings)
    {
        var type = cell.Attribute("t")?.Value;
        if (type == "inlineStr")
        {
            return string.Concat(cell.Descendants(SpreadsheetNs + "t").Select(text => text.Value));
        }

        var rawValue = cell.Element(SpreadsheetNs + "v")?.Value ?? string.Empty;
        if (string.IsNullOrEmpty(rawValue))
        {
            return string.Empty;
        }

        return type switch
        {
            "s" when int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) && index >= 0 && index < sharedStrings.Count => sharedStrings[index],
            "b" => rawValue == "1" ? "TRUE" : "FALSE",
            _ => rawValue
        };
    }

    private static int? ColumnIndex(string? cellReference)
    {
        if (string.IsNullOrWhiteSpace(cellReference))
        {
            return null;
        }

        var match = Regex.Match(cellReference, "^[A-Za-z]+");
        if (!match.Success)
        {
            return null;
        }

        var index = 0;
        foreach (var c in match.Value.ToUpperInvariant())
        {
            index = index * 26 + (c - 'A' + 1);
        }

        return index;
    }
}
