using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Downio.ViewModels;
using Downio.Views;

namespace Downio;

public partial class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        return param switch
        {
            MainWindowViewModel => new MainWindow(),
            _ => new TextBlock { Text = "Not Found: " + param.GetType().Name }
        };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
