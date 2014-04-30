using System.IO;

namespace Microsoft.Azure.Jobs
{
    // interface for easily binding custom types to streams
    /// <summary>
    /// Defines a custom blob binder.
    /// </summary>
    /// <typeparam name="T">The type of object the binder can bind.</typeparam>
    public interface ICloudBlobStreamBinder<T>
    {
        /// <summary>
        /// Binds the content of the blob to a custom type.
        /// </summary>
        /// <param name="input">The stream attached to the blob.</param>
        /// <returns>The deserialized object.</returns>
        T ReadFromStream(Stream input);

        /// <summary>
        /// Binds the custom type to the content of the blob.
        /// </summary>
        /// <param name="value">The value to serialize.</param>
        /// <param name="output">The stream to which to write the value.</param>
        void WriteToStream(T value, Stream output);
    }
}
