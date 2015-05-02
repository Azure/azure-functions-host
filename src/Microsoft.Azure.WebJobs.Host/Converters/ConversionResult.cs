// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Host.Converters
{
    /// <summary>
    /// Represents the result of a conversion.
    /// </summary>
    /// <typeparam name="TResult">The <see cref="System.Type"/> of the conversion result.</typeparam>
    public struct ConversionResult<TResult>
    {
        /// <summary>
        /// Gets a value indicating whether the conversion succeeded.
        /// </summary>
        public bool Succeeded;

        /// <summary>
        /// Gets the conversion result.
        /// </summary>
        public TResult Result;
    }
}
