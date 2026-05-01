using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;

namespace SPCStar.Core.Services;

public sealed record AlertOverrideRequest(
    Guid AlertId,
    string OverrideUserName,
    string CauseText,
    string SolutionText,
    string? WhyStandardProcessWasBypassed,
    DateTimeOffset UnlockedAt);

public sealed class AlertOverrideService(
    InMemorySpcRepository repository,
    PermissionService permissionService)
{
    public ServiceResult<AlertOverride> Override(AlertOverrideRequest request)
    {
        var alert = repository.Alerts.FirstOrDefault(item => item.Id == request.AlertId);
        if (alert is null)
        {
            return ServiceResult<AlertOverride>.Fail("Alert was not found.");
        }

        if (alert.Status != AlertStatus.Active)
        {
            return ServiceResult<AlertOverride>.Fail("Alert is not active.");
        }

        if (!permissionService.UserHasPermission(request.OverrideUserName, PermissionNames.CanOverrideDriftLock))
        {
            return ServiceResult<AlertOverride>.Fail("User is not authorized to override drift locks.");
        }

        var overrideRole = permissionService.HighestOverrideRole(request.OverrideUserName);
        if (string.IsNullOrWhiteSpace(request.CauseText))
        {
            return ServiceResult<AlertOverride>.Fail("CauseText is required.");
        }

        if (string.IsNullOrWhiteSpace(request.SolutionText))
        {
            return ServiceResult<AlertOverride>.Fail("SolutionText is required.");
        }

        if (overrideRole == RoleNames.GOD && string.IsNullOrWhiteSpace(request.WhyStandardProcessWasBypassed))
        {
            return ServiceResult<AlertOverride>.Fail("WhyStandardProcessWasBypassed is required for GOD overrides.");
        }

        var audit = new AlertOverride
        {
            AlertId = alert.Id,
            OperatorUserId = alert.OperatorUserId,
            OverrideUserId = request.OverrideUserName,
            OverrideRole = overrideRole ?? string.Empty,
            JobNum = alert.JobNum,
            PartNum = alert.PartNum,
            ResourceId = alert.ResourceId,
            CharacteristicName = alert.CharacteristicName,
            RuleTriggered = alert.RuleTriggered,
            CauseText = request.CauseText.Trim(),
            SolutionText = request.SolutionText.Trim(),
            WhyStandardProcessWasBypassed = request.WhyStandardProcessWasBypassed?.Trim(),
            LockedAt = alert.LockedAt,
            UnlockedAt = request.UnlockedAt
        };

        repository.AlertOverrides.Add(audit);
        alert.Status = AlertStatus.Overridden;
        return ServiceResult<AlertOverride>.Ok(audit);
    }
}
