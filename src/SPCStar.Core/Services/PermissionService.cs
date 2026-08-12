using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;

namespace SPCStar.Core.Services;

public sealed class PermissionService(ISpcRepository repository)
{
    public bool UserHasPermission(string userName, string permission)
    {
        return repository.Users
            .FirstOrDefault(user => string.Equals(user.UserName, userName, StringComparison.OrdinalIgnoreCase))
            ?.Roles.Any(role => role.Permissions.Contains(permission)) == true;
    }

    public string? HighestOverrideRole(string userName)
    {
        var roles = repository.Users
            .FirstOrDefault(user => string.Equals(user.UserName, userName, StringComparison.OrdinalIgnoreCase))
            ?.Roles
            .Select(role => role.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (roles is null)
        {
            return null;
        }

        if (roles.Contains(RoleNames.GOD)) return RoleNames.GOD;
        if (roles.Contains(RoleNames.QA)) return RoleNames.QA;
        if (roles.Contains(RoleNames.LineTech)) return RoleNames.LineTech;
        return null;
    }
}
