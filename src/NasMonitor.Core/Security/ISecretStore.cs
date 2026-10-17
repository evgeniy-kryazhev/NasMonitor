namespace NasMonitor.Core.Security;

/// <summary>OS-protected password storage; identifiers are opaque and have no NAS/user data.</summary>
public interface ISecretStore
{
    /// <summary>Persist a password under an opaque identifier.</summary>
    public Task WriteAsync(Guid credentialId, string password, CancellationToken cancellationToken);

    /// <summary>Read a password, or null if the item no longer exists.</summary>
    public Task<string?> ReadAsync(Guid credentialId, CancellationToken cancellationToken);

    /// <summary>Remove a password; a missing item is already deleted.</summary>
    public Task DeleteAsync(Guid credentialId, CancellationToken cancellationToken);
}
