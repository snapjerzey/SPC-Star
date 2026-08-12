using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;

namespace SPCStar.Core.Tests;

internal static class TestSeedData
{
    public static void SeedUsers(ISpcRepository repository)
    {
        AddUser(repository, "operator1", "operator1", RoleNames.Operator, "General");
        AddUser(repository, "linetech1", "linetech1", RoleNames.LineTech, "General");
        AddUser(repository, "qa1", "qa1", RoleNames.QA, "General");
        AddUser(repository, "god1", "god1", RoleNames.GOD, "General");
    }

    private static void AddUser(ISpcRepository repository, string userName, string password, string roleName, params string[] productGroups)
    {
        if (repository.Users.Any(user => user.UserName.Equals(userName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var role = repository.Roles.Single(item => item.Name.Equals(roleName, StringComparison.OrdinalIgnoreCase));
        var (hash, salt) = PasswordHasher.HashPassword(password);
        var user = new User { UserName = userName, PasswordHash = hash, PasswordSalt = salt, Roles = { role } };
        user.ProductGroups.AddRange(productGroups);
        repository.Users.Add(user);
    }
}
