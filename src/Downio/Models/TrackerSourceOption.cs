using CommunityToolkit.Mvvm.ComponentModel;

namespace Downio.Models;

public partial class TrackerSourceOption : ObservableObject
{
    public TrackerSourceOption(string name, string url, bool isCdn, bool isCustom)
    {
        Name = name;
        Url = url;
        IsCdn = isCdn;
        IsCustom = isCustom;
    }

    public string Name { get; }
    public string Url { get; }
    public bool IsCdn { get; }
    public bool IsCustom { get; }

    [ObservableProperty]
    private bool _isSelected;
}
