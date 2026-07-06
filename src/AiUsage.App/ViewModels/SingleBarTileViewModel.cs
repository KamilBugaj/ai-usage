using CommunityToolkit.Mvvm.ComponentModel;

namespace AiUsage.App.ViewModels;

/// <summary>
/// A tile that shows a single usage bar (one window). Concrete providers subclass this only
/// so the dashboard's DataTemplates can pick the right view (logo) — the bound state is
/// identical. Used by ChatGPT and Copilot.
/// </summary>
public partial class SingleBarTileViewModel : TileViewModelBase
{
    [ObservableProperty] private double _utilization;
    [ObservableProperty] private bool _hasLimit;
    [ObservableProperty] private string _utilizationLabel = "";  // "5%" — ultra-compact bar label
    [ObservableProperty] private string _resetsAt = "";
}
