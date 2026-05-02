using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;

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
    bool IsRequiredForCoa);

public sealed class SetupQueryService(InMemorySpcRepository repository)
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
                characteristic.IsRequiredForCoa);

        return query.ToArray();
    }
}
