using NasMonitor.Core;

namespace NasMonitor.Infrastructure;

/// <summary>Coordinates configuration and OS secrets without exposing secrets in profile objects.</summary>
public sealed class NasProfileService(NasProfileRepository repository, ISecretStore secrets)
{
    /// <summary>Load all saved NAS profiles.</summary>
    public Task<IReadOnlyList<NasProfile>> LoadAsync(CancellationToken cancellationToken)
    {
        return repository.LoadAsync(cancellationToken);
    }

    /// <summary>Save metadata and optionally replace a remembered password using a new opaque key.</summary>
    public async Task<NasProfile> SaveAsync(NasProfile requested, string? password, bool rememberPassword,
        CancellationToken cancellationToken)
    {
        requested.Validate();
        IReadOnlyList<NasProfile> profiles = await repository.LoadAsync(cancellationToken).ConfigureAwait(false);
        NasProfile? existing = profiles.FirstOrDefault(profile => profile.Id == requested.Id);
        bool sameIdentity = existing is not null && existing.Endpoint == requested.Endpoint &&
            existing.ExternalEndpoint == requested.ExternalEndpoint && existing.Username == requested.Username;
        Guid? credentialId = rememberPassword && sameIdentity ? existing?.CredentialId : null;
        Guid? createdCredentialId = null;
        if (rememberPassword && !string.IsNullOrEmpty(password))
        {
            createdCredentialId = Guid.NewGuid();
            await secrets.WriteAsync(createdCredentialId.Value, password, cancellationToken).ConfigureAwait(false);
            credentialId = createdCredentialId;
        }
        NasProfile saved = requested with { CredentialId = credentialId };
        try
        {
            await repository.UpsertAsync(saved, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (createdCredentialId.HasValue)
            {
                await secrets.DeleteAsync(createdCredentialId.Value, CancellationToken.None).ConfigureAwait(false);
            }
            throw;
        }
        if (existing?.CredentialId is Guid previousId && previousId != credentialId)
        {
            // Configuration is committed before deleting the old item: a failure never
            // leaves a profile referencing a password that was deliberately removed.
            await secrets.DeleteAsync(previousId, cancellationToken).ConfigureAwait(false);
        }
        return saved;
    }

    /// <summary>Remove remembered credentials before deleting profile metadata.</summary>
    public async Task DeleteAsync(NasProfile profile, CancellationToken cancellationToken)
    {
        if (profile.CredentialId is Guid credentialId)
        {
            await secrets.DeleteAsync(credentialId, cancellationToken).ConfigureAwait(false);
        }
        await repository.DeleteAsync(profile.Id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Retrieve a saved password only for the exact saved identity.</summary>
    public async Task<string?> ReadPasswordAsync(NasProfile requested, CancellationToken cancellationToken)
    {
        IReadOnlyList<NasProfile> profiles = await repository.LoadAsync(cancellationToken).ConfigureAwait(false);
        NasProfile? saved = profiles.FirstOrDefault(profile => profile.Id == requested.Id);
        if (saved is null || saved.Endpoint != requested.Endpoint || saved.ExternalEndpoint != requested.ExternalEndpoint ||
            saved.Username != requested.Username || saved.CredentialId is not Guid credentialId)
        {
            return null;
        }
        return await secrets.ReadAsync(credentialId, cancellationToken).ConfigureAwait(false);
    }
}
