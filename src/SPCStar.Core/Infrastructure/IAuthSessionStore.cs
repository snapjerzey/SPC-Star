using System.Collections.Concurrent;

namespace SPCStar.Core.Infrastructure;

public interface IAuthSessionStore
{
    void Save(string tokenHash, string userName, string credentialHash);
    bool Contains(string tokenHash, string userName, string credentialHash);
    void Remove(string tokenHash);
}

public sealed class InMemoryAuthSessionStore : IAuthSessionStore
{
    private readonly ConcurrentDictionary<string, (string UserName, string CredentialHash)> sessions = new(StringComparer.Ordinal);
    public void Save(string tokenHash, string userName, string credentialHash) => sessions[tokenHash] = (userName, credentialHash);
    public bool Contains(string tokenHash, string userName, string credentialHash) =>
        sessions.TryGetValue(tokenHash, out var session) &&
        session.UserName.Equals(userName, StringComparison.OrdinalIgnoreCase) && session.CredentialHash == credentialHash;
    public void Remove(string tokenHash) => sessions.TryRemove(tokenHash, out _);
}
