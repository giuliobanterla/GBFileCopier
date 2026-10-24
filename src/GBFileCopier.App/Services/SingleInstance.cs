using System.IO.Pipes;
using System.Text;

namespace GBFileCopier.App.Services;

/// <summary>
/// Keeps one running app per user. Explorer starts the app once per selected item; every start
/// after the first one sends its arguments to the running app through a named pipe and exits, so
/// all the items end up in the same window. The pipe accepts only the current user.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _name;
    private readonly CancellationTokenSource _stop = new();

    private SingleInstance(Mutex mutex, string name)
    {
        _mutex = mutex;
        _name = name;
    }

    /// <summary>Per user: two users on the same machine each have their own app.</summary>
    public static string DefaultName { get; } = "GBFileCopier-" + Environment.UserName;

    /// <summary>The running instance, or null if another one already runs.</summary>
    public static SingleInstance? TryAcquire(string? name = null)
    {
        name ??= DefaultName;
        var mutex = new Mutex(initiallyOwned: true, @"Local\" + name, out var created);
        if (created)
        {
            return new SingleInstance(mutex, name);
        }

        mutex.Dispose();
        return null;
    }

    /// <summary>Sends the arguments to the running instance; false if it doesn't answer in time.</summary>
    public static bool Forward(IReadOnlyList<string> arguments, string workingDirectory, TimeSpan timeout, string? name = null)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", name ?? DefaultName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            pipe.Connect(timeout); // waits for the server, also while it is starting
            var payload = Encoding.UTF8.GetBytes(string.Join('\0', [workingDirectory, .. arguments]));
            pipe.Write(payload);
            pipe.Flush();
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Receives the arguments of the following starts, on a background thread.</summary>
    /// <param name="onRequest">Called with the arguments and the directory they were given in.</param>
    public void Listen(Action<IReadOnlyList<string>, string> onRequest)
    {
        _ = Task.Run(async () =>
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    await using var pipe = new NamedPipeServerStream(_name, PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(_stop.Token);
                    using var reader = new StreamReader(pipe, Encoding.UTF8);
                    var parts = (await reader.ReadToEndAsync(_stop.Token)).Split('\0');
                    if (parts.Length > 0)
                    {
                        onRequest(parts[1..], parts[0]);
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (IOException)
                {
                    // A client that went away: wait for the next one
                }
            }
        });
    }

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
        _mutex.Dispose();
    }
}
