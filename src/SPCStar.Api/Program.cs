using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddSingleton<ISpcRepository, InMemorySpcRepository>();
builder.Services.AddSingleton<WesternElectricRuleService>();
builder.Services.AddSingleton<PermissionService>();
builder.Services.AddSingleton<CredentialService>();
builder.Services.AddSingleton<SetupImportService>();
builder.Services.AddSingleton<InspectionMeasurementService>();
builder.Services.AddSingleton<AlertOverrideService>();
builder.Services.AddSingleton<QaSummaryExportService>();
builder.Services.AddSingleton<MaterialChangeLogService>();
builder.Services.AddSingleton<InspectionFrequencyService>();
builder.Services.AddSingleton<ChartDataService>();
builder.Services.AddSingleton<HistoryExportService>();
builder.Services.AddSingleton<SetupQueryService>();
builder.Services.AddSingleton<SetupManagementService>();
builder.Services.AddSingleton<OfflineSyncService>();
builder.Services.AddSingleton<AuthSessionService>();
builder.Services.AddSingleton<WorkContextService>();

var app = builder.Build();

SeedData.SeedAll(app.Services.GetRequiredService<ISpcRepository>());

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new { status = "ok", app = "SPC Star" }));

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

app.MapPost("/setup/import-csv", (CsvImportRequest request, SetupImportService service) =>
{
    var result = service.ImportCsv(request.Csv);
    return result.Succeeded
        ? Results.Ok(new { imported = true })
        : Results.BadRequest(new { imported = false, errors = result.Errors });
});

app.MapGet("/setup/users", (SetupManagementService service) =>
{
    return Results.Ok(service.GetUsers());
});

app.MapGet("/setup/roles", (SetupManagementService service) =>
{
    return Results.Ok(service.GetRoles());
});

app.MapPost("/setup/users", (UpsertUserRequest request, SetupManagementService service) =>
{
    var result = service.UpsertUser(request);
    return result.Succeeded
        ? Results.Ok(result.Value)
        : Results.BadRequest(new { errors = result.Errors });
});

app.MapPost("/setup/inspection-plans", (UpsertInspectionSetupRequest request, SetupManagementService service) =>
{
    var result = service.UpsertInspectionSetup(request);
    return result.Succeeded
        ? Results.Ok(result.Value)
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

app.MapPost("/inspections/measurements", (InspectionMeasurementEntry request, InspectionMeasurementService service) =>
{
    var result = service.EnterMeasurement(request);
    return result.Succeeded
        ? Results.Created($"/inspections/measurements/{result.Value!.Id}", result.Value)
        : Results.BadRequest(new { errors = result.Errors });
});

app.MapPost("/material-changes", (MaterialChangeLogEntry request, MaterialChangeLogService service) =>
{
    var result = service.Record(request);
    return result.Succeeded
        ? Results.Created($"/material-changes/{result.Value!.Id}", result.Value)
        : Results.BadRequest(new { errors = result.Errors });
});

app.MapPost("/alerts/{alertId:guid}/override", (
    Guid alertId,
    AlertOverrideApiRequest request,
    AlertOverrideService service) =>
{
    var result = service.Override(new AlertOverrideRequest(
        alertId,
        request.OverrideUserName,
        request.OverridePassword,
        request.CauseText,
        request.SolutionText,
        request.WhyStandardProcessWasBypassed,
        request.UnlockedAt ?? DateTimeOffset.UtcNow));

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
    WorkContextService service) =>
{
    return Results.Ok(service.Build(new WorkContextRequest(
        jobNum,
        partNum,
        processCode,
        operationSeq,
        resourceId,
        characteristicName,
        DateTimeOffset.UtcNow)));
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

app.MapGet("/qa/jobs/{jobNum}/variable-means", (string jobNum, bool? requiredOnly, QaSummaryExportService service) =>
{
    var result = service.BuildJobVariableMeans(jobNum, requiredOnly ?? true);
    return result.Succeeded
        ? Results.Ok(result.Value)
        : Results.BadRequest(new { errors = result.Errors });
});

app.MapGet("/qa/jobs/{jobNum}/variable-means.csv", (string jobNum, bool? requiredOnly, QaSummaryExportService service) =>
{
    var result = service.ExportJobVariableMeansCsv(jobNum, requiredOnly ?? true);
    return result.Succeeded
        ? Results.Text(result.Value!, "text/csv")
        : Results.BadRequest(new { errors = result.Errors });
});

app.MapGet("/qa/job-variable-means", (string jobNums, bool? requiredOnly, QaSummaryExportService service) =>
{
    var result = service.BuildJobVariableMeans(SplitCsv(jobNums), requiredOnly ?? true);
    return result.Succeeded
        ? Results.Ok(result.Value)
        : Results.BadRequest(new { errors = result.Errors });
});

app.MapGet("/qa/job-variable-means.csv", (string jobNums, bool? requiredOnly, QaSummaryExportService service) =>
{
    var result = service.ExportJobVariableMeansCsv(SplitCsv(jobNums), requiredOnly ?? true);
    return result.Succeeded
        ? Results.Text(result.Value!, "text/csv")
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

app.MapPost("/sync/offline-changes", (OfflineSyncRequest request, OfflineSyncService service) =>
{
    var result = service.Sync(request);
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

app.Run();

public sealed record CsvImportRequest(string Csv);

public sealed record AlertOverrideApiRequest(
    string OverrideUserName,
    string OverridePassword,
    string CauseText,
    string SolutionText,
    string? WhyStandardProcessWasBypassed,
    DateTimeOffset? UnlockedAt);
