using Microsoft.Data.Sqlite;
using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using Xunit;

namespace SPCStar.Core.Tests;

public sealed class PersistentSessionTests
{
    [Fact]
    public void SessionSurvivesServiceAndRepositoryRestartWithoutStoringBearerToken()
    {
        var folder = Path.Combine(Path.GetTempPath(), "spc-sessions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var database = Path.Combine(folder, "data.db");
            var storePath = Path.Combine(folder, "sessions.db");
            var repository = new SqliteBackedSpcRepository(database);
            SeedData.SeedSecurity(repository); TestSeedData.SeedUsers(repository); repository.SaveChanges();
            var service = new AuthSessionService(repository, new CredentialService(repository), new SqliteAuthSessionStore(storePath));
            var token = service.Login(new("qa1", "qa1")).Value!.SessionToken;
            var reloaded = new SqliteBackedSpcRepository(database);
            var restarted = new AuthSessionService(reloaded, new CredentialService(reloaded), new SqliteAuthSessionStore(storePath));
            Assert.True(restarted.ValidateSession("QA1", token));
            Assert.Equal(token, restarted.CurrentUser("qa1", token).Value!.SessionToken);
            Assert.False(restarted.ValidateSession("Archon", token));
            Assert.False(restarted.ValidateSession("qa1", new string('A', 64)));
            Assert.False(restarted.ValidateSession("qa1", null));
            using var connection = new SqliteConnection($"Data Source={storePath};Pooling=False");
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "SELECT token_hash FROM auth_sessions;";
            var stored = Assert.IsType<string>(command.ExecuteScalar());
            Assert.NotEqual(token, stored); Assert.Equal(64, stored.Length);
            connection.Close();
            Assert.DoesNotContain(token, File.ReadAllText(storePath));
            restarted.Logout("qa1", token);
            Assert.False(service.ValidateSession("qa1", token));
            Assert.False(restarted.ValidateSession("qa1", token));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void PasswordChangeAndAccountRemovalInvalidateExistingSessions()
    {
        var r = new InMemorySpcRepository(); SeedData.SeedSecurity(r); TestSeedData.SeedUsers(r);
        var store = new InMemoryAuthSessionStore();
        var service = new AuthSessionService(r, new CredentialService(r), store);
        var first = service.Login(new("qa1", "qa1")).Value!.SessionToken;
        Assert.True(service.ChangePassword(new("qa1", "qa1", "new-password", "new-password")).Succeeded);
        Assert.False(service.ValidateSession("qa1", first));
        var next = service.Login(new("qa1", "new-password")).Value!.SessionToken;
        var user = r.Users.Single(u => u.UserName == "qa1");
        r.Users.Remove(user); Assert.False(service.ValidateSession("qa1", next));
        r.Users.Add(new User { UserName = "qa1", PasswordHash = user.PasswordHash, PasswordSalt = user.PasswordSalt });
        Assert.False(service.ValidateSession("qa1", next));
    }

    [Fact]
    public void LogoutOnlyRevokesItsOwnSessionAndPermissionsAreReadFromCurrentAccount()
    {
        var r = new InMemorySpcRepository(); SeedData.SeedSecurity(r); TestSeedData.SeedUsers(r);
        var service = new AuthSessionService(r, new CredentialService(r));
        var first = service.Login(new("qa1", "qa1")).Value!.SessionToken;
        var second = service.Login(new("qa1", "qa1")).Value!.SessionToken;
        service.Logout("Archon", second); Assert.True(service.ValidateSession("qa1", second));
        service.Logout("qa1", first); Assert.False(service.ValidateSession("qa1", first));
        Assert.True(service.ValidateSession("qa1", second));
        var user = r.Users.Single(u => u.UserName == "qa1");
        user.Roles.Clear(); user.Roles.Add(r.Roles.Single(role => role.Name == RoleNames.Operator));
        var current = service.CurrentUser("qa1", second);
        Assert.True(current.Succeeded); Assert.DoesNotContain(PermissionNames.CanManageUsers, current.Value!.Permissions);
    }
}
