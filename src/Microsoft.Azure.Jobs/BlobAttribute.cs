using System;
using System.Diagnostics;
using System.IO;

namespace Microsoft.Azure.Jobs
{
    /// <summary>Represents an attribute that binds a parameter to an Azure Blob.</summary>
    /// <remarks>
    /// The method parameter type can one of the following:
    /// <list type="bullet">
    /// <item><description>ICloudBlob</description></item>
    /// <item><description>CloudBlockBlob</description></item>
    /// <item><description>CloudPageBlob</description></item>
    /// <item><description><see cref="Stream"/></description></item>
    /// <item><description><see cref="TextReader"/></description></item>
    /// <item><description><see cref="string"/></description></item>
    /// <item><description>A custom type implementing <see cref="ICloudBlobStreamBinder{T}"/></description></item>
    /// </list>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Parameter)]
    [DebuggerDisplay("{BlobPath,nq}")]
    public sealed class BlobAttribute : Attribute
    {
        private readonly string _blobPath;

        /// <summary>Initializes a new instance of the <see cref="BlobAttribute"/> class.</summary>
        /// <param name="blobPath">The path of the blob to which to bind.</param>
        public BlobAttribute(string blobPath)
        {
            _blobPath = blobPath;
        }

        /// <summary>Gets the path of the blob to which to bind.</summary>
        public string BlobPath
        {
            get { return _blobPath; }
        }
    }
}
