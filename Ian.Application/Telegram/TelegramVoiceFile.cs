using System.Security.Cryptography;

namespace Ian.Application.Telegram;

// Takes ownership of the buffer. Consumers must not retain it after disposal.
public sealed class TelegramVoiceFile(byte[] content) : IDisposable
{
    private bool _disposed;

    public ReadOnlyMemory<byte> Content
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return content;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(content);
        _disposed = true;
    }
}
