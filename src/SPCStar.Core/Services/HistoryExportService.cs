using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using System.IO.Compression;
using System.Text;

namespace SPCStar.Core.Services;

public sealed record InspectionHistoryExportRequest(
    IReadOnlyCollection<string> PartNums,
    IReadOnlyCollection<string> JobNums,
    IReadOnlyCollection<string> ResourceIds,
    IReadOnlyCollection<string> CharacteristicNames,
    DateTimeOffset? From,
    DateTimeOffset? To);

public sealed record AlertHistoryExportRequest(
    IReadOnlyCollection<string> PartNums,
    IReadOnlyCollection<string> JobNums,
    IReadOnlyCollection<string> ResourceIds,
    IReadOnlyCollection<string> CharacteristicNames,
    DateTimeOffset? From,
    DateTimeOffset? To,
    bool IncludeOverridden);

public sealed record MaterialHistoryExportRequest(
    IReadOnlyCollection<string> PartNums,
    IReadOnlyCollection<string> JobNums,
    IReadOnlyCollection<string> ResourceIds,
    DateTimeOffset? From,
    DateTimeOffset? To);

public sealed record LedgerHistoryExportRequest(
    IReadOnlyCollection<string> PartNums,
    IReadOnlyCollection<string> JobNums,
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Operation = null,
    string? InspectionPhase = null,
    string? Shift = null,
    string? ResourceId = null,
    string? CharacteristicName = null);

public sealed class HistoryExportService(ISpcRepository repository)
{
    private static readonly string[] InspectionHeaders =
    [
        "JobNum",
        "PartNum",
        "ProcessCode",
        "OperationSeq",
        "ResourceID",
        "CharacteristicName",
        "MeasurementValue",
        "Timestamp",
        "OperatorUserID",
        "OperatorShift",
        "InspectionPhase"
    ];

    private static readonly string[] AlertHeaders =
    [
        "AlertID",
        "JobNum",
        "PartNum",
        "ResourceID",
        "CharacteristicName",
        "OperatorUserID",
        "OperatorShift",
        "RuleTriggered",
        "LockedAt",
        "UnlockedAt",
        "Status",
        "ClearedBy",
        "ClearedByRole",
        "CauseCategory",
        "CauseDetails",
        "ActionTaken",
        "BypassReason",
        "LockDetails"
    ];

    private static readonly string[] MaterialHeaders =
    [
        "JobNum",
        "PartNum",
        "MaterialPartNum",
        "OldLotNum",
        "NewLotNum",
        "QuantityLoaded",
        "ResourceID",
        "OperatorUserID",
        "Timestamp",
        "Reason",
        "MaterialName",
        "MaterialDescription"
    ];

    private static readonly string[] LedgerHeaders =
    [
        "Date / Time",
        "Record Type",
        "Job",
        "Part",
        "Phase",
        "Operation",
        "Machine",
        "Inspection Item / Event",
        "Result / Value",
        "User",
        "Shift",
        "Status",
        "Reason / Details"
    ];

    private static readonly string[] SummaryHeaders = ["Item", "Value"];
    private static readonly string[] JobNoteHeaders = ["Date / Time", "Job", "Part", "Machine", "User", "Note", "Replies"];

    private static readonly string[] EditAuditHeaders =
    [
        "Edited At",
        "Job",
        "Part",
        "Phase",
        "Operation",
        "Machine",
        "Inspection Item",
        "Original Value",
        "Corrected Value",
        "Edited By",
        "Shift",
        "Reason"
    ];

    private static readonly string[] LockoutHeaders =
    [
        "Locked At",
        "Unlocked At",
        "Job",
        "Part",
        "Phase",
        "Operation",
        "Machine",
        "Inspection Item",
        "Rule / Trigger",
        "Status",
        "Locked By",
        "Locked By Shift",
        "Cleared By",
        "Cleared By Role",
        "Cause Category",
        "Cause Details",
        "Action Taken",
        "Bypass Reason",
        "Lock Details"
    ];

    public string ExportInspectionCsv(InspectionHistoryExportRequest request)
    {
        var rows = FilterMeasurements(request)
            .OrderBy(item => item.Timestamp)
            .Select(item => new Dictionary<string, string>
            {
                ["JobNum"] = item.JobNum,
                ["PartNum"] = item.PartNum,
                ["ProcessCode"] = item.ProcessCode,
                ["OperationSeq"] = item.OperationSeq.ToString(),
                ["ResourceID"] = item.ResourceId,
                ["CharacteristicName"] = item.CharacteristicName,
                ["MeasurementValue"] = item.Value.ToString("0.#####"),
                ["Timestamp"] = item.Timestamp.ToString("O"),
                ["OperatorUserID"] = item.OperatorUserId,
                ["OperatorShift"] = item.OperatorShift,
                ["InspectionPhase"] = item.InspectionPhase
            });

        return CsvSupport.WriteRows(InspectionHeaders, rows);
    }

    public string ExportJobInspectionHistoryCsv(string jobNum)
    {
        return ExportInspectionCsv(new InspectionHistoryExportRequest([], [jobNum], [], [], null, null));
    }

    public string ExportAlertHistoryCsv(AlertHistoryExportRequest request)
    {
        var partNums = request.PartNums.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var jobNums = request.JobNums.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var resourceIds = request.ResourceIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var characteristics = request.CharacteristicNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var overridesByAlert = repository.AlertOverrides
            .GroupBy(item => item.AlertId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(item => item.UnlockedAt).First());

        var rows = repository.Alerts
            .Where(alert =>
                (request.IncludeOverridden || alert.Status != AlertStatus.Overridden) &&
                Matches(partNums, alert.PartNum) &&
                Matches(jobNums, alert.JobNum) &&
                Matches(resourceIds, alert.ResourceId) &&
                Matches(characteristics, alert.CharacteristicName) &&
                (!request.From.HasValue || alert.LockedAt >= request.From.Value) &&
                (!request.To.HasValue || alert.LockedAt <= request.To.Value))
            .OrderBy(alert => alert.LockedAt)
            .Select(alert =>
            {
                overridesByAlert.TryGetValue(alert.Id, out var alertOverride);
                return new Dictionary<string, string>
                {
                    ["AlertID"] = alert.Id.ToString(),
                    ["JobNum"] = alert.JobNum,
                    ["PartNum"] = alert.PartNum,
                    ["ResourceID"] = alert.ResourceId,
                    ["CharacteristicName"] = alert.CharacteristicName,
                    ["OperatorUserID"] = alert.OperatorUserId,
                    ["OperatorShift"] = alert.OperatorShift,
                    ["RuleTriggered"] = alert.RuleTriggered.ToString(),
                    ["LockedAt"] = alert.LockedAt.ToString("O"),
                    ["UnlockedAt"] = alertOverride?.UnlockedAt.ToString("O") ?? "",
                    ["Status"] = alert.Status.ToString(),
                    ["ClearedBy"] = alertOverride?.OverrideUserId ?? "",
                    ["ClearedByRole"] = alertOverride?.OverrideRole ?? "",
                    ["CauseCategory"] = DisplayCauseCategory(alertOverride) ?? "",
                    ["CauseDetails"] = DisplayCause(alertOverride) ?? "",
                    ["ActionTaken"] = DisplaySolution(alertOverride) ?? "",
                    ["BypassReason"] = alertOverride?.WhyStandardProcessWasBypassed ?? "",
                    ["LockDetails"] = alert.Detail ?? ""
                };
            });

        return CsvSupport.WriteRows(AlertHeaders, rows);
    }

    public string ExportMaterialChangeHistoryCsv(MaterialHistoryExportRequest request)
    {
        var partNums = request.PartNums.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var jobNums = request.JobNums.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var resourceIds = request.ResourceIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rows = repository.MaterialChanges
            .Where(log =>
                Matches(partNums, log.PartNum) &&
                Matches(jobNums, log.JobNum) &&
                Matches(resourceIds, log.ResourceId) &&
                (!request.From.HasValue || log.Timestamp >= request.From.Value) &&
                (!request.To.HasValue || log.Timestamp <= request.To.Value))
            .OrderBy(log => log.Timestamp)
            .Select(log => new Dictionary<string, string>
            {
                ["JobNum"] = log.JobNum,
                ["PartNum"] = log.PartNum,
                ["MaterialPartNum"] = log.MaterialPartNum,
                ["MaterialName"] = JobMaterialRules.MaterialName(repository, log),
                ["MaterialDescription"] = JobMaterialRules.MaterialDescription(repository, log),
                ["OldLotNum"] = log.OldLotNum,
                ["NewLotNum"] = log.NewLotNum,
                ["QuantityLoaded"] = log.QuantityLoaded?.ToString("0.#####") ?? string.Empty,
                ["ResourceID"] = log.ResourceId,
                ["OperatorUserID"] = log.OperatorUserId,
                ["Timestamp"] = log.Timestamp.ToString("O"),
                ["Reason"] = log.Reason
            });

        return CsvSupport.WriteRows(MaterialHeaders, rows);
    }

    public byte[] ExportLedgerXlsx(LedgerHistoryExportRequest request)
    {
        var rows = BuildLedgerRows(request)
            .Where(row =>
                MatchesTextFilter(request.Operation, row["Operation"]) &&
                MatchesTextFilter(request.InspectionPhase, row["Phase"]) &&
                MatchesShift(request.Shift, row["Shift"]))
            .Where(row => DataReviewFilter.Text(request.ResourceId, row["Machine"]))
            .OrderByDescending(row => row["Date / Time"])
            .ToArray();

        var lockoutRows = BuildLockoutRows(request)
            .Where(row =>
                MatchesTextFilter(request.Operation, row["Operation"]) &&
                MatchesTextFilter(request.InspectionPhase, row["Phase"]) &&
                MatchesShift(request.Shift, row["Locked By Shift"]))
            .Where(row => DataReviewFilter.Text(request.ResourceId, row["Machine"]) &&
                DataReviewFilter.Text(request.CharacteristicName, row["Inspection Item"]))
            .OrderByDescending(row => row["Locked At"])
            .ToArray();
        var editRows = BuildEditAuditRows(request)
            .Where(row =>
                MatchesTextFilter(request.Operation, row["Operation"]) &&
                MatchesTextFilter(request.InspectionPhase, row["Phase"]) &&
                MatchesShift(request.Shift, row["Shift"]))
            .Where(row => DataReviewFilter.Text(request.ResourceId, row["Machine"]) &&
                DataReviewFilter.Text(request.CharacteristicName, row["Inspection Item"]))
            .OrderByDescending(row => row["Edited At"])
            .ToArray();
        var noteRows = FilterLedgerNotes(request)
            .Where(note => MatchesShift(request.Shift, UserShift(note.OperatorUserId)))
            .Where(note => DataReviewFilter.Text(request.ResourceId, note.ResourceId))
            .OrderByDescending(note => note.Timestamp)
            .ThenBy(note => note.Id)
            .Select(note => new Dictionary<string, string>
            {
                ["Date / Time"] = note.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                ["Job"] = note.JobNum,
                ["Part"] = note.PartNum,
                ["Machine"] = note.ResourceId,
                ["User"] = note.OperatorUserId,
                ["Note"] = note.NoteText,
                ["Replies"] = string.Join(Environment.NewLine + Environment.NewLine, note.Replies.OrderBy(reply => reply.Timestamp).ThenBy(reply => reply.Id)
                    .Select(reply => $"{reply.Timestamp.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz} / {reply.UserName}{Environment.NewLine}{reply.ReplyText}"))
            }).ToArray();
        var summaryRows = BuildLedgerSummaryRows(request, rows, lockoutRows, editRows)
            .Append(SummaryRow("Job notes rows", noteRows.Length.ToString())).ToArray();
        return XlsxSupport.WriteWorkbook([
            new XlsxWorksheet("Summary", SummaryHeaders, summaryRows),
            new XlsxWorksheet("Ledger", LedgerHeaders, rows),
            new XlsxWorksheet("Lockouts", LockoutHeaders, lockoutRows),
            new XlsxWorksheet("Edit Audit", EditAuditHeaders, editRows),
            new XlsxWorksheet("Job Notes", JobNoteHeaders, noteRows)
        ]);
    }

    private static IEnumerable<Dictionary<string, string>> BuildLedgerSummaryRows(
        LedgerHistoryExportRequest request,
        IReadOnlyCollection<Dictionary<string, string>> rows,
        IReadOnlyCollection<Dictionary<string, string>> lockoutRows,
        IReadOnlyCollection<Dictionary<string, string>> editRows)
    {
        yield return SummaryRow("Generated", DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        yield return SummaryRow("Part filter", request.PartNums.Count == 0 ? "All parts" : string.Join(", ", request.PartNums));
        yield return SummaryRow("Job filter", request.JobNums.Count == 0 ? "All jobs" : string.Join(", ", request.JobNums));
        yield return SummaryRow("From", request.From?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "No start date");
        yield return SummaryRow("To", request.To?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "No end date");
        yield return SummaryRow("Operation filter", string.IsNullOrWhiteSpace(request.Operation) ? "All operations" : request.Operation.Trim());
        yield return SummaryRow("Inspection phase filter", string.IsNullOrWhiteSpace(request.InspectionPhase) ? "All phases" : request.InspectionPhase.Trim());
        yield return SummaryRow("Shift filter", string.IsNullOrWhiteSpace(request.Shift) ? "All shifts" : request.Shift.Trim());
        yield return SummaryRow("Machine filter", string.IsNullOrWhiteSpace(request.ResourceId) ? "All machines" : request.ResourceId.Trim());
        yield return SummaryRow("Variable filter", string.IsNullOrWhiteSpace(request.CharacteristicName) ? "All variables" : request.CharacteristicName.Trim());
        yield return SummaryRow("Total ledger rows", rows.Count.ToString());
        yield return SummaryRow("Lockout rows", lockoutRows.Count.ToString());
        yield return SummaryRow("Edit audit rows", editRows.Count.ToString());

        foreach (var group in rows
            .GroupBy(row => row["Record Type"], StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            yield return SummaryRow($"{group.Key} rows", group.Count().ToString());
        }
    }

    private IEnumerable<Dictionary<string, string>> BuildLockoutRows(LedgerHistoryExportRequest request)
    {
        var overridesByAlert = repository.AlertOverrides
            .GroupBy(item => item.AlertId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(item => item.UnlockedAt).First());

        foreach (var alert in FilterLedgerAlerts(request))
        {
            overridesByAlert.TryGetValue(alert.Id, out var alertOverride);
            var measurement = FindMeasurementForAlert(alert);
            var operation = measurement is null ? "" : $"{measurement.ProcessCode} {measurement.OperationSeq}";
            var phase = measurement?.InspectionPhase ?? "";

            yield return new Dictionary<string, string>
            {
                ["Locked At"] = alert.LockedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                ["Unlocked At"] = alertOverride?.UnlockedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                ["Job"] = alert.JobNum,
                ["Part"] = alert.PartNum,
                ["Phase"] = phase,
                ["Operation"] = operation,
                ["Machine"] = alert.ResourceId,
                ["Inspection Item"] = alert.CharacteristicName,
                ["Rule / Trigger"] = RuleTriggerLabel(alert.RuleTriggered),
                ["Status"] = alert.Status.ToString(),
                ["Locked By"] = alert.OperatorUserId,
                ["Locked By Shift"] = alert.OperatorShift,
                ["Cleared By"] = alertOverride?.OverrideUserId ?? "",
                ["Cleared By Role"] = alertOverride?.OverrideRole ?? "",
                ["Cause Category"] = DisplayCauseCategory(alertOverride) ?? "",
                ["Cause Details"] = DisplayCause(alertOverride) ?? "",
                ["Action Taken"] = DisplaySolution(alertOverride) ?? "",
                ["Bypass Reason"] = alertOverride?.WhyStandardProcessWasBypassed ?? "",
                ["Lock Details"] = alert.Detail ?? ""
            };
        }
    }

    private string? DisplaySolution(AlertOverride? alertOverride)
    {
        if (alertOverride?.OverrideRole.Equals("Failed Inspection", StringComparison.OrdinalIgnoreCase) == true)
        {
            var failedSolution = repository.FailedInspections
                .Where(failure => failure.AlertId == alertOverride.AlertId)
                .OrderByDescending(failure => failure.FailedAt)
                .Select(failure => string.IsNullOrWhiteSpace(failure.SolutionText) ? failure.Reason : failure.SolutionText)
                .FirstOrDefault(reason => !string.IsNullOrWhiteSpace(reason));
            if (!string.IsNullOrWhiteSpace(failedSolution))
            {
                return failedSolution.Trim();
            }
        }

        if (!string.IsNullOrWhiteSpace(alertOverride?.WhyStandardProcessWasBypassed))
        {
            return alertOverride.WhyStandardProcessWasBypassed.Trim();
        }

        return string.IsNullOrWhiteSpace(alertOverride?.SolutionText)
            ? null
            : alertOverride.SolutionText.Trim();
    }

    private string? DisplayCause(AlertOverride? alertOverride)
    {
        if (alertOverride?.OverrideRole.Equals("Failed Inspection", StringComparison.OrdinalIgnoreCase) == true)
        {
            var failedCause = repository.FailedInspections
                .Where(failure => failure.AlertId == alertOverride.AlertId)
                .OrderByDescending(failure => failure.FailedAt)
                .Select(failure => string.IsNullOrWhiteSpace(failure.CauseText) ? failure.FailureDetail : failure.CauseText)
                .FirstOrDefault(reason => !string.IsNullOrWhiteSpace(reason));
            if (!string.IsNullOrWhiteSpace(failedCause))
            {
                return failedCause.Trim();
            }
        }

        if (!string.IsNullOrWhiteSpace(alertOverride?.WhyStandardProcessWasBypassed))
        {
            return alertOverride.WhyStandardProcessWasBypassed.Trim();
        }

        if (!string.IsNullOrWhiteSpace(alertOverride?.CauseText) &&
            !alertOverride.CauseText.Equals("Standard correction workflow bypassed by System Manager access.", StringComparison.OrdinalIgnoreCase))
        {
            return alertOverride.CauseText.Trim();
        }

        return null;
    }

    private static string? DisplayCauseCategory(AlertOverride? alertOverride)
    {
        if (string.IsNullOrWhiteSpace(alertOverride?.CauseCategory))
        {
            return null;
        }

        var category = alertOverride.CauseCategory.Trim();
        return category.Equals("System Manager Bypass", StringComparison.OrdinalIgnoreCase) ||
            category.Equals("Unspecified", StringComparison.OrdinalIgnoreCase)
                ? null
                : category;
    }

    private IEnumerable<Dictionary<string, string>> BuildEditAuditRows(LedgerHistoryExportRequest request)
    {
        foreach (var edit in FilterLedgerEdits(request))
        {
            var measurement = repository.Measurements.FirstOrDefault(item => item.Id == edit.MeasurementId);
            var operation = measurement is null ? "" : $"{measurement.ProcessCode} {measurement.OperationSeq}";
            var phase = measurement?.InspectionPhase ?? edit.NewInspectionPhase;

            yield return new Dictionary<string, string>
            {
                ["Edited At"] = edit.EditedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                ["Job"] = edit.JobNum,
                ["Part"] = edit.PartNum,
                ["Phase"] = phase,
                ["Operation"] = operation,
                ["Machine"] = edit.ResourceId,
                ["Inspection Item"] = edit.CharacteristicName,
                ["Original Value"] = $"{edit.OldInspectionPhase}: {edit.OldValue:0.#####}",
                ["Corrected Value"] = $"{edit.NewInspectionPhase}: {edit.NewValue:0.#####}",
                ["Edited By"] = edit.EditedByUserId,
                ["Shift"] = UserShift(edit.EditedByUserId),
                ["Reason"] = edit.Reason
            };
        }

        foreach (var edit in FilterLedgerMachineCounterEdits(request).Where(_ => string.IsNullOrWhiteSpace(request.CharacteristicName)))
        {
            yield return new Dictionary<string, string>
            {
                ["Edited At"] = edit.EditedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                ["Job"] = edit.JobNum,
                ["Part"] = edit.PartNum,
                ["Phase"] = edit.InspectionPhase,
                ["Operation"] = $"{edit.ProcessCode} {edit.OperationSeq}",
                ["Machine"] = edit.ResourceId,
                ["Inspection Item"] = "Machine Counter",
                ["Original Value"] = edit.OldMachineCounter?.ToString() ?? "",
                ["Corrected Value"] = edit.NewMachineCounter?.ToString() ?? "",
                ["Edited By"] = edit.EditedByUserId,
                ["Shift"] = UserShift(edit.EditedByUserId),
                ["Reason"] = edit.Reason
            };
        }
    }

    private static Dictionary<string, string> SummaryRow(string item, string value)
    {
        return new Dictionary<string, string>
        {
            ["Item"] = item,
            ["Value"] = value
        };
    }

    private IEnumerable<Dictionary<string, string>> BuildLedgerRows(LedgerHistoryExportRequest request)
    {
        var inspectionNumbers = InspectionSequence.Numbers(repository);
        foreach (var measurement in FilterLedgerMeasurements(request).Where(item => DataReviewFilter.Text(request.CharacteristicName, item.CharacteristicName)))
        {
            yield return LedgerRow(
                measurement.Timestamp,
                "Inspection",
                measurement.JobNum,
                measurement.PartNum,
                measurement.InspectionPhase,
                measurement.CharacteristicName,
                measurement.Value.ToString("0.#####"),
                measurement.ResourceId,
                $"{measurement.ProcessCode} {measurement.OperationSeq}",
                measurement.OperatorUserId,
                measurement.OperatorShift,
                string.IsNullOrWhiteSpace(measurement.WatchReason) ? "" : "Watch",
                measurement.WatchReason);
        }

        foreach (var note in FilterLedgerNotes(request).Where(_ => string.IsNullOrWhiteSpace(request.CharacteristicName)))
        {
            yield return LedgerRow(
                note.Timestamp,
                "Note",
                note.JobNum,
                note.PartNum,
                "",
                "Job Note",
                note.NoteText,
                note.ResourceId,
                "",
                note.OperatorUserId,
                UserShift(note.OperatorUserId),
                "",
                "");
        }

        foreach (var material in FilterLedgerMaterials(request).Where(_ => string.IsNullOrWhiteSpace(request.CharacteristicName)))
        {
            yield return LedgerRow(
                material.Timestamp,
                "Material",
                material.JobNum,
                material.PartNum,
                "",
                string.IsNullOrWhiteSpace(JobMaterialRules.MaterialName(repository, material))
                    ? material.MaterialPartNum
                    : $"{JobMaterialRules.MaterialName(repository, material)} ({material.MaterialPartNum})",
                material.NewLotNum,
                material.ResourceId,
                "",
                material.OperatorUserId,
                UserShift(material.OperatorUserId),
                "",
                string.Join(" - ", new[] { JobMaterialRules.MaterialDescription(repository, material), material.Reason }.Where(value => !string.IsNullOrWhiteSpace(value))));
        }

        foreach (var tag in FilterLedgerTags(request).Where(_ => string.IsNullOrWhiteSpace(request.CharacteristicName)))
        {
            yield return LedgerRow(
                tag.UpdatedAt,
                LooksLikeMaterialTag(tag.TagName) ? "Material Data" : "Job Data",
                tag.JobNum,
                tag.PartNum,
                "",
                tag.TagName,
                tag.TagValue,
                tag.ResourceId,
                "",
                tag.OperatorUserId,
                UserShift(tag.OperatorUserId),
                "",
                "");
        }

        foreach (var completion in FilterLedgerCompletions(request).Where(item => HasLedgerVariable(item.MeasurementIds, request.CharacteristicName)))
        {
            yield return LedgerRow(
                completion.CompletedAt,
                "Inspection Complete",
                completion.JobNum,
                completion.PartNum,
                completion.InspectionPhase,
                $"{completion.InspectionPhase} inspection {inspectionNumbers[completion.Id]} complete",
                completion.MachineCounter.HasValue ? $"Machine Counter: {completion.MachineCounter}" : "",
                completion.ResourceId,
                $"{completion.ProcessCode} {completion.OperationSeq}",
                completion.CompletedByUserId,
                completion.OperatorShift,
                "Complete",
                $"{completion.MeasurementIds.Count} inspection entries");
        }

        foreach (var failure in FilterLedgerFailures(request).Where(item => HasLedgerVariable(item.MeasurementIds, request.CharacteristicName) ||
            repository.Alerts.Any(alert => alert.Id == item.AlertId && DataReviewFilter.Text(request.CharacteristicName, alert.CharacteristicName))))
        {
            yield return LedgerRow(
                failure.FailedAt,
                "Inspection Failed",
                failure.JobNum,
                failure.PartNum,
                failure.InspectionPhase,
                $"{failure.InspectionPhase} inspection {inspectionNumbers[failure.Id]} failed",
                failure.MachineCounter.HasValue ? $"Machine Counter: {failure.MachineCounter}" : "",
                failure.ResourceId,
                $"{failure.ProcessCode} {failure.OperationSeq}",
                failure.FailedByUserId,
                failure.OperatorShift,
                "Failed",
                FailedInspectionLedgerDetails(failure));
        }

        foreach (var edit in FilterLedgerMachineCounterEdits(request).Where(_ => string.IsNullOrWhiteSpace(request.CharacteristicName)))
        {
            yield return LedgerRow(
                edit.EditedAt,
                "Machine Counter Edit",
                edit.JobNum,
                edit.PartNum,
                edit.InspectionPhase,
                "Machine Counter",
                $"{edit.OldMachineCounter?.ToString() ?? "blank"} -> {edit.NewMachineCounter?.ToString() ?? "blank"}",
                edit.ResourceId,
                $"{edit.ProcessCode} {edit.OperationSeq}",
                edit.EditedByUserId,
                UserShift(edit.EditedByUserId),
                "Edited",
                edit.Reason);
        }
    }

    private static string FailedInspectionLedgerDetails(FailedInspection failure)
    {
        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(failure.FailureDetail))
        {
            details.Add($"Failed: {failure.FailureDetail.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(failure.CauseCategory))
        {
            details.Add($"Cause category: {failure.CauseCategory.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(failure.CauseText))
        {
            details.Add($"Cause details: {failure.CauseText.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(failure.SolutionText))
        {
            details.Add($"Action taken: {failure.SolutionText.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(failure.Reason))
        {
            details.Add($"Reason: {failure.Reason.Trim()}");
        }

        if (details.Count == 0)
        {
            details.Add($"{failure.MeasurementIds.Count} inspection entries");
        }

        return string.Join("; ", details);
    }

    private IEnumerable<InspectionMeasurement> FilterLedgerMeasurements(LedgerHistoryExportRequest request)
    {
        var partNums = request.PartNums.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var jobNums = request.JobNums.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return repository.Measurements.Where(item =>
            Matches(partNums, item.PartNum) &&
            Matches(jobNums, item.JobNum) &&
            InRange(item.Timestamp, request.From, request.To));
    }

    private IEnumerable<ProcessAlert> FilterLedgerAlerts(LedgerHistoryExportRequest request)
    {
        var partNums = request.PartNums.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var jobNums = request.JobNums.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return repository.Alerts.Where(item =>
            item.Status != AlertStatus.Warning && item.Status != AlertStatus.Resolved &&
            Matches(partNums, item.PartNum) &&
            Matches(jobNums, item.JobNum) &&
            InRange(item.LockedAt, request.From, request.To));
    }

    private IEnumerable<MeasurementEditAudit> FilterLedgerEdits(LedgerHistoryExportRequest request)
    {
        var partNums = request.PartNums.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var jobNums = request.JobNums.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return repository.MeasurementEditAudits.Where(item =>
            Matches(partNums, item.PartNum) &&
            Matches(jobNums, item.JobNum) &&
            InRange(item.EditedAt, request.From, request.To));
    }

    private IEnumerable<MachineCounterEditAudit> FilterLedgerMachineCounterEdits(LedgerHistoryExportRequest request)
    {
        var partNums = request.PartNums.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var jobNums = request.JobNums.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return repository.MachineCounterEditAudits.Where(item =>
            Matches(partNums, item.PartNum) &&
            Matches(jobNums, item.JobNum) &&
            InRange(item.EditedAt, request.From, request.To));
    }

    private IEnumerable<JobNote> FilterLedgerNotes(LedgerHistoryExportRequest request)
    {
        var partNums = request.PartNums.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var jobNums = request.JobNums.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return repository.JobNotes.Where(item =>
            Matches(partNums, item.PartNum) &&
            Matches(jobNums, item.JobNum) &&
            InRange(item.Timestamp, request.From, request.To));
    }

    private IEnumerable<MaterialChangeLog> FilterLedgerMaterials(LedgerHistoryExportRequest request)
    {
        var partNums = request.PartNums.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var jobNums = request.JobNums.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return repository.MaterialChanges.Where(item =>
            Matches(partNums, item.PartNum) &&
            Matches(jobNums, item.JobNum) &&
            InRange(item.Timestamp, request.From, request.To));
    }

    private IEnumerable<JobTag> FilterLedgerTags(LedgerHistoryExportRequest request)
    {
        var partNums = request.PartNums.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var jobNums = request.JobNums.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return repository.JobTags.Where(item =>
            Matches(partNums, item.PartNum) &&
            Matches(jobNums, item.JobNum) &&
            InRange(item.UpdatedAt, request.From, request.To));
    }

    private IEnumerable<JobPhaseCompletion> FilterLedgerCompletions(LedgerHistoryExportRequest request)
    {
        var partNums = request.PartNums.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var jobNums = request.JobNums.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return repository.JobPhaseCompletions.Where(item =>
            Matches(partNums, item.PartNum) &&
            Matches(jobNums, item.JobNum) &&
            InRange(item.CompletedAt, request.From, request.To));
    }

    private IEnumerable<FailedInspection> FilterLedgerFailures(LedgerHistoryExportRequest request)
    {
        var partNums = request.PartNums.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var jobNums = request.JobNums.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return repository.FailedInspections.Where(item =>
            Matches(partNums, item.PartNum) &&
            Matches(jobNums, item.JobNum) &&
            InRange(item.FailedAt, request.From, request.To));
    }

    private IEnumerable<InspectionMeasurement> FilterMeasurements(InspectionHistoryExportRequest request)
    {
        var partNums = request.PartNums.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var jobNums = request.JobNums.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var resourceIds = request.ResourceIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var characteristics = request.CharacteristicNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return repository.Measurements.Where(item =>
            Matches(partNums, item.PartNum) &&
            Matches(jobNums, item.JobNum) &&
            Matches(resourceIds, item.ResourceId) &&
            Matches(characteristics, item.CharacteristicName) &&
            (!request.From.HasValue || item.Timestamp >= request.From.Value) &&
            (!request.To.HasValue || item.Timestamp <= request.To.Value));
    }

    private static bool Matches(IReadOnlySet<string> filters, string value)
    {
        return filters.Count == 0 || filters.Contains(value);
    }

    private static bool MatchesShift(string? filter, string value) =>
        string.IsNullOrWhiteSpace(filter) || value.Equals(filter.Trim(), StringComparison.OrdinalIgnoreCase);

    private string UserShift(string userName)
    {
        return repository.Users.FirstOrDefault(user => user.UserName.Equals(userName, StringComparison.OrdinalIgnoreCase))?.Shift ?? "";
    }

    private static bool InRange(DateTimeOffset timestamp, DateTimeOffset? from, DateTimeOffset? to)
    {
        return (!from.HasValue || timestamp >= from.Value) &&
            (!to.HasValue || timestamp <= to.Value);
    }

    private bool HasLedgerVariable(IEnumerable<Guid> ids, string? name) => string.IsNullOrWhiteSpace(name) ||
        repository.Measurements.Any(item => ids.Contains(item.Id) && DataReviewFilter.Text(name, item.CharacteristicName));

    private static bool MatchesTextFilter(string? filter, string value)
    {
        return string.IsNullOrWhiteSpace(filter) ||
            value.Trim().Equals(filter.Trim().Replace('|', ' '), StringComparison.OrdinalIgnoreCase);
    }

    private InspectionMeasurement? FindMeasurementForAlert(ProcessAlert alert)
    {
        return repository.Measurements
            .Where(item =>
                item.JobNum.Equals(alert.JobNum, StringComparison.OrdinalIgnoreCase) &&
                item.PartNum.Equals(alert.PartNum, StringComparison.OrdinalIgnoreCase) &&
                item.ResourceId.Equals(alert.ResourceId, StringComparison.OrdinalIgnoreCase) &&
                item.CharacteristicName.Equals(alert.CharacteristicName, StringComparison.OrdinalIgnoreCase) &&
                item.Timestamp <= alert.LockedAt.AddMinutes(1))
            .OrderByDescending(item => item.Timestamp)
            .FirstOrDefault();
    }

    private static string RuleTriggerLabel(RuleTriggered rule)
    {
        return rule switch
        {
            RuleTriggered.OnePointBeyondControlLimit => "One point beyond control limit",
            RuleTriggered.TwoOfThreeNearControlLimit => "Two of three near control limit",
            RuleTriggered.FourOfFiveApproachingLimit => "Four of five approaching limit",
            RuleTriggered.EightConsecutiveOneSideOfCenterline => "Eight consecutive on one side of centerline",
            RuleTriggered.SpecLimitViolation => "Specification limit violation",
            RuleTriggered.NelsonTrend => "Nelson trend",
            RuleTriggered.CusumShift => "CUSUM shift",
            RuleTriggered.EwmaShift => "EWMA shift",
            RuleTriggered.MovingAverageTrend => "Moving average trend",
            RuleTriggered.LinearTrendSlope => "Linear trend / slope",
            RuleTriggered.CustomRuleTriggered => "Custom rule",
            RuleTriggered.AttributeRejected => "Attribute rejected",
            _ => rule.ToString()
        };
    }

    private static bool LooksLikeMaterialTag(string tagName)
    {
        return tagName.Contains("lot", StringComparison.OrdinalIgnoreCase) ||
            tagName.Contains("coil", StringComparison.OrdinalIgnoreCase) ||
            tagName.Contains("spool", StringComparison.OrdinalIgnoreCase) ||
            tagName.Contains("wire shipment", StringComparison.OrdinalIgnoreCase) ||
            tagName.Contains("material", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> LedgerRow(
        DateTimeOffset timestamp,
        string entryType,
        string jobNum,
        string partNum,
        string phase,
        string item,
        string value,
        string machine,
        string operation,
        string user,
        string shift,
        string status,
        string details)
    {
        return new Dictionary<string, string>
        {
            ["Date / Time"] = timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
            ["Record Type"] = entryType,
            ["Job"] = jobNum,
            ["Part"] = partNum,
            ["Phase"] = phase,
            ["Operation"] = operation,
            ["Machine"] = machine,
            ["Inspection Item / Event"] = item,
            ["Result / Value"] = value,
            ["User"] = user,
            ["Shift"] = shift,
            ["Status"] = status,
            ["Reason / Details"] = details
        };
    }
}

internal sealed record XlsxWorksheet(
    string Name,
    IReadOnlyList<string> Headers,
    IReadOnlyList<Dictionary<string, string>> Rows);

internal static class XlsxSupport
{
    public static byte[] WriteWorkbook(IReadOnlyList<XlsxWorksheet> worksheets)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "[Content_Types].xml", ContentTypesXml(worksheets.Count));
            WriteEntry(archive, "_rels/.rels", RootRelationshipsXml());
            WriteEntry(archive, "xl/workbook.xml", WorkbookXml(worksheets));
            WriteEntry(archive, "xl/_rels/workbook.xml.rels", WorkbookRelationshipsXml(worksheets.Count));
            for (var index = 0; index < worksheets.Count; index++)
            {
                WriteEntry(archive, $"xl/worksheets/sheet{index + 1}.xml", WorksheetXml(worksheets[index]));
            }
            WriteEntry(archive, "xl/styles.xml", StylesXml());
        }

        return stream.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string WorksheetXml(XlsxWorksheet worksheet)
    {
        var headers = worksheet.Headers;
        var rows = worksheet.Rows;
        var builder = new StringBuilder();
        builder.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""");
        builder.Append("""<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">""");
        builder.Append("""<sheetViews><sheetView workbookViewId="0"><pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/></sheetView></sheetViews>""");
        builder.Append(ColumnWidths(headers));
        builder.Append("<sheetData>");
        AppendRow(builder, 1, headers, isHeader: true);
        for (var index = 0; index < rows.Count; index++)
        {
            AppendRow(builder, index + 2, headers.Select(header => rows[index].TryGetValue(header, out var value) ? value : "").ToArray(), isHeader: false,
                isWatch: rows[index].TryGetValue("Status", out var status) && status == "Watch");
        }
        builder.Append("</sheetData>");
        builder.Append($"""<autoFilter ref="A1:{CellRef(headers.Count, rows.Count + 1)}"/>""");
        builder.Append("</worksheet>");
        return builder.ToString();
    }

    private static string ColumnWidths(IReadOnlyList<string> headers)
    {
        var builder = new StringBuilder("<cols>");
        for (var index = 0; index < headers.Count; index++)
        {
            var width = headers[index] switch
            {
                "Date / Time" => 20,
                "Locked At" => 20,
                "Unlocked At" => 20,
                "Edited At" => 20,
                "Inspection Item / Event" => 34,
                "Inspection Item" => 34,
                "Result / Value" => 28,
                "Reason / Details" => 45,
                "Rule / Trigger" => 30,
                "Cause Details" => 42,
                "Action Taken" => 42,
                "Bypass Reason" => 42,
                "Lock Details" => 42,
                "Operation" => 24,
                "Record Type" => 20,
                "Value" => 60,
                "Note" => 80,
                _ => Math.Clamp(headers[index].Length + 6, 12, 22)
            };
            builder.Append($"""<col min="{index + 1}" max="{index + 1}" width="{width}" customWidth="1"/>""");
        }
        builder.Append("</cols>");
        return builder.ToString();
    }

    private static void AppendRow(StringBuilder builder, int rowNumber, IEnumerable<string> values, bool isHeader, bool isWatch = false)
    {
        builder.Append($"""<row r="{rowNumber}">""");
        var column = 1;
        foreach (var value in values)
        {
            builder.Append($"""<c r="{CellRef(column, rowNumber)}" t="inlineStr"{(isHeader ? " s=\"1\"" : isWatch ? " s=\"2\"" : "")}><is><t>{Escape(value)}</t></is></c>""");
            column++;
        }
        builder.Append("</row>");
    }

    private static string CellRef(int column, int row)
    {
        var dividend = column;
        var name = "";
        while (dividend > 0)
        {
            var modulo = (dividend - 1) % 26;
            name = Convert.ToChar('A' + modulo) + name;
            dividend = (dividend - modulo) / 26;
        }

        return $"{name}{row}";
    }

    private static string Escape(string? value)
    {
        return (value ?? "")
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }

    private static string ContentTypesXml(int sheetCount)
    {
        var builder = new StringBuilder("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>""");
        for (var index = 1; index <= sheetCount; index++)
        {
            builder.Append($"""<Override PartName="/xl/worksheets/sheet{index}.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>""");
        }
        builder.Append("</Types>");
        return builder.ToString();
    }

    private static string RootRelationshipsXml() =>
        """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""";

    private static string WorkbookXml(IReadOnlyList<XlsxWorksheet> worksheets)
    {
        var builder = new StringBuilder("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets>""");
        for (var index = 0; index < worksheets.Count; index++)
        {
            builder.Append($"""<sheet name="{Escape(SafeSheetName(worksheets[index].Name))}" sheetId="{index + 1}" r:id="rId{index + 1}"/>""");
        }
        builder.Append("</sheets></workbook>");
        return builder.ToString();
    }

    private static string WorkbookRelationshipsXml(int sheetCount)
    {
        var builder = new StringBuilder("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">""");
        for (var index = 1; index <= sheetCount; index++)
        {
            builder.Append($"""<Relationship Id="rId{index}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet{index}.xml"/>""");
        }
        builder.Append($"""<Relationship Id="rId{sheetCount + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>""");
        return builder.ToString();
    }

    private static string SafeSheetName(string name)
    {
        var cleaned = new string(name.Select(character => "[]:*?/\\'".Contains(character) ? '-' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned)
            ? "Sheet"
            : cleaned[..Math.Min(cleaned.Length, 31)];
    }

    private static string StylesXml() =>
        """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts><fills count="3"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FFFFF3CD"/><bgColor indexed="64"/></patternFill></fill></fills><borders count="1"><border/></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="3"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="0" fillId="2" borderId="0" xfId="0" applyFill="1"/></cellXfs></styleSheet>""";
}
