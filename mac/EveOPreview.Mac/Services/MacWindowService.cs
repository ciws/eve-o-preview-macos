using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EveOPreview.Mac.Models;

namespace EveOPreview.Mac.Services;

public sealed class MacWindowService : IMacWindowService
{
    public async Task<IReadOnlyList<ClientWindowInfo>> GetWindowsAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return [];
        }

        try
        {
            var lines = await RunAppleScriptAsync(
                [
                    "tell application \"System Events\"",
                    "set output to {}",
                    "repeat with p in (application processes whose background only is false)",
                    "set procName to name of p",
                    "set procId to unix id of p",
                    "set procWindows to {}",
                    "try",
                    "set procWindows to windows of p",
                    "end try",
                    "repeat with w in procWindows",
                    "set winName to \"\"",
                    "set winNumber to \"\"",
                    "try",
                    "set winName to name of w as text",
                    "end try",
                    "try",
                    "set winNumber to (value of attribute \"AXWindowNumber\" of w) as text",
                    "end try",
                    "set end of output to procName & tab & (procId as text) & tab & winNumber & tab & winName",
                    "end repeat",
                    "end repeat",
                    "set AppleScript's text item delimiters to linefeed",
                    "return output as text",
                    "end tell",
                ],
                cancellationToken);

            var results = ParseAppleScriptWindowLines(lines);
            ReconcileWindows(results, GetWindowsFromCoreGraphics());
            return results;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Fall back to CoreGraphics-only discovery when Accessibility/System Events
            // is unavailable. This keeps the idle app quiet instead of showing a scary
            // error before EVE is even running.
            return GetWindowsFromCoreGraphics();
        }
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
        var activated = RunningApplicationActivator.TryActivate(window.ProcessId);
        var escapedWindowTitle = EscapeAppleScriptString(window.WindowTitle);

        try
        {
            await RunAppleScriptAsync(
                [
                    "tell application \"System Events\"",
                    $"set targetProcess to first application process whose unix id is {window.ProcessId}",
                    "set frontmost of targetProcess to true",
                    "set targetWindow to missing value",
                    "try",
                    $"set matchingWindows to (windows of targetProcess whose value of attribute \"AXWindowNumber\" is {window.WindowId})",
                    "if (count of matchingWindows) > 0 then set targetWindow to item 1 of matchingWindows",
                    "end try",
                    "if targetWindow is missing value then",
                    $"if (exists (first window of targetProcess whose name is \"{escapedWindowTitle}\")) then",
                    $"set targetWindow to first window of targetProcess whose name is \"{escapedWindowTitle}\"",
                    "end if",
                    "end if",
                    "if targetWindow is not missing value then",
                    "perform action \"AXRaise\" of targetWindow",
                    "end if",
                    "end tell",
                ],
                cancellationToken);
        }
        catch (Exception ex) when (activated && ex is not OperationCanceledException)
        {
            // System Events cannot see some apps (e.g. the native EVE client).
            // The app is already frontmost, so skipping the window raise is fine.
        }
    }

    private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(5);

    public async Task<byte[]?> CaptureWindowPreviewPngAsync(int windowId, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsMacOS() || windowId <= 0)
        {
            return null;
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"eve-preview-{windowId}-{Guid.NewGuid():N}.png");
        // Bound the wait on the child process: without this, a screencapture call that
        // never exits would hang this call forever, which in turn stalls every future
        // preview tick since OverlayWindowManager's refresh guard never clears.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(CaptureTimeout);

        Process? process = null;
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "/usr/sbin/screencapture",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("-x");
            startInfo.ArgumentList.Add("-l");
            startInfo.ArgumentList.Add(windowId.ToString());
            startInfo.ArgumentList.Add(tempPath);

            process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            await process.WaitForExitAsync(timeoutCts.Token);
            if (process.ExitCode != 0 || !File.Exists(tempPath))
            {
                return null;
            }

            var bytes = await File.ReadAllBytesAsync(tempPath, cancellationToken);
            return bytes.Length == 0 ? null : bytes;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (process is { HasExited: false })
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Ignore failures killing an already-exiting process.
                }
            }

            process?.Dispose();

            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // Ignore cleanup failures for temp files.
            }
        }
    }

    private static string EscapeAppleScriptString(string input) => input.Replace("\"", "\\\"");

    private static List<ClientWindowInfo> ParseAppleScriptWindowLines(IReadOnlyList<string> lines)
    {
        var results = new List<ClientWindowInfo>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var firstTab = line.IndexOf('\t');
            if (firstTab <= 0)
            {
                continue;
            }

            var secondTab = line.IndexOf('\t', firstTab + 1);
            if (secondTab <= firstTab)
            {
                continue;
            }

            var thirdTab = line.IndexOf('\t', secondTab + 1);
            if (thirdTab <= secondTab)
            {
                continue;
            }

            var processName = line[..firstTab].Trim();
            var pidText = line[(firstTab + 1)..secondTab].Trim();
            var windowIdText = line[(secondTab + 1)..thirdTab].Trim();
            var windowTitle = line[(thirdTab + 1)..].Trim();

            if (string.IsNullOrWhiteSpace(processName)
                || !int.TryParse(pidText, out var processId) || processId <= 0)
            {
                continue;
            }

            var windowId = 0;
            if (!string.IsNullOrWhiteSpace(windowIdText))
            {
                _ = int.TryParse(windowIdText, out windowId);
            }

            results.Add(new ClientWindowInfo
            {
                WindowId = windowId,
                ProcessId = processId,
                ProcessName = processName,
                WindowTitle = windowTitle,
            });
        }

        return results;
    }

    private static IReadOnlyList<ClientWindowInfo> GetWindowsFromCoreGraphics()
    {
        var cgWindows = CoreGraphicsWindowLookup.GetVisibleWindows();
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

    private static void ReconcileWindows(
        List<ClientWindowInfo> windows, IReadOnlyList<ClientWindowInfo> coreGraphicsWindows)
    {
        foreach (var window in coreGraphicsWindows)
        {
            var index = windows.FindIndex(x => x.ProcessId == window.ProcessId && x.WindowId == window.WindowId);
            if (index < 0 && !string.IsNullOrWhiteSpace(window.WindowTitle))
            {
                index = windows.FindIndex(x => x.ProcessId == window.ProcessId && x.WindowId <= 0
                    && x.WindowTitle.Equals(window.WindowTitle, StringComparison.OrdinalIgnoreCase));
            }

            if (index < 0)
            {
                if (window.IsEveClient)
                {
                    windows.Add(window);
                }
                continue;
            }

            var existing = windows[index];
            if (window.IsEveClient && !existing.IsEveClient)
            {
                windows[index] = window;
            }
            else if (existing.WindowId <= 0)
            {
                windows[index] = WithWindowId(existing, window.WindowId);
            }
        }

        // CoreGraphics may omit titles. Use a PID match only when both sides have one window.
        var untitledWindows = coreGraphicsWindows.GroupBy(x => x.ProcessId)
            .Where(group => group.Count() == 1 && string.IsNullOrWhiteSpace(group.First().WindowTitle))
            .ToDictionary(group => group.Key, group => group.First().WindowId);
        var windowCounts = windows.GroupBy(x => x.ProcessId).ToDictionary(group => group.Key, group => group.Count());

        for (var i = 0; i < windows.Count; i++)
        {
            var window = windows[i];
            if (window.IsEveClient && window.WindowId <= 0 && windowCounts[window.ProcessId] == 1
                && untitledWindows.TryGetValue(window.ProcessId, out var windowId))
            {
                windows[i] = WithWindowId(window, windowId);
            }
        }
    }

    private static ClientWindowInfo WithWindowId(ClientWindowInfo window, int windowId) => new()
    {
        WindowId = windowId,
        ProcessId = window.ProcessId,
        ProcessName = window.ProcessName,
        WindowTitle = window.WindowTitle,
    };

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
