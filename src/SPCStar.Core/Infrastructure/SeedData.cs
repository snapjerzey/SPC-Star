using SPCStar.Core.Domain;

namespace SPCStar.Core.Infrastructure;

public static class SeedData
{
    public static void SeedAll(ISpcRepository repository)
    {
        SeedSecurity(repository);
        NormalizeLegacyProductGroups(repository);
    }

    public static void SeedSecurity(ISpcRepository repository)
    {
        var operatorRole = UpsertRole(repository, RoleNames.Operator, PermissionNames.CanEnterInspections);
        var lineTech = UpsertRole(
            repository,
            RoleNames.LineTech,
            PermissionNames.CanEnterInspections,
            PermissionNames.CanOverrideDriftLock);
        var qa = UpsertRole(
            repository,
            RoleNames.QA,
            PermissionNames.CanEnterInspections,
            PermissionNames.CanOverrideDriftLock,
            PermissionNames.CanManageInspectionPlans,
            PermissionNames.CanExportQAData,
            PermissionNames.CanManageUsers);
        qa.Permissions.Remove(PermissionNames.CanImportSetupData);
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

        MigrateAdminRoleToQa(repository, qa);
        AddDefaultUser(repository, "Archon", "archon", god);
    }

    public static void SeedSampleInspectionPlans(ISpcRepository repository)
    {
        if (repository.Parts.Any(part => part.PartNum.Equals("P100", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var part = new Part { PartNum = "P100", Description = "Sample molded widget", ProductGroup = "General Production" };
        var process = new ManufacturingProcess { ProcessCode = "MOLD", Description = "Injection molding" };
        var operation = new Operation { PartId = part.Id, ProcessId = process.Id, OperationSeq = 10 };
        var diameter = new Characteristic
        {
            OperationId = operation.Id,
            Name = "Diameter",
            Type = CharacteristicType.Variable,
            UnitOfMeasure = "mm",
        };
        var length = new Characteristic
        {
            OperationId = operation.Id,
            Name = "Length",
            Type = CharacteristicType.Variable,
            UnitOfMeasure = "mm",
        };
        var weight = new Characteristic
        {
            OperationId = operation.Id,
            Name = "Weight",
            Type = CharacteristicType.Variable,
            UnitOfMeasure = "g",
        };

        repository.Parts.Add(part);
        repository.Processes.Add(process);
        repository.Operations.Add(operation);
        repository.Characteristics.AddRange([diameter, length, weight]);
        repository.Jobs.Add(new Job { JobNum = "J100", PartNum = part.PartNum });
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

    private static void MigrateAdminRoleToQa(ISpcRepository repository, Role qa)
    {
        var adminRoles = repository.Roles
            .Where(role => role.Name.Equals("Admin", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var user in repository.Users)
        {
            if (!user.Roles.Any(role => role.Name.Equals("Admin", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            user.Roles.RemoveAll(role => role.Name.Equals("Admin", StringComparison.OrdinalIgnoreCase));
            if (!user.Roles.Any(role => role.Name.Equals(RoleNames.QA, StringComparison.OrdinalIgnoreCase)))
            {
                user.Roles.Add(qa);
            }
        }

        foreach (var adminRole in adminRoles)
        {
            repository.Roles.Remove(adminRole);
        }
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

    private static void NormalizeLegacyProductGroups(ISpcRepository repository)
    {
        foreach (var part in repository.Parts)
        {
            part.ProductGroup = NormalizeProductGroup(part.ProductGroup);
        }

        var drillingProcessIds = repository.Processes
            .Where(process => ContainsDrill(process.ProcessCode) || ContainsDrill(process.Description))
            .Select(process => process.Id)
            .ToHashSet();
        var drilledPartIds = repository.Operations
            .Where(operation => drillingProcessIds.Contains(operation.ProcessId))
            .Select(operation => operation.PartId)
            .ToHashSet();

        foreach (var part in repository.Parts.Where(part => drilledPartIds.Contains(part.Id)))
        {
            if (part.ProductGroup.Equals("Ethicon Cutting Edge - Needles", StringComparison.OrdinalIgnoreCase))
            {
                part.ProductGroup = "Ethicon Cutting Edge - Drilled";
            }
            else if (part.ProductGroup.Equals("Ethicon Taperpoint - Needles", StringComparison.OrdinalIgnoreCase))
            {
                part.ProductGroup = "Ethicon Taperpoint - Drilled";
            }
        }

        foreach (var user in repository.Users)
        {
            NormalizeProductGroupList(user.ProductGroups);
        }

        foreach (var resource in repository.Resources)
        {
            NormalizeProductGroupList(resource.ProductGroups);
        }
    }

    private static bool ContainsDrill(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Contains("Drill", StringComparison.OrdinalIgnoreCase);

    private static void NormalizeProductGroupList(List<string> groups)
    {
        var normalized = groups
            .Select(NormalizeProductGroup)
            .Where(group => !string.IsNullOrWhiteSpace(group))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        groups.Clear();
        groups.AddRange(normalized);
    }

    private static string NormalizeProductGroup(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "General Production";
        }

        var trimmed = value.Trim();
        return trimmed switch
        {
            "General" => "General Production",
            "Ethicon Cutting Edge - Driller" => "Ethicon Cutting Edge - Drilled",
            "Ethicon Taperpoint - Driller" => "Ethicon Taperpoint - Drilled",
            "Ethicon Ethalloy Cardio" => "Ethicon Ethalloy Cardio - Needles",
            "Ethicon Everpoint" => "Ethicon Everpoint - Needles",
            _ => trimmed
        };
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
