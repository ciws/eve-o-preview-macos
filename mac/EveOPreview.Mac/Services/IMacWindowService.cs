using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EveOPreview.Mac.Models;

namespace EveOPreview.Mac.Services;

public interface IMacWindowService
{
    string? PreviewPermissionError { get; }

    Task<IReadOnlyList<ClientWindowInfo>> GetWindowsAsync(CancellationToken cancellationToken = default);

    Task ActivateWindowAsync(ClientWindowInfo window, CancellationToken cancellationToken = default);

    NativeWindowCapture CreateWindowPreview(int windowId, Avalonia.PixelSize size);
}
