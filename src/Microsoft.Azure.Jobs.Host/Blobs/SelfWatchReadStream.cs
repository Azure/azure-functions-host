using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace Microsoft.Azure.Jobs.Host.Blobs
{
    internal class SelfWatchReadStream : DelegatingStream, ISelfWatch
    {
        private volatile int _countRead;

        private readonly Stopwatch _timeRead = new Stopwatch();
        private readonly long _totalLength;

        public SelfWatchReadStream(Stream inner)
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

        public string GetStatus()
        {
            StringBuilder sb = new StringBuilder();
            var x = _countRead;
            double complete = x * 100.0 / _totalLength;
            sb.AppendFormat("Read {0:n0} bytes ({1:0.00}% of total). ", x, complete);
            AppendNetworkTime(sb, _timeRead.Elapsed);
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
                unitCount = (int)Math.Round(elapsed.TotalMinutes);
            }
            else if (elapsed > TimeSpan.FromMilliseconds(950)) // it is about a second, right?
            {
                unitName = "second";
                unitCount = (int)Math.Round(elapsed.TotalSeconds);
            }
            else
            {
                unitName = "millisecond";
                unitCount = Math.Max((int)Math.Round(elapsed.TotalMilliseconds), 1);
            }
            sb.AppendFormat(CultureInfo.CurrentCulture, "{0} {1}{2}", unitCount, unitName, unitCount > 1 ? "s" : String.Empty);
            sb.Append(" spent on I/O)");
        }
    }
}
