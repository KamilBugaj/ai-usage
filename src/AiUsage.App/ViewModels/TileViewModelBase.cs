using System;
using CommunityToolkit.Mvvm.ComponentModel;
using AiUsage.Application.Polling;
using AiUsage.Core.Config;

namespace AiUsage.App.ViewModels;

public abstract partial class TileViewModelBase : ObservableObject, IPollStatus
{
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _statusLine = "";
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private string _updatedAt = "";

    // Provider key (matches Source enum name) — identifies the tile across config/UI.
    [ObservableProperty] private string _provider = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLarge))]
    private TileSize _size = TileSize.Large;

    public bool IsLarge => Size == TileSize.Large;

    // Alert threshold (unit depends on provider: %, $, tokens). null = no alert.
    [ObservableProperty] private double? _alertThreshold;

    // Set true by the sink when the provider's metric crosses AlertThreshold.
    [ObservableProperty] private bool _alertActive;

    // --- Reset rings (time-windowed limits) ---
    // Sinks set the raw reset time + window length; a timer recomputes the fractions.
    public DateTimeOffset? SessionResetsAtUtc { get; set; }
    public TimeSpan SessionWindow { get; set; }
    public DateTimeOffset? WeeklyResetsAtUtc { get; set; }
    public TimeSpan WeeklyWindow { get; set; }

    // 0..1 fraction of the window that has elapsed (the ring fills as reset approaches).
    [ObservableProperty] private double _sessionResetFraction;
    [ObservableProperty] private double _weeklyResetFraction;

    public void RecomputeResetFractions()
    {
        SessionResetFraction = Elapsed(SessionResetsAtUtc, SessionWindow);
        WeeklyResetFraction  = Elapsed(WeeklyResetsAtUtc, WeeklyWindow);
    }

    private static double Elapsed(DateTimeOffset? resetsAt, TimeSpan window)
    {
        if (resetsAt is null || window <= TimeSpan.Zero) return 0;
        var remaining = resetsAt.Value - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero) return 0; // window expired — new window just started
        return Math.Clamp(1.0 - remaining / window, 0.0, 1.0);
    }
}
