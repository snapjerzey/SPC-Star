using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;

namespace SPCStar.Core.Services;

public sealed class SetupImportService(ISpcRepository repository)
{
    private static readonly string[] BaseRequiredFields =
    [
        "RowType",
        "PartNum",
        "PartDescription",
        "ProductGroup",
        "InspectionPhase"
    ];

    private static readonly string[] CharacteristicRequiredFields =
    [
        "Operation",
        "CharacteristicName",
        "CharacteristicType",
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
        var seenRows = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < rows.Count; index++)
        {
            var rowNumber = index + 2;
            var row = rows[index];
            var rowType = RowType(row);

            foreach (var field in BaseRequiredFields)
            {
                Required(row, field, rowNumber, errors);
            }

            if (!IsValidRowType(rowType))
            {
                errors.Add($"Row {rowNumber}: RowType must be Variable, Attribute, JobData, or Material.");
                continue;
            }

            if (!IsValidInspectionPhase(row.GetValueOrDefault("InspectionPhase")))
            {
                errors.Add($"Row {rowNumber}: InspectionPhase must be Startup, Setup, In Process, or Spool.");
            }

            switch (rowType)
            {
                case "Variable":
                case "Attribute":
                    ValidateCharacteristicRow(row, rowNumber, rowType, errors);
                    break;
                case "JobData":
                    ValidateJobDataRow(row, rowNumber, errors);
                    break;
                case "Material":
                    ValidateMaterialRow(row, rowNumber, errors);
                    break;
            }

            var duplicateKey = DuplicateKey(row, rowType);
            if (!seenRows.Add(duplicateKey))
            {
                errors.Add($"Row {rowNumber}: Duplicate {rowType} definition in import.");
            }
        }

        return errors;
    }

    private static void ValidateCharacteristicRow(Dictionary<string, string> row, int rowNumber, string rowType, List<string> errors)
    {
        foreach (var field in CharacteristicRequiredFields)
        {
            Required(row, field, rowNumber, errors);
        }

        if (!Enum.TryParse<CharacteristicType>(row.GetValueOrDefault("CharacteristicType"), true, out var characteristicType))
        {
            errors.Add($"Row {rowNumber}: Invalid CharacteristicType.");
        }
        else if ((rowType == "Variable" && characteristicType != CharacteristicType.Variable) ||
            (rowType == "Attribute" && characteristicType != CharacteristicType.Attribute))
        {
            errors.Add($"Row {rowNumber}: RowType and CharacteristicType must match.");
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

        if (!IsSupportedRuleSet(row.GetValueOrDefault("AlertRuleSet") ?? ""))
        {
            errors.Add($"Row {rowNumber}: Invalid AlertRuleSet.");
        }

        if (!bool.TryParse(row.GetValueOrDefault("IsRequiredForCOA"), out _))
        {
            errors.Add($"Row {rowNumber}: IsRequiredForCOA must be true or false.");
        }

        if (!string.IsNullOrWhiteSpace(row.GetValueOrDefault("COAStatistic")) &&
            !Enum.TryParse<CoaStatisticType>(row.GetValueOrDefault("COAStatistic"), true, out _))
        {
            errors.Add($"Row {rowNumber}: Invalid COAStatistic.");
        }

        if (rowType == "Variable")
        {
            ValidateVariableLimits(row, rowNumber, errors);
        }
    }

    private static void ValidateVariableLimits(Dictionary<string, string> row, int rowNumber, List<string> errors)
    {
        if (!decimal.TryParse(row.GetValueOrDefault("LSL"), out var lsl) ||
            !decimal.TryParse(row.GetValueOrDefault("USL"), out var usl) ||
            !decimal.TryParse(row.GetValueOrDefault("Nominal"), out _))
        {
            errors.Add($"Row {rowNumber}: Nominal, LSL, and USL must be numeric for Variable rows.");
        }
        else if (lsl >= usl)
        {
            errors.Add($"Row {rowNumber}: Invalid spec limits. LSL must be less than USL.");
        }

        if (!OptionalDecimal(row, "LCL", out var lcl) || !OptionalDecimal(row, "UCL", out var ucl))
        {
            errors.Add($"Row {rowNumber}: LCL and UCL must be numeric when provided.");
        }
        else if (lcl.HasValue && ucl.HasValue && lcl.Value >= ucl.Value)
        {
            errors.Add($"Row {rowNumber}: Invalid control limits. LCL must be less than UCL.");
        }
    }

    private static void ValidateJobDataRow(Dictionary<string, string> row, int rowNumber, List<string> errors)
    {
        Required(row, "FieldName", rowNumber, errors);
        ValidateRequiredFlag(row, rowNumber, errors);
        ValidateDisplayOrder(row, rowNumber, errors);
    }

    private static void ValidateMaterialRow(Dictionary<string, string> row, int rowNumber, List<string> errors)
    {
        Required(row, "MaterialName", rowNumber, errors);
        Required(row, "MaterialPartNum", rowNumber, errors);
        ValidateRequiredFlag(row, rowNumber, errors);
        ValidateDisplayOrder(row, rowNumber, errors);
    }

    private static void ValidateRequiredFlag(Dictionary<string, string> row, int rowNumber, List<string> errors)
    {
        if (!string.IsNullOrWhiteSpace(row.GetValueOrDefault("IsRequired")) &&
            !bool.TryParse(row.GetValueOrDefault("IsRequired"), out _))
        {
            errors.Add($"Row {rowNumber}: IsRequired must be true or false when provided.");
        }
    }

    private static void ValidateDisplayOrder(Dictionary<string, string> row, int rowNumber, List<string> errors)
    {
        if (!string.IsNullOrWhiteSpace(row.GetValueOrDefault("DisplayOrder")) &&
            (!int.TryParse(row.GetValueOrDefault("DisplayOrder"), out var displayOrder) || displayOrder < 0))
        {
            errors.Add($"Row {rowNumber}: DisplayOrder must be zero or greater when provided.");
        }
    }

    private void Upsert(Dictionary<string, string> row)
    {
        var part = UpsertPart(row);
        switch (RowType(row))
        {
            case "Variable":
            case "Attribute":
                UpsertCharacteristic(row, part);
                break;
            case "JobData":
                UpsertJobDataField(row, part);
                break;
            case "Material":
                UpsertMaterialField(row, part);
                break;
        }
    }

    private Part UpsertPart(Dictionary<string, string> row)
    {
        var part = repository.Parts.FirstOrDefault(p => p.PartNum.Equals(row["PartNum"], StringComparison.OrdinalIgnoreCase));
        if (part is null)
        {
            part = new Part { PartNum = row["PartNum"].Trim(), Description = row["PartDescription"].Trim(), ProductGroup = CleanProductGroup(row.GetValueOrDefault("ProductGroup")) };
            repository.Parts.Add(part);
        }
        else
        {
            part.Description = row["PartDescription"].Trim();
            part.ProductGroup = CleanProductGroup(row.GetValueOrDefault("ProductGroup"));
        }

        return part;
    }

    private void UpsertCharacteristic(Dictionary<string, string> row, Part part)
    {
        var process = repository.Processes.FirstOrDefault(p => p.ProcessCode.Equals(row["Operation"], StringComparison.OrdinalIgnoreCase));
        if (process is null)
        {
            process = new ManufacturingProcess { ProcessCode = row["Operation"].Trim(), Description = row["Operation"].Trim() };
            repository.Processes.Add(process);
        }
        else
        {
            process.Description = row["Operation"].Trim();
        }

        const int operationSeq = 10;
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
                Name = row["CharacteristicName"].Trim(),
                Type = Enum.Parse<CharacteristicType>(row["CharacteristicType"], true),
                UnitOfMeasure = row.GetValueOrDefault("UnitOfMeasure")?.Trim() ?? "",
                IsRequiredForCoa = bool.Parse(row["IsRequiredForCOA"]),
                CoaStatisticType = CoaStatistic(row)
            };
            repository.Characteristics.Add(characteristic);
        }
        else
        {
            characteristic.Type = Enum.Parse<CharacteristicType>(row["CharacteristicType"], true);
            characteristic.UnitOfMeasure = row.GetValueOrDefault("UnitOfMeasure")?.Trim() ?? "";
            characteristic.IsRequiredForCoa = bool.Parse(row["IsRequiredForCOA"]);
            characteristic.CoaStatisticType = CoaStatistic(row);
        }

        UpsertSpecLimit(row, characteristic);
        UpsertPlan(row, characteristic);
        if (characteristic.Type == CharacteristicType.Variable)
        {
            UpsertControlLimit(row, part, process, operationSeq);
        }
    }

    private void UpsertSpecLimit(Dictionary<string, string> row, Characteristic characteristic)
    {
        var isVariable = characteristic.Type == CharacteristicType.Variable;
        var nominal = isVariable ? decimal.Parse(row["Nominal"]) : 1m;
        var lsl = isVariable ? decimal.Parse(row["LSL"]) : 1m;
        var usl = isVariable ? decimal.Parse(row["USL"]) : 1m;
        var spec = repository.SpecLimits.FirstOrDefault(s => s.CharacteristicId == characteristic.Id);
        if (spec is null)
        {
            repository.SpecLimits.Add(new SpecLimit { CharacteristicId = characteristic.Id, Nominal = nominal, Lsl = lsl, Usl = usl });
            return;
        }

        spec.Nominal = nominal;
        spec.Lsl = lsl;
        spec.Usl = usl;
    }

    private void UpsertPlan(Dictionary<string, string> row, Characteristic characteristic)
    {
        var inspectionPhase = NormalizeInspectionPhase(row.GetValueOrDefault("InspectionPhase"));
        var plan = repository.InspectionPlans.FirstOrDefault(p =>
            p.CharacteristicId == characteristic.Id &&
            p.InspectionPhase.Equals(inspectionPhase, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            repository.InspectionPlans.Add(new InspectionPlan
            {
                CharacteristicId = characteristic.Id,
                InspectionPhase = inspectionPhase,
                SampleSize = int.Parse(row["SampleSize"]),
                AlertRuleSet = row["AlertRuleSet"].Trim(),
                Frequency = BuildFrequency(row)
            });
            return;
        }

        plan.InspectionPhase = inspectionPhase;
        plan.SampleSize = int.Parse(row["SampleSize"]);
        plan.AlertRuleSet = row["AlertRuleSet"].Trim();
        plan.Frequency = BuildFrequency(row);
    }

    private void UpsertJobDataField(Dictionary<string, string> row, Part part)
    {
        var inspectionPhase = NormalizeInspectionPhase(row.GetValueOrDefault("InspectionPhase"));
        var fieldName = row["FieldName"].Trim();
        var field = repository.PartJobDataFields.FirstOrDefault(item =>
            item.PartId == part.Id &&
            item.InspectionPhase.Equals(inspectionPhase, StringComparison.OrdinalIgnoreCase) &&
            item.FieldName.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
        if (field is null)
        {
            field = new PartJobDataField { PartId = part.Id, InspectionPhase = inspectionPhase, FieldName = fieldName };
            repository.PartJobDataFields.Add(field);
        }

        field.IsRequired = OptionalBool(row, "IsRequired", true);
        field.DisplayOrder = OptionalInt(row, "DisplayOrder", repository.PartJobDataFields.Count(item => item.PartId == part.Id && item.InspectionPhase.Equals(inspectionPhase, StringComparison.OrdinalIgnoreCase)));
    }

    private void UpsertMaterialField(Dictionary<string, string> row, Part part)
    {
        var inspectionPhase = NormalizeInspectionPhase(row.GetValueOrDefault("InspectionPhase"));
        var materialName = row["MaterialName"].Trim();
        var field = repository.PartMaterialFields.FirstOrDefault(item =>
            item.PartId == part.Id &&
            item.InspectionPhase.Equals(inspectionPhase, StringComparison.OrdinalIgnoreCase) &&
            item.MaterialName.Equals(materialName, StringComparison.OrdinalIgnoreCase));
        if (field is null)
        {
            field = new PartMaterialField
            {
                PartId = part.Id,
                InspectionPhase = inspectionPhase,
                MaterialName = materialName,
                MaterialPartNum = row["MaterialPartNum"].Trim()
            };
            repository.PartMaterialFields.Add(field);
        }

        field.MaterialPartNum = row["MaterialPartNum"].Trim();
        field.IsRequired = OptionalBool(row, "IsRequired", true);
        field.DisplayOrder = OptionalInt(row, "DisplayOrder", repository.PartMaterialFields.Count(item => item.PartId == part.Id && item.InspectionPhase.Equals(inspectionPhase, StringComparison.OrdinalIgnoreCase)));
    }

    private static string DuplicateKey(Dictionary<string, string> row, string rowType)
    {
        var phase = NormalizeInspectionPhase(row.GetValueOrDefault("InspectionPhase"));
        var part = row.GetValueOrDefault("PartNum");
        return rowType switch
        {
            "Variable" or "Attribute" => $"{rowType}|{part}|{row.GetValueOrDefault("Operation")}|{phase}|{row.GetValueOrDefault("CharacteristicName")}",
            "JobData" => $"{rowType}|{part}|{phase}|{row.GetValueOrDefault("FieldName")}",
            "Material" => $"{rowType}|{part}|{phase}|{row.GetValueOrDefault("MaterialName")}",
            _ => $"{rowType}|{part}|{phase}"
        };
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

    private static bool IsSupportedRuleSet(string ruleSet)
    {
        return string.Equals(ruleSet, "WesternElectric", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ruleSet, "GlobalDefault", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ruleSet, "NelsonRules", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ruleSet, "Cusum", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ruleSet, "Ewma", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ruleSet, "MovingAverageTrend", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ruleSet, "LinearTrendSlope", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ruleSet, "Custom", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ruleSet, "SpecLimitOnly", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ruleSet, "None", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsValidInspectionPhase(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ||
            value.Trim().Equals("Startup", StringComparison.OrdinalIgnoreCase) ||
            value.Trim().Equals("Set Up", StringComparison.OrdinalIgnoreCase) ||
            value.Trim().Equals("Setup", StringComparison.OrdinalIgnoreCase) ||
            value.Trim().Equals("Spool", StringComparison.OrdinalIgnoreCase) ||
            value.Trim().Equals("Spool Start", StringComparison.OrdinalIgnoreCase) ||
            value.Trim().Equals("Spool End", StringComparison.OrdinalIgnoreCase) ||
            value.Trim().Equals("In Process", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeInspectionPhase(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "In Process";
        }

        var phase = value.Trim();
        if (phase.Equals("Startup", StringComparison.OrdinalIgnoreCase))
        {
            return "Startup";
        }
        if (phase.Equals("Spool", StringComparison.OrdinalIgnoreCase) ||
            phase.Equals("Spool Start", StringComparison.OrdinalIgnoreCase) ||
            phase.Equals("Spool End", StringComparison.OrdinalIgnoreCase))
        {
            return "Spool";
        }

        return phase.Equals("Set Up", StringComparison.OrdinalIgnoreCase) ||
            phase.Equals("Setup", StringComparison.OrdinalIgnoreCase)
            ? "Setup"
            : "In Process";
    }

    private void UpsertControlLimit(Dictionary<string, string> row, Part part, ManufacturingProcess process, int operationSeq)
    {
        var nominal = decimal.Parse(row["Nominal"]);
        var lcl = OptionalDecimal(row, "LCL", out var parsedLcl) && parsedLcl.HasValue ? parsedLcl.Value : decimal.Parse(row["LSL"]);
        var ucl = OptionalDecimal(row, "UCL", out var parsedUcl) && parsedUcl.HasValue ? parsedUcl.Value : decimal.Parse(row["USL"]);
        var limit = repository.ControlLimits.FirstOrDefault(item =>
            item.PartNum.Equals(part.PartNum, StringComparison.OrdinalIgnoreCase) &&
            item.ProcessCode.Equals(process.ProcessCode, StringComparison.OrdinalIgnoreCase) &&
            item.OperationSeq == operationSeq &&
            item.CharacteristicName.Equals(row["CharacteristicName"], StringComparison.OrdinalIgnoreCase));

        if (limit is null)
        {
            repository.ControlLimits.Add(new ControlLimitSet
            {
                PartNum = part.PartNum,
                ProcessCode = process.ProcessCode,
                OperationSeq = operationSeq,
                CharacteristicName = row["CharacteristicName"].Trim(),
                CenterLine = nominal,
                Lcl = lcl,
                Ucl = ucl
            });
            return;
        }

        limit.CenterLine = nominal;
        limit.Lcl = lcl;
        limit.Ucl = ucl;
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

    private static CoaStatisticType CoaStatistic(Dictionary<string, string> row)
    {
        return row.TryGetValue("COAStatistic", out var value) && !string.IsNullOrWhiteSpace(value)
            ? Enum.Parse<CoaStatisticType>(value, true)
            : CoaStatisticType.Mean;
    }

    private static bool OptionalDecimal(Dictionary<string, string> row, string field, out decimal? value)
    {
        value = null;
        if (!row.TryGetValue(field, out var text) || string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (!decimal.TryParse(text, out var parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool OptionalBool(Dictionary<string, string> row, string field, bool fallback)
    {
        return row.TryGetValue(field, out var value) && bool.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static int OptionalInt(Dictionary<string, string> row, string field, int fallback)
    {
        return row.TryGetValue(field, out var value) && int.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static void Required(Dictionary<string, string> row, string field, int rowNumber, List<string> errors)
    {
        if (!row.TryGetValue(field, out var value) || string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"Row {rowNumber}: Missing required field {field}.");
        }
    }

    private static string RowType(Dictionary<string, string> row) => CanonicalRowType(row.GetValueOrDefault("RowType"));

    private static string CanonicalRowType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Variable";
        }

        var clean = value.Trim().Replace(" ", "", StringComparison.OrdinalIgnoreCase);
        if (clean.Equals("AcceptReject", StringComparison.OrdinalIgnoreCase) || clean.Equals("Accept/Reject", StringComparison.OrdinalIgnoreCase))
        {
            return "Attribute";
        }

        return clean switch
        {
            _ when clean.Equals("Variable", StringComparison.OrdinalIgnoreCase) => "Variable",
            _ when clean.Equals("Attribute", StringComparison.OrdinalIgnoreCase) => "Attribute",
            _ when clean.Equals("JobData", StringComparison.OrdinalIgnoreCase) => "JobData",
            _ when clean.Equals("Material", StringComparison.OrdinalIgnoreCase) => "Material",
            _ => value.Trim()
        };
    }

    private static bool IsValidRowType(string rowType) => rowType is "Variable" or "Attribute" or "JobData" or "Material";

    private static string CleanProductGroup(string? value) => string.IsNullOrWhiteSpace(value) ? "General" : value.Trim();
}
