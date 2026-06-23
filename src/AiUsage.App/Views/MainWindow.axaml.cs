using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AiUsage.App.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    // Borderless tray popup has no OS chrome — hide back to the tray instead of closing
    // (the tray icon reopens it). Closing for real happens via the tray "Exit" item.
    private void Hide_Click(object? sender, RoutedEventArgs e) => Hide();
}
