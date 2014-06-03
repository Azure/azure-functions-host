using System.IO;

namespace Microsoft.Azure.Jobs.Host.Blobs
{
    internal class CloudBlobStreamObjectBinder<TValue> : ICloudBlobStreamObjectBinder
    {
        private readonly ICloudBlobStreamBinder<TValue> _innerBinder;

        public CloudBlobStreamObjectBinder(ICloudBlobStreamBinder<TValue> innerBinder)
        {
            _innerBinder = innerBinder;
        }

        public object ReadFromStream(Stream input)
        {
            return _innerBinder.ReadFromStream(input);
        }

        public void WriteToStream(Stream output, object value)
        {
            _innerBinder.WriteToStream((TValue)value, output);
        }
    }
}
