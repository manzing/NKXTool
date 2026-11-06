namespace NKXTool.Core.Cryptography;

/// <summary>
/// A read-only stream wrapper that transparently decrypts encrypted Kontakt container payload data
/// on the fly using a 64 KB repeating keystream.
/// </summary>
public sealed class NkxDecryptReadStream : Stream
{
    private readonly Stream _baseStream;
    private readonly byte[] _keystream;
    private readonly bool _leaveOpen;
    private readonly long _phase;
    private long _position;

    /// <param name="baseStream">Stream positioned on the first payload byte.</param>
    /// <param name="keystream">Repeating XOR keystream.</param>
    /// <param name="leaveOpen">Keep the base stream open when this stream is disposed.</param>
    /// <param name="phase">Absolute file offset of the payload, for legacy static keys whose
    /// XOR phase depends on where the payload sits in the container.</param>
    public NkxDecryptReadStream(Stream baseStream, byte[] keystream, bool leaveOpen = false, long phase = 0)
    {
        _baseStream = baseStream ?? throw new ArgumentNullException(nameof(baseStream));
        _keystream = keystream ?? throw new ArgumentNullException(nameof(keystream));
        _leaveOpen = leaveOpen;
        _phase = phase;
        _position = 0;
    }

    public override bool CanRead => _baseStream.CanRead;
    public override bool CanSeek => _baseStream.CanSeek;
    public override bool CanWrite => false;
    public override long Length => _baseStream.Length;

    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        return Read(buffer.AsSpan(offset, count));
    }

    public override int Read(Span<byte> buffer)
    {
        long currentOffset = _position;
        int bytesRead = _baseStream.Read(buffer);
        if (bytesRead > 0)
        {
            NkxKeystream.ApplyKeystream(_keystream, _phase + currentOffset, buffer[..bytesRead]);
            _position += bytesRead;
        }
        return bytesRead;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        long currentOffset = _position;
        int bytesRead = await _baseStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        if (bytesRead > 0)
        {
            NkxKeystream.ApplyKeystream(_keystream, _phase + currentOffset, buffer.Span[..bytesRead]);
            _position += bytesRead;
        }
        return bytesRead;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        long target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };

        _baseStream.Seek(offset, origin);
        _position = target;
        return _position;
    }

    public override void Flush() => _baseStream.Flush();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_leaveOpen)
        {
            _baseStream.Dispose();
        }
        base.Dispose(disposing);
    }
}
