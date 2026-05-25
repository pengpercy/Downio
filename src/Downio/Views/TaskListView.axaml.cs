using Avalonia.Controls;
using Avalonia.Input;
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
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not ListBox listBox) return;

        var point = e.GetCurrentPoint(listBox);
        var props = point.Properties;
        var isLeft = props.IsLeftButtonPressed;
        var isRight = props.IsRightButtonPressed;
        if (!isLeft && !isRight) return;

        var sourceControl = e.Source as Control;
        if (FindAncestor<Button>(sourceControl) != null) return;

        var clickedContainer = FindAncestor<ListBoxItem>(sourceControl);
        if (clickedContainer == null)
        {
            if (isLeft)
            {
                ClearSelection(listBox);
                e.Handled = true;
            }
            return;
        }

        var clickedItem = clickedContainer.DataContext;
        if (clickedItem == null) return;

        var clickedIndex = listBox.Items.IndexOf(clickedItem);
        if (clickedIndex < 0) return;

        var selectedItems = listBox.SelectedItems;
        var selectedCount = selectedItems?.Count ?? 0;
        var isSelected = selectedItems?.Contains(clickedItem) == true;

        var mods = e.KeyModifiers;
        var isCtrlOrCmd = mods.HasFlag(KeyModifiers.Control) || mods.HasFlag(KeyModifiers.Meta);
        var isShift = mods.HasFlag(KeyModifiers.Shift);

        if (isRight)
        {
            if (!isSelected)
            {
                selectedItems?.Clear();
                selectedItems?.Add(clickedItem);
            }
            listBox.SelectedItem = clickedItem;
            _anchorIndex = clickedIndex;
            e.Handled = true;
            return;
        }

        if (isShift)
        {
            if (_anchorIndex < 0) _anchorIndex = clickedIndex;
            SelectRange(listBox, _anchorIndex, clickedIndex, isCtrlOrCmd);
            listBox.SelectedItem = clickedItem;
            e.Handled = true;
            return;
        }

        if (isCtrlOrCmd)
        {
            if (isSelected)
            {
                selectedItems?.Remove(clickedItem);
            }
            else
            {
                selectedItems?.Add(clickedItem);
            }
            listBox.SelectedItem = clickedItem;
            _anchorIndex = clickedIndex;
            e.Handled = true;
            return;
        }

        if (isSelected && selectedCount == 1)
        {
            ClearSelection(listBox);
            e.Handled = true;
            return;
        }

        selectedItems?.Clear();
        selectedItems?.Add(clickedItem);
        listBox.SelectedItem = clickedItem;
        _anchorIndex = clickedIndex;
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
            if (listBox.ItemCount > 0)
            {
                listBox.SelectedItem = listBox.Items[listBox.ItemCount - 1];
                _anchorIndex = 0;
            }
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
            if (DataContext is MainWindowViewModel vm && vm.DeleteSelectedTasksCommand.CanExecute(null))
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

            var listBox = this.FindControl<ListBox>("TasksListBox");
            if (listBox == null || !listBox.Items.Contains(_viewModel.SelectedTask))
            {
                return;
            }

            var selectedItems = listBox.SelectedItems;
            selectedItems?.Clear();
            selectedItems?.Add(_viewModel.SelectedTask);
            listBox.SelectedItem = _viewModel.SelectedTask;
            listBox.ScrollIntoView(_viewModel.SelectedTask);
            listBox.Focus();

            var selectedIndex = listBox.Items.IndexOf(_viewModel.SelectedTask);
            if (selectedIndex >= 0)
            {
                _anchorIndex = selectedIndex;
            }
        }, DispatcherPriority.Background);
    }

    private void ClearSelection(ListBox listBox)
    {
        listBox.SelectedItems?.Clear();
        listBox.SelectedItem = null;
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
