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
    private readonly IMacWindowService _windowService;
    private readonly IAppSettingsStore _settingsStore;
    private readonly Dictionary<string, OverlayThumbnailWindow> _windows = new();
    private readonly DispatcherTimer _previewTimer;
    private AppSettings _currentSettings = new();

    public OverlayWindowManager(IMacWindowService windowService, IAppSettingsStore settingsStore)
    {
        _windowService = windowService;
        _settingsStore = settingsStore;

        _previewTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1.0 / 15),
        };
        _previewTimer.Tick += (_, _) => RefreshPreviews();
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
            overlay.ConfigurePreview(_windowService);

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

    private void RefreshPreviews()
    {
        foreach (var overlay in _windows.Values)
        {
            overlay.RefreshPreview();
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

        var key = BuildKey(client);
        var savedPosition = new OverlayPosition
        {
            X = position.X,
            Y = position.Y,
        };

        _currentSettings.OverlayPositions[key] = savedPosition;
        _ = PersistOverlayPositionAsync(key, savedPosition);
    }

    private async Task PersistOverlayPositionAsync(string key, OverlayPosition position)
    {
        try
        {
            await _settingsStore.UpdateAsync(storedSettings =>
            {
                storedSettings.OverlayPositions[key] = position;
                return storedSettings;
            });
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
