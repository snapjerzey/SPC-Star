using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using System.Security.Cryptography;
using System.Text;

namespace SPCStar.Core.Services;

public sealed record PartSetupDto(string PartNum, string Description, string ProductGroup, string BlankCode, string HoleSize);

public sealed record InspectionPlanSetupDto(
    string PartNum,
    string PartDescription,
    string ProductGroup,
    string ProcessCode,
    string ProcessDescription,
    int OperationSeq,
    string CharacteristicName,
    CharacteristicType CharacteristicType,
    decimal? Nominal,
    decimal? Lsl,
    decimal? Usl,
    string UnitOfMeasure,
    string Location,
    string InspectionMethod,
    string InspectionPhase,
    int SampleSize,
    int DisplayOrder,
    FrequencyType FrequencyType,
    int FrequencyValue,
    FrequencyUnit FrequencyUnit,
    int? FirstDueValue,
    string AlertRuleSet);

public sealed record ProcessSetupDto(Guid Id, string ProcessCode, string Description);

public sealed record OperationSetupDto(Guid Id, Guid PartId, Guid ProcessId, int OperationSeq);

public sealed record CharacteristicSetupDto(
    Guid Id,
    Guid OperationId,
    string Name,
    CharacteristicType Type,
    string UnitOfMeasure,
    string Location,
    string InspectionMethod);

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

public sealed record ResourceSetupDto(string ResourceId, string? Description, string DeviceProfile, int SerialBaudRate, IReadOnlyList<string> ProductGroups);

public sealed record CustomDriftRuleSetupDto(
    string Name,
    int WindowSize,
    decimal SigmaThreshold,
    int MinimumPointsBeyondThreshold,
    string Direction,
    bool IncludeWesternElectric,
    string WarningBehavior,
    string Notes);

public sealed record CapabilityThresholdSetupDto(decimal YellowMinimum, decimal GreenMinimum);

public sealed record SettingsSetupDto(
    string GlobalAlertRuleSet,
    CustomDriftRuleSetupDto CustomDriftRule,
    CapabilityThresholdSetupDto CapabilityThresholds);

public sealed record PartJobDataFieldSetupDto(
    string PartNum,
    string InspectionPhase,
    string FieldName,
    bool IsRequired,
    int DisplayOrder);

public sealed record PartMaterialFieldSetupDto(
    string PartNum,
    string InspectionPhase,
    string MaterialName,
    string MaterialPartNum,
    string MaterialDescription,
    bool IsRequired,
    int DisplayOrder);

public sealed record SetupSnapshotDto(
    DateTimeOffset GeneratedAt,
    string SetupVersion,
    SettingsSetupDto Settings,
    IReadOnlyList<string> ProductGroups,
    IReadOnlyList<PartSetupDto> Parts,
    IReadOnlyList<ProcessSetupDto> Processes,
    IReadOnlyList<OperationSetupDto> Operations,
    IReadOnlyList<CharacteristicSetupDto> Characteristics,
    IReadOnlyList<SpecLimitSetupDto> SpecLimits,
    IReadOnlyList<InspectionPlanSetupDto> InspectionPlans,
    IReadOnlyList<ControlLimitSetupDto> ControlLimits,
    IReadOnlyList<JobSetupDto> Jobs,
    IReadOnlyList<ResourceSetupDto> Resources,
    IReadOnlyList<PartJobDataFieldSetupDto> PartJobDataFields,
    IReadOnlyList<PartMaterialFieldSetupDto> PartMaterialFields);

public sealed class SetupQueryService(ISpcRepository repository)
{
    private static readonly string[] StandardProductGroups =
    [
        "Ethicon Cutting Edge - Drilled",
        "Ethicon Cutting Edge - Needles",
        "Ethicon Ethalloy Cardio - Needles",
        "Ethicon Everpoint - Needles",
        "Ethicon Taperpoint - Drilled",
        "Ethicon Taperpoint - Needles",
        "General Production",
        "Schneider"
    ];

    public IReadOnlyList<PartSetupDto> GetParts()
    {
        return repository.Parts
            .OrderBy(part => part.PartNum)
            .Select(part => new PartSetupDto(part.PartNum, part.Description, ProductGroup(part.ProductGroup), part.BlankCode, part.HoleSize))
            .ToArray();
    }

    public IReadOnlyList<InspectionPlanSetupDto> GetInspectionPlans(string? partNum = null)
    {
        var query =
            from part in repository.Parts
            join operation in repository.Operations on part.Id equals operation.PartId
            join process in repository.Processes on operation.ProcessId equals process.Id
            join characteristic in repository.Characteristics on operation.Id equals characteristic.OperationId
            join specLimit in repository.SpecLimits on characteristic.Id equals specLimit.CharacteristicId into specLimits
            from spec in specLimits.DefaultIfEmpty()
            join plan in repository.InspectionPlans on characteristic.Id equals plan.CharacteristicId
            where string.IsNullOrWhiteSpace(partNum) || part.PartNum.Equals(partNum, StringComparison.OrdinalIgnoreCase)
            orderby part.PartNum, operation.OperationSeq, plan.InspectionPhase, plan.DisplayOrder, characteristic.Name
            select new InspectionPlanSetupDto(
                part.PartNum,
                part.Description,
                ProductGroup(part.ProductGroup),
                process.ProcessCode,
                process.Description,
                operation.OperationSeq,
                characteristic.Name,
                characteristic.Type,
                plan.Nominal ?? spec?.Nominal,
                plan.Lsl ?? spec?.Lsl,
                plan.Usl ?? spec?.Usl,
                characteristic.UnitOfMeasure,
                characteristic.Location,
                characteristic.InspectionMethod,
                plan.InspectionPhase,
                plan.SampleSize,
                plan.DisplayOrder,
                plan.Frequency.Type,
                plan.Frequency.Value,
                plan.Frequency.Unit,
                plan.Frequency.FirstDueValue,
                plan.AlertRuleSet);

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
                characteristic.Location,
                characteristic.InspectionMethod))
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
            .GroupBy(resource => resource.ResourceId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(resource => !string.IsNullOrWhiteSpace(resource.Description)).First())
            .OrderBy(resource => resource.ResourceId)
            .Select(resource => new ResourceSetupDto(
                resource.ResourceId,
                resource.Description,
                resource.DeviceProfile,
                resource.SerialBaudRate,
                resource.ProductGroups.OrderBy(group => group).ToArray()))
            .ToArray();
        var jobDataFields =
            (from field in repository.PartJobDataFields
             join part in repository.Parts on field.PartId equals part.Id
             orderby part.PartNum, field.InspectionPhase, field.DisplayOrder, field.FieldName
             select new PartJobDataFieldSetupDto(part.PartNum, field.InspectionPhase, field.FieldName, field.IsRequired, field.DisplayOrder))
            .ToArray();
        var materialFields =
            (from field in repository.PartMaterialFields
             join part in repository.Parts on field.PartId equals part.Id
             orderby part.PartNum, field.InspectionPhase, field.DisplayOrder, field.MaterialName
             select new PartMaterialFieldSetupDto(part.PartNum, field.InspectionPhase, field.MaterialName, field.MaterialPartNum, field.MaterialDescription, field.IsRequired, field.DisplayOrder))
            .ToArray();
        var productGroups = LoadedProductGroups();

        return new SetupSnapshotDto(
            generatedAt ?? DateTimeOffset.UtcNow,
            BuildSetupVersion(SettingsDto(), productGroups, parts, processes, operations, characteristics, specLimits, inspectionPlans, controlLimits, jobs, resources, jobDataFields, materialFields),
            SettingsDto(),
            productGroups,
            parts,
            processes,
            operations,
            characteristics,
            specLimits,
            inspectionPlans,
            controlLimits,
            jobs,
            resources,
            jobDataFields,
            materialFields);
    }

    private SettingsSetupDto SettingsDto()
    {
        var custom = repository.Settings.CustomDriftRule;
        var capability = repository.Settings.CapabilityThresholds;
        return new SettingsSetupDto(
            repository.Settings.GlobalAlertRuleSet,
            new CustomDriftRuleSetupDto(
                custom.Name,
                custom.WindowSize,
                custom.SigmaThreshold,
                custom.MinimumPointsBeyondThreshold,
                custom.Direction,
                custom.IncludeWesternElectric,
                custom.WarningBehavior,
                custom.Notes),
            new CapabilityThresholdSetupDto(
                capability.YellowMinimum,
                capability.GreenMinimum));
    }

    private IReadOnlyList<string> LoadedProductGroups()
    {
        return repository.Parts
            .Select(part => ProductGroup(part.ProductGroup))
            .Concat(repository.Users.SelectMany(user => user.ProductGroups.Select(ProductGroup)))
            .Concat(repository.Resources.SelectMany(resource => resource.ProductGroups.Select(ProductGroup)))
            .Concat(StandardProductGroups)
            .Where(group => !string.IsNullOrWhiteSpace(group))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group)
            .ToArray();
    }

    private static string ProductGroup(string? value)
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

    private static string BuildSetupVersion(
        SettingsSetupDto settings,
        IReadOnlyList<string> productGroups,
        IReadOnlyList<PartSetupDto> parts,
        IReadOnlyList<ProcessSetupDto> processes,
        IReadOnlyList<OperationSetupDto> operations,
        IReadOnlyList<CharacteristicSetupDto> characteristics,
        IReadOnlyList<SpecLimitSetupDto> specLimits,
        IReadOnlyList<InspectionPlanSetupDto> inspectionPlans,
        IReadOnlyList<ControlLimitSetupDto> controlLimits,
        IReadOnlyList<JobSetupDto> jobs,
        IReadOnlyList<ResourceSetupDto> resources,
        IReadOnlyList<PartJobDataFieldSetupDto> jobDataFields,
        IReadOnlyList<PartMaterialFieldSetupDto> materialFields)
    {
        var builder = new StringBuilder();
        builder.Append(nameof(SettingsSetupDto)).Append('|').Append(settings).AppendLine();
        AppendRows(builder, productGroups);
        AppendRows(builder, parts);
        AppendRows(builder, processes);
        AppendRows(builder, operations);
        AppendRows(builder, characteristics);
        AppendRows(builder, specLimits);
        AppendRows(builder, inspectionPlans);
        AppendRows(builder, controlLimits);
        AppendRows(builder, jobs);
        AppendRows(builder, resources);
        AppendRows(builder, jobDataFields);
        AppendRows(builder, materialFields);

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
