using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using Xunit;

namespace SPCStar.Core.Tests;

public sealed class ImportPermissionTests
{
    [Theory]
    [InlineData(RoleNames.QA, false)]
    [InlineData(RoleNames.Operator, false)]
    [InlineData(RoleNames.LineTech, false)]
    [InlineData(RoleNames.GOD, true)]
    [InlineData("Custom", false)]
    public void ImportRequiresSystemManagerRole_EvenIfPermissionWasGrantedElsewhere(string name, bool allowed)
    {
        var r = new InMemorySpcRepository();
        var role = new Role { Name=name };
        role.Permissions.Add(PermissionNames.CanImportSetupData);
        role.Permissions.Add(PermissionNames.CanExportQAData);
        r.Roles.Add(role);
        var (hash,salt)=PasswordHasher.HashPassword("test-password");
        r.Users.Add(new User {UserName="tester",PasswordHash=hash,PasswordSalt=salt,Roles={role}});
        var permissions = new PermissionService(r);
        Assert.Equal(allowed,permissions.UserHasPermission("tester",PermissionNames.CanImportSetupData));
        Assert.True(permissions.UserHasPermission("tester",PermissionNames.CanExportQAData));
        var session = new AuthSessionService(r,new CredentialService(r)).Login(new("tester","test-password"));
        Assert.True(session.Succeeded);
        Assert.Equal(allowed,session.Value!.Permissions.Contains(PermissionNames.CanImportSetupData));
    }

    [Fact]
    public void CurrentUserCannotIssueSessionWithoutCredentialsOrImpersonateAnotherAccount()
    {
        var r=new InMemorySpcRepository();SeedData.SeedSecurity(r);TestSeedData.SeedUsers(r);
        var auth=new AuthSessionService(r,new CredentialService(r));
        Assert.False(auth.CurrentUser("Archon").Succeeded);
        Assert.False(auth.CurrentUser("Archon","made-up-token").Succeeded);
        var qa=auth.Login(new("qa1","qa1")).Value!;
        Assert.False(auth.CurrentUser("Archon",qa.SessionToken).Succeeded);
        Assert.Equal(qa.SessionToken,auth.CurrentUser("qa1",qa.SessionToken).Value!.SessionToken);
    }
}
