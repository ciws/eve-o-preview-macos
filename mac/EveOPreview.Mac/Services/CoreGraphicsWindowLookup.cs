using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace EveOPreview.Mac.Services;

internal static class CoreGraphicsWindowLookup
{
    private const uint KcgWindowListExcludeDesktopElements = 16;
    private const uint KcgNullWindowId = 0;
    private const uint KcfStringEncodingUtf8 = 0x0800_0100;
    private const int KcfNumberSInt64Type = 4;

    public static IReadOnlyList<CoreGraphicsWindowInfo> GetWindows()
    {
        var listRef = CGWindowListCopyWindowInfo(KcgWindowListExcludeDesktopElements, KcgNullWindowId);
        if (listRef == IntPtr.Zero)
        {
            return [];
        }

        try
        {
            var count = CFArrayGetCount(listRef);
            var result = new List<CoreGraphicsWindowInfo>((int)count);

            for (var i = 0; i < count; i++)
            {
                var dictRef = CFArrayGetValueAtIndex(listRef, i);
                if (dictRef == IntPtr.Zero)
                {
                    continue;
                }

                var pid = GetIntValue(dictRef, "kCGWindowOwnerPID");
                var windowId = GetIntValue(dictRef, "kCGWindowNumber");
                var layer = GetIntValue(dictRef, "kCGWindowLayer");
                var title = GetStringValue(dictRef, "kCGWindowName") ?? string.Empty;

                if (pid <= 0 || windowId <= 0 || layer != 0)
                {
                    continue;
                }

                result.Add(new CoreGraphicsWindowInfo
                {
                    ProcessId = pid,
                    WindowId = windowId,
                    WindowTitle = title,
                });
            }

            return result;
        }
        finally
        {
            CFRelease(listRef);
        }
    }

    private static int GetIntValue(IntPtr dictRef, string key)
    {
        var valueRef = GetDictionaryValue(dictRef, key);
        if (valueRef == IntPtr.Zero)
        {
            return 0;
        }

        long value = 0;
        return CFNumberGetValue(valueRef, KcfNumberSInt64Type, ref value) ? (int)value : 0;
    }

    private static string? GetStringValue(IntPtr dictRef, string key)
    {
        var valueRef = GetDictionaryValue(dictRef, key);
        return valueRef == IntPtr.Zero ? null : CFStringToString(valueRef);
    }

    private static IntPtr GetDictionaryValue(IntPtr dictRef, string key)
    {
        var keyRef = CFStringCreateWithCString(IntPtr.Zero, key, KcfStringEncodingUtf8);
        if (keyRef == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        try
        {
            return CFDictionaryGetValue(dictRef, keyRef);
        }
        finally
        {
            CFRelease(keyRef);
        }
    }

    private static string? CFStringToString(IntPtr cfStringRef)
    {
        var length = CFStringGetLength(cfStringRef);
        var maxBytes = CFStringGetMaximumSizeForEncoding(length, KcfStringEncodingUtf8) + 1;
        var buffer = Marshal.AllocHGlobal((int)maxBytes);
        try
        {
            if (!CFStringGetCString(cfStringRef, buffer, maxBytes, KcfStringEncodingUtf8))
            {
                return null;
            }

            return Marshal.PtrToStringUTF8(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern IntPtr CGWindowListCopyWindowInfo(uint option, uint relativeToWindow);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr cfTypeRef);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern long CFArrayGetCount(IntPtr arrayRef);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern IntPtr CFArrayGetValueAtIndex(IntPtr arrayRef, long index);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern IntPtr CFDictionaryGetValue(IntPtr dictRef, IntPtr keyRef);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern IntPtr CFStringCreateWithCString(IntPtr alloc, string cStr, uint encoding);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern long CFStringGetLength(IntPtr cfStringRef);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern long CFStringGetMaximumSizeForEncoding(long length, uint encoding);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CFStringGetCString(IntPtr cfStringRef, IntPtr buffer, long bufferSize, uint encoding);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CFNumberGetValue(IntPtr numberRef, int theType, ref long valuePtr);
}

internal sealed class CoreGraphicsWindowInfo
{
    public int ProcessId { get; init; }

    public int WindowId { get; init; }

    public string WindowTitle { get; init; } = string.Empty;
}
