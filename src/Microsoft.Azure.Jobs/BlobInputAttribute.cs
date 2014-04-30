using System;
using System.IO;
using System.Globalization;

namespace Microsoft.Azure.Jobs
{
    // $$$ Only read these once:
    // - orchestration, for knowing what to listen on and when to run it
    // - invoker, for knowing how to bind the parameters
    /// <summary>
    /// Represents an attribute that is used to provide details about how a Microsoft Azure Blob is
    /// bound as a method input parameter.
    /// This attribute also serves as a trigger that will run the Job function when a new blob is uploaded.
    /// The method parameter type can be a <see cref="String" />, <see cref="Stream" />, <see cref="TextReader" />,
    /// CloudBlob, ICloudBlob, CloudPageBlob, CloudBlockBlob,
    /// or a custom type implementing <see cref="ICloudBlobStreamBinder{T}" />.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter)]
    public class BlobInputAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of the BlobInputAttribute class.
        /// </summary>
        /// <param name="blobPath">Path of the blob to bind to. The blob portion of the path can contain
        /// tokens in curly braces to indicate a pattern to match. The matched name can be used in other
        /// binding attributes to define the output name of a Job function.</param>
        public BlobInputAttribute(string blobPath)
        {
            BlobPath = blobPath;
        }

        /// <summary>
        /// Gets or sets the path of the blob to bind to. The blob portion of the path can contain
        /// tokens in curly braces to indicate a pattern to match. The matched name can be used in other
        /// binding attributes to define the output name of a Job function.
        /// </summary>
        public string BlobPath { get; private set; }

        /// <inheritdoc />
        public override string ToString()
        {
            return String.Format(CultureInfo.InvariantCulture, "[BlobInput({0})]", BlobPath);
        }
    }
}
