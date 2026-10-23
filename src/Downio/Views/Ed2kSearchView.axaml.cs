using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Downio.Models;
using Downio.ViewModels;

namespace Downio.Views;

public partial class Ed2kSearchView : UserControl
{
    public Ed2kSearchView()
    {
        InitializeComponent();
    }

    // A single click on a row toggles it for batch download.
    private void OnResultTapped(object? sender, TappedEventArgs e)
    {
        if (IsFromInteractiveChild(e) || sender is not Control { DataContext: Ed2kSearchResult { IsAdded: false } result }) return;
        result.IsSelected = !result.IsSelected;
    }

    // A double click downloads the row right away.
    private void OnResultDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (IsFromInteractiveChild(e) || sender is not Control { DataContext: Ed2kSearchResult result }) return;
        // The two taps of the double click toggled the row twice; leave it unchecked.
        result.IsSelected = false;
        if (DataContext is MainWindowViewModel vm) vm.DownloadEd2kResultCommand.Execute(result);
    }

    private static bool IsFromInteractiveChild(RoutedEventArgs e)
    {
        for (var visual = e.Source as Visual; visual != null; visual = visual.GetVisualParent())
        {
            if (visual is Button or CheckBox) return true;
            if (visual is Border { Classes: var classes } && (classes.Contains("resultRow") || classes.Contains("tableRow"))) return false;
        }
        return false;
    }
}
