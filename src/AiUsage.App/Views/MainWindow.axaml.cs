using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AiUsage.App.ViewModels;

namespace AiUsage.App.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    // Borderless tray popup has no OS chrome — hide back to the tray instead of closing
    // (the tray icon reopens it). Closing for real happens via the tray "Exit" item.
    private void Hide_Click(object? sender, RoutedEventArgs e) => Hide();

    // Ultra-compact has no ✕ button: a left click anywhere on the dashboard hides the
    // window. Settings (opened from the tray) must stay interactive, hence the
    // ShowSettings guard. Only the primary button triggers this — right/middle clicks
    // pass through so context menus and other pointer interactions still work.
    private void Root_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel { IsUltraCompact: true, ShowSettings: false }
            && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            Hide();
    }
}
