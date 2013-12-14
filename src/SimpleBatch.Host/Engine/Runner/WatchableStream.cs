using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using Newtonsoft.Json;



namespace Microsoft.WindowsAzure.Jobs
{
    internal class WatchableStream : DelegatingStream, ISelfWatch
    {
        private volatile int _countRead;
        private volatile int _countWritten;

        private Stopwatch _timeRead = new Stopwatch();

        private readonly long _totalLength; // 0 if not known,

        public WatchableStream(Stream inner) : base(inner)
        {            
        }

        public WatchableStream(Stream inner, long totalLength) : base(inner)
        {
            _totalLength = totalLength;
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

        public override void Write(byte[] buffer, int offset, int count)
        {
            _countWritten += count;
            base.Write(buffer, offset, count);
        }
        public override void WriteByte(byte value)
        {
            _countWritten++;
            base.WriteByte(value);
        }

        public string GetStatus()
        {
            StringBuilder sb = new StringBuilder();
            if (_countRead > 0)
            {
                var x = _countRead;
                if (_totalLength > 0)
                {                    
                    double complete = x * 100.0 / _totalLength;
                    sb.AppendFormat("Read {0:n0} bytes ({1:0.00}% of total). ", x, complete);
                }
                else
                {
                    sb.AppendFormat("Read {0:n0} bytes. ", x);
                }

                sb.AppendFormat("({0} time)", _timeRead.Elapsed);
            }
            if (_countWritten > 0)
            {
                sb.AppendFormat("Written {0:n0} bytes", _countWritten);
            }
            return sb.ToString();
        }
    }

    internal class DelegatingStream : Stream
    {        
        protected readonly Stream _inner;
             
        public DelegatingStream(Stream inner)
        {
            _inner = inner;
        }
      
        public override bool CanRead
        {
            get { return _inner.CanRead;  }
        }

        public override bool CanSeek
        {
            get { return _inner.CanSeek;  }
        }

        public override bool CanWrite
        {
            get { return _inner.CanWrite;  }
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