using System.IO;

namespace Microsoft.Azure.Jobs.Host.Blobs
{
    internal interface ICloudBlobStreamObjectBinder
    {
        object ReadFromStream(Stream input);

        void WriteToStream(Stream output, object value);
    }
}
