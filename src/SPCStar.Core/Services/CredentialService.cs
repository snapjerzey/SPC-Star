using SPCStar.Core.Infrastructure;

namespace SPCStar.Core.Services;

public sealed class CredentialService(InMemorySpcRepository repository)
{
    public bool ValidateCredential(string userName, string password)
    {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
        {
            return false;
        }

        var user = repository.Users.FirstOrDefault(item => item.UserName.Equals(userName, StringComparison.OrdinalIgnoreCase));
        return user is not null && PasswordHasher.Verify(password, user.PasswordHash, user.PasswordSalt);
    }
}
