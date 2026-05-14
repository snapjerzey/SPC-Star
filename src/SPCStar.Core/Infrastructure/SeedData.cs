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
        var operatorRole = UpsertRole(repository, RoleNames.Operator, PermissionNames.CanEnterInspections);
        var lineTech = UpsertRole(
            repository,
            RoleNames.LineTech,
            PermissionNames.CanEnterInspections,
            PermissionNames.CanOverrideDriftLock);
        var qa = UpsertRole(repository, RoleNames.QA, PermissionNames.CanOverrideDriftLock, PermissionNames.CanExportQAData);
        var admin = UpsertRole(
            repository,
            RoleNames.Admin,
            PermissionNames.CanEnterInspections,
            PermissionNames.CanManageInspectionPlans,
            PermissionNames.CanImportSetupData,
            PermissionNames.CanOverrideDriftLock,
            PermissionNames.CanManageUsers);
        var god = UpsertRole(
            repository,
            RoleNames.GOD,
            PermissionNames.CanEnterInspections,
            PermissionNames.CanOverrideDriftLock,
            PermissionNames.CanManageInspectionPlans,
            PermissionNames.CanImportSetupData,
            PermissionNames.CanExportQAData,
            PermissionNames.CanManageUsers,
            PermissionNames.CanUseGodMode);

        AddDefaultUser(repository, "operator1", "operator1", operatorRole, "General");
        AddDefaultUser(repository, "linetech1", "linetech1", lineTech, "General");
        AddDefaultUser(repository, "qa1", "qa1", qa);
        AddDefaultUser(repository, "admin1", "admin1", admin);
        AddDefaultUser(repository, "god1", "god1", god);
    }

    public static void SeedSampleInspectionPlans(ISpcRepository repository)
    {
        if (repository.Parts.Any(part => part.PartNum.Equals("P100", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var part = new Part { PartNum = "P100", Description = "Sample molded widget", ProductGroup = "General" };
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

    private static Role UpsertRole(ISpcRepository repository, string name, params string[] permissions)
    {
        var role = repository.Roles.FirstOrDefault(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (role is null)
        {
            role = new Role { Name = name };
            repository.Roles.Add(role);
        }

        foreach (var permission in permissions)
        {
            role.Permissions.Add(permission);
        }

        return role;
    }

    private static void AddDefaultUser(ISpcRepository repository, string userName, string password, Role role, params string[] productGroups)
    {
        var existing = repository.Users.FirstOrDefault(user => user.UserName.Equals(userName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            foreach (var group in productGroups.Where(group => !string.IsNullOrWhiteSpace(group)))
            {
                if (!existing.ProductGroups.Contains(group, StringComparer.OrdinalIgnoreCase))
                {
                    existing.ProductGroups.Add(group);
                }
            }
            return;
        }

        repository.Users.Add(User(userName, password, role, productGroups));
    }

    private static User User(string userName, string password, Role role, params string[] productGroups)
    {
        var (hash, salt) = Services.PasswordHasher.HashPassword(password);
        var user = new User { UserName = userName, PasswordHash = hash, PasswordSalt = salt, Roles = { role } };
        user.ProductGroups.AddRange(productGroups.Where(group => !string.IsNullOrWhiteSpace(group)).Distinct(StringComparer.OrdinalIgnoreCase));
        return user;
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
