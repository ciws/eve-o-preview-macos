using System;
using System.Runtime.InteropServices;

namespace EveOPreview.Mac.Services;

// Activates an app by PID through NSRunningApplication. Unlike System Events,
// this works for apps that System Events reports with the wrong unix id, such
// as the native macOS EVE client.
internal static class RunningApplicationActivator
{
    private const ulong NsApplicationActivateIgnoringOtherApps = 1 << 1;

    public static bool TryActivate(int processId)
    {
        var runningApplicationClass = objc_getClass("NSRunningApplication");
        if (runningApplicationClass == IntPtr.Zero)
        {
            return false;
        }

        var application = objc_msgSend_IntPtr_Int(
            runningApplicationClass,
            sel_registerName("runningApplicationWithProcessIdentifier:"),
            processId);
        if (application == IntPtr.Zero)
        {
            return false;
        }

        return objc_msgSend_Bool_ULong(
            application,
            sel_registerName("activateWithOptions:"),
            NsApplicationActivateIgnoringOtherApps);
    }

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern IntPtr objc_getClass(string name);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern IntPtr sel_registerName(string name);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_IntPtr_Int(IntPtr receiver, IntPtr selector, int arg);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool objc_msgSend_Bool_ULong(IntPtr receiver, IntPtr selector, ulong arg);
}
