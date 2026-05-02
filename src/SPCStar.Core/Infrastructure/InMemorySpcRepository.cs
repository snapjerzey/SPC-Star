using SPCStar.Core.Domain;

namespace SPCStar.Core.Infrastructure;

public sealed class InMemorySpcRepository : ISpcRepository
{
    public List<User> Users { get; } = [];
    public List<Role> Roles { get; } = [];
    public List<Part> Parts { get; } = [];
    public List<ManufacturingProcess> Processes { get; } = [];
    public List<Operation> Operations { get; } = [];
    public List<Characteristic> Characteristics { get; } = [];
    public List<SpecLimit> SpecLimits { get; } = [];
    public List<InspectionPlan> InspectionPlans { get; } = [];
    public List<InspectionMeasurement> Measurements { get; } = [];
    public List<ControlLimitSet> ControlLimits { get; } = [];
    public List<ProcessAlert> Alerts { get; } = [];
    public List<RuleViolation> RuleViolations { get; } = [];
    public List<AlertOverride> AlertOverrides { get; } = [];
    public List<MaterialChangeLog> MaterialChanges { get; } = [];
}
