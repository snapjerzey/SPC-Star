using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;

namespace SPCStar.Core.Services;

public sealed record UserSetupDto(string UserName, string Shift, IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions, IReadOnlyList<string> ProductGroups);

public sealed record UpsertUserRequest(string UserName, string Password, IReadOnlyList<string> Roles, IReadOnlyList<string>? ProductGroups = null, string? Shift = null, string? ActingUserName = null, string? ActingSessionToken = null);

public sealed record ResetUserPasswordRequest(string UserName, string TemporaryPassword, string? ActingUserName = null, string? ActingSessionToken = null);

public sealed record UserImportResult(int Imported);

public sealed record UpsertResourceMachineRequest(
    string ResourceId,
    string? Description,
    string? OriginalResourceId = null,
    string DeviceProfile = "keyboard",
    int SerialBaudRate = 9600,
    IReadOnlyList<string>? ProductGroups = null);

public sealed record ResourceImportResult(int Imported);

public sealed record UpdateSettingsRequest(
    string GlobalAlertRuleSet,
    CustomDriftRuleSetupDto? CustomDriftRule = null,
    CapabilityThresholdSetupDto? CapabilityThresholds = null);

public sealed record UpsertInspectionSetupRequest(
    string PartNum,
    string PartDescription,
    string ProductGroup,
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
    string? OriginalProcessCode = null,
    int? OriginalOperationSeq = null,
    string? OriginalCharacteristicName = null,
    string InspectionPhase = "In Process",
    string? Location = null,
    string? InspectionMethod = null,
    int? DisplayOrder = null,
    string? BlankCode = null,
    string? HoleSize = null,
    int? FirstDueValue = null);

public sealed record DeleteInspectionSetupPhaseRequest(
    string PartNum,
    string ProcessCode,
    int OperationSeq,
    string CharacteristicName,
    string InspectionPhase,
    string? OriginalProcessCode = null,
    int? OriginalOperationSeq = null,
    string? OriginalCharacteristicName = null);

public sealed record UpsertPartJobDataFieldRequest(
    string PartNum,
    string InspectionPhase,
    string FieldName,
    bool IsRequired,
    int DisplayOrder,
    string? OriginalFieldName = null);

public sealed record UpsertPartMaterialFieldRequest(
    string PartNum,
    string InspectionPhase,
    string MaterialName,
    string MaterialPartNum,
    string MaterialDescription,
    bool IsRequired,
    int DisplayOrder,
    string? OriginalMaterialName = null);

public sealed class SetupManagementService(ISpcRepository repository)
{
    private static readonly string[] StandardProductGroups =
    [
        "Ethicon Cutting Edge - Drilled",
        "Ethicon Cutting Edge - Needles",
        "Ethicon Ethalloy Cardio - Needles",
        "Ethicon Everpoint - Needles",
        "Ethicon Taperpoint - Drilled",
        "Ethicon Taperpoint - Needles",
        "Schneider"
    ];

    public IReadOnlyList<UserSetupDto> GetUsers()
    {
        return repository.Users
            .OrderBy(user => user.UserName)
            .Select(user => new UserSetupDto(
                user.UserName,
                user.Shift,
                user.Roles.Select(role => role.Name).OrderBy(role => role).ToArray(),
                user.Roles.SelectMany(role => role.Permissions).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(permission => permission).ToArray(),
                user.ProductGroups.OrderBy(group => group).ToArray()))
            .ToArray();
    }

    public IReadOnlyList<string> GetRoles()
    {
        return repository.Roles
            .Select(role => role.Name)
            .OrderBy(role => role)
            .ToArray();
    }

    public IReadOnlyList<ResourceSetupDto> GetResources()
    {
        return repository.Resources
            .GroupBy(resource => resource.ResourceId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(resource => !string.IsNullOrWhiteSpace(resource.Description)).First())
            .OrderBy(resource => resource.ResourceId)
            .Select(ResourceDto)
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

        if (request.CapabilityThresholds is not null)
        {
            var thresholdErrors = ValidateCapabilityThresholds(request.CapabilityThresholds);
            if (thresholdErrors.Count > 0)
            {
                return ServiceResult<SettingsSetupDto>.Fail(thresholdErrors);
            }

            repository.Settings.CapabilityThresholds = new CapabilityThresholdSettings
            {
                YellowMinimum = request.CapabilityThresholds.YellowMinimum,
                GreenMinimum = request.CapabilityThresholds.GreenMinimum
            };
        }

        return ServiceResult<SettingsSetupDto>.Ok(GetSettings());
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

    private static List<string> ValidateCapabilityThresholds(CapabilityThresholdSetupDto thresholds)
    {
        var errors = new List<string>();
        if (thresholds.YellowMinimum <= 0 || thresholds.YellowMinimum > 10)
        {
            errors.Add("Capability yellow minimum must be greater than 0 and no more than 10.");
        }

        if (thresholds.GreenMinimum <= 0 || thresholds.GreenMinimum > 10)
        {
            errors.Add("Capability green minimum must be greater than 0 and no more than 10.");
        }

        if (thresholds.GreenMinimum <= thresholds.YellowMinimum)
        {
            errors.Add("Capability green minimum must be greater than the yellow minimum.");
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
        user.Shift = CleanOptional(request.Shift);
        user.ProductGroups.Clear();
        foreach (var group in CleanProductGroups(request.ProductGroups))
        {
            user.ProductGroups.Add(group);
        }
        return ServiceResult<UserSetupDto>.Ok(GetUsers().First(item => item.UserName.Equals(user.UserName, StringComparison.OrdinalIgnoreCase)));
    }

    public ServiceResult<UserSetupDto> ResetUserPassword(ResetUserPasswordRequest request)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(request.UserName))
        {
            errors.Add("UserName is required.");
        }

        if (string.IsNullOrWhiteSpace(request.TemporaryPassword))
        {
            errors.Add("Temporary password is required.");
        }
        else if (request.TemporaryPassword.Length < 4)
        {
            errors.Add("Temporary password must be at least 4 characters.");
        }

        if (errors.Count > 0)
        {
            return ServiceResult<UserSetupDto>.Fail(errors);
        }

        var user = repository.Users.FirstOrDefault(item => item.UserName.Equals(request.UserName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (user is null)
        {
            return ServiceResult<UserSetupDto>.Fail("User was not found.");
        }

        if (UserHasGodRole(user) && !IsGodUser(request.ActingUserName))
        {
            return ServiceResult<UserSetupDto>.Fail("Only a GOD user can reset the password for another GOD user.");
        }

        var (hash, salt) = PasswordHasher.HashPassword(request.TemporaryPassword);
        user.PasswordHash = hash;
        user.PasswordSalt = salt;
        return ServiceResult<UserSetupDto>.Ok(GetUsers().First(item => item.UserName.Equals(user.UserName, StringComparison.OrdinalIgnoreCase)));
    }

    public ServiceResult<UserImportResult> ImportUsersCsv(string csv, string? actingUserName = null)
    {
        var rows = CsvSupport.ReadRows(csv)
            .Where(row => row.Values.Any(value => !string.IsNullOrWhiteSpace(value)))
            .ToArray();
        var requests = new List<UpsertUserRequest>();
        var errors = new List<string>();
        var rowNumber = 1;
        var seenUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            rowNumber++;
            var request = UserRequestFromImportRow(row);
            if (request is null)
            {
                errors.Add($"Row {rowNumber}: UserName is required.");
                continue;
            }

            if (!seenUsers.Add(request.UserName.Trim()))
            {
                errors.Add($"Row {rowNumber}: Duplicate user {request.UserName}.");
                continue;
            }

            request = request with { ActingUserName = actingUserName };
            errors.AddRange(ValidateUser(request).Select(error => $"Row {rowNumber}: {error}"));
            if (request.ProductGroups is null || request.ProductGroups.Count == 0)
            {
                errors.Add($"Row {rowNumber}: At least one product group access column must be marked X.");
            }

            var unknownGroups = CleanProductGroups(request.ProductGroups)
                .Where(group => !LoadedProductGroups().Contains(group, StringComparer.OrdinalIgnoreCase))
                .ToArray();
            foreach (var group in unknownGroups)
            {
                errors.Add($"Row {rowNumber}: Unknown product group {group}.");
            }

            requests.Add(request);
        }

        if (errors.Count > 0)
        {
            return ServiceResult<UserImportResult>.Fail(errors);
        }

        foreach (var request in requests)
        {
            UpsertUser(request with { ActingUserName = actingUserName });
        }

        return ServiceResult<UserImportResult>.Ok(new UserImportResult(requests.Count));
    }

    public ServiceResult<ResourceImportResult> ImportResourcesCsv(string csv)
    {
        var rows = CsvSupport.ReadRows(csv)
            .Where(row => row.Values.Any(value => !string.IsNullOrWhiteSpace(value)))
            .ToArray();
        var requests = new List<UpsertResourceMachineRequest>();
        var errors = new List<string>();
        var rowNumber = 1;
        var seenResources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            rowNumber++;
            var resourceId = Value(row, "MachineID", "Machine ID", "ResourceID", "Resource ID", "Machine", "Resource");
            var description = Value(row, "Description", "Machine Description", "Resource Description");
            var deviceProfile = Value(row, "DeviceProfile", "Device Profile", "Gauge Profile", "Measurement Device");
            var baud = Value(row, "SerialBaudRate", "Serial Baud Rate", "Baud Rate", "Baud");
            var productGroups = ProductGroupsFromMachineRow(row);
            if (string.IsNullOrWhiteSpace(resourceId))
            {
                errors.Add($"Row {rowNumber}: Machine ID is required.");
                continue;
            }

            if (!seenResources.Add(resourceId.Trim()))
            {
                errors.Add($"Row {rowNumber}: Duplicate machine {resourceId}.");
                continue;
            }

            var request = new UpsertResourceMachineRequest(
                resourceId.Trim(),
                description.Trim(),
                resourceId.Trim(),
                string.IsNullOrWhiteSpace(deviceProfile) ? "keyboard" : deviceProfile.Trim(),
                int.TryParse(baud, out var parsedBaud) ? parsedBaud : 9600,
                productGroups);
            errors.AddRange(ValidateResource(request).Select(error => $"Row {rowNumber}: {error}"));
            foreach (var group in CleanProductGroups(productGroups).Where(group => !LoadedProductGroups().Contains(group, StringComparer.OrdinalIgnoreCase)))
            {
                errors.Add($"Row {rowNumber}: Unknown product group {group}.");
            }
            requests.Add(request);
        }

        if (errors.Count > 0)
        {
            return ServiceResult<ResourceImportResult>.Fail(errors);
        }

        foreach (var request in requests)
        {
            var result = UpsertResource(request);
            if (!result.Succeeded)
            {
                return ServiceResult<ResourceImportResult>.Fail(result.Errors);
            }
        }

        return ServiceResult<ResourceImportResult>.Ok(new ResourceImportResult(requests.Count));
    }

    public string ExportResourcesCsv()
    {
        var (headers, rows) = ResourceExportRows();

        return CsvSupport.WriteRows(headers, rows);
    }

    public byte[] ExportResourcesXlsx()
    {
        var (headers, rows) = ResourceExportRows();
        return XlsxSupport.WriteWorkbook([
            new XlsxWorksheet("SPC-Star Machine Import", headers, rows)
        ]);
    }

    public string ExportUsersCsv()
    {
        var productGroups = LoadedProductGroups().OrderBy(group => group).ToArray();
        var headers = new[] { "UserName", "TemporaryPassword", "Role", "Shift" }.Concat(productGroups).ToArray();
        var rows = repository.Users
            .OrderBy(user => user.UserName)
            .Select(user =>
            {
                var row = headers.ToDictionary(header => header, _ => "", StringComparer.OrdinalIgnoreCase);
                row["UserName"] = user.UserName;
                row["TemporaryPassword"] = "test";
                row["Role"] = user.Roles.OrderBy(role => RoleSort(role.Name)).ThenBy(role => role.Name).FirstOrDefault()?.Name ?? "";
                row["Shift"] = user.Shift;
                foreach (var group in productGroups)
                {
                    row[group] = user.ProductGroups.Contains(group, StringComparer.OrdinalIgnoreCase) ? "X" : "";
                }

                return row;
            });

        return CsvSupport.WriteRows(headers, rows);
    }

    private (IReadOnlyList<string> Headers, IReadOnlyList<Dictionary<string, string>> Rows) ResourceExportRows()
    {
        var productGroups = LoadedProductGroups().OrderBy(group => group).ToArray();
        var headers = new[] { "Machine ID", "Description", "Device Profile", "Baud Rate" }.Concat(productGroups).ToArray();
        var rows = repository.Resources
            .GroupBy(resource => resource.ResourceId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(resource => !string.IsNullOrWhiteSpace(resource.Description)).First())
            .OrderBy(resource => resource.ResourceId)
            .Select(resource =>
            {
                var row = headers.ToDictionary(header => header, _ => "", StringComparer.OrdinalIgnoreCase);
                row["Machine ID"] = resource.ResourceId;
                row["Description"] = resource.Description ?? "";
                row["Device Profile"] = DeviceProfileLabel(resource.DeviceProfile);
                row["Baud Rate"] = resource.SerialBaudRate.ToString();
                foreach (var group in productGroups)
                {
                    row[group] = resource.ProductGroups.Contains(group, StringComparer.OrdinalIgnoreCase) ? "X" : "";
                }

                return row;
            })
            .ToArray();

        return (headers, rows);
    }

    public ServiceResult DeleteUser(string userName, string? actingUserName = null)
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

        var hasGodRole = user.Roles.Any(role => role.Name.Equals(RoleNames.GOD, StringComparison.OrdinalIgnoreCase));
        if (hasGodRole)
        {
            if (!IsGodUser(actingUserName))
            {
                return ServiceResult.Fail("Only a GOD user can delete another GOD user.");
            }

            var remainingGodUsers = repository.Users.Count(item =>
                item.Id != user.Id &&
                item.Roles.Any(role => role.Name.Equals(RoleNames.GOD, StringComparison.OrdinalIgnoreCase)));
            if (remainingGodUsers == 0)
            {
                return ServiceResult.Fail("At least one GOD user must remain.");
            }
        }

        repository.Users.Remove(user);
        return ServiceResult.Ok();
    }

    public ServiceResult<ResourceSetupDto> UpsertResource(UpsertResourceMachineRequest request)
    {
        var errors = ValidateResource(request);
        if (errors.Count > 0)
        {
            return ServiceResult<ResourceSetupDto>.Fail(errors);
        }

        var resourceId = request.ResourceId.Trim();
        var originalResourceId = string.IsNullOrWhiteSpace(request.OriginalResourceId)
            ? resourceId
            : request.OriginalResourceId.Trim();
        var resource = repository.Resources.FirstOrDefault(item =>
            item.ResourceId.Equals(originalResourceId, StringComparison.OrdinalIgnoreCase));

        if (resource is null)
        {
            if (repository.Resources.Any(item => item.ResourceId.Equals(resourceId, StringComparison.OrdinalIgnoreCase)))
            {
                return ServiceResult<ResourceSetupDto>.Fail("Machine already exists.");
            }

            resource = new ResourceMachine
            {
                ResourceId = resourceId,
                Description = CleanOptional(request.Description),
                DeviceProfile = NormalizeDeviceProfile(request.DeviceProfile),
                SerialBaudRate = NormalizeBaudRate(request.SerialBaudRate)
            };
            repository.Resources.Add(resource);
        }
        else
        {
            if (!resource.ResourceId.Equals(resourceId, StringComparison.OrdinalIgnoreCase) && ResourceHasHistory(resource.ResourceId))
            {
                return ServiceResult<ResourceSetupDto>.Fail("Machine ID cannot be changed because inspection history already exists for this machine.");
            }

            if (!resource.ResourceId.Equals(resourceId, StringComparison.OrdinalIgnoreCase) &&
                repository.Resources.Any(item => item.Id != resource.Id && item.ResourceId.Equals(resourceId, StringComparison.OrdinalIgnoreCase)))
            {
                return ServiceResult<ResourceSetupDto>.Fail("Machine already exists.");
            }

            resource.ResourceId = resourceId;
            resource.Description = CleanOptional(request.Description);
            resource.DeviceProfile = NormalizeDeviceProfile(request.DeviceProfile);
            resource.SerialBaudRate = NormalizeBaudRate(request.SerialBaudRate);
        }

        resource.ProductGroups.Clear();
        foreach (var group in CleanProductGroups(request.ProductGroups))
        {
            resource.ProductGroups.Add(group);
        }

        return ServiceResult<ResourceSetupDto>.Ok(ResourceDto(resource));
    }

    public ServiceResult DeleteResource(string resourceId)
    {
        if (string.IsNullOrWhiteSpace(resourceId))
        {
            return ServiceResult.Fail("Machine ID is required.");
        }

        var resource = repository.Resources.FirstOrDefault(item => item.ResourceId.Equals(resourceId.Trim(), StringComparison.OrdinalIgnoreCase));
        if (resource is null)
        {
            return ServiceResult.Fail("Machine was not found.");
        }

        if (ResourceHasHistory(resource.ResourceId))
        {
            return ServiceResult.Fail("Machine cannot be deleted because inspection history already exists for this machine.");
        }

        repository.Resources.Remove(resource);
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
            part = new Part
            {
                PartNum = request.PartNum.Trim(),
                Description = request.PartDescription.Trim(),
                ProductGroup = CleanProductGroup(request.ProductGroup),
                BlankCode = CleanOptional(request.BlankCode),
                HoleSize = CleanOptional(request.HoleSize)
            };
            repository.Parts.Add(part);
        }
        else
        {
            part.Description = request.PartDescription.Trim();
            part.ProductGroup = CleanProductGroup(request.ProductGroup);
            part.BlankCode = CleanOptional(request.BlankCode);
            part.HoleSize = CleanOptional(request.HoleSize);
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
                Location = CleanOptional(request.Location),
                InspectionMethod = CleanOptional(request.InspectionMethod)
            };
            repository.Characteristics.Add(characteristic);
        }
        else
        {
            var oldCharacteristicName = characteristic.Name;
            characteristic.Name = request.CharacteristicName.Trim();
            characteristic.Type = request.CharacteristicType;
            characteristic.UnitOfMeasure = request.UnitOfMeasure.Trim();
            characteristic.Location = CleanOptional(request.Location);
            characteristic.InspectionMethod = CleanOptional(request.InspectionMethod);
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
            ShiftInspectionDisplayOrdersForInsert(operation.Id, inspectionPhase, request.DisplayOrder.GetValueOrDefault(), characteristic.Id);
            plan = new InspectionPlan
            {
                CharacteristicId = characteristic.Id,
                InspectionPhase = inspectionPhase,
                SampleSize = request.SampleSize,
                DisplayOrder = request.DisplayOrder.GetValueOrDefault(NextInspectionDisplayOrder(operation.Id, inspectionPhase)),
                AlertRuleSet = request.AlertRuleSet.Trim()
            };
            repository.InspectionPlans.Add(plan);
        }

        plan.InspectionPhase = inspectionPhase;
        plan.SampleSize = request.SampleSize;
        plan.DisplayOrder = request.DisplayOrder.GetValueOrDefault(plan.DisplayOrder);
        plan.AlertRuleSet = request.AlertRuleSet.Trim();
        plan.Frequency = new InspectionFrequency
        {
            Type = request.FrequencyType,
            Value = request.FrequencyValue,
            Unit = request.FrequencyUnit,
            FirstDueValue = request.FirstDueValue is > 0 ? request.FirstDueValue : null
        };
        if (request.CharacteristicType == CharacteristicType.Variable)
        {
            UpsertControlLimit(request);
        }
        else
        {
            RemoveControlLimit(request);
        }

        NormalizeInspectionDisplayOrders(operation.Id, inspectionPhase, request.DisplayOrder.HasValue ? characteristic.Id : null);
        return ServiceResult<InspectionPlanSetupDto>.Ok(new SetupQueryService(repository).GetInspectionPlans(request.PartNum).First(item =>
            item.ProcessCode.Equals(request.ProcessCode, StringComparison.OrdinalIgnoreCase) &&
            item.OperationSeq == request.OperationSeq &&
            item.InspectionPhase.Equals(inspectionPhase, StringComparison.OrdinalIgnoreCase) &&
            item.CharacteristicName.Equals(request.CharacteristicName, StringComparison.OrdinalIgnoreCase)));
    }

    public ServiceResult DeleteInspectionSetupPhase(DeleteInspectionSetupPhaseRequest request)
    {
        var errors = new List<string>();
        Required(request.PartNum, nameof(request.PartNum), errors);
        Required(request.ProcessCode, nameof(request.ProcessCode), errors);
        Required(request.CharacteristicName, nameof(request.CharacteristicName), errors);
        if (!IsValidInspectionPhase(request.InspectionPhase))
        {
            errors.Add("InspectionPhase must be Startup, Setup, In Process, Coil Change, Spool, or End of Spool.");
        }
        if (request.OperationSeq <= 0)
        {
            errors.Add("OperationSeq must be greater than zero.");
        }
        if (errors.Count > 0)
        {
            return ServiceResult.Fail(errors);
        }

        var processCode = string.IsNullOrWhiteSpace(request.OriginalProcessCode)
            ? request.ProcessCode.Trim()
            : request.OriginalProcessCode.Trim();
        var operationSeq = request.OriginalOperationSeq.GetValueOrDefault(request.OperationSeq);
        var characteristicName = string.IsNullOrWhiteSpace(request.OriginalCharacteristicName)
            ? request.CharacteristicName.Trim()
            : request.OriginalCharacteristicName.Trim();
        var phase = NormalizeInspectionPhase(request.InspectionPhase);

        var query =
            from part in repository.Parts
            join operation in repository.Operations on part.Id equals operation.PartId
            join process in repository.Processes on operation.ProcessId equals process.Id
            join characteristic in repository.Characteristics on operation.Id equals characteristic.OperationId
            join plan in repository.InspectionPlans on characteristic.Id equals plan.CharacteristicId
            where part.PartNum.Equals(request.PartNum.Trim(), StringComparison.OrdinalIgnoreCase) &&
                process.ProcessCode.Equals(processCode, StringComparison.OrdinalIgnoreCase) &&
                operation.OperationSeq == operationSeq &&
                characteristic.Name.Equals(characteristicName, StringComparison.OrdinalIgnoreCase) &&
                plan.InspectionPhase.Equals(phase, StringComparison.OrdinalIgnoreCase)
            select plan;

        var match = query.FirstOrDefault();
        if (match is not null)
        {
            repository.InspectionPlans.Remove(match);
        }

        return ServiceResult.Ok();
    }

    private int NextInspectionDisplayOrder(Guid operationId, string inspectionPhase)
    {
        var currentMax = (from characteristic in repository.Characteristics
                          join plan in repository.InspectionPlans on characteristic.Id equals plan.CharacteristicId
                          where characteristic.OperationId == operationId &&
                              plan.InspectionPhase.Equals(inspectionPhase, StringComparison.OrdinalIgnoreCase)
                          select (int?)plan.DisplayOrder)
            .Max();
        return currentMax.GetValueOrDefault() + 1;
    }

    private void ShiftInspectionDisplayOrdersForInsert(Guid operationId, string inspectionPhase, int requestedDisplayOrder, Guid insertedCharacteristicId)
    {
        if (requestedDisplayOrder <= 0)
        {
            return;
        }

        var affectedPlans = from characteristic in repository.Characteristics
                            join plan in repository.InspectionPlans on characteristic.Id equals plan.CharacteristicId
                            where characteristic.OperationId == operationId &&
                                characteristic.Id != insertedCharacteristicId &&
                                plan.InspectionPhase.Equals(inspectionPhase, StringComparison.OrdinalIgnoreCase) &&
                                plan.DisplayOrder >= requestedDisplayOrder
                            select plan;
        foreach (var affectedPlan in affectedPlans)
        {
            affectedPlan.DisplayOrder++;
        }
    }

    private void NormalizeInspectionDisplayOrders(Guid operationId, string inspectionPhase, Guid? preferredCharacteristicId = null)
    {
        var plans = (from characteristic in repository.Characteristics
                     join plan in repository.InspectionPlans on characteristic.Id equals plan.CharacteristicId
                     where characteristic.OperationId == operationId &&
                         plan.InspectionPhase.Equals(inspectionPhase, StringComparison.OrdinalIgnoreCase)
                     orderby plan.DisplayOrder,
                         preferredCharacteristicId.HasValue && characteristic.Id == preferredCharacteristicId.Value ? 0 : 1,
                         characteristic.Name
                     select plan)
            .ToArray();

        for (var index = 0; index < plans.Length; index++)
        {
            plans[index].DisplayOrder = index + 1;
        }
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
            return ServiceResult<PartJobDataFieldSetupDto>.Fail($"Part {request.PartNum.Trim()} must be created with a product group before job data fields can be added.");
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
                IsRequired = !IsEndCountField(request.FieldName) && request.IsRequired,
                DisplayOrder = request.DisplayOrder
            };
            repository.PartJobDataFields.Add(field);
        }
        else
        {
            field.InspectionPhase = inspectionPhase;
            field.FieldName = request.FieldName.Trim();
            field.IsRequired = !IsEndCountField(request.FieldName) && request.IsRequired;
            field.DisplayOrder = request.DisplayOrder;
        }

        return ServiceResult<PartJobDataFieldSetupDto>.Ok(new PartJobDataFieldSetupDto(part.PartNum, field.InspectionPhase, field.FieldName, field.IsRequired, field.DisplayOrder));
    }

    private static bool IsEndCountField(string fieldName)
    {
        var normalized = new string((fieldName ?? string.Empty).Where(char.IsLetterOrDigit).ToArray());
        return normalized.Equals("EndCount", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("EndingCount", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("FinalCount", StringComparison.OrdinalIgnoreCase);
    }

    public ServiceResult<PartMaterialFieldSetupDto> UpsertPartMaterialField(UpsertPartMaterialFieldRequest request)
    {
        var errors = ValidateMaterialField(request);
        if (errors.Count > 0)
        {
            return ServiceResult<PartMaterialFieldSetupDto>.Fail(errors);
        }

        var part = repository.Parts.FirstOrDefault(item => item.PartNum.Equals(request.PartNum.Trim(), StringComparison.OrdinalIgnoreCase));
        if (part is null)
        {
            return ServiceResult<PartMaterialFieldSetupDto>.Fail($"Part {request.PartNum.Trim()} must be created with a product group before material fields can be added.");
        }

        var inspectionPhase = NormalizeInspectionPhase(request.InspectionPhase);
        var originalName = string.IsNullOrWhiteSpace(request.OriginalMaterialName) ? request.MaterialName.Trim() : request.OriginalMaterialName.Trim();
        var field = repository.PartMaterialFields.FirstOrDefault(item =>
            item.PartId == part.Id &&
            item.MaterialName.Equals(originalName, StringComparison.OrdinalIgnoreCase));
        if (field is null)
        {
            field = new PartMaterialField
            {
                PartId = part.Id,
                InspectionPhase = inspectionPhase,
                MaterialName = request.MaterialName.Trim(),
                MaterialPartNum = request.MaterialPartNum.Trim(),
                MaterialDescription = request.MaterialDescription.Trim(),
                IsRequired = request.IsRequired,
                DisplayOrder = request.DisplayOrder
            };
            repository.PartMaterialFields.Add(field);
        }
        else
        {
            field.MaterialName = request.MaterialName.Trim();
            field.MaterialPartNum = request.MaterialPartNum.Trim();
            field.MaterialDescription = request.MaterialDescription.Trim();
            field.IsRequired = request.IsRequired;
            field.DisplayOrder = request.DisplayOrder;
        }

        return ServiceResult<PartMaterialFieldSetupDto>.Ok(new PartMaterialFieldSetupDto(part.PartNum, field.InspectionPhase, field.MaterialName, field.MaterialPartNum, field.MaterialDescription, field.IsRequired, field.DisplayOrder));
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

        var existingUser = string.IsNullOrWhiteSpace(request.UserName)
            ? null
            : repository.Users.FirstOrDefault(user => user.UserName.Equals(request.UserName.Trim(), StringComparison.OrdinalIgnoreCase));
        var touchesGodAccess = request.Roles.Any(role => role.Trim().Equals(RoleNames.GOD, StringComparison.OrdinalIgnoreCase)) ||
            (existingUser is not null && UserHasGodRole(existingUser));
        if (touchesGodAccess && !IsGodUser(request.ActingUserName))
        {
            errors.Add("Only a GOD user can create, edit, or assign GOD access.");
        }

        return errors;
    }

    private bool IsGodUser(string? userName)
    {
        return !string.IsNullOrWhiteSpace(userName) &&
            repository.Users.Any(user =>
                user.UserName.Equals(userName.Trim(), StringComparison.OrdinalIgnoreCase) &&
                UserHasGodRole(user));
    }

    private static bool UserHasGodRole(User user)
    {
        return user.Roles.Any(role => role.Name.Equals(RoleNames.GOD, StringComparison.OrdinalIgnoreCase));
    }

    private static List<string> ValidateResource(UpsertResourceMachineRequest request)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(request.ResourceId))
        {
            errors.Add("Machine ID is required.");
        }
        else if (request.ResourceId.Trim().Length > 40)
        {
            errors.Add("Machine ID must be 40 characters or fewer.");
        }

        if (!string.IsNullOrWhiteSpace(request.Description) && request.Description.Trim().Length > 120)
        {
            errors.Add("Machine description must be 120 characters or fewer.");
        }

        if (!new[] { "keyboard", "serial-text" }.Contains(NormalizeDeviceProfile(request.DeviceProfile), StringComparer.OrdinalIgnoreCase))
        {
            errors.Add("Device profile must be Keyboard input or Serial text gauge.");
        }

        if (!AllowedBaudRates.Contains(NormalizeBaudRate(request.SerialBaudRate)))
        {
            errors.Add("Serial baud rate is not supported.");
        }

        return errors;
    }

    private static readonly int[] AllowedBaudRates = [4800, 9600, 19200, 38400, 57600, 115200];

    private static string NormalizeDeviceProfile(string? profile)
    {
        if (string.IsNullOrWhiteSpace(profile))
        {
            return "keyboard";
        }

        var trimmed = profile.Trim();
        if (trimmed.Equals("Serial text gauge", StringComparison.OrdinalIgnoreCase))
        {
            return "serial-text";
        }

        if (trimmed.Equals("Keyboard input", StringComparison.OrdinalIgnoreCase))
        {
            return "keyboard";
        }

        return trimmed;
    }

    private static int NormalizeBaudRate(int baudRate)
    {
        return baudRate <= 0 ? 9600 : baudRate;
    }

    private bool ResourceHasHistory(string resourceId)
    {
        return repository.Measurements.Any(item => item.ResourceId.Equals(resourceId, StringComparison.OrdinalIgnoreCase)) ||
            repository.JobNotes.Any(item => item.ResourceId.Equals(resourceId, StringComparison.OrdinalIgnoreCase)) ||
            repository.JobTags.Any(item => item.ResourceId.Equals(resourceId, StringComparison.OrdinalIgnoreCase)) ||
            repository.Alerts.Any(item => item.ResourceId.Equals(resourceId, StringComparison.OrdinalIgnoreCase)) ||
            repository.AlertOverrides.Any(item => item.ResourceId.Equals(resourceId, StringComparison.OrdinalIgnoreCase)) ||
            repository.MaterialChanges.Any(item => item.ResourceId.Equals(resourceId, StringComparison.OrdinalIgnoreCase));
    }

    private ResourceSetupDto ResourceDto(ResourceMachine resource)
    {
        return new ResourceSetupDto(
            resource.ResourceId,
            resource.Description,
            resource.DeviceProfile,
            resource.SerialBaudRate,
            resource.ProductGroups.OrderBy(group => group).ToArray());
    }

    private IReadOnlyList<string> ProductGroupsFromMachineRow(Dictionary<string, string> row)
    {
        var groups = new List<string>();
        groups.AddRange(SplitValues(Value(row, "ProductGroups", "Product Groups")));
        foreach (var group in LoadedProductGroups())
        {
            if (IsMarked(row.GetValueOrDefault(group)))
            {
                groups.Add(group);
            }
        }

        return groups.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private UpsertUserRequest? UserRequestFromImportRow(Dictionary<string, string> row)
    {
        var userName = Value(row, "UserName", "User Name", "Username");
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var password = Value(row, "TemporaryPassword", "Temporary Password", "Password");
        var shift = Value(row, "Shift", "Work Shift", "Assigned Shift");
        var roles = SplitValues(Value(row, "Role", "Roles", "Access Level"))
            .Select(NormalizeRoleName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var groups = new List<string>();
        groups.AddRange(SplitValues(Value(row, "ProductGroups", "Product Groups")));

        foreach (var group in LoadedProductGroups())
        {
            if (IsMarked(row.GetValueOrDefault(group)))
            {
                groups.Add(group);
            }
        }

        return new UpsertUserRequest(
            userName.Trim(),
            password.Trim(),
            roles,
            groups.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            shift);
    }

    private IReadOnlyList<string> LoadedProductGroups()
    {
        return repository.Parts
            .Select(part => CleanProductGroup(part.ProductGroup))
            .Concat(repository.Users.SelectMany(user => user.ProductGroups.Select(CleanProductGroup)))
            .Concat(repository.Resources.SelectMany(resource => resource.ProductGroups.Select(CleanProductGroup)))
            .Concat(StandardProductGroups)
            .Where(group => !string.IsNullOrWhiteSpace(group))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group)
            .ToArray();
    }

    private static IReadOnlyList<string> SplitValues(string value)
    {
        return value
            .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
    }

    private static string NormalizeRoleName(string roleName)
    {
        return roleName.Equals("Admin", StringComparison.OrdinalIgnoreCase) ? RoleNames.QA : roleName;
    }

    private static string Value(Dictionary<string, string> row, params string[] fields)
    {
        foreach (var field in fields)
        {
            if (row.TryGetValue(field, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static bool IsMarked(string? value)
    {
        return value?.Trim().Equals("x", StringComparison.OrdinalIgnoreCase) == true ||
            value?.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase) == true ||
            value?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) == true ||
            value?.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) == true ||
            value?.Trim().Equals("1", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static List<string> ValidateInspectionSetup(UpsertInspectionSetupRequest request)
    {
        var errors = new List<string>();
        Required(request.PartNum, nameof(request.PartNum), errors);
        Required(request.PartDescription, nameof(request.PartDescription), errors);
        Required(request.ProductGroup, nameof(request.ProductGroup), errors);
        Required(request.ProcessCode, nameof(request.ProcessCode), errors);
        Required(request.ProcessDescription, nameof(request.ProcessDescription), errors);
        Required(request.CharacteristicName, nameof(request.CharacteristicName), errors);
        Required(request.UnitOfMeasure, nameof(request.UnitOfMeasure), errors);
        Required(request.AlertRuleSet, nameof(request.AlertRuleSet), errors);
        if (!IsValidInspectionPhase(request.InspectionPhase))
        {
            errors.Add("InspectionPhase must be Startup, Setup, In Process, Coil Change, Spool, or End of Spool.");
        }

        if (request.OperationSeq <= 0) errors.Add("OperationSeq must be greater than zero.");
        if (request.CharacteristicType == CharacteristicType.Variable && request.Lsl >= request.Usl) errors.Add("LSL must be less than USL.");
        if (request.CharacteristicType == CharacteristicType.Variable && request.Lcl.HasValue && request.Ucl.HasValue && request.Lcl.Value >= request.Ucl.Value) errors.Add("LCL must be less than UCL.");
        if (request.SampleSize <= 0) errors.Add("SampleSize must be greater than zero.");
        if (request.FrequencyValue <= 0) errors.Add("FrequencyValue must be greater than zero.");
        if (request.FirstDueValue is <= 0) errors.Add("FirstDueValue must be greater than zero when provided.");
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
            errors.Add("InspectionPhase must be Startup, Setup, In Process, Coil Change, Spool, or End of Spool.");
        }

        if (request.DisplayOrder < 0)
        {
            errors.Add("DisplayOrder must be zero or greater.");
        }

        return errors;
    }

    private static List<string> ValidateMaterialField(UpsertPartMaterialFieldRequest request)
    {
        var errors = new List<string>();
        Required(request.PartNum, nameof(request.PartNum), errors);
        Required(request.MaterialName, nameof(request.MaterialName), errors);
        Required(request.MaterialPartNum, nameof(request.MaterialPartNum), errors);
        Required(request.MaterialDescription, nameof(request.MaterialDescription), errors);
        if (!IsValidInspectionPhase(request.InspectionPhase))
        {
            errors.Add("InspectionPhase must be Startup, Setup, In Process, Coil Change, Spool, or End of Spool.");
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
            FrequencyType.Quantity => unit is FrequencyUnit.Pieces or FrequencyUnit.Box,
            FrequencyType.Event => unit is FrequencyUnit.StartOfJob or FrequencyUnit.MaterialChange or FrequencyUnit.ToolChange or FrequencyUnit.Restart or FrequencyUnit.Shift or FrequencyUnit.Spool,
            _ => false
        };
    }

    private static bool IsValidInspectionPhase(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ||
            value.Trim().Equals("Startup", StringComparison.OrdinalIgnoreCase) ||
            value.Trim().Equals("Set Up", StringComparison.OrdinalIgnoreCase) ||
            value.Trim().Equals("Setup", StringComparison.OrdinalIgnoreCase) ||
            value.Trim().Equals("Coil Change", StringComparison.OrdinalIgnoreCase) ||
            value.Trim().Equals("CoilChange", StringComparison.OrdinalIgnoreCase) ||
            value.Trim().Equals("Spool", StringComparison.OrdinalIgnoreCase) ||
            value.Trim().Equals("Spool Start", StringComparison.OrdinalIgnoreCase) ||
            value.Trim().Equals("End of Spool", StringComparison.OrdinalIgnoreCase) ||
            value.Trim().Equals("EndOfSpool", StringComparison.OrdinalIgnoreCase) ||
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
            phase.Equals("Spool Start", StringComparison.OrdinalIgnoreCase))
        {
            return "Spool";
        }
        if (phase.Equals("End of Spool", StringComparison.OrdinalIgnoreCase) ||
            phase.Equals("EndOfSpool", StringComparison.OrdinalIgnoreCase) ||
            phase.Equals("Spool End", StringComparison.OrdinalIgnoreCase))
        {
            return "End of Spool";
        }
        if (phase.Equals("Coil Change", StringComparison.OrdinalIgnoreCase) ||
            phase.Equals("CoilChange", StringComparison.OrdinalIgnoreCase))
        {
            return "Coil Change";
        }

        return phase.Equals("Set Up", StringComparison.OrdinalIgnoreCase) ||
            phase.Equals("Setup", StringComparison.OrdinalIgnoreCase)
            ? "Setup"
            : phase.Equals("In Process", StringComparison.OrdinalIgnoreCase) ||
                phase.Equals("InProcess", StringComparison.OrdinalIgnoreCase)
                ? "In Process"
                : phase;
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

    private static string CleanProductGroup(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "General";
        }

        var trimmed = value.Trim();
        return trimmed switch
        {
            "Ethicon Cutting Edge - Driller" => "Ethicon Cutting Edge - Drilled",
            "Ethicon Taperpoint - Driller" => "Ethicon Taperpoint - Drilled",
            "Ethicon Ethalloy Cardio" => "Ethicon Ethalloy Cardio - Needles",
            "Ethicon Everpoint" => "Ethicon Everpoint - Needles",
            _ => trimmed
        };
    }

    private static string CleanOptional(string? value) => string.IsNullOrWhiteSpace(value) ? "" : value.Trim();

    private static string DeviceProfileLabel(string? value)
    {
        return string.Equals(value, "serial-text", StringComparison.OrdinalIgnoreCase)
            ? "Serial text gauge"
            : "Keyboard input";
    }

    private static int RoleSort(string roleName)
    {
        return roleName switch
        {
            RoleNames.GOD => 0,
            RoleNames.QA => 1,
            RoleNames.LineTech => 2,
            RoleNames.Operator => 3,
            _ => 99
        };
    }

    private static IReadOnlyList<string> CleanProductGroups(IReadOnlyList<string>? values)
    {
        return values?
            .Select(CleanProductGroup)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value)
            .ToArray() ?? [];
    }
}



