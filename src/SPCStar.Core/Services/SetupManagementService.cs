using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;

namespace SPCStar.Core.Services;

public sealed record UserSetupDto(string UserName, IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions);

public sealed record UpsertUserRequest(string UserName, string Password, IReadOnlyList<string> Roles);

public sealed record UpdateSettingsRequest(string GlobalAlertRuleSet, CustomDriftRuleSetupDto? CustomDriftRule = null);

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
    bool IsRequiredForCoa,
    string? OriginalProcessCode = null,
    int? OriginalOperationSeq = null,
    string? OriginalCharacteristicName = null,
    CoaStatisticType CoaStatisticType = CoaStatisticType.Mean,
    string InspectionPhase = "In Process");

public sealed record UpsertPartJobDataFieldRequest(
    string PartNum,
    string InspectionPhase,
    string FieldName,
    bool IsRequired,
    int DisplayOrder,
    string? OriginalFieldName = null);

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

    public SettingsSetupDto GetSettings()
    {
        return SettingsDto();
    }

    public ServiceResult<SettingsSetupDto> UpdateSettings(UpdateSettingsRequest request)
    {
        if (!IsSupportedRuleSet(request.GlobalAlertRuleSet) || string.Equals(request.GlobalAlertRuleSet, "GlobalDefault", StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResult<SettingsSetupDto>.Fail("GlobalAlertRuleSet is not supported.");
        }

        repository.Settings.GlobalAlertRuleSet = request.GlobalAlertRuleSet.Trim();
        if (request.CustomDriftRule is not null)
        {
            var customErrors = ValidateCustomRule(request.CustomDriftRule);
            if (customErrors.Count > 0)
            {
                return ServiceResult<SettingsSetupDto>.Fail(customErrors);
            }

            repository.Settings.CustomDriftRule = new CustomDriftRuleSettings
            {
                Name = string.IsNullOrWhiteSpace(request.CustomDriftRule.Name) ? "Custom Drift Rule" : request.CustomDriftRule.Name.Trim(),
                WindowSize = request.CustomDriftRule.WindowSize,
                SigmaThreshold = request.CustomDriftRule.SigmaThreshold,
                MinimumPointsBeyondThreshold = request.CustomDriftRule.MinimumPointsBeyondThreshold,
                Direction = request.CustomDriftRule.Direction.Trim(),
                IncludeWesternElectric = request.CustomDriftRule.IncludeWesternElectric,
                WarningBehavior = request.CustomDriftRule.WarningBehavior.Trim(),
                Notes = request.CustomDriftRule.Notes?.Trim() ?? string.Empty
            };
        }

        return ServiceResult<SettingsSetupDto>.Ok(GetSettings());
    }

    private SettingsSetupDto SettingsDto()
    {
        var custom = repository.Settings.CustomDriftRule;
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
                custom.Notes));
    }

    private static List<string> ValidateCustomRule(CustomDriftRuleSetupDto rule)
    {
        var errors = new List<string>();
        if (rule.WindowSize < 2 || rule.WindowSize > 25)
        {
            errors.Add("Custom rule window size must be between 2 and 25.");
        }

        if (rule.SigmaThreshold <= 0 || rule.SigmaThreshold > 6)
        {
            errors.Add("Custom rule sigma threshold must be greater than 0 and no more than 6.");
        }

        if (rule.MinimumPointsBeyondThreshold < 1 || rule.MinimumPointsBeyondThreshold > rule.WindowSize)
        {
            errors.Add("Custom rule minimum points must be at least 1 and no more than the window size.");
        }

        if (!new[] { "SameSide", "Above", "Below", "EitherSide" }.Contains(rule.Direction, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add("Custom rule direction is not supported.");
        }

        if (!new[] { "Lock", "Warning", "AuditOnly" }.Contains(rule.WarningBehavior, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add("Custom rule warning behavior is not supported.");
        }

        return errors;
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

    public ServiceResult DeleteUser(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            return ServiceResult.Fail("UserName is required.");
        }

        var user = repository.Users.FirstOrDefault(item => item.UserName.Equals(userName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (user is null)
        {
            return ServiceResult.Fail("User was not found.");
        }

        var hasAdminRole = user.Roles.Any(role =>
            role.Name.Equals(RoleNames.Admin, StringComparison.OrdinalIgnoreCase) ||
            role.Name.Equals(RoleNames.GOD, StringComparison.OrdinalIgnoreCase));
        if (hasAdminRole)
        {
            var remainingAdmins = repository.Users.Count(item =>
                item.Id != user.Id &&
                item.Roles.Any(role =>
                    role.Name.Equals(RoleNames.Admin, StringComparison.OrdinalIgnoreCase) ||
                    role.Name.Equals(RoleNames.GOD, StringComparison.OrdinalIgnoreCase)));
            if (remainingAdmins == 0)
            {
                return ServiceResult.Fail("At least one Admin or GOD user must remain.");
            }
        }

        repository.Users.Remove(user);
        return ServiceResult.Ok();
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

        var originalProcessCode = string.IsNullOrWhiteSpace(request.OriginalProcessCode)
            ? request.ProcessCode.Trim()
            : request.OriginalProcessCode.Trim();
        var originalOperationSeq = request.OriginalOperationSeq.GetValueOrDefault(request.OperationSeq);

        var process = repository.Processes.FirstOrDefault(item => item.ProcessCode.Equals(originalProcessCode, StringComparison.OrdinalIgnoreCase))
            ?? repository.Processes.FirstOrDefault(item => item.ProcessCode.Equals(request.ProcessCode.Trim(), StringComparison.OrdinalIgnoreCase));
        if (process is null)
        {
            process = new ManufacturingProcess { ProcessCode = request.ProcessCode.Trim(), Description = request.ProcessDescription.Trim() };
            repository.Processes.Add(process);
        }
        else
        {
            var oldProcessCode = process.ProcessCode;
            process.Description = request.ProcessDescription.Trim();
            process.ProcessCode = request.ProcessCode.Trim();
            RenameControlLimitProcess(request, oldProcessCode, originalOperationSeq);
        }

        var operation = repository.Operations.FirstOrDefault(item =>
            item.PartId == part.Id &&
            item.ProcessId == process.Id &&
            item.OperationSeq == originalOperationSeq);
        if (operation is null)
        {
            operation = new Operation { PartId = part.Id, ProcessId = process.Id, OperationSeq = request.OperationSeq };
            repository.Operations.Add(operation);
        }
        else
        {
            operation.OperationSeq = request.OperationSeq;
        }

        var originalCharacteristicName = string.IsNullOrWhiteSpace(request.OriginalCharacteristicName)
            ? request.CharacteristicName.Trim()
            : request.OriginalCharacteristicName.Trim();
        var characteristic = repository.Characteristics.FirstOrDefault(item =>
            item.OperationId == operation.Id &&
            item.Name.Equals(originalCharacteristicName, StringComparison.OrdinalIgnoreCase));
        if (characteristic is null)
        {
            characteristic = new Characteristic
            {
                OperationId = operation.Id,
                Name = request.CharacteristicName.Trim(),
                Type = request.CharacteristicType,
                UnitOfMeasure = request.UnitOfMeasure.Trim(),
                IsRequiredForCoa = request.IsRequiredForCoa,
                CoaStatisticType = request.CoaStatisticType
            };
            repository.Characteristics.Add(characteristic);
        }
        else
        {
            var oldCharacteristicName = characteristic.Name;
            characteristic.Name = request.CharacteristicName.Trim();
            characteristic.Type = request.CharacteristicType;
            characteristic.UnitOfMeasure = request.UnitOfMeasure.Trim();
            characteristic.IsRequiredForCoa = request.IsRequiredForCoa;
            characteristic.CoaStatisticType = request.CoaStatisticType;
            RenameControlLimitCharacteristic(request, oldCharacteristicName);
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

        var inspectionPhase = NormalizeInspectionPhase(request.InspectionPhase);
        var plan = repository.InspectionPlans.FirstOrDefault(item =>
            item.CharacteristicId == characteristic.Id &&
            item.InspectionPhase.Equals(inspectionPhase, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            plan = new InspectionPlan
            {
                CharacteristicId = characteristic.Id,
                InspectionPhase = inspectionPhase,
                SampleSize = request.SampleSize,
                AlertRuleSet = request.AlertRuleSet.Trim()
            };
            repository.InspectionPlans.Add(plan);
        }

        plan.InspectionPhase = inspectionPhase;
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
            item.InspectionPhase.Equals(inspectionPhase, StringComparison.OrdinalIgnoreCase) &&
            item.CharacteristicName.Equals(request.CharacteristicName, StringComparison.OrdinalIgnoreCase)));
    }

    public ServiceResult<PartJobDataFieldSetupDto> UpsertPartJobDataField(UpsertPartJobDataFieldRequest request)
    {
        var errors = ValidateJobDataField(request);
        if (errors.Count > 0)
        {
            return ServiceResult<PartJobDataFieldSetupDto>.Fail(errors);
        }

        var part = repository.Parts.FirstOrDefault(item => item.PartNum.Equals(request.PartNum.Trim(), StringComparison.OrdinalIgnoreCase));
        if (part is null)
        {
            part = new Part { PartNum = request.PartNum.Trim(), Description = request.PartNum.Trim() };
            repository.Parts.Add(part);
        }

        var inspectionPhase = NormalizeInspectionPhase(request.InspectionPhase);
        var originalName = string.IsNullOrWhiteSpace(request.OriginalFieldName) ? request.FieldName.Trim() : request.OriginalFieldName.Trim();
        var field = repository.PartJobDataFields.FirstOrDefault(item =>
            item.PartId == part.Id &&
            item.InspectionPhase.Equals(inspectionPhase, StringComparison.OrdinalIgnoreCase) &&
            item.FieldName.Equals(originalName, StringComparison.OrdinalIgnoreCase));
        if (field is null)
        {
            field = new PartJobDataField
            {
                PartId = part.Id,
                InspectionPhase = inspectionPhase,
                FieldName = request.FieldName.Trim(),
                IsRequired = request.IsRequired,
                DisplayOrder = request.DisplayOrder
            };
            repository.PartJobDataFields.Add(field);
        }
        else
        {
            field.InspectionPhase = inspectionPhase;
            field.FieldName = request.FieldName.Trim();
            field.IsRequired = request.IsRequired;
            field.DisplayOrder = request.DisplayOrder;
        }

        return ServiceResult<PartJobDataFieldSetupDto>.Ok(new PartJobDataFieldSetupDto(part.PartNum, field.InspectionPhase, field.FieldName, field.IsRequired, field.DisplayOrder));
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

    private void RenameControlLimitProcess(UpsertInspectionSetupRequest request, string oldProcessCode, int originalOperationSeq)
    {
        foreach (var limit in repository.ControlLimits.Where(item =>
            item.PartNum.Equals(request.PartNum.Trim(), StringComparison.OrdinalIgnoreCase) &&
            item.ProcessCode.Equals(oldProcessCode, StringComparison.OrdinalIgnoreCase) &&
            item.OperationSeq == originalOperationSeq))
        {
            limit.ProcessCode = request.ProcessCode.Trim();
            limit.OperationSeq = request.OperationSeq;
        }
    }

    private void RenameControlLimitCharacteristic(UpsertInspectionSetupRequest request, string oldCharacteristicName)
    {
        foreach (var limit in repository.ControlLimits.Where(item =>
            item.PartNum.Equals(request.PartNum.Trim(), StringComparison.OrdinalIgnoreCase) &&
            item.ProcessCode.Equals(request.ProcessCode.Trim(), StringComparison.OrdinalIgnoreCase) &&
            item.OperationSeq == request.OperationSeq &&
            item.CharacteristicName.Equals(oldCharacteristicName, StringComparison.OrdinalIgnoreCase)))
        {
            limit.CharacteristicName = request.CharacteristicName.Trim();
        }
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
        if (!IsValidInspectionPhase(request.InspectionPhase))
        {
            errors.Add("InspectionPhase must be Startup, Setup, In Process, or Spool.");
        }

        if (request.OperationSeq <= 0) errors.Add("OperationSeq must be greater than zero.");
        if (request.CharacteristicType == CharacteristicType.Variable && request.Lsl >= request.Usl) errors.Add("LSL must be less than USL.");
        if (request.CharacteristicType == CharacteristicType.Variable && request.Lcl.HasValue && request.Ucl.HasValue && request.Lcl.Value >= request.Ucl.Value) errors.Add("LCL must be less than UCL.");
        if (request.SampleSize <= 0) errors.Add("SampleSize must be greater than zero.");
        if (request.FrequencyValue <= 0) errors.Add("FrequencyValue must be greater than zero.");
        if (!IsSupportedRuleSet(request.AlertRuleSet))
        {
            errors.Add("AlertRuleSet is not supported.");
        }

        if (!IsValidFrequencyPair(request.FrequencyType, request.FrequencyUnit))
        {
            errors.Add("FrequencyType and FrequencyUnit are not compatible.");
        }

        return errors;
    }

    private static List<string> ValidateJobDataField(UpsertPartJobDataFieldRequest request)
    {
        var errors = new List<string>();
        Required(request.PartNum, nameof(request.PartNum), errors);
        Required(request.FieldName, nameof(request.FieldName), errors);
        if (!IsValidInspectionPhase(request.InspectionPhase))
        {
            errors.Add("InspectionPhase must be Startup, Setup, In Process, or Spool.");
        }

        if (request.DisplayOrder < 0)
        {
            errors.Add("DisplayOrder must be zero or greater.");
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

    private static void Required(string value, string name, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{name} is required.");
        }
    }
}



