using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using Xunit;

namespace SPCStar.Core.Tests;

public sealed class AuthSessionServiceTests
{
    [Fact]
    public void Login_ReturnsRolesPermissionsAndDevToken()
    {
        var repository = new InMemorySpcRepository();
        SeedData.SeedSecurity(repository);
        var service = new AuthSessionService(repository, new CredentialService(repository));

        var result = service.Login(new LoginRequest("qa1", "qa1"));

        Assert.True(result.Succeeded);
        Assert.Equal("qa1", result.Value!.UserName);
        Assert.Contains("QA", result.Value.Roles);
        Assert.Contains("CanOverrideDriftLock", result.Value.Permissions);
        Assert.Empty(result.Value.ProductGroups);
        Assert.Equal("dev-session:qa1", result.Value.SessionToken);
    }

    [Fact]
    public void Login_RejectsBadPassword()
    {
        var repository = new InMemorySpcRepository();
        SeedData.SeedSecurity(repository);
        var service = new AuthSessionService(repository, new CredentialService(repository));

        var result = service.Login(new LoginRequest("qa1", "wrong"));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Login_LineTechCanInspectAndOverride()
    {
        var repository = new InMemorySpcRepository();
        SeedData.SeedSecurity(repository);
        var service = new AuthSessionService(repository, new CredentialService(repository));

        var result = service.Login(new LoginRequest("linetech1", "linetech1"));

        Assert.True(result.Succeeded);
        Assert.Contains("LineTech", result.Value!.Roles);
        Assert.Contains("CanEnterInspections", result.Value.Permissions);
        Assert.Contains("CanOverrideDriftLock", result.Value.Permissions);
        Assert.Empty(result.Value.ProductGroups);
    }

    [Fact]
    public void SeedSecurity_UpgradesExistingLineTechRole()
    {
        var repository = new InMemorySpcRepository();
        var lineTech = new Role { Name = "LineTech" };
        lineTech.Permissions.Add("CanOverrideDriftLock");
        repository.Roles.Add(lineTech);
        repository.Users.Add(new User
        {
            UserName = "linetech1",
            PasswordHash = "hash",
            PasswordSalt = "salt",
            Roles = { lineTech }
        });

        SeedData.SeedSecurity(repository);

        Assert.Contains(lineTech.Permissions, permission => permission == "CanEnterInspections");
        Assert.Contains(lineTech.Permissions, permission => permission == "CanOverrideDriftLock");
    }
}
