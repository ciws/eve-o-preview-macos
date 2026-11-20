using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using EveOPreview.Mac.Services;
using EveOPreview.Mac.ViewModels;

namespace EveOPreview.Mac.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        SizeChanged += OnSizeChanged;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        UpdateResponsiveLayout();
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateResponsiveLayout();
    }

    private void UpdateResponsiveLayout()
    {
        if (this.FindControl<TextBlock>("SubtitleText") is { } subtitleText)
        {
            subtitleText.IsVisible = Bounds.Width >= 660;
        }
    }

    private async void OnScreenRecordingClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        button.IsEnabled = false;
        try
        {
            var error = NativeWindowCapture.CheckPermission(request: true);
            viewModel.NeedsScreenRecordingPermission = error is not null;
            if (error is null)
            {
                await viewModel.RefreshClientsCommand.ExecuteAsync(null);
                viewModel.ScreenRecordingHelp = "Screen Recording is allowed.";
                return;
            }

            var opened = await Launcher.LaunchUriAsync(new Uri(
                "x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture"));
            viewModel.ScreenRecordingHelp = opened
                ? "Enable EVE-O Preview in Screen & System Audio Recording. If it is missing, click + and select this app. Then quit and reopen it."
                : "Open System Settings > Privacy & Security > Screen & System Audio Recording, enable EVE-O Preview, then quit and reopen the app.";
        }
        catch (Exception ex)
        {
            viewModel.ScreenRecordingHelp = $"Unable to open permissions: {ex.Message}. Open Screen Recording in System Settings manually.";
        }
        finally
        {
            button.IsEnabled = true;
        }
    }
}
