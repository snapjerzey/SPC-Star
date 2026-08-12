using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "SPC-Star";
});
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var projectRoot = FindProjectRoot(builder.Environment.ContentRootPath);
var jsonStoragePath = Environment.GetEnvironmentVariable("SPCSTAR_DATA_PATH")
    ?? builder.Configuration["SPCStar:DataPath"]
    ?? Path.Combine(projectRoot, ".appdata", "spcstar-data.json");
var archivePath = Environment.GetEnvironmentVariable("SPCSTAR_ARCHIVE_PATH")
    ?? builder.Configuration["SPCStar:ArchivePath"]
    ?? Path.Combine(projectRoot, ".appdata", "archives");
var backupPath = Environment.GetEnvironmentVariable("SPCSTAR_BACKUP_PATH")
    ?? builder.Configuration["SPCStar:BackupPath"]
    ?? Path.Combine(projectRoot, ".appdata", "backups");
var quarantinePath = Environment.GetEnvironmentVariable("SPCSTAR_QUARANTINE_PATH")
    ?? builder.Configuration["SPCStar:QuarantinePath"]
    ?? Path.Combine(projectRoot, ".appdata", "quarantine");
var repository = CreateRepository(builder, projectRoot, jsonStoragePath);
builder.Services.AddSingleton<ISpcRepository>((ISpcRepository)repository);
builder.Services.AddSingleton<IRepositoryPersistence>(repository);
builder.Services.AddSingleton<WesternElectricRuleService>();
builder.Services.AddSingleton<PermissionService>();
builder.Services.AddSingleton<CredentialService>();
builder.Services.AddSingleton<SetupImportService>();
builder.Services.AddSingleton<SetupTemplateExportService>();
builder.Services.AddSingleton<InspectionMeasurementService>();
builder.Services.AddSingleton<AlertOverrideService>();
builder.Services.AddSingleton<QaSummaryExportService>();
builder.Services.AddSingleton<MaterialChangeLogService>();
builder.Services.AddSingleton<JobNoteService>();
builder.Services.AddSingleton<JobHistoryService>();
builder.Services.AddSingleton<JobTagService>();
builder.Services.AddSingleton<InspectionFrequencyService>();
builder.Services.AddSingleton<ChartDataService>();
builder.Services.AddSingleton<HistoryExportService>();
builder.Services.AddSingleton<HistoryIssueSummaryService>();
builder.Services.AddSingleton<SetupQueryService>();
builder.Services.AddSingleton<SetupManagementService>();
builder.Services.AddSingleton<OfflineSyncService>();
builder.Services.AddSingleton<AuthSessionService>();
builder.Services.AddSingleton<WorkContextService>();
builder.Services.AddSingleton<JobReviewService>();
builder.Services.AddSingleton(provider => new BackupService(
    provider.GetRequiredService<ISpcRepository>(),
    provider.GetRequiredService<IRepositoryPersistence>(),
    provider.GetRequiredService<CredentialService>(),
    provider.GetRequiredService<PermissionService>(),
    backupPath,
    quarantinePath));
builder.Services.AddSingleton(provider => new ArchiveService(
    provider.GetRequiredService<ISpcRepository>(),
    provider.GetRequiredService<CredentialService>(),
    provider.GetRequiredService<PermissionService>(),
    archivePath));

var app = builder.Build();

SeedData.SeedAll(app.Services.GetRequiredService<ISpcRepository>());
app.Services.GetRequiredService<IRepositoryPersistence>().SaveChanges();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new { status = "ok", app = "SPC Star" }));

app.MapPost("/admin/backups", (BackupRequest request, HttpContext httpContext, IRepositoryPersistence persistence) =>
{
    var remoteAddress = httpContext.Connection.RemoteIpAddress;
    if (remoteAddress is null || !System.Net.IPAddress.IsLoopback(remoteAddress))
    {
        return Results.NotFound();
    }

    if (string.IsNullOrWhiteSpace(request.BackupPath))
    {
        return Results.BadRequest(new { backedUp = false, errors = new[] { "Backup path is required." } });
    }

    persistence.BackupTo(request.BackupPath);
    return Results.Ok(new { backedUp = true, backupPath = request.BackupPath });
});

app.MapPost("/setup/archive/preview", (ArchivePreviewRequest request, ArchiveService service) =>
{
    return Results.Ok(service.Preview(request));
});

app.MapPost("/setup/archive", (CreateArchiveRequest request, ArchiveService service, IRepositoryPersistence persistence) =>
{
    var result = service.Create(request);
    if (result.Succeeded)
    {
        persistence.SaveChanges();
    }

    return result.Succeeded
        ? Results.Ok(result.Value)
        : Results.BadRequest(new { archived = false, errors = result.Errors });
});

app.MapGet("/setup/archive/files/{fileName}", (string fileName, ArchiveService service) =>
{
    var archivePath = service.ArchivePathFor(fileName);
    if (!System.IO.File.Exists(archivePath))
    {
        return Results.NotFound();
    }

    return Results.File(archivePath, "application/json", Path.GetFileName(archivePath));
});

app.MapPost("/setup/backups/manual", (CreateManualBackupRequest request, BackupService service) =>
{
    var result = service.CreateManualBackup(request);
    return result.Succeeded
        ? Results.Ok(result.Value)
        : Results.BadRequest(new { backedUp = false, errors = result.Errors });
});

app.MapGet("/setup/backups/files/{fileName}", (string fileName, BackupService service) =>
{
    var backupPath = service.BackupPathFor(fileName);
    if (!System.IO.File.Exists(backupPath))
    {
        return Results.NotFound();
    }

    return Results.File(backupPath, "application/octet-stream", Path.GetFileName(backupPath));
});

app.MapPost("/setup/database/clear-history", (ClearHistoryDataRequest request, BackupService service) =>
{
    var result = service.ClearHistoryData(request);
    return result.Succeeded
        ? Results.Ok(result.Value)
        : Results.BadRequest(new { cleared = false, errors = result.Errors });
});

app.MapPost("/setup/database/restore-latest", (RestoreLatestBackupRequest request, BackupService service) =>
{
    var result = service.RestoreLatestBackup(request);
    return result.Succeeded
        ? Results.Ok(result.Value)
        : Results.BadRequest(new { restored = false, errors = result.Errors });
});

app.MapPost("/auth/login", (LoginRequest request, AuthSessionService service) =>
{
    var result = service.Login(request);
    return result.Succeeded
        ? Results.Ok(result.Value)
        : Results.Unauthorized();
});

app.MapGet("/auth/me", (string userName, AuthSessionService service) =>
{
    var result = service.CurrentUser(userName);
    return result.Succeeded
        ? Results.Ok(result.Value)
        : Results.NotFound(new { errors = result.Errors });
});

app.MapPost("/auth/change-password", (ChangePasswordRequest request, AuthSessionService service, IRepositoryPersistence persistence) =>
{
    var result = service.ChangePassword(request);
    if (result.Succeeded)
    {
        persistence.SaveChanges();
    }

    return result.Succeeded
        ? Results.Ok(new { changed = true })
        : Results.BadRequest(new { changed = false, errors = result.Errors });
});

app.MapPost("/setup/import-csv", (CsvImportRequest request, SetupImportService service, IRepositoryPersistence persistence) =>
{
    var result = service.ImportCsv(request.Csv);
    if (result.Succeeded)
    {
        persistence.SaveChanges();
    }

    return result.Succeeded
        ? Results.Ok(new { imported = true })
        : Results.BadRequest(new { imported = false, errors = result.Errors });
});

app.MapGet("/setup/export-template.csv", (SetupTemplateExportService service) =>
{
    return Results.File(
        System.Text.Encoding.UTF8.GetBytes(service.ExportCsv()),
        "text/csv",
        $"spc-star-parts-inspections-export-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
});

app.MapPost("/setup/import-xlsx", async (IFormFile file, SetupImportService service, IRepositoryPersistence persistence) =>
{
    if (file.Length == 0)
    {
        return Results.BadRequest(new { imported = false, errors = new[] { "Select an Excel workbook to import." } });
    }

    try
    {
        await using var stream = file.OpenReadStream();
        var csv = XlsxImportSupport.ReadImportSheetAsCsv(stream);
        var result = service.ImportCsv(csv);
        if (result.Succeeded)
        {
            persistence.SaveChanges();
        }

        return result.Succeeded
            ? Results.Ok(new { imported = true })
            : Results.BadRequest(new { imported = false, errors = result.Errors });
    }
    catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException)
    {
        return Results.BadRequest(new { imported = false, errors = new[] { ex.Message } });
    }
}).DisableAntiforgery();

app.MapGet("/setup/users", (SetupManagementService service) =>
{
    return Results.Ok(service.GetUsers());
});

app.MapGet("/setup/users/export.csv", (SetupManagementService service) =>
{
    return Results.File(
        System.Text.Encoding.UTF8.GetBytes(service.ExportUsersCsv()),
        "text/csv",
        $"spc-star-users-permissions-export-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
});

app.MapGet("/setup/resources", (SetupManagementService service) =>
{
    return Results.Ok(service.GetResources());
});

app.MapGet("/setup/resources/export.csv", (SetupManagementService service) =>
{
    return Results.File(
        System.Text.Encoding.UTF8.GetBytes(service.ExportResourcesCsv()),
        "text/csv",
        $"spc-star-machines-export-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
});

app.MapGet("/setup/roles", (SetupManagementService service) =>
{
    return Results.Ok(service.GetRoles());
});

app.MapGet("/setup/settings", (SetupManagementService service) =>
{
    return Results.Ok(service.GetSettings());
});

app.MapPost("/setup/settings", (UpdateSettingsRequest request, SetupManagementService service, IRepositoryPersistence persistence) =>
{
    var result = service.UpdateSettings(request);
    if (result.Succeeded)
    {
        persistence.SaveChanges();
    }

    return result.Succeeded
        ? Results.Ok(result.Value)
        : Results.BadRequest(new { errors = result.Errors });
});

app.MapPost("/setup/users", (UpsertUserRequest request, SetupManagementService service, IRepositoryPersistence persistence) =>
{
    var result = service.UpsertUser(request);
    if (result.Succeeded)
    {
        persistence.SaveChanges();
    }

    return result.Succeeded
        ? Results.Ok(result.Value)
        : Results.BadRequest(new { errors = result.Errors });
});

app.MapPost("/setup/users/reset-password", (ResetUserPasswordRequest request, SetupManagementService service, IRepositoryPersistence persistence) =>
{
    var result = service.ResetUserPassword(request);
    if (result.Succeeded)
    {
        persistence.SaveChanges();
    }

    return result.Succeeded
        ? Results.Ok(result.Value)
        : Results.BadRequest(new { errors = result.Errors });
});

app.MapPost("/setup/resources", (UpsertResourceMachineRequest request, SetupManagementService service, IRepositoryPersistence persistence) =>
{
    var result = service.UpsertResource(request);
    if (result.Succeeded)
    {
        persistence.SaveChanges();
    }

    return result.Succeeded
        ? Results.Ok(result.Value)
        : Results.BadRequest(new { errors = result.Errors });
});

app.MapPost("/setup/resources/import-xlsx", async (IFormFile file, SetupManagementService service, IRepositoryPersistence persistence) =>
{
    if (file.Length == 0)
    {
        return Results.BadRequest(new { imported = false, errors = new[] { "Select a machine workbook to import." } });
    }

    try
    {
        await using var stream = file.OpenReadStream();
        var csv = XlsxImportSupport.ReadImportSheetAsCsv(stream, "SPC-Star Machine Import");
        var result = service.ImportResourcesCsv(csv);
        if (result.Succeeded)
        {
            persistence.SaveChanges();
        }

        return result.Succeeded
            ? Results.Ok(new { imported = true, count = result.Value!.Imported })
            : Results.BadRequest(new { imported = false, errors = result.Errors });
    }
    catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException)
    {
        return Results.BadRequest(new { imported = false, errors = new[] { ex.Message } });
    }
}).DisableAntiforgery();

app.MapPost("/setup/users/import-xlsx", async (IFormFile file, SetupManagementService service, IRepositoryPersistence persistence) =>
{
    if (file.Length == 0)
    {
        return Results.BadRequest(new { imported = false, errors = new[] { "Select an Excel workbook to import." } });
    }

    try
    {
        await using var stream = file.OpenReadStream();
        var csv = XlsxImportSupport.ReadImportSheetAsCsv(stream, "SPC-Star User Import");
        var result = service.ImportUsersCsv(csv);
        if (result.Succeeded)
        {
            persistence.SaveChanges();
        }

        return result.Succeeded
            ? Results.Ok(new { imported = true, count = result.Value!.Imported })
            : Results.BadRequest(new { imported = false, errors = result.Errors });
    }
    catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException)
    {
        return Results.BadRequest(new { imported = false, errors = new[] { ex.Message } });
    }
}).DisableAntiforgery();

app.MapDelete("/setup/users/{userName}", (string userName, SetupManagementService service, IRepositoryPersistence persistence) =>
{
    var result = service.DeleteUser(userName);
    if (result.Succeeded)
    {
        persistence.SaveChanges();
    }

    return result.Succeeded
        ? Results.NoContent()
        : Results.BadRequest(new { errors = result.Errors });
});

app.MapDelete("/setup/resources/{resourceId}", (string resourceId, SetupManagementService service, IRepositoryPersistence persistence) =>
{
    var result = service.DeleteResource(resourceId);
    if (result.Succeeded)
    {
        persistence.SaveChanges();
    }

    return result.Succeeded
        ? Results.NoContent()
        : Results.BadRequest(new { errors = result.Errors });
});

app.MapPost("/setup/inspection-plans", (UpsertInspectionSetupRequest request, SetupManagementService service, IRepositoryPersistence persistence) =>
{
    var result = service.UpsertInspectionSetup(request);
    if (result.Succeeded)
    {
        persistence.SaveChanges();
    }

    return result.Succeeded
        ? Results.Ok(result.Value)
        : Results.BadRequest(new { errors = result.Errors });
});

app.MapPost("/setup/inspection-plans/delete-phase", (DeleteInspectionSetupPhaseRequest request, SetupManagementService service, IRepositoryPersistence persistence) =>
{
    var result = service.DeleteInspectionSetupPhase(request);
    if (result.Succeeded)
    {
        persistence.SaveChanges();
    }

    return result.Succeeded
        ? Results.NoContent()
        : Results.BadRequest(new { errors = result.Errors });
});

app.MapGet("/setup/parts", (SetupQueryService service) =>
{
    return Results.Ok(service.GetParts());
});

app.MapGet("/setup/inspection-plans", (string? partNum, SetupQueryService service) =>
{
    return Results.Ok(service.GetInspectionPlans(partNum));
});

app.MapGet("/sync/setup-snapshot", (SetupQueryService service) =>
{
    return Results.Ok(service.GetSetupSnapshot());
});

app.MapPost("/inspections/measurements", (InspectionMeasurementEntry request, InspectionMeasurementService service, IRepositoryPersistence persistence) =>
{
    var result = service.EnterMeasurement(request);
    if (result.Succeeded)
    {
        persistence.SaveChanges();
    }

    return result.Succeeded
        ? Results.Created($"/inspections/measurements/{result.Value!.Id}", result.Value)
        : Results.BadRequest(new { errors = result.Errors });
});

app.MapPost("/material-changes", (MaterialChangeLogEntry request, MaterialChangeLogService service, IRepositoryPersistence persistence) =>
{
    var result = service.Record(request);
    if (result.Succeeded)
    {
        persistence.SaveChanges();
    }

    return result.Succeeded
        ? Results.Created($"/material-changes/{result.Value!.Id}", result.Value)
        : Results.BadRequest(new { errors = result.Errors });
});

app.MapGet("/jobs/{jobNum}/notes", (string jobNum, JobNoteService service) =>
{
    return Results.Ok(service.GetForJob(jobNum));
});

app.MapGet("/jobs/{jobNum}/history", (string jobNum, JobHistoryService service) =>
{
    return Results.Ok(service.GetForJob(jobNum));
});

app.MapGet("/jobs/{jobNum}/tags", (string jobNum, JobTagService service) =>
{
    return Results.Ok(service.GetForJob(jobNum));
});

app.MapPost("/jobs/{jobNum}/tags", (string jobNum, SaveJobTagsRequest request, JobTagService service, IRepositoryPersistence persistence) =>
{
    var result = service.Save(request with { JobNum = jobNum });
    if (result.Succeeded)
    {
        persistence.SaveChanges();
    }

    return result.Succeeded
        ? Results.Ok(result.Value)
        : Results.BadRequest(new { errors = result.Errors });
});

app.MapPost("/setup/job-data-fields", (UpsertPartJobDataFieldRequest request, SetupManagementService service, IRepositoryPersistence persistence) =>
{
    var result = service.UpsertPartJobDataField(request);
    if (result.Succeeded)
    {
        persistence.SaveChanges();
    }

    return result.Succeeded
        ? Results.Ok(result.Value)
        : Results.BadRequest(new { errors = result.Errors });
});

app.MapPost("/setup/material-fields", (UpsertPartMaterialFieldRequest request, SetupManagementService service, IRepositoryPersistence persistence) =>
{
    var result = service.UpsertPartMaterialField(request);
    if (result.Succeeded)
    {
        persistence.SaveChanges();
    }

    return result.Succeeded
        ? Results.Ok(result.Value)
        : Results.BadRequest(new { errors = result.Errors });
});

app.MapPost("/jobs/{jobNum}/notes", (string jobNum, JobNoteEntry request, JobNoteService service, IRepositoryPersistence persistence) =>
{
    var result = service.Add(request with { JobNum = jobNum });
    if (result.Succeeded)
    {
        persistence.SaveChanges();
    }

    return result.Succeeded
        ? Results.Created($"/jobs/{jobNum}/notes/{result.Value!.Id}", result.Value)
        : Results.BadRequest(new { errors = result.Errors });
});

app.MapPost("/alerts/{alertId:guid}/override", (
    Guid alertId,
    AlertOverrideApiRequest request,
    AlertOverrideService service,
    IRepositoryPersistence persistence) =>
{
    var result = service.Override(new AlertOverrideRequest(
        alertId,
        request.OverrideUserName,
        request.OverridePassword,
        request.CauseText,
        request.SolutionText,
        request.WhyStandardProcessWasBypassed,
        request.UnlockedAt ?? DateTimeOffset.UtcNow,
        CauseCategory: request.CauseCategory));
    if (result.Succeeded)
    {
        persistence.SaveChanges();
    }

    return result.Succeeded
        ? Results.Ok(result.Value)
        : Results.BadRequest(new { errors = result.Errors });
});

app.MapPost("/inspection-frequency/evaluate", (InspectionFrequencyCheckRequest request, InspectionFrequencyService service) =>
{
    return Results.Ok(service.Evaluate(request));
});

app.MapGet("/work-context", (
    string jobNum,
    string partNum,
    string processCode,
    int operationSeq,
    string resourceId,
    string characteristicName,
    string? inspectionPhase,
    WorkContextService service) =>
{
    return Results.Ok(service.Build(new WorkContextRequest(
        jobNum,
        partNum,
        processCode,
        operationSeq,
        resourceId,
        characteristicName,
        DateTimeOffset.UtcNow,
        inspectionPhase ?? "In Process")));
});

app.MapPost("/charts/data", (ChartDataRequest request, ChartDataService service) =>
{
    return Results.Ok(service.Build(request));
});

app.MapPost("/qa/summary", (QaSummaryExportRequest request, QaSummaryExportService service) =>
{
    var result = service.BuildSummary(request);
    return result.Succeeded
        ? Results.Ok(result.Value)
        : Results.BadRequest(new { errors = result.Errors });
});

app.MapPost("/qa/summary.csv", (QaSummaryExportRequest request, QaSummaryExportService service) =>
{
    var result = service.ExportCsv(request);
    return result.Succeeded
        ? Results.Text(result.Value!, "text/csv")
        : Results.BadRequest(new { errors = result.Errors });
});

app.MapGet("/qa/jobs/{jobNum}/variable-means", (string jobNum, QaSummaryExportService service) =>
{
    var result = service.BuildJobVariableMeans(jobNum);
    return result.Succeeded
        ? Results.Ok(result.Value)
        : Results.BadRequest(new { errors = result.Errors });
});

app.MapGet("/qa/jobs/{jobNum}/variable-means.csv", (string jobNum, QaSummaryExportService service) =>
{
    var result = service.ExportJobVariableMeansCsv(jobNum);
    return result.Succeeded
        ? Results.Text(result.Value!, "text/csv")
        : Results.BadRequest(new { errors = result.Errors });
});

app.MapGet("/qa/job-variable-means", (string jobNums, QaSummaryExportService service) =>
{
    var result = service.BuildJobVariableMeans(SplitCsv(jobNums));
    return result.Succeeded
        ? Results.Ok(result.Value)
        : Results.BadRequest(new { errors = result.Errors });
});

app.MapGet("/qa/job-variable-means.csv", (string jobNums, QaSummaryExportService service) =>
{
    var result = service.ExportJobVariableMeansCsv(SplitCsv(jobNums));
    return result.Succeeded
        ? Results.Text(result.Value!, "text/csv")
        : Results.BadRequest(new { errors = result.Errors });
});

app.MapGet("/review/job", (string partNum, string jobNum, JobReviewService service) =>
{
    var result = service.Build(partNum, jobNum);
    return result.Succeeded
        ? Results.Ok(result.Value)
        : Results.BadRequest(new { errors = result.Errors });
});

app.MapGet("/review/part", (string partNum, QaSummaryExportService service) =>
{
    var result = service.BuildPartCapability(partNum);
    return result.Succeeded
        ? Results.Ok(result.Value)
        : Results.BadRequest(new { errors = result.Errors });
});

app.MapPatch("/review/measurements/{measurementId:guid}", (
    Guid measurementId,
    UpdateInspectionMeasurementRequest request,
    JobReviewService service,
    IRepositoryPersistence persistence) =>
{
    var result = service.UpdateMeasurement(measurementId, request);
    if (result.Succeeded)
    {
        persistence.SaveChanges();
    }

    return result.Succeeded
        ? Results.Ok(result.Value)
        : Results.BadRequest(new { errors = result.Errors });
});

app.MapPost("/exports/inspection-data.csv", (InspectionHistoryExportRequest request, HistoryExportService service) =>
{
    return Results.Text(service.ExportInspectionCsv(request), "text/csv");
});

app.MapGet("/exports/jobs/{jobNum}/inspection-history.csv", (string jobNum, HistoryExportService service) =>
{
    return Results.Text(service.ExportJobInspectionHistoryCsv(jobNum), "text/csv");
});

app.MapPost("/exports/drift-alerts.csv", (AlertHistoryExportRequest request, HistoryExportService service) =>
{
    return Results.Text(service.ExportAlertHistoryCsv(request), "text/csv");
});

app.MapPost("/exports/material-changes.csv", (MaterialHistoryExportRequest request, HistoryExportService service) =>
{
    return Results.Text(service.ExportMaterialChangeHistoryCsv(request), "text/csv");
});

app.MapPost("/history/top-issues", (HistoryIssueSummaryRequest request, HistoryIssueSummaryService service) =>
{
    return Results.Ok(service.TopIssues(request));
});

app.MapPost("/sync/offline-changes", (OfflineSyncRequest request, OfflineSyncService service, IRepositoryPersistence persistence) =>
{
    var result = service.Sync(request);
    if (result.Accepted.Count > 0)
    {
        persistence.SaveChanges();
    }

    return result.HasErrors
        ? Results.BadRequest(result)
        : Results.Ok(result);
});

app.MapGet("/alerts/active", (ISpcRepository repository) =>
{
    return Results.Ok(repository.Alerts.Where(alert => alert.Status == AlertStatus.Active));
});

static string[] SplitCsv(string value)
{
    return value
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

static string FindProjectRoot(string startPath)
{
    var directory = new DirectoryInfo(startPath);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "SPCStar.sln")) ||
            Directory.Exists(Path.Combine(directory.FullName, ".git")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    return Directory.GetCurrentDirectory();
}

static IRepositoryPersistence CreateRepository(WebApplicationBuilder builder, string projectRoot, string jsonStoragePath)
{
    var provider = Environment.GetEnvironmentVariable("SPCSTAR_STORAGE_PROVIDER")
        ?? builder.Configuration["SPCStar:StorageProvider"]
        ?? "sqlite";
    if (provider.Equals("json", StringComparison.OrdinalIgnoreCase))
    {
        return new FileBackedSpcRepository(jsonStoragePath);
    }

    var sqlitePath = Environment.GetEnvironmentVariable("SPCSTAR_DATABASE_PATH")
        ?? builder.Configuration["SPCStar:DatabasePath"]
        ?? Path.Combine(projectRoot, ".appdata", "spcstar.db");
    var sqliteRepository = new SqliteBackedSpcRepository(sqlitePath);
    if (((ISpcRepository)sqliteRepository).Roles.Count == 0 && File.Exists(jsonStoragePath))
    {
        var jsonRepository = new FileBackedSpcRepository(jsonStoragePath);
        sqliteRepository.ImportFrom(jsonRepository);
        sqliteRepository.SaveChanges();
    }

    return sqliteRepository;
}

app.Run();

public sealed record CsvImportRequest(string Csv);

public sealed record BackupRequest(string BackupPath);

public sealed record AlertOverrideApiRequest(
    string OverrideUserName,
    string OverridePassword,
    string? CauseCategory,
    string CauseText,
    string SolutionText,
    string? WhyStandardProcessWasBypassed,
    DateTimeOffset? UnlockedAt);
