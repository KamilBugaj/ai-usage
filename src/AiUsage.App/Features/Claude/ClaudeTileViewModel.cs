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

    // Credit balances (opt-in, UiConfig.ShowClaudeCredits). The purchased balance shows
    // whenever it can be read; the included grant only while one exists.
    public bool HasAnyCredit => HasCredits || HasIncludedCredit;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnyCredit))]
    private bool _hasCredits;
    [ObservableProperty] private string _creditsLabel = "";          // "$12.34"

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnyCredit))]
    private bool _hasIncludedCredit;
    [ObservableProperty] private string _includedCreditLabel = "";   // "$100.00"
    [ObservableProperty] private string? _includedCreditExpires;     // "expires in 27d"
}
