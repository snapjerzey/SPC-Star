using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using System.Globalization;

namespace SPCStar.Core.Services;

public sealed class SetupTemplateExportService(ISpcRepository repository)
{
    private static readonly string[] Headers =
    [
        "Part Number",
        "Part Description",
        "Product Group",
        "Blank Code",
        "Hole Size",
        "Inspection Phase",
        "Operation",
        "Job Data Field",
        "Material Name",
        "Material Part Number",
        "Material Description",
        "Variable Name",
        "Attribute Name",
        "Required",
        "Sort Order",
        "Unit",
        "Location",
        "Inspection Method",
        "Target",
        "Lower Spec",
        "Upper Spec",
        "Lower Control",
        "Upper Control",
        "Drift Rule",
        "Startup Required",
        "Startup Sample Size",
        "Startup Frequency Type",
        "Startup Frequency",
        "Startup First Due",
        "Startup Frequency Unit",
        "Startup Drift Rule",
        "Setup Required",
        "Setup Sample Size",
        "Setup Frequency Type",
        "Setup Frequency",
        "Setup First Due",
        "Setup Frequency Unit",
        "Setup Drift Rule",
        "Coil Change Required",
        "Coil Change Sample Size",
        "Coil Change Frequency Type",
        "Coil Change Frequency",
        "Coil Change First Due",
        "Coil Change Frequency Unit",
        "Coil Change Drift Rule",
        "In Process Required",
        "In Process Sample Size",
        "In Process Frequency Type",
        "In Process Frequency",
        "In Process First Due",
        "In Process Frequency Unit",
        "In Process Drift Rule",
        "Spool Required",
        "Spool Sample Size",
        "Spool Frequency Type",
        "Spool Frequency",
        "Spool First Due",
        "Spool Frequency Unit",
        "Spool Drift Rule",
        "End of Spool Required",
        "End of Spool Sample Size",
        "End of Spool Frequency Type",
        "End of Spool Frequency",
        "End of Spool First Due",
        "End of Spool Frequency Unit",
        "End of Spool Drift Rule"
    ];

    private static readonly string[] Phases = ["Startup", "Setup", "Coil Change", "In Process", "Spool", "End of Spool"];

    public string ExportCsv()
    {
        var rows = new List<IReadOnlyDictionary<string, string>>();
        rows.AddRange(InspectionRows());
        rows.AddRange(JobDataRows());
        rows.AddRange(MaterialRows());

        return CsvSupport.WriteRows(Headers, rows);
    }

    private IEnumerable<IReadOnlyDictionary<string, string>> InspectionRows()
    {
        var query =
            from part in repository.Parts
            join operation in repository.Operations on part.Id equals operation.PartId
            join process in repository.Processes on operation.ProcessId equals process.Id
            join characteristic in repository.Characteristics on operation.Id equals characteristic.OperationId
            join specLimit in repository.SpecLimits on characteristic.Id equals specLimit.CharacteristicId into specLimits
            from spec in specLimits.DefaultIfEmpty()
            let controlLimit = repository.ControlLimits.FirstOrDefault(limit =>
                limit.PartNum.Equals(part.PartNum, StringComparison.OrdinalIgnoreCase) &&
                limit.ProcessCode.Equals(process.ProcessCode, StringComparison.OrdinalIgnoreCase) &&
                limit.OperationSeq == operation.OperationSeq &&
                limit.CharacteristicName.Equals(characteristic.Name, StringComparison.OrdinalIgnoreCase))
            orderby part.PartNum, process.ProcessCode, operation.OperationSeq, PlansFor(characteristic).Select(plan => plan.DisplayOrder).DefaultIfEmpty(0).Min(), characteristic.Name
            select new
            {
                Part = part,
                Process = process,
                Operation = operation,
                Characteristic = characteristic,
                Spec = spec,
                Control = controlLimit,
                Plans = PlansFor(characteristic)
            };

        foreach (var item in query)
        {
            var row = BaseRow(item.Part);
            row["Operation"] = item.Process.ProcessCode;
            row["Sort Order"] = item.Plans.Select(plan => plan.DisplayOrder).DefaultIfEmpty(0).Min().ToString(CultureInfo.InvariantCulture);
            row["Unit"] = item.Characteristic.UnitOfMeasure;
            row["Location"] = item.Characteristic.Location;
            row["Inspection Method"] = item.Characteristic.InspectionMethod;
            row["Required"] = item.Plans.Any() ? "TRUE" : "";
            row["Drift Rule"] = item.Plans.FirstOrDefault()?.AlertRuleSet ?? "";

            if (item.Characteristic.Type == CharacteristicType.Variable)
            {
                row["Variable Name"] = item.Characteristic.Name;
                row["Target"] = FormatDecimal(item.Spec?.Nominal);
                row["Lower Spec"] = FormatDecimal(item.Spec?.Lsl);
                row["Upper Spec"] = FormatDecimal(item.Spec?.Usl);
                if (item.Control?.Lcl < item.Control?.Ucl)
                {
                    row["Lower Control"] = FormatDecimal(item.Control?.Lcl);
                    row["Upper Control"] = FormatDecimal(item.Control?.Ucl);
                }
            }
            else
            {
                row["Attribute Name"] = item.Characteristic.Name;
            }

            ApplyPlanPhaseColumns(row, item.Plans);
            yield return row;
        }
    }

    private IEnumerable<IReadOnlyDictionary<string, string>> JobDataRows()
    {
        var rows =
            from field in repository.PartJobDataFields
            join part in repository.Parts on field.PartId equals part.Id
            group field by new { Part = part, FieldNameKey = field.FieldName.ToUpperInvariant() } into fields
            let first = fields.OrderBy(field => field.DisplayOrder).First()
            orderby fields.Key.Part.PartNum, fields.Select(field => field.DisplayOrder).DefaultIfEmpty(0).Min(), first.FieldName
            select new { fields.Key.Part, first.FieldName, Fields = fields.ToArray() };

        foreach (var item in rows)
        {
            var row = BaseRow(item.Part);
            row["Job Data Field"] = item.FieldName;
            row["Required"] = item.Fields.Any(field => field.IsRequired) ? "TRUE" : "FALSE";
            row["Sort Order"] = item.Fields.Select(field => field.DisplayOrder).DefaultIfEmpty(0).Min().ToString(CultureInfo.InvariantCulture);
            foreach (var field in item.Fields)
            {
                var prefix = PhasePrefix(field.InspectionPhase);
                if (prefix == "")
                {
                    continue;
                }

                row[$"{prefix} Required"] = field.IsRequired ? "TRUE" : "FALSE";
            }

            yield return row;
        }
    }

    private IEnumerable<IReadOnlyDictionary<string, string>> MaterialRows()
    {
        var rows =
            from field in repository.PartMaterialFields
            join part in repository.Parts on field.PartId equals part.Id
            group field by new
            {
                Part = part,
                MaterialNameKey = field.MaterialName.ToUpperInvariant(),
                MaterialPartNumKey = field.MaterialPartNum.ToUpperInvariant()
            } into fields
            let first = fields.OrderBy(field => field.DisplayOrder).First()
            orderby fields.Key.Part.PartNum, fields.Select(field => field.DisplayOrder).DefaultIfEmpty(0).Min(), first.MaterialName
            select new { fields.Key.Part, Field = first, Fields = fields.ToArray() };

        foreach (var item in rows)
        {
            var row = BaseRow(item.Part);
            row["Material Name"] = item.Field.MaterialName;
            row["Material Part Number"] = item.Field.MaterialPartNum;
            row["Material Description"] = item.Field.MaterialDescription;
            row["Required"] = item.Fields.Any(field => field.IsRequired) ? "TRUE" : "FALSE";
            row["Sort Order"] = item.Fields.Select(field => field.DisplayOrder).DefaultIfEmpty(0).Min().ToString(CultureInfo.InvariantCulture);
            foreach (var field in item.Fields)
            {
                var prefix = PhasePrefix(field.InspectionPhase);
                if (prefix == "")
                {
                    continue;
                }

                row[$"{prefix} Required"] = field.IsRequired ? "TRUE" : "FALSE";
            }

            yield return row;
        }
    }

    private List<InspectionPlan> PlansFor(Characteristic characteristic)
    {
        return repository.InspectionPlans
            .Where(plan => plan.CharacteristicId == characteristic.Id)
            .OrderBy(plan => PhaseSort(plan.InspectionPhase))
            .ThenBy(plan => plan.DisplayOrder)
            .ToList();
    }

    private static void ApplyPlanPhaseColumns(Dictionary<string, string> row, IReadOnlyList<InspectionPlan> plans)
    {
        foreach (var plan in plans)
        {
            var prefix = PhasePrefix(plan.InspectionPhase);
            if (prefix == "")
            {
                continue;
            }

            row[$"{prefix} Required"] = "TRUE";
            row[$"{prefix} Sample Size"] = plan.SampleSize.ToString(CultureInfo.InvariantCulture);
            row[$"{prefix} Frequency Type"] = plan.Frequency.Type.ToString();
            row[$"{prefix} Frequency"] = plan.Frequency.Value.ToString(CultureInfo.InvariantCulture);
            row[$"{prefix} First Due"] = plan.Frequency.FirstDueValue?.ToString(CultureInfo.InvariantCulture) ?? "";
            row[$"{prefix} Frequency Unit"] = plan.Frequency.Unit.ToString();
            row[$"{prefix} Drift Rule"] = plan.AlertRuleSet;
        }
    }

    private static Dictionary<string, string> BaseRow(Part part)
    {
        var row = Headers.ToDictionary(header => header, _ => "", StringComparer.OrdinalIgnoreCase);
        row["Part Number"] = part.PartNum;
        row["Part Description"] = part.Description;
        row["Product Group"] = string.IsNullOrWhiteSpace(part.ProductGroup) ? "General" : part.ProductGroup;
        row["Blank Code"] = part.BlankCode;
        row["Hole Size"] = part.HoleSize;
        return row;
    }

    private static string PhasePrefix(string? phase)
    {
        var normalized = NormalizePhase(phase);
        return Phases.FirstOrDefault(item => item.Equals(normalized, StringComparison.OrdinalIgnoreCase)) ?? "";
    }

    private static int PhaseSort(string? phase)
    {
        var normalized = NormalizePhase(phase);
        var index = Array.FindIndex(Phases, item => item.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? 999 : index;
    }

    private static string NormalizePhase(string? phase)
    {
        if (string.IsNullOrWhiteSpace(phase))
        {
            return "In Process";
        }

        var value = phase.Trim();
        if (value.Equals("Spool Start", StringComparison.OrdinalIgnoreCase))
        {
            return "Spool";
        }
        if (value.Equals("EndOfSpool", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Spool End", StringComparison.OrdinalIgnoreCase))
        {
            return "End of Spool";
        }

        return Phases.FirstOrDefault(item => item.Equals(value, StringComparison.OrdinalIgnoreCase)) ?? value;
    }

    private static string FormatDecimal(decimal? value)
    {
        return value?.ToString("0.####", CultureInfo.InvariantCulture) ?? "";
    }
}
