using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;

namespace SPCStar.Core.Services;

public sealed record ChangeRecordSnapshot(string Area, string Record, string PartNum, string Operation,
    string Phase, IReadOnlyDictionary<string, string?> Fields);
public sealed record ChangeCapture(IReadOnlyDictionary<string, ChangeRecordSnapshot> Records);
public sealed record ChangeHistoryTarget(string Type, Guid Id);
public sealed record ChangeHistoryQuery(string? Search = null, string? UserName = null, string? Area = null,
    string? PartNum = null, DateTimeOffset? From = null, DateTimeOffset? To = null, int Page = 1, int PageSize = 50);
public sealed record ChangeHistoryPage(int Total, int Page, int PageSize, IReadOnlyList<ChangeHistoryEntry> Entries);

public sealed class ChangeHistoryService(ISpcRepository repository)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { Converters = { new JsonStringEnumConverter() } };

    public ChangeCapture Capture(ChangeHistoryTarget? target = null)
    {
        var records = new Dictionary<string, ChangeRecordSnapshot>();
        var parts = repository.Parts.ToDictionary(item => item.Id);
        var processes = repository.Processes.ToDictionary(item => item.Id);
        var operations = repository.Operations.ToDictionary(item => item.Id);
        var characteristics = repository.Characteristics.ToDictionary(item => item.Id);
        var phaseSpecIds = repository.InspectionPlans.Select(item=>item.CharacteristicId).ToHashSet();
        (string Part, string Operation) Context(Guid characteristicId)
        {
            if (!characteristics.TryGetValue(characteristicId, out var characteristic) ||
                !operations.TryGetValue(characteristic.OperationId, out var operation)) return ("", "");
            return (parts.GetValueOrDefault(operation.PartId)?.PartNum ?? "",
                $"{processes.GetValueOrDefault(operation.ProcessId)?.ProcessCode} {operation.OperationSeq}".Trim());
        }
        void Add(string key, string area, string record, object fields, string part = "", string operation = "", string phase = "") =>
            records.Add(key, new(area, record, part, operation, phase, Flatten(fields)));
        foreach (var item in repository.Parts)
            Add($"part:{item.Id}", "Parts & Inspections", item.PartNum, new { item.PartNum, item.Description, item.ProductGroup, item.BlankCode, item.HoleSize }, item.PartNum);
        foreach (var item in repository.Processes)
            Add($"process:{item.Id}", "Parts & Inspections", item.ProcessCode, new { item.ProcessCode, item.Description });
        foreach (var item in repository.Operations)
            Add($"operation:{item.Id}", "Parts & Inspections", "Operation", new { Process=processes.GetValueOrDefault(item.ProcessId)?.ProcessCode, item.OperationSeq },
                parts.GetValueOrDefault(item.PartId)?.PartNum ?? "", $"{processes.GetValueOrDefault(item.ProcessId)?.ProcessCode} {item.OperationSeq}");
        foreach (var item in repository.Characteristics)
        {
            var context=Context(item.Id);
            Add($"variable:{item.Id}", "Parts & Inspections", item.Name,
                new { item.Name, item.Type, item.UnitOfMeasure, item.Location, item.InspectionMethod }, context.Part, context.Operation);
        }
        // Phase plans hold editable specs; their legacy compatibility copies are derived data.
        foreach (var item in repository.SpecLimits.Where(item=>!phaseSpecIds.Contains(item.CharacteristicId)))
        {
            var context=Context(item.CharacteristicId);
            Add($"spec:{item.CharacteristicId}", "Parts & Inspections", $"{characteristics.GetValueOrDefault(item.CharacteristicId)?.Name} / master specs",
                new { Target=item.Nominal, LSL=item.Lsl, USL=item.Usl }, context.Part, context.Operation);
        }
        foreach (var item in repository.InspectionPlans)
        {
            var context=Context(item.CharacteristicId);
            Add($"plan:{item.Id}", "Parts & Inspections", characteristics.GetValueOrDefault(item.CharacteristicId)?.Name ?? "Inspection",
                new { item.InspectionPhase, Target=item.Nominal, LSL=item.Lsl, USL=item.Usl, item.SampleSize, Order=item.DisplayOrder,
                    item.AlertRuleSet, item.Frequency }, context.Part, context.Operation, item.InspectionPhase);
        }
        foreach (var item in repository.PartJobDataFields)
            Add($"job-field:{item.Id}", "Parts & Inspections", $"Job data / {item.FieldName}", new { item.FieldName, item.Instruction, item.IsRequired, Order=item.DisplayOrder, item.InspectionPhase },
                parts.GetValueOrDefault(item.PartId)?.PartNum ?? "", phase:item.InspectionPhase);
        foreach (var item in repository.PartMaterialFields)
            Add($"material-field:{item.Id}", "Parts & Inspections", $"Material / {item.MaterialName}",
                new { item.MaterialName, item.MaterialPartNum, item.MaterialDescription, item.IsRequired, Order=item.DisplayOrder, item.InspectionPhase },
                parts.GetValueOrDefault(item.PartId)?.PartNum ?? "", phase:item.InspectionPhase);
        foreach (var item in repository.Resources)
            Add($"machine:{item.Id}", "Machines", item.ResourceId, new { item.ResourceId, item.Description, item.DeviceProfile, item.SerialBaudRate,
                ProductGroups=item.ProductGroups.Order(StringComparer.OrdinalIgnoreCase).ToArray() });
        foreach (var item in repository.Users)
            Add($"user:{item.Id}", "Users", item.UserName, new { item.UserName, item.Shift,
                Roles=item.Roles.Select(role=>role.Name).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                ProductGroups=item.ProductGroups.Order(StringComparer.OrdinalIgnoreCase).ToArray(), Password=item.PasswordHash+":"+item.PasswordSalt });
        foreach (var item in repository.Roles)
            Add($"role:{item.Id}", "Users", $"Role / {item.Name}", new { item.Name, Permissions=item.Permissions.Order(StringComparer.OrdinalIgnoreCase).ToArray() });
        Add("settings", "Rules", "Rules", repository.Settings);
        foreach (var item in repository.ControlLimits)
        {
            Add($"control:{item.PartNum}|{item.ProcessCode}|{item.OperationSeq}|{item.CharacteristicName}", "Parts & Inspections", $"{item.CharacteristicName} / control limits",
                new { LCL=item.Lcl, Center=item.CenterLine, UCL=item.Ucl }, item.PartNum, $"{item.ProcessCode} {item.OperationSeq}");
        }
        if (target?.Type == "Measurement" && repository.Measurements.FirstOrDefault(item=>item.Id==target.Id) is { } reading)
            Add($"correction:{reading.Id}", "Inspection Corrections", $"Job {reading.JobNum} / {reading.CharacteristicName}",
                new { reading.Value, reading.Timestamp, reading.InspectionPhase }, reading.PartNum, $"{reading.ProcessCode} {reading.OperationSeq}", reading.InspectionPhase);
        if (target?.Type == "MachineCounter" && repository.JobPhaseCompletions.FirstOrDefault(item=>item.Id==target.Id) is { } completion)
            Add($"correction:{completion.Id}", "Inspection Corrections", $"Job {completion.JobNum} / Machine Counter", new { completion.MachineCounter },
                completion.PartNum, $"{completion.ProcessCode} {completion.OperationSeq}", completion.InspectionPhase);
        if (target?.Type == "MaterialLot" && repository.MaterialChanges.FirstOrDefault(item=>item.Id==target.Id) is { } material)
            Add($"correction:{material.Id}", "Inspection Corrections", $"Job {material.JobNum} / Material {material.MaterialPartNum}",
                new { material.NewLotNum }, material.PartNum);
        return new(records);
    }

    public ChangeHistoryEntry? RecordSaved(ChangeCapture before, string actor, string source, bool isImport = false,
        ChangeHistoryTarget? target = null, string? reason = null, string? groupId = null)
    {
        if (isImport) return AddAction(actor, "Imports", source, groupId);
        var after=Capture(target);
        var changes=new List<SavedRecordChange>();
        foreach (var key in before.Records.Keys.Union(after.Records.Keys))
        {
            before.Records.TryGetValue(key,out var old); after.Records.TryGetValue(key,out var current);
            var record=current ?? old!;
            var fields=new List<SavedFieldChange>();
            foreach (var field in (old?.Fields.Keys ?? []).Union(current?.Fields.Keys ?? []))
            {
                var previous=old?.Fields.GetValueOrDefault(field); var value=current?.Fields.GetValueOrDefault(field);
                if (previous==value) continue;
                if (field=="Password") { if (old is not null && current is not null) fields.Add(new("Password", "Not displayed", "Reset")); continue; }
                fields.Add(new(Friendly(field),previous,value));
            }
            if (fields.Count>0) changes.Add(new(record.Area,record.Record,record.PartNum,record.Operation,record.Phase,
                old is null ? "Added" : current is null ? "Deleted" : "Updated",fields));
        }
        if (changes.Count==0) return null;
        var summary=changes.Count==1 ? $"{changes[0].Record}: {changes[0].Action.ToLowerInvariant()}" : $"{changes.Count} records updated";
        var entry=new ChangeHistoryEntry(Guid.NewGuid(),DateTimeOffset.UtcNow,actor,source,summary,changes,reason,groupId);
        repository.ChangeHistory.Add(entry);
        return entry;
    }

    public ChangeHistoryEntry AddAction(string actor, string area, string summary, string? groupId = null)
    {
        var entry=new ChangeHistoryEntry(Guid.NewGuid(),DateTimeOffset.UtcNow,actor,area,summary,[],GroupId:groupId);
        repository.ChangeHistory.Add(entry); return entry;
    }

    public ChangeHistoryPage Read(ChangeHistoryQuery query)
    {
        var entries=Filter(query).ToArray();
        var size=Math.Clamp(query.PageSize,1,100);var page=Math.Clamp(query.Page,1,Math.Max(1,(int)Math.Ceiling((double)entries.Length/size)));
        return new(entries.Length,page,size,entries.Skip((page-1)*size).Take(size).ToArray());
    }

    public string Export(ChangeHistoryQuery query) => CsvSupport.WriteRows(
        ["Date / Time (UTC)","Changed By","Area","Summary","Changes","Reason"], Filter(query).Select(entry=>new Dictionary<string,string> {
            ["Date / Time (UTC)"]=entry.SavedAt.ToString("O"),["Changed By"]=entry.UserName,["Area"]=entry.Source,["Summary"]=entry.Summary,
            ["Changes"]=string.Join("; ",entry.Changes.SelectMany(record=>record.Fields.Select(field=>$"{record.PartNum} / {record.Operation} / {record.Phase} / {record.Record}: {field.Field}: {field.Before ?? "(blank)"} -> {field.After ?? "(blank)"}"))),
            ["Reason"]=entry.Reason ?? "" }));

    private IEnumerable<ChangeHistoryEntry> Filter(ChangeHistoryQuery query) => repository.ChangeHistory.ToArray()
        .GroupBy(entry=>string.IsNullOrEmpty(entry.GroupId) ? entry.Id.ToString() : $"{entry.UserName}|{entry.GroupId}")
        .Select(group=>group.Count()==1 ? group.First() : group.OrderByDescending(entry=>entry.SavedAt).First() with {
            Changes=group.OrderBy(entry=>entry.SavedAt).SelectMany(entry=>entry.Changes).ToArray(),
            Summary=$"{group.Sum(entry=>entry.Changes.Count)} records updated" })
        .Where(entry=>(!query.From.HasValue || entry.SavedAt>=query.From) && (!query.To.HasValue || entry.SavedAt<=query.To) &&
            (string.IsNullOrWhiteSpace(query.UserName) || entry.UserName.Contains(query.UserName.Trim(),StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(query.Area) || entry.Source.Equals(query.Area,StringComparison.OrdinalIgnoreCase) || entry.Changes.Any(change=>change.Area.Equals(query.Area,StringComparison.OrdinalIgnoreCase))) &&
            (string.IsNullOrWhiteSpace(query.PartNum) || entry.Changes.Any(change=>change.PartNum.Equals(query.PartNum.Trim(),StringComparison.OrdinalIgnoreCase))) &&
            (string.IsNullOrWhiteSpace(query.Search) || JsonSerializer.Serialize(entry).Contains(query.Search.Trim(),StringComparison.OrdinalIgnoreCase)))
        .OrderByDescending(entry=>entry.SavedAt).ThenByDescending(entry=>entry.Id);

    private static Dictionary<string,string?> Flatten(object value)
    {
        var fields=new Dictionary<string,string?>();
        void Visit(JsonElement element,string path)
        {
            if (element.ValueKind==JsonValueKind.Object) foreach (var property in element.EnumerateObject()) Visit(property.Value,string.IsNullOrEmpty(path)?property.Name:$"{path}.{property.Name}");
            else fields[path]=element.ValueKind switch { JsonValueKind.Null=>null,JsonValueKind.String=>element.GetString(),JsonValueKind.Array=>string.Join(", ",element.EnumerateArray().Select(item=>item.ToString())),_=>element.GetRawText() };
        }
        Visit(JsonSerializer.SerializeToElement(value,JsonOptions),"");return fields;
    }
    private static string Friendly(string name) => Regex.Replace(name.Replace('.', ' '),"(?<=[a-z])(?=[A-Z])"," ");
}
