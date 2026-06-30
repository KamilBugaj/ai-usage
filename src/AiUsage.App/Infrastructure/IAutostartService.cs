namespace AiUsage.App.Infrastructure;

/// <summary>
/// Manages whether the app launches automatically at user login. On Windows this is
/// backed by the per-user registry Run key; elsewhere it is a no-op (Windows-first app).
/// The app already starts hidden in the tray, so autostart needs no extra launch flag.
/// </summary>
public interface IAutostartService
{
    /// <summary>Whether this platform supports managing autostart from within the app.</summary>
    bool IsSupported { get; }

    /// <summary>True when the login autostart entry is currently registered.</summary>
    bool IsEnabled { get; }

    /// <summary>Registers (true) or removes (false) the login autostart entry.</summary>
    void SetEnabled(bool enabled);
}

/// <summary>No-op fallback for non-Windows platforms and the XAML designer.</summary>
public sealed class NoopAutostartService : IAutostartService
{
    public bool IsSupported => false;
    public bool IsEnabled => false;
    public void SetEnabled(bool enabled) { }
}
