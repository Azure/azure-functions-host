using System.IO;

namespace Microsoft.Azure.Jobs
{
    /// <summary>Defines a blob binder for a custom type.</summary>
    /// <typeparam name="T">The type of object the binder can bind.</typeparam>
    public interface ICloudBlobStreamBinder<T>
    {
        /// <summary>Binds the content of the blob to a custom type.</summary>
        /// <param name="input">The blob stream to read.</param>
        /// <returns>The deserialized object.</returns>
        T ReadFromStream(Stream input);

        /// <summary>Binds the custom type to the contents of a blob.</summary>
        /// <param name="value">The value to serialize.</param>
        /// <param name="output">The stream to which to write the value.</param>
        void WriteToStream(T value, Stream output);
    }
}
