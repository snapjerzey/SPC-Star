using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;

namespace SPCStar.Core.Services;

public sealed record JobInspectionMeasurementDto(
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
    IReadOnlyList<InspectionPlanSetupDto> InspectionPlan,
    IReadOnlyList<JobVariableMeanRow> VariableSummary,
    IReadOnlyList<JobInspectionMeasurementDto> Measurements,
    IReadOnlyList<JobHistoryEntryDto> History);

public sealed class JobReviewService(
    ISpcRepository repository,
    SetupQueryService setupQueryService,
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
            setupQueryService.GetInspectionPlans(part),
            summary.Value,
            measurements,
            jobHistoryService.GetForJob(job)));
    }
}
