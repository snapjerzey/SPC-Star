using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;

namespace SPCStar.Core.Services;

public sealed record AlertOverrideRequest(
    Guid AlertId,
    string OverrideUserName,
    string OverridePassword,
    string CauseText,
    string SolutionText,
    string? WhyStandardProcessWasBypassed,
    DateTimeOffset UnlockedAt,
    string? DeviceId = null,
    string? ClientRecordId = null,
    DateTimeOffset? SubmittedAt = null);

public sealed class AlertOverrideService(
    ISpcRepository repository,
    PermissionService permissionService,
    CredentialService credentialService)
{
    public ServiceResult<AlertOverride> Override(AlertOverrideRequest request)
    {
        var duplicate = FindDuplicate(request.DeviceId, request.ClientRecordId);
        if (duplicate is not null)
        {
            return ServiceResult<AlertOverride>.Ok(duplicate);
        }

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

        if (!credentialService.ValidateCredential(request.OverrideUserName, request.OverridePassword))
        {
            return ServiceResult<AlertOverride>.Fail("Invalid override credentials.");
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
            ClientRecordId = CleanOptional(request.ClientRecordId),
            DeviceId = CleanOptional(request.DeviceId),
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
            UnlockedAt = request.UnlockedAt,
            SubmittedAt = request.SubmittedAt ?? request.UnlockedAt,
            SyncedAt = DateTimeOffset.UtcNow
        };

        repository.AlertOverrides.Add(audit);
        alert.Status = AlertStatus.Overridden;
        return ServiceResult<AlertOverride>.Ok(audit);
    }

    private AlertOverride? FindDuplicate(string? deviceId, string? clientRecordId)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(clientRecordId))
        {
            return null;
        }

        return repository.AlertOverrides.FirstOrDefault(item =>
            item.DeviceId?.Equals(deviceId.Trim(), StringComparison.OrdinalIgnoreCase) == true &&
            item.ClientRecordId?.Equals(clientRecordId.Trim(), StringComparison.OrdinalIgnoreCase) == true);
    }

    private static string? CleanOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
