using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;

namespace SPCStar.Core.Services;

public sealed class SetupImportService(InMemorySpcRepository repository)
{
    private static readonly string[] RequiredFields =
    [
        "PartNum",
        "PartDescription",
        "ProcessCode",
        "ProcessDescription",
        "OperationSeq",
        "CharacteristicName",
        "CharacteristicType",
        "Nominal",
        "LSL",
        "USL",
        "UnitOfMeasure",
        "SampleSize",
        "FrequencyType",
        "FrequencyValue",
        "FrequencyUnit",
        "AlertRuleSet",
        "IsRequiredForCOA"
    ];

    public ServiceResult ImportCsv(string csv)
    {
        var rows = CsvSupport.ReadRows(csv);
        var errors = ValidateRows(rows);
        if (errors.Count > 0)
        {
            return ServiceResult.Fail(errors);
        }

        foreach (var row in rows)
        {
            Upsert(row);
        }

        return ServiceResult.Ok();
    }

    public IReadOnlyList<string> ValidateCsv(string csv) => ValidateRows(CsvSupport.ReadRows(csv));

    private static List<string> ValidateRows(IReadOnlyList<Dictionary<string, string>> rows)
    {
        var errors = new List<string>();
        var seenCharacteristics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < rows.Count; index++)
        {
            var rowNumber = index + 2;
            var row = rows[index];

            foreach (var field in RequiredFields)
            {
                if (!row.TryGetValue(field, out var value) || string.IsNullOrWhiteSpace(value))
                {
                    errors.Add($"Row {rowNumber}: Missing required field {field}.");
                }
            }

            if (!int.TryParse(row.GetValueOrDefault("OperationSeq"), out var operationSeq))
            {
                errors.Add($"Row {rowNumber}: OperationSeq must be a whole number.");
            }

            if (!decimal.TryParse(row.GetValueOrDefault("LSL"), out var lsl) ||
                !decimal.TryParse(row.GetValueOrDefault("USL"), out var usl) ||
                !decimal.TryParse(row.GetValueOrDefault("Nominal"), out _))
            {
                errors.Add($"Row {rowNumber}: Nominal, LSL, and USL must be numeric.");
            }
            else if (lsl >= usl)
            {
                errors.Add($"Row {rowNumber}: Invalid spec limits. LSL must be less than USL.");
            }

            if (!Enum.TryParse<CharacteristicType>(row.GetValueOrDefault("CharacteristicType"), true, out _))
            {
                errors.Add($"Row {rowNumber}: Invalid CharacteristicType.");
            }

            if (!int.TryParse(row.GetValueOrDefault("SampleSize"), out var sampleSize) || sampleSize <= 0)
            {
                errors.Add($"Row {rowNumber}: SampleSize must be greater than zero.");
            }

            var hasFrequencyType = Enum.TryParse<FrequencyType>(row.GetValueOrDefault("FrequencyType"), true, out var frequencyType);
            if (!hasFrequencyType)
            {
                errors.Add($"Row {rowNumber}: Invalid FrequencyType.");
            }

            if (!int.TryParse(row.GetValueOrDefault("FrequencyValue"), out var frequencyValue) || frequencyValue <= 0)
            {
                errors.Add($"Row {rowNumber}: FrequencyValue must be greater than zero.");
            }

            var hasFrequencyUnit = Enum.TryParse<FrequencyUnit>(row.GetValueOrDefault("FrequencyUnit"), true, out var frequencyUnit);
            if (!hasFrequencyUnit)
            {
                errors.Add($"Row {rowNumber}: Invalid FrequencyUnit.");
            }
            else if (hasFrequencyType && !IsValidFrequencyPair(frequencyType, frequencyUnit))
            {
                errors.Add($"Row {rowNumber}: Invalid frequency. FrequencyType and FrequencyUnit are not compatible.");
            }

            if (!string.Equals(row.GetValueOrDefault("AlertRuleSet"), "WesternElectric", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Row {rowNumber}: Invalid AlertRuleSet. MVP supports WesternElectric.");
            }

            if (!bool.TryParse(row.GetValueOrDefault("IsRequiredForCOA"), out _))
            {
                errors.Add($"Row {rowNumber}: IsRequiredForCOA must be true or false.");
            }

            var duplicateKey = $"{row.GetValueOrDefault("PartNum")}|{row.GetValueOrDefault("ProcessCode")}|{operationSeq}|{row.GetValueOrDefault("CharacteristicName")}";
            if (!seenCharacteristics.Add(duplicateKey))
            {
                errors.Add($"Row {rowNumber}: Duplicate characteristic in import.");
            }
        }

        return errors;
    }

    private static bool IsValidFrequencyPair(FrequencyType type, FrequencyUnit unit)
    {
        return type switch
        {
            FrequencyType.Time => unit is FrequencyUnit.Minutes or FrequencyUnit.Hours,
            FrequencyType.Quantity => unit is FrequencyUnit.Pieces,
            FrequencyType.Event => unit is FrequencyUnit.StartOfJob or FrequencyUnit.MaterialChange or FrequencyUnit.ToolChange or FrequencyUnit.Restart,
            _ => false
        };
    }

    private void Upsert(Dictionary<string, string> row)
    {
        var part = repository.Parts.FirstOrDefault(p => p.PartNum.Equals(row["PartNum"], StringComparison.OrdinalIgnoreCase));
        if (part is null)
        {
            part = new Part { PartNum = row["PartNum"], Description = row["PartDescription"] };
            repository.Parts.Add(part);
        }
        else
        {
            part.Description = row["PartDescription"];
        }

        var process = repository.Processes.FirstOrDefault(p => p.ProcessCode.Equals(row["ProcessCode"], StringComparison.OrdinalIgnoreCase));
        if (process is null)
        {
            process = new ManufacturingProcess { ProcessCode = row["ProcessCode"], Description = row["ProcessDescription"] };
            repository.Processes.Add(process);
        }
        else
        {
            process.Description = row["ProcessDescription"];
        }

        var operationSeq = int.Parse(row["OperationSeq"]);
        var operation = repository.Operations.FirstOrDefault(o =>
            o.PartId == part.Id &&
            o.ProcessId == process.Id &&
            o.OperationSeq == operationSeq);
        if (operation is null)
        {
            operation = new Operation { PartId = part.Id, ProcessId = process.Id, OperationSeq = operationSeq };
            repository.Operations.Add(operation);
        }

        var characteristic = repository.Characteristics.FirstOrDefault(c =>
            c.OperationId == operation.Id &&
            c.Name.Equals(row["CharacteristicName"], StringComparison.OrdinalIgnoreCase));
        if (characteristic is null)
        {
            characteristic = new Characteristic
            {
                OperationId = operation.Id,
                Name = row["CharacteristicName"],
                Type = Enum.Parse<CharacteristicType>(row["CharacteristicType"], true),
                UnitOfMeasure = row["UnitOfMeasure"],
                IsRequiredForCoa = bool.Parse(row["IsRequiredForCOA"])
            };
            repository.Characteristics.Add(characteristic);
        }
        else
        {
            characteristic.Type = Enum.Parse<CharacteristicType>(row["CharacteristicType"], true);
            characteristic.UnitOfMeasure = row["UnitOfMeasure"];
            characteristic.IsRequiredForCoa = bool.Parse(row["IsRequiredForCOA"]);
        }

        var spec = repository.SpecLimits.FirstOrDefault(s => s.CharacteristicId == characteristic.Id);
        if (spec is null)
        {
            repository.SpecLimits.Add(new SpecLimit
            {
                CharacteristicId = characteristic.Id,
                Nominal = decimal.Parse(row["Nominal"]),
                Lsl = decimal.Parse(row["LSL"]),
                Usl = decimal.Parse(row["USL"])
            });
        }
        else
        {
            spec.Nominal = decimal.Parse(row["Nominal"]);
            spec.Lsl = decimal.Parse(row["LSL"]);
            spec.Usl = decimal.Parse(row["USL"]);
        }

        var plan = repository.InspectionPlans.FirstOrDefault(p => p.CharacteristicId == characteristic.Id);
        if (plan is null)
        {
            repository.InspectionPlans.Add(BuildPlan(characteristic.Id, row));
        }
        else
        {
            plan.SampleSize = int.Parse(row["SampleSize"]);
            plan.AlertRuleSet = row["AlertRuleSet"];
            plan.Frequency = BuildFrequency(row);
        }
    }

    private static InspectionPlan BuildPlan(Guid characteristicId, Dictionary<string, string> row)
    {
        return new InspectionPlan
        {
            CharacteristicId = characteristicId,
            SampleSize = int.Parse(row["SampleSize"]),
            AlertRuleSet = row["AlertRuleSet"],
            Frequency = BuildFrequency(row)
        };
    }

    private static InspectionFrequency BuildFrequency(Dictionary<string, string> row)
    {
        return new InspectionFrequency
        {
            Type = Enum.Parse<FrequencyType>(row["FrequencyType"], true),
            Value = int.Parse(row["FrequencyValue"]),
            Unit = Enum.Parse<FrequencyUnit>(row["FrequencyUnit"], true)
        };
    }
}
