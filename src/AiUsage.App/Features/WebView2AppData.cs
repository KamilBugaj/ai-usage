using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace AiUsage.App.Features;

/// <summary>
/// Redirects the WebView2 user-data folder to %AppData% so it stays writable
/// when the app is installed to Program Files (read-only for non-admin users).
/// </summary>
internal static class WebView2AppData
{
    internal static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AiUsage", "WebView2");

    internal static void Apply(object? _, WebViewEnvironmentRequestedEventArgs args)
    {
        if (args is WindowsWebView2EnvironmentRequestedEventArgs w)
            w.UserDataFolder = Folder;
    }

    /// <summary>
    /// Hides the dialog's underlying window while keeping the WebView2 context alive,
    /// so the session can keep issuing fetch() calls. Replaces the move-offscreen trick
    /// (Resize 1x1 + Move -4000,-4000), which left the WebView2 child HWND visible on
    /// some setups — the login window stayed on screen and, being owned by the main
    /// window, blocked it from activating (tray clicks appeared to do nothing).
    /// </summary>
    internal static void HideWindow(this NativeWebDialog dialog)
    {
        var window = dialog.TryGetWindow();
        if (window is null) return;
        window.ShowInTaskbar = false;
        // Move off-screen as well as hiding: on some setups the WebView2 child HWND can
        // briefly linger after Hide(), so parking it off-screen guarantees nothing flashes.
        window.Position = new PixelPoint(-32000, -32000);
        window.Hide();
    }
}
