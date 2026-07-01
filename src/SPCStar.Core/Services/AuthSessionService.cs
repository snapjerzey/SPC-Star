using SPCStar.Core.Infrastructure;

namespace SPCStar.Core.Services;

public sealed record LoginRequest(string UserName, string Password);

public sealed record ChangePasswordRequest(string UserName, string CurrentPassword, string NewPassword, string ConfirmPassword);

public sealed record UserSessionDto(
    string UserName,
    string Shift,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<string> ProductGroups,
    string SessionToken);

public sealed class AuthSessionService(
    ISpcRepository repository,
    CredentialService credentialService)
{
    public ServiceResult<UserSessionDto> Login(LoginRequest request)
    {
        if (!credentialService.ValidateCredential(request.UserName, request.Password))
        {
            return ServiceResult<UserSessionDto>.Fail("Invalid username or password.");
        }

        var user = repository.Users.First(item => item.UserName.Equals(request.UserName, StringComparison.OrdinalIgnoreCase));
        return ServiceResult<UserSessionDto>.Ok(BuildSession(user.UserName));
    }

    public ServiceResult<UserSessionDto> CurrentUser(string userName)
    {
        if (repository.Users.All(item => !item.UserName.Equals(userName, StringComparison.OrdinalIgnoreCase)))
        {
            return ServiceResult<UserSessionDto>.Fail("User was not found.");
        }

        return ServiceResult<UserSessionDto>.Ok(BuildSession(userName));
    }

    public ServiceResult ChangePassword(ChangePasswordRequest request)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(request.UserName))
        {
            errors.Add("UserName is required.");
        }

        if (string.IsNullOrWhiteSpace(request.CurrentPassword))
        {
            errors.Add("Current password is required.");
        }

        if (string.IsNullOrWhiteSpace(request.NewPassword))
        {
            errors.Add("New password is required.");
        }
        else if (request.NewPassword.Length < 4)
        {
            errors.Add("New password must be at least 4 characters.");
        }

        if (!string.Equals(request.NewPassword, request.ConfirmPassword, StringComparison.Ordinal))
        {
            errors.Add("New password and confirmation do not match.");
        }

        if (errors.Count > 0)
        {
            return ServiceResult.Fail(errors);
        }

        var user = repository.Users.FirstOrDefault(item => item.UserName.Equals(request.UserName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (user is null || !credentialService.ValidateCredential(request.UserName, request.CurrentPassword))
        {
            return ServiceResult.Fail("Current username or password is incorrect.");
        }

        var (hash, salt) = PasswordHasher.HashPassword(request.NewPassword);
        user.PasswordHash = hash;
        user.PasswordSalt = salt;
        return ServiceResult.Ok();
    }

    private UserSessionDto BuildSession(string userName)
    {
        var user = repository.Users.First(item => item.UserName.Equals(userName, StringComparison.OrdinalIgnoreCase));
        var roles = user.Roles
            .Select(role => role.Name)
            .OrderBy(role => role)
            .ToArray();
        var permissions = user.Roles
            .SelectMany(role => role.Permissions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(permission => permission)
            .ToArray();

        return new UserSessionDto(user.UserName, user.Shift, roles, permissions, user.ProductGroups.OrderBy(group => group).ToArray(), $"dev-session:{user.UserName}");
    }
}
