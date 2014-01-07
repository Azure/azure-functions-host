using System.IO;

namespace Microsoft.WindowsAzure.Jobs
{
    // interface for easily binding custom types to streams
    public interface ICloudBlobStreamBinder<T>
    {
        T ReadFromStream(Stream input);
        void WriteToStream(T result, Stream output);
    }
}
