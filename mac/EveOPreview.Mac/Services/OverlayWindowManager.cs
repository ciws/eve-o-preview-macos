using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using EveOPreview.Mac.Models;
using EveOPreview.Mac.Views;

namespace EveOPreview.Mac.Services;

public sealed class OverlayWindowManager : IOverlayWindowManager
{
    // Each tick spawns one /usr/sbin/screencapture process per visible overlay, so the
    // base interval keeps things snappy for a small number of clients, while the
    // per-window budget caps the aggregate spawn rate as more clients are added instead
    // of letting it grow unbounded with the client count.
    private const int BaseIntervalMilliseconds = 450;
    private const int PerWindowMillisecondBudget = 100;

    private readonly IMacWindowService _windowService;
    private readonly IAppSettingsStore _settingsStore;
    private readonly Dictionary<string, OverlayThumbnailWindow> _windows = new();
    private readonly DispatcherTimer _previewTimer;
    private AppSettings _currentSettings = new();
    private bool _isPreviewRefreshRunning;

    public OverlayWindowManager(IMacWindowService windowService, IAppSettingsStore settingsStore)
    {
        _windowService = windowService;
        _settingsStore = settingsStore;

        _previewTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(BaseIntervalMilliseconds),
        };
        _previewTimer.Tick += async (_, _) => await RefreshPreviewsAsync();
        _previewTimer.Start();
    }

    public void SyncOverlays(
        IReadOnlyList<ClientWindowInfo> clients,
        AppSettings settings,
        Func<ClientWindowInfo, Task> activateClientAsync)
    {
        _currentSettings = settings;

        var keysToKeep = new HashSet<string>();
        var x = 30;
        var y = 120;

        foreach (var client in clients)
        {
            var key = BuildKey(client);
            keysToKeep.Add(key);

            if (!_windows.TryGetValue(key, out var overlay))
            {
                overlay = new OverlayThumbnailWindow();
                _windows[key] = overlay;
                overlay.Show();
                overlay.PositionChangedByUser += OnOverlayPositionChanged;
                overlay.Position = GetInitialPosition(client, key, x, y, settings);
            }

            overlay.Width = settings.ThumbnailWidth;
            overlay.Height = settings.ThumbnailHeight;
            overlay.Opacity = Math.Clamp(settings.ThumbnailOpacity / 100.0, 0.2, 1.0);
            overlay.Topmost = settings.PreviewsAlwaysOnTop;
            overlay.SetOverlayHeaderVisible(settings.ShowOverlay);
            overlay.BindClient(client, activateClientAsync);

            y += settings.ThumbnailHeight + 10;
        }

        var toRemove = _windows.Keys.Where(k => !keysToKeep.Contains(k)).ToList();
        foreach (var key in toRemove)
        {
            _windows[key].PositionChangedByUser -= OnOverlayPositionChanged;
            _windows[key].Close();
            _windows.Remove(key);
        }
    }

    public void Dispose()
    {
        _previewTimer.Stop();
        CloseAll();
    }

    private async Task RefreshPreviewsAsync()
    {
        if (_isPreviewRefreshRunning || _windows.Count == 0)
        {
            return;
        }

        _isPreviewRefreshRunning = true;
        try
        {
            UpdatePreviewInterval();

            foreach (var overlay in _windows.Values)
            {
                var client = overlay.Client;
                if (client is null || client.WindowId <= 0)
                {
                    continue;
                }

                var frame = await _windowService.CaptureWindowPreviewPngAsync(client.WindowId);
                overlay.UpdateFrame(frame);
            }
        }
        finally
        {
            _isPreviewRefreshRunning = false;
        }
    }

    private void UpdatePreviewInterval()
    {
        var interval = TimeSpan.FromMilliseconds(Math.Max(BaseIntervalMilliseconds, _windows.Count * PerWindowMillisecondBudget));
        if (_previewTimer.Interval != interval)
        {
            _previewTimer.Interval = interval;
        }
    }

    private void CloseAll()
    {
        foreach (var window in _windows.Values)
        {
            window.PositionChangedByUser -= OnOverlayPositionChanged;
            window.Close();
        }

        _windows.Clear();
    }

    private PixelPoint GetInitialPosition(ClientWindowInfo client, string key, int defaultX, int defaultY, AppSettings settings)
    {
        if (settings.TrackClientLocations
            && settings.OverlayPositions.TryGetValue(key, out var savedPosition))
        {
            return new PixelPoint(savedPosition.X, savedPosition.Y);
        }

        return new PixelPoint(defaultX, defaultY);
    }

    private void OnOverlayPositionChanged(ClientWindowInfo client, PixelPoint position)
    {
        if (!_currentSettings.TrackClientLocations)
        {
            return;
        }

        _currentSettings.OverlayPositions[BuildKey(client)] = new OverlayPosition
        {
            X = position.X,
            Y = position.Y,
        };

        _ = PersistOverlayPositionsAsync();
    }

    private async Task PersistOverlayPositionsAsync()
    {
        try
        {
            var storedSettings = await _settingsStore.LoadAsync();
            storedSettings.OverlayPositions = new Dictionary<string, OverlayPosition>(_currentSettings.OverlayPositions);
            storedSettings.TrackClientLocations = _currentSettings.TrackClientLocations;
            await _settingsStore.SaveAsync(storedSettings);
        }
        catch
        {
            // Ignore persistence failures; overlays should remain usable.
        }
    }

    private static string BuildKey(ClientWindowInfo client)
    {
        if (!string.IsNullOrWhiteSpace(client.WindowTitle))
        {
            return client.WindowTitle;
        }

        return $"{client.ProcessName}:{client.WindowId}";
    }
}
