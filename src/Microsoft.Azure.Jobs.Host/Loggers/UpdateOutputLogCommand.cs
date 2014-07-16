// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Azure.Jobs.Host.Timers;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Loggers
{
    // Flush on a timer so that we get updated output.
    // Flush will come on a different thread, so we need to have thread-safe
    // access between the Reader (ToString)  and the Writers (which are happening as our
    // caller uses the textWriter that we return).
    internal sealed class UpdateOutputLogCommand : ICanFailCommand, IDisposable, IFunctionOutput
    {
        private readonly CloudBlockBlob _outputBlob;

        // Contents for what's written. Owned by the timer thread.
        private readonly StringWriter _innerWriter;

        // Thread-safe access to _innerWriter so that user threads can write to it. 
        private readonly TextWriter _synchronizedWriter;

        private readonly Action<string> _uploadCommand;

        private bool _disposed;

        public UpdateOutputLogCommand(CloudBlockBlob outputBlob, string existingContents)
            : this(outputBlob, existingContents, (contents) => UploadText(outputBlob, contents))
        {
        }

        public UpdateOutputLogCommand(CloudBlockBlob outputBlob, string existingContents, Action<string> uploadCommand)
        {
            if (outputBlob == null)
            {
                throw new ArgumentNullException("outputBlob");
            }
            else if (uploadCommand == null)
            {
                throw new ArgumentNullException("_uploadCommand");
            }

            _outputBlob = outputBlob;
            _uploadCommand = uploadCommand;
            _innerWriter = new StringWriter();
            _synchronizedWriter = TextWriter.Synchronized(_innerWriter);

            if (existingContents != null)
            {
                // This can happen if the function was running previously and the 
                // node crashed. Save previous output, could be useful for diagnostics.
                _innerWriter.WriteLine("Previous execution information:");
                _innerWriter.WriteLine(existingContents);

                var lastTime = GetBlobModifiedUtcTime(outputBlob);
                if (lastTime.HasValue)
                {
                    var delta = DateTime.UtcNow - lastTime.Value;
                    _innerWriter.WriteLine("... Last write at {0}, {1} ago", lastTime, delta);
                }

                _innerWriter.WriteLine("========================");
            }
        }

        public ICanFailCommand UpdateCommand
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

        public bool TryExecute()
        {
            ThrowIfDisposed();

            // For synchronized text writer, the object is its own lock.
            lock (_synchronizedWriter)
            {
                _uploadCommand.Invoke(_innerWriter.ToString());
                return true;
            }
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

        public void SaveAndClose()
        {
            ThrowIfDisposed();

            lock (_synchronizedWriter)
            {
                _synchronizedWriter.Flush();
                _uploadCommand.Invoke(_innerWriter.ToString());
                _synchronizedWriter.Close();
                _innerWriter.Close();
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(null);
            }
        }

        private static void UploadText(CloudBlockBlob outputBlob, string contents)
        {
            outputBlob.UploadText(contents);
        }

        private static DateTime? GetBlobModifiedUtcTime(ICloudBlob blob)
        {
            if (!blob.Exists())
            {
                return null; // no blob, no time.
            }

            var props = blob.Properties;
            var time = props.LastModified;
            return time.HasValue ? (DateTime?)time.Value.UtcDateTime : null;
        }
    }
}
