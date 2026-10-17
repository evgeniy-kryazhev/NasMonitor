namespace NasMonitor.Core.Profiles;

/// <summary>Non-secret connection configuration for one NAS.</summary>
public sealed record NasProfile(
    Guid Id,
    string Name,
    Uri Endpoint,
    string Username,
    bool Enabled = true,
    Guid? CredentialId = null,
    Uri? ExternalEndpoint = null)
{
    /// <summary>Reject ambiguous URLs and plaintext authentication before any network access.</summary>
    public void Validate()
    {
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Name) || Name.Length > 100 ||
            string.IsNullOrWhiteSpace(Username) || Username.Length > 256)
        {
            throw new ArgumentException("profile.invalid");
        }

        ValidateEndpoint(Endpoint);
        if (ExternalEndpoint is not null)
        {
            ValidateEndpoint(ExternalEndpoint);
        }
    }

    /// <summary>Require an HTTPS origin without credentials, query, fragment or proxy subpath.</summary>
    public static void ValidateEndpoint(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrEmpty(endpoint.Host) || !string.IsNullOrEmpty(endpoint.UserInfo) ||
            !string.IsNullOrEmpty(endpoint.Query) || !string.IsNullOrEmpty(endpoint.Fragment) ||
            endpoint.AbsolutePath != "/")
        {
            throw new ArgumentException("endpoint.https-origin-required");
        }
    }
}
