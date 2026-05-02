using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddSingleton<OfflineSyncService>();

var app = builder.Build();

SeedData.SeedAll(app.Services.GetRequiredService<ISpcRepository>());

app.MapGet("/health", () => Results.Ok(new { status = "ok", app = "SPC Star" }));

app.MapPost("/setup/import-csv", (CsvImportRequest request, SetupImportService service) =>
{
    var result = service.ImportCsv(request.Csv);
    return result.Succeeded
        ? Results.Ok(new { imported = true })
        : Results.BadRequest(new { imported = false, errors = result.Errors });
});

app.MapGet("/setup/parts", (SetupQueryService service) =>
{
    return Results.Ok(service.GetParts());
});

app.MapGet("/setup/inspection-plans", (string? partNum, SetupQueryService service) =>
{
    return Results.Ok(service.GetInspectionPlans(partNum));
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

app.Run();

public sealed record CsvImportRequest(string Csv);

public sealed record AlertOverrideApiRequest(
    string OverrideUserName,
    string OverridePassword,
    string CauseText,
    string SolutionText,
    string? WhyStandardProcessWasBypassed,
    DateTimeOffset? UnlockedAt);
