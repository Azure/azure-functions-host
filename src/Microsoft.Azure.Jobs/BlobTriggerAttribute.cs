// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;

namespace Microsoft.Azure.WebJobs
{
    /// <summary>
    /// Represents an attribute that binds a parameter to an Azure Blob, causing the method to run when a blob is
    /// uploaded.
    /// </summary>
    /// <remarks>
    /// The method parameter type can be one of the following:
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
    public sealed class BlobTriggerAttribute : Attribute
    {
        private readonly string _blobPath;

        /// <summary>
        /// Initializes a new instance of the <see cref="BlobTriggerAttribute"/> class.
        /// </summary>
        /// <param name="blobPath">The path of the blob to which to bind.</param>
        /// <remarks>
        /// The blob portion of the path can contain tokens in curly braces to indicate a pattern to match. The matched
        /// name can be used in other binding attributes to define the output name of a Job function.
        /// </remarks>
        public BlobTriggerAttribute(string blobPath)
        {
            _blobPath = blobPath;
        }

        /// <summary>Gets the path of the blob to which to bind.</summary>
        /// <remarks>
        /// The blob portion of the path can contain tokens in curly braces to indicate a pattern to match. The matched
        /// name can be used in other binding attributes to define the output name of a Job function.
        /// </remarks>
        public string BlobPath
        {
            get { return _blobPath; }
        }
    }
}
