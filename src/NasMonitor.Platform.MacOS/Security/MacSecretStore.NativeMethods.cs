using System.Runtime.InteropServices;

namespace NasMonitor.Platform.MacOS.Security;

public sealed partial class MacSecretStore
{
    [DllImport(SecurityFramework)]
    private static extern int SecItemAdd(nint attributes, nint result);

    [DllImport(SecurityFramework)]
    private static extern int SecItemUpdate(nint query, nint attributes);

    [DllImport(SecurityFramework)]
    private static extern int SecItemCopyMatching(nint query, out nint result);

    [DllImport(SecurityFramework)]
    private static extern int SecItemDelete(nint query);

    [DllImport(CoreFoundation)]
    private static extern nint CFDictionaryCreateMutable(nint allocator, nint capacity, nint keys, nint values);

    [DllImport(CoreFoundation)]
    private static extern void CFDictionarySetValue(nint dictionary, nint key, nint value);

    [DllImport(CoreFoundation)]
    private static extern nint CFStringCreateWithCString(nint allocator,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string value,
        uint encoding);

    [DllImport(CoreFoundation)]
    private static extern nint CFDataCreate(nint allocator, byte[] bytes, nint length);

    [DllImport(CoreFoundation)]
    private static extern nint CFDataGetLength(nint data);

    [DllImport(CoreFoundation)]
    private static extern nint CFDataGetBytePtr(nint data);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(nint value);

    private sealed class NativeDictionary : IDisposable
    {
        public nint Handle
        {
            get;
        } = CFDictionaryCreateMutable(0,
            0,
            NativeLibrary.GetExport(FoundationLibrary, "kCFTypeDictionaryKeyCallBacks"),
            NativeLibrary.GetExport(FoundationLibrary, "kCFTypeDictionaryValueCallBacks"));

        public void Dispose()
        {
            CFRelease(Handle);
        }

        public void SetConstant(string key, nint value)
        {
            CFDictionarySetValue(Handle, SecurityConstant(key), value);
        }

        public void SetString(string key, string value)
        {
            nint native = CFStringCreateWithCString(0, value, 0x08000100);
            try
            {
                SetConstant(key, native);
            }
            finally
            {
                CFRelease(native);
            }
        }

        public void SetData(string key, byte[] value)
        {
            nint data = CFDataCreate(0, value, value.Length);
            try
            {
                SetConstant(key, data);
            }
            finally
            {
                CFRelease(data);
            }
        }
    }
}
