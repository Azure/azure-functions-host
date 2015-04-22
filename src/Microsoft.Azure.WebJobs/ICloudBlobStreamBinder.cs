// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs
{
    /// <summary>Defines a blob binder for a custom type.</summary>
    /// <typeparam name="T">The type of object the binder can bind.</typeparam>
    public interface ICloudBlobStreamBinder<T>
    {
        /// <summary>Binds the content of the blob to a custom type.</summary>
        /// <param name="input">The blob stream to read.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>A <see cref="Task"/> that will provide the deserialized object.</returns>
        Task<T> ReadFromStreamAsync(Stream input, CancellationToken cancellationToken);

        /// <summary>Binds the custom type to the contents of a blob.</summary>
        /// <param name="value">The value to serialize.</param>
        /// <param name="output">The stream to which to write the value.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>A <see cref="Task"/> that will write to the stream.</returns>
        Task WriteToStreamAsync(T value, Stream output, CancellationToken cancellationToken);
    }
}
