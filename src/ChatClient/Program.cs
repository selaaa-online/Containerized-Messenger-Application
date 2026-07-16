using System.Threading.Channels;
using ChatClient;

// Ensure console output is flushed immediately. When stdout is redirected to a pipe
// (as in a container), the default writer is block-buffered, which would hide log
// lines until the process exits. Auto-flushing keeps `docker logs` live.
Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });

// ---- Resolve credentials (environment first, then interactive prompt) ----
var username = ClientConfig.Username;
while (string.IsNullOrWhiteSpace(username))
{
    Console.Write("Username: ");
    username = Console.ReadLine();
    if (username is null)
    {
        Console.Error.WriteLine("No username provided (stdin closed). Set CHAT_USERNAME or run with an interactive terminal.");
        return;
    }
}

var password = ClientConfig.Password;
while (string.IsNullOrWhiteSpace(password))
{
    Console.Write("Password: ");
    password = Console.ReadLine();
    if (password is null)
    {
        Console.Error.WriteLine("No password provided (stdin closed). Set CHAT_PASSWORD or run with an interactive terminal.");
        return;
    }
}

var host = ClientConfig.ServerHost;
var port = ClientConfig.ServerPort;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

// Single background reader turns console input into a stream the active session consumes.
// This MUST run on a separate thread: when stdin is an interactive TTY, the underlying
// console reader blocks synchronously (it never yields on an awaited read), so calling it
// inline would stall startup and prevent the client from ever connecting.
var outgoing = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = false });
var inputTask = Task.Run(() => ReadConsoleAsync(outgoing.Writer, cts.Token));

Console.WriteLine($"Connecting to {host}:{port} as '{username}'...");

var backoffSeconds = 1;
const int maxBackoffSeconds = 15;

while (!cts.IsCancellationRequested)
{
    var session = new ChatSession(host, port, username!, password!);
    try
    {
        var reason = await session.RunAsync(outgoing.Reader, cts.Token);

        if (reason == ChatSession.SessionEndReason.UserQuit)
        {
            Console.WriteLine("[client] Goodbye.");
            break;
        }

        if (reason == ChatSession.SessionEndReason.AuthFailed)
        {
            Console.WriteLine("[client] Authentication failed. Exiting.");
            break;
        }

        Console.WriteLine("[client] Connection lost.");
    }
    catch (OperationCanceledException)
    {
        break;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[client] Unable to reach server: {ex.Message}");
    }

    if (cts.IsCancellationRequested)
    {
        break;
    }

    Console.WriteLine($"[client] Reconnecting in {backoffSeconds}s...");
    try
    {
        await Task.Delay(TimeSpan.FromSeconds(backoffSeconds), cts.Token);
    }
    catch (OperationCanceledException)
    {
        break;
    }

    backoffSeconds = Math.Min(backoffSeconds * 2, maxBackoffSeconds);
}

cts.Cancel();
outgoing.Writer.TryComplete();
try { await inputTask; } catch { /* ignore */ }

static async Task ReadConsoleAsync(ChannelWriter<string> writer, CancellationToken ct)
{
    try
    {
        while (!ct.IsCancellationRequested)
        {
            var line = await Console.In.ReadLineAsync(ct);
            if (line is null)
            {
                // stdin is closed (e.g. a non-interactive container started with
                // `docker compose up`). Stop reading input but leave the channel
                // open so the session stays connected and keeps receiving messages,
                // rather than tearing down and reconnecting in a loop.
                return;
            }

            await writer.WriteAsync(line, ct);
        }
    }
    catch (OperationCanceledException)
    {
        // Shutting down.
    }
    finally
    {
        // Only signal completion on real shutdown; an EOF alone must not end the session.
        if (ct.IsCancellationRequested)
        {
            writer.TryComplete();
        }
    }
}
