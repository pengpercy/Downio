using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace Downio.Services;

public sealed class SingleInstanceService : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cts = new();
    private Action? _onActivate;
    private Task? _listenerTask;

    private SingleInstanceService(Mutex mutex, string pipeName)
    {
        _mutex = mutex;
        _pipeName = pipeName;
    }

    public static bool TryCreate(string appId, out SingleInstanceService? instance)
    {
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

        instance = new SingleInstanceService(mutex!, appId);
        return true;
    }

    public static void NotifyExisting(string appId)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);

        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", appId, PipeDirection.Out);
                client.Connect(150);
                using var writer = new StreamWriter(client) { AutoFlush = true };
                writer.WriteLine("activate");
                return;
            }
            catch
            {
                Thread.Sleep(100);
            }
        }
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
            catch
            {
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();

        try
        {
            _mutex.ReleaseMutex();
        }
        catch
        {
        }

        _mutex.Dispose();
    }
}

