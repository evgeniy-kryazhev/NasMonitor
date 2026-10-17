using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using NasMonitor.Core;

namespace NasMonitor.Platform.Windows;

/// <summary>Stores generic credentials in the current Windows user's Credential Manager.</summary>
public sealed class WindowsSecretStore : ISecretStore
{
    /// <inheritdoc />
    public Task WriteAsync(Guid credentialId, string password, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] bytes = Encoding.Unicode.GetBytes(password);
            nint buffer = 0;
            try
            {
                if (bytes.Length > 2560)
                {
                    throw new ArgumentException("secret.too-long");
                }
                buffer = Marshal.AllocHGlobal(bytes.Length);
                Marshal.Copy(bytes, 0, buffer, bytes.Length);
                var credential = new Credential
                {
                    Type = 1,
                    TargetName = Target(credentialId),
                    UserName = "NASMonitor",
                    CredentialBlobSize = (uint)bytes.Length,
                    CredentialBlob = buffer,
                    Persist = 2
                };
                if (!CredWriteW(ref credential, 0))
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
                if (buffer != 0)
                {
                    Marshal.Copy(bytes, 0, buffer, bytes.Length);
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<string?> ReadAsync(Guid credentialId, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!CredReadW(Target(credentialId), 1, 0, out nint pointer))
            {
                int error = Marshal.GetLastPInvokeError();
                if (error == 1168)
                {
                    return null;
                }
                throw new Win32Exception(error);
            }
            try
            {
                Credential credential = Marshal.PtrToStructure<Credential>(pointer);
                return Marshal.PtrToStringUni(credential.CredentialBlob, checked((int)credential.CredentialBlobSize / 2));
            }
            finally
            {
                CredFree(pointer);
            }
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task DeleteAsync(Guid credentialId, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!CredDeleteW(Target(credentialId), 1, 0))
            {
                int error = Marshal.GetLastPInvokeError();
                if (error != 1168)
                {
                    throw new Win32Exception(error);
                }
            }
        }, cancellationToken);
    }

    private static string Target(Guid credentialId)
    {
        if (credentialId == Guid.Empty)
        {
            throw new ArgumentException("secret.invalid-id");
        }
        return "NASMonitor/DSM/" + credentialId.ToString("N");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public nint CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public nint Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWriteW(ref Credential credential, uint flags);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredReadW(string target, uint type, uint flags, out nint credential);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDeleteW(string target, uint type, uint flags);
    [DllImport("advapi32.dll")]
    private static extern void CredFree(nint credential);
}
