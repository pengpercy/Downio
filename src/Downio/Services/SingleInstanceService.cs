using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace Downio.Services;

public sealed class SingleInstanceService : IDisposable
{
    private readonly Mutex? _mutex;
    private readonly FileStream? _lockFile;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cts = new();
    private Action? _onActivate;
    private Task? _listenerTask;

    private SingleInstanceService(Mutex? mutex, FileStream? lockFile, string pipeName)
    {
        _mutex = mutex;
        _lockFile = lockFile;
        _pipeName = pipeName;
    }

    // Unix pipes are sockets in the shared /tmp, so keep each user's apart.
    private static string GetPipeName(string appId) =>
        OperatingSystem.IsWindows() ? appId : $"{appId}-{Environment.UserName}";

    public static bool TryCreate(string appId, out SingleInstanceService? instance)
    {
        if (!OperatingSystem.IsWindows())
            return TryCreateWithLockFile(appId, out instance);

        var mutexName = $"Local\\{appId}";
        var createdNew = false;
        Mutex? mutex = null;

        try
        {
            mutex = new Mutex(true, mutexName, out createdNew);
        }
        catch
        {
            createdNew = true;
        }

        if (!createdNew)
        {
            mutex?.Dispose();
            instance = null;
            return false;
        }

        instance = new SingleInstanceService(mutex!, null, GetPipeName(appId));
        return true;
    }

    // The named mutex didn't hold across launches on Linux: every launch from the XFCE menu acquired it.
    // An exclusive FileStream takes a flock instead, which the kernel drops if the process dies.
    private static bool TryCreateWithLockFile(string appId, out SingleInstanceService? instance)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), appId);
        FileStream? lockFile;
        try
        {
            Directory.CreateDirectory(dir);
            lockFile = new FileStream(Path.Combine(dir, "instance.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            instance = null;
            return false;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Single instance lock file unavailable: {ex.Message}");
            lockFile = null;
        }

        instance = new SingleInstanceService(null, lockFile, GetPipeName(appId));
        return true;
    }

    public static bool NotifyExisting(string appId)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);

        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", GetPipeName(appId), PipeDirection.Out);
                client.Connect(150);
                using var writer = new StreamWriter(client) { AutoFlush = true };
                writer.WriteLine("activate");
                AppLog.Info("Existing Downio instance activation requested.");
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Warn($"Failed to notify existing instance: {ex.Message}");
                Thread.Sleep(100);
            }
        }

        return false;
    }

    public void SetActivateHandler(Action onActivate)
    {
        _onActivate = onActivate;

        _listenerTask ??= Task.Run(ListenAsync);
    }

    private async Task ListenAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(_cts.Token).ConfigureAwait(false);

                using var reader = new StreamReader(server);
                var line = await reader.ReadLineAsync().ConfigureAwait(false);

                if (string.Equals(line, "activate", StringComparison.OrdinalIgnoreCase))
                {
                    var handler = _onActivate;
                    if (handler != null)
                    {
                        Dispatcher.UIThread.Post(handler);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                AppLog.Warn($"Single instance listener failed: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();

        try
        {
            _mutex?.ReleaseMutex();
        }
        catch
        {
        }

        _mutex?.Dispose();
        _lockFile?.Dispose();
    }
}
