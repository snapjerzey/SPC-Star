using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;

namespace SPCStar.Core.Services;

public sealed record UserSetupDto(string UserName, IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions);

public sealed record UpsertUserRequest(string UserName, string Password, IReadOnlyList<string> Roles);

public sealed record UpsertInspectionSetupRequest(
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
    decimal? Lcl,
    decimal? Ucl,
    string UnitOfMeasure,
    int SampleSize,
    FrequencyType FrequencyType,
    int FrequencyValue,
    FrequencyUnit FrequencyUnit,
    string AlertRuleSet,
    bool IsRequiredForCoa);

public sealed class SetupManagementService(ISpcRepository repository)
{
    public IReadOnlyList<UserSetupDto> GetUsers()
    {
        return repository.Users
            .OrderBy(user => user.UserName)
            .Select(user => new UserSetupDto(
                user.UserName,
                user.Roles.Select(role => role.Name).OrderBy(role => role).ToArray(),
                user.Roles.SelectMany(role => role.Permissions).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(permission => permission).ToArray()))
            .ToArray();
    }

    public IReadOnlyList<string> GetRoles()
    {
        return repository.Roles
            .Select(role => role.Name)
            .OrderBy(role => role)
            .ToArray();
    }

    public ServiceResult<UserSetupDto> UpsertUser(UpsertUserRequest request)
    {
        var errors = ValidateUser(request);
        if (errors.Count > 0)
        {
            return ServiceResult<UserSetupDto>.Fail(errors);
        }

        var roles = request.Roles
            .Select(roleName => repository.Roles.First(role => role.Name.Equals(roleName.Trim(), StringComparison.OrdinalIgnoreCase)))
            .DistinctBy(role => role.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var user = repository.Users.FirstOrDefault(item => item.UserName.Equals(request.UserName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (user is null)
        {
            var (hash, salt) = PasswordHasher.HashPassword(request.Password);
            user = new User { UserName = request.UserName.Trim(), PasswordHash = hash, PasswordSalt = salt };
            repository.Users.Add(user);
        }
        else if (!string.IsNullOrWhiteSpace(request.Password))
        {
            var (hash, salt) = PasswordHasher.HashPassword(request.Password);
            user.PasswordHash = hash;
            user.PasswordSalt = salt;
        }

        user.Roles.Clear();
        user.Roles.AddRange(roles);
        return ServiceResult<UserSetupDto>.Ok(GetUsers().First(item => item.UserName.Equals(user.UserName, StringComparison.OrdinalIgnoreCase)));
    }

    public ServiceResult<InspectionPlanSetupDto> UpsertInspectionSetup(UpsertInspectionSetupRequest request)
    {
        var errors = ValidateInspectionSetup(request);
        if (errors.Count > 0)
        {
            return ServiceResult<InspectionPlanSetupDto>.Fail(errors);
        }

        var part = repository.Parts.FirstOrDefault(item => item.PartNum.Equals(request.PartNum.Trim(), StringComparison.OrdinalIgnoreCase));
        if (part is null)
        {
            part = new Part { PartNum = request.PartNum.Trim(), Description = request.PartDescription.Trim() };
            repository.Parts.Add(part);
        }
        else
        {
            part.Description = request.PartDescription.Trim();
        }

        var process = repository.Processes.FirstOrDefault(item => item.ProcessCode.Equals(request.ProcessCode.Trim(), StringComparison.OrdinalIgnoreCase));
        if (process is null)
        {
            process = new ManufacturingProcess { ProcessCode = request.ProcessCode.Trim(), Description = request.ProcessDescription.Trim() };
            repository.Processes.Add(process);
        }
        else
        {
            process.Description = request.ProcessDescription.Trim();
        }

        var operation = repository.Operations.FirstOrDefault(item =>
            item.PartId == part.Id &&
            item.ProcessId == process.Id &&
            item.OperationSeq == request.OperationSeq);
        if (operation is null)
        {
            operation = new Operation { PartId = part.Id, ProcessId = process.Id, OperationSeq = request.OperationSeq };
            repository.Operations.Add(operation);
        }

        var characteristic = repository.Characteristics.FirstOrDefault(item =>
            item.OperationId == operation.Id &&
            item.Name.Equals(request.CharacteristicName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (characteristic is null)
        {
            characteristic = new Characteristic
            {
                OperationId = operation.Id,
                Name = request.CharacteristicName.Trim(),
                Type = request.CharacteristicType,
                UnitOfMeasure = request.UnitOfMeasure.Trim(),
                IsRequiredForCoa = request.IsRequiredForCoa
            };
            repository.Characteristics.Add(characteristic);
        }
        else
        {
            characteristic.Type = request.CharacteristicType;
            characteristic.UnitOfMeasure = request.UnitOfMeasure.Trim();
            characteristic.IsRequiredForCoa = request.IsRequiredForCoa;
        }

        var spec = repository.SpecLimits.FirstOrDefault(item => item.CharacteristicId == characteristic.Id);
        if (spec is null)
        {
            repository.SpecLimits.Add(new SpecLimit { CharacteristicId = characteristic.Id, Nominal = request.Nominal, Lsl = request.Lsl, Usl = request.Usl });
        }
        else
        {
            spec.Nominal = request.Nominal;
            spec.Lsl = request.Lsl;
            spec.Usl = request.Usl;
        }

        var plan = repository.InspectionPlans.FirstOrDefault(item => item.CharacteristicId == characteristic.Id);
        if (plan is null)
        {
            plan = new InspectionPlan { CharacteristicId = characteristic.Id, SampleSize = request.SampleSize, AlertRuleSet = request.AlertRuleSet.Trim() };
            repository.InspectionPlans.Add(plan);
        }

        plan.SampleSize = request.SampleSize;
        plan.AlertRuleSet = request.AlertRuleSet.Trim();
        plan.Frequency = new InspectionFrequency { Type = request.FrequencyType, Value = request.FrequencyValue, Unit = request.FrequencyUnit };
        if (request.CharacteristicType == CharacteristicType.Variable)
        {
            UpsertControlLimit(request);
        }
        else
        {
            RemoveControlLimit(request);
        }

        return ServiceResult<InspectionPlanSetupDto>.Ok(new SetupQueryService(repository).GetInspectionPlans(request.PartNum).First(item =>
            item.ProcessCode.Equals(request.ProcessCode, StringComparison.OrdinalIgnoreCase) &&
            item.OperationSeq == request.OperationSeq &&
            item.CharacteristicName.Equals(request.CharacteristicName, StringComparison.OrdinalIgnoreCase)));
    }

    private void UpsertControlLimit(UpsertInspectionSetupRequest request)
    {
        var lcl = request.Lcl ?? request.Lsl;
        var ucl = request.Ucl ?? request.Usl;
        var limit = repository.ControlLimits.FirstOrDefault(item =>
            item.PartNum.Equals(request.PartNum.Trim(), StringComparison.OrdinalIgnoreCase) &&
            item.ProcessCode.Equals(request.ProcessCode.Trim(), StringComparison.OrdinalIgnoreCase) &&
            item.OperationSeq == request.OperationSeq &&
            item.CharacteristicName.Equals(request.CharacteristicName.Trim(), StringComparison.OrdinalIgnoreCase));

        if (limit is null)
        {
            repository.ControlLimits.Add(new ControlLimitSet
            {
                PartNum = request.PartNum.Trim(),
                ProcessCode = request.ProcessCode.Trim(),
                OperationSeq = request.OperationSeq,
                CharacteristicName = request.CharacteristicName.Trim(),
                CenterLine = request.Nominal,
                Lcl = lcl,
                Ucl = ucl
            });
            return;
        }

        limit.CenterLine = request.Nominal;
        limit.Lcl = lcl;
        limit.Ucl = ucl;
    }

    private void RemoveControlLimit(UpsertInspectionSetupRequest request)
    {
        repository.ControlLimits.RemoveAll(item =>
            item.PartNum.Equals(request.PartNum.Trim(), StringComparison.OrdinalIgnoreCase) &&
            item.ProcessCode.Equals(request.ProcessCode.Trim(), StringComparison.OrdinalIgnoreCase) &&
            item.OperationSeq == request.OperationSeq &&
            item.CharacteristicName.Equals(request.CharacteristicName.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private List<string> ValidateUser(UpsertUserRequest request)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(request.UserName))
        {
            errors.Add("UserName is required.");
        }

        var existing = repository.Users.Any(user => user.UserName.Equals(request.UserName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (!existing && string.IsNullOrWhiteSpace(request.Password))
        {
            errors.Add("Password is required for new users.");
        }

        if (request.Roles.Count == 0)
        {
            errors.Add("At least one role is required.");
        }

        foreach (var role in request.Roles)
        {
            if (repository.Roles.All(item => !item.Name.Equals(role.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add($"Unknown role {role}.");
            }
        }

        return errors;
    }

    private static List<string> ValidateInspectionSetup(UpsertInspectionSetupRequest request)
    {
        var errors = new List<string>();
        Required(request.PartNum, nameof(request.PartNum), errors);
        Required(request.PartDescription, nameof(request.PartDescription), errors);
        Required(request.ProcessCode, nameof(request.ProcessCode), errors);
        Required(request.ProcessDescription, nameof(request.ProcessDescription), errors);
        Required(request.CharacteristicName, nameof(request.CharacteristicName), errors);
        Required(request.UnitOfMeasure, nameof(request.UnitOfMeasure), errors);
        Required(request.AlertRuleSet, nameof(request.AlertRuleSet), errors);

        if (request.OperationSeq <= 0) errors.Add("OperationSeq must be greater than zero.");
        if (request.CharacteristicType == CharacteristicType.Variable && request.Lsl >= request.Usl) errors.Add("LSL must be less than USL.");
        if (request.CharacteristicType == CharacteristicType.Variable && request.Lcl.HasValue && request.Ucl.HasValue && request.Lcl.Value >= request.Ucl.Value) errors.Add("LCL must be less than UCL.");
        if (request.SampleSize <= 0) errors.Add("SampleSize must be greater than zero.");
        if (request.FrequencyValue <= 0) errors.Add("FrequencyValue must be greater than zero.");
        if (!string.Equals(request.AlertRuleSet, "WesternElectric", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("MVP supports WesternElectric alert rules.");
        }

        if (!IsValidFrequencyPair(request.FrequencyType, request.FrequencyUnit))
        {
            errors.Add("FrequencyType and FrequencyUnit are not compatible.");
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

    private static void Required(string value, string name, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{name} is required.");
        }
    }
}
