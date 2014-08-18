// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Timers;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Loggers
{
    // Flush on a timer so that we get updated output.
    // Flush will come on a different thread, so we need to have thread-safe
    // access between the Reader (ToString)  and the Writers (which are happening as our
    // caller uses the textWriter that we return).
    internal sealed class UpdateOutputLogCommand : IRecurrentCommand, IDisposable, IFunctionOutput
    {
        private readonly CloudBlockBlob _outputBlob;

        // Contents for what's written. Owned by the timer thread.
        private readonly StringWriter _innerWriter;

        // Thread-safe access to _innerWriter so that user threads can write to it. 
        private readonly TextWriter _synchronizedWriter;

        private readonly Func<string, CancellationToken, Task> _uploadCommand;

        private bool _disposed;

        private UpdateOutputLogCommand(CloudBlockBlob outputBlob, StringWriter innerWriter,
            Func<string, CancellationToken, Task> uploadCommand)
        {
            _outputBlob = outputBlob;
            _innerWriter = innerWriter;
            _synchronizedWriter = TextWriter.Synchronized(_innerWriter);
            _uploadCommand = uploadCommand;
        }

        public IRecurrentCommand UpdateCommand
        {
            get
            {
                ThrowIfDisposed();
                return this;
            }
        }

        public TextWriter Output
        {
            get
            {
                ThrowIfDisposed();
                return _synchronizedWriter;
            }
        }

        public static Task<UpdateOutputLogCommand> CreateAsync(CloudBlockBlob outputBlob, string existingContents,
            CancellationToken cancellationToken)
        {
            return CreateAsync(outputBlob, existingContents, (contents, innerToken) => UploadTextAsync(
                outputBlob, contents, innerToken), cancellationToken);
        }

        public static async Task<UpdateOutputLogCommand> CreateAsync(CloudBlockBlob outputBlob, string existingContents,
            Func<string, CancellationToken, Task> uploadCommand, CancellationToken cancellationToken)
        {
            if (outputBlob == null)
            {
                throw new ArgumentNullException("outputBlob");
            }
            else if (uploadCommand == null)
            {
                throw new ArgumentNullException("uploadCommand");
            }

            StringWriter innerWriter = new StringWriter();

            if (existingContents != null)
            {
                // This can happen if the function was running previously and the 
                // node crashed. Save previous output, could be useful for diagnostics.
                innerWriter.WriteLine("Previous execution information:");
                innerWriter.WriteLine(existingContents);

                var lastTime = await GetBlobModifiedUtcTimeAsync(outputBlob, cancellationToken);
                if (lastTime.HasValue)
                {
                    var delta = DateTime.UtcNow - lastTime.Value;
                    innerWriter.WriteLine("... Last write at {0}, {1} ago", lastTime, delta);
                }

                innerWriter.WriteLine("========================");
            }

            return new UpdateOutputLogCommand(outputBlob, innerWriter, uploadCommand);
        }

        public async Task<bool> TryExecuteAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();

            // For synchronized text writer, the object is its own lock.
            string snapshot;

            lock (_synchronizedWriter)
            {
                snapshot = _innerWriter.ToString();
            }

            await _uploadCommand.Invoke(snapshot, cancellationToken);
            return true;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _innerWriter.Dispose();
                _synchronizedWriter.Dispose();
                _disposed = true;
            }
        }

        public Task SaveAndCloseAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();

            string finalSnapshot;

            lock (_synchronizedWriter)
            {
                _synchronizedWriter.Flush();
                finalSnapshot = _innerWriter.ToString();
                _synchronizedWriter.Close();
                _innerWriter.Close();
            }

            return _uploadCommand.Invoke(finalSnapshot, cancellationToken);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(null);
            }
        }

        private static Task UploadTextAsync(CloudBlockBlob outputBlob, string contents,
            CancellationToken cancellationToken)
        {
            return outputBlob.UploadTextAsync(contents, cancellationToken);
        }

        private static async Task<DateTime?> GetBlobModifiedUtcTimeAsync(ICloudBlob blob,
            CancellationToken cancellaitonToken)
        {
            if (!await blob.ExistsAsync(cancellaitonToken))
            {
                return null; // no blob, no time.
            }

            var props = blob.Properties;
            var time = props.LastModified;
            return time.HasValue ? (DateTime?)time.Value.UtcDateTime : null;
        }
    }
}
