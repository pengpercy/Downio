namespace Downio.Models;

public enum ToastType
{
    Info,
    Success,
    Warning,
    Error
}

public class ToastMessage
{
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public ToastType Type { get; set; } = ToastType.Info;
    public string Id { get; } = System.Guid.NewGuid().ToString();
}
