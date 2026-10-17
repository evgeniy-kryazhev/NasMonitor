using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using NasMonitor.Core;

namespace NasMonitor.App.Monitoring;

internal interface ICredentialStore
{
    bool IsAvailable { get; }
    NasCredentials? Read(Guid id);
    void Write(Guid id, NasCredentials credentials);
    void Delete(Guid id);
}
internal static class CredentialStore
{
    public static ICredentialStore Create() => OperatingSystem.IsWindows() ? new WindowsCredentialStore()
        : OperatingSystem.IsMacOS() ? new MacCredentialStore() : new UnavailableCredentialStore();
    private sealed class UnavailableCredentialStore : ICredentialStore
    {
        public bool IsAvailable => false;
        public NasCredentials? Read(Guid id) => null;
        public void Write(Guid id, NasCredentials credentials) => throw new PlatformNotSupportedException("Credential saving is available on Windows and macOS only.");
        public void Delete(Guid id) { }
    }
}
internal sealed class WindowsCredentialStore : ICredentialStore
{
    public bool IsAvailable => OperatingSystem.IsWindows();
    private static string Target(Guid id) => $"NasMonitor/{id:D}";
    public NasCredentials? Read(Guid id)
    {
        if (!CredRead(Target(id), 1, 0, out var pointer))
        {
            if (Marshal.GetLastPInvokeError() == 1168) return null;
            throw new Win32Exception();
        }
        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            var bytes = new byte[credential.BlobSize];
            Marshal.Copy(credential.Blob, bytes, 0, bytes.Length);
            try { return new(credential.Username, Encoding.UTF8.GetString(bytes)); }
            finally { Array.Clear(bytes); }
        }
        finally { CredFree(pointer); }
    }
    public void Write(Guid id, NasCredentials credentials)
    {
        var bytes = Encoding.UTF8.GetBytes(credentials.Password);
        if (bytes.Length > 2560) throw new ArgumentException("Password exceeds the Windows Credential Manager limit.");
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, pointer, bytes.Length);
            var credential = new Credential { Type = 1, Target = Target(id), Username = credentials.Username,
                BlobSize = (uint)bytes.Length, Blob = pointer, Persist = 2 };
            if (!CredWrite(ref credential, 0)) throw new Win32Exception();
        }
        finally
        {
            Marshal.Copy(new byte[bytes.Length], 0, pointer, bytes.Length);
            Marshal.FreeHGlobal(pointer);
            Array.Clear(bytes);
        }
    }
    public void Delete(Guid id)
    {
        if (!CredDelete(Target(id), 1, 0) && Marshal.GetLastPInvokeError() != 1168) throw new Win32Exception();
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags, Type;
        public string Target;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint BlobSize;
        public IntPtr Blob;
        public uint Persist, AttributeCount;
        public IntPtr Attributes;
        public string? Alias;
        public string Username;
    }
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredWrite(ref Credential credential, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredDelete(string target, uint type, uint flags);
    [DllImport("advapi32.dll")] private static extern void CredFree(IntPtr pointer);
}
internal sealed class MacCredentialStore : ICredentialStore
{
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private static readonly byte[] Service = Encoding.UTF8.GetBytes("NasMonitor");
    public bool IsAvailable => OperatingSystem.IsMacOS();
    private static byte[] Account(Guid id) => Encoding.UTF8.GetBytes(id.ToString("D"));
    public NasCredentials? Read(Guid id)
    {
        var account = Account(id);
        var status = Find(IntPtr.Zero, (uint)Service.Length, Service, (uint)account.Length, account, out var size, out var data, out var item);
        if (status == -25300) return null; // errSecItemNotFound
        Check(status);
        try
        {
            var bytes = new byte[size];
            Marshal.Copy(data, bytes, 0, bytes.Length);
            try { return JsonSerializer.Deserialize<NasCredentials>(bytes); }
            finally { Array.Clear(bytes); }
        }
        finally { FreeContent(IntPtr.Zero, data); CFRelease(item); }
    }
    public void Write(Guid id, NasCredentials credentials)
    {
        var account = Account(id);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(credentials);
        var status = Find(IntPtr.Zero, (uint)Service.Length, Service, (uint)account.Length, account, out _, out var data, out var item);
        try
        {
            if (status == -25300)
                Check(Add(IntPtr.Zero, (uint)Service.Length, Service, (uint)account.Length, account, (uint)bytes.Length, bytes, out item));
            else
            {
                Check(status);
                Check(Modify(item, IntPtr.Zero, (uint)bytes.Length, bytes));
            }
        }
        finally
        {
            Array.Clear(bytes);
            if (data != IntPtr.Zero) FreeContent(IntPtr.Zero, data);
            if (item != IntPtr.Zero) CFRelease(item);
        }
    }
    public void Delete(Guid id)
    {
        var account = Account(id);
        var status = Find(IntPtr.Zero, (uint)Service.Length, Service, (uint)account.Length, account, out _, out var data, out var item);
        if (status == -25300) return;
        Check(status);
        try { Check(Remove(item)); }
        finally { FreeContent(IntPtr.Zero, data); CFRelease(item); }
    }
    private static void Check(int status)
    {
        if (status != 0) throw new InvalidOperationException($"Keychain operation failed (OSStatus {status}).");
    }
    // Generic-password items in the user's default login keychain. No secret command-line arguments.
    [DllImport(Security, EntryPoint = "SecKeychainFindGenericPassword")] private static extern int Find(IntPtr keychain, uint serviceLength, byte[] service, uint accountLength, byte[] account, out uint size, out IntPtr data, out IntPtr item);
    [DllImport(Security, EntryPoint = "SecKeychainAddGenericPassword")] private static extern int Add(IntPtr keychain, uint serviceLength, byte[] service, uint accountLength, byte[] account, uint size, byte[] data, out IntPtr item);
    [DllImport(Security, EntryPoint = "SecKeychainItemModifyAttributesAndData")] private static extern int Modify(IntPtr item, IntPtr attributes, uint size, byte[] data);
    [DllImport(Security, EntryPoint = "SecKeychainItemDelete")] private static extern int Remove(IntPtr item);
    [DllImport(Security, EntryPoint = "SecKeychainItemFreeContent")] private static extern int FreeContent(IntPtr attributes, IntPtr data);
    [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr item);
}
