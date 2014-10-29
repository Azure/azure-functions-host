// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests.TestDoubles
{
    internal class MemoryCloudPageBlobStream : CloudBlobStream
    {
        private readonly MemoryStream _buffer;
        private readonly Action<byte[]> _commitAction;

        private bool _committed;
        private bool _disposed;

        public MemoryCloudPageBlobStream(Action<byte[]> commitAction)
        {
            _buffer = new MemoryStream();
            _commitAction = commitAction;
        }

        public override bool CanRead
        {
            get { return false; }
        }

        public override bool CanSeek
        {
            get { return _buffer.CanSeek; }
        }

        public override bool CanWrite
        {
            get { return _buffer.CanWrite; }
        }

        public override long Length
        {
            get { return _buffer.Length; }
        }

        public override long Position
        {
            get { return _buffer.Position; }
            set { _buffer.Position = value; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (!_disposed)
                {
                    if (!_committed)
                    {
                        Commit();
                    }

                    _buffer.Dispose();
                    _disposed = true;
                }
            }

            base.Dispose(disposing);
        }

        public override void Commit()
        {
            ThrowIfDisposed();
            ThrowIfCommitted();

            _commitAction.Invoke(_buffer.ToArray());
            _committed = true;
        }

        public override ICancellableAsyncResult BeginCommit(AsyncCallback callback, object state)
        {
            Commit();

            ICancellableAsyncResult result = new CompletedCancellableAsyncResult(state);

            if (callback != null)
            {
                callback.Invoke(result);
            }

            return result;
        }

        public override void EndCommit(IAsyncResult asyncResult)
        {
            // Always completes synchronously
        }

        public override void Flush()
        {
            _buffer.Flush();
        }

        public override ICancellableAsyncResult BeginFlush(AsyncCallback callback, object state)
        {
            Flush();

            ICancellableAsyncResult result = new CompletedCancellableAsyncResult(state);

            if (callback != null)
            {
                callback.Invoke(result);
            }

            return result;
        }

        public override void EndFlush(IAsyncResult asyncResult)
        {
            // Always completes synchronously
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            ThrowIfDisposed();
            ThrowIfCommitted();
            _buffer.Write(buffer, offset, count);
        }

        private void ThrowIfCommitted()
        {
            if (_committed)
            {
                throw new InvalidOperationException("The blob stream has already been committed once.");
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(null);
            }
        }
    }
}
