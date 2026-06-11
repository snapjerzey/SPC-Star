using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;

namespace SPCStar.Core.Services;

public sealed class SetupImportService(ISpcRepository repository)
{
    private static readonly string[] BaseRequiredFields =
    [
        "PartNum",
        "PartDescription",
        "ProductGroup"
    ];

    private static readonly string[] CharacteristicRequiredFields =
    [
        "Operation",
        "CharacteristicName",
        "CharacteristicType",
        "SampleSize",
        "AlertRuleSet",
        "IsRequiredForCOA"
    ];

    public ServiceResult ImportCsv(string csv)
    {
        var rows = NormalizeRows(CsvSupport.ReadRows(csv));
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

    public IReadOnlyList<string> ValidateCsv(string csv) => ValidateRows(NormalizeRows(CsvSupport.ReadRows(csv)));

    private static IReadOnlyList<Dictionary<string, string>> NormalizeRows(IReadOnlyList<Dictionary<string, string>> rows)
    {
        return rows
            .Select(NormalizeRow)
            .SelectMany(ExpandPhaseMatrixRow)
            .ToArray();
    }

    private static IEnumerable<Dictionary<string, string>> ExpandPhaseMatrixRow(Dictionary<string, string> row)
    {
        var rowType = RowType(row);
        if (rowType is not ("Variable" or "Attribute" or "JobData") || !HasPhaseMatrix(row))
        {
            yield return row;
            yield break;
        }

        var expanded = false;
        foreach (var phase in PhaseMatrixDefinitions())
        {
            if (!PhaseIsRequired(row, phase))
            {
                continue;
            }

            var clone = new Dictionary<string, string>(row, StringComparer.OrdinalIgnoreCase)
            {
                ["InspectionPhase"] = phase.CanonicalName
            };
            CopyPhaseValue(row, clone, phase, "SampleSize", "Sample Size");
            CopyPhaseValue(row, clone, phase, "FrequencyType", "Frequency Type");
            CopyPhaseValue(row, clone, phase, "FrequencyValue", "Frequency");
            CopyPhaseValue(row, clone, phase, "FrequencyUnit", "Frequency Unit");
            CopyPhaseValue(row, clone, phase, "DisplayOrder", "Display Order");
            ApplyPhaseDefaults(clone, phase.CanonicalName);
            expanded = true;
            yield return clone;
        }

        if (!expanded)
        {
            yield return row;
        }
    }

    private static bool HasPhaseMatrix(Dictionary<string, string> row)
    {
        return PhaseMatrixDefinitions().Any(phase =>
            PhaseFieldValue(row, phase, "Required") != "" ||
            PhaseFieldValue(row, phase, "Sample Size") != "" ||
            PhaseFieldValue(row, phase, "Frequency Type") != "" ||
            PhaseFieldValue(row, phase, "Frequency") != "" ||
            PhaseFieldValue(row, phase, "Frequency Qty") != "" ||
            PhaseFieldValue(row, phase, "Frequency Unit") != "" ||
            PhaseFieldValue(row, phase, "Display Order") != "");
    }

    private static bool PhaseIsRequired(Dictionary<string, string> row, PhaseMatrixDefinition phase)
    {
        var required = PhaseFieldValue(row, phase, "Required");
        if (!string.IsNullOrWhiteSpace(required))
        {
            return IsTruthy(required);
        }

        return PhaseFieldValue(row, phase, "Sample Size") != "" ||
            PhaseFieldValue(row, phase, "Frequency Type") != "" ||
            PhaseFieldValue(row, phase, "Frequency") != "" ||
            PhaseFieldValue(row, phase, "Frequency Qty") != "" ||
            PhaseFieldValue(row, phase, "Frequency Unit") != "" ||
            PhaseFieldValue(row, phase, "Display Order") != "";
    }

    private static bool IsTruthy(string value)
    {
        return value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) ||
            value.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase) ||
            value.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) ||
            value.Trim().Equals("required", StringComparison.OrdinalIgnoreCase) ||
            value.Trim().Equals("x", StringComparison.OrdinalIgnoreCase) ||
            value.Trim().Equals("1", StringComparison.OrdinalIgnoreCase);
    }

    private static void CopyPhaseValue(Dictionary<string, string> source, Dictionary<string, string> target, PhaseMatrixDefinition phase, string canonicalField, string suffix)
    {
        var value = PhaseFieldValue(source, phase, suffix);
        if (!string.IsNullOrWhiteSpace(value))
        {
            target[canonicalField] = value;
        }
    }

    private static string PhaseFieldValue(Dictionary<string, string> row, PhaseMatrixDefinition phase, string suffix)
    {
        foreach (var prefix in phase.Prefixes)
        {
            foreach (var field in PhaseFieldNames(prefix, suffix))
            {
                if (row.TryGetValue(field, out var value) && !string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }
        }

        return "";
    }

    private static IEnumerable<string> PhaseFieldNames(string prefix, string suffix)
    {
        yield return $"{prefix} {suffix}";
        yield return $"{prefix}{suffix.Replace(" ", "", StringComparison.OrdinalIgnoreCase)}";
        if (suffix.Equals("Frequency", StringComparison.OrdinalIgnoreCase))
        {
            yield return $"{prefix} Frequency Qty";
            yield return $"{prefix} Frequency Value";
            yield return $"{prefix}FrequencyQty";
            yield return $"{prefix}FrequencyValue";
        }
        if (suffix.Equals("Display Order", StringComparison.OrdinalIgnoreCase))
        {
            yield return $"{prefix} Order";
            yield return $"{prefix} DisplayOrder";
            yield return $"{prefix}Order";
        }
    }

    private static IReadOnlyList<PhaseMatrixDefinition> PhaseMatrixDefinitions()
    {
        return
        [
            new("Startup", ["Startup", "Start Up"]),
            new("Setup", ["Setup", "Set Up"]),
            new("In Process", ["In Process", "InProcess"]),
            new("Coil Change", ["Coil Change", "CoilChange"]),
            new("Spool", ["Spool"])
        ];
    }

    private static void ApplyPhaseDefaults(Dictionary<string, string> row, string phase)
    {
        if (string.IsNullOrWhiteSpace(row.GetValueOrDefault("FrequencyType")))
        {
            row["FrequencyType"] = phase.Equals("In Process", StringComparison.OrdinalIgnoreCase) ? "Quantity" : "Event";
        }

        if (string.IsNullOrWhiteSpace(row.GetValueOrDefault("FrequencyValue")))
        {
            row["FrequencyValue"] = "1";
        }

        if (string.IsNullOrWhiteSpace(row.GetValueOrDefault("FrequencyUnit")))
        {
            row["FrequencyUnit"] = phase.Equals("In Process", StringComparison.OrdinalIgnoreCase)
                ? "Pieces"
                : phase.Equals("Coil Change", StringComparison.OrdinalIgnoreCase)
                    ? "MaterialChange"
                    : phase.Equals("Setup", StringComparison.OrdinalIgnoreCase)
                        ? "ToolChange"
                        : "StartOfJob";
        }

        NormalizeTimingFields(row);
    }

    private static Dictionary<string, string> NormalizeRow(Dictionary<string, string> row)
    {
        var normalized = new Dictionary<string, string>(row, StringComparer.OrdinalIgnoreCase);
        CopyAlias(normalized, "RowType", "RecordType", "Section", "Type");
        CopyAlias(normalized, "PartNum", "Part Number", "Part #", "Part No");
        CopyAlias(normalized, "PartDescription", "Part Description");
        CopyAlias(normalized, "ProductGroup", "Product Group");
        CopyAlias(normalized, "InspectionPhase", "Phase", "Inspection Phase");
        CopyAlias(normalized, "Operation", "Operation Name");
        CopyAlias(normalized, "CustomerPartNum", "Customer Part Number", "Customer Part #", "Customer Part No");
        CopyAlias(normalized, "MaterialPartNum", "Material Part Number", "Material Part #", "Material Part No");
        CopyAlias(normalized, "MaterialDescription", "Material Description");
        CopyAlias(normalized, "CharacteristicType", "Attribute/Variable", "DataType", "Inspection Type");
        CopyAlias(normalized, "Nominal", "NominalSpec", "Target");
        CopyAlias(normalized, "LSL", "LowerSpec", "Lower Spec", "Lower Spec Limit");
        CopyAlias(normalized, "USL", "UpperSpec", "Upper Spec", "Upper Spec Limit");
        CopyAlias(normalized, "LCL", "Lower Control", "Lower Control Limit");
        CopyAlias(normalized, "UCL", "Upper Control", "Upper Control Limit");
        CopyAlias(normalized, "UnitOfMeasure", "UOM", "Unit", "Units");
        CopyAlias(normalized, "SampleSize", "Sample Size");
        CopyAlias(normalized, "FrequencyType", "Frequency Type");
        CopyAlias(normalized, "FrequencyValue", "FrequencyQty", "Frequency Value", "Frequency");
        CopyAlias(normalized, "FrequencyUnit", "Frequency Unit");
        CopyAlias(normalized, "AlertRuleSet", "Drift Rule", "Rule Set");
        CopyAlias(normalized, "IsRequiredForCOA", "COA Required");
        CopyAlias(normalized, "COAStatistic", "COA Statistic");
        CopyAlias(normalized, "IsRequired", "RequiresLotEntry", "Required");
        CopyAlias(normalized, "DisplayOrder", "ParameterSeq", "Sort Order");
        CopyAlias(normalized, "Location", "SampleContext", "RequirementText", "Location", "Side", "Sample Location");
        CopyAlias(normalized, "InspectionMethod", "ToolUsed", "Tool Used", "ToolMethod", "Inspection Method", "Measurement Method", "Tool", "Gauge", "Gage");

        CopyAlias(normalized, "FieldName", "Job Data Field", "Job Data Field Name");
        CopyAlias(normalized, "MaterialName", "MaterialRole", "Material Name");
        CopyAlias(normalized, "CharacteristicName", "InspectionParameter", "Variable Name", "Attribute Name");
        var itemName = Value(normalized, "Item Name", "Name");
        var rowType = CanonicalRowType(normalized.GetValueOrDefault("RowType"));
        if (rowType == "Inspection")
        {
            rowType = CanonicalRowType(normalized.GetValueOrDefault("CharacteristicType"));
            if (IsValidRowType(rowType))
            {
                normalized["RowType"] = rowType;
                normalized["CharacteristicType"] = rowType;
            }
        }
        if (!IsValidRowType(rowType))
        {
            rowType = InferRowType(normalized);
            if (IsValidRowType(rowType))
            {
                normalized["RowType"] = rowType;
            }
        }
        if (IsValidRowType(rowType))
        {
            normalized["RowType"] = rowType;
        }
        if (!string.IsNullOrWhiteSpace(itemName))
        {
            if (rowType == "JobData")
            {
                normalized["FieldName"] = itemName;
            }
            else if (rowType == "Material")
            {
                normalized["MaterialName"] = itemName;
            }
            else if (rowType is "Variable" or "Attribute")
            {
                normalized["CharacteristicName"] = itemName;
            }
        }

        if (rowType == "JobData" &&
            string.IsNullOrWhiteSpace(normalized.GetValueOrDefault("FieldName")) &&
            !string.IsNullOrWhiteSpace(Value(normalized, "InspectionParameter", "CharacteristicName")))
        {
            normalized["FieldName"] = Value(normalized, "InspectionParameter", "CharacteristicName").Trim();
        }

        if (rowType is "Variable" or "Attribute" && string.IsNullOrWhiteSpace(normalized.GetValueOrDefault("CharacteristicType")))
        {
            normalized["CharacteristicType"] = rowType;
        }

        normalized["AlertRuleSet"] = string.IsNullOrWhiteSpace(normalized.GetValueOrDefault("AlertRuleSet")) ? "GlobalDefault" : normalized["AlertRuleSet"].Trim();
        normalized["IsRequiredForCOA"] = string.IsNullOrWhiteSpace(normalized.GetValueOrDefault("IsRequiredForCOA")) ? "false" : normalized["IsRequiredForCOA"].Trim();
        normalized["COAStatistic"] = string.IsNullOrWhiteSpace(normalized.GetValueOrDefault("COAStatistic")) ? "Mean" : normalized["COAStatistic"].Trim();
        ApplySpecDefaults(normalized);
        NormalizeTimingFields(normalized);

        return normalized;
    }

    private static void NormalizeTimingFields(Dictionary<string, string> row)
    {
        NormalizeLeadingInt(row, "SampleSize");
        NormalizeLeadingInt(row, "FrequencyValue");

        var unit = row.GetValueOrDefault("FrequencyUnit");
        if (string.IsNullOrWhiteSpace(unit))
        {
            return;
        }

        var clean = unit.Trim().Replace(" ", "", StringComparison.OrdinalIgnoreCase).Replace("/", "", StringComparison.OrdinalIgnoreCase);
        if (clean.Contains("pc", StringComparison.OrdinalIgnoreCase) || clean.Contains("piece", StringComparison.OrdinalIgnoreCase))
        {
            row["FrequencyUnit"] = "Pieces";
        }
        else if (clean.Contains("minute", StringComparison.OrdinalIgnoreCase) || clean.Equals("min", StringComparison.OrdinalIgnoreCase))
        {
            row["FrequencyUnit"] = "Minutes";
        }
        else if (clean.Contains("hour", StringComparison.OrdinalIgnoreCase))
        {
            row["FrequencyUnit"] = "Hours";
        }
        else if (clean.Contains("material", StringComparison.OrdinalIgnoreCase) || clean.Contains("coil", StringComparison.OrdinalIgnoreCase))
        {
            row["FrequencyUnit"] = "MaterialChange";
        }
        else if (clean.Contains("tool", StringComparison.OrdinalIgnoreCase) || clean.Contains("setup", StringComparison.OrdinalIgnoreCase))
        {
            row["FrequencyUnit"] = "ToolChange";
        }
        else if (clean.Contains("shift", StringComparison.OrdinalIgnoreCase))
        {
            row["FrequencyUnit"] = "Shift";
            row["FrequencyType"] = "Event";
        }
    }

    private static void NormalizeLeadingInt(Dictionary<string, string> row, string field)
    {
        var value = row.GetValueOrDefault(field);
        if (string.IsNullOrWhiteSpace(value) || int.TryParse(value, out _))
        {
            return;
        }

        var digits = new string(value.Trim().TakeWhile(char.IsDigit).ToArray());
        if (!string.IsNullOrWhiteSpace(digits))
        {
            row[field] = digits;
        }
    }

    private static void ApplySpecDefaults(Dictionary<string, string> row)
    {
        if (CanonicalRowType(row.GetValueOrDefault("RowType")) != "Variable")
        {
            return;
        }

        var lsl = row.GetValueOrDefault("LSL");
        var usl = row.GetValueOrDefault("USL");
        var nominal = row.GetValueOrDefault("Nominal");
        if (string.IsNullOrWhiteSpace(lsl) && !string.IsNullOrWhiteSpace(usl))
        {
            row["LSL"] = "0";
        }

        if (string.IsNullOrWhiteSpace(usl) && !string.IsNullOrWhiteSpace(lsl))
        {
            row["USL"] = "9999";
        }

        if (string.IsNullOrWhiteSpace(nominal) &&
            decimal.TryParse(row.GetValueOrDefault("LSL"), out var parsedLsl) &&
            decimal.TryParse(row.GetValueOrDefault("USL"), out var parsedUsl))
        {
            row["Nominal"] = ((parsedLsl + parsedUsl) / 2m).ToString("0.####");
        }
    }

    private static string InferRowType(Dictionary<string, string> row)
    {
        if (!string.IsNullOrWhiteSpace(row.GetValueOrDefault("Variable Name")))
        {
            row["CharacteristicName"] = row["Variable Name"];
            row["CharacteristicType"] = "Variable";
            return "Variable";
        }

        if (!string.IsNullOrWhiteSpace(row.GetValueOrDefault("Attribute Name")))
        {
            row["CharacteristicName"] = row["Attribute Name"];
            row["CharacteristicType"] = "Attribute";
            return "Attribute";
        }

        if (!string.IsNullOrWhiteSpace(row.GetValueOrDefault("MaterialName")) ||
            !string.IsNullOrWhiteSpace(row.GetValueOrDefault("MaterialPartNum")))
        {
            return "Material";
        }

        if (!string.IsNullOrWhiteSpace(row.GetValueOrDefault("FieldName")))
        {
            return "JobData";
        }

        return "";
    }

    private static void CopyAlias(Dictionary<string, string> row, string canonicalField, params string[] aliases)
    {
        if (!string.IsNullOrWhiteSpace(row.GetValueOrDefault(canonicalField)))
        {
            return;
        }

        var value = Value(row, aliases);
        if (!string.IsNullOrWhiteSpace(value))
        {
            row[canonicalField] = value;
        }
    }

    private static string Value(Dictionary<string, string> row, params string[] fields)
    {
        foreach (var field in fields)
        {
            if (row.TryGetValue(field, out var value))
            {
                return value;
            }
        }

        return "";
    }

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
                errors.Add($"Row {rowNumber}: RowType must be Part, Variable, Attribute, JobData, or Material.");
                continue;
            }

            if (rowType != "Part" && !IsValidInspectionPhase(row.GetValueOrDefault("InspectionPhase")))
            {
                errors.Add($"Row {rowNumber}: InspectionPhase must be Startup, Setup, In Process, Coil Change, or Spool.");
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
        var hasAnySpec = HasAnySpecValue(row);
        if (!hasAnySpec)
        {
            ValidateControlLimits(row, rowNumber, errors);
            return;
        }

        if (!decimal.TryParse(row.GetValueOrDefault("LSL"), out var lsl) ||
            !decimal.TryParse(row.GetValueOrDefault("USL"), out var usl) ||
            !decimal.TryParse(row.GetValueOrDefault("Nominal"), out _))
        {
            errors.Add($"Row {rowNumber}: Nominal, LSL, and USL must be numeric when any spec value is provided for Variable rows.");
        }
        else if (lsl > usl)
        {
            errors.Add($"Row {rowNumber}: Invalid spec limits. LSL must be less than or equal to USL.");
        }

        ValidateControlLimits(row, rowNumber, errors);
    }

    private static void ValidateControlLimits(Dictionary<string, string> row, int rowNumber, List<string> errors)
    {
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
        Required(row, "MaterialDescription", rowNumber, errors);
        ValidateRequiredFlag(row, rowNumber, errors);
        ValidateDisplayOrder(row, rowNumber, errors);
    }

    private static void ValidateRequiredFlag(Dictionary<string, string> row, int rowNumber, List<string> errors)
    {
        if (!string.IsNullOrWhiteSpace(row.GetValueOrDefault("IsRequired")) &&
            !IsBooleanLike(row.GetValueOrDefault("IsRequired")))
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
                Location = row.GetValueOrDefault("Location")?.Trim() ?? "",
                InspectionMethod = row.GetValueOrDefault("InspectionMethod")?.Trim() ?? "",
                IsRequiredForCoa = bool.Parse(row["IsRequiredForCOA"]),
                CoaStatisticType = CoaStatistic(row)
            };
            repository.Characteristics.Add(characteristic);
        }
        else
        {
            characteristic.Type = Enum.Parse<CharacteristicType>(row["CharacteristicType"], true);
            characteristic.UnitOfMeasure = row.GetValueOrDefault("UnitOfMeasure")?.Trim() ?? "";
            characteristic.Location = row.GetValueOrDefault("Location")?.Trim() ?? "";
            characteristic.InspectionMethod = row.GetValueOrDefault("InspectionMethod")?.Trim() ?? "";
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
        if (isVariable && !HasAnySpecValue(row))
        {
            repository.SpecLimits.RemoveAll(s => s.CharacteristicId == characteristic.Id);
            return;
        }

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
                DisplayOrder = OptionalInt(row, "DisplayOrder", repository.InspectionPlans.Count(item => item.CharacteristicId == characteristic.Id)),
                AlertRuleSet = row["AlertRuleSet"].Trim(),
                Frequency = BuildFrequency(row)
            });
            return;
        }

        plan.InspectionPhase = inspectionPhase;
        plan.SampleSize = int.Parse(row["SampleSize"]);
        plan.DisplayOrder = OptionalInt(row, "DisplayOrder", plan.DisplayOrder);
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
                MaterialPartNum = row["MaterialPartNum"].Trim(),
                MaterialDescription = row["MaterialDescription"].Trim()
            };
            repository.PartMaterialFields.Add(field);
        }

        field.MaterialPartNum = row["MaterialPartNum"].Trim();
        field.MaterialDescription = row["MaterialDescription"].Trim();
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
            "Part" => $"{rowType}|{part}",
            _ => $"{rowType}|{part}|{phase}"
        };
    }

    private static bool IsValidFrequencyPair(FrequencyType type, FrequencyUnit unit)
    {
        return type switch
        {
            FrequencyType.Time => unit is FrequencyUnit.Minutes or FrequencyUnit.Hours,
            FrequencyType.Quantity => unit is FrequencyUnit.Pieces,
            FrequencyType.Event => unit is FrequencyUnit.StartOfJob or FrequencyUnit.MaterialChange or FrequencyUnit.ToolChange or FrequencyUnit.Restart or FrequencyUnit.Shift,
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
            value.Trim().Equals("Coil Change", StringComparison.OrdinalIgnoreCase) ||
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
        if (phase.Equals("Coil Change", StringComparison.OrdinalIgnoreCase))
        {
            return "Coil Change";
        }

        return phase.Equals("Set Up", StringComparison.OrdinalIgnoreCase) ||
            phase.Equals("Setup", StringComparison.OrdinalIgnoreCase)
            ? "Setup"
            : "In Process";
    }

    private void UpsertControlLimit(Dictionary<string, string> row, Part part, ManufacturingProcess process, int operationSeq)
    {
        if (!HasAnySpecValue(row))
        {
            repository.ControlLimits.RemoveAll(limit =>
                limit.PartNum.Equals(part.PartNum, StringComparison.OrdinalIgnoreCase) &&
                limit.ProcessCode.Equals(process.ProcessCode, StringComparison.OrdinalIgnoreCase) &&
                limit.OperationSeq == operationSeq &&
                limit.CharacteristicName.Equals(row["CharacteristicName"], StringComparison.OrdinalIgnoreCase));
            return;
        }

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

    private static bool HasAnySpecValue(Dictionary<string, string> row)
    {
        return !string.IsNullOrWhiteSpace(row.GetValueOrDefault("Nominal")) ||
            !string.IsNullOrWhiteSpace(row.GetValueOrDefault("LSL")) ||
            !string.IsNullOrWhiteSpace(row.GetValueOrDefault("USL"));
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
        if (!row.TryGetValue(field, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return IsTruthy(value);
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
            return "";
        }

        var clean = value.Trim().Replace(" ", "", StringComparison.OrdinalIgnoreCase);
        if (clean.Equals("AcceptReject", StringComparison.OrdinalIgnoreCase) || clean.Equals("Accept/Reject", StringComparison.OrdinalIgnoreCase) || clean.Equals("Attributes", StringComparison.OrdinalIgnoreCase))
        {
            return "Attribute";
        }

        return clean switch
        {
            _ when clean.Equals("Variable", StringComparison.OrdinalIgnoreCase) || clean.Equals("Variables", StringComparison.OrdinalIgnoreCase) => "Variable",
            _ when clean.Equals("Attribute", StringComparison.OrdinalIgnoreCase) => "Attribute",
            _ when clean.Equals("Part", StringComparison.OrdinalIgnoreCase) => "Part",
            _ when clean.Equals("Inspection", StringComparison.OrdinalIgnoreCase) => "Inspection",
            _ when clean.Equals("JobData", StringComparison.OrdinalIgnoreCase) => "JobData",
            _ when clean.Equals("Material", StringComparison.OrdinalIgnoreCase) || clean.Equals("Materials", StringComparison.OrdinalIgnoreCase) => "Material",
            _ => value.Trim()
        };
    }

    private static bool IsBooleanLike(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Trim().Equals("false", StringComparison.OrdinalIgnoreCase) ||
            value.Trim().Equals("no", StringComparison.OrdinalIgnoreCase) ||
            value.Trim().Equals("n", StringComparison.OrdinalIgnoreCase) ||
            value.Trim().Equals("0", StringComparison.OrdinalIgnoreCase) ||
            IsTruthy(value);
    }

    private static bool IsValidRowType(string rowType) => rowType is "Part" or "Variable" or "Attribute" or "JobData" or "Material";

    private static string CleanProductGroup(string? value) => string.IsNullOrWhiteSpace(value) ? "General" : value.Trim();
}

internal sealed record PhaseMatrixDefinition(string CanonicalName, IReadOnlyList<string> Prefixes);
