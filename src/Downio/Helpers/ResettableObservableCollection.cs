using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Downio.Helpers;

/// <summary>
/// An <see cref="ObservableCollection{T}"/> that can swap its whole contents with a single Reset
/// notification, so a re-sorted list of thousands of rows costs one layout pass instead of one per move.
/// </summary>
public sealed class ResettableObservableCollection<T> : ObservableCollection<T>
{
    public void ResetTo(IEnumerable<T> items)
    {
        CheckReentrancy();
        Items.Clear();
        foreach (var item in items) Items.Add(item);

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
