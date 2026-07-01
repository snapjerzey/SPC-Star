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

        var result = service.UpsertUser(new UpsertUserRequest("inspector2", "secret", [RoleNames.Operator], ["Needles"], "2nd Shift"));

        Assert.True(result.Succeeded);
        Assert.True(new CredentialService(repository).ValidateCredential("inspector2", "secret"));
        Assert.Contains(repository.Users.Single(user => user.UserName == "inspector2").Roles, role => role.Name == RoleNames.Operator);
        Assert.Contains("Needles", repository.Users.Single(user => user.UserName == "inspector2").ProductGroups);
        Assert.Equal("2nd Shift", repository.Users.Single(user => user.UserName == "inspector2").Shift);
    }

    [Fact]
    public void ImportUsersCsv_CreatesUsersFromProductGroupColumns()
    {
        var repository = new InMemorySpcRepository();
        SeedData.SeedSecurity(repository);
        repository.Parts.Add(new Part { PartNum = "70305", Description = "Jaw assy", ProductGroup = "Schneider" });
        repository.Parts.Add(new Part { PartNum = "61135", Description = "Needle blank", ProductGroup = "Ethicon Taperpoint - Needles" });
        var service = new SetupManagementService(repository);

        var result = service.ImportUsersCsv(string.Join(Environment.NewLine, [
            "UserName,FullName,TemporaryPassword,Role,Shift,Schneider,Ethicon Taperpoint - Needles",
            "Jsmith,Smith Jane,TempPass123!,Operator,1st Shift,X,",
            "Ttech,Tech Tim,TempPass123!,LineTech,2nd Shift,,X",
            "JTGill,Gill JT,test,GOD,3rd Shift,X,X",
            string.Empty
        ]));

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.Equal(3, result.Value!.Imported);
        var operatorUser = repository.Users.Single(user => user.UserName == "Jsmith");
        Assert.Contains(operatorUser.Roles, role => role.Name == RoleNames.Operator);
        Assert.Equal(["Schneider"], operatorUser.ProductGroups);
        Assert.Equal("1st Shift", operatorUser.Shift);
        var lineTech = repository.Users.Single(user => user.UserName == "Ttech");
        Assert.Contains(lineTech.Roles, role => role.Name == RoleNames.LineTech);
        Assert.Equal(["Ethicon Taperpoint - Needles"], lineTech.ProductGroups);
        Assert.Equal("2nd Shift", lineTech.Shift);
        var god = repository.Users.Single(user => user.UserName == "JTGill");
        Assert.Contains(god.Roles, role => role.Name == RoleNames.GOD);
        Assert.Equal(["Ethicon Taperpoint - Needles", "Schneider"], god.ProductGroups.OrderBy(group => group).ToArray());
        Assert.Equal("3rd Shift", god.Shift);
    }

    [Fact]
    public void ImportUsersCsv_RejectsUsersWithoutProductGroupAccess()
    {
        var repository = new InMemorySpcRepository();
        SeedData.SeedSecurity(repository);
        repository.Parts.Add(new Part { PartNum = "70305", Description = "Jaw assy", ProductGroup = "Schneider" });
        var service = new SetupManagementService(repository);

        var result = service.ImportUsersCsv(string.Join(Environment.NewLine, [
            "UserName,FullName,TemporaryPassword,Role,Schneider",
            "Jsmith,Smith Jane,TempPass123!,Operator,",
            string.Empty
        ]));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("At least one product group", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(repository.Users, user => user.UserName == "Jsmith");
    }

    [Fact]
    public void DeleteUser_RemovesUser()
    {
        var repository = new InMemorySpcRepository();
        SeedData.SeedSecurity(repository);
        var service = new SetupManagementService(repository);
        Assert.True(service.UpsertUser(new UpsertUserRequest("inspector2", "secret", [RoleNames.Operator])).Succeeded);

        var result = service.DeleteUser("inspector2");

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(repository.Users, user => user.UserName == "inspector2");
    }

    [Fact]
    public void DeleteUser_KeepsAtLeastOneAdminOrGod()
    {
        var repository = new InMemorySpcRepository();
        SeedData.SeedSecurity(repository);
        var service = new SetupManagementService(repository);
        Assert.True(service.DeleteUser("admin1").Succeeded);

        var result = service.DeleteUser("god1");

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void ResetUserPassword_UpdatesCredentialWithoutChangingAccess()
    {
        var repository = new InMemorySpcRepository();
        SeedData.SeedSecurity(repository);
        var service = new SetupManagementService(repository);
        var credentialService = new CredentialService(repository);

        var result = service.ResetUserPassword(new ResetUserPasswordRequest("operator1", "temp"));

        Assert.True(result.Succeeded);
        Assert.True(credentialService.ValidateCredential("operator1", "temp"));
        Assert.False(credentialService.ValidateCredential("operator1", "operator1"));
        Assert.Contains(repository.Users.Single(user => user.UserName == "operator1").Roles, role => role.Name == RoleNames.Operator);
    }

    [Fact]
    public void UpdateSettings_SavesGlobalAlertRuleSet()
    {
        var repository = new InMemorySpcRepository();
        var service = new SetupManagementService(repository);

        var result = service.UpdateSettings(new UpdateSettingsRequest("Cusum"));

        Assert.True(result.Succeeded);
        Assert.Equal("Cusum", repository.Settings.GlobalAlertRuleSet);
    }

    [Fact]
    public void UpdateSettings_SavesCapabilityThresholds()
    {
        var repository = new InMemorySpcRepository();
        var service = new SetupManagementService(repository);

        var result = service.UpdateSettings(new UpdateSettingsRequest(
            "WesternElectric",
            CapabilityThresholds: new CapabilityThresholdSetupDto(1.10m, 1.50m)));

        Assert.True(result.Succeeded);
        Assert.Equal(1.10m, repository.Settings.CapabilityThresholds.YellowMinimum);
        Assert.Equal(1.50m, repository.Settings.CapabilityThresholds.GreenMinimum);
    }

    [Fact]
    public void UpdateSettings_RejectsCapabilityThresholdsWhenGreenIsNotHigherThanYellow()
    {
        var repository = new InMemorySpcRepository();
        var service = new SetupManagementService(repository);

        var result = service.UpdateSettings(new UpdateSettingsRequest(
            "WesternElectric",
            CapabilityThresholds: new CapabilityThresholdSetupDto(1.33m, 1.00m)));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("green minimum", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UpsertInspectionSetup_CreatesPartOperationCharacteristicAndLimits()
    {
        var repository = new InMemorySpcRepository();
        var service = new SetupManagementService(repository);

        var result = service.UpsertInspectionSetup(new UpsertInspectionSetupRequest(
            "P200",
            "Customer part",
            "General",
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
            "WesternElectric"));

        Assert.True(result.Succeeded);
        Assert.Single(repository.Parts);
        Assert.Equal("General", repository.Parts.Single().ProductGroup);
        Assert.Single(repository.Operations);
        Assert.Single(repository.Characteristics);
        Assert.Single(repository.InspectionPlans);
        var controlLimit = Assert.Single(repository.ControlLimits);
        Assert.Equal(2.2m, controlLimit.Lcl);
        Assert.Equal(2.8m, controlLimit.Ucl);
    }

    [Fact]
    public void UpsertInspectionSetup_AllowsDifferentRequirementsByInspectionPhase()
    {
        var repository = new InMemorySpcRepository();
        var service = new SetupManagementService(repository);

        Assert.True(service.UpsertInspectionSetup(Request(
            processCode: "MOLD",
            characteristicName: "Diameter",
            inspectionPhase: "Startup")).Succeeded);
        Assert.True(service.UpsertInspectionSetup(Request(
            processCode: "MOLD",
            characteristicName: "Diameter",
            inspectionPhase: "Setup")).Succeeded);
        Assert.True(service.UpsertInspectionSetup(Request(
            processCode: "MOLD",
            characteristicName: "Diameter",
            inspectionPhase: "In Process")).Succeeded);
        Assert.True(service.UpsertInspectionSetup(Request(
            processCode: "MOLD",
            characteristicName: "Diameter",
            inspectionPhase: "Spool")).Succeeded);

        var plans = new SetupQueryService(repository).GetInspectionPlans("P200");

        Assert.Equal(4, plans.Count);
        Assert.Contains(plans, plan => plan.InspectionPhase == "Startup");
        Assert.Contains(plans, plan => plan.InspectionPhase == "Setup");
        Assert.Contains(plans, plan => plan.InspectionPhase == "In Process");
        Assert.Contains(plans, plan => plan.InspectionPhase == "Spool");
    }

    [Fact]
    public void UpsertInspectionSetup_NormalizesSetUpToSetup()
    {
        var repository = new InMemorySpcRepository();
        var service = new SetupManagementService(repository);

        var result = service.UpsertInspectionSetup(Request(
            processCode: "MOLD",
            characteristicName: "Diameter",
            inspectionPhase: "Set Up"));

        Assert.True(result.Succeeded);
        Assert.Equal("Setup", repository.InspectionPlans.Single().InspectionPhase);
    }

    [Fact]
    public void UpsertPartJobDataField_SavesFieldForPartAndPhase()
    {
        var repository = new InMemorySpcRepository();
        var service = new SetupManagementService(repository);
        Assert.True(service.UpsertInspectionSetup(Request("MOLD", "Diameter")).Succeeded);

        var result = service.UpsertPartJobDataField(new UpsertPartJobDataFieldRequest("P200", "Spool", "Coil Number", true, 0));

        Assert.True(result.Succeeded);
        var field = Assert.Single(repository.PartJobDataFields);
        Assert.Equal("Coil Number", field.FieldName);
        Assert.Equal("Spool", field.InspectionPhase);
        var snapshot = new SetupQueryService(repository).GetSetupSnapshot();
        Assert.Contains(snapshot.PartJobDataFields, item => item.PartNum == "P200" && item.FieldName == "Coil Number");
    }

    [Fact]
    public void UpsertPartMaterialField_SavesMaterialForPartAndPhase()
    {
        var repository = new InMemorySpcRepository();
        var service = new SetupManagementService(repository);
        Assert.True(service.UpsertInspectionSetup(Request("MOLD", "Diameter")).Succeeded);

        var result = service.UpsertPartMaterialField(new UpsertPartMaterialFieldRequest("P200", "Startup", "Wire", "WIRE-302", "302 stainless wire", true, 0));

        Assert.True(result.Succeeded);
        var field = Assert.Single(repository.PartMaterialFields);
        Assert.Equal("Wire", field.MaterialName);
        Assert.Equal("WIRE-302", field.MaterialPartNum);
        Assert.Equal("302 stainless wire", field.MaterialDescription);
        Assert.Equal("Startup", field.InspectionPhase);
        var snapshot = new SetupQueryService(repository).GetSetupSnapshot();
        Assert.Contains(snapshot.PartMaterialFields, item => item.PartNum == "P200" && item.MaterialPartNum == "WIRE-302" && item.MaterialDescription == "302 stainless wire");
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

    [Fact]
    public void UpsertResource_AddsMachineForOperatorSelection()
    {
        var repository = new InMemorySpcRepository();
        var service = new SetupManagementService(repository);

        var result = service.UpsertResource(new UpsertResourceMachineRequest("NM-10", "Needlemaker 10"));

        Assert.True(result.Succeeded);
        Assert.Contains(service.GetResources(), resource => resource.ResourceId == "NM-10" && resource.Description == "Needlemaker 10");
    }

    [Fact]
    public void DeleteResource_BlocksMachineWithInspectionHistory()
    {
        var repository = new InMemorySpcRepository();
        repository.Resources.Add(new ResourceMachine { ResourceId = "PRESS1", Description = "Main press" });
        repository.Measurements.Add(new InspectionMeasurement
        {
            JobNum = "J100",
            PartNum = "P100",
            ProcessCode = "General Production",
            OperationSeq = 10,
            ResourceId = "PRESS1",
            CharacteristicName = "Diameter",
            Value = 1m,
            Timestamp = DateTimeOffset.UtcNow,
            OperatorUserId = "operator1"
        });
        var service = new SetupManagementService(repository);

        var result = service.DeleteResource("PRESS1");

        Assert.False(result.Succeeded);
        Assert.Contains(repository.Resources, resource => resource.ResourceId == "PRESS1");
    }

    [Fact]
    public void ImportResourcesCsv_AddsMachinesFromTemplateColumns()
    {
        var repository = new InMemorySpcRepository();
        var service = new SetupManagementService(repository);

        var result = service.ImportResourcesCsv(string.Join(Environment.NewLine, [
            "Machine ID,Description",
            "ETH-1,Needle Maker #1",
            "GP-1,GRM 50 Hook Machine"
        ]));

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Value!.Imported);
        Assert.Contains(repository.Resources, resource => resource.ResourceId == "ETH-1" && resource.Description == "Needle Maker #1");
        Assert.Contains(repository.Resources, resource => resource.ResourceId == "GP-1" && resource.Description == "GRM 50 Hook Machine");
    }

    [Fact]
    public void ImportResourcesCsv_RejectsDuplicateMachinesInSameFile()
    {
        var repository = new InMemorySpcRepository();
        var service = new SetupManagementService(repository);

        var result = service.ImportResourcesCsv(string.Join(Environment.NewLine, [
            "Machine ID,Description",
            "ETH-1,Needle Maker #1",
            "ETH-1,Duplicate"
        ]));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("Duplicate machine ETH-1", StringComparison.OrdinalIgnoreCase));
    }

    private static UpsertInspectionSetupRequest Request(
        string processCode,
        string characteristicName,
        string? originalProcessCode = null,
        string? originalCharacteristicName = null,
        string inspectionPhase = "In Process")
    {
        return new UpsertInspectionSetupRequest(
            "P200",
            "Customer part",
            "General",
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
            originalProcessCode,
            10,
            originalCharacteristicName,
            inspectionPhase);
    }
}

