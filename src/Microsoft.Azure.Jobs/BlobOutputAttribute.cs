using System;
using System.Globalization;

namespace Microsoft.Azure.Jobs
{
    /// <summary>
    /// Represents an attribute that is used to provide details about how a Microsoft Azure Blob is
    /// bound as a method input parameter.
    /// The method parameter type by default can be a Stream, TextWriter, string (declared as "out"), CloudBlob, ICloudBlob, CloudPageBlob, or CloudBlockBlob.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter)]
    public class BlobOutputAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of the BlobOutputAttribute class.
        /// </summary>
        /// <param name="blobPath">Path of the blob to bind to. The blob portion of the path can contain
        /// tokens in curly braces to indicate a named parameter from an input attribute to substitute.</param>
        public BlobOutputAttribute(string blobPath)
        {
            BlobPath = blobPath;
        }

        /// <summary>
        /// Gets or sets the path of the blob to bind to. The blob portion of the path can contain
        /// tokens in curly braces to indicate a named parameter from an input attribute to substitute.
        /// </summary>
        public string BlobPath { get; private set; }

        /// <inheritdoc />
        public override string ToString()
        {
            return String.Format(CultureInfo.InvariantCulture, "[BlobOutput({0})]", BlobPath);
        }
    }
}
