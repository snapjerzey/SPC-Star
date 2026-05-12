using SPCStar.Core.Domain;

namespace SPCStar.Core.Infrastructure;

public class InMemorySpcRepository : ISpcRepository
{
    public AppSettings Settings { get; } = new();
    public List<User> Users { get; } = [];
    public List<Role> Roles { get; } = [];
    public List<Part> Parts { get; } = [];
    public List<ManufacturingProcess> Processes { get; } = [];
    public List<Operation> Operations { get; } = [];
    public List<Characteristic> Characteristics { get; } = [];
    public List<SpecLimit> SpecLimits { get; } = [];
    public List<InspectionPlan> InspectionPlans { get; } = [];
    public List<Job> Jobs { get; } = [];
    public List<ResourceMachine> Resources { get; } = [];
    public List<Device> Devices { get; } = [];
    public List<InspectionMeasurement> Measurements { get; } = [];
    public List<ControlLimitSet> ControlLimits { get; } = [];
    public List<ProcessAlert> Alerts { get; } = [];
    public List<RuleViolation> RuleViolations { get; } = [];
    public List<AlertOverride> AlertOverrides { get; } = [];
    public List<MaterialChangeLog> MaterialChanges { get; } = [];
}
