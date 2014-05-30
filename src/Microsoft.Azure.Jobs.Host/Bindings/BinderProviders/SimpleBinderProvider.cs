using System;
using System.IO;

namespace Microsoft.Azure.Jobs
{
    // $$$ Share with implementation in Microsoft.Azure.Jobs.dll. Both are private 
    // Really... can we get rid of this completely? It's just glue for binding T to streams. 
    // seems like there ought to be some existing FX convention for that. 
    class SimpleBinderProvider<T> : ICloudBlobBinderProvider
    {
        ICloudBlobStreamBinder<T> _inner;
        public SimpleBinderProvider(ICloudBlobStreamBinder<T> inner)
        {
            _inner = inner;
        }

        class OutputBinder : ICloudBlobBinder
        {
            public ICloudBlobStreamBinder<T> _inner;

            public BindResult Bind(IBinderEx binder, string containerName, string blobName, Type targetType)
            {
                var bindStream = binder.BindWriteStream<Stream>(containerName, blobName);

                return new BindResult<T>(default(T), bindStream)
                {
                    Cleanup = newResult =>
                    {
                        if (newResult != null)
                        {
                            _inner.WriteToStream(newResult, bindStream.Result);
                        }
                    }
                };
            }
        }

        public ICloudBlobBinder TryGetBinder(Type targetType)
        {
            if (targetType == typeof(T))
            {
                return new OutputBinder { _inner = this._inner };
            }
            return null;
        }
    }
}
