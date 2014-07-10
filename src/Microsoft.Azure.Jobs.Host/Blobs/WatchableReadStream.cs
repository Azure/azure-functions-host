using System.Diagnostics;
using System.IO;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs.Host.Blobs
{
    internal class WatchableReadStream : DelegatingStream, IWatcher
    {
        private volatile int _countRead;

        private readonly Stopwatch _timeRead = new Stopwatch();
        private readonly long _totalLength;

        public WatchableReadStream(Stream inner)
            : base(inner)
        {
            _totalLength = inner.Length;
        }

        public override int ReadByte()
        {
            try
            {
                _timeRead.Start();
                _countRead++;
                return base.ReadByte();
            }
            finally
            {
                _timeRead.Stop();
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            try
            {
                _timeRead.Start();
                var actualRead = base.Read(buffer, offset, count);
                _countRead += actualRead;
                return actualRead;
            }
            finally
            {
                _timeRead.Stop();
            }
        }

        public ParameterLog GetStatus()
        {
            return new ReadBlobParameterLog
            {
                BytesRead = _countRead,
                Length = _totalLength,
                ElapsedTime = _timeRead.Elapsed
            };
        }
    }
}
