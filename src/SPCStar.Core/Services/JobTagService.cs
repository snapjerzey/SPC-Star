using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;

namespace SPCStar.Core.Services;

public sealed record JobTagDto(
    string TagName,
    string TagValue,
    string OperatorUserId,
    DateTimeOffset UpdatedAt);

public sealed record SaveJobTagsRequest(
    string JobNum,
    string PartNum,
    string ResourceId,
    string OperatorUserId,
    Dictionary<string, string> Tags,
    DateTimeOffset? UpdatedAt = null);

public sealed class JobTagService(ISpcRepository repository)
{
    public IReadOnlyList<JobTagDto> GetForJob(string jobNum)
    {
        if (string.IsNullOrWhiteSpace(jobNum))
        {
            return [];
        }

        return repository.JobTags
            .Where(tag => tag.JobNum.Equals(jobNum.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderBy(tag => tag.TagName)
            .Select(tag => new JobTagDto(tag.TagName, tag.TagValue, tag.OperatorUserId, tag.UpdatedAt))
            .ToArray();
    }

    public ServiceResult<IReadOnlyList<JobTagDto>> Save(SaveJobTagsRequest request)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return ServiceResult<IReadOnlyList<JobTagDto>>.Fail(errors);
        }

        var updatedAt = request.UpdatedAt ?? DateTimeOffset.UtcNow;
        foreach (var pair in request.Tags!)
        {
            var name = pair.Key.Trim();
            var value = pair.Value?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var existing = repository.JobTags.FirstOrDefault(tag =>
                tag.JobNum.Equals(request.JobNum.Trim(), StringComparison.OrdinalIgnoreCase) &&
                tag.TagName.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                repository.JobTags.Add(new JobTag
                {
                    JobNum = request.JobNum.Trim(),
                    PartNum = request.PartNum.Trim(),
                    ResourceId = request.ResourceId.Trim(),
                    TagName = name,
                    TagValue = value,
                    OperatorUserId = request.OperatorUserId.Trim(),
                    UpdatedAt = updatedAt
                });
                continue;
            }

            existing.PartNum = request.PartNum.Trim();
            existing.ResourceId = request.ResourceId.Trim();
            existing.TagValue = value;
            existing.OperatorUserId = request.OperatorUserId.Trim();
            existing.UpdatedAt = updatedAt;
        }

        return ServiceResult<IReadOnlyList<JobTagDto>>.Ok(GetForJob(request.JobNum));
    }

    private static List<string> Validate(SaveJobTagsRequest request)
    {
        var errors = new List<string>();
        Required(request.JobNum, nameof(request.JobNum), errors);
        Required(request.PartNum, nameof(request.PartNum), errors);
        Required(request.ResourceId, nameof(request.ResourceId), errors);
        Required(request.OperatorUserId, nameof(request.OperatorUserId), errors);
        if (request.Tags is null)
        {
            errors.Add($"{nameof(request.Tags)} is required.");
        }
        return errors;
    }

    private static void Required(string value, string field, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{field} is required.");
        }
    }
}
