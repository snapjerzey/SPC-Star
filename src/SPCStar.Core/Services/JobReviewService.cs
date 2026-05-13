using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;

namespace SPCStar.Core.Services;

public sealed record JobInspectionMeasurementDto(
    Guid Id,
    string JobNum,
    string PartNum,
    string ProcessCode,
    int OperationSeq,
    string InspectionPhase,
    string ResourceId,
    string CharacteristicName,
    decimal Value,
    DateTimeOffset Timestamp,
    string OperatorUserId);

public sealed record JobReviewDto(
    string PartNum,
    string JobNum,
    IReadOnlyList<JobVariableMeanRow> PartCapability,
    IReadOnlyList<JobVariableMeanRow> VariableSummary,
    IReadOnlyList<JobInspectionMeasurementDto> Measurements,
    IReadOnlyList<JobHistoryEntryDto> History);

public sealed record UpdateInspectionMeasurementRequest(
    decimal Value,
    DateTimeOffset? Timestamp = null,
    string? InspectionPhase = null);

public sealed class JobReviewService(
    ISpcRepository repository,
    QaSummaryExportService qaSummaryExportService,
    JobHistoryService jobHistoryService)
{
    public ServiceResult<JobReviewDto> Build(string partNum, string jobNum)
    {
        if (string.IsNullOrWhiteSpace(partNum))
        {
            return ServiceResult<JobReviewDto>.Fail("PartNum is required.");
        }

        if (string.IsNullOrWhiteSpace(jobNum))
        {
            return ServiceResult<JobReviewDto>.Fail("JobNum is required.");
        }

        var part = partNum.Trim();
        var job = jobNum.Trim();
        var existingJob = repository.Jobs.FirstOrDefault(item => item.JobNum.Equals(job, StringComparison.OrdinalIgnoreCase));
        if (existingJob is null)
        {
            return ServiceResult<JobReviewDto>.Fail($"Job was not found: {job}.");
        }

        if (!existingJob.PartNum.Equals(part, StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResult<JobReviewDto>.Fail($"Job {job} is assigned to part {existingJob.PartNum}, not {part}.");
        }

        var summary = qaSummaryExportService.BuildJobVariableMeans([job], requiredOnly: false);
        if (!summary.Succeeded || summary.Value is null)
        {
            return ServiceResult<JobReviewDto>.Fail(summary.Errors);
        }

        var measurements = repository.Measurements
            .Where(measurement =>
                measurement.JobNum.Equals(job, StringComparison.OrdinalIgnoreCase) &&
                measurement.PartNum.Equals(part, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(measurement => measurement.Timestamp)
            .Select(measurement => new JobInspectionMeasurementDto(
                measurement.Id,
                measurement.JobNum,
                measurement.PartNum,
                measurement.ProcessCode,
                measurement.OperationSeq,
                measurement.InspectionPhase,
                measurement.ResourceId,
                measurement.CharacteristicName,
                measurement.Value,
                measurement.Timestamp,
                measurement.OperatorUserId))
            .ToArray();

        return ServiceResult<JobReviewDto>.Ok(new JobReviewDto(
            part,
            job,
            qaSummaryExportService.BuildPartCapability(part).Value ?? [],
            summary.Value,
            measurements,
            jobHistoryService.GetForJob(job)));
    }

    public ServiceResult<JobInspectionMeasurementDto> UpdateMeasurement(Guid measurementId, UpdateInspectionMeasurementRequest request)
    {
        var measurement = repository.Measurements.FirstOrDefault(item => item.Id == measurementId);
        if (measurement is null)
        {
            return ServiceResult<JobInspectionMeasurementDto>.Fail("Inspection measurement was not found.");
        }

        measurement.Value = request.Value;
        if (request.Timestamp.HasValue)
        {
            measurement.Timestamp = request.Timestamp.Value;
        }

        if (!string.IsNullOrWhiteSpace(request.InspectionPhase))
        {
            measurement.InspectionPhase = NormalizeInspectionPhase(request.InspectionPhase);
        }

        return ServiceResult<JobInspectionMeasurementDto>.Ok(new JobInspectionMeasurementDto(
            measurement.Id,
            measurement.JobNum,
            measurement.PartNum,
            measurement.ProcessCode,
            measurement.OperationSeq,
            measurement.InspectionPhase,
            measurement.ResourceId,
            measurement.CharacteristicName,
            measurement.Value,
            measurement.Timestamp,
            measurement.OperatorUserId));
    }

    private static string NormalizeInspectionPhase(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "In Process";
        }

        var phase = value.Trim();
        if (phase.Equals("Startup", StringComparison.OrdinalIgnoreCase))
        {
            return "Startup";
        }

        return phase.Equals("Set Up", StringComparison.OrdinalIgnoreCase) ||
            phase.Equals("Setup", StringComparison.OrdinalIgnoreCase)
            ? "Setup"
            : "In Process";
    }
}
