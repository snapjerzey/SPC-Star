using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using Xunit;

namespace SPCStar.Core.Tests;

public sealed class ReadOnlyQaPermissionTests
{
    [Fact]
    public void ReadOnlyQaCanReadAndGenerateCertsButCannotWriteEvenWithCombinedQaRole()
    {
        var repository = new InMemorySpcRepository();
        SeedData.SeedSecurity(repository);
        var (hash, salt) = PasswordHasher.HashPassword("test-password");
        var user = new User { UserName = "readonly", PasswordHash = hash, PasswordSalt = salt };
        user.Roles.Add(repository.Roles.Single(r => r.Name == RoleNames.QAReadOnly));
        user.Roles.Add(repository.Roles.Single(r => r.Name == RoleNames.QA));
        repository.Users.Add(user);
        var permissions = new PermissionService(repository);
        Assert.True(permissions.UserHasPermission(user.UserName, PermissionNames.CanExportQAData));
        Assert.True(permissions.UserHasPermission(user.UserName, PermissionNames.CanGenerateCerts));
        foreach (var permission in new[] { PermissionNames.CanEnterInspections, PermissionNames.CanManageUsers,
            PermissionNames.CanManageInspectionPlans, PermissionNames.CanEditInspectionHistory,
            PermissionNames.CanImportSetupData, PermissionNames.CanUseGodMode, PermissionNames.CanViewChangeHistory })
            Assert.False(permissions.UserHasPermission(user.UserName, permission));
        var session = new AuthSessionService(repository, new CredentialService(repository))
            .Login(new("readonly", "test-password")).Value!;
        Assert.Equal(new[] { PermissionNames.CanExportQAData, PermissionNames.CanGenerateCerts }.Order(), session.Permissions.Order());
    }

    [Fact]
    public void EverySystemManagerHasSamePermissionsRegardlessOfAccountName()
    {
        var repository = new InMemorySpcRepository();
        SeedData.SeedSecurity(repository);
        repository.Users.Add(new User { UserName = "second-manager", PasswordHash = "test", PasswordSalt = "test", Roles = { repository.Roles.Single(r => r.Name == RoleNames.GOD) } });
        var service = new PermissionService(repository);
        foreach (var permission in repository.Roles.SelectMany(r => r.Permissions).Distinct())
            Assert.Equal(service.UserHasPermission("Archon", permission), service.UserHasPermission("second-manager", permission));
        var qa = repository.Roles.Single(r => r.Name == RoleNames.QA);
        Assert.Contains(PermissionNames.CanViewChangeHistory, qa.Permissions);
        Assert.DoesNotContain(PermissionNames.CanUseGodMode, qa.Permissions);
    }
}
