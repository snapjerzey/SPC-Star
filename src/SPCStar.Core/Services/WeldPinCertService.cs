using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SPCStar.Core.Services;

public sealed record WeldPinCertPreviewSummary(
    int InventoryBoxes,
    int ReadyBoxes,
    int BlockedBoxes);

public sealed record WeldPinCertRequestPreview(
    string PurchaseOrder,
    string OrderNumber,
    string OrderDate,
    string PartNum,
    string PartDescription,
    string ShipBy,
    string NeedBy,
    decimal RequestedQuantity,
    string RawMaterialPartNum,
    string RawMaterialLotNumber,
    string RawMaterialHeatNumber);

public sealed record WeldPinCertPreviewBox(
    string BoxNumber,
    string FinishedGoodsLotNumber,
    string FinishedGoodsBaseLot,
    decimal Quantity,
    string ToteNumber,
    string KeyenceLotNumber,
    string MeasurementTime,
    IReadOnlyList<WeldPinCertMeasurementPreview> Measurements,
    string Status,
    IReadOnlyList<string> Messages);

public sealed record WeldPinCertMeasurementPreview(
    int SerialCounter,
    decimal DeltaE,
    string OkNg);

public sealed record WeldPinCertPreviewResult(
    WeldPinCertPreviewSummary Summary,
    WeldPinCertRequestPreview Request,
    IReadOnlyList<WeldPinCertPreviewBox> Boxes);

public sealed record GeneratedWeldPinCert(
    string FileName,
    string ContentType,
    byte[] Content);

public sealed class WeldPinCertService
{
    private const string WeldPinPartNum = "71350";
    private const string WeldPinRawMaterialPartNum = "51207";
    private const string ExcelWorkbookContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private static readonly CultureInfo UsCulture = CultureInfo.GetCultureInfo("en-US");

    public ServiceResult<WeldPinCertPreviewResult> Preview(Stream requestWorkbook, Stream keyenceCsv)
    {
        var request = ParseRequest(requestWorkbook);
        var keyenceRows = ParseKeyenceRows(keyenceCsv);
        var result = BuildPreview(request, keyenceRows);
        return result.Succeeded
            ? ServiceResult<WeldPinCertPreviewResult>.Ok(result.Value!)
            : ServiceResult<WeldPinCertPreviewResult>.Fail(result.Errors);
    }

    public ServiceResult<GeneratedWeldPinCert> Generate(Stream requestWorkbook, Stream keyenceCsv, Stream templateWorkbook)
    {
        var request = ParseRequest(requestWorkbook);
        var keyenceRows = ParseKeyenceRows(keyenceCsv);
        var preview = BuildPreview(request, keyenceRows);
        if (!preview.Succeeded)
        {
            return ServiceResult<GeneratedWeldPinCert>.Fail(preview.Errors);
        }

        var result = preview.Value!;
        var blocked = result.Boxes.Where(box => box.Status != "Ready").ToArray();
        if (blocked.Length > 0)
        {
            return ServiceResult<GeneratedWeldPinCert>.Fail(blocked.SelectMany(box => box.Messages));
        }

        var workbook = GenerateWorkbook(result, templateWorkbook);
        var safePo = SafeFileName(result.Request.PurchaseOrder);
        var safeLot = SafeFileName(FirstNonBlank(result.Boxes.Select(box => box.FinishedGoodsBaseLot)));
        return ServiceResult<GeneratedWeldPinCert>.Ok(new GeneratedWeldPinCert(
            $"Weld-Pin-Cert-{safePo}-{safeLot}.xlsx",
            ExcelWorkbookContentType,
            workbook));
    }

    private static ServiceResult<WeldPinCertPreviewResult> BuildPreview(WeldPinCertRequest request, IReadOnlyList<KeyenceRow> keyenceRows)
    {
        var errors = new List<string>();
        if (request.Inventory.Count == 0)
        {
            errors.Add("No selected finished-goods inventory rows were found in the Weld Pin cert request.");
        }

        if (keyenceRows.Count == 0)
        {
            errors.Add("No Keyence inspection rows were found in the uploaded CSV.");
        }

        if (string.IsNullOrWhiteSpace(request.PurchaseOrder))
        {
            errors.Add("The Weld Pin cert request is missing the customer PO.");
        }

        if (string.IsNullOrWhiteSpace(request.RawMaterialHeatNumber))
        {
            errors.Add("The Weld Pin cert request is missing the raw material heat number.");
        }

        if (errors.Count > 0)
        {
            return ServiceResult<WeldPinCertPreviewResult>.Fail(errors);
        }

        var boxes = request.Inventory
            .OrderBy(item => NumericSortValue(item.BoxNumber))
            .ThenBy(item => item.BoxNumber, StringComparer.OrdinalIgnoreCase)
            .Select(item => BuildPreviewBox(item, keyenceRows))
            .ToArray();

        var preview = new WeldPinCertPreviewResult(
            new WeldPinCertPreviewSummary(
                boxes.Length,
                boxes.Count(box => box.Status == "Ready"),
                boxes.Count(box => box.Status != "Ready")),
            new WeldPinCertRequestPreview(
                request.PurchaseOrder,
                request.OrderNumber,
                request.OrderDate,
                WeldPinPartNum,
                request.PartDescription,
                request.ShipBy,
                request.NeedBy,
                request.RequestedQuantity,
                WeldPinRawMaterialPartNum,
                request.RawMaterialLotNumber,
                request.RawMaterialHeatNumber),
            boxes);

        return ServiceResult<WeldPinCertPreviewResult>.Ok(preview);
    }

    private static WeldPinCertPreviewBox BuildPreviewBox(FinishedGoodsInventory item, IReadOnlyList<KeyenceRow> keyenceRows)
    {
        var messages = new List<string>();
        var matchingRows = keyenceRows
            .Where(row => row.ToteNumberNormalized == item.BoxNumberNormalized)
            .Where(row => !IsSpecialInspection(row.LotNumber))
            .OrderBy(row => row.MeasurementTime ?? DateTime.MaxValue)
            .ThenBy(row => row.SourceOrder)
            .ToArray();

        var eventGroup = matchingRows
            .GroupBy(row => NormalizeEventKey(row.LotNumber), StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .Select(group => CompleteMeasurementSet(group.ToArray()))
            .Where(group => group is not null)
            .OrderBy(group => group!.MeasurementTime)
            .ThenBy(group => group!.SourceOrder)
            .FirstOrDefault();

        if (eventGroup is null)
        {
            messages.Add($"Box {item.BoxNumber} does not have a complete normal Keyence set with Serial Counter 1, 2, and 3.");
            return new WeldPinCertPreviewBox(
                item.BoxNumber,
                item.FinishedGoodsLotNumber,
                item.FinishedGoodsBaseLot,
                item.Quantity,
                item.BoxNumberNormalized,
                "",
                "",
                [],
                "Blocked",
                messages);
        }

        messages.Add("Ready. First complete normal Keyence set selected.");
        return new WeldPinCertPreviewBox(
            item.BoxNumber,
            item.FinishedGoodsLotNumber,
            item.FinishedGoodsBaseLot,
            item.Quantity,
            item.BoxNumberNormalized,
            eventGroup.KeyenceLotNumber,
            eventGroup.MeasurementTime.ToString("M/d/yyyy h:mm:ss tt", UsCulture),
            eventGroup.Measurements
                .OrderBy(measurement => measurement.SerialCounter)
                .Select(measurement => new WeldPinCertMeasurementPreview(measurement.SerialCounter, measurement.DeltaE, measurement.OkNg))
                .ToArray(),
            "Ready",
            messages);
    }

    private static CompleteKeyenceSet? CompleteMeasurementSet(IReadOnlyList<KeyenceRow> rows)
    {
        var measurements = new List<KeyenceMeasurement>();
        foreach (var counter in new[] { 1, 2, 3 })
        {
            var row = rows
                .Where(item => item.SerialCounter == counter && item.DeltaE.HasValue)
                .OrderBy(item => item.MeasurementTime ?? DateTime.MaxValue)
                .ThenBy(item => item.SourceOrder)
                .FirstOrDefault();
            if (row is null || !row.MeasurementTime.HasValue)
            {
                return null;
            }

            measurements.Add(new KeyenceMeasurement(counter, row.DeltaE!.Value, row.OkNg));
        }

        var first = rows
            .Where(row => row.MeasurementTime.HasValue)
            .OrderBy(row => row.MeasurementTime!.Value)
            .ThenBy(row => row.SourceOrder)
            .First();
        return new CompleteKeyenceSet(
            NormalizeEventKey(first.LotNumber),
            first.MeasurementTime!.Value,
            first.SourceOrder,
            measurements);
    }

    private static WeldPinCertRequest ParseRequest(Stream requestWorkbook)
    {
        var sheets = SimpleWorkbook.Read(requestWorkbook);
        var sheet = sheets.FirstOrDefault()
            ?? throw new InvalidOperationException("The Weld Pin cert request workbook does not contain a worksheet.");

        var epicor = ParseEpicorRequest(sheet);
        var inventory = ParseInventoryRows(sheet);
        var rawMaterialLot = FindValueBesideLabel(sheet, "Raw Material Lot Number");
        var rawMaterialHeat = FindValueBesideLabel(sheet, "QA - Raw Material Heat Number");
        var rawMaterialPart = FirstNonBlank([FindValueBesideLabel(sheet, "Raw Material Part"), WeldPinRawMaterialPartNum]);

        return new WeldPinCertRequest(
            epicor.PurchaseOrder,
            epicor.OrderNumber,
            epicor.OrderDate,
            FirstNonBlank([epicor.PartNum, WeldPinPartNum]),
            epicor.PartDescription,
            epicor.ShipBy,
            epicor.NeedBy,
            epicor.RequestedQuantity,
            rawMaterialPart,
            rawMaterialLot,
            rawMaterialHeat,
            inventory);
    }

    private static EpicorRequest ParseEpicorRequest(WorkbookSheet sheet)
    {
        var headerRow = FindHeaderRow(sheet, ["Entry Person", "PO", "Order", "Order Date", "Part", "Req Qty"]);
        if (headerRow == 0)
        {
            return new EpicorRequest("", "", "", WeldPinPartNum, "", "", "", 0m);
        }

        var headers = HeadersForRow(sheet, headerRow);
        for (var rowIndex = headerRow + 1; rowIndex <= sheet.MaxRow; rowIndex++)
        {
            var row = RowDictionary(sheet, rowIndex, headers);
            if (string.IsNullOrWhiteSpace(Value(row, "PO")) &&
                string.IsNullOrWhiteSpace(Value(row, "Order")) &&
                string.IsNullOrWhiteSpace(Value(row, "Part")))
            {
                continue;
            }

            return new EpicorRequest(
                CleanWorkbookDisplayNumber(Value(row, "PO")),
                CleanWorkbookDisplayNumber(Value(row, "Order")),
                FormatWorkbookDate(Value(row, "Order Date")),
                CleanWorkbookDisplayNumber(FirstNonBlank([Value(row, "Part"), WeldPinPartNum])),
                Value(row, "Part Description"),
                FormatWorkbookDate(Value(row, "Ship By")),
                FormatWorkbookDate(Value(row, "Need By")),
                ParseDecimal(Value(row, "Req Qty")) ?? 0m);
        }

        return new EpicorRequest("", "", "", WeldPinPartNum, "", "", "", 0m);
    }

    private static IReadOnlyList<FinishedGoodsInventory> ParseInventoryRows(WorkbookSheet sheet)
    {
        var headerRow = FindHeaderRow(sheet, ["Company", "Part", "Warehouse", "On Hand", "Lot Number"]);
        if (headerRow == 0)
        {
            return [];
        }

        var headers = HeadersForRow(sheet, headerRow);
        var rows = new List<FinishedGoodsInventory>();
        for (var rowIndex = headerRow + 1; rowIndex <= sheet.MaxRow; rowIndex++)
        {
            var row = RowDictionary(sheet, rowIndex, headers);
            var lotNumber = Value(row, "Lot Number");
            if (string.IsNullOrWhiteSpace(lotNumber))
            {
                continue;
            }

            var part = CleanWorkbookDisplayNumber(Value(row, "Part"));
            if (!string.IsNullOrWhiteSpace(part) && !part.Equals(WeldPinPartNum, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var boxNumber = BoxNumberFromFinishedGoodsLot(lotNumber);
            if (string.IsNullOrWhiteSpace(boxNumber))
            {
                continue;
            }

            rows.Add(new FinishedGoodsInventory(
                lotNumber,
                FinishedGoodsBaseLot(lotNumber),
                boxNumber,
                NormalizeNumericKey(boxNumber),
                ParseDecimal(Value(row, "On Hand")) ?? 0m));
        }

        return rows;
    }

    private static IReadOnlyList<KeyenceRow> ParseKeyenceRows(Stream keyenceCsv)
    {
        using var reader = new StreamReader(keyenceCsv, leaveOpen: true);
        var rows = CsvSupport.ReadRows(reader.ReadToEnd());
        var result = new List<KeyenceRow>();
        var sourceOrder = 0;
        foreach (var row in rows)
        {
            sourceOrder++;
            var toteNumber = Value(row, "Tote Number");
            var delta = ParseDecimal(Value(row, "2:[1].998 Delta E"));
            if (!delta.HasValue)
            {
                var deltaKey = row.Keys.FirstOrDefault(key => key.Contains("Delta E", StringComparison.OrdinalIgnoreCase));
                delta = deltaKey is null ? null : ParseDecimal(Value(row, deltaKey));
            }

            result.Add(new KeyenceRow(
                sourceOrder,
                ParseDateTime(Value(row, "Measurement time")),
                Value(row, "OK/NG"),
                Value(row, "Lot Number"),
                ParseSerialCounter(Value(row, "Serial Counter")),
                toteNumber,
                NormalizeNumericKey(toteNumber),
                delta));
        }

        return result;
    }

    private static byte[] GenerateWorkbook(WeldPinCertPreviewResult result, Stream templateWorkbook)
    {
        if (templateWorkbook.CanSeek)
        {
            templateWorkbook.Position = 0;
        }

        using var output = new MemoryStream();
        templateWorkbook.CopyTo(output);
        output.Position = 0;

        using (var archive = new ZipArchive(output, ZipArchiveMode.Update, leaveOpen: true))
        {
            var sheetPaths = SimpleWorkbook.SheetPaths(archive).ToDictionary(item => item.Name.Trim(), item => item.Path, StringComparer.OrdinalIgnoreCase);
            var cocPath = sheetPaths.FirstOrDefault(item => item.Key.Equals("QF-225 COC", StringComparison.OrdinalIgnoreCase)).Value
                ?? throw new InvalidOperationException("The Weld Pin cert template is missing the QF-225 COC sheet.");
            var dataPath = sheetPaths.TryGetValue("QF-225-Data", out var path)
                ? path
                : throw new InvalidOperationException("The Weld Pin cert template is missing the QF-225-Data sheet.");

            UpdateWorksheet(archive, cocPath, sheet => FillCocSheet(sheet, result));
            UpdateWorksheet(archive, dataPath, sheet => FillDataSheet(sheet, result));
            RemoveCalculationChain(archive);
            ForceFullCalculationOnOpen(archive);
        }

        return output.ToArray();
    }

    private static void FillCocSheet(XDocument sheet, WeldPinCertPreviewResult result)
    {
        var request = result.Request;
        var boxes = result.Boxes.OrderBy(box => NumericSortValue(box.BoxNumber)).ThenBy(box => box.BoxNumber, StringComparer.OrdinalIgnoreCase).ToArray();
        SetCellText(sheet, 12, 3, WeldPinPartNum);
        SetCellText(sheet, 14, 9, request.PurchaseOrder);
        SetCellText(sheet, 18, 3, request.OrderNumber);
        SetCellText(sheet, 18, 9, FirstNonBlank(boxes.Select(box => box.FinishedGoodsBaseLot)));
        SetCellNumber(sheet, 20, 3, boxes.Sum(box => box.Quantity));
        SetCellText(sheet, 20, 9, string.Join(", ", boxes.Select(box => box.BoxNumber)));
        SetCellText(sheet, 22, 9, request.ShipBy);
        SetCellText(sheet, 24, 4, WeldPinRawMaterialPartNum);
        SetCellText(sheet, 26, 4, request.RawMaterialHeatNumber);
        SetCellText(sheet, 28, 4, request.RawMaterialLotNumber);
    }

    private static void FillDataSheet(XDocument sheet, WeldPinCertPreviewResult result)
    {
        var boxes = result.Boxes.OrderBy(box => NumericSortValue(box.BoxNumber)).ThenBy(box => box.BoxNumber, StringComparer.OrdinalIgnoreCase).ToArray();
        if (boxes.Length > 11)
        {
            throw new InvalidOperationException("The Weld Pin cert template supports up to 11 boxes. Split the request or use an expanded approved template.");
        }

        for (var row = 7; row <= 39; row++)
        {
            for (var column = 7; column <= 10; column++)
            {
                ClearCellValue(sheet, row, column);
            }
        }

        for (var boxIndex = 0; boxIndex < boxes.Length; boxIndex++)
        {
            var box = boxes[boxIndex];
            var startRow = 7 + (boxIndex * 3);
            SetCellText(sheet, startRow, 7, box.BoxNumber);
            var measurements = box.Measurements.OrderBy(measurement => measurement.SerialCounter).ToArray();
            for (var measurementIndex = 0; measurementIndex < measurements.Length; measurementIndex++)
            {
                var row = startRow + measurementIndex;
                var measurement = measurements[measurementIndex];
                SetCellText(sheet, row, 8, box.KeyenceLotNumber);
                SetCellNumber(sheet, row, 9, measurement.DeltaE);
                SetCellText(sheet, row, 10, measurement.OkNg);
            }
        }
    }

    private static int FindHeaderRow(WorkbookSheet sheet, IReadOnlyCollection<string> requiredHeaders)
    {
        for (var row = 1; row <= sheet.MaxRow; row++)
        {
            var values = Enumerable.Range(1, sheet.MaxColumn)
                .Select(column => NormalizeHeader(sheet.Cell(row, column)))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (requiredHeaders.Select(NormalizeHeader).All(values.Contains))
            {
                return row;
            }
        }

        return 0;
    }

    private static Dictionary<string, int> HeadersForRow(WorkbookSheet sheet, int row)
    {
        var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var column = 1; column <= sheet.MaxColumn; column++)
        {
            var header = sheet.Cell(row, column);
            if (!string.IsNullOrWhiteSpace(header))
            {
                headers[NormalizeHeader(header)] = column;
            }
        }

        return headers;
    }

    private static Dictionary<string, string> RowDictionary(WorkbookSheet sheet, int row, IReadOnlyDictionary<string, int> headers)
    {
        return headers.ToDictionary(
            item => item.Key,
            item => sheet.Cell(row, item.Value),
            StringComparer.OrdinalIgnoreCase);
    }

    private static string FindValueBesideLabel(WorkbookSheet sheet, string label)
    {
        var normalizedLabel = NormalizeHeader(label);
        foreach (var cell in sheet.Cells)
        {
            if (!NormalizeHeader(cell.Value).Equals(normalizedLabel, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            for (var column = cell.Key.Column + 1; column <= Math.Min(sheet.MaxColumn, cell.Key.Column + 4); column++)
            {
                var value = sheet.Cell(cell.Key.Row, column);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return CleanWorkbookDisplayNumber(value);
                }
            }
        }

        return "";
    }

    private static bool IsSpecialInspection(string value)
    {
        var normalized = NormalizeHeader(value);
        return normalized.Contains("startup", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("setup", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("materialchange", StringComparison.OrdinalIgnoreCase);
    }

    private static string BoxNumberFromFinishedGoodsLot(string value)
    {
        var match = Regex.Match(value.Trim(), @"(\d+)\s*$");
        return match.Success ? match.Groups[1].Value : "";
    }

    private static string FinishedGoodsBaseLot(string value)
    {
        return Regex.Replace(value.Trim(), @"\s*\d+\s*$", "").Trim();
    }

    private static string NormalizeEventKey(string value) => value.Trim();

    private static string NormalizeNumericKey(string value)
    {
        var digits = Regex.Match(value, @"\d+").Value;
        if (string.IsNullOrWhiteSpace(digits))
        {
            return value.Trim();
        }

        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : digits.TrimStart('0');
    }

    private static int ParseSerialCounter(string value)
    {
        var digits = Regex.Match(value, @"\d+").Value;
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    }

    private static DateTime? ParseDateTime(string value)
    {
        if (DateTime.TryParse(value, UsCulture, DateTimeStyles.AssumeLocal, out var parsed) ||
            DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out parsed))
        {
            return parsed;
        }

        return null;
    }

    private static decimal? ParseDecimal(string value)
    {
        return decimal.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsed) ||
            decimal.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, UsCulture, out parsed)
            ? parsed
            : null;
    }

    private static string FormatWorkbookDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) && serial > 20000)
        {
            return DateTime.FromOADate(serial).ToString("M/d/yyyy", UsCulture);
        }

        return DateTime.TryParse(value, UsCulture, DateTimeStyles.None, out var parsed) ||
            DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)
            ? parsed.ToString("M/d/yyyy", UsCulture)
            : value;
    }

    private static string CleanWorkbookDisplayNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return value.Trim();
        }

        return parsed.ToString("0.###############", CultureInfo.InvariantCulture);
    }

    private static string NormalizeHeader(string value)
    {
        return Regex.Replace(value ?? "", @"[^A-Za-z0-9]+", "").ToLowerInvariant();
    }

    private static string Value(IReadOnlyDictionary<string, string> row, string key)
    {
        return row.TryGetValue(NormalizeHeader(key), out var normalizedValue)
            ? normalizedValue
            : row.TryGetValue(key, out var value) ? value : "";
    }

    private static string FirstNonBlank(IEnumerable<string> values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
    }

    private static decimal NumericSortValue(string value)
    {
        return decimal.TryParse(Regex.Match(value, @"\d+").Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : decimal.MaxValue;
    }

    private static string SafeFileName(string value)
    {
        var safe = Regex.Replace(value, @"[^A-Za-z0-9._-]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(safe) ? "WeldPin" : safe;
    }

    private static void UpdateWorksheet(ZipArchive archive, string path, Action<XDocument> update)
    {
        var entry = archive.GetEntry(path) ?? throw new InvalidOperationException($"Worksheet '{path}' was not found.");
        XDocument document;
        using (var stream = entry.Open())
        {
            document = XDocument.Load(stream, LoadOptions.PreserveWhitespace);
        }

        update(document);
        entry.Delete();
        var updatedEntry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var output = updatedEntry.Open();
        document.Save(output, SaveOptions.DisableFormatting);
    }

    private static void SetCellText(XDocument sheet, int rowIndex, int columnIndex, string value)
    {
        var cell = GetOrCreateCell(sheet, rowIndex, columnIndex);
        cell.SetAttributeValue("t", "inlineStr");
        cell.Element(SimpleWorkbook.SpreadsheetNs + "f")?.Remove();
        cell.Element(SimpleWorkbook.SpreadsheetNs + "v")?.Remove();
        cell.Element(SimpleWorkbook.SpreadsheetNs + "is")?.Remove();
        cell.Add(new XElement(SimpleWorkbook.SpreadsheetNs + "is",
            new XElement(SimpleWorkbook.SpreadsheetNs + "t", value ?? "")));
    }

    private static void SetCellNumber(XDocument sheet, int rowIndex, int columnIndex, decimal value)
    {
        var cell = GetOrCreateCell(sheet, rowIndex, columnIndex);
        cell.Attribute("t")?.Remove();
        cell.Element(SimpleWorkbook.SpreadsheetNs + "f")?.Remove();
        cell.Element(SimpleWorkbook.SpreadsheetNs + "is")?.Remove();
        cell.Element(SimpleWorkbook.SpreadsheetNs + "v")?.Remove();
        cell.Add(new XElement(SimpleWorkbook.SpreadsheetNs + "v", value.ToString("0.############################", CultureInfo.InvariantCulture)));
    }

    private static void ClearCellValue(XDocument sheet, int rowIndex, int columnIndex)
    {
        var cell = FindCell(sheet, rowIndex, columnIndex);
        if (cell is null)
        {
            return;
        }

        cell.Attribute("t")?.Remove();
        cell.Element(SimpleWorkbook.SpreadsheetNs + "f")?.Remove();
        cell.Element(SimpleWorkbook.SpreadsheetNs + "is")?.Remove();
        cell.Element(SimpleWorkbook.SpreadsheetNs + "v")?.Remove();
    }

    private static XElement GetOrCreateCell(XDocument sheet, int rowIndex, int columnIndex)
    {
        var sheetData = sheet.Descendants(SimpleWorkbook.SpreadsheetNs + "sheetData").First();
        var row = sheetData.Elements(SimpleWorkbook.SpreadsheetNs + "row")
            .FirstOrDefault(item => (int?)item.Attribute("r") == rowIndex);
        if (row is null)
        {
            row = new XElement(SimpleWorkbook.SpreadsheetNs + "row", new XAttribute("r", rowIndex));
            var nextRow = sheetData.Elements(SimpleWorkbook.SpreadsheetNs + "row")
                .FirstOrDefault(item => ((int?)item.Attribute("r") ?? 0) > rowIndex);
            if (nextRow is null)
            {
                sheetData.Add(row);
            }
            else
            {
                nextRow.AddBeforeSelf(row);
            }
        }

        var reference = CellReference(rowIndex, columnIndex);
        var cell = row.Elements(SimpleWorkbook.SpreadsheetNs + "c")
            .FirstOrDefault(item => string.Equals(item.Attribute("r")?.Value, reference, StringComparison.OrdinalIgnoreCase));
        if (cell is not null)
        {
            return cell;
        }

        cell = new XElement(SimpleWorkbook.SpreadsheetNs + "c", new XAttribute("r", reference));
        var nextCell = row.Elements(SimpleWorkbook.SpreadsheetNs + "c")
            .FirstOrDefault(item => (SimpleWorkbook.ColumnIndex(item.Attribute("r")?.Value) ?? 0) > columnIndex);
        if (nextCell is null)
        {
            row.Add(cell);
        }
        else
        {
            nextCell.AddBeforeSelf(cell);
        }

        return cell;
    }

    private static XElement? FindCell(XDocument sheet, int rowIndex, int columnIndex)
    {
        var reference = CellReference(rowIndex, columnIndex);
        return sheet.Descendants(SimpleWorkbook.SpreadsheetNs + "c")
            .FirstOrDefault(item => string.Equals(item.Attribute("r")?.Value, reference, StringComparison.OrdinalIgnoreCase));
    }

    private static string CellReference(int rowIndex, int columnIndex)
    {
        var column = "";
        var value = columnIndex;
        while (value > 0)
        {
            var remainder = (value - 1) % 26;
            column = (char)('A' + remainder) + column;
            value = (value - remainder - 1) / 26;
        }

        return $"{column}{rowIndex}";
    }

    private static void ForceFullCalculationOnOpen(ZipArchive archive)
    {
        const string path = "xl/workbook.xml";
        var entry = archive.GetEntry(path) ?? throw new InvalidOperationException("Workbook metadata was not found.");
        XDocument document;
        using (var stream = entry.Open())
        {
            document = XDocument.Load(stream, LoadOptions.PreserveWhitespace);
        }

        var workbook = document.Root ?? throw new InvalidOperationException("Workbook metadata was invalid.");
        var calcPr = workbook.Element(SimpleWorkbook.SpreadsheetNs + "calcPr");
        if (calcPr is null)
        {
            calcPr = new XElement(SimpleWorkbook.SpreadsheetNs + "calcPr");
            workbook.Add(calcPr);
        }

        calcPr.SetAttributeValue("calcMode", "auto");
        calcPr.SetAttributeValue("fullCalcOnLoad", "1");
        calcPr.SetAttributeValue("forceFullCalc", "1");

        entry.Delete();
        var updatedEntry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var output = updatedEntry.Open();
        document.Save(output, SaveOptions.DisableFormatting);
    }

    private static void RemoveCalculationChain(ZipArchive archive)
    {
        archive.GetEntry("xl/calcChain.xml")?.Delete();

        UpdatePackageXml(archive, "xl/_rels/workbook.xml.rels", document =>
        {
            document.Root?
                .Elements()
                .Where(item =>
                    item.Attribute("Target")?.Value.Equals("calcChain.xml", StringComparison.OrdinalIgnoreCase) == true ||
                    item.Attribute("Type")?.Value.EndsWith("/calcChain", StringComparison.OrdinalIgnoreCase) == true)
                .Remove();
        });

        UpdatePackageXml(archive, "[Content_Types].xml", document =>
        {
            document.Root?
                .Elements()
                .Where(item => item.Attribute("PartName")?.Value.Equals("/xl/calcChain.xml", StringComparison.OrdinalIgnoreCase) == true)
                .Remove();
        });
    }

    private static void UpdatePackageXml(ZipArchive archive, string path, Action<XDocument> update)
    {
        var entry = archive.GetEntry(path);
        if (entry is null)
        {
            return;
        }

        XDocument document;
        using (var stream = entry.Open())
        {
            document = XDocument.Load(stream, LoadOptions.PreserveWhitespace);
        }

        update(document);
        entry.Delete();
        var updatedEntry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var output = updatedEntry.Open();
        document.Save(output, SaveOptions.DisableFormatting);
    }

    private sealed record WeldPinCertRequest(
        string PurchaseOrder,
        string OrderNumber,
        string OrderDate,
        string PartNum,
        string PartDescription,
        string ShipBy,
        string NeedBy,
        decimal RequestedQuantity,
        string RawMaterialPartNum,
        string RawMaterialLotNumber,
        string RawMaterialHeatNumber,
        IReadOnlyList<FinishedGoodsInventory> Inventory);

    private sealed record EpicorRequest(
        string PurchaseOrder,
        string OrderNumber,
        string OrderDate,
        string PartNum,
        string PartDescription,
        string ShipBy,
        string NeedBy,
        decimal RequestedQuantity);

    private sealed record FinishedGoodsInventory(
        string FinishedGoodsLotNumber,
        string FinishedGoodsBaseLot,
        string BoxNumber,
        string BoxNumberNormalized,
        decimal Quantity);

    private sealed record KeyenceRow(
        int SourceOrder,
        DateTime? MeasurementTime,
        string OkNg,
        string LotNumber,
        int SerialCounter,
        string ToteNumber,
        string ToteNumberNormalized,
        decimal? DeltaE);

    private sealed record CompleteKeyenceSet(
        string KeyenceLotNumber,
        DateTime MeasurementTime,
        int SourceOrder,
        IReadOnlyList<KeyenceMeasurement> Measurements);

    private sealed record KeyenceMeasurement(int SerialCounter, decimal DeltaE, string OkNg);

    private sealed record WorkbookSheet(string Name, Dictionary<(int Row, int Column), string> Cells, int MaxRow, int MaxColumn)
    {
        public string Cell(int row, int column) => Cells.TryGetValue((row, column), out var value) ? value : "";
    }

    private static class SimpleWorkbook
    {
        public static readonly XNamespace SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace RelationshipNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace PackageRelationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        public static IReadOnlyList<WorkbookSheet> Read(Stream stream)
        {
            if (stream.CanSeek)
            {
                stream.Position = 0;
            }

            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            var sharedStrings = ReadSharedStrings(archive);
            return SheetPaths(archive)
                .Select(sheet => ReadSheet(archive, sharedStrings, sheet.Name, sheet.Path))
                .ToArray();
        }

        public static IReadOnlyList<(string Name, string Path)> SheetPaths(ZipArchive archive)
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

            return workbook.Descendants(SpreadsheetNs + "sheet")
                .Select(sheet =>
                {
                    var name = sheet.Attribute("name")?.Value ?? "Sheet";
                    var relationshipId = sheet.Attribute(RelationshipNs + "id")?.Value ?? "";
                    if (!relationships.TryGetValue(relationshipId, out var target) || string.IsNullOrWhiteSpace(target))
                    {
                        throw new InvalidOperationException($"Worksheet '{name}' could not be resolved.");
                    }

                    target = target.Replace('\\', '/');
                    var path = target.StartsWith('/')
                        ? target.TrimStart('/')
                        : target.StartsWith("xl/", StringComparison.OrdinalIgnoreCase) ? target : $"xl/{target}";
                    return (name, path);
                })
                .ToArray();
        }

        public static int? ColumnIndex(string? cellReference)
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

        private static WorkbookSheet ReadSheet(ZipArchive archive, IReadOnlyList<string> sharedStrings, string name, string path)
        {
            var entry = archive.GetEntry(path) ?? throw new InvalidOperationException($"Worksheet '{name}' was not found.");
            using var sheetStream = entry.Open();
            var document = XDocument.Load(sheetStream);
            var cells = new Dictionary<(int Row, int Column), string>();
            var maxRow = 0;
            var maxColumn = 0;
            foreach (var row in document.Descendants(SpreadsheetNs + "sheetData").Elements(SpreadsheetNs + "row"))
            {
                var rowIndex = int.TryParse(row.Attribute("r")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedRow)
                    ? parsedRow
                    : maxRow + 1;
                maxRow = Math.Max(maxRow, rowIndex);
                var nextColumn = 1;
                foreach (var cell in row.Elements(SpreadsheetNs + "c"))
                {
                    var columnIndex = ColumnIndex(cell.Attribute("r")?.Value) ?? nextColumn;
                    var value = ReadCellValue(cell, sharedStrings);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        cells[(rowIndex, columnIndex)] = value.Trim();
                    }

                    maxColumn = Math.Max(maxColumn, columnIndex);
                    nextColumn = columnIndex + 1;
                }
            }

            return new WorkbookSheet(name, cells, maxRow, maxColumn);
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
    }
}
