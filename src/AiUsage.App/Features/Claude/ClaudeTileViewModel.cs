using CommunityToolkit.Mvvm.ComponentModel;
using AiUsage.App.ViewModels;

namespace AiUsage.App.Features.Claude;

public partial class ClaudeTileViewModel : TileViewModelBase
{
    // Session / primary limit
    [ObservableProperty] private double _utilization;
    [ObservableProperty] private bool _hasLimit;
    [ObservableProperty] private string _utilizationLabel = "";  // "60%"
    [ObservableProperty] private string _resetsAt = "";

    // Weekly limit
    [ObservableProperty] private double _weeklyUtilization;
    [ObservableProperty] private bool _hasWeeklyLimit;
    [ObservableProperty] private string _weeklyLabel = "";       // "32%"
    [ObservableProperty] private string _weeklyResetsAt = "";
}
