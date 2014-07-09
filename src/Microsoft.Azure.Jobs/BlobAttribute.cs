using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace Microsoft.Azure.Jobs
{
    /// <summary>Represents an attribute that binds a parameter to an Azure Blob.</summary>
    /// <remarks>
    /// The method parameter type can be one of the following:
    /// <list type="bullet">
    /// <item><description>ICloudBlob</description></item>
    /// <item><description>CloudBlockBlob</description></item>
    /// <item><description>CloudPageBlob</description></item>
    /// <item><description><see cref="Stream"/> (read-only)</description></item>
    /// <item><description>CloudBlobStream (write-only)</description></item>
    /// <item><description><see cref="TextReader"/></description></item>
    /// <item><description><see cref="TextWriter"/></description></item>
    /// <item><description>
    /// <see cref="string"/> (normally for reading, or as an out param for writing)
    /// </description></item>
    /// <item><description>
    /// A custom type implementing <see cref="ICloudBlobStreamBinder{T}"/> (normally for reading, or as an out param for
    /// writing)
    /// </description></item>
    /// </list>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Parameter)]
    [DebuggerDisplay("{BlobPath,nq}")]
    public sealed class BlobAttribute : Attribute
    {
        private readonly string _blobPath;
        private readonly FileAccess? _access;

        /// <summary>Initializes a new instance of the <see cref="BlobAttribute"/> class.</summary>
        /// <param name="blobPath">The path of the blob to which to bind.</param>
        public BlobAttribute(string blobPath)
        {
            _blobPath = blobPath;
        }

        /// <summary>Initializes a new instance of the <see cref="BlobAttribute"/> class.</summary>
        /// <param name="blobPath">The path of the blob to which to bind.</param>
        /// <param name="access">The kind of operations that can be performed on the blob.</param>
        public BlobAttribute(string blobPath, FileAccess access)
        {
            _blobPath = blobPath;
            _access = access;
        }

        /// <summary>Gets the path of the blob to which to bind.</summary>
        public string BlobPath
        {
            get { return _blobPath; }
        }

        /// <summary>Gets the kind of operations that can be performed on the blob.</summary>
        public FileAccess? Access
        {
            get { return _access; }
        }

        // IBinder's watcher uses an attribute's ToString as a key.
        /// <inheritdoc />
        public override string ToString()
        {
            return String.Format(CultureInfo.InvariantCulture, "[Blob(\"{0}\")]", _blobPath);
        }
    }
}
