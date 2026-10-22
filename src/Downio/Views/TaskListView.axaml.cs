using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Downio.ViewModels;
using Downio.Models;
using System.Linq;
using System.ComponentModel;

namespace Downio.Views;

public partial class TaskListView : UserControl
{
    private int _anchorIndex = -1;
    private MainWindowViewModel? _viewModel;

    public TaskListView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        TasksListBox.AddHandler(PointerPressedEvent, OnListPointerPressed, RoutingStrategies.Tunnel);
        TasksListBox.AddHandler(PointerReleasedEvent, OnListPointerReleased, RoutingStrategies.Tunnel);
    }

    private void OpenFolderButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Control { DataContext: DownloadTask task } && DataContext is MainWindowViewModel vm)
        {
            _ = vm.OpenFolder(task);
        }
    }

    // Selection is driven entirely from here. The handlers run in the tunnel phase so the
    // ListBox's built-in Multiple-mode toggling (which may fire on press or on release)
    // never runs and can't undo what a click just selected.
    private void OnListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not ListBox listBox) return;

        var props = e.GetCurrentPoint(listBox).Properties;
        var isLeft = props.IsLeftButtonPressed;
        var isRight = props.IsRightButtonPressed;
        if (!isLeft && !isRight) return;

        var sourceControl = e.Source as Control;
        if (FindAncestor<Button>(sourceControl) != null || FindAncestor<ScrollBar>(sourceControl) != null) return;

        listBox.Focus();

        var clickedItem = FindAncestor<ListBoxItem>(sourceControl)?.DataContext;
        var clickedIndex = clickedItem == null ? -1 : listBox.Items.IndexOf(clickedItem);
        if (clickedItem == null || clickedIndex < 0)
        {
            // Clicking empty space clears the selection.
            if (isLeft)
            {
                ClearSelection(listBox);
                e.Handled = true;
            }
            return;
        }

        var selectedItems = listBox.SelectedItems;
        if (selectedItems == null) return;
        var isSelected = selectedItems.Contains(clickedItem);

        var mods = e.KeyModifiers;
        var isCtrlOrCmd = mods.HasFlag(KeyModifiers.Control) || mods.HasFlag(KeyModifiers.Meta);
        var isShift = mods.HasFlag(KeyModifiers.Shift);

        if (isRight)
        {
            // Right-clicking inside the selection keeps it so the context menu acts on it.
            if (!isSelected) SelectOnly(listBox, clickedItem, clickedIndex);
            e.Handled = true;
            return;
        }

        if (isShift)
        {
            if (_anchorIndex < 0) _anchorIndex = clickedIndex;
            SelectRange(listBox, _anchorIndex, clickedIndex, isCtrlOrCmd);
        }
        else if (isCtrlOrCmd)
        {
            if (isSelected) selectedItems.Remove(clickedItem);
            else selectedItems.Add(clickedItem);
            _anchorIndex = clickedIndex;
        }
        else
        {
            SelectOnly(listBox, clickedItem, clickedIndex);
        }

        e.Handled = true;
    }

    private void OnListPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // Swallow the left release too, otherwise the ListBoxItem toggles the selection back.
        // Right release is left alone: it is what opens the context menu.
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        var sourceControl = e.Source as Control;
        if (FindAncestor<Button>(sourceControl) != null || FindAncestor<ScrollBar>(sourceControl) != null) return;
        e.Handled = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not ListBox listBox) return;

        var mods = e.KeyModifiers;
        var isCtrlOrCmd = mods.HasFlag(KeyModifiers.Control) || mods.HasFlag(KeyModifiers.Meta);

        if (isCtrlOrCmd && e.Key == Key.A)
        {
            var selectedItems = listBox.SelectedItems;
            selectedItems?.Clear();
            for (var i = 0; i < listBox.ItemCount; i++)
            {
                selectedItems?.Add(listBox.Items[i]);
            }
            _anchorIndex = listBox.ItemCount > 0 ? 0 : -1;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            ClearSelection(listBox);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete || e.Key == Key.Back)
        {
            // Keyboard delete only acts on a selection; clearing the whole list stays a toolbar action.
            if (DataContext is MainWindowViewModel { HasSelectedTasks: true } vm)
            {
                vm.DeleteSelectedTasksCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && sender is ListBox listBox)
        {
            var selected = listBox.SelectedItems?.Cast<DownloadTask>().ToList() ?? [];
            vm.UpdateSelectedTasks(selected);
        }
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.SelectedTask))
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_viewModel?.SelectedTask == null)
            {
                return;
            }

            var listBox = TasksListBox;
            var task = _viewModel.SelectedTask;
            var index = listBox.Items.IndexOf(task);
            if (index < 0)
            {
                return;
            }

            SelectOnly(listBox, task, index);
            listBox.ScrollIntoView(task);
            listBox.Focus();
        }, DispatcherPriority.Background);
    }

    private void SelectOnly(ListBox listBox, object item, int index)
    {
        var selectedItems = listBox.SelectedItems;
        if (selectedItems == null) return;
        if (selectedItems.Count != 1 || !selectedItems.Contains(item))
        {
            selectedItems.Clear();
            selectedItems.Add(item);
        }
        _anchorIndex = index;
    }

    private void ClearSelection(ListBox listBox)
    {
        listBox.SelectedItems?.Clear();
        _anchorIndex = -1;
    }

    private void SelectRange(ListBox listBox, int from, int to, bool additive)
    {
        var selectedItems = listBox.SelectedItems;
        if (selectedItems == null) return;

        var start = from <= to ? from : to;
        var end = from <= to ? to : from;

        if (!additive) selectedItems.Clear();

        for (var i = start; i <= end; i++)
        {
            var item = listBox.Items[i];
            if (!selectedItems.Contains(item))
            {
                selectedItems.Add(item);
            }
        }
    }

    private static T? FindAncestor<T>(Control? control) where T : class
    {
        while (control != null)
        {
            if (control is T t) return t;
            control = control.GetVisualParent() as Control;
        }
        return null;
    }
}
