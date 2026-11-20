using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EveOPreview.Mac.Models;

namespace EveOPreview.Mac.Services;

public sealed class MacWindowService : IMacWindowService
{
    public string? PreviewPermissionError { get; private set; }

    public Task<IReadOnlyList<ClientWindowInfo>> GetWindowsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsMacOS())
        {
            return Task.FromResult<IReadOnlyList<ClientWindowInfo>>([]);
        }

        PreviewPermissionError = NativeWindowCapture.CheckPermission();
        // WindowServer supplies native IDs without waiting for System Events.
        return Task.FromResult(GetWindowsFromCoreGraphics());
    }

    public async Task ActivateWindowAsync(ClientWindowInfo window, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        if (window.ProcessId <= 0)
        {
            return;
        }

        // Every EVE client is a separate process of the same app, and System Events
        // names them all "exefile". A process stored in an AppleScript variable is
        // referred to by name, so later commands on it (set frontmost, windows of)
        // land on the first "exefile" process: the first-launched client. On macOS
        // 27.2 that sent focus back to the first client after every switch.
        // Activate by PID, and fall back to System Events only if that fails, using
        // the unix id inline so the reference can't fall back to the process name.
        if (RunningApplicationActivator.TryActivate(window.ProcessId))
        {
            return;
        }

        await RunAppleScriptAsync(
            [
                $"tell application \"System Events\" to set frontmost of (first application process whose unix id is {window.ProcessId}) to true",
            ],
            cancellationToken);
    }

    public NativeWindowCapture CreateWindowPreview(int windowId, Avalonia.PixelSize size) => new(windowId, size);

    private static IReadOnlyList<ClientWindowInfo> GetWindowsFromCoreGraphics()
    {
        var cgWindows = CoreGraphicsWindowLookup.GetWindows();
        if (cgWindows.Count == 0)
        {
            return [];
        }

        var results = new List<ClientWindowInfo>(cgWindows.Count);
        foreach (var window in cgWindows)
        {
            var processName = TryGetProcessName(window.ProcessId);
            if (string.IsNullOrWhiteSpace(processName))
            {
                continue;
            }

            results.Add(new ClientWindowInfo
            {
                WindowId = window.WindowId,
                ProcessId = window.ProcessId,
                ProcessName = processName,
                WindowTitle = window.WindowTitle,
            });
        }

        return results;
    }

    private static string? TryGetProcessName(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<IReadOnlyList<string>> RunAppleScriptAsync(
        IReadOnlyList<string> scriptLines,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "/usr/bin/osascript",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var line in scriptLines)
        {
            startInfo.ArgumentList.Add("-e");
            startInfo.ArgumentList.Add(line);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start osascript process.");
        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        var stdOut = await stdOutTask;
        var stdErr = await stdErrTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stdErr) ? "osascript failed." : stdErr.Trim());
        }

        return stdOut
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
    }
}
