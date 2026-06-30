using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using AiUsage.App.Composition;
using AiUsage.App.Infrastructure;
using AiUsage.App.Theming;
using AiUsage.App.ViewModels;
using AiUsage.App.Views;
using AiUsage.Application.Abstractions;
using AiUsage.Core.Config;

namespace AiUsage.App;

public partial class App : Avalonia.Application
{
    private readonly IUiDispatcher _dispatcher = new AvaloniaUiDispatcher();
    private AppComposer? _composer;
    private MainWindow? _window;
    private MainWindowViewModel? _vm;
    private SettingsViewModel? _settings;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += (_, _) => _composer?.Dispose();

            var cfg = ConfigLoader.Load(AppHost.DefaultConfigPath());
            ThemeService.Apply(cfg.Ui?.Theme);

            // Settings connect/disconnect commands fan out to the composer's connectors by key.
            IAutostartService autostart = OperatingSystem.IsWindows()
                ? new WindowsAutostartService()
                : new NoopAutostartService();
            _settings = new SettingsViewModel(
                ApplySettings, ThemeService.Apply, Connect, Disconnect, autostart);
            _settings.Load(cfg, ClaudeConnected(cfg));

            _vm = new MainWindowViewModel(_settings);

            _window = new MainWindow { DataContext = _vm };
            // Re-anchor to bottom-right whenever content changes window height
            // (also covers switching between the dashboard and the settings panel).
            _window.SizeChanged += (_, _) =>
            {
                if (_window.IsVisible)
                    Dispatcher.UIThread.Post(PositionNearTray, DispatcherPriority.Render);
            };

            _composer = new AppComposer(_vm, _settings, _dispatcher, AppHost.DefaultConfigPath());
            _composer.RestoreSaved(cfg);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static bool ClaudeConnected(AppConfig cfg)
        => !string.IsNullOrWhiteSpace(cfg.ClaudeWeb?.SessionKey);

    private void Connect(string key)
    {
        if (_window is not null) _composer?.Connect(_window, key);
    }

    private void Disconnect(string key) => _composer?.Disconnect(key);

    // --- Settings ---

    // Persists the edited settings, re-applies the theme and rebuilds the tiles.
    private void ApplySettings()
    {
        if (_settings is null) return;

        var baseCfg = ConfigLoader.Load(AppHost.DefaultConfigPath());
        var updated = BuildConfigFromSettings(baseCfg, _settings);

        ConfigSaver.Save(AppHost.DefaultConfigPath(), updated);
        ThemeService.Apply(updated.Ui?.Theme);
        _composer?.RebuildHost();

        // Re-sync the editor (normalised order, etc.).
        var claudeLive = _composer?.HasLiveSession("ClaudeWeb") ?? false;
        _settings.Load(updated, claudeLive || ClaudeConnected(updated));

        // Return to the dashboard so changes are visible immediately.
        if (_vm is not null) _vm.ShowSettings = false;
    }

    private static AppConfig BuildConfigFromSettings(AppConfig baseCfg, SettingsViewModel s)
    {
        return baseCfg with
        {
            Ui = new UiConfig(s.CurrentTheme, s.BuildTileConfigs())
            // ClaudeWeb / ChatGptWeb / Copilot preserved from baseCfg (written by their
            // connect flows).
        };
    }

    // --- Tray ---

    // Clicking the tray icon toggles the window: show if hidden, hide if already visible.
    private void TrayIcon_Clicked(object? sender, EventArgs e)
    {
        if (_window is not null && _window.IsVisible) _window.Hide();
        else ShowWindow();
    }

    // "Status" — show the dashboard (tiles).
    private void TrayMenu_Open(object? sender, EventArgs e)
    {
        if (_vm is not null) _vm.ShowSettings = false;
        ShowWindow();
    }

    // "Settings" — show the window with the settings panel open.
    private void TrayMenu_Settings(object? sender, EventArgs e)
    {
        if (_vm is not null) _vm.ShowSettings = true;
        ShowWindow();
    }

    private void TrayMenu_Exit(object? sender, EventArgs e)
    {
        // Shutdown() raises desktop.Exit, which disposes _composer — don't dispose twice here.
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    private void ShowWindow()
    {
        if (_window is null) return;
        if (_window.IsVisible) { _window.Activate(); return; }
        _window.Show();
        // Position after layout so ClientSize reflects actual content height.
        Dispatcher.UIThread.Post(PositionNearTray, DispatcherPriority.Render);
        _window.Activate();
    }

    private void PositionNearTray()
    {
        if (_window is null) return;
        try
        {
            var screen = _window.Screens.Primary;
            if (screen is null) return;
            var work   = screen.WorkingArea;
            var scale  = screen.Scaling;
            var w = (int)(_window.Width             * scale);
            var h = (int)(_window.ClientSize.Height * scale);
            if (h <= 0) return;
            _window.Position = new PixelPoint(work.Right - w - 16, work.Bottom - h - 16);
        }
        catch { }
    }
}
