using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AiUsage.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    public ObservableCollection<TileViewModelBase> Tiles { get; } = [];
    public SettingsViewModel Settings { get; }

    [ObservableProperty] private bool _showSettings;
    [ObservableProperty] private bool _hasAlerts;
    [ObservableProperty] private string _alertBanner = "";

    public MainWindowViewModel() : this(new SettingsViewModel()) { }

    public MainWindowViewModel(SettingsViewModel settings)
    {
        Settings = settings;
        Tiles.CollectionChanged += OnTilesChanged;

        // Reset rings depend on the current time — tick every minute (5h/7d windows
        // change slowly, so per-minute is smooth enough).
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        timer.Tick += (_, _) => RecomputeResetFractions();
        timer.Start();
    }

    private void RecomputeResetFractions()
    {
        foreach (var t in Tiles) t.RecomputeResetFractions();
    }

    private void OnTilesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (TileViewModelBase t in e.OldItems) t.PropertyChanged -= OnTileChanged;
        if (e.NewItems is not null)
            foreach (TileViewModelBase t in e.NewItems) t.PropertyChanged += OnTileChanged;
        RefreshAlerts();
    }

    private void OnTileChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TileViewModelBase.AlertActive)
                           or nameof(TileViewModelBase.Title))
            RefreshAlerts();
    }

    public void RefreshAlerts()
    {
        var hit = Tiles.Where(t => t.AlertActive).Select(t => t.Title).ToList();
        HasAlerts   = hit.Count > 0;
        AlertBanner = hit.Count == 0 ? "" : "⚠ Usage alert: " + string.Join(", ", hit);
    }

    [RelayCommand] private void OpenSettings() => ShowSettings = true;
    [RelayCommand] private void CloseSettings() => ShowSettings = false;
}
