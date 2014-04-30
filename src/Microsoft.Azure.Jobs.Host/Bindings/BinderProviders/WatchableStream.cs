using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace Microsoft.Azure.Jobs
{
    internal class WatchableStream : DelegatingStream, ISelfWatch
    {
        private volatile int _countRead;
        private volatile int _countWritten;
        private volatile bool _wasExplicitlyClosed;

        private Stopwatch _timeRead = new Stopwatch();

        private readonly long _totalLength; // 0 if not known,
        private bool _completed; // flag to help make .Complete() idempotent;

        public WatchableStream(Stream inner)
            : base(inner)
        {
        }

        public WatchableStream(Stream inner, long totalLength)
            : base(inner)
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
                AppendNetworkTime(sb, _timeRead.Elapsed);
            }
            if (_countWritten > 0)
            {
                sb.AppendFormat("Wrote {0:n0} bytes.", _countWritten);
            }
            else if (InnerWasWriteable && !Inner.CanWrite)  // inner stream has been closed
            {
                if (_wasExplicitlyClosed)
                {
                    sb.Append("Wrote 0 bytes.");
                }
                else
                {
                    sb.Append("Nothing was written.");
                }
            }
            return sb.ToString();
        }

        internal static void AppendNetworkTime(StringBuilder sb, TimeSpan elapsed)
        {
            if (elapsed == TimeSpan.Zero)
            {
                return;
            }

            sb.Append("(about ");

            string unitName;
            int unitCount;

            if (elapsed > TimeSpan.FromMinutes(55)) // it is about an hour, right?
            {
                unitName = "hour"; 
                unitCount = (int)Math.Round(elapsed.TotalHours);
            }
            else if (elapsed > TimeSpan.FromSeconds(55)) // it is about a minute, right?
            {
                unitName = "minute";
                unitCount = (int) Math.Round(elapsed.TotalMinutes);
            }
            else if (elapsed > TimeSpan.FromMilliseconds(950)) // it is about a second, right?
            {
                unitName = "second";
                unitCount = (int)Math.Round(elapsed.TotalSeconds);
            }
            else
            {
                unitName = "millisecond";
                unitCount = Math.Max((int) Math.Round(elapsed.TotalMilliseconds), 1);
            }
            sb.AppendFormat(CultureInfo.CurrentCulture, "{0} {1}{2}", unitCount, unitName, unitCount > 1 ? "s" : String.Empty);
            sb.Append(" spent on I/O)");
        }

        /// <summary>
        /// Ensure the stream is closed, and calculate whether it was written to or not.
        /// </summary>
        /// <returns>True if the stream was written to, or was already closed at the time
        /// this method was called. False otherwise.</returns>
        public bool Complete()
        {
            if (!_completed)
            {
                _completed = true;
                _wasExplicitlyClosed = InnerWasWriteable && !Inner.CanWrite;
                Close();
            }
            return _wasExplicitlyClosed || _countWritten > 0;
        }
    }
}
