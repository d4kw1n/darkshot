using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace DarkShot.Core;

public static class SingleInstance
{
    public static string UserKey
    {
        get
        {
            var name = Environment.UserName ?? "user";
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            var key = sb.ToString();
            return string.IsNullOrWhiteSpace(key) ? "user" : key;
        }
    }

    public static string MutexName => $"Local\\DarkShot.SingleInstance.{UserKey}";
    public static string PipeName => $"DarkShot.Pipe.{UserKey}.{Process.GetCurrentProcess().SessionId}";

    public static bool SendToRunningInstance(string[] args, int timeoutMs = 2500)
    {
        string payload = JsonSerializer.Serialize(new IpcMessage { Args = args });
        var deadline = Environment.TickCount64 + timeoutMs;

        while (Environment.TickCount64 < deadline)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.None);
                client.Connect(350);
                using var writer = new StreamWriter(client, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
                using var reader = new StreamReader(client, Encoding.UTF8, false, 1024, leaveOpen: true);
                writer.WriteLine(payload);
                var response = reader.ReadLine();
                if (string.Equals(response, "OK", StringComparison.Ordinal)) return true;
            }
            catch (Exception ex)
            {
                Log.Warn("IPC send attempt failed: " + ex.Message);
                Thread.Sleep(120);
            }
        }

        return false;
    }

    public sealed class IpcMessage
    {
        public string[] Args { get; set; } = Array.Empty<string>();
    }
}

public sealed class SingleInstanceServer : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;
    private bool _disposed;

    public event Action<string[]>? ArgsReceived;

    public SingleInstanceServer()
    {
        _loop = Task.Run(RunAsync);
    }

    private async Task RunAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    SingleInstance.PipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(_cts.Token);
                using var reader = new StreamReader(server, Encoding.UTF8, false, 4096, leaveOpen: true);
                using var writer = new StreamWriter(server, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };

                string? line = await reader.ReadLineAsync(_cts.Token);
                if (!string.IsNullOrWhiteSpace(line))
                {
                    try
                    {
                        var message = JsonSerializer.Deserialize<SingleInstance.IpcMessage>(line);
                        if (message?.Args is { Length: > 0 })
                            ArgsReceived?.Invoke(message.Args);
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("Invalid IPC payload: " + ex.Message);
                    }
                }

                await writer.WriteLineAsync("OK");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!_cts.IsCancellationRequested)
                {
                    Log.Warn("IPC server error: " + ex.Message);
                    try { await Task.Delay(150, _cts.Token); } catch { break; }
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _cts.Cancel(); } catch { /* ignore */ }
        try { _loop.Wait(500); } catch { /* ignore */ }
        _cts.Dispose();
    }
}
