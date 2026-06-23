using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using AiUsage.App.ViewModels;

namespace AiUsage.App.Views;

public partial class SettingsView : UserControl
{
    // In-process format — carries the dragged row object within the app.
    private static readonly DataFormat<TileSettingRow> RowFormat =
        DataFormat.CreateInProcessFormat<TileSettingRow>("ai-usage-tile-row");

    public SettingsView()
    {
        InitializeComponent();
        RowsHost.AddHandler(DragDrop.DragOverEvent, Rows_DragOver);
        RowsHost.AddHandler(DragDrop.DropEvent, Rows_Drop);
        DragDrop.SetAllowDrop(RowsHost, true);
    }

    private SettingsViewModel? Settings => (DataContext as MainWindowViewModel)?.Settings;

    private void Swatch_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: string hex } && Settings is { } s)
            s.SelectedAccent = hex;
    }

    // --- Drag & drop reorder of tile rows ---

    private async void DragHandle_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: TileSettingRow row }) return;
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(RowFormat, row));
        await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
    }

    private void Rows_DragOver(object? sender, DragEventArgs e)
        => e.DragEffects = e.DataTransfer.Contains(RowFormat) ? DragDropEffects.Move : DragDropEffects.None;

    private void Rows_Drop(object? sender, DragEventArgs e)
    {
        if (Settings is not { } s) return;
        if (e.DataTransfer.TryGetValue(RowFormat) is not { } dragged) return;

        var target = (e.Source as Visual)?
            .GetSelfAndVisualAncestors()
            .OfType<Control>()
            .FirstOrDefault(c => c.DataContext is TileSettingRow)?
            .DataContext as TileSettingRow;

        if (target is null || ReferenceEquals(target, dragged)) return;

        int from = s.TileRows.IndexOf(dragged);
        int to   = s.TileRows.IndexOf(target);
        if (from < 0 || to < 0) return;
        s.TileRows.Move(from, to);
    }
}
