using System.Text.Json;
using System.Text.Json.Serialization;
using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;

namespace SPCStar.Core.Services;

public sealed record ArchivePreviewRequest(DateTimeOffset CutoffDate);

public sealed record CreateArchiveRequest(
    DateTimeOffset CutoffDate,
    string ArchiveUserName,
    string ArchivePassword,
    string ConfirmationText);

public sealed record ArchiveCounts(
    int Measurements,
    int MeasurementEditAudits,
    int JobNotes,
    int JobPhaseCompletions,
    int JobTags,
    int Alerts,
    int RuleViolations,
    int AlertOverrides,
    int MaterialChanges);

public sealed record ArchivePreviewDto(DateTimeOffset CutoffDate, ArchiveCounts Counts, int ActiveLocksBeforeCutoff);

public sealed record ArchiveResultDto(
    DateTimeOffset CutoffDate,
    DateTimeOffset ArchivedAt,
    string ArchiveFileName,
    string ArchivePath,
    string DownloadPath,
    ArchiveCounts Counts);

public sealed class ArchiveService(
    ISpcRepository repository,
    CredentialService credentialService,
    PermissionService permissionService,
    string archiveDirectory)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public ArchivePreviewDto Preview(ArchivePreviewRequest request)
    {
        var cutoff = NormalizeCutoff(request.CutoffDate);
        return new ArchivePreviewDto(cutoff, BuildCounts(cutoff), ActiveLocksBefore(cutoff));
    }

    public ServiceResult<ArchiveResultDto> Create(CreateArchiveRequest request)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return ServiceResult<ArchiveResultDto>.Fail(errors);
        }

        var cutoff = NormalizeCutoff(request.CutoffDate);
        var activeLocks = ActiveLocksBefore(cutoff);
        if (activeLocks > 0)
        {
            return ServiceResult<ArchiveResultDto>.Fail($"Archive blocked. {activeLocks} active lock(s) exist before the cutoff date.");
        }

        var package = BuildPackage(cutoff, request.ArchiveUserName.Trim());
        if (package.Counts == new ArchiveCounts(0, 0, 0, 0, 0, 0, 0, 0, 0))
        {
            return ServiceResult<ArchiveResultDto>.Fail("No archiveable records were found before the cutoff date.");
        }

        Directory.CreateDirectory(archiveDirectory);
        var archivedAt = DateTimeOffset.UtcNow;
        var fileName = $"spc-star-archive-before-{cutoff:yyyyMMdd}-created-{archivedAt:yyyyMMdd-HHmmss}.json";
        var archivePath = Path.Combine(archiveDirectory, fileName);
        File.WriteAllText(archivePath, JsonSerializer.Serialize(package with { ArchivedAt = archivedAt }, JsonOptions));

        RemoveArchivedRecords(package);

        return ServiceResult<ArchiveResultDto>.Ok(new ArchiveResultDto(
            cutoff,
            archivedAt,
            fileName,
            archivePath,
            $"/setup/archive/files/{Uri.EscapeDataString(fileName)}",
            package.Counts));
    }

    public string ArchivePathFor(string fileName)
    {
        var safeName = Path.GetFileName(fileName);
        return Path.Combine(archiveDirectory, safeName);
    }

    private List<string> Validate(CreateArchiveRequest request)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(request.ArchiveUserName))
        {
            errors.Add("Archive username is required.");
        }

        if (string.IsNullOrWhiteSpace(request.ArchivePassword))
        {
            errors.Add("Archive password is required.");
        }

        if (!request.ConfirmationText.Equals("ARCHIVE", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Type ARCHIVE to confirm this action.");
        }

        if (request.CutoffDate >= DateTimeOffset.UtcNow)
        {
            errors.Add("Cutoff date must be in the past.");
        }

        if (errors.Count == 0 &&
            (!credentialService.ValidateCredential(request.ArchiveUserName.Trim(), request.ArchivePassword) ||
             !permissionService.UserHasPermission(request.ArchiveUserName.Trim(), PermissionNames.CanUseGodMode)))
        {
            errors.Add("Archive requires valid GOD credentials.");
        }

        return errors;
    }

    private ArchiveCounts BuildCounts(DateTimeOffset cutoff)
    {
        var measurementIds = repository.Measurements
            .Where(item => item.Timestamp < cutoff)
            .Select(item => item.Id)
            .ToHashSet();
        var alertIds = repository.Alerts
            .Where(item => item.LockedAt < cutoff)
            .Select(item => item.Id)
            .ToHashSet();

        return new ArchiveCounts(
            repository.Measurements.Count(item => item.Timestamp < cutoff),
            repository.MeasurementEditAudits.Count(item => item.EditedAt < cutoff || measurementIds.Contains(item.MeasurementId)),
            repository.JobNotes.Count(item => item.Timestamp < cutoff),
            repository.JobPhaseCompletions.Count(item => item.CompletedAt < cutoff),
            repository.JobTags.Count(item => item.UpdatedAt < cutoff),
            repository.Alerts.Count(item => item.LockedAt < cutoff),
            repository.RuleViolations.Count(item => item.DetectedAt < cutoff || alertIds.Contains(item.AlertId)),
            repository.AlertOverrides.Count(item => item.UnlockedAt < cutoff || alertIds.Contains(item.AlertId)),
            repository.MaterialChanges.Count(item => item.Timestamp < cutoff));
    }

    private ArchivePackage BuildPackage(DateTimeOffset cutoff, string archivedByUserId)
    {
        var measurementIds = repository.Measurements
            .Where(item => item.Timestamp < cutoff)
            .Select(item => item.Id)
            .ToHashSet();
        var alertIds = repository.Alerts
            .Where(item => item.LockedAt < cutoff)
            .Select(item => item.Id)
            .ToHashSet();

        var measurements = repository.Measurements.Where(item => item.Timestamp < cutoff).ToArray();
        var editAudits = repository.MeasurementEditAudits.Where(item => item.EditedAt < cutoff || measurementIds.Contains(item.MeasurementId)).ToArray();
        var notes = repository.JobNotes.Where(item => item.Timestamp < cutoff).ToArray();
        var completions = repository.JobPhaseCompletions.Where(item => item.CompletedAt < cutoff).ToArray();
        var tags = repository.JobTags.Where(item => item.UpdatedAt < cutoff).ToArray();
        var alerts = repository.Alerts.Where(item => item.LockedAt < cutoff).ToArray();
        var violations = repository.RuleViolations.Where(item => item.DetectedAt < cutoff || alertIds.Contains(item.AlertId)).ToArray();
        var overrides = repository.AlertOverrides.Where(item => item.UnlockedAt < cutoff || alertIds.Contains(item.AlertId)).ToArray();
        var materialChanges = repository.MaterialChanges.Where(item => item.Timestamp < cutoff).ToArray();

        return new ArchivePackage(
            ArchiveSchemaVersion: 1,
            CutoffDate: cutoff,
            ArchivedAt: DateTimeOffset.MinValue,
            ArchivedByUserId: archivedByUserId,
            Counts: new ArchiveCounts(
                measurements.Length,
                editAudits.Length,
                notes.Length,
                completions.Length,
                tags.Length,
                alerts.Length,
                violations.Length,
                overrides.Length,
                materialChanges.Length),
            Measurements: measurements,
            MeasurementEditAudits: editAudits,
            JobNotes: notes,
            JobPhaseCompletions: completions,
            JobTags: tags,
            Alerts: alerts,
            RuleViolations: violations,
            AlertOverrides: overrides,
            MaterialChanges: materialChanges);
    }

    private void RemoveArchivedRecords(ArchivePackage package)
    {
        var measurementIds = package.Measurements.Select(item => item.Id).ToHashSet();
        var editIds = package.MeasurementEditAudits.Select(item => item.Id).ToHashSet();
        var noteIds = package.JobNotes.Select(item => item.Id).ToHashSet();
        var completionIds = package.JobPhaseCompletions.Select(item => item.Id).ToHashSet();
        var tagIds = package.JobTags.Select(item => item.Id).ToHashSet();
        var alertIds = package.Alerts.Select(item => item.Id).ToHashSet();
        var violationIds = package.RuleViolations.Select(item => item.Id).ToHashSet();
        var overrideIds = package.AlertOverrides.Select(item => item.Id).ToHashSet();
        var materialIds = package.MaterialChanges.Select(item => item.Id).ToHashSet();

        repository.Measurements.RemoveAll(item => measurementIds.Contains(item.Id));
        repository.MeasurementEditAudits.RemoveAll(item => editIds.Contains(item.Id));
        repository.JobNotes.RemoveAll(item => noteIds.Contains(item.Id));
        repository.JobPhaseCompletions.RemoveAll(item => completionIds.Contains(item.Id));
        repository.JobTags.RemoveAll(item => tagIds.Contains(item.Id));
        repository.Alerts.RemoveAll(item => alertIds.Contains(item.Id));
        repository.RuleViolations.RemoveAll(item => violationIds.Contains(item.Id));
        repository.AlertOverrides.RemoveAll(item => overrideIds.Contains(item.Id));
        repository.MaterialChanges.RemoveAll(item => materialIds.Contains(item.Id));
    }

    private int ActiveLocksBefore(DateTimeOffset cutoff)
    {
        return repository.Alerts.Count(item => item.LockedAt < cutoff && item.Status == AlertStatus.Active);
    }

    private static DateTimeOffset NormalizeCutoff(DateTimeOffset cutoff)
    {
        return cutoff.ToUniversalTime();
    }
}

public sealed record ArchivePackage(
    int ArchiveSchemaVersion,
    DateTimeOffset CutoffDate,
    DateTimeOffset ArchivedAt,
    string ArchivedByUserId,
    ArchiveCounts Counts,
    IReadOnlyList<InspectionMeasurement> Measurements,
    IReadOnlyList<MeasurementEditAudit> MeasurementEditAudits,
    IReadOnlyList<JobNote> JobNotes,
    IReadOnlyList<JobPhaseCompletion> JobPhaseCompletions,
    IReadOnlyList<JobTag> JobTags,
    IReadOnlyList<ProcessAlert> Alerts,
    IReadOnlyList<RuleViolation> RuleViolations,
    IReadOnlyList<AlertOverride> AlertOverrides,
    IReadOnlyList<MaterialChangeLog> MaterialChanges);
