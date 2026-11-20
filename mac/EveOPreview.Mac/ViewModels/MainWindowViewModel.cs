using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EveOPreview.Mac.Models;
using EveOPreview.Mac.Services;

namespace EveOPreview.Mac.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IMacWindowService _windowService;
    private readonly IAppSettingsStore _settingsStore;
    private readonly IOverlayWindowManager _overlayWindowManager;
    private readonly DispatcherTimer _refreshTimer;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly Task _settingsLoaded;
    private Dictionary<string, OverlayPosition> _overlayPositions = new();

    public MainWindowViewModel(
        IMacWindowService windowService,
        IAppSettingsStore settingsStore,
        IOverlayWindowManager overlayWindowManager)
    {
        _windowService = windowService;
        _settingsStore = settingsStore;
        _overlayWindowManager = overlayWindowManager;

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2),
        };
        _refreshTimer.Tick += async (_, _) => await RefreshClientsAsync();
        _settingsLoaded = LoadSettingsAsync();
        _refreshTimer.Start();
        _ = RefreshClientsAsync();
    }

    public string Title { get; } = "EVE-O Preview (macOS)";

    public ObservableCollection<ClientWindowInfo> Clients { get; } = [];

    [ObservableProperty]
    private string _status = "Scanning for EVE client windows...";

    [ObservableProperty]
    private bool _needsScreenRecordingPermission;

    [ObservableProperty]
    private string _screenRecordingHelp = "Allow EVE-O Preview in Screen & System Audio Recording, then quit and reopen the app if macOS requests it.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ActivateSelectedCommand))]
    private ClientWindowInfo? _selectedClient;

    [ObservableProperty]
    private bool _trackClientLocations = true;

    [ObservableProperty]
    private bool _hidePreviewOfActiveClient;

    [ObservableProperty]
    private bool _minimizeInactiveClients;

    [ObservableProperty]
    private bool _previewsAlwaysOnTop;

    [ObservableProperty]
    private bool _hidePreviewsWhenClientNotActive;

    [ObservableProperty]
    private bool _uniqueLayoutForEachClient;

    [ObservableProperty]
    private int _thumbnailOpacity = 80;

    [ObservableProperty]
    private int _thumbnailWidth = 320;

    [ObservableProperty]
    private int _thumbnailHeight = 200;

    [ObservableProperty]
    private bool _zoomOnHover;

    [ObservableProperty]
    private int _zoomFactor = 2;

    [ObservableProperty]
    private bool _showOverlay = true;

    [ObservableProperty]
    private bool _showFrames = true;

    [ObservableProperty]
    private bool _highlightActiveClient = true;

    private bool CanActivateSelected() => SelectedClient is not null;

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        await _settingsLoaded;
        var settings = BuildCurrentSettings();
        try
        {
            await _settingsStore.UpdateAsync(storedSettings =>
            {
                settings.OverlayPositions = storedSettings.OverlayPositions;
                return settings;
            });
            Status = "Settings saved.";
            _overlayWindowManager.SyncOverlays(Clients.ToList(), BuildCurrentSettings(), ActivateClientInternalAsync);
        }
        catch (Exception ex)
        {
            Status = $"Unable to save settings: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RefreshClientsAsync()
    {
        if (!await _refreshLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            await _settingsLoaded;
            NeedsScreenRecordingPermission = OperatingSystem.IsMacOS()
                && NativeWindowCapture.CheckPermission() is not null;
            var allWindows = await _windowService.GetWindowsAsync();
            var eveWindows = allWindows
                .Where(x => x.IsEveClient)
                .OrderBy(x => x.ProcessName)
                .ThenBy(x => x.WindowTitle)
                .ToList();

            var previousSelection = SelectedClient;
            UpdateClients(eveWindows);

            _overlayWindowManager.SyncOverlays(eveWindows, BuildCurrentSettings(), ActivateClientInternalAsync);

            if (Clients.Count > 0)
            {
                SelectedClient = FindSelection(previousSelection) ?? Clients[0];
                Status = $"Found {Clients.Count} EVE window(s).";
            }
            else
            {
                SelectedClient = null;
                Status = _windowService.PreviewPermissionError
                    ?? "No EVE windows found. Keep this open while EVE is running.";
            }
        }
        catch (Exception ex)
        {
            Status = $"Unable to read windows. Enable Accessibility for this app in macOS Privacy & Security. Details: {ex.Message}";
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    // Update Clients in place instead of clearing it. Clearing the collection
    // cleared the ListBox selection, so every refresh moved the selection back
    // to the first client. Unchanged clients keep their existing instance.
    private void UpdateClients(IReadOnlyList<ClientWindowInfo> windows)
    {
        for (var i = 0; i < windows.Count; i++)
        {
            var window = windows[i];
            var existingIndex = -1;
            for (var j = i; j < Clients.Count; j++)
            {
                if (IsSameClient(Clients[j], window)
                    && Clients[j].WindowTitle.Equals(window.WindowTitle, StringComparison.Ordinal))
                {
                    existingIndex = j;
                    break;
                }
            }

            if (existingIndex < 0)
            {
                Clients.Insert(i, window);
            }
            else if (existingIndex != i)
            {
                Clients.Move(existingIndex, i);
            }
        }

        while (Clients.Count > windows.Count)
        {
            Clients.RemoveAt(Clients.Count - 1);
        }
    }

    // Keep the selected client selected across refreshes. If its title changed
    // (e.g. a different character logged in), match it by process and window.
    private ClientWindowInfo? FindSelection(ClientWindowInfo? previous)
    {
        if (previous is null)
        {
            return null;
        }

        return Clients.FirstOrDefault(x => ReferenceEquals(x, previous))
            ?? Clients.FirstOrDefault(x => IsSameClient(x, previous));
    }

    private static bool IsSameClient(ClientWindowInfo a, ClientWindowInfo b) =>
        a.ProcessId == b.ProcessId && a.WindowId == b.WindowId;

    [RelayCommand(CanExecute = nameof(CanActivateSelected))]
    private async Task ActivateSelectedAsync()
    {
        if (SelectedClient is null)
        {
            return;
        }

        await ActivateClientInternalAsync(SelectedClient);
    }

    private async Task ActivateClientInternalAsync(ClientWindowInfo client)
    {
        try
        {
            await _windowService.ActivateWindowAsync(client);
            Status = $"Activated {client.WindowTitle}.";
        }
        catch (Exception ex)
        {
            Status = $"Failed to activate client: {ex.Message}";
        }
    }

    private async Task LoadSettingsAsync()
    {
        try
        {
            var settings = await _settingsStore.LoadAsync();
            TrackClientLocations = settings.TrackClientLocations;
            HidePreviewOfActiveClient = settings.HidePreviewOfActiveClient;
            MinimizeInactiveClients = settings.MinimizeInactiveClients;
            PreviewsAlwaysOnTop = settings.PreviewsAlwaysOnTop;
            HidePreviewsWhenClientNotActive = settings.HidePreviewsWhenClientNotActive;
            UniqueLayoutForEachClient = settings.UniqueLayoutForEachClient;
            ThumbnailOpacity = settings.ThumbnailOpacity;
            ThumbnailWidth = settings.ThumbnailWidth;
            ThumbnailHeight = settings.ThumbnailHeight;
            ZoomOnHover = settings.ZoomOnHover;
            ZoomFactor = settings.ZoomFactor;
            ShowOverlay = settings.ShowOverlay;
            ShowFrames = settings.ShowFrames;
            HighlightActiveClient = settings.HighlightActiveClient;
            _overlayPositions = new Dictionary<string, OverlayPosition>(settings.OverlayPositions);
        }
        catch (Exception ex)
        {
            Status = $"Unable to load saved settings; using defaults. Details: {ex.Message}";
        }
    }

    private AppSettings BuildCurrentSettings() => new()
    {
        TrackClientLocations = TrackClientLocations,
        HidePreviewOfActiveClient = HidePreviewOfActiveClient,
        MinimizeInactiveClients = MinimizeInactiveClients,
        PreviewsAlwaysOnTop = PreviewsAlwaysOnTop,
        HidePreviewsWhenClientNotActive = HidePreviewsWhenClientNotActive,
        UniqueLayoutForEachClient = UniqueLayoutForEachClient,
        ThumbnailOpacity = ThumbnailOpacity,
        ThumbnailWidth = ThumbnailWidth,
        ThumbnailHeight = ThumbnailHeight,
        ZoomOnHover = ZoomOnHover,
        ZoomFactor = ZoomFactor,
        ShowOverlay = ShowOverlay,
        ShowFrames = ShowFrames,
        HighlightActiveClient = HighlightActiveClient,
        OverlayPositions = _overlayPositions,
    };
}
