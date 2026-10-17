using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using NasMonitor.Core.Security;

namespace NasMonitor.Platform.MacOS.Security;

/// <summary>Generic passwords in macOS Keychain using Security.framework, without shell arguments.</summary>
public sealed partial class MacSecretStore : ISecretStore
{
    private const string SecurityFramework = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const int ItemNotFound = -25300;
    private static readonly nint SecurityLibrary = NativeLibrary.Load(SecurityFramework);
    private static readonly nint FoundationLibrary = NativeLibrary.Load(CoreFoundation);

    /// <inheritdoc />
    public Task WriteAsync(Guid credentialId, string password, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using NativeDictionary query = CreateQuery(credentialId);
                using var attributes = new NativeDictionary();
                byte[] bytes = Encoding.UTF8.GetBytes(password);
                try
                {
                    attributes.SetData("kSecValueData", bytes);
                    int result = SecItemUpdate(query.Handle, attributes.Handle);
                    if (result == ItemNotFound)
                    {
                        query.SetData("kSecValueData", bytes);
                        result = SecItemAdd(query.Handle, 0);
                    }

                    CheckStatus(result);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(bytes);
                }
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<string?> ReadAsync(Guid credentialId, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using NativeDictionary query = CreateQuery(credentialId);
                query.SetConstant("kSecReturnData", FoundationConstant("kCFBooleanTrue"));
                query.SetConstant("kSecMatchLimit", SecurityConstant("kSecMatchLimitOne"));
                int status = SecItemCopyMatching(query.Handle, out nint result);
                if (status == ItemNotFound)
                {
                    return null;
                }

                CheckStatus(status);
                try
                {
                    int length = checked((int)CFDataGetLength(result));
                    byte[] bytes = new byte[length];
                    try
                    {
                        Marshal.Copy(CFDataGetBytePtr(result), bytes, 0, length);
                        return Encoding.UTF8.GetString(bytes);
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(bytes);
                    }
                }
                finally
                {
                    CFRelease(result);
                }
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task DeleteAsync(Guid credentialId, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using NativeDictionary query = CreateQuery(credentialId);
                int status = SecItemDelete(query.Handle);
                if (status != ItemNotFound)
                {
                    CheckStatus(status);
                }
            },
            cancellationToken);
    }

    private static NativeDictionary CreateQuery(Guid credentialId)
    {
        if (credentialId == Guid.Empty)
        {
            throw new ArgumentException("secret.invalid-id");
        }

        var query = new NativeDictionary();
        query.SetConstant("kSecClass", SecurityConstant("kSecClassGenericPassword"));
        query.SetString("kSecAttrService", "NASMonitor.DSM");
        query.SetString("kSecAttrAccount", credentialId.ToString("N"));
        query.SetConstant("kSecAttrSynchronizable", FoundationConstant("kCFBooleanFalse"));
        return query;
    }

    private static void CheckStatus(int status)
    {
        if (status != 0)
        {
            throw new InvalidOperationException("keychain.error-" + status);
        }
    }

    private static nint SecurityConstant(string name)
    {
        return Marshal.ReadIntPtr(NativeLibrary.GetExport(SecurityLibrary, name));
    }

    private static nint FoundationConstant(string name)
    {
        return Marshal.ReadIntPtr(NativeLibrary.GetExport(FoundationLibrary, name));
    }
}
