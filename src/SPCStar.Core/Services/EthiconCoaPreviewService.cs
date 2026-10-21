using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;

namespace SPCStar.Core.Services;

public sealed record EthiconCoaPreviewSummary(
    int ShipmentRows,
    int CertificateGroups,
    int ReadyGroups,
    int BlockedGroups);

public sealed record EthiconCoaPreviewGroup(
    string Family,
    string PartNum,
    string WireShipment,
    string JobNum,
    int ShipmentRows,
    decimal TotalQuantity,
    int BoxCount,
    int CarrierCount,
    string PurchaseOrder,
    string ShipmentDate,
    string BlankCode,
    string HoleSize,
    int InProcessMeasurementCount,
    int InProcessVariableCount,
    string Status,
    IReadOnlyList<string> Messages);

public sealed record EthiconCoaPreviewResult(
    EthiconCoaPreviewSummary Summary,
    IReadOnlyList<EthiconCoaPreviewGroup> Groups);

public sealed record GeneratedEthiconCoa(
    string FileName,
    string ContentType,
    byte[] Content);

public sealed record EthiconCoaSeedResult(
    int ShipmentRows,
    int GroupsProcessed,
    int JobsCreated,
    int JobsUpdated,
    int MeasurementsCreated,
    IReadOnlyList<EthiconCoaSeedGroup> Groups);

public sealed record EthiconCoaSeedGroup(
    string PartNum,
    string WireShipment,
    string JobNum,
    string Status,
    int MeasurementsCreated,
    IReadOnlyList<string> Messages);

public sealed class EthiconCoaPreviewService(ISpcRepository repository)
{
    private static readonly StringComparer KeyComparer = StringComparer.OrdinalIgnoreCase;
    private const string MacroWorkbookContentType = "application/vnd.ms-excel.sheet.macroEnabled.12";
    private const string CertSeedDeviceId = "CERT-SEED";
    private const string CertSeedOperatorUserId = "CERT-SEED";
    private static readonly string[] CertSeedResourceIds = ["CERT-SEED", "CERT-TEST"];
    private static readonly string[] CertSeedDeviceIds = [CertSeedDeviceId, "cert-test-seed"];

    public ServiceResult<EthiconCoaPreviewResult> Preview(string family, Stream shipmentWorkbook, Stream shipmentMasterWorkbook)
    {
        var normalizedFamily = NormalizeFamily(family);
        if (normalizedFamily is null)
        {
            return ServiceResult<EthiconCoaPreviewResult>.Fail("Select CE or TP before previewing Ethicon COAs.");
        }

        var shipmentSheets = SimpleWorkbook.Read(shipmentWorkbook);
        var shipmentRows = ParseShipmentRows(shipmentSheets);
        if (shipmentRows.Count == 0)
        {
            return ServiceResult<EthiconCoaPreviewResult>.Fail("No valid shipment rows were found. Valid rows need Part Number, Job Number, Qty, and Wire Ship Num.");
        }

        var masterSheets = SimpleWorkbook.Read(shipmentMasterWorkbook);
        var master = ParseShipmentMaster(masterSheets);
        var groups = shipmentRows
            .GroupBy(row => $"{row.PartNum}\u001f{row.WireShipment}", StringComparer.OrdinalIgnoreCase)
            .Select(group => BuildPreviewGroup(normalizedFamily, group.ToArray(), master))
            .OrderBy(group => group.PartNum, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.WireShipment, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return ServiceResult<EthiconCoaPreviewResult>.Ok(new EthiconCoaPreviewResult(
            new EthiconCoaPreviewSummary(
                shipmentRows.Count,
                groups.Length,
                groups.Count(group => group.Status == "Ready"),
                groups.Count(group => group.Status == "Blocked")),
            groups));
    }

    public ServiceResult<GeneratedEthiconCoa> Generate(
        string family,
        Stream shipmentWorkbook,
        Stream shipmentMasterWorkbook,
        Stream templateWorkbook,
        string partNum,
        string wireShipment)
    {
        var normalizedFamily = NormalizeFamily(family);
        if (normalizedFamily is null)
        {
            return ServiceResult<GeneratedEthiconCoa>.Fail("Select CE or TP before generating an Ethicon COA.");
        }

        if (string.IsNullOrWhiteSpace(partNum) || string.IsNullOrWhiteSpace(wireShipment))
        {
            return ServiceResult<GeneratedEthiconCoa>.Fail("Select a Part + Wire Shipment group before generating a COA.");
        }

        var shipmentRows = ParseShipmentRows(SimpleWorkbook.Read(shipmentWorkbook));
        var rows = shipmentRows
            .Where(row =>
                row.PartNum.Equals(partNum, StringComparison.OrdinalIgnoreCase) &&
                row.WireShipment.Equals(wireShipment, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (rows.Length == 0)
        {
            return ServiceResult<GeneratedEthiconCoa>.Fail("The selected Part + Wire Shipment group was not found in the shipment workbook.");
        }

        var master = ParseShipmentMaster(SimpleWorkbook.Read(shipmentMasterWorkbook));
        var group = BuildPreviewGroup(normalizedFamily, rows, master);
        if (group.Status != "Ready")
        {
            return ServiceResult<GeneratedEthiconCoa>.Fail(group.Messages);
        }

        var workbook = GenerateWorkbook(normalizedFamily, rows, group, templateWorkbook);
        var safePart = SafeFileName(group.PartNum);
        var safeWire = SafeFileName(group.WireShipment);
        var safeJob = SafeFileName(group.JobNum);
        return ServiceResult<GeneratedEthiconCoa>.Ok(new GeneratedEthiconCoa(
            $"Ethicon-{normalizedFamily}-COA-{safePart}-{safeJob}-{safeWire}.xlsm",
            MacroWorkbookContentType,
            workbook));
    }

    public ServiceResult<EthiconCoaSeedResult> SeedTestData(string family, Stream shipmentWorkbook, string? resourceId = null)
    {
        var normalizedFamily = NormalizeFamily(family);
        if (normalizedFamily is null)
        {
            return ServiceResult<EthiconCoaSeedResult>.Fail("Select CE or TP before seeding Ethicon COA test data.");
        }

        var shipmentRows = ParseShipmentRows(SimpleWorkbook.Read(shipmentWorkbook));
        if (shipmentRows.Count == 0)
        {
            return ServiceResult<EthiconCoaSeedResult>.Fail("No valid shipment rows were found. Valid rows need Part Number, Job Number, Qty, and Wire Ship Num.");
        }

        var seedResourceId = string.IsNullOrWhiteSpace(resourceId) ? "CERT-SEED" : resourceId.Trim();
        UpsertSeedResource(seedResourceId);

        var groups = new List<EthiconCoaSeedGroup>();
        var jobsCreated = 0;
        var jobsUpdated = 0;
        var measurementsCreated = 0;
        foreach (var groupRows in shipmentRows
            .GroupBy(row => $"{row.PartNum}\u001f{row.WireShipment}", StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.First().PartNum, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.First().WireShipment, StringComparer.OrdinalIgnoreCase))
        {
            var result = SeedGroup(groupRows.ToArray(), seedResourceId);
            groups.Add(result.Group);
            jobsCreated += result.JobsCreated;
            jobsUpdated += result.JobsUpdated;
            measurementsCreated += result.Group.MeasurementsCreated;
        }

        return ServiceResult<EthiconCoaSeedResult>.Ok(new EthiconCoaSeedResult(
            shipmentRows.Count,
            groups.Count,
            jobsCreated,
            jobsUpdated,
            measurementsCreated,
            groups));
    }

    private EthiconCoaPreviewGroup BuildPreviewGroup(string family, IReadOnlyCollection<ShipmentRow> rows, ShipmentMaster master)
    {
        var first = rows.First();
        var messages = new List<string>();
        var jobNums = rows.Select(row => row.JobNum).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(KeyComparer).ToArray();
        var jobNum = jobNums.Length == 1 ? jobNums[0] : "";
        if (jobNums.Length == 0)
        {
            messages.Add("Missing job number.");
        }
        else if (jobNums.Length > 1)
        {
            messages.Add($"Multiple jobs are present for this Part + Wire Shipment group: {string.Join(", ", jobNums)}.");
        }

        var part = repository.Parts.FirstOrDefault(item => item.PartNum.Equals(first.PartNum, StringComparison.OrdinalIgnoreCase));
        if (part is null)
        {
            messages.Add($"Part is not configured in SPC-Star: {first.PartNum}.");
        }

        var job = string.IsNullOrWhiteSpace(jobNum)
            ? null
            : repository.Jobs.FirstOrDefault(item =>
                item.JobNum.Equals(jobNum, StringComparison.OrdinalIgnoreCase) &&
                item.PartNum.Equals(first.PartNum, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(jobNum) && job is null)
        {
            messages.Add($"Job was not found for this part in SPC-Star: {jobNum}.");
        }

        var inProcessMeasurements = string.IsNullOrWhiteSpace(jobNum)
            ? Array.Empty<InspectionMeasurement>()
            : repository.Measurements
                .Where(measurement =>
                    measurement.JobNum.Equals(jobNum, StringComparison.OrdinalIgnoreCase) &&
                    measurement.PartNum.Equals(first.PartNum, StringComparison.OrdinalIgnoreCase) &&
                    measurement.InspectionPhase.Equals("In Process", StringComparison.OrdinalIgnoreCase))
                .ToArray();
        if (job is not null && inProcessMeasurements.Length == 0)
        {
            messages.Add("No In Process inspection measurements were found for this job.");
        }

        var po = master.FindPurchaseOrder(first.PartNum, jobNum);
        if (string.IsNullOrWhiteSpace(po))
        {
            messages.Add("Purchase order was not found in the Bihler shipment master.");
        }

        if (!master.ShipmentDate.HasValue)
        {
            messages.Add("Shipment date was not found in the Bihler shipment master.");
        }

        var status = messages.Count == 0 ? "Ready" : "Blocked";
        return new EthiconCoaPreviewGroup(
            family,
            first.PartNum,
            first.WireShipment,
            jobNum,
            rows.Count,
            rows.Sum(row => row.Quantity),
            rows.Select(row => row.BoxNumber).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(KeyComparer).Count(),
            rows.Select(row => row.CarrierNumber).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(KeyComparer).Count(),
            po,
            master.ShipmentDate?.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture) ?? "",
            FirstNonBlank(rows.Select(row => row.BlankCode)),
            CleanWorkbookDisplayNumber(FirstNonBlank(rows.Select(row => row.HoleSize))),
            inProcessMeasurements.Length,
            inProcessMeasurements.Select(measurement => measurement.CharacteristicName).Distinct(KeyComparer).Count(),
            status,
            messages);
    }

    private SeedGroupResult SeedGroup(IReadOnlyList<ShipmentRow> rows, string resourceId)
    {
        var first = rows.First();
        var messages = new List<string>();
        var jobNums = rows.Select(row => row.JobNum).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(KeyComparer).ToArray();
        var jobNum = jobNums.Length == 1 ? jobNums[0].Trim() : "";
        if (jobNums.Length == 0)
        {
            messages.Add("Missing job number.");
        }
        else if (jobNums.Length > 1)
        {
            messages.Add($"Multiple jobs are present for this Part + Wire Shipment group: {string.Join(", ", jobNums)}.");
        }

        var part = repository.Parts.FirstOrDefault(item => item.PartNum.Equals(first.PartNum, StringComparison.OrdinalIgnoreCase));
        if (part is null)
        {
            messages.Add($"Part is not configured in SPC-Star: {first.PartNum}.");
        }

        var plans = part is null
            ? []
            : InProcessPlansForPart(part).ToArray();
        if (part is not null && plans.Length == 0)
        {
            messages.Add("No In Process inspection plans were found for this part.");
        }

        if (messages.Count > 0 || part is null)
        {
            return new SeedGroupResult(
                new EthiconCoaSeedGroup(first.PartNum, first.WireShipment, jobNum, "Skipped", 0, messages),
                0,
                0);
        }

        var existingJob = repository.Jobs.FirstOrDefault(item =>
            item.JobNum.Equals(jobNum, StringComparison.OrdinalIgnoreCase) &&
            item.PartNum.Equals(first.PartNum, StringComparison.OrdinalIgnoreCase));
        var jobsCreated = 0;
        var jobsUpdated = 0;
        if (existingJob is null)
        {
            repository.Jobs.Add(new Job { JobNum = jobNum, PartNum = first.PartNum });
            jobsCreated = 1;
        }
        else
        {
            jobsUpdated = 1;
        }

        RemoveExistingSeedData(first.PartNum, jobNum, resourceId);
        UpsertSeedJobTags(first, jobNum, resourceId);

        var now = DateTimeOffset.UtcNow;
        var created = 0;
        for (var run = 0; run < 10; run++)
        {
            var timestamp = now.AddMinutes(-(10 - run) * 7);
            foreach (var operationGroup in plans.GroupBy(plan => new
            {
                plan.Process.ProcessCode,
                plan.Operation.OperationSeq
            }))
            {
                var runMeasurementIds = new List<Guid>();
                foreach (var plan in operationGroup)
                {
                    var measurement = new InspectionMeasurement
                    {
                        ClientRecordId = CertSeedClientRecordId(first.PartNum, jobNum, first.WireShipment, plan.Characteristic.Name, run),
                        DeviceId = CertSeedDeviceId,
                        JobNum = jobNum,
                        PartNum = first.PartNum,
                        ProcessCode = plan.Process.ProcessCode,
                        OperationSeq = plan.Operation.OperationSeq,
                        ResourceId = resourceId,
                        CharacteristicName = plan.Characteristic.Name,
                        InspectionPhase = "In Process",
                        Value = SeedValue(plan.Characteristic, plan.Plan, run),
                        Timestamp = timestamp,
                        OperatorUserId = CertSeedOperatorUserId,
                        OperatorShift = "Cert Test",
                        SubmittedAt = timestamp,
                        SyncedAt = now
                    };
                    repository.Measurements.Add(measurement);
                    runMeasurementIds.Add(measurement.Id);
                    created++;
                }

                if (runMeasurementIds.Count > 0)
                {
                    var completion = new JobPhaseCompletion
                    {
                        JobNum = jobNum,
                        PartNum = first.PartNum,
                        ProcessCode = operationGroup.Key.ProcessCode,
                        OperationSeq = operationGroup.Key.OperationSeq,
                        ResourceId = resourceId,
                        InspectionPhase = "In Process",
                        CompletionNumber = run + 1,
                        CompletedByUserId = CertSeedOperatorUserId,
                        OperatorShift = "Cert Test",
                        CompletedAt = timestamp,
                        MachineCounter = run + 1
                    };
                    completion.MeasurementIds.AddRange(runMeasurementIds);
                    repository.JobPhaseCompletions.Add(completion);
                }
            }
        }

        return new SeedGroupResult(
            new EthiconCoaSeedGroup(first.PartNum, first.WireShipment, jobNum, "Seeded", created, []),
            jobsCreated,
            jobsUpdated);
    }

    private IEnumerable<SeedInspectionPlan> InProcessPlansForPart(Part part)
    {
        var operations =
            from operation in repository.Operations
            join process in repository.Processes on operation.ProcessId equals process.Id
            where operation.PartId == part.Id
            select new { Operation = operation, Process = process };
        return from operation in operations
               join characteristic in repository.Characteristics on operation.Operation.Id equals characteristic.OperationId
               join plan in repository.InspectionPlans on characteristic.Id equals plan.CharacteristicId
               where plan.InspectionPhase.Equals("In Process", StringComparison.OrdinalIgnoreCase)
               orderby operation.Process.ProcessCode, operation.Operation.OperationSeq, plan.DisplayOrder, characteristic.Name
               select new SeedInspectionPlan(plan, characteristic, operation.Operation, operation.Process);
    }

    private void UpsertSeedResource(string resourceId)
    {
        var resource = repository.Resources.FirstOrDefault(item => item.ResourceId.Equals(resourceId, StringComparison.OrdinalIgnoreCase));
        if (resource is null)
        {
            resource = new ResourceMachine
            {
                ResourceId = resourceId,
                Description = "COA test data seed resource",
                DeviceProfile = "keyboard"
            };
            repository.Resources.Add(resource);
        }

        foreach (var group in new[] { "Ethicon Cutting Edge - Drilled", "Ethicon Taperpoint - Drilled" })
        {
            if (!resource.ProductGroups.Contains(group, StringComparer.OrdinalIgnoreCase))
            {
                resource.ProductGroups.Add(group);
            }
        }
    }

    private void RemoveExistingSeedData(string partNum, string jobNum, string resourceId)
    {
        var certSeedResourceIds = CertSeedResourceIds.Append(resourceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var certSeedMeasurementIds = repository.Measurements
            .Where(measurement =>
                measurement.PartNum.Equals(partNum, StringComparison.OrdinalIgnoreCase) &&
                measurement.JobNum.Equals(jobNum, StringComparison.OrdinalIgnoreCase) &&
                (certSeedResourceIds.Contains(measurement.ResourceId) ||
                    CertSeedDeviceIds.Contains(measurement.DeviceId ?? "", StringComparer.OrdinalIgnoreCase) ||
                    measurement.OperatorUserId.Equals(CertSeedOperatorUserId, StringComparison.OrdinalIgnoreCase)))
            .Select(measurement => measurement.Id)
            .ToHashSet();

        repository.Measurements.RemoveAll(measurement =>
            measurement.PartNum.Equals(partNum, StringComparison.OrdinalIgnoreCase) &&
            measurement.JobNum.Equals(jobNum, StringComparison.OrdinalIgnoreCase) &&
            certSeedMeasurementIds.Contains(measurement.Id));
        repository.JobPhaseCompletions.RemoveAll(completion =>
            completion.PartNum.Equals(partNum, StringComparison.OrdinalIgnoreCase) &&
            completion.JobNum.Equals(jobNum, StringComparison.OrdinalIgnoreCase) &&
            (certSeedResourceIds.Contains(completion.ResourceId) ||
                completion.CompletedByUserId.Equals(CertSeedOperatorUserId, StringComparison.OrdinalIgnoreCase) ||
                completion.MeasurementIds.Any(certSeedMeasurementIds.Contains)));
        repository.JobTags.RemoveAll(tag =>
            tag.PartNum.Equals(partNum, StringComparison.OrdinalIgnoreCase) &&
            tag.JobNum.Equals(jobNum, StringComparison.OrdinalIgnoreCase) &&
            (certSeedResourceIds.Contains(tag.ResourceId) ||
                tag.OperatorUserId.Equals(CertSeedOperatorUserId, StringComparison.OrdinalIgnoreCase)));
    }

    private void UpsertSeedJobTags(ShipmentRow row, string jobNum, string resourceId)
    {
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Wire Shipment (W/S) #"] = row.WireShipment,
            ["Wire Shipment"] = row.WireShipment,
            ["Cert Shipment Box Count"] = row.BoxNumber,
            ["Cert Carrier"] = row.CarrierNumber
        };

        foreach (var (name, value) in tags.Where(item => !string.IsNullOrWhiteSpace(item.Value)))
        {
            repository.JobTags.Add(new JobTag
            {
                JobNum = jobNum,
                PartNum = row.PartNum,
                ResourceId = resourceId,
                TagName = name,
                TagValue = value.Trim(),
                OperatorUserId = CertSeedOperatorUserId,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
    }

    private static string CertSeedClientRecordId(string partNum, string jobNum, string wireShipment, string characteristicName, int run)
    {
        var cleanCharacteristic = SafeFileName(characteristicName);
        return $"{CertSeedDeviceId}|{partNum}|{jobNum}|{wireShipment}|{cleanCharacteristic}|{run + 1}";
    }

    private static decimal SeedValue(Characteristic characteristic, InspectionPlan plan, int run)
    {
        if (characteristic.Type == CharacteristicType.Attribute)
        {
            return 1m;
        }

        if (plan.Lsl.HasValue && plan.Usl.HasValue)
        {
            if (plan.Lsl.Value == plan.Usl.Value)
            {
                return plan.Lsl.Value;
            }

            var range = plan.Usl.Value - plan.Lsl.Value;
            var center = plan.Nominal ?? plan.Lsl.Value + (range / 2m);
            var offset = ((run % 5) - 2) * (range / 100m);
            return Clamp(center + offset, plan.Lsl.Value + (range / 20m), plan.Usl.Value - (range / 20m));
        }

        if (plan.Lsl.HasValue)
        {
            var increment = Math.Max(Math.Abs(plan.Lsl.Value) / 100m, 0.001m);
            return plan.Lsl.Value + increment + (run % 3) * increment;
        }

        if (plan.Usl.HasValue)
        {
            var decrement = Math.Max(Math.Abs(plan.Usl.Value) / 100m, 0.001m);
            return plan.Usl.Value - decrement - (run % 3) * decrement;
        }

        return 1m + (run % 5) * 0.01m;
    }

    private static decimal Clamp(decimal value, decimal min, decimal max)
    {
        if (min > max)
        {
            return value;
        }

        return Math.Min(Math.Max(value, min), max);
    }

    private static IReadOnlyList<ShipmentRow> ParseShipmentRows(IReadOnlyList<WorkbookSheet> sheets)
    {
        var rows = new List<ShipmentRow>();
        foreach (var sheet in sheets.Where(sheet => Regex.IsMatch(sheet.Name, @"^\d+$")))
        {
            var header = FindHeader(sheet, ["Job Number", "Part Number", "Qty", "Wire Ship Num"]);
            if (header is null)
            {
                continue;
            }

            var columns = HeaderColumns(sheet, header.Value);
            for (var rowIndex = header.Value + 1; rowIndex <= sheet.MaxRow; rowIndex++)
            {
                var partNum = Cell(sheet, rowIndex, columns, "Part Number");
                var jobNum = Cell(sheet, rowIndex, columns, "Job Number");
                var wireShipment = Cell(sheet, rowIndex, columns, "Wire Ship Num");
                var qtyText = Cell(sheet, rowIndex, columns, "Qty");
                if (string.IsNullOrWhiteSpace(partNum) &&
                    string.IsNullOrWhiteSpace(jobNum) &&
                    string.IsNullOrWhiteSpace(wireShipment) &&
                    string.IsNullOrWhiteSpace(qtyText))
                {
                    continue;
                }

                if (!partNum.Equals(sheet.Name, StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(wireShipment) ||
                    !TryParseDecimal(qtyText, out var quantity))
                {
                    continue;
                }

                rows.Add(new ShipmentRow(
                    partNum,
                    jobNum,
                    Cell(sheet, rowIndex, columns, "Carrier Num"),
                    Cell(sheet, rowIndex, columns, "Box Number"),
                    quantity,
                    wireShipment,
                    Cell(sheet, rowIndex, columns, "Blank Code"),
                    Cell(sheet, rowIndex, columns, "Bihler Lot"),
                    Cell(sheet, rowIndex, columns, "Condition"),
                    Cell(sheet, rowIndex, columns, "Hole Size")));
            }
        }

        return rows;
    }

    private static ShipmentMaster ParseShipmentMaster(IReadOnlyList<WorkbookSheet> sheets)
    {
        var sheet = sheets.FirstOrDefault(item => item.Name.Equals("Add BOA", StringComparison.OrdinalIgnoreCase));
        if (sheet is null)
        {
            return new ShipmentMaster(null, []);
        }

        var shipmentDate = ParseExcelDate(sheet.Cell(3, 4));
        var header = FindHeader(sheet, ["BOA PN", "PO #", "Lot#"]);
        if (header is null)
        {
            return new ShipmentMaster(shipmentDate, []);
        }

        var columns = HeaderColumns(sheet, header.Value);
        var records = new List<ShipmentMasterRecord>();
        var currentPart = "";
        var currentPo = "";
        for (var rowIndex = header.Value + 1; rowIndex <= sheet.MaxRow; rowIndex++)
        {
            var partNum = Cell(sheet, rowIndex, columns, "BOA PN");
            var po = Cell(sheet, rowIndex, columns, "PO #");
            var lot = Cell(sheet, rowIndex, columns, "Lot#");
            if (!string.IsNullOrWhiteSpace(partNum))
            {
                currentPart = partNum;
            }
            if (!string.IsNullOrWhiteSpace(po))
            {
                currentPo = po;
            }

            if (string.IsNullOrWhiteSpace(currentPart) || string.IsNullOrWhiteSpace(lot))
            {
                continue;
            }

            records.Add(new ShipmentMasterRecord(currentPart, currentPo, lot));
        }

        return new ShipmentMaster(shipmentDate, records);
    }

    private static int? FindHeader(WorkbookSheet sheet, IReadOnlyCollection<string> requiredHeaders)
    {
        for (var rowIndex = 1; rowIndex <= Math.Min(sheet.MaxRow, 40); rowIndex++)
        {
            var values = Enumerable.Range(1, sheet.MaxColumn)
                .Select(column => sheet.Cell(rowIndex, column))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (requiredHeaders.All(values.Contains))
            {
                return rowIndex;
            }
        }

        return null;
    }

    private static Dictionary<string, int> HeaderColumns(WorkbookSheet sheet, int rowIndex)
    {
        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var column = 1; column <= sheet.MaxColumn; column++)
        {
            var value = sheet.Cell(rowIndex, column);
            if (!string.IsNullOrWhiteSpace(value) && !columns.ContainsKey(value))
            {
                columns[value] = column;
            }
        }

        return columns;
    }

    private static string Cell(WorkbookSheet sheet, int rowIndex, IReadOnlyDictionary<string, int> columns, string header)
    {
        return columns.TryGetValue(header, out var column)
            ? sheet.Cell(rowIndex, column).Trim()
            : "";
    }

    private static string? NormalizeFamily(string family)
    {
        if (family.Equals("CE", StringComparison.OrdinalIgnoreCase) ||
            family.Equals("QF-E112", StringComparison.OrdinalIgnoreCase))
        {
            return "CE";
        }

        if (family.Equals("TP", StringComparison.OrdinalIgnoreCase) ||
            family.Equals("QF-E113", StringComparison.OrdinalIgnoreCase))
        {
            return "TP";
        }

        return null;
    }

    private static bool TryParseDecimal(string value, out decimal result)
    {
        return decimal.TryParse(value.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out result);
    }

    private static DateTime? ParseExcelDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed))
        {
            return parsed.Date;
        }

        if (double.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var serial))
        {
            try
            {
                return DateTime.FromOADate(serial).Date;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        return null;
    }

    private static string FirstNonBlank(IEnumerable<string> values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
    }

    private static string CleanWorkbookDisplayNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return value;
        }

        return parsed.ToString("0.###############", CultureInfo.InvariantCulture);
    }

    private byte[] GenerateWorkbook(string family, IReadOnlyList<ShipmentRow> rows, EthiconCoaPreviewGroup group, Stream templateWorkbook)
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
            var sheetPaths = SimpleWorkbook.SheetPaths(archive).ToDictionary(item => item.Name, item => item.Path, StringComparer.OrdinalIgnoreCase);
            var epicorSheet = sheetPaths.TryGetValue("Epicor", out var epicorPath)
                ? epicorPath
                : throw new InvalidOperationException("The Ethicon COA template is missing the Epicor data sheet.");
            var certificateSheet = sheetPaths.Keys.FirstOrDefault(name => !name.Equals("Epicor", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("The Ethicon COA template is missing the certificate sheet.");
            var certificatePath = sheetPaths[certificateSheet];
            var borderedStyles = EnsureShipmentTableBorderStyles(archive);

            UpdateWorksheet(archive, epicorSheet, sheet => FillEpicorSheet(sheet, rows));
            UpdateWorksheet(archive, certificatePath, sheet => FillCertificateSheet(sheet, family, rows, group, borderedStyles));
            RemoveCalculationChain(archive);
            ForceFullCalculationOnOpen(archive);
        }

        return output.ToArray();
    }

    private static void FillEpicorSheet(XDocument sheet, IReadOnlyList<ShipmentRow> rows)
    {
        for (var rowIndex = 2; rowIndex <= 2810; rowIndex++)
        {
            for (var column = 1; column <= 7; column++)
            {
                RemoveCell(sheet, rowIndex, column);
            }
        }

        var sorted = rows
            .OrderBy(row => NumericSortValue(row.CarrierNumber))
            .ThenBy(row => row.CarrierNumber, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => NumericSortValue(row.BoxNumber))
            .ThenBy(row => row.BoxNumber, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        for (var index = 0; index < sorted.Length; index++)
        {
            var rowIndex = index + 2;
            var row = sorted[index];
            SetCellText(sheet, rowIndex, 1, row.JobNum);
            SetCellText(sheet, rowIndex, 2, row.BihlerLot);
            SetCellText(sheet, rowIndex, 3, row.PartNum);
            SetCellText(sheet, rowIndex, 4, row.CarrierNumber);
            SetCellText(sheet, rowIndex, 5, row.BoxNumber);
            SetCellNumber(sheet, rowIndex, 6, row.Quantity);
            SetCellText(sheet, rowIndex, 7, row.WireShipment);
        }
    }

    private void FillCertificateSheet(
        XDocument sheet,
        string family,
        IReadOnlyList<ShipmentRow> rows,
        EthiconCoaPreviewGroup group,
        IReadOnlyDictionary<int, int> borderedStyles)
    {
        var part = repository.Parts.FirstOrDefault(item => item.PartNum.Equals(group.PartNum, StringComparison.OrdinalIgnoreCase));
        var blankCode = FirstNonBlank([group.BlankCode, part?.BlankCode ?? ""]);
        var holeSize = CleanWorkbookDisplayNumber(FirstNonBlank([group.HoleSize, part?.HoleSize ?? ""]));
        var totalQuantity = rows.Sum(row => row.Quantity);

        SetCellText(sheet, 10, 5, group.PartNum);
        SetCellText(sheet, 12, 5, blankCode);
        SetCellText(sheet, 14, 5, holeSize);
        SetCellText(sheet, 16, 5, group.JobNum);
        SetCellNumber(sheet, 18, 5, totalQuantity);
        SetCellText(sheet, 20, 5, group.PurchaseOrder);
        SetCellText(sheet, 22, 5, group.ShipmentDate);
        FillVisibleShipmentTable(sheet, rows, borderedStyles);
        EnsureVisibleShipmentTableBorderRule(sheet);

        var holeDiameter = BuildStats(group.PartNum, group.JobNum, "Hole Diameter");
        var holeDepth = BuildStats(group.PartNum, group.JobNum, "Hole Depth");
        if (family.Equals("TP", StringComparison.OrdinalIgnoreCase))
        {
            SetCellNumberOrBlank(sheet, 26, 5, holeDiameter.Mean);
            SetCellNumberOrBlank(sheet, 28, 5, holeDiameter.Min);
            SetCellNumberOrBlank(sheet, 30, 5, holeDiameter.Max);
            SetCellNumberOrBlank(sheet, 32, 5, holeDepth.Mean);
        }
        else
        {
            SetCellNumberOrBlank(sheet, 26, 5, holeDiameter.Mean);
            SetCellNumberOrBlank(sheet, 28, 5, holeDiameter.StandardDeviation);
            SetCellNumberOrBlank(sheet, 30, 5, holeDepth.Mean);
        }
    }

    private static void FillVisibleShipmentTable(
        XDocument sheet,
        IReadOnlyList<ShipmentRow> rows,
        IReadOnlyDictionary<int, int> borderedStyles)
    {
        for (var rowIndex = 10; rowIndex <= 1239; rowIndex++)
        {
            for (var columnIndex = 8; columnIndex <= 11; columnIndex++)
            {
                ClearCellValue(sheet, rowIndex, columnIndex);
            }
        }

        var sorted = rows
            .OrderBy(row => NumericSortValue(row.CarrierNumber))
            .ThenBy(row => row.CarrierNumber, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => NumericSortValue(row.BoxNumber))
            .ThenBy(row => row.BoxNumber, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        for (var index = 0; index < sorted.Length; index++)
        {
            var rowIndex = 10 + index;
            var row = sorted[index];
            SetCellText(sheet, rowIndex, 8, row.CarrierNumber);
            SetCellText(sheet, rowIndex, 9, row.BoxNumber);
            SetCellNumber(sheet, rowIndex, 10, row.Quantity);
            SetCellText(sheet, rowIndex, 11, row.WireShipment);
            ApplyShipmentTableBorderStyles(sheet, rowIndex, borderedStyles);
        }
    }

    private static void ApplyShipmentTableBorderStyles(
        XDocument sheet,
        int rowIndex,
        IReadOnlyDictionary<int, int> borderedStyles)
    {
        for (var columnIndex = 8; columnIndex <= 11; columnIndex++)
        {
            var cell = GetOrCreateCell(sheet, rowIndex, columnIndex);
            if (!int.TryParse(cell.Attribute("s")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var styleIndex))
            {
                styleIndex = 0;
            }

            if (borderedStyles.TryGetValue(styleIndex, out var borderedStyleIndex))
            {
                cell.SetAttributeValue("s", borderedStyleIndex.ToString(CultureInfo.InvariantCulture));
            }
        }
    }

    private static void EnsureVisibleShipmentTableBorderRule(XDocument sheet)
    {
        var root = sheet.Root ?? throw new InvalidOperationException("Worksheet metadata was invalid.");
        const string markerFormula = "LEN(H10)>0";
        root.Elements(SimpleWorkbook.SpreadsheetNs + "conditionalFormatting")
            .Where(element =>
                element.Attribute("sqref")?.Value.Equals("H10:K1239", StringComparison.OrdinalIgnoreCase) == true &&
                element.Elements(SimpleWorkbook.SpreadsheetNs + "cfRule").Any(rule =>
                    rule.Attribute("type")?.Value == "expression" &&
                    rule.Element(SimpleWorkbook.SpreadsheetNs + "formula")?.Value == markerFormula))
            .Remove();

        var maxPriority = root.Elements(SimpleWorkbook.SpreadsheetNs + "conditionalFormatting")
            .Elements(SimpleWorkbook.SpreadsheetNs + "cfRule")
            .Select(rule => int.TryParse(rule.Attribute("priority")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var priority) ? priority : 0)
            .DefaultIfEmpty(0)
            .Max();
        var newRule = new XElement(SimpleWorkbook.SpreadsheetNs + "conditionalFormatting",
            new XAttribute("sqref", "H10:K1239"),
            new XElement(SimpleWorkbook.SpreadsheetNs + "cfRule",
                new XAttribute("type", "expression"),
                new XAttribute("dxfId", "0"),
                new XAttribute("priority", (maxPriority + 1).ToString(CultureInfo.InvariantCulture)),
                new XElement(SimpleWorkbook.SpreadsheetNs + "formula", markerFormula)));

        var lastConditionalFormatting = root.Elements(SimpleWorkbook.SpreadsheetNs + "conditionalFormatting").LastOrDefault();
        if (lastConditionalFormatting is not null)
        {
            lastConditionalFormatting.AddAfterSelf(newRule);
            return;
        }

        var sheetData = root.Element(SimpleWorkbook.SpreadsheetNs + "sheetData");
        if (sheetData is not null)
        {
            sheetData.AddAfterSelf(newRule);
        }
        else
        {
            root.Add(newRule);
        }
    }

    private MeasurementStats BuildStats(string partNum, string jobNum, string characteristicName)
    {
        var values = repository.Measurements
            .Where(measurement =>
                measurement.PartNum.Equals(partNum, StringComparison.OrdinalIgnoreCase) &&
                measurement.JobNum.Equals(jobNum, StringComparison.OrdinalIgnoreCase) &&
                measurement.InspectionPhase.Equals("In Process", StringComparison.OrdinalIgnoreCase) &&
                measurement.CharacteristicName.Equals(characteristicName, StringComparison.OrdinalIgnoreCase))
            .Select(measurement => measurement.Value)
            .ToArray();
        if (values.Length == 0)
        {
            return new MeasurementStats(null, null, null, null);
        }

        var mean = values.Average();
        var variance = values.Length > 1
            ? values.Sum(value => (value - mean) * (value - mean)) / (values.Length - 1)
            : 0m;
        var standardDeviation = (decimal)Math.Sqrt((double)variance);
        return new MeasurementStats(values.Min(), values.Max(), mean, standardDeviation);
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
            var relationships = document.Root;
            if (relationships is null)
            {
                return;
            }

            relationships.Elements()
                .Where(item =>
                    item.Attribute("Target")?.Value.Equals("calcChain.xml", StringComparison.OrdinalIgnoreCase) == true ||
                    item.Attribute("Type")?.Value.EndsWith("/calcChain", StringComparison.OrdinalIgnoreCase) == true)
                .Remove();
        });

        UpdatePackageXml(archive, "[Content_Types].xml", document =>
        {
            var types = document.Root;
            if (types is null)
            {
                return;
            }

            types.Elements()
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

    private static IReadOnlyDictionary<int, int> EnsureShipmentTableBorderStyles(ZipArchive archive)
    {
        const string path = "xl/styles.xml";
        var entry = archive.GetEntry(path) ?? throw new InvalidOperationException("Workbook styles were not found.");
        XDocument document;
        using (var stream = entry.Open())
        {
            document = XDocument.Load(stream, LoadOptions.PreserveWhitespace);
        }

        var root = document.Root ?? throw new InvalidOperationException("Workbook styles were invalid.");
        var borders = root.Element(SimpleWorkbook.SpreadsheetNs + "borders")
            ?? throw new InvalidOperationException("Workbook border styles were invalid.");
        var cellXfs = root.Element(SimpleWorkbook.SpreadsheetNs + "cellXfs")
            ?? throw new InvalidOperationException("Workbook cell styles were invalid.");

        var sourceStyles = cellXfs.Elements(SimpleWorkbook.SpreadsheetNs + "xf").ToArray();
        var borderId = borders.Elements(SimpleWorkbook.SpreadsheetNs + "border").Count();
        borders.Add(new XElement(SimpleWorkbook.SpreadsheetNs + "border",
            ThinBorderSide("left"),
            ThinBorderSide("right"),
            ThinBorderSide("top"),
            ThinBorderSide("bottom"),
            new XElement(SimpleWorkbook.SpreadsheetNs + "diagonal")));
        borders.SetAttributeValue("count", borderId + 1);

        var styleMap = new Dictionary<int, int>();
        for (var index = 0; index < sourceStyles.Length; index++)
        {
            var clone = new XElement(sourceStyles[index]);
            clone.SetAttributeValue("borderId", borderId.ToString(CultureInfo.InvariantCulture));
            clone.SetAttributeValue("applyBorder", "1");
            styleMap[index] = sourceStyles.Length + index;
            cellXfs.Add(clone);
        }

        cellXfs.SetAttributeValue("count", sourceStyles.Length * 2);

        entry.Delete();
        var updatedEntry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var output = updatedEntry.Open();
        document.Save(output, SaveOptions.DisableFormatting);
        return styleMap;
    }

    private static XElement ThinBorderSide(string name)
    {
        return new XElement(SimpleWorkbook.SpreadsheetNs + name,
            new XAttribute("style", "thin"),
            new XElement(SimpleWorkbook.SpreadsheetNs + "color", new XAttribute("indexed", "64")));
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

    private static void SetCellNumberOrBlank(XDocument sheet, int rowIndex, int columnIndex, decimal? value)
    {
        if (value.HasValue)
        {
            SetCellNumber(sheet, rowIndex, columnIndex, value.Value);
            return;
        }

        SetCellText(sheet, rowIndex, columnIndex, "");
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

    private static void SetFormulaCachedText(XDocument sheet, int rowIndex, int columnIndex, string value)
    {
        var cell = GetOrCreateCell(sheet, rowIndex, columnIndex);
        if (cell.Element(SimpleWorkbook.SpreadsheetNs + "f") is null)
        {
            SetCellText(sheet, rowIndex, columnIndex, value);
            return;
        }

        cell.SetAttributeValue("t", "str");
        cell.Element(SimpleWorkbook.SpreadsheetNs + "is")?.Remove();
        cell.Element(SimpleWorkbook.SpreadsheetNs + "v")?.Remove();
        cell.Add(new XElement(SimpleWorkbook.SpreadsheetNs + "v", value ?? ""));
    }

    private static void SetFormulaCachedNumber(XDocument sheet, int rowIndex, int columnIndex, decimal value)
    {
        var cell = GetOrCreateCell(sheet, rowIndex, columnIndex);
        if (cell.Element(SimpleWorkbook.SpreadsheetNs + "f") is null)
        {
            SetCellNumber(sheet, rowIndex, columnIndex, value);
            return;
        }

        cell.Attribute("t")?.Remove();
        cell.Element(SimpleWorkbook.SpreadsheetNs + "is")?.Remove();
        cell.Element(SimpleWorkbook.SpreadsheetNs + "v")?.Remove();
        cell.Add(new XElement(SimpleWorkbook.SpreadsheetNs + "v", value.ToString("0.############################", CultureInfo.InvariantCulture)));
    }

    private static void ClearFormulaCachedValue(XDocument sheet, int rowIndex, int columnIndex)
    {
        var cell = FindCell(sheet, rowIndex, columnIndex);
        if (cell is null)
        {
            return;
        }

        if (cell.Element(SimpleWorkbook.SpreadsheetNs + "f") is null)
        {
            ClearCellValue(sheet, rowIndex, columnIndex);
            return;
        }

        cell.Attribute("t")?.Remove();
        cell.Element(SimpleWorkbook.SpreadsheetNs + "is")?.Remove();
        cell.Element(SimpleWorkbook.SpreadsheetNs + "v")?.Remove();
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

    private static void RemoveCell(XDocument sheet, int rowIndex, int columnIndex)
    {
        FindCell(sheet, rowIndex, columnIndex)?.Remove();
    }

    private static string CellReference(int rowIndex, int columnIndex)
    {
        var column = "";
        var value = columnIndex;
        while (value > 0)
        {
            value--;
            column = (char)('A' + value % 26) + column;
            value /= 26;
        }

        return $"{column}{rowIndex}";
    }

    private static decimal NumericSortValue(string value)
    {
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : decimal.MaxValue;
    }

    private static string SafeFileName(string value)
    {
        var cleaned = Regex.Replace(value ?? "", @"[^A-Za-z0-9._-]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(cleaned) ? "COA" : cleaned;
    }

    private sealed record ShipmentRow(
        string PartNum,
        string JobNum,
        string CarrierNumber,
        string BoxNumber,
        decimal Quantity,
        string WireShipment,
        string BlankCode,
        string BihlerLot,
        string Condition,
        string HoleSize);

    private sealed record ShipmentMasterRecord(string PartNum, string PurchaseOrder, string Lot);

    private sealed record ShipmentMaster(DateTime? ShipmentDate, IReadOnlyList<ShipmentMasterRecord> Records)
    {
        public string FindPurchaseOrder(string partNum, string jobNum)
        {
            var exact = Records.FirstOrDefault(record =>
                record.PartNum.Equals(partNum, StringComparison.OrdinalIgnoreCase) &&
                record.Lot.Equals(jobNum, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(record.PurchaseOrder));
            if (exact is not null)
            {
                return exact.PurchaseOrder;
            }

            return Records.FirstOrDefault(record =>
                record.PartNum.Equals(partNum, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(record.PurchaseOrder))?.PurchaseOrder ?? "";
        }
    }

    private sealed record MeasurementStats(decimal? Min, decimal? Max, decimal? Mean, decimal? StandardDeviation);
    private sealed record SeedGroupResult(EthiconCoaSeedGroup Group, int JobsCreated, int JobsUpdated);
    private sealed record SeedInspectionPlan(
        InspectionPlan Plan,
        Characteristic Characteristic,
        Operation Operation,
        ManufacturingProcess Process);

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
    }
}
