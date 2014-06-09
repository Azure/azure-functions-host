using System;
using System.IO;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Blobs.Bindings;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Blobs
{
    internal class StreamArgumentBindingProvider : IBlobArgumentBindingProvider
    {
        public IBlobArgumentBinding TryCreate(ParameterInfo parameter, FileAccess? access)
        {
            if (parameter.ParameterType != typeof(Stream))
            {
                return null;
            }

            if (access.HasValue && access.Value == FileAccess.ReadWrite)
            {
                throw new InvalidOperationException("Cannot bind blob to Stream using access ReadWrite.");
            }

            if (!access.HasValue || access.Value == FileAccess.Read)
            {
                return new ReadStreamArgumentBinding();
            }
            else
            {
                return new WriteStreamArgumentBinding();
            }
        }

        private class ReadStreamArgumentBinding : IBlobArgumentBinding
        {
            public FileAccess Access
            {
                get { return FileAccess.Read; }
            }

            public Type ValueType
            {
                get { return typeof(Stream); }
            }

            public IValueProvider Bind(ICloudBlob blob, ArgumentBindingContext context)
            {
                Stream rawStream = blob.OpenRead();
                using (SelfWatchReadStream selfWatchStream = new SelfWatchReadStream(rawStream))
                return new BlobWatchableDisposableValueProvider(blob, selfWatchStream, typeof(Stream),
                    watcher: selfWatchStream, disposable: selfWatchStream);
            }
        }

        private class WriteStreamArgumentBinding : IBlobArgumentBinding
        {
            public FileAccess Access
            {
                get { return FileAccess.Write; }
            }

            public Type ValueType
            {
                get { return typeof(Stream); }
            }

            public IValueProvider Bind(ICloudBlob blob, ArgumentBindingContext context)
            {
                CloudBlockBlob blockBlob = blob as CloudBlockBlob;

                if (blockBlob == null)
                {
                    throw new InvalidOperationException("Cannot bind a page blob using a Stream.");
                }

                CloudBlobStream rawStream = blockBlob.OpenWrite();
                IBlobCommitedAction committedAction = new BlobCommittedAction(blob, context.FunctionInstanceId,
                    context.NotifyNewBlob);
                SelfWatchCloudBlobStream selfWatchStream = new SelfWatchCloudBlobStream(rawStream, committedAction);
                return new WriteStreamValueBinder(blob, selfWatchStream);
            }

            // There's no way to dispose a CloudBlobStream without committing.
            // This class intentionally does not implement IDisposable because there's nothing it can do in Dispose.
            private sealed class WriteStreamValueBinder : IValueBinder, IWatchable
            {
                private readonly ICloudBlob _blob;
                private readonly SelfWatchCloudBlobStream _stream;

                public WriteStreamValueBinder(ICloudBlob blob, SelfWatchCloudBlobStream stream)
                {
                    _blob = blob;
                    _stream = stream;
                }

                public Type Type
                {
                    get { return typeof(Stream); }
                }

                public ISelfWatch Watcher
                {
                    get { return _stream; }
                }

                public object GetValue()
                {
                    return _stream;
                }

                public void SetValue(object value)
                {
                    // Not ByRef, so can ignore value argument.

                    // Determine whether or not to upload the blob.
                    if (_stream.Complete())
                    {
                        _stream.Dispose(); // Can only dispose when committing; see note on class above.
                    }
                }

                public string ToInvokeString()
                {
                    return _blob.GetBlobPath();
                }
            }
        }
    }
}
