using System.IO;

namespace Microsoft.Azure.Jobs.Host.Blobs
{
    internal class CloudBlobStreamObjectBinder<T> : ICloudBlobStreamObjectBinder
    {
        private readonly ICloudBlobStreamBinder<T> _innerBinder;

        public CloudBlobStreamObjectBinder(ICloudBlobStreamBinder<T> innerBinder)
        {
            _innerBinder = innerBinder;
        }

        public object ReadFromStream(Stream input)
        {
            return _innerBinder.ReadFromStream(input);
        }

        public void WriteToStream(Stream output, object value)
        {
            _innerBinder.WriteToStream((T)value, output);
        }
    }
}
