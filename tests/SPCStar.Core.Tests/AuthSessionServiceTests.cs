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
}
