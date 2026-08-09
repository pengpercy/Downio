using System;
using Avalonia.Controls;

namespace Downio.Services.TaskbarBadge;

/// <summary>
/// Displays the aggregate active download speed on the application's taskbar icon.
/// </summary>
public interface ITaskbarBadgeService : IDisposable
{
    void Attach(Window window);

    void Update(long totalDownloadSpeedBytesPerSecond);

    void Clear();
}
