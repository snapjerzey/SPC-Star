using SPCStar.Core.Domain;

namespace SPCStar.Core.Infrastructure;

public static class SeedData
{
    public static void SeedAll(InMemorySpcRepository repository)
    {
        SeedSecurity(repository);
        SeedSampleInspectionPlans(repository);
    }

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
        repository.Users.Add(User("operator1", "operator1", operatorRole));
        repository.Users.Add(User("linetech1", "linetech1", lineTech));
        repository.Users.Add(User("qa1", "qa1", qa));
        repository.Users.Add(User("admin1", "admin1", admin));
        repository.Users.Add(User("god1", "god1", god));
    }

    public static void SeedSampleInspectionPlans(InMemorySpcRepository repository)
    {
        if (repository.Parts.Any(part => part.PartNum.Equals("P100", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var part = new Part { PartNum = "P100", Description = "Sample molded widget" };
        var process = new ManufacturingProcess { ProcessCode = "MOLD", Description = "Injection molding" };
        var operation = new Operation { PartId = part.Id, ProcessId = process.Id, OperationSeq = 10 };
        var characteristic = new Characteristic
        {
            OperationId = operation.Id,
            Name = "Diameter",
            Type = CharacteristicType.Variable,
            UnitOfMeasure = "mm",
            IsRequiredForCoa = true
        };

        repository.Parts.Add(part);
        repository.Processes.Add(process);
        repository.Operations.Add(operation);
        repository.Characteristics.Add(characteristic);
        repository.SpecLimits.Add(new SpecLimit { CharacteristicId = characteristic.Id, Nominal = 5m, Lsl = 4.5m, Usl = 5.5m });
        repository.InspectionPlans.Add(new InspectionPlan
        {
            CharacteristicId = characteristic.Id,
            SampleSize = 1,
            AlertRuleSet = "WesternElectric",
            Frequency = new InspectionFrequency { Type = FrequencyType.Time, Value = 30, Unit = FrequencyUnit.Minutes }
        });
        repository.ControlLimits.Add(new ControlLimitSet
        {
            PartNum = part.PartNum,
            ProcessCode = process.ProcessCode,
            OperationSeq = operation.OperationSeq,
            CharacteristicName = characteristic.Name,
            CenterLine = 5m,
            Lcl = 4m,
            Ucl = 6m
        });
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

    private static User User(string userName, string password, Role role)
    {
        var (hash, salt) = Services.PasswordHasher.HashPassword(password);
        return new User { UserName = userName, PasswordHash = hash, PasswordSalt = salt, Roles = { role } };
    }
}
