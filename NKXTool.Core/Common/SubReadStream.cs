namespace NKXTool.Core.Common;

/// <summary>
/// A non-closing, bounded read stream wrapping a slice of an underlying stream.
/// </summary>
public sealed class SubReadStream : Stream
{
    private readonly Stream _baseStream;
    private readonly long _originOffset;
    private readonly long _length;
    private long _position;

    public SubReadStream(Stream baseStream, long originOffset, long length)
    {
        _baseStream = baseStream ?? throw new ArgumentNullException(nameof(baseStream));
        _originOffset = originOffset;
        _length = length;
        _position = 0;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_position >= _length) return 0;
        int toRead = (int)Math.Min(count, _length - _position);

        _baseStream.Seek(_originOffset + _position, SeekOrigin.Begin);
        int read = _baseStream.Read(buffer, offset, toRead);
        _position += read;
        return read;
    }

    public override int Read(Span<byte> buffer)
    {
        if (_position >= _length) return 0;
        int toRead = (int)Math.Min(buffer.Length, _length - _position);

        _baseStream.Seek(_originOffset + _position, SeekOrigin.Begin);
        int read = _baseStream.Read(buffer[..toRead]);
        _position += read;
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        long newPos = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };

        if (newPos < 0) throw new IOException("An attempt was made to move the file pointer before the beginning of the file.");
        _position = newPos;
        return _position;
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
