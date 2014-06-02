using System;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Blobs.Bindings
{
    internal class OutObjectArgumentBindingProvider : IBlobArgumentBindingProvider
    {
        private readonly ICloudBlobStreamObjectBinder _objectBinder;
        private readonly Type _valueType;

        public OutObjectArgumentBindingProvider(ICloudBlobStreamObjectBinder objectBinder, Type valueType)
        {
            _objectBinder = objectBinder;
            _valueType = valueType;
        }

        public IArgumentBinding<ICloudBlob> TryCreate(ParameterInfo parameter)
        {
            if (!parameter.IsOut || parameter.ParameterType.GetElementType() != _valueType)
            {
                return null;
            }

            return new ObjectArgumentBinding(_objectBinder, _valueType);
        }

        private class ObjectArgumentBinding : IArgumentBinding<ICloudBlob>
        {
            private readonly ICloudBlobStreamObjectBinder _objectBinder;
            private readonly Type _valueType;

            public ObjectArgumentBinding(ICloudBlobStreamObjectBinder objectBinder, Type valueType)
            {
                _valueType = valueType;
                _objectBinder = objectBinder;
            }

            public Type ValueType
            {
                get { return _valueType; }
            }

            public IValueProvider Bind(ICloudBlob blob, ArgumentBindingContext context)
            {
                CloudBlockBlob blockBlob = blob as CloudBlockBlob;

                if (blockBlob == null)
                {
                    throw new InvalidOperationException("Cannot bind a page blob using an ICloudBlobStreamBinder.");
                }

                CloudBlobStream rawStream = blockBlob.OpenWrite();
                IBlobCommitedAction committedAction = new BlobCommittedAction(blob, context.FunctionInstanceId,
                    context.NotifyNewBlob);
                SelfWatchCloudBlobStream selfWatchStream = new SelfWatchCloudBlobStream(rawStream, committedAction);
                return new ObjectValueBinder(blob, selfWatchStream, _objectBinder, _valueType);
            }

            // There's no way to dispose a CloudBlobStream without committing.
            // This class intentionally does not implement IDisposable because there's nothing it can do in Dispose.
            private sealed class ObjectValueBinder : IValueBinder, IWatchable
            {
                private readonly ICloudBlob _blob;
                private readonly SelfWatchCloudBlobStream _stream;
                private readonly ICloudBlobStreamObjectBinder _objectBinder;
                private readonly Type _valueType;

                public ObjectValueBinder(ICloudBlob blob, SelfWatchCloudBlobStream stream,
                    ICloudBlobStreamObjectBinder objectBinder, Type valueType)
                {
                    _blob = blob;
                    _stream = stream;
                    _objectBinder = objectBinder;
                    _valueType = valueType;
                }

                public Type Type
                {
                    get { return _valueType; }
                }

                public ISelfWatch Watcher
                {
                    get { return _stream; }
                }

                public object GetValue()
                {
                    return null;
                }

                public void SetValue(object value)
                {
                    _objectBinder.WriteToStream(_stream, value);
                    _stream.Commit();
                    _stream.Dispose();
                }

                public string ToInvokeString()
                {
                    return _blob.GetBlobPath();
                }
            }
        }
    }
}
