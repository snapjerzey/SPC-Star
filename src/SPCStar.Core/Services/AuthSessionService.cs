using SPCStar.Core.Infrastructure;

namespace SPCStar.Core.Services;

public sealed record LoginRequest(string UserName, string Password);

public sealed record UserSessionDto(
    string UserName,
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

        return new UserSessionDto(user.UserName, roles, permissions, user.ProductGroups.OrderBy(group => group).ToArray(), $"dev-session:{user.UserName}");
    }
}
