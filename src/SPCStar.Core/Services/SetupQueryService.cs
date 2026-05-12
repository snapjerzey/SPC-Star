using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using System.Security.Cryptography;
using System.Text;

namespace SPCStar.Core.Services;

public sealed record PartSetupDto(string PartNum, string Description);

public sealed record InspectionPlanSetupDto(
    string PartNum,
    string PartDescription,
    string ProcessCode,
    string ProcessDescription,
    int OperationSeq,
    string CharacteristicName,
    CharacteristicType CharacteristicType,
    decimal Nominal,
    decimal Lsl,
    decimal Usl,
    string UnitOfMeasure,
    int SampleSize,
    FrequencyType FrequencyType,
    int FrequencyValue,
    FrequencyUnit FrequencyUnit,
    string AlertRuleSet,
    bool IsRequiredForCoa,
    CoaStatisticType CoaStatisticType);

public sealed record ProcessSetupDto(Guid Id, string ProcessCode, string Description);

public sealed record OperationSetupDto(Guid Id, Guid PartId, Guid ProcessId, int OperationSeq);

public sealed record CharacteristicSetupDto(
    Guid Id,
    Guid OperationId,
    string Name,
    CharacteristicType Type,
    string UnitOfMeasure,
    bool IsRequiredForCoa,
    CoaStatisticType CoaStatisticType);

public sealed record SpecLimitSetupDto(Guid CharacteristicId, decimal Nominal, decimal Lsl, decimal Usl);

public sealed record ControlLimitSetupDto(
    string PartNum,
    string ProcessCode,
    int OperationSeq,
    string CharacteristicName,
    decimal CenterLine,
    decimal Lcl,
    decimal Ucl);

public sealed record JobSetupDto(string JobNum, string PartNum);

public sealed record ResourceSetupDto(string ResourceId, string? Description);

public sealed record SettingsSetupDto(string GlobalAlertRuleSet);

public sealed record SetupSnapshotDto(
    DateTimeOffset GeneratedAt,
    string SetupVersion,
    SettingsSetupDto Settings,
    IReadOnlyList<PartSetupDto> Parts,
    IReadOnlyList<ProcessSetupDto> Processes,
    IReadOnlyList<OperationSetupDto> Operations,
    IReadOnlyList<CharacteristicSetupDto> Characteristics,
    IReadOnlyList<SpecLimitSetupDto> SpecLimits,
    IReadOnlyList<InspectionPlanSetupDto> InspectionPlans,
    IReadOnlyList<ControlLimitSetupDto> ControlLimits,
    IReadOnlyList<JobSetupDto> Jobs,
    IReadOnlyList<ResourceSetupDto> Resources);

public sealed class SetupQueryService(ISpcRepository repository)
{
    public IReadOnlyList<PartSetupDto> GetParts()
    {
        return repository.Parts
            .OrderBy(part => part.PartNum)
            .Select(part => new PartSetupDto(part.PartNum, part.Description))
            .ToArray();
    }

    public IReadOnlyList<InspectionPlanSetupDto> GetInspectionPlans(string? partNum = null)
    {
        var query =
            from part in repository.Parts
            join operation in repository.Operations on part.Id equals operation.PartId
            join process in repository.Processes on operation.ProcessId equals process.Id
            join characteristic in repository.Characteristics on operation.Id equals characteristic.OperationId
            join spec in repository.SpecLimits on characteristic.Id equals spec.CharacteristicId
            join plan in repository.InspectionPlans on characteristic.Id equals plan.CharacteristicId
            where string.IsNullOrWhiteSpace(partNum) || part.PartNum.Equals(partNum, StringComparison.OrdinalIgnoreCase)
            orderby part.PartNum, operation.OperationSeq, characteristic.Name
            select new InspectionPlanSetupDto(
                part.PartNum,
                part.Description,
                process.ProcessCode,
                process.Description,
                operation.OperationSeq,
                characteristic.Name,
                characteristic.Type,
                spec.Nominal,
                spec.Lsl,
                spec.Usl,
                characteristic.UnitOfMeasure,
                plan.SampleSize,
                plan.Frequency.Type,
                plan.Frequency.Value,
                plan.Frequency.Unit,
                plan.AlertRuleSet,
                characteristic.IsRequiredForCoa,
                characteristic.CoaStatisticType);

        return query.ToArray();
    }

    public SetupSnapshotDto GetSetupSnapshot(DateTimeOffset? generatedAt = null)
    {
        var parts = GetParts();
        var processes = repository.Processes
            .OrderBy(process => process.ProcessCode)
            .Select(process => new ProcessSetupDto(process.Id, process.ProcessCode, process.Description))
            .ToArray();
        var operations = repository.Operations
            .OrderBy(operation => operation.PartId)
            .ThenBy(operation => operation.ProcessId)
            .ThenBy(operation => operation.OperationSeq)
            .Select(operation => new OperationSetupDto(operation.Id, operation.PartId, operation.ProcessId, operation.OperationSeq))
            .ToArray();
        var characteristics = repository.Characteristics
            .OrderBy(characteristic => characteristic.OperationId)
            .ThenBy(characteristic => characteristic.Name)
            .Select(characteristic => new CharacteristicSetupDto(
                characteristic.Id,
                characteristic.OperationId,
                characteristic.Name,
                characteristic.Type,
                characteristic.UnitOfMeasure,
                characteristic.IsRequiredForCoa,
                characteristic.CoaStatisticType))
            .ToArray();
        var specLimits = repository.SpecLimits
            .OrderBy(spec => spec.CharacteristicId)
            .Select(spec => new SpecLimitSetupDto(spec.CharacteristicId, spec.Nominal, spec.Lsl, spec.Usl))
            .ToArray();
        var inspectionPlans = GetInspectionPlans();
        var controlLimits = repository.ControlLimits
            .OrderBy(limit => limit.PartNum)
            .ThenBy(limit => limit.ProcessCode)
            .ThenBy(limit => limit.OperationSeq)
            .ThenBy(limit => limit.CharacteristicName)
            .Select(limit => new ControlLimitSetupDto(
                limit.PartNum,
                limit.ProcessCode,
                limit.OperationSeq,
                limit.CharacteristicName,
                limit.CenterLine,
                limit.Lcl,
                limit.Ucl))
            .ToArray();
        var jobs = repository.Jobs
            .OrderBy(job => job.JobNum)
            .Select(job => new JobSetupDto(job.JobNum, job.PartNum))
            .ToArray();
        var resources = repository.Resources
            .OrderBy(resource => resource.ResourceId)
            .Select(resource => new ResourceSetupDto(resource.ResourceId, resource.Description))
            .ToArray();

        return new SetupSnapshotDto(
            generatedAt ?? DateTimeOffset.UtcNow,
            BuildSetupVersion(new SettingsSetupDto(repository.Settings.GlobalAlertRuleSet), parts, processes, operations, characteristics, specLimits, inspectionPlans, controlLimits, jobs, resources),
            new SettingsSetupDto(repository.Settings.GlobalAlertRuleSet),
            parts,
            processes,
            operations,
            characteristics,
            specLimits,
            inspectionPlans,
            controlLimits,
            jobs,
            resources);
    }

    private static string BuildSetupVersion(
        SettingsSetupDto settings,
        IReadOnlyList<PartSetupDto> parts,
        IReadOnlyList<ProcessSetupDto> processes,
        IReadOnlyList<OperationSetupDto> operations,
        IReadOnlyList<CharacteristicSetupDto> characteristics,
        IReadOnlyList<SpecLimitSetupDto> specLimits,
        IReadOnlyList<InspectionPlanSetupDto> inspectionPlans,
        IReadOnlyList<ControlLimitSetupDto> controlLimits,
        IReadOnlyList<JobSetupDto> jobs,
        IReadOnlyList<ResourceSetupDto> resources)
    {
        var builder = new StringBuilder();
        builder.Append(nameof(SettingsSetupDto)).Append('|').Append(settings).AppendLine();
        AppendRows(builder, parts);
        AppendRows(builder, processes);
        AppendRows(builder, operations);
        AppendRows(builder, characteristics);
        AppendRows(builder, specLimits);
        AppendRows(builder, inspectionPlans);
        AppendRows(builder, controlLimits);
        AppendRows(builder, jobs);
        AppendRows(builder, resources);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hash)[..16];
    }

    private static void AppendRows<T>(StringBuilder builder, IReadOnlyList<T> rows)
    {
        foreach (var row in rows)
        {
            builder.Append(typeof(T).Name).Append('|').Append(row).AppendLine();
        }
    }
}
