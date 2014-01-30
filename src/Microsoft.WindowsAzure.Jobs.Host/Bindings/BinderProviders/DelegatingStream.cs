using System.IO;

namespace Microsoft.WindowsAzure.Jobs
{
    internal class DelegatingStream : Stream
    {
        private readonly Stream _inner;

        // Capture this as CanWrite will turn to false upon closing the
        // of the inner stream, and that info might be required
        private readonly bool _innerWasWriteable;

        public DelegatingStream(Stream inner)
        {
            _inner = inner;
            _innerWasWriteable = inner.CanWrite;
        }

        protected Stream Inner { get { return _inner; }}

        protected bool InnerWasWriteable { get { return _innerWasWriteable; } }

        public override bool CanRead
        {
            get { return _inner.CanRead; }
        }

        public override bool CanSeek
        {
            get { return _inner.CanSeek; }
        }

        public override bool CanWrite
        {
            get { return _inner.CanWrite; }
        }

        public override void Flush()
        {
            _inner.Flush();
        }

        public override void Close()
        {
            _inner.Close();
        }

        public override long Length
        {
            get { return _inner.Length; }
        }

        public override long Position
        {
            get
            {
                return _inner.Position;
            }
            set
            {
                _inner.Position = value;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _inner.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return _inner.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            _inner.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _inner.Write(buffer, offset, count);
        }

        public override void WriteByte(byte value)
        {
            _inner.WriteByte(value);
        }

        public override int GetHashCode()
        {
            return _inner.GetHashCode();
        }
    }
}
