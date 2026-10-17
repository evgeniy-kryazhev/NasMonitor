# Read-only shell topology. No window titles, HWNDs or user data are printed.
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class ShellTopology
{
    private delegate bool WindowCallback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(WindowCallback callback, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, WindowCallback callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr window, StringBuilder buffer, int maximum);
    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr window);

    private static string ClassName(IntPtr window)
    {
        var buffer = new StringBuilder(256);
        GetClassNameW(window, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    public static void Print()
    {
        EnumWindows((window, parameter) =>
        {
            string className = ClassName(window);
            if (className == "Progman" || className == "WorkerW")
            {
                Console.WriteLine("top: " + className);
                EnumChildWindows(window, (child, unused) =>
                {
                    string childClass = ClassName(child);
                    if (childClass == "WorkerW" || childClass == "SHELLDLL_DefView" || childClass == "SysListView32")
                    {
                        Console.WriteLine("  child: " + childClass + " parent: " + ClassName(GetParent(child)));
                    }
                    return true;
                }, IntPtr.Zero);
            }
            return true;
        }, IntPtr.Zero);
    }
}
'@
[ShellTopology]::Print()
