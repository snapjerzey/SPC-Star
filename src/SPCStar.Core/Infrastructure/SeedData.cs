using SPCStar.Core.Domain;

namespace SPCStar.Core.Infrastructure;

public static class SeedData
{
    public static void SeedAll(ISpcRepository repository)
    {
        SeedSecurity(repository);
        SeedSampleInspectionPlans(repository);
    }

    public static void SeedSecurity(ISpcRepository repository)
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

    public static void SeedSampleInspectionPlans(ISpcRepository repository)
    {
        if (repository.Parts.Any(part => part.PartNum.Equals("P100", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var part = new Part { PartNum = "P100", Description = "Sample molded widget" };
        var process = new ManufacturingProcess { ProcessCode = "MOLD", Description = "Injection molding" };
        var operation = new Operation { PartId = part.Id, ProcessId = process.Id, OperationSeq = 10 };
        var diameter = new Characteristic
        {
            OperationId = operation.Id,
            Name = "Diameter",
            Type = CharacteristicType.Variable,
            UnitOfMeasure = "mm",
            IsRequiredForCoa = true
        };
        var length = new Characteristic
        {
            OperationId = operation.Id,
            Name = "Length",
            Type = CharacteristicType.Variable,
            UnitOfMeasure = "mm",
            IsRequiredForCoa = true
        };
        var weight = new Characteristic
        {
            OperationId = operation.Id,
            Name = "Weight",
            Type = CharacteristicType.Variable,
            UnitOfMeasure = "g",
            IsRequiredForCoa = true
        };

        repository.Parts.Add(part);
        repository.Processes.Add(process);
        repository.Operations.Add(operation);
        repository.Characteristics.AddRange([diameter, length, weight]);
        repository.Jobs.Add(new Job { JobNum = "J100", PartNum = part.PartNum });
        repository.Resources.Add(new ResourceMachine { ResourceId = "PRESS1", Description = "Demo press" });
        AddVariablePlan(repository, part, process, operation, diameter, 5m, 4.5m, 5.5m, 4m, 6m);
        AddVariablePlan(repository, part, process, operation, length, 42m, 41.5m, 42.5m, 41m, 43m);
        AddVariablePlan(repository, part, process, operation, weight, 18m, 17.2m, 18.8m, 16.8m, 19.2m);
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

    private static void AddVariablePlan(
        ISpcRepository repository,
        Part part,
        ManufacturingProcess process,
        Operation operation,
        Characteristic characteristic,
        decimal nominal,
        decimal lsl,
        decimal usl,
        decimal lcl,
        decimal ucl)
    {
        repository.SpecLimits.Add(new SpecLimit { CharacteristicId = characteristic.Id, Nominal = nominal, Lsl = lsl, Usl = usl });
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
            CenterLine = nominal,
            Lcl = lcl,
            Ucl = ucl
        });
    }
}
