using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Downio.Assets.Lang;

public partial class EnUs : ResourceDictionary
{
    public EnUs()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
