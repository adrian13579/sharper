using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SharpLint.Daemon;

public static class DaemonProcess
{
    /// <summary>Called only in the detached CLI host, never by an embedded/in-process host.</summary>
    public static void DetachFromTerminal()
    {
        if (!OperatingSystem.IsWindows() && SetSessionId() < 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not detach the daemon from its parent terminal.");
    }

    [DllImport("libc", EntryPoint = "setsid", SetLastError = true)]
    private static extern int SetSessionId();
}
