// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Microsoft.Azure.WebJobs.Host.Timers;

namespace Microsoft.Azure.WebJobs.Host.Loggers
{
    // Flush on a timer so that we get updated output.
    // Flush will come on a different thread, so we need to have thread-safe
    // access between the Reader (ToString)  and the Writers (which are happening as our
    // caller uses the textWriter that we return).
    internal sealed class UpdateOutputLogCommand : IRecurrentCommand, IDisposable, IFunctionOutput
    {
        // Contents for what's written. Owned by the timer thread.
        private readonly StringWriter _innerWriter;
        private readonly IStorageBlockBlob _outputBlob;
        private readonly Func<string, CancellationToken, Task> _uploadCommand;

        // Thread-safe access to _innerWriter so that user threads can write to it. 
        private readonly TextWriter _synchronizedWriter;
        private object _writerSyncLock = new object();
        private bool _disposed;
        private string _existingContent = null;

        private UpdateOutputLogCommand(IStorageBlockBlob outputBlob, Func<string, CancellationToken, Task> uploadCommand)
        {
            _outputBlob = outputBlob;
            _innerWriter = new StringWriter(CultureInfo.InvariantCulture);
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

        public static UpdateOutputLogCommand Create(IStorageBlockBlob outputBlob)
        {
            return Create(outputBlob, (contents, innerToken) => UploadTextAsync(outputBlob, contents, innerToken));
        }

        public static UpdateOutputLogCommand Create(IStorageBlockBlob outputBlob, Func<string, CancellationToken, Task> uploadCommand)
        {
            if (outputBlob == null)
            {
                throw new ArgumentNullException("outputBlob");
            }
            else if (uploadCommand == null)
            {
                throw new ArgumentNullException("uploadCommand");
            }

            return new UpdateOutputLogCommand(outputBlob, uploadCommand);
        }

        public async Task<bool> TryExecuteAsync(CancellationToken cancellationToken)
        {
            await UpdateOutputBlob(cancellationToken);
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

        public async Task SaveAndCloseAsync(FunctionInstanceLogEntry item, CancellationToken cancellationToken)
        {
            await UpdateOutputBlob(cancellationToken, flushAndClose: true);
        }

        private async Task<string> GetExistingContent(CancellationToken cancellationToken)
        {
            if (_existingContent == null)
            {
                _existingContent = await ReadBlobAsync(_outputBlob, cancellationToken);
                if (_existingContent != null)
                {
                    // This can happen if the function was running previously and the 
                    // node crashed. Save previous output, could be useful for diagnostics.
                    StringWriter stringWriter = new StringWriter();
                    stringWriter.WriteLine("Previous execution information:");
                    stringWriter.WriteLine(_existingContent);

                    var lastTime = await GetBlobModifiedUtcTimeAsync(_outputBlob, cancellationToken);
                    if (lastTime.HasValue)
                    {
                        var delta = DateTime.UtcNow - lastTime.Value;
                        stringWriter.WriteLine("... Last write at {0}, {1} ago", lastTime, delta);
                    }

                    stringWriter.WriteLine("========================");
                }
                else
                {
                    _existingContent = string.Empty;
                }
            }

            return _existingContent;
        }

        private async Task UpdateOutputBlob(CancellationToken cancellationToken, bool flushAndClose = false)
        {
            ThrowIfDisposed();

            string snapshot;
            lock (_writerSyncLock)
            {
                if (flushAndClose)
                {
                    _synchronizedWriter.Flush();
                }

                // Explicitly specify the length. Without this, any writes occurring 
                // during the call to ToString() could throw.
                StringBuilder innerBuilder = _innerWriter.GetStringBuilder();
                snapshot = innerBuilder.ToString(0, innerBuilder.Length);

                if (flushAndClose)
                {
                    _synchronizedWriter.Close();
                    _innerWriter.Close();
                }
            }

            // when we write the output blob, ensure that we always include
            // any preexisting contents
            string existingText = await GetExistingContent(cancellationToken);
            StringBuilder sb = new StringBuilder(existingText);
            sb.Append(snapshot);
            snapshot = sb.ToString();

            await _uploadCommand.Invoke(snapshot, cancellationToken);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(null);
            }
        }

        private static Task UploadTextAsync(IStorageBlockBlob outputBlob, string contents, CancellationToken cancellationToken)
        {
            return outputBlob.UploadTextAsync(contents, cancellationToken: cancellationToken);
        }

        private static async Task<DateTime?> GetBlobModifiedUtcTimeAsync(IStorageBlob blob, CancellationToken cancellationToken)
        {
            if (!await blob.ExistsAsync(cancellationToken))
            {
                return null; // no blob, no time.
            }

            var lastModified = blob.Properties.LastModified;
            return lastModified.HasValue ? (DateTime?)lastModified.Value.UtcDateTime : null;
        }

        [DebuggerNonUserCode]
        private static async Task<string> ReadBlobAsync(IStorageBlob blob, CancellationToken cancellationToken)
        {
            try
            {
                // Beware! Blob.DownloadText does not strip the BOM!
                using (var stream = await blob.OpenReadAsync(cancellationToken))
                using (StreamReader sr = new StreamReader(stream, detectEncodingFromByteOrderMarks: true))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string data = await sr.ReadToEndAsync();
                    return data;
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
