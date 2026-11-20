using System;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace EveOPreview.Mac.Services;

// Used only on the UI thread. The native stream keeps just its most recent frame.
public sealed class NativeWindowCapture : IDisposable
{
    private const string Library = "EveCapture";
    private IntPtr _handle;
    private WriteableBitmap? _bitmap;
    private readonly StringBuilder _errorBuffer = new(512);
    private ulong _sequence;
    private DateTime _retryAfter;
    private bool _disposed;

    public int WindowId { get; }
    public PixelSize Size { get; }
    public string? Error { get; private set; }

    public NativeWindowCapture(int windowId, PixelSize size)
    {
        WindowId = windowId;
        Size = size;
        Start();
    }

    private void Start()
    {
        _retryAfter = DateTime.UtcNow.AddSeconds(10);
        Error = CheckPermission();
        if (Error is not null) return;

        _handle = eop_capture_create((uint)WindowId, Size.Width, Size.Height);
        Error = _handle == IntPtr.Zero ? "Unable to create native preview." : null;
    }

    public static string? CheckPermission(bool request = false)
    {
        if (!OperatingSystem.IsMacOSVersionAtLeast(12, 3))
        {
            return "Native previews require macOS 12.3 or later.";
        }

        try
        {
            if (eop_capture_permission(request ? 1 : 0) == 0)
            {
                return "Enable Screen Recording for EVE-O Preview in System Settings, then reopen the app.";
            }
            return null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return "Native capture module could not be loaded. Rebuild or reinstall the app.";
        }
    }

    public WriteableBitmap? GetLatestFrame()
    {
        if (_disposed) return null;
        if (Error is not null && DateTime.UtcNow >= _retryAfter)
        {
            ReleaseStream();
            Start();
        }
        if (_handle == IntPtr.Zero) return null;

        _errorBuffer.Clear();
        eop_capture_error(_handle, _errorBuffer, _errorBuffer.Capacity);
        if (_errorBuffer.Length > 0)
        {
            Error = _errorBuffer.ToString();
            return null;
        }

        var sequence = eop_capture_frame_info(_handle, out var width, out var height);
        if (sequence == 0 && DateTime.UtcNow >= _retryAfter)
        {
            Error = "No frames received. Check Screen Recording permission and that the client window is available.";
            _retryAfter = DateTime.UtcNow.AddSeconds(10);
        }
        if (sequence == 0 || sequence == _sequence) return null;
        var size = new PixelSize(width, height);
        if (_bitmap is null || _bitmap.PixelSize != size)
        {
            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        }

        using var framebuffer = _bitmap.Lock();
        sequence = eop_capture_copy_frame(_handle, framebuffer.Address, framebuffer.RowBytes, width, height);
        if (sequence == 0) return null;
        _sequence = sequence;
        Error = null;
        return _bitmap;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseStream();
        _bitmap?.Dispose();
        _bitmap = null;
    }

    private void ReleaseStream()
    {
        if (_handle != IntPtr.Zero) eop_capture_destroy(_handle);
        _handle = IntPtr.Zero;
        _sequence = 0;
    }

    [DllImport(Library)]
    private static extern int eop_capture_permission(int request);
    [DllImport(Library)]
    private static extern IntPtr eop_capture_create(uint windowId, int width, int height);
    [DllImport(Library)]
    private static extern ulong eop_capture_frame_info(IntPtr handle, out int width, out int height);
    [DllImport(Library)]
    private static extern ulong eop_capture_copy_frame(IntPtr handle, IntPtr destination, int stride, int width, int height);
    [DllImport(Library, CharSet = CharSet.Ansi)]
    private static extern void eop_capture_error(IntPtr handle, StringBuilder destination, int capacity);
    [DllImport(Library)]
    private static extern void eop_capture_destroy(IntPtr handle);
}
