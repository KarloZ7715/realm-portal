using RealmPortal.Core.IO;

namespace RealmPortal.Tests.Support;

/// <summary>
/// One-shot fault for <see cref="AtomicFileWriter"/>: the <c>FailOnWrite</c>-th write (1-based, counted over every file the writer
/// writes) stops after <c>AllowedBytes</c> bytes with an <see cref="IOException"/>, i.e. in the middle of the temporary file.
/// Every other write is untouched, so recovery code (revert, retry, undo) can use the same writer.
/// </summary>
public sealed class WriteFault(int failOnWrite, int allowedBytes)
{
    private int _writes;

    public bool Fired { get; private set; }

    public AtomicFileWriter Writer() => new(Decorate);

    private Stream Decorate(string tempPath, Stream stream) =>
        ++_writes == failOnWrite ? new FaultyStream(stream, allowedBytes, () => Fired = true) : stream;

    private sealed class FaultyStream(Stream inner, int allowedBytes, Action onFault) : Stream
    {
        private int _remaining = allowedBytes;

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (count > _remaining)
            {
                inner.Write(buffer, offset, _remaining); // the half-written part really reaches the temp file
                inner.Flush();
                _remaining = 0;
                onFault();
                throw new IOException("Injected fault in the middle of a write.");
            }

            _remaining -= count;
            inner.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer) => Write(buffer.ToArray(), 0, buffer.Length);

        public override void Flush() => inner.Flush();
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
