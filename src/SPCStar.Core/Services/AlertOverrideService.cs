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
    DateTimeOffset? SubmittedAt = null,
    string? CauseCategory = null);

public sealed class AlertOverrideService(
    ISpcRepository repository,
    PermissionService permissionService,
    CredentialService credentialService)
{
    public ServiceResult<AlertOverride> Override(AlertOverrideRequest request)
    {
        if (!IsSupportedCauseCategory(request.CauseCategory))
        {
            return ServiceResult<AlertOverride>.Fail("CauseCategory is not supported.");
        }
        if (!NormalizeCauseCategory(request.CauseCategory).Equals("User Error", StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResult<AlertOverride>.Fail("Only User Error can clear a lock. End the inspection as failed for Machine, Tooling, or Material.");
        }
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

        if (alert.Status == AlertStatus.Overridden)
        {
            var existing = repository.AlertOverrides
                .OrderByDescending(item => item.UnlockedAt)
                .FirstOrDefault(item => item.AlertId == alert.Id);
            if (existing is not null)
            {
                return ServiceResult<AlertOverride>.Ok(existing);
            }
        }
        else if (alert.Status != AlertStatus.Active)
        {
            return ServiceResult<AlertOverride>.Fail("Alert is not active.");
        }

        var hasOverrideAccess = permissionService.UserHasPermission(request.OverrideUserName, PermissionNames.CanOverrideDriftLock) ||
            IsArchonSystemManager(request.OverrideUserName) ||
            (alert.OperatorUserId.Equals(request.OverrideUserName, StringComparison.OrdinalIgnoreCase) &&
                permissionService.UserHasPermission(request.OverrideUserName, PermissionNames.CanEnterInspections));
        if (!hasOverrideAccess)
        {
            return ServiceResult<AlertOverride>.Fail("Operators may clear only their own entry mistakes. A Line Tech or QA must assist with another user's lock.");
        }

        if (!credentialService.ValidateCredential(request.OverrideUserName, request.OverridePassword))
        {
            return ServiceResult<AlertOverride>.Fail("Invalid override credentials.");
        }

        var isArchonOverride = IsArchonSystemManager(request.OverrideUserName);
        var overrideRole = isArchonOverride ? "System Manager" : permissionService.HighestOverrideRole(request.OverrideUserName) ?? RoleNames.Operator;
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
            CauseCategory = NormalizeCauseCategory(request.CauseCategory),
            CauseText = CleanOptional(request.CauseText) ?? "",
            SolutionText = CleanOptional(request.SolutionText) ?? "",
            LockedAt = alert.LockedAt,
            UnlockedAt = request.UnlockedAt,
            SubmittedAt = request.SubmittedAt ?? request.UnlockedAt,
            SyncedAt = DateTimeOffset.UtcNow
        };

        repository.RuleViolations.RemoveAll(violation => violation.AlertId == alert.Id);
        repository.Alerts.Remove(alert);
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

    private bool IsArchonSystemManager(string userName)
    {
        return permissionService.UserHasPermission(userName, PermissionNames.CanUseGodMode);
    }

    private static string NormalizeCauseCategory(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "Unspecified" : value.Trim();
    }

    private static bool IsSupportedCauseCategory(string? value)
    {
        return NormalizeCauseCategory(value) is
            "Machine" or
            "Tooling" or
            "Material" or
            "User Error";
    }
}
