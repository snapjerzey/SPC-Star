using System.Text.Json;
using System.Text.Json.Serialization;
using SPCStar.Core.Domain;

namespace SPCStar.Core.Infrastructure;

public sealed class FileBackedSpcRepository : InMemorySpcRepository, IRepositoryPersistence
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public FileBackedSpcRepository(string storagePath)
    {
        StoragePath = storagePath;
        Load();
    }

    public string StoragePath { get; }

    public void SaveChanges()
    {
        var directory = Path.GetDirectoryName(StoragePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var snapshot = RepositorySnapshot.FromRepository(this);
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        var tempPath = $"{StoragePath}.tmp";
        File.WriteAllText(tempPath, json);

        if (File.Exists(StoragePath))
        {
            File.Replace(tempPath, StoragePath, null);
            return;
        }

        File.Move(tempPath, StoragePath);
    }

    private void Load()
    {
        if (!File.Exists(StoragePath))
        {
            return;
        }

        var json = File.ReadAllText(StoragePath);
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        var snapshot = JsonSerializer.Deserialize<RepositorySnapshot>(json, JsonOptions);
        snapshot?.CopyTo(this);
    }

    private sealed record RepositorySnapshot(
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
        List<ControlLimitSet> ControlLimits,
        List<ProcessAlert> Alerts,
        List<RuleViolation> RuleViolations,
        List<AlertOverride> AlertOverrides,
        List<MaterialChangeLog> MaterialChanges)
    {
        public static RepositorySnapshot FromRepository(ISpcRepository repository)
        {
            return new RepositorySnapshot(
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
                [.. repository.ControlLimits],
                [.. repository.Alerts],
                [.. repository.RuleViolations],
                [.. repository.AlertOverrides],
                [.. repository.MaterialChanges]);
        }

        public void CopyTo(ISpcRepository repository)
        {
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
            repository.ControlLimits.AddRange(ControlLimits);
            repository.Alerts.AddRange(Alerts);
            repository.RuleViolations.AddRange(RuleViolations);
            repository.AlertOverrides.AddRange(AlertOverrides);
            repository.MaterialChanges.AddRange(MaterialChanges);
        }
    }

    private sealed record PersistedRole(Guid Id, string Name, List<string> Permissions)
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

    private sealed record PersistedUser(
        Guid Id,
        string UserName,
        string PasswordHash,
        string PasswordSalt,
        List<string> RoleNames)
    {
        public static PersistedUser FromUser(User user)
        {
            return new PersistedUser(
                user.Id,
                user.UserName,
                user.PasswordHash,
                user.PasswordSalt,
                user.Roles.Select(role => role.Name).ToList());
        }

        public User ToUser(IReadOnlyCollection<Role> roles)
        {
            var user = new User
            {
                Id = Id,
                UserName = UserName,
                PasswordHash = PasswordHash,
                PasswordSalt = PasswordSalt
            };

            foreach (var roleName in RoleNames)
            {
                var role = roles.FirstOrDefault(item => item.Name.Equals(roleName, StringComparison.OrdinalIgnoreCase));
                if (role is not null)
                {
                    user.Roles.Add(role);
                }
            }

            return user;
        }
    }
}
