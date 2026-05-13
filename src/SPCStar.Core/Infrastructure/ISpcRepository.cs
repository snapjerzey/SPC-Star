using SPCStar.Core.Domain;

namespace SPCStar.Core.Infrastructure;

public interface ISpcRepository
{
    AppSettings Settings { get; }
    List<User> Users { get; }
    List<Role> Roles { get; }
    List<Part> Parts { get; }
    List<ManufacturingProcess> Processes { get; }
    List<Operation> Operations { get; }
    List<Characteristic> Characteristics { get; }
    List<SpecLimit> SpecLimits { get; }
    List<InspectionPlan> InspectionPlans { get; }
    List<Job> Jobs { get; }
    List<ResourceMachine> Resources { get; }
    List<Device> Devices { get; }
    List<InspectionMeasurement> Measurements { get; }
    List<JobNote> JobNotes { get; }
    List<JobTag> JobTags { get; }
    List<PartJobDataField> PartJobDataFields { get; }
    List<ControlLimitSet> ControlLimits { get; }
    List<ProcessAlert> Alerts { get; }
    List<RuleViolation> RuleViolations { get; }
    List<AlertOverride> AlertOverrides { get; }
    List<MaterialChangeLog> MaterialChanges { get; }
}
