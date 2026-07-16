using System.Net.Sockets;
using System.Text;

namespace Shared;

/// <summary>
/// Wraps a <see cref="NetworkStream"/> and provides newline-framed, thread-safe
/// reading and writing of <see cref="Message"/> objects. Each message occupies a
/// single line of UTF-8 JSON terminated by '\n'.
/// </summary>
public sealed class MessageChannel : IDisposable
{
    private readonly NetworkStream _stream;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public MessageChannel(NetworkStream stream)
    {
        _stream = stream;
        _reader = new StreamReader(stream, Encoding.UTF8);
        _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = false };
    }

    /// <summary>
    /// Reads the next message from the stream, or returns <c>null</c> when the
    /// remote peer has closed the connection.
    /// </summary>
    public async Task<Message?> ReadAsync(CancellationToken cancellationToken = default)
    {
        var line = await _reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (line is null)
        {
            return null; // connection closed
        }

        if (string.IsNullOrWhiteSpace(line))
        {
            // Skip empty frames instead of failing to parse them.
            return await ReadAsync(cancellationToken).ConfigureAwait(false);
        }

        return Message.FromJson(line);
    }

    /// <summary>Serializes and sends a message, guarding concurrent writers.</summary>
    public async Task SendAsync(Message message, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _writer.WriteAsync(message.ToJson().AsMemory(), cancellationToken).ConfigureAwait(false);
            await _writer.WriteAsync("\n".AsMemory(), cancellationToken).ConfigureAwait(false);
            await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void Dispose()
    {
        _writeLock.Dispose();
        _reader.Dispose();
        _writer.Dispose();
        _stream.Dispose();
    }
}
