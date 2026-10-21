using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
var maintenanceWorkbook = builder.Configuration["maintenance-import-workbook"];
if (maintenanceWorkbook is not null)
{
    if (!OperatingSystem.IsWindows() ||
        !new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent())
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
    {
        Console.Error.WriteLine("Local setup maintenance requires Windows administrator access.");
        Environment.ExitCode = 1;
        return;
    }
    var maintenanceDatabase = builder.Configuration["SPCStar:DatabasePath"];
    if (string.IsNullOrWhiteSpace(maintenanceDatabase) || !File.Exists(maintenanceDatabase))
    {
        Console.Error.WriteLine("The existing SPC-Star database must be specified for local setup maintenance.");
        Environment.ExitCode = 1;
        return;
    }
    try
    {
        var maintenanceRepository = new SqliteBackedSpcRepository(maintenanceDatabase);
        var result = new SetupWorkbookMaintenanceService(maintenanceRepository, maintenanceRepository)
            .Apply(maintenanceWorkbook, Environment.UserName);
        if (!result.Succeeded)
        {
            Console.Error.WriteLine(string.Join(Environment.NewLine, result.Errors));
            Environment.ExitCode = 1;
        }
        else Console.WriteLine("SPC-Star setup workbook updated.");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Local setup update failed: {ex.Message}");
        Environment.ExitCode = 1;
    }
    return;
}
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
builder.Services.AddSingleton<EthiconCoaPreviewService>();
builder.Services.AddSingleton<WeldPinCertService>();
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
builder.Services.AddSingleton<SetupWriteEndpoint>();
builder.Services.AddSingleton<ChangeHistoryService>();
builder.Services.AddSingleton<OfflineSyncService>();
builder.Services.AddSingleton<IAuthSessionStore>(new SqliteAuthSessionStore(Path.ChangeExtension(repository.StoragePath, ".sessions.db")));
builder.Services.AddSingleton<AuthSessionService>();
builder.Services.AddSingleton<WorkContextService>();
builder.Services.AddSingleton<JobReviewService>();
builder.Services.AddSingleton<PartQualityService>();
builder.Services.AddSingleton(provider => new BackupService(
    provider.GetRequiredService<ISpcRepository>(),
    provider.GetRequiredService<IRepositoryPersistence>(),
    provider.GetRequiredService<CredentialService>(),
    provider.GetRequiredService<PermissionService>(),
    backupPath,
    quarantinePath,
    provider.GetRequiredService<ChangeHistoryService>()));
builder.Services.AddSingleton(provider => new ArchiveService(
    provider.GetRequiredService<ISpcRepository>(),
    provider.GetRequiredService<CredentialService>(),
    provider.GetRequiredService<PermissionService>(),
    archivePath));

var app = builder.Build();

SeedData.SeedAll(app.Services.GetRequiredService<ISpcRepository>());
app.Services.GetRequiredService<IRepositoryPersistence>().SaveChanges();

app.UseDefaultFiles();
app.UseMiddleware<WriteAccessMiddleware>();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        if (context.File.Name is "index.html" or "app.js" or "styles.css")
        {
            context.Context.Response.Headers.CacheControl = "no-store";
        }
    }
});

app.MapGet("/health", (HttpContext context) =>
{
    context.Response.Headers.CacheControl = "no-store";
    return Results.Ok(new { status = "ok", app = "SPC Star", version = ApplicationVersion.Current });
});
app.MapChangeHistory();
app.MapJobNotesReview();

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

app.MapPost("/setup/archive", (CreateArchiveRequest request, ArchiveService service, HttpContext context, SetupWriteEndpoint audit) =>
{
    return audit.Observe(request.ArchiveUserName, "Archive", () =>
    {
        var result = service.Create(request);
        return result.Succeeded
            ? Results.Ok(result.Value)
            : Results.BadRequest(new { archived = false, errors = result.Errors });
    }, actionOnly: true);
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

app.MapGet("/auth/me", (string userName, HttpContext context, AuthSessionService service) =>
{
    context.Response.Headers.CacheControl = "no-store";
    var result = service.CurrentUser(userName, context.Request.Headers["X-SPCStar-Session"].ToString());
    return result.Succeeded
        ? Results.Ok(result.Value)
        : Results.Unauthorized();
});

app.MapPost("/auth/logout", (HttpContext context, AuthSessionService service) =>
{
    service.Logout(context.Request.Headers["X-SPCStar-User"].ToString(),
        context.Request.Headers["X-SPCStar-Session"].ToString());
    return Results.NoContent();
});

app.MapPost("/auth/change-password", (ChangePasswordRequest request, AuthSessionService service, HttpContext context, SetupWriteEndpoint audit) =>
{
    return audit.Observe(request.UserName, "Users", () =>
    {
        var result = service.ChangePassword(request);
        return result.Succeeded
            ? Results.Ok(new { changed = true })
            : Results.BadRequest(new { changed = false, errors = result.Errors });
    });
});

app.MapPost("/setup/import-csv", (CsvImportRequest request, SetupImportService service, HttpContext context, SetupWriteEndpoint audit) =>
{
    return audit.Run(context, "Parts & Inspections CSV imported", () =>
    {
        var result = service.ImportCsv(request.Csv);
        return result.Succeeded ? Results.Ok(new { imported = true })
            : Results.BadRequest(new { imported = false, errors = result.Errors });
    }, isImport: true);
});

app.MapGet("/setup/export-template.csv", (SetupTemplateExportService service) =>
{
    return Results.File(
        System.Text.Encoding.UTF8.GetBytes(service.ExportCsv()),
        "text/csv",
        $"spc-star-parts-inspections-export-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
});

app.MapPost("/setup/import-xlsx", async (IFormFile file, bool? replaceSetup, SetupImportService service, HttpContext context, SetupWriteEndpoint audit) =>
{
    var actor = audit.ResolveActor(context, isImport: true);
    if (!actor.Succeeded) return Results.Json(new { errors = actor.Errors }, statusCode: 403);
    if (file.Length == 0)
    {
        return Results.BadRequest(new { imported = false, errors = new[] { "Select an Excel workbook to import." } });
    }

    try
    {
        await using var stream = file.OpenReadStream();
        var csv = XlsxImportSupport.ReadImportSheetAsCsv(stream);
        return audit.Run(context, $"Parts & Inspections workbook imported: {Path.GetFileName(file.FileName)}", () =>
        {
            var result = service.ImportCsv(csv, replaceSetup == true);
            return result.Succeeded ? Results.Ok(new { imported = true })
                : Results.BadRequest(new { imported = false, errors = result.Errors });
        }, isImport: true);
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

app.MapGet("/setup/resources/export.xlsx", (SetupManagementService service) =>
{
    return Results.File(
        service.ExportResourcesXlsx(),
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        $"spc-star-machines-import-{DateTime.UtcNow:yyyyMMdd-HHmm}.xlsx");
});

app.MapGet("/setup/roles", (SetupManagementService service) =>
{
    return Results.Ok(service.GetRoles());
});

app.MapGet("/setup/settings", (SetupManagementService service) =>
{
    return Results.Ok(service.GetSettings());
});

app.MapPost("/setup/settings", (UpdateSettingsRequest request, SetupManagementService service, HttpContext context, SetupWriteEndpoint audit) =>
{
    return audit.Observe(audit.RequestActor(context), "Rules", () =>
    {
        var result = service.UpdateSettings(request);
        return result.Succeeded
            ? Results.Ok(result.Value)
            : Results.BadRequest(new { errors = result.Errors });
    });
});

app.MapPost("/setup/users", (UpsertUserRequest request, AuthSessionService authService, SetupManagementService service, HttpContext context, SetupWriteEndpoint audit) =>
{
    var sessionError = RejectInvalidUserManagementSession(request.ActingUserName, request.ActingSessionToken, authService);
    if (sessionError is not null)
    {
        return sessionError;
    }

    return audit.Observe(request.ActingUserName!, "Users", () =>
    {
        var result = service.UpsertUser(request);
        return result.Succeeded
            ? Results.Ok(result.Value)
            : Results.BadRequest(new { errors = result.Errors });
    });
});

app.MapPost("/setup/users/reset-password", (ResetUserPasswordRequest request, AuthSessionService authService, SetupManagementService service, HttpContext context, SetupWriteEndpoint audit) =>
{
    var sessionError = RejectInvalidUserManagementSession(request.ActingUserName, request.ActingSessionToken, authService);
    if (sessionError is not null)
    {
        return sessionError;
    }

    return audit.Observe(request.ActingUserName!, "Users", () =>
    {
        var result = service.ResetUserPassword(request);
        return result.Succeeded
            ? Results.Ok(result.Value)
            : Results.BadRequest(new { errors = result.Errors });
    });
});

app.MapPost("/setup/resources", (UpsertResourceMachineRequest request, SetupManagementService service, HttpContext context, SetupWriteEndpoint audit) =>
{
    return audit.Observe(audit.RequestActor(context), "Machines", () =>
    {
        var result = service.UpsertResource(request);
        return result.Succeeded
            ? Results.Ok(result.Value)
            : Results.BadRequest(new { errors = result.Errors });
    });
});

app.MapPost("/setup/resources/import-xlsx", async (IFormFile file, SetupManagementService service, HttpContext context, SetupWriteEndpoint audit) =>
{
    var actor = audit.ResolveActor(context, isImport: true);
    if (!actor.Succeeded) return Results.Json(new { errors = actor.Errors }, statusCode: 403);
    if (file.Length == 0)
    {
        return Results.BadRequest(new { imported = false, errors = new[] { "Select a machine workbook to import." } });
    }

    try
    {
        await using var stream = file.OpenReadStream();
        var csv = XlsxImportSupport.ReadImportSheetAsCsv(stream, "SPC-Star Machine Import");
        return audit.Run(context, $"Machine workbook imported: {Path.GetFileName(file.FileName)}", () =>
        {
            var result = service.ImportResourcesCsv(csv);
            return result.Succeeded
                ? Results.Ok(new { imported = true, count = result.Value!.Imported })
                : Results.BadRequest(new { imported = false, errors = result.Errors });
        }, isImport: true);
    }
    catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException)
    {
        return Results.BadRequest(new { imported = false, errors = new[] { ex.Message } });
    }
}).DisableAntiforgery();

app.MapPost("/setup/users/import-xlsx", async (IFormFile file, HttpContext context, SetupWriteEndpoint audit, SetupManagementService service) =>
{
    var actor = audit.ResolveActor(context, isImport: true);
    if (!actor.Succeeded) return Results.Json(new { errors = actor.Errors }, statusCode: 403);

    if (file.Length == 0)
    {
        return Results.BadRequest(new { imported = false, errors = new[] { "Select an Excel workbook to import." } });
    }

    try
    {
        await using var stream = file.OpenReadStream();
        var csv = XlsxImportSupport.ReadImportSheetAsCsv(stream, "SPC-Star User Import");
        return audit.Run(context, $"User workbook imported: {Path.GetFileName(file.FileName)}", () =>
        {
            var result = service.ImportUsersCsv(csv, actor.Value);
            return result.Succeeded
                ? Results.Ok(new { imported = true, count = result.Value!.Imported })
                : Results.BadRequest(new { imported = false, errors = result.Errors });
        }, isImport: true);
    }
    catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException)
    {
        return Results.BadRequest(new { imported = false, errors = new[] { ex.Message } });
    }
}).DisableAntiforgery();

app.MapDelete("/setup/users/{userName}", (string userName, string? actingUserName, string? actingSessionToken, AuthSessionService authService, SetupManagementService service, HttpContext context, SetupWriteEndpoint audit) =>
{
    var sessionError = RejectInvalidUserManagementSession(actingUserName, actingSessionToken, authService);
    if (sessionError is not null)
    {
        return sessionError;
    }

    return audit.Observe(actingUserName!, "Users", () =>
    {
        var result = service.DeleteUser(userName, actingUserName);
        return result.Succeeded
            ? Results.NoContent()
            : Results.BadRequest(new { errors = result.Errors });
    });
});

app.MapDelete("/setup/resources/{resourceId}", (string resourceId, SetupManagementService service, HttpContext context, SetupWriteEndpoint audit) =>
{
    return audit.Observe(audit.RequestActor(context), "Machines", () =>
    {
        var result = service.DeleteResource(resourceId);
        return result.Succeeded
            ? Results.NoContent()
            : Results.BadRequest(new { errors = result.Errors });
    });
});

app.MapPost("/setup/inspection-plans", (UpsertInspectionSetupRequest request, SetupManagementService service, HttpContext context, SetupWriteEndpoint audit) =>
{
    return audit.Run(context, "Parts & Inspections", () =>
    {
        var result = service.UpsertInspectionSetup(request);
        return result.Succeeded ? Results.Ok(result.Value) : Results.BadRequest(new { errors = result.Errors });
    });
});

app.MapPost("/setup/inspection-plans/delete-phase", (DeleteInspectionSetupPhaseRequest request, SetupManagementService service, HttpContext context, SetupWriteEndpoint audit) =>
{
    return audit.Run(context, "Parts & Inspections", () =>
    {
        var result = service.DeleteInspectionSetupPhase(request);
        return result.Succeeded ? Results.NoContent() : Results.BadRequest(new { errors = result.Errors });
    });
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

app.MapPost("/inspections/complete", (CompleteInspectionRequest request, InspectionMeasurementService service, IRepositoryPersistence persistence) =>
{
    var result = service.CompleteInspection(request);
    if (result.Succeeded)
    {
        persistence.SaveChanges();
    }

    return result.Succeeded
        ? Results.Ok(result.Value)
        : Results.BadRequest(new { errors = result.Errors });
});

app.MapPost("/inspections/fail", (FailInspectionRequest request, InspectionMeasurementService service, IRepositoryPersistence persistence) =>
{
    var result = service.FailInspectionWithSignoff(request);
    if (result.Succeeded)
    {
        persistence.SaveChanges();
    }

    return result.Succeeded
        ? Results.Ok(result.Value)
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

app.MapPatch("/material-changes/{materialChangeId:guid}/lot", (
    Guid materialChangeId,
    UpdateMaterialLotRequest request,
    MaterialChangeLogService service,
    HttpContext context, SetupWriteEndpoint audit) =>
{
    return audit.Observe(audit.RequestActor(context, request.EditedByUserId), "Inspection Corrections", () =>
    {
        var result = service.UpdateLot(materialChangeId, request);
        return result.Succeeded
            ? Results.Ok(result.Value)
            : Results.BadRequest(new { errors = result.Errors });
    }, target: new("MaterialLot", materialChangeId));
});

app.MapGet("/jobs/{jobNum}/notes", (string jobNum, JobNoteService service, HttpContext context, AuthSessionService auth) =>
{
    if (!auth.ValidateSession(context.Request.Headers["X-SPCStar-User"].ToString(),
        context.Request.Headers["X-SPCStar-Session"].ToString()))
        return Results.Json(new { errors = new[] { "Sign in to view job notes." } }, statusCode: 401);
    return Results.Ok(service.GetForJob(jobNum));
});

app.MapGet("/jobs/{jobNum}/history", (string jobNum, JobHistoryService service, HttpContext context, AuthSessionService auth) =>
{
    if (!auth.ValidateSession(context.Request.Headers["X-SPCStar-User"].ToString(),
        context.Request.Headers["X-SPCStar-Session"].ToString()))
        return Results.Json(new { errors = new[] { "Sign in to view job history." } }, statusCode: 401);
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

app.MapPost("/setup/job-data-fields", (UpsertPartJobDataFieldRequest request, SetupManagementService service, HttpContext context, SetupWriteEndpoint audit) =>
{
    return audit.Run(context, "Parts & Inspections", () =>
    {
        var result = service.UpsertPartJobDataField(request);
        return result.Succeeded ? Results.Ok(result.Value) : Results.BadRequest(new { errors = result.Errors });
    });
});

app.MapPost("/setup/material-fields", (UpsertPartMaterialFieldRequest request, SetupManagementService service, HttpContext context, SetupWriteEndpoint audit) =>
{
    return audit.Run(context, "Parts & Inspections", () =>
    {
        var result = service.UpsertPartMaterialField(request);
        return result.Succeeded ? Results.Ok(result.Value) : Results.BadRequest(new { errors = result.Errors });
    });
});

app.MapPost("/jobs/{jobNum}/notes", (string jobNum, JobNoteEntry request, JobNoteService service,
    IRepositoryPersistence persistence, HttpContext context, AuthSessionService auth) =>
{
    var actor = auth.CurrentUser(context.Request.Headers["X-SPCStar-User"].ToString(),
        context.Request.Headers["X-SPCStar-Session"].ToString());
    if (!actor.Succeeded)
        return Results.Json(new { errors = new[] { "Sign in to submit a job note." } }, statusCode: 401);
    var result = service.Add(request with { JobNum = jobNum,
        OperatorUserId = actor.Value!.UserName, Timestamp = DateTimeOffset.UtcNow });
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

app.MapPost("/work-context/batch", (WorkContextBatchRequest request, WorkContextService service) =>
{
    return Results.Ok(service.BuildBatch(request));
});

app.MapPost("/charts/data", (ChartDataRequest request, ChartDataService service) =>
{
    return Results.Ok(service.Build(request));
});

app.MapPost("/history/part-quality", (PartQualityRequest request, PartQualityService service) =>
{
    var result = service.Build(request);
    return result.Succeeded ? Results.Ok(result.Value) : Results.BadRequest(new { errors = result.Errors });
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

app.MapPost("/certs/ethicon/preview", async ([FromForm] IFormFile shipmentFile, [FromForm] IFormFile masterFile, [FromForm] string family, EthiconCoaPreviewService service) =>
{
    if (shipmentFile.Length == 0)
    {
        return Results.BadRequest(new { errors = new[] { "Select an Ethicon CE or TP shipment workbook." } });
    }

    if (masterFile.Length == 0)
    {
        return Results.BadRequest(new { errors = new[] { "Select the Bihler shipment master workbook." } });
    }

    await using var shipmentStream = shipmentFile.OpenReadStream();
    await using var masterStream = masterFile.OpenReadStream();
    var result = service.Preview(family, shipmentStream, masterStream);
    return result.Succeeded
        ? Results.Ok(result.Value)
        : Results.BadRequest(new { errors = result.Errors });
}).DisableAntiforgery();

app.MapPost("/certs/ethicon/generate", async (
    [FromForm] IFormFile shipmentFile,
    [FromForm] IFormFile masterFile,
    [FromForm] string family,
    [FromForm] string partNum,
    [FromForm] string wireShipment,
    EthiconCoaPreviewService service) =>
{
    if (shipmentFile.Length == 0)
    {
        return Results.BadRequest(new { errors = new[] { "Select an Ethicon CE or TP shipment workbook." } });
    }

    if (masterFile.Length == 0)
    {
        return Results.BadRequest(new { errors = new[] { "Select the Bihler shipment master workbook." } });
    }

    var templatePath = EthiconTemplatePath(builder.Environment.ContentRootPath, family);
    if (templatePath is null || !File.Exists(templatePath))
    {
        return Results.BadRequest(new { errors = new[] { "The Ethicon COA template is not installed for the selected family." } });
    }

    await using var shipmentStream = shipmentFile.OpenReadStream();
    await using var masterStream = masterFile.OpenReadStream();
    await using var templateStream = File.OpenRead(templatePath);
    var result = service.Generate(family, shipmentStream, masterStream, templateStream, partNum, wireShipment);
    return result.Succeeded
        ? Results.File(result.Value!.Content, result.Value.ContentType, result.Value.FileName)
        : Results.BadRequest(new { errors = result.Errors });
}).DisableAntiforgery();

app.MapPost("/certs/weld-pin/preview", async (
    [FromForm] IFormFile requestFile,
    [FromForm] IFormFile keyenceFile,
    WeldPinCertService service) =>
{
    if (requestFile.Length == 0)
    {
        return Results.BadRequest(new { errors = new[] { "Select the Weld Pin Production Planning cert request workbook." } });
    }

    if (keyenceFile.Length == 0)
    {
        return Results.BadRequest(new { errors = new[] { "Select the full-job Keyence CSV export." } });
    }

    await using var requestStream = requestFile.OpenReadStream();
    await using var keyenceStream = keyenceFile.OpenReadStream();
    var result = service.Preview(requestStream, keyenceStream);
    return result.Succeeded
        ? Results.Ok(result.Value)
        : Results.BadRequest(new { errors = result.Errors });
}).DisableAntiforgery();

app.MapPost("/certs/weld-pin/generate", async (
    [FromForm] IFormFile requestFile,
    [FromForm] IFormFile keyenceFile,
    WeldPinCertService service) =>
{
    if (requestFile.Length == 0)
    {
        return Results.BadRequest(new { errors = new[] { "Select the Weld Pin Production Planning cert request workbook." } });
    }

    if (keyenceFile.Length == 0)
    {
        return Results.BadRequest(new { errors = new[] { "Select the full-job Keyence CSV export." } });
    }

    var templatePath = WeldPinTemplatePath(builder.Environment.ContentRootPath);
    if (!File.Exists(templatePath))
    {
        return Results.BadRequest(new { errors = new[] { "The Weld Pin cert template is not installed." } });
    }

    await using var requestStream = requestFile.OpenReadStream();
    await using var keyenceStream = keyenceFile.OpenReadStream();
    await using var templateStream = File.OpenRead(templatePath);
    var result = service.Generate(requestStream, keyenceStream, templateStream);
    return result.Succeeded
        ? Results.File(result.Value!.Content, result.Value.ContentType, result.Value.FileName)
        : Results.BadRequest(new { errors = result.Errors });
}).DisableAntiforgery();

app.MapPost("/certs/ethicon/seed-test-data", async (
    HttpContext httpContext,
    [FromForm] IFormFile shipmentFile,
    [FromForm] string family,
    [FromForm] string? resourceId,
    EthiconCoaPreviewService service,
    IRepositoryPersistence persistence) =>
{
    var remoteAddress = httpContext.Connection.RemoteIpAddress;
    if (remoteAddress is null || !System.Net.IPAddress.IsLoopback(remoteAddress))
    {
        return Results.NotFound();
    }

    if (shipmentFile.Length == 0)
    {
        return Results.BadRequest(new { errors = new[] { "Select an Ethicon CE or TP shipment workbook." } });
    }

    await using var shipmentStream = shipmentFile.OpenReadStream();
    var result = service.SeedTestData(family, shipmentStream, resourceId);
    if (result.Succeeded)
    {
        persistence.SaveChanges();
    }

    return result.Succeeded
        ? Results.Ok(result.Value)
        : Results.BadRequest(new { errors = result.Errors });
}).DisableAntiforgery();

app.MapGet("/review/job", (string partNum, string jobNum, JobReviewService service, string? shift,
    string? processCode, int? operationSeq, string? inspectionPhase, string? resourceId,
    string? characteristicName, DateTimeOffset? from, DateTimeOffset? to) =>
{
    var result = service.Build(partNum, jobNum, shift, new(processCode, operationSeq, inspectionPhase, resourceId, characteristicName, from, to));
    return result.Succeeded
        ? Results.Ok(result.Value)
        : Results.BadRequest(new { errors = result.Errors });
});

app.MapGet("/review/part", (string partNum, QaSummaryExportService service, string? shift,
    string? processCode, int? operationSeq, string? inspectionPhase, string? resourceId,
    string? characteristicName, DateTimeOffset? from, DateTimeOffset? to) =>
{
    var result = service.BuildPartCapability(partNum, shift, new(processCode, operationSeq, inspectionPhase, resourceId, characteristicName, from, to));
    return result.Succeeded
        ? Results.Ok(result.Value)
        : Results.BadRequest(new { errors = result.Errors });
});

app.MapPatch("/review/measurements/{measurementId:guid}", (
    Guid measurementId,
    UpdateInspectionMeasurementRequest request,
    JobReviewService service,
    HttpContext context, SetupWriteEndpoint audit) =>
{
    return audit.Observe(audit.RequestActor(context, request.EditedByUserId), "Inspection Corrections", () =>
    {
        var result = service.UpdateMeasurement(measurementId, request);
        return result.Succeeded
            ? Results.Ok(result.Value)
            : Results.BadRequest(new { errors = result.Errors });
    }, target: new("Measurement", measurementId), reason: request.Reason);
});

app.MapPatch("/review/completions/{completionId:guid}/machine-counter", (
    Guid completionId,
    UpdateMachineCounterRequest request,
    JobReviewService service,
    HttpContext context, SetupWriteEndpoint audit) =>
{
    return audit.Observe(audit.RequestActor(context, request.EditedByUserId), "Inspection Corrections", () =>
    {
        var result = service.UpdateMachineCounter(completionId, request);
        return result.Succeeded
            ? Results.Ok(result.Value)
            : Results.BadRequest(new { errors = result.Errors });
    }, target: new("MachineCounter", completionId), reason: request.Reason);
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

app.MapPost("/exports/history-ledger.xlsx", (LedgerHistoryExportRequest request, HistoryExportService service) =>
{
    var bytes = service.ExportLedgerXlsx(request);
    return Results.File(
        bytes,
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        $"SPC-Star-History-Ledger-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.xlsx");
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

static string? EthiconTemplatePath(string contentRootPath, string family)
{
    var fileName = family.Equals("CE", StringComparison.OrdinalIgnoreCase)
        ? "QF-E112-CE-5-Digits-Epicor.xlsm"
        : family.Equals("TP", StringComparison.OrdinalIgnoreCase)
            ? "QF-E113-TP-5-Digits-Epicor.xlsm"
            : null;
    return fileName is null
        ? null
        : Path.Combine(contentRootPath, "CertTemplates", "Ethicon", fileName);
}

static string WeldPinTemplatePath(string contentRootPath)
{
    return Path.Combine(contentRootPath, "CertTemplates", "WeldPin", "Weld Pin Cert Template.xlsx");
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

static IResult? RejectInvalidUserManagementSession(string? actingUserName, string? actingSessionToken, AuthSessionService authService)
{
    return authService.ValidateSession(actingUserName, actingSessionToken)
        ? null
        : Results.BadRequest(new { errors = new[] { "Current sign-in session is not valid. Sign out and sign back in before changing user access." } });
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
