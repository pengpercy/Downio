using Avalonia;
using Avalonia.Controls;

namespace Downio.Helpers;

/// <summary>
/// Puts a task's status on a control as a style class (<c>status-downloading</c>,
/// <c>status-completed</c>, …) so the status colors come from theme resources and follow
/// light/dark switches, which a converter returning fixed brushes cannot do.
/// </summary>
public static class TaskStatusClasses
{
    public static readonly AttachedProperty<string?> StatusProperty =
        AvaloniaProperty.RegisterAttached<StyledElement, string?>("Status", typeof(TaskStatusClasses));

    private static readonly string[] AllClasses =
    [
        "status-downloading", "status-waiting", "status-paused",
        "status-stopped", "status-completed", "status-error",
    ];

    static TaskStatusClasses()
    {
        StatusProperty.Changed.AddClassHandler<StyledElement>((element, e) =>
        {
            element.Classes.RemoveAll(AllClasses);
            if (ToClass(e.NewValue as string) is { } cls)
            {
                element.Classes.Add(cls);
            }
        });
    }

    public static string? GetStatus(StyledElement element) => element.GetValue(StatusProperty);
    public static void SetStatus(StyledElement element, string? value) => element.SetValue(StatusProperty, value);

    private static string? ToClass(string? status) => status switch
    {
        "StatusDownloading" => "status-downloading",
        "StatusWaiting" => "status-waiting",
        "StatusPaused" => "status-paused",
        "StatusStopped" => "status-stopped",
        "StatusCompleted" => "status-completed",
        "StatusError" => "status-error",
        _ => null,
    };
}
