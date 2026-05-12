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

    [Fact]
    public void UpsertInspectionSetup_RenamesExistingOperationWithoutCreatingDuplicate()
    {
        var repository = new InMemorySpcRepository();
        var service = new SetupManagementService(repository);
        var original = Request(processCode: "MOLD", characteristicName: "Diameter");
        Assert.True(service.UpsertInspectionSetup(original).Succeeded);

        var renamed = Request(processCode: "PRESS", characteristicName: "Diameter", originalProcessCode: "MOLD");
        var result = service.UpsertInspectionSetup(renamed);

        Assert.True(result.Succeeded);
        var process = Assert.Single(repository.Processes);
        Assert.Equal("PRESS", process.ProcessCode);
        Assert.Single(repository.Operations);
        Assert.Single(repository.Characteristics);
        var limit = Assert.Single(repository.ControlLimits);
        Assert.Equal("PRESS", limit.ProcessCode);
    }

    [Fact]
    public void UpsertInspectionSetup_RenamesExistingVariableWithoutCreatingDuplicate()
    {
        var repository = new InMemorySpcRepository();
        var service = new SetupManagementService(repository);
        Assert.True(service.UpsertInspectionSetup(Request(processCode: "MOLD", characteristicName: "Diameter")).Succeeded);

        var result = service.UpsertInspectionSetup(Request(
            processCode: "MOLD",
            characteristicName: "Outside Diameter",
            originalCharacteristicName: "Diameter"));

        Assert.True(result.Succeeded);
        var characteristic = Assert.Single(repository.Characteristics);
        Assert.Equal("Outside Diameter", characteristic.Name);
        Assert.Single(repository.InspectionPlans);
        var limit = Assert.Single(repository.ControlLimits);
        Assert.Equal("Outside Diameter", limit.CharacteristicName);
    }

    private static UpsertInspectionSetupRequest Request(
        string processCode,
        string characteristicName,
        string? originalProcessCode = null,
        string? originalCharacteristicName = null)
    {
        return new UpsertInspectionSetupRequest(
            "P200",
            "Customer part",
            processCode,
            processCode,
            10,
            characteristicName,
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
            true,
            originalProcessCode,
            10,
            originalCharacteristicName);
    }
}
