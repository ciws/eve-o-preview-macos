using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using EveOPreview.Mac.Services;
using EveOPreview.Mac.ViewModels;
using EveOPreview.Mac.Views;

namespace EveOPreview.Mac;

public partial class App : Application
{
    private static readonly Uri ProjectUri = new("https://github.com/ciws/eve-o-preview-macos");

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();
            var windowService = new MacWindowService();
            var settingsStore = new JsonAppSettingsStore();
            var overlayWindowManager = new OverlayWindowManager(windowService, settingsStore);
            desktop.Exit += (_, _) => overlayWindowManager.Dispose();
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(windowService, settingsStore, overlayWindowManager),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnAboutClicked(object? sender, EventArgs e) => AboutPanel.TryShow();

    private async void OnGitHubClicked(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } mainWindow })
        {
            return;
        }

        try
        {
            await mainWindow.Launcher.LaunchUriAsync(ProjectUri);
        }
        catch
        {
            // Ignore failures to open the browser.
        }
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}
