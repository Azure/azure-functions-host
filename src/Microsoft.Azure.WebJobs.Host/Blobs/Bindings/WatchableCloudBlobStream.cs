// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Bindings
{
    internal class WatchableCloudBlobStream : DelegatingCloudBlobStream, IWatcher
    {
        private readonly IBlobCommitedAction _committedAction;

        private bool _committed;
        private bool _completed;
        private long _countWritten;
        private bool _flushed;

        public WatchableCloudBlobStream(CloudBlobStream inner, IBlobCommitedAction committedAction)
            : base(inner)
        {
            _committedAction = committedAction;
        }

        public override bool CanWrite
        {
            get
            {
                // Be nicer than the SDK. Return false when calling Write would throw.
                return base.CanWrite && !_committed;
            }
        }

        public bool HasCommitted
        {
            get { return _committed; }
        }

        public override ICancellableAsyncResult BeginFlush(AsyncCallback callback, object state)
        {
            CancellationTokenSource cancellationSource = new CancellationTokenSource();
            Task baseTask = CancellableTaskFactory.FromAsync(base.BeginFlush, base.EndFlush, cancellationSource.Token);
            return new TaskCancellableAsyncResult(FlushAsyncCore(baseTask), cancellationSource, callback, state);
        }

        public override void EndFlush(IAsyncResult asyncResult)
        {
            TaskCancellableAsyncResult result = (TaskCancellableAsyncResult)asyncResult;
            result.End();
        }

        public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback,
            object state)
        {
            Task baseTask = new TaskFactory().FromAsync(base.BeginWrite, base.EndWrite, buffer, offset, count,
                state: null);
            return new TaskAsyncResult(WriteAsyncCore(baseTask, count), callback, state);
        }

        public override void EndWrite(IAsyncResult asyncResult)
        {
            TaskAsyncResult result = (TaskAsyncResult)asyncResult;
            result.End();
        }

        public override ICancellableAsyncResult BeginCommit(AsyncCallback callback, object state)
        {
            CancellationTokenSource cancellationSource = new CancellationTokenSource();
            _committed = true;
            Task baseTask = CancellableTaskFactory.FromAsync(base.BeginCommit, base.EndCommit,
                cancellationSource.Token);
            return new TaskCancellableAsyncResult(CommitAsyncCore(baseTask, cancellationSource.Token),
                cancellationSource, callback, state);
        }

        public override void EndCommit(IAsyncResult asyncResult)
        {
            TaskCancellableAsyncResult result = (TaskCancellableAsyncResult)asyncResult;
            result.End();
        }

        public override void Close()
        {
            if (!_committed)
            {
                Commit();
            }

            base.Close();
        }

        public override void Commit()
        {
            _committed = true;
            base.Commit();

            if (_committedAction != null)
            {
                _committedAction.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
        }

        public Task CommitAsync(CancellationToken cancellationToken)
        {
            _committed = true;
            Task baseTask = CancellableTaskFactory.FromAsync(base.BeginCommit, base.EndCommit, cancellationToken);
            return CommitAsyncCore(baseTask, cancellationToken);
        }

        private async Task CommitAsyncCore(Task task, CancellationToken cancellationToken)
        {
            await task;

            if (_committedAction != null)
            {
                await _committedAction.ExecuteAsync(cancellationToken);
            }
        }

        public override void Flush()
        {
            base.Flush();
            _flushed = true;
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            Task baskTask = base.FlushAsync(cancellationToken);
            return FlushAsyncCore(baskTask);
        }

        private async Task FlushAsyncCore(Task task)
        {
            await task;
            _flushed = true;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            base.Write(buffer, offset, count);
            _countWritten += count;
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Task baseTask = base.WriteAsync(buffer, offset, count, cancellationToken);
            return WriteAsyncCore(baseTask, count);
        }

        private async Task WriteAsyncCore(Task task, int count)
        {
            await task;
            _countWritten += count;
        }

        public override void WriteByte(byte value)
        {
            base.WriteByte(value);
            _countWritten++;
        }

        /// <summary>Commits the stream as appropriate (when written to or flushed) and finalizes status.</summary>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>
        /// A <see cref="Task"/> that commits the stream as appropriate and finalizes status. The result of the task is
        /// <see langword="true"/> when the stream was committed (either by this method or previously); otherwise,
        /// <see langword="false"/>
        /// </returns>
        public async Task<bool> CompleteAsync(CancellationToken cancellationToken)
        {
            if (!_committed && (_countWritten > 0 || _flushed))
            {
                await CommitAsync(cancellationToken);
            }

            _completed = true;

            return _committed;
        }

        public ParameterLog GetStatus()
        {
            if (_committed || _countWritten > 0 || _flushed)
            {
                return new WriteBlobParameterLog { WasWritten = true, BytesWritten = _countWritten };
            }
            else if (_completed)
            {
                return new WriteBlobParameterLog { WasWritten = false, BytesWritten = 0 };
            }
            else
            {
                return null;
            }
        }
    }
}
