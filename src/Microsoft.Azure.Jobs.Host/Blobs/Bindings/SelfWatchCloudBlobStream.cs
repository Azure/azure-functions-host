using System;
using System.Globalization;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Blobs.Bindings
{
    internal class SelfWatchCloudBlobStream : DelegatingCloudBlobStream, ISelfWatch
    {
        private readonly IBlobCommitedAction _committedAction;

        private volatile int _countWritten;
        private volatile bool _wasExplicitlyClosed;

        private bool _completed; // flag to help make .Complete() idempotent;

        public SelfWatchCloudBlobStream(CloudBlobStream inner, IBlobCommitedAction committedAction)
            : base(inner)
        {
            _committedAction = committedAction;
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
            if (_countWritten > 0)
            {
                return String.Format(CultureInfo.InvariantCulture, "Wrote {0:n0} bytes.", _countWritten);
            }
            else if (_completed || !Inner.CanWrite)
            {
                if (_wasExplicitlyClosed)
                {
                    return "Wrote 0 bytes.";
                }
                else
                {
                    return "Nothing was written.";
                }
            }
            else if (_completed)
            {
                return "Nothing was written.";
            }
            else
            {
                return String.Empty;
            }
        }

        public override void Commit()
        {
            base.Commit();
            _committedAction.Execute();
        }

        /// <summary>Commits the stream as appropriate (when written to or explicitly closed).</summary>
        /// <returns><see langword="true"/> when the stream was committed; otherwise, <see langword="false"/></returns>
        public bool Complete()
        {
            if (!_completed)
            {
                _wasExplicitlyClosed = !Inner.CanWrite; // inner stream has been closed

                if (_wasExplicitlyClosed || _countWritten > 0)
                {
                    Commit();
                }

                _completed = true;
            }

            return _wasExplicitlyClosed || _countWritten > 0;
        }
    }
}
