using SPCStar.Core.Domain;

namespace SPCStar.Core.Infrastructure;

internal sealed record RepositorySnapshot(
    AppSettings? Settings,
    List<PersistedUser> Users,
    List<PersistedRole> Roles,
    List<Part> Parts,
    List<ManufacturingProcess> Processes,
    List<Operation> Operations,
    List<Characteristic> Characteristics,
    List<SpecLimit> SpecLimits,
    List<InspectionPlan> InspectionPlans,
    List<Job> Jobs,
    List<ResourceMachine> Resources,
    List<Device> Devices,
    List<InspectionMeasurement> Measurements,
    List<MeasurementEditAudit>? MeasurementEditAudits,
    List<JobNote>? JobNotes,
    List<JobTag>? JobTags,
    List<PartJobDataField>? PartJobDataFields,
    List<PartMaterialField>? PartMaterialFields,
    List<ControlLimitSet> ControlLimits,
    List<ProcessAlert> Alerts,
    List<RuleViolation> RuleViolations,
    List<AlertOverride> AlertOverrides,
    List<MaterialChangeLog> MaterialChanges)
{
    public static RepositorySnapshot FromRepository(ISpcRepository repository)
    {
        return new RepositorySnapshot(
            repository.Settings,
            repository.Users.Select(PersistedUser.FromUser).ToList(),
            repository.Roles.Select(PersistedRole.FromRole).ToList(),
            [.. repository.Parts],
            [.. repository.Processes],
            [.. repository.Operations],
            [.. repository.Characteristics],
            [.. repository.SpecLimits],
            [.. repository.InspectionPlans],
            [.. repository.Jobs],
            [.. repository.Resources],
            [.. repository.Devices],
            [.. repository.Measurements],
            [.. repository.MeasurementEditAudits],
            [.. repository.JobNotes],
            [.. repository.JobTags],
            [.. repository.PartJobDataFields],
            [.. repository.PartMaterialFields],
            [.. repository.ControlLimits],
            [.. repository.Alerts],
            [.. repository.RuleViolations],
            [.. repository.AlertOverrides],
            [.. repository.MaterialChanges]);
    }

    public void CopyTo(ISpcRepository repository)
    {
        repository.Settings.GlobalAlertRuleSet = Settings?.GlobalAlertRuleSet ?? "WesternElectric";
        repository.Settings.CustomDriftRule = Settings?.CustomDriftRule ?? new CustomDriftRuleSettings();
        repository.Settings.CapabilityThresholds = Settings?.CapabilityThresholds ?? new CapabilityThresholdSettings();
        repository.Roles.AddRange(Roles.Select(role => role.ToRole()));
        foreach (var user in Users)
        {
            repository.Users.Add(user.ToUser(repository.Roles));
        }

        repository.Parts.AddRange(Parts);
        repository.Processes.AddRange(Processes);
        repository.Operations.AddRange(Operations);
        repository.Characteristics.AddRange(Characteristics);
        repository.SpecLimits.AddRange(SpecLimits);
        repository.InspectionPlans.AddRange(InspectionPlans);
        repository.Jobs.AddRange(Jobs);
        repository.Resources.AddRange(Resources);
        repository.Devices.AddRange(Devices);
        repository.Measurements.AddRange(Measurements);
        repository.MeasurementEditAudits.AddRange(MeasurementEditAudits ?? []);
        repository.JobNotes.AddRange(JobNotes ?? []);
        repository.JobTags.AddRange(JobTags ?? []);
        repository.PartJobDataFields.AddRange(PartJobDataFields ?? []);
        repository.PartMaterialFields.AddRange(PartMaterialFields ?? []);
        repository.ControlLimits.AddRange(ControlLimits);
        repository.Alerts.AddRange(Alerts);
        repository.RuleViolations.AddRange(RuleViolations);
        repository.AlertOverrides.AddRange(AlertOverrides);
        repository.MaterialChanges.AddRange(MaterialChanges);
    }
}

internal sealed record PersistedRole(Guid Id, string Name, List<string> Permissions)
{
    public static PersistedRole FromRole(Role role)
    {
        return new PersistedRole(role.Id, role.Name, [.. role.Permissions]);
    }

    public Role ToRole()
    {
        var role = new Role { Id = Id, Name = Name };
        foreach (var permission in Permissions)
        {
            role.Permissions.Add(permission);
        }

        return role;
    }
}

internal sealed record PersistedUser(
    Guid Id,
    string UserName,
    string PasswordHash,
    string PasswordSalt,
    List<string> RoleNames,
    List<string>? ProductGroups,
    string? Shift)
{
    public static PersistedUser FromUser(User user)
    {
        return new PersistedUser(
            user.Id,
            user.UserName,
            user.PasswordHash,
            user.PasswordSalt,
            user.Roles.Select(role => role.Name).ToList(),
            [.. user.ProductGroups],
            user.Shift);
    }

    public User ToUser(IReadOnlyCollection<Role> roles)
    {
        var user = new User
        {
            Id = Id,
            UserName = UserName,
            PasswordHash = PasswordHash,
            PasswordSalt = PasswordSalt,
            Shift = string.IsNullOrWhiteSpace(Shift) ? "" : Shift.Trim()
        };

        foreach (var roleName in RoleNames)
        {
            var role = roles.FirstOrDefault(item => item.Name.Equals(roleName, StringComparison.OrdinalIgnoreCase));
            if (role is not null)
            {
                user.Roles.Add(role);
            }
        }

        foreach (var group in ProductGroups ?? [])
        {
            if (!string.IsNullOrWhiteSpace(group) && !user.ProductGroups.Contains(group.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                user.ProductGroups.Add(group.Trim());
            }
        }

        return user;
    }
}
