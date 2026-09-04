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

        var hasOverrideAccess = permissionService.UserHasPermission(request.OverrideUserName, PermissionNames.CanOverrideDriftLock) ||
            IsArchonSystemManager(request.OverrideUserName);
        if (!hasOverrideAccess)
        {
            return ServiceResult<AlertOverride>.Fail("User is not authorized to override drift locks.");
        }

        if (!credentialService.ValidateCredential(request.OverrideUserName, request.OverridePassword))
        {
            return ServiceResult<AlertOverride>.Fail("Invalid override credentials.");
        }

        var isArchonOverride = IsArchonSystemManager(request.OverrideUserName);
        var overrideRole = isArchonOverride ? "System Manager" : permissionService.HighestOverrideRole(request.OverrideUserName);
        var isGodOverride = IsGodBypassUser(request.OverrideUserName);
        if (!IsSupportedCauseCategory(request.CauseCategory))
        {
            return ServiceResult<AlertOverride>.Fail("CauseCategory is not supported.");
        }

        if (isGodOverride && string.IsNullOrWhiteSpace(request.WhyStandardProcessWasBypassed))
        {
            return ServiceResult<AlertOverride>.Fail("WhyStandardProcessWasBypassed is required for GOD overrides.");
        }

        if (!isGodOverride && string.IsNullOrWhiteSpace(request.CauseText))
        {
            return ServiceResult<AlertOverride>.Fail("CauseText is required.");
        }

        if (!isGodOverride && string.IsNullOrWhiteSpace(request.SolutionText))
        {
            return ServiceResult<AlertOverride>.Fail("SolutionText is required.");
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
            CauseCategory = isGodOverride ? "GOD Bypass" : NormalizeCauseCategory(request.CauseCategory),
            CauseText = isGodOverride ? "Standard correction workflow bypassed by GOD access." : request.CauseText.Trim(),
            SolutionText = isGodOverride ? "Bypass approved. See bypass reason." : request.SolutionText.Trim(),
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

    private static bool IsArchonSystemManager(string userName)
    {
        return userName.Equals("Archon", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGodBypassUser(string userName)
    {
        return userName.Equals("god1", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeCauseCategory(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "Unspecified" : value.Trim();
    }

    private static bool IsSupportedCauseCategory(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return NormalizeCauseCategory(value) is
            "Machine" or
            "Tooling" or
            "Material" or
            "User Error" or
            "Measurement Method" or
            "Process Drift" or
            "Other" or
            "Unspecified";
    }
}
