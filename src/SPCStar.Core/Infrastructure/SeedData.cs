using SPCStar.Core.Domain;

namespace SPCStar.Core.Infrastructure;

public static class SeedData
{
    public static void SeedSecurity(InMemorySpcRepository repository)
    {
        if (repository.Roles.Count > 0)
        {
            return;
        }

        var operatorRole = Role(RoleNames.Operator);
        var lineTech = Role(RoleNames.LineTech, PermissionNames.CanOverrideDriftLock);
        var qa = Role(RoleNames.QA, PermissionNames.CanOverrideDriftLock, PermissionNames.CanExportQAData);
        var admin = Role(
            RoleNames.Admin,
            PermissionNames.CanManageInspectionPlans,
            PermissionNames.CanImportSetupData,
            PermissionNames.CanManageUsers);
        var god = Role(
            RoleNames.GOD,
            PermissionNames.CanOverrideDriftLock,
            PermissionNames.CanManageInspectionPlans,
            PermissionNames.CanImportSetupData,
            PermissionNames.CanExportQAData,
            PermissionNames.CanManageUsers,
            PermissionNames.CanUseGodMode);

        repository.Roles.AddRange([operatorRole, lineTech, qa, admin, god]);
        repository.Users.Add(new User { UserName = "operator1", Roles = { operatorRole } });
        repository.Users.Add(new User { UserName = "linetech1", Roles = { lineTech } });
        repository.Users.Add(new User { UserName = "qa1", Roles = { qa } });
        repository.Users.Add(new User { UserName = "admin1", Roles = { admin } });
        repository.Users.Add(new User { UserName = "god1", Roles = { god } });
    }

    private static Role Role(string name, params string[] permissions)
    {
        var role = new Role { Name = name };
        foreach (var permission in permissions)
        {
            role.Permissions.Add(permission);
        }

        return role;
    }
}
