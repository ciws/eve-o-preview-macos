using System;

namespace EveOPreview.Mac.Models;

public sealed class ClientWindowInfo
{
    public int WindowId { get; init; }

    public int ProcessId { get; init; }

    public required string ProcessName { get; init; }

    public required string WindowTitle { get; init; }

    internal bool IsEveClient =>
        ProcessName.Equals("exefile", StringComparison.OrdinalIgnoreCase)
        && WindowTitle.StartsWith("EVE - ", StringComparison.OrdinalIgnoreCase);

    public string DisplayName =>
        string.IsNullOrWhiteSpace(WindowTitle)
            ? ProcessName
            : $"{ProcessName} - {WindowTitle}";
}
