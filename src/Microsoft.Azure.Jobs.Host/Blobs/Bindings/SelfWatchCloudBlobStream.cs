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
        private bool _committed;
        private bool _disposed;

        public SelfWatchCloudBlobStream(CloudBlobStream inner, IBlobCommitedAction committedAction)
            : base(inner)
        {
            _committedAction = committedAction;
        }

        public override bool CanWrite
        {
            get
            {
                return !_disposed;
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
            if (_countWritten > 0)
            {
                return String.Format(CultureInfo.InvariantCulture, "Wrote {0:n0} bytes.", _countWritten);
            }
            else if (!CanWrite)
            {
                return "Wrote 0 bytes.";
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

            if (_committedAction != null)
            {
                _committedAction.Execute();
            }

            _committed = true;
        }

        public override void Close()
        {
            Dispose(true);
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (!_committed)
                {
                    Commit();
                }

                _disposed = true;
            }
            base.Dispose(disposing);
        }

        /// <summary>Commits the stream as appropriate (when written to or explicitly closed).</summary>
        /// <returns><see langword="true"/> when the stream was committed; otherwise, <see langword="false"/></returns>
        public bool Complete()
        {
            if (!_completed)
            {
                _wasExplicitlyClosed = !CanWrite; // inner stream has been closed

                if (!_wasExplicitlyClosed && _countWritten > 0 && !_committed)
                {
                    Commit();
                }

                _completed = true;
            }

            return _wasExplicitlyClosed || _countWritten > 0;
        }
    }
}
