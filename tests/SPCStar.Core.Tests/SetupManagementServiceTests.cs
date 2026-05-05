using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using Xunit;

namespace SPCStar.Core.Tests;

public sealed class SetupManagementServiceTests
{
    [Fact]
    public void UpsertUser_CreatesUserWithSelectedRole()
    {
        var repository = new InMemorySpcRepository();
        SeedData.SeedSecurity(repository);
        var service = new SetupManagementService(repository);

        var result = service.UpsertUser(new UpsertUserRequest("inspector2", "secret", [RoleNames.Operator]));

        Assert.True(result.Succeeded);
        Assert.True(new CredentialService(repository).ValidateCredential("inspector2", "secret"));
        Assert.Contains(repository.Users.Single(user => user.UserName == "inspector2").Roles, role => role.Name == RoleNames.Operator);
    }

    [Fact]
    public void UpsertInspectionSetup_CreatesPartOperationCharacteristicAndLimits()
    {
        var repository = new InMemorySpcRepository();
        var service = new SetupManagementService(repository);

        var result = service.UpsertInspectionSetup(new UpsertInspectionSetupRequest(
            "P200",
            "Customer part",
            "CUT",
            "Cutting",
            20,
            "Wall",
            CharacteristicType.Variable,
            2.5m,
            2.3m,
            2.7m,
            2.2m,
            2.8m,
            "mm",
            1,
            FrequencyType.Time,
            30,
            FrequencyUnit.Minutes,
            "WesternElectric",
            true));

        Assert.True(result.Succeeded);
        Assert.Single(repository.Parts);
        Assert.Single(repository.Operations);
        Assert.Single(repository.Characteristics);
        Assert.Single(repository.InspectionPlans);
        var controlLimit = Assert.Single(repository.ControlLimits);
        Assert.Equal(2.2m, controlLimit.Lcl);
        Assert.Equal(2.8m, controlLimit.Ucl);
    }
}
